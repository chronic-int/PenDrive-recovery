using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class DeviceDiagnosticEngineTests
{
    private readonly DeviceDiagnosticEngine _engine = new();

    public static IEnumerable<object[]> DiagnosticCases()
    {
        yield return [DiagnosticEvidenceFactory.Healthy(), DeviceDiagnosticCondition.MountedAndReadable];
        yield return [DiagnosticEvidenceFactory.Raw(), DeviceDiagnosticCondition.RawFileSystem];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                DriveLetter = string.Empty,
                HasMountedVolume = EvidenceState.No,
                VolumeAccessible = EvidenceState.No,
                RootDirectoryReadable = EvidenceState.Unknown
            },
            DeviceDiagnosticCondition.MissingDriveLetter
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                PartitionCount = 0,
                HasAllocatedPartition = EvidenceState.No,
                HasVolume = EvidenceState.No,
                HasMountedVolume = EvidenceState.No,
                DriveLetter = string.Empty,
                FileSystem = string.Empty,
                FileSystemRecognized = EvidenceState.Unknown,
                VolumeAccessible = EvidenceState.No,
                RootDirectoryReadable = EvidenceState.Unknown
            },
            DeviceDiagnosticCondition.UnallocatedDisk
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with { IsReadOnly = EvidenceState.Yes },
            DeviceDiagnosticCondition.ReadOnlyDisk
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                VolumeAccessible = EvidenceState.No,
                RootDirectoryReadable = EvidenceState.No,
                AccessFailureCategory = DiagnosticFailureCategory.AccessDenied
            },
            DeviceDiagnosticCondition.InaccessibleVolume
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                ReadProbeSucceeded = EvidenceState.No,
                ReadProbeBytesCompleted = 512 * 1024,
                IoErrorCount = 3,
                ReadErrorCount = 1,
                TimedOut = EvidenceState.Yes
            },
            DeviceDiagnosticCondition.SevereIoFailure
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                DeviceDisconnectedDuringAnalysis = EvidenceState.Yes,
                DeviceReappearedDuringAnalysis = EvidenceState.Yes
            },
            DeviceDiagnosticCondition.IntermittentConnection
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                SuspiciousShortcutPatternDetected = EvidenceState.Yes,
                SuspiciousLauncherCount = 2,
                DefenderThreatRequiresAction = EvidenceState.No
            },
            DeviceDiagnosticCondition.MalwareSymptoms
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                DefenderThreatRequiresAction = EvidenceState.Yes
            },
            DeviceDiagnosticCondition.ActiveMalwareThreat
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                CapacityEvidenceIsConsistent = EvidenceState.No,
                CapacityEvidenceReason = "Capacity changed between checks."
            },
            DeviceDiagnosticCondition.SuspiciousCapacity
        ];
        yield return
        [
            DiagnosticEvidenceFactory.Healthy() with
            {
                PartitionMetadataAvailable = EvidenceState.Unknown,
                HasPartitionTable = EvidenceState.Unknown,
                HasAllocatedPartition = EvidenceState.Unknown,
                PartitionCount = null,
                VolumeMetadataAvailable = EvidenceState.Unknown,
                HasMountedVolume = EvidenceState.Unknown,
                FileSystem = string.Empty,
                FileSystemRecognized = EvidenceState.Unknown,
                RootDirectoryReadable = EvidenceState.Unknown,
                ReadProbeAttempted = EvidenceState.No,
                ReadProbeSucceeded = EvidenceState.Unknown,
                ReadProbeBytesRequested = 0,
                ReadProbeBytesCompleted = 0
            },
            DeviceDiagnosticCondition.AnalysisIncomplete
        ];
    }

    [Theory]
    [MemberData(nameof(DiagnosticCases))]
    public void Evaluate_ReturnsExpectedPrimaryCondition(
        DeviceDiagnosticEvidence evidence,
        DeviceDiagnosticCondition expected)
    {
        var result = Evaluate(evidence);

        Assert.Equal(expected, result.PrimaryCondition);
        Assert.NotEmpty(result.Findings);
        Assert.NotEmpty(result.RecommendedActions);
        Assert.DoesNotContain(
            result.RecommendedActions,
            action => action.Kind == DiagnosticActionKind.ConsiderDestructiveRepair && action.Enabled);
    }

    [Fact]
    public void Evaluate_RawFileSystemDoesNotImplyPhysicalDamage()
    {
        var result = Evaluate(DiagnosticEvidenceFactory.Raw());

        Assert.False(result.LikelyPhysicalDamage);
        Assert.True(result.RecoveryRecommendedFirst);
        Assert.Contains(result.RecommendedActions, action =>
            action.Kind == DiagnosticActionKind.RunDeepScan && action.Enabled);
    }

    [Fact]
    public void Evaluate_MissingDriveLetterDoesNotImplyPhysicalDamage()
    {
        var evidence = DiagnosticEvidenceFactory.Healthy() with
        {
            DriveLetter = string.Empty,
            HasMountedVolume = EvidenceState.No,
            VolumeAccessible = EvidenceState.No,
            RootDirectoryReadable = EvidenceState.Unknown
        };

        var result = Evaluate(evidence);

        Assert.Equal(DeviceDiagnosticCondition.MissingDriveLetter, result.PrimaryCondition);
        Assert.False(result.LikelyPhysicalDamage);
        Assert.True(result.RecoveryRecommendedFirst);
    }

    [Fact]
    public void Evaluate_OneTimeoutDoesNotDiagnosePhysicalDamage()
    {
        var evidence = DiagnosticEvidenceFactory.Healthy() with
        {
            ReadProbeSucceeded = EvidenceState.No,
            ReadProbeBytesCompleted = 0,
            ReadErrorCount = 1,
            IoErrorCount = 1,
            TimedOut = EvidenceState.Yes
        };

        var result = Evaluate(evidence);

        Assert.Equal(DeviceDiagnosticCondition.ReadErrorsDetected, result.PrimaryCondition);
        Assert.False(result.LikelyPhysicalDamage);
    }

    [Fact]
    public void Evaluate_PhysicalFailureDisablesScansAndRepairs()
    {
        var evidence = DiagnosticEvidenceFactory.Healthy() with
        {
            ReadProbeSucceeded = EvidenceState.No,
            ReadProbeBytesCompleted = 1024,
            ReadErrorCount = 1,
            IoErrorCount = 3,
            TimedOut = EvidenceState.Yes
        };

        var result = Evaluate(evidence);

        Assert.True(result.LikelyPhysicalDamage);
        Assert.DoesNotContain(result.RecommendedActions, action =>
            action.Enabled && action.Kind is DiagnosticActionKind.RunDeepScan
                or DiagnosticActionKind.TrySafeRepair
                or DiagnosticActionKind.ConsiderDestructiveRepair);
        Assert.Contains(result.RecommendedActions, action =>
            action.Kind == DiagnosticActionKind.SeekProfessionalRecovery && action.Enabled);
    }

    private DeviceDiagnosticResult Evaluate(DeviceDiagnosticEvidence evidence)
    {
        return _engine.Evaluate(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 18, 12, 0, 1, TimeSpan.Zero),
            evidence);
    }
}
