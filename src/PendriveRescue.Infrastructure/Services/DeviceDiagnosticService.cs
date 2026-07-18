using System.Security.Cryptography;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;
using Serilog;

namespace PendriveRescue.Infrastructure.Services;

/// <summary>
/// Coordinates read-only diagnostic collectors. It never formats, repairs, assigns letters, runs CHKDSK, or writes to the USB.
/// </summary>
public sealed class DeviceDiagnosticService : IDeviceDiagnosticService
{
    private readonly IStorageDeviceOperationGuard _operationGuard;
    private readonly IDeviceDiagnosticEvidenceCollector _evidenceCollector;
    private readonly IDeviceReadProbe _readProbe;
    private readonly IDeviceSecurityEvidenceProvider _securityEvidenceProvider;
    private readonly IDeviceDiagnosticEngine _diagnosticEngine;
    private readonly TimeProvider _timeProvider;

    public DeviceDiagnosticService(
        IStorageDeviceOperationGuard operationGuard,
        IDeviceDiagnosticEvidenceCollector evidenceCollector,
        IDeviceReadProbe readProbe,
        IDeviceSecurityEvidenceProvider securityEvidenceProvider,
        IDeviceDiagnosticEngine diagnosticEngine,
        TimeProvider timeProvider)
    {
        _operationGuard = operationGuard;
        _evidenceCollector = evidenceCollector;
        _readProbe = readProbe;
        _securityEvidenceProvider = securityEvidenceProvider;
        _diagnosticEngine = diagnosticEngine;
        _timeProvider = timeProvider;
    }

