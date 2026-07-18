using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Entities;

public sealed record DeviceDiagnosticEvidence
{
    public DateTimeOffset CollectedAtUtc { get; init; }
    public StorageDeviceIdentity DeviceIdentity { get; init; } = new();
    public EvidenceState DevicePresent { get; init; }
    public EvidenceState IdentityRevalidated { get; init; }
    public EvidenceState FinalIdentityRevalidated { get; init; }
    public string IdentityValidationReason { get; init; } = string.Empty;
    public EvidenceState IsRemovable { get; init; }
    public EvidenceState IsSystemDisk { get; init; }
    public EvidenceState IsBootDisk { get; init; }
    public int? PhysicalDiskNumber { get; init; }
    public string PhysicalPath { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string BusType { get; init; } = string.Empty;
    public long? ReportedDiskCapacityBytes { get; init; }
    public long? InitialReportedCapacityBytes { get; init; }
    public int? PartitionCount { get; init; }
    public EvidenceState PartitionMetadataAvailable { get; init; }
    public EvidenceState HasPartitionTable { get; init; }
    public EvidenceState HasAllocatedPartition { get; init; }
    public EvidenceState HasUnallocatedCapacity { get; init; }
    public EvidenceState HasVolume { get; init; }
    public EvidenceState HasMountedVolume { get; init; }
    public string DriveLetter { get; init; } = string.Empty;
    public IReadOnlyList<string> MountPoints { get; init; } = Array.Empty<string>();
    public string VolumeLabel { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public long? VolumeCapacityBytes { get; init; }
    public long? FreeSpaceBytes { get; init; }
    public EvidenceState VolumeMetadataAvailable { get; init; }
    public EvidenceState VolumeAccessible { get; init; }
    public EvidenceState RootDirectoryReadable { get; init; }
    public DiagnosticFailureCategory AccessFailureCategory { get; init; }
    public EvidenceState IsReadOnly { get; init; }
    public string ReadOnlyEvidenceSource { get; init; } = string.Empty;
    public EvidenceState IsOffline { get; init; }
    public EvidenceState IsNoMedia { get; init; }
    public EvidenceState IsRawFileSystem { get; init; }
    public EvidenceState FileSystemRecognized { get; init; }
    public EvidenceState ReadProbeAttempted { get; init; }
    public EvidenceState ReadProbeSucceeded { get; init; }
    public long ReadProbeBytesRequested { get; init; }
    public long ReadProbeBytesCompleted { get; init; }
    public TimeSpan? ReadProbeDuration { get; init; }
    public int ReadErrorCount { get; init; }
    public int IoErrorCount { get; init; }
    public EvidenceState TimedOut { get; init; }
    public DiagnosticFailureCategory ReadProbeFailureCategory { get; init; }
    public EvidenceState DeviceDisconnectedDuringAnalysis { get; init; }
    public EvidenceState DeviceReappearedDuringAnalysis { get; init; }
    public EvidenceState IdentityChangedDuringAnalysis { get; init; }
    public EvidenceState SecurityEvidenceCollected { get; init; }
    public EvidenceState SuspiciousAutorunDetected { get; init; }
    public EvidenceState SuspiciousShortcutPatternDetected { get; init; }
    public int? SuspiciousLauncherCount { get; init; }
    public EvidenceState DefenderAvailable { get; init; }
    public EvidenceState DefenderThreatRequiresAction { get; init; }
    public EvidenceState CapacityEvidenceIsConsistent { get; init; }
    public string CapacityEvidenceReason { get; init; } = string.Empty;
    public IReadOnlyList<string> CollectionWarnings { get; init; } = Array.Empty<string>();
}

public sealed record DiagnosticFinding
{
    public string Code { get; init; } = string.Empty;
    public DeviceDiagnosticCondition Condition { get; init; }
    public DiagnosticConfidence Confidence { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingEvidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RecommendedActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ActionsToAvoid { get; init; } = Array.Empty<string>();
    public bool RecoveryRecommendedFirst { get; init; }
    public bool DestructiveRepairAllowedAsNextStep { get; init; }
}

public sealed record RecommendedDiagnosticAction
{
    public DiagnosticActionKind Kind { get; init; }
    public int Priority { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool RequiresUserConfirmation { get; init; }
    public bool Destructive { get; init; }
    public bool Enabled { get; init; } = true;
    public string DisabledReason { get; init; } = string.Empty;
}

public sealed record DeviceDiagnosticResult
{
    public Guid AnalysisId { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public StorageDeviceIdentity TargetIdentity { get; init; } = new();
    public DeviceDiagnosticEvidence Evidence { get; init; } = new();
    public DeviceDiagnosticCondition PrimaryCondition { get; init; }
    public DiagnosticConfidence Confidence { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public string Title { get; init; } = "Analysis incomplete";
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<DiagnosticFinding> Findings { get; init; } = Array.Empty<DiagnosticFinding>();
    public IReadOnlyList<string> EvidenceSummary { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RecommendedDiagnosticAction> RecommendedActions { get; init; } = Array.Empty<RecommendedDiagnosticAction>();
    public IReadOnlyList<string> ActionsToAvoid { get; init; } = Array.Empty<string>();
    public bool RecoveryRecommendedFirst { get; init; }
    public bool SafeRepairMayBeAppropriate { get; init; }
    public bool DestructiveRepairMayBeAppropriate { get; init; }
    public bool LikelyPhysicalDamage { get; init; }
    public bool AnalysisComplete { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    public DeviceProblemKind ProblemKind => PrimaryCondition switch
    {
        DeviceDiagnosticCondition.MountedAndReadable => DeviceProblemKind.None,
        DeviceDiagnosticCondition.MissingDriveLetter => DeviceProblemKind.MissingDriveLetter,
        DeviceDiagnosticCondition.RawFileSystem => DeviceProblemKind.RawFileSystem,
        DeviceDiagnosticCondition.InaccessibleVolume => DeviceProblemKind.InaccessibleVolume,
        DeviceDiagnosticCondition.ReadErrorsDetected or
        DeviceDiagnosticCondition.SevereIoFailure or
        DeviceDiagnosticCondition.LikelyPhysicalDamage => DeviceProblemKind.PhysicalReadFailure,
        _ => DeviceProblemKind.Unknown
    };

    public string Details => Summary;
    public string Recommendation => RecommendedActions.FirstOrDefault(action => action.Enabled)?.Title
        ?? "Review the diagnostic evidence before choosing an action.";
    public bool CanAttemptSafeRepair => SafeRepairMayBeAppropriate;
    public bool ShouldUseDeepScan => RecommendedActions.Any(action =>
        action.Kind == DiagnosticActionKind.RunDeepScan && action.Enabled);
    public bool IsLikelyPhysicalDamage => LikelyPhysicalDamage;
    public string ConfidenceDisplay => $"{Confidence} confidence";
    public string SeverityDisplay => Severity.ToString();
    public string AnalysisTimeDisplay => CompletedAtUtc == default
        ? "Not completed"
        : CompletedAtUtc.ToLocalTime().ToString("g");
}

public sealed record DeviceReadProbeOptions
{
    public const int DefaultBytesToRead = 4 * 1024 * 1024;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    public int BytesToRead { get; init; } = DefaultBytesToRead;
    public int BlockSize { get; init; } = 256 * 1024;
    public TimeSpan Timeout { get; init; } = DefaultTimeout;
}

public sealed record DeviceReadProbeResult
{
    public bool Attempted { get; init; }
    public bool Success { get; init; }
    public long BytesRequested { get; init; }
    public long BytesRead { get; init; }
    public TimeSpan Duration { get; init; }
    public int IoErrorCount { get; init; }
    public bool TimedOut { get; init; }
    public bool DeviceRemoved { get; init; }
    public DiagnosticFailureCategory FailureCategory { get; init; }
}

public sealed record DeviceSecurityEvidence
{
    public EvidenceState Collected { get; init; }
    public EvidenceState SuspiciousAutorunDetected { get; init; }
    public EvidenceState SuspiciousShortcutPatternDetected { get; init; }
    public int? SuspiciousLauncherCount { get; init; }
    public EvidenceState DefenderAvailable { get; init; }
    public EvidenceState DefenderThreatRequiresAction { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record DeviceAnalysisProgress(
    DeviceAnalysisStage Stage,
    double Percentage,
    string Message);
