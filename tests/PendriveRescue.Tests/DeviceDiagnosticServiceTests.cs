using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class DeviceDiagnosticServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_RevalidatesBeforeAndAfterCollection()
    {
        var guard = new FakeOperationGuard(CreateValidatedDevice());
        var collector = new FakeEvidenceCollector(DiagnosticEvidenceFactory.Healthy());
        var service = CreateService(guard, collector);
        var progress = new RecordingProgress<DeviceAnalysisProgress>();

        var result = await service.AnalyzeAsync(
            guard.Device.Device,
            CancellationToken.None,
            progress);

        Assert.Equal(2, guard.RevalidationCount);
        Assert.Equal(1, collector.CollectionCount);
        Assert.Equal(DeviceDiagnosticCondition.MountedAndReadable, result.PrimaryCondition);
        Assert.Contains(progress.Values, item => item.Stage == DeviceAnalysisStage.RevalidatingDevice);
        Assert.Contains(progress.Values, item => item.Stage == DeviceAnalysisStage.Completed);
    }

    [Fact]
    public async Task AnalyzeAsync_DiscardsEvidenceWhenFinalIdentityIsIndeterminate()
    {
        var guard = new FakeOperationGuard(
            CreateValidatedDevice(),
            failSecondValidationWith: StorageSafetyMessages.IdentityIndeterminate);
        var service = CreateService(
            guard,
            new FakeEvidenceCollector(DiagnosticEvidenceFactory.Raw()));

        var result = await service.AnalyzeAsync(
            guard.Device.Device,
            CancellationToken.None,
            new RecordingProgress<DeviceAnalysisProgress>());

        Assert.Equal(DeviceDiagnosticCondition.DeviceIdentityChanged, result.PrimaryCondition);
        Assert.False(result.AnalysisComplete);
        Assert.Empty(result.Findings.Where(finding => finding.Condition == DeviceDiagnosticCondition.RawFileSystem));
        Assert.False(result.SafeRepairMayBeAppropriate);
    }

    [Fact]
    public async Task AnalyzeAsync_ContinuesWhenEvidenceCollectorFails()
    {
        var guard = new FakeOperationGuard(CreateValidatedDevice());
        var service = CreateService(guard, new FakeEvidenceCollector(throwOnCollect: true));

        var result = await service.AnalyzeAsync(
            guard.Device.Device,
            CancellationToken.None,
            new RecordingProgress<DeviceAnalysisProgress>());

        Assert.Equal(DeviceDiagnosticCondition.AnalysisIncomplete, result.PrimaryCondition);
        Assert.False(result.AnalysisComplete);
        Assert.Contains(result.Limitations, value => value.Contains("metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeAsync_TreatsReadProbeAccessDeniedAsUnavailableEvidence()
    {
        var guard = new FakeOperationGuard(CreateValidatedDevice());
        var service = CreateService(
            guard,
            new FakeEvidenceCollector(DiagnosticEvidenceFactory.Healthy()),
            new FakeReadProbe(new DeviceReadProbeResult
            {
                Attempted = true,
                BytesRequested = DeviceReadProbeOptions.DefaultBytesToRead,
                FailureCategory = DiagnosticFailureCategory.AccessDenied
            }));

        var result = await service.AnalyzeAsync(
            guard.Device.Device,
            CancellationToken.None,
            new RecordingProgress<DeviceAnalysisProgress>());

        Assert.Equal(DeviceDiagnosticCondition.AnalysisIncomplete, result.PrimaryCondition);
        Assert.False(result.AnalysisComplete);
        Assert.Equal(0, result.Evidence.ReadErrorCount);
        Assert.DoesNotContain(result.Findings, finding =>
            finding.Condition == DeviceDiagnosticCondition.ReadErrorsDetected);
        Assert.Contains(result.Limitations, value =>
            value.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase));
    }

    private static DeviceDiagnosticService CreateService(
        FakeOperationGuard guard,
        IDeviceDiagnosticEvidenceCollector collector,
        IDeviceReadProbe? readProbe = null)
    {
        return new DeviceDiagnosticService(
            guard,
            collector,
            readProbe ?? new FakeReadProbe(),
            new FakeSecurityEvidenceProvider(),
            new DeviceDiagnosticEngine(),
            new FixedTimeProvider());
    }

    private static ValidatedStorageDevice CreateValidatedDevice()
    {
        var identity = new StorageDeviceIdentity
        {
            PhysicalDiskNumber = 2,
            PhysicalDevicePath = @"\\.\PHYSICALDRIVE2",
            PnpDeviceId = "USBSTOR\\TEST",
            Model = "Test USB",
            CapacityBytes = 8L * 1024 * 1024 * 1024,
            BusType = "USB"
        };
        var device = new StorageDevice
        {
            Identity = identity,
            DiskNumber = 2,
            PhysicalPath = identity.PhysicalDevicePath,
            DisplayName = "Test USB (E:)",
            DriveLetter = "E:",
            FileSystem = "exFAT",
            TotalBytes = identity.CapacityBytes,
            IsRemovable = true,
            IsUsbConnected = true,
            Status = DeviceHealthStatus.Healthy
        };
        return new ValidatedStorageDevice(device, new DeviceIdentityValidation
        {
            OriginalIdentity = identity,
            CurrentIdentity = identity,
            Match = DeviceIdentityMatch.Match,
            Reason = "Identity unchanged."
        });
    }

    private sealed class FakeOperationGuard : IStorageDeviceOperationGuard
    {
        private readonly string? _failSecondValidationWith;

        public FakeOperationGuard(
            ValidatedStorageDevice device,
            string? failSecondValidationWith = null)
        {
            Device = device;
            _failSecondValidationWith = failSecondValidationWith;
        }

        public ValidatedStorageDevice Device { get; }
        public int RevalidationCount { get; private set; }

        public Task<ValidatedStorageDevice> RevalidateAsync(
            StorageDevice selectedDevice,
            StorageOperationKind operation,
            CancellationToken cancellationToken)
        {
            RevalidationCount++;
            if (RevalidationCount == 2 && _failSecondValidationWith is not null)
            {
                throw new InvalidOperationException(_failSecondValidationWith);
            }
            return Task.FromResult(Device);
        }

        public Task<ValidatedRecoveryTarget> ValidateRecoveryAsync(
            StorageDevice selectedSource,
            RecoveryDestinationSelection destination,
            bool requiresMountedSource,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeEvidenceCollector : IDeviceDiagnosticEvidenceCollector
    {
        private readonly DeviceDiagnosticEvidence? _evidence;
        private readonly bool _throwOnCollect;

        public FakeEvidenceCollector(DeviceDiagnosticEvidence? evidence = null, bool throwOnCollect = false)
        {
            _evidence = evidence;
            _throwOnCollect = throwOnCollect;
        }

        public int CollectionCount { get; private set; }

        public Task<DeviceDiagnosticEvidence> CollectAsync(
            ValidatedStorageDevice device,
            CancellationToken cancellationToken)
        {
            CollectionCount++;
            return _throwOnCollect
                ? Task.FromException<DeviceDiagnosticEvidence>(new InvalidOperationException("Simulated collector failure."))
                : Task.FromResult(_evidence!);
        }
    }

    private sealed class FakeReadProbe : IDeviceReadProbe
    {
        private readonly DeviceReadProbeResult? _result;

        public FakeReadProbe(DeviceReadProbeResult? result = null)
        {
            _result = result;
        }

        public Task<DeviceReadProbeResult> ProbeAsync(
            ValidatedStorageDevice device,
            DeviceReadProbeOptions options,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result ?? new DeviceReadProbeResult
            {
                Attempted = true,
                Success = true,
                BytesRequested = options.BytesToRead,
                BytesRead = options.BytesToRead,
                Duration = TimeSpan.FromMilliseconds(20)
            });
        }
    }

    private sealed class FakeSecurityEvidenceProvider : IDeviceSecurityEvidenceProvider
    {
        public Task<DeviceSecurityEvidence> CollectAsync(
            ValidatedStorageDevice device,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new DeviceSecurityEvidence
            {
                Collected = EvidenceState.Yes,
                SuspiciousAutorunDetected = EvidenceState.No,
                SuspiciousShortcutPatternDetected = EvidenceState.No,
                SuspiciousLauncherCount = 0,
                DefenderAvailable = EvidenceState.Yes,
                DefenderThreatRequiresAction = EvidenceState.Unknown
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