    public async Task<DeviceDiagnosticResult> AnalyzeAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<DeviceAnalysisProgress> progress)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(progress);
        var analysisId = Guid.NewGuid();
        var startedAt = _timeProvider.GetUtcNow();
        var fingerprint = Fingerprint(device.Identity);
        Log.Information(
            "DeviceAnalysisStarted AnalysisId={AnalysisId} DeviceFingerprint={DeviceFingerprint}",
            analysisId,
            fingerprint);

        Report(progress, DeviceAnalysisStage.RevalidatingDevice, 5, "Verifying physical USB identity...");
        ValidatedStorageDevice validated;
        try
        {
            validated = await _operationGuard.RevalidateAsync(
                device,
                StorageOperationKind.Diagnostic,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            var failedEvidence = BuildInitialValidationFailure(device, ex);
            Log.Warning(
                "DeviceAnalysisIncomplete AnalysisId={AnalysisId} DeviceFingerprint={DeviceFingerprint} Reason={Reason}",
                analysisId,
                fingerprint,
                SafeIdentityReason(ex));
            return Complete(analysisId, startedAt, failedEvidence, progress);
        }

        Log.Information(
            "DeviceIdentityRevalidated AnalysisId={AnalysisId} DeviceFingerprint={DeviceFingerprint}",
            analysisId,
            fingerprint);

        Report(progress, DeviceAnalysisStage.ReadingDiskInformation, 15, "Reading disk information...");
        Report(progress, DeviceAnalysisStage.ReadingPartitionInformation, 25, "Inspecting partitions...");
        Report(progress, DeviceAnalysisStage.ReadingVolumeInformation, 35, "Reading volume information...");
        Report(progress, DeviceAnalysisStage.CheckingAccessibility, 45, "Checking filesystem accessibility...");
        DeviceDiagnosticEvidence evidence;
        try
        {
            evidence = await _evidenceCollector.CollectAsync(validated, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            evidence = BuildCollectorFailure(validated);
        }

        if (CanRunReadProbe(evidence))
        {
            Report(progress, DeviceAnalysisStage.RunningReadProbe, 55, "Testing read stability...");
            var probe = await CollectReadProbeAsync(validated, cancellationToken);
            evidence = Merge(evidence, probe);
        }
        else
        {
            evidence = evidence with
            {
                ReadProbeAttempted = EvidenceState.No,
                ReadProbeFailureCategory = DiagnosticFailureCategory.EvidenceUnavailable,
                CollectionWarnings = Append(
                    evidence.CollectionWarnings,
                    "The bounded read probe was skipped because the physical device was unavailable, offline, or reported no media.")
            };
        }

        Report(progress, DeviceAnalysisStage.CheckingSecurityIndicators, 72, "Checking USB security indicators...");
        var security = await CollectSecurityEvidenceAsync(validated, cancellationToken);
        evidence = Merge(evidence, security);

        Report(progress, DeviceAnalysisStage.RevalidatingIdentity, 84, "Rechecking physical USB identity...");
        try
        {
            var finalValidation = await _operationGuard.RevalidateAsync(
                validated.Device,
                StorageOperationKind.Diagnostic,
                cancellationToken);
            evidence = MergeFinalValidation(evidence, finalValidation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            evidence = BuildFinalValidationFailure(validated, ex);
            Log.Warning(
                "DeviceIdentityChangedDuringAnalysis AnalysisId={AnalysisId} DeviceFingerprint={DeviceFingerprint} Reason={Reason}",
                analysisId,
                fingerprint,
                SafeIdentityReason(ex));
        }

        Log.Information(
            "DiagnosticEvidenceCollected AnalysisId={AnalysisId} DeviceFingerprint={DeviceFingerprint} WarningCount={WarningCount}",
            analysisId,
            fingerprint,
            evidence.CollectionWarnings.Count);
        return Complete(analysisId, startedAt, evidence, progress);
    }

    private DeviceDiagnosticResult Complete(
        Guid analysisId,
        DateTimeOffset startedAt,
        DeviceDiagnosticEvidence evidence,
        IProgress<DeviceAnalysisProgress> progress)
    {
        Report(progress, DeviceAnalysisStage.EvaluatingEvidence, 92, "Evaluating diagnostic evidence...");
        var result = _diagnosticEngine.Evaluate(
            analysisId,
            startedAt,
            _timeProvider.GetUtcNow(),
            evidence);
        Report(progress, DeviceAnalysisStage.Completed, 100, "Analysis completed.");
        Log.Information(
            "DeviceAnalysisCompleted AnalysisId={AnalysisId} Condition={Condition} Confidence={Confidence} Severity={Severity} Complete={Complete}",
            analysisId,
            result.PrimaryCondition,
            result.Confidence,
            result.Severity,
            result.AnalysisComplete);
        return result;
    }

    private async Task<DeviceReadProbeResult> CollectReadProbeAsync(
        ValidatedStorageDevice device,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _readProbe.ProbeAsync(
                device,
                new DeviceReadProbeOptions(),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new DeviceReadProbeResult
            {
                Attempted = true,
                BytesRequested = DeviceReadProbeOptions.DefaultBytesToRead,
                FailureCategory = DiagnosticFailureCategory.AccessDenied
            };
        }
        catch
        {
            return new DeviceReadProbeResult
            {
                Attempted = true,
                BytesRequested = DeviceReadProbeOptions.DefaultBytesToRead,
                IoErrorCount = 1,
                FailureCategory = DiagnosticFailureCategory.IoFailure
            };
        }
    }

    private async Task<DeviceSecurityEvidence> CollectSecurityEvidenceAsync(
        ValidatedStorageDevice device,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _securityEvidenceProvider.CollectAsync(device, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new DeviceSecurityEvidence
            {
                Warnings = ["USB security metadata could not be inspected (EvidenceUnavailable)."]
            };
        }
    }

    private static DeviceDiagnosticEvidence Merge(
        DeviceDiagnosticEvidence evidence,
        DeviceReadProbeResult probe)
    {
        var isReadFailure = probe.FailureCategory is DiagnosticFailureCategory.DeviceNotReady
            or DiagnosticFailureCategory.DeviceRemoved
            or DiagnosticFailureCategory.IoFailure
            or DiagnosticFailureCategory.Timeout;
        var warnings = probe.FailureCategory switch
        {
            DiagnosticFailureCategory.AccessDenied => Append(
                evidence.CollectionWarnings,
                "The bounded physical read probe was unavailable because Windows denied access (AccessDenied)."),
            DiagnosticFailureCategory.EvidenceUnavailable => Append(
                evidence.CollectionWarnings,
                "The bounded physical read probe was unavailable (EvidenceUnavailable)."),
            _ => evidence.CollectionWarnings
        };

        return evidence with
        {
            ReadProbeAttempted = probe.Attempted ? EvidenceState.Yes : EvidenceState.No,
            ReadProbeSucceeded = probe.Attempted ? ToState(probe.Success) : EvidenceState.Unknown,
            ReadProbeBytesRequested = probe.BytesRequested,
            ReadProbeBytesCompleted = probe.BytesRead,
            ReadProbeDuration = probe.Attempted ? probe.Duration : null,
            ReadErrorCount = probe.Success ? 0 : isReadFailure ? 1 : 0,
            IoErrorCount = probe.IoErrorCount,
            TimedOut = probe.Attempted ? ToState(probe.TimedOut) : EvidenceState.Unknown,
            ReadProbeFailureCategory = probe.FailureCategory,
            DeviceDisconnectedDuringAnalysis = probe.DeviceRemoved ? EvidenceState.Yes : evidence.DeviceDisconnectedDuringAnalysis,
            CollectionWarnings = warnings
        };
    }

    private static DeviceDiagnosticEvidence Merge(
        DeviceDiagnosticEvidence evidence,
        DeviceSecurityEvidence security)
    {
        return evidence with
        {
            SecurityEvidenceCollected = security.Collected,
            SuspiciousAutorunDetected = security.SuspiciousAutorunDetected,
            SuspiciousShortcutPatternDetected = security.SuspiciousShortcutPatternDetected,
            SuspiciousLauncherCount = security.SuspiciousLauncherCount,
            DefenderAvailable = security.DefenderAvailable,
            DefenderThreatRequiresAction = security.DefenderThreatRequiresAction,
            CollectionWarnings = Append(evidence.CollectionWarnings, security.Warnings)
        };
    }

    private static DeviceDiagnosticEvidence MergeFinalValidation(
        DeviceDiagnosticEvidence evidence,
        ValidatedStorageDevice finalValidation)
    {
        var finalCapacity = finalValidation.Device.TotalBytes;
        var initialCapacity = evidence.ReportedDiskCapacityBytes;
        var capacityState = evidence.CapacityEvidenceIsConsistent;
        var capacityReason = evidence.CapacityEvidenceReason;
        if (initialCapacity is > 0 && finalCapacity > 0)
        {
            var tolerance = Math.Max(16L * 1024 * 1024, Math.Max(initialCapacity.Value, finalCapacity) / 100);
            if (Math.Abs(initialCapacity.Value - finalCapacity) > tolerance)
            {
                capacityState = EvidenceState.No;
                capacityReason = "Physical disk capacity changed between the initial and final identity checks.";
            }
        }

        return evidence with
        {
            DevicePresent = EvidenceState.Yes,
            FinalIdentityRevalidated = EvidenceState.Yes,
            CapacityEvidenceIsConsistent = capacityState,
            CapacityEvidenceReason = capacityReason
        };
    }

    private static DeviceDiagnosticEvidence BuildInitialValidationFailure(
        StorageDevice device,
        InvalidOperationException exception)
    {
        var disconnected = exception.Message.Equals(
            StorageSafetyMessages.DeviceChanged,
            StringComparison.Ordinal);
        return new DeviceDiagnosticEvidence
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            DeviceIdentity = device.Identity,
            DevicePresent = disconnected ? EvidenceState.No : EvidenceState.Unknown,
            IdentityRevalidated = EvidenceState.No,
            FinalIdentityRevalidated = EvidenceState.No,
            IdentityValidationReason = SafeIdentityReason(exception),
            PhysicalDiskNumber = device.Identity.PhysicalDiskNumber,
            PhysicalPath = device.Identity.PhysicalDevicePath,
            Model = device.Identity.Model ?? string.Empty,
            BusType = device.Identity.BusType ?? string.Empty,
            DeviceDisconnectedDuringAnalysis = disconnected ? EvidenceState.Yes : EvidenceState.Unknown,
            CollectionWarnings = ["The selected physical identity could not be validated safely."]
        };
    }

    private static DeviceDiagnosticEvidence BuildCollectorFailure(ValidatedStorageDevice device)
    {
        return new DeviceDiagnosticEvidence
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            DeviceIdentity = device.Device.Identity,
            DevicePresent = EvidenceState.Yes,
            IdentityRevalidated = EvidenceState.Yes,
            IdentityValidationReason = device.Validation.Reason,
            IsRemovable = ToState(device.Device.IsRemovable),
            IsSystemDisk = ToState(device.Device.IsSystemDisk),
            IsBootDisk = ToState(device.Device.IsBootDisk),
            PhysicalDiskNumber = device.Device.Identity.PhysicalDiskNumber,
            PhysicalPath = device.Device.PhysicalPath,
            Model = device.Device.Identity.Model ?? string.Empty,
            BusType = device.Device.Identity.BusType ?? string.Empty,
            ReportedDiskCapacityBytes = device.Device.TotalBytes > 0 ? device.Device.TotalBytes : null,
            CollectionWarnings = ["Windows disk, partition, and volume evidence could not be collected (EvidenceUnavailable)."]
        };
    }

    private static DeviceDiagnosticEvidence BuildFinalValidationFailure(
        ValidatedStorageDevice initial,
        InvalidOperationException exception)
    {
        var identityIndeterminate = exception.Message.Equals(
            StorageSafetyMessages.IdentityIndeterminate,
            StringComparison.Ordinal);
        return new DeviceDiagnosticEvidence
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            DeviceIdentity = initial.Device.Identity,
            DevicePresent = identityIndeterminate ? EvidenceState.Unknown : EvidenceState.No,
            IdentityRevalidated = EvidenceState.Yes,
            FinalIdentityRevalidated = EvidenceState.No,
            IdentityValidationReason = SafeIdentityReason(exception),
            IsRemovable = ToState(initial.Device.IsRemovable),
            IsSystemDisk = ToState(initial.Device.IsSystemDisk),
            IsBootDisk = ToState(initial.Device.IsBootDisk),
            PhysicalDiskNumber = initial.Device.Identity.PhysicalDiskNumber,
            PhysicalPath = initial.Device.PhysicalPath,
            Model = initial.Device.Identity.Model ?? string.Empty,
            BusType = initial.Device.Identity.BusType ?? string.Empty,
            IdentityChangedDuringAnalysis = identityIndeterminate ? EvidenceState.Yes : EvidenceState.Unknown,
            DeviceDisconnectedDuringAnalysis = identityIndeterminate ? EvidenceState.Unknown : EvidenceState.Yes,
            CollectionWarnings = ["Evidence collected before the failed final identity check was discarded."]
        };
    }

    private static bool CanRunReadProbe(DeviceDiagnosticEvidence evidence)
    {
        return !string.IsNullOrWhiteSpace(evidence.PhysicalPath)
            && !IsYes(evidence.IsNoMedia)
            && !IsYes(evidence.IsOffline)
            && !IsYes(evidence.IdentityChangedDuringAnalysis);
    }

    private static IReadOnlyList<string> Append(
        IReadOnlyList<string> existing,
        string value)
    {
        return existing.Append(value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> Append(
        IReadOnlyList<string> existing,
        IReadOnlyList<string> values)
    {
        return existing.Concat(values).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string SafeIdentityReason(InvalidOperationException exception)
    {
        if (exception.Message.Equals(
            StorageSafetyMessages.DeviceChanged,
            StringComparison.Ordinal))
        {
            return "The selected physical device was changed or disconnected.";
        }
        if (exception.Message.Equals(
            StorageSafetyMessages.IdentityIndeterminate,
            StringComparison.Ordinal))
        {
            return "The physical identity evidence was ambiguous.";
        }
        return "The physical device could not be validated for read-only analysis.";
    }

    private static string Fingerprint(StorageDeviceIdentity identity)
    {
        var source = identity.SerialNumber
            ?? identity.PnpDeviceId
            ?? identity.DeviceInstanceId
            ?? $"{identity.Model}|{identity.CapacityBytes}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
    }

    private static void Report(
        IProgress<DeviceAnalysisProgress> progress,
        DeviceAnalysisStage stage,
        double percentage,
        string message)
    {
        progress.Report(new DeviceAnalysisProgress(stage, percentage, message));
    }

    private static EvidenceState ToState(bool value) => value ? EvidenceState.Yes : EvidenceState.No;
    private static bool IsYes(EvidenceState value) => value == EvidenceState.Yes;
}
