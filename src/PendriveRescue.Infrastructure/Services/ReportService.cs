using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class ReportService : IReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<bool> ExportReportAsync(ScanResult result, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
            var report = new
            {
                ReportType = "Scan",
                CreatedAt = DateTimeOffset.Now,
                result.Type,
                FilesFound = result.FilesFound.Count,
                result.Errors,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                SourceDevice = ToReportIdentity(result.SourceDeviceIdentity),
                IdentityValidation = ToReportValidation(result.IdentityValidation),
                Files = result.FilesFound
            };

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(report, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExportReportAsync(RecoveryJob job, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
            var report = new
            {
                ReportType = "Recovery",
                CreatedAt = DateTimeOffset.Now,
                job.DestinationPath,
                job.ProgressPercentage,
                job.State,
                SourceDevice = ToReportIdentity(job.SourceDeviceIdentity),
                DestinationDevice = ToReportIdentity(job.DestinationDeviceIdentity),
                IdentityValidation = ToReportValidation(job.IdentityValidation),
                TotalFiles = job.SourceFiles.Count,
                RecoveredFiles = job.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Recovered),
                FailedFiles = job.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Failed),
                PartialFiles = job.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Partial),
                Files = job.SourceFiles
            };

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(report, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExportReportAsync(DeviceDiagnosticResult result, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
            var evidence = result.Evidence;
            var report = new
            {
                ReportType = "DeviceDiagnostic",
                CreatedAt = DateTimeOffset.Now,
                result.AnalysisId,
                result.StartedAtUtc,
                result.CompletedAtUtc,
                TargetDevice = ToDiagnosticReportIdentity(result.TargetIdentity),
                result.PrimaryCondition,
                result.Confidence,
                result.Severity,
                result.Title,
                result.Summary,
                result.AnalysisComplete,
                result.RecoveryRecommendedFirst,
                result.SafeRepairMayBeAppropriate,
                result.DestructiveRepairMayBeAppropriate,
                result.LikelyPhysicalDamage,
                Findings = result.Findings.Select(finding => new
                {
                    finding.Code,
                    finding.Condition,
                    finding.Confidence,
                    finding.Severity,
                    finding.Title,
                    finding.Explanation,
                    finding.Evidence,
                    finding.MissingEvidence,
                    finding.RecommendedActions,
                    finding.ActionsToAvoid,
                    finding.RecoveryRecommendedFirst
                }),
                Evidence = new
                {
                    evidence.CollectedAtUtc,
                    evidence.DevicePresent,
                    evidence.IdentityRevalidated,
                    evidence.FinalIdentityRevalidated,
                    evidence.IdentityValidationReason,
                    evidence.IsRemovable,
                    evidence.IsSystemDisk,
                    evidence.IsBootDisk,
                    evidence.PhysicalDiskNumber,
                    evidence.Model,
                    evidence.BusType,
                    evidence.ReportedDiskCapacityBytes,
                    evidence.PartitionCount,
                    evidence.PartitionMetadataAvailable,
                    evidence.HasPartitionTable,
                    evidence.HasAllocatedPartition,
                    evidence.HasUnallocatedCapacity,
                    evidence.HasVolume,
                    evidence.HasMountedVolume,
                    evidence.FileSystem,
                    evidence.VolumeCapacityBytes,
                    evidence.FreeSpaceBytes,
                    evidence.VolumeMetadataAvailable,
                    evidence.VolumeAccessible,
                    evidence.RootDirectoryReadable,
                    evidence.AccessFailureCategory,
                    evidence.IsReadOnly,
                    evidence.ReadOnlyEvidenceSource,
                    evidence.IsOffline,
                    evidence.IsNoMedia,
                    evidence.IsRawFileSystem,
                    evidence.FileSystemRecognized,
                    evidence.ReadProbeAttempted,
                    evidence.ReadProbeSucceeded,
                    evidence.ReadProbeBytesRequested,
                    evidence.ReadProbeBytesCompleted,
                    evidence.ReadProbeDuration,
                    evidence.ReadErrorCount,
                    evidence.IoErrorCount,
                    evidence.TimedOut,
                    evidence.ReadProbeFailureCategory,
                    evidence.DeviceDisconnectedDuringAnalysis,
                    evidence.DeviceReappearedDuringAnalysis,
                    evidence.IdentityChangedDuringAnalysis,
                    evidence.SecurityEvidenceCollected,
                    evidence.SuspiciousAutorunDetected,
                    evidence.SuspiciousShortcutPatternDetected,
                    evidence.SuspiciousLauncherCount,
                    evidence.DefenderAvailable,
                    evidence.DefenderThreatRequiresAction,
                    evidence.CapacityEvidenceIsConsistent,
                    evidence.CapacityEvidenceReason,
                    evidence.CollectionWarnings
                },
                result.EvidenceSummary,
                result.RecommendedActions,
                result.ActionsToAvoid,
                result.Limitations
            };

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(report, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? ToReportIdentity(StorageDeviceIdentity? identity)
    {
        if (identity is null)
        {
            return null;
        }

        return new
        {
            identity.PhysicalDiskNumber,
            identity.PhysicalDevicePath,
            identity.Model,
            identity.CapacityBytes,
            identity.BusType,
            SerialNumberHash = HashSerial(identity.SerialNumber),
            identity.VolumeGuidPaths,
            identity.MountPoints
        };
    }

    private static object? ToReportValidation(DeviceIdentityValidation? validation)
    {
        return validation is null
            ? null
            : new
            {
                validation.Match,
                validation.Reason,
                validation.ValidatedAtUtc
            };
    }

    private static object ToDiagnosticReportIdentity(StorageDeviceIdentity identity)
    {
        return new
        {
            identity.PhysicalDiskNumber,
            identity.Model,
            identity.CapacityBytes,
            identity.BusType,
            SerialNumberHash = HashSerial(identity.SerialNumber),
            PnpIdentityHash = HashSerial(identity.PnpDeviceId ?? identity.DeviceInstanceId)
        };
    }

    private static string? HashSerial(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serialNumber.Trim()));
        return Convert.ToHexString(hash)[..12];
    }
}
