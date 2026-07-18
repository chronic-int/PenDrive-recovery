namespace PendriveRescue.Domain.Enums;

public enum DeviceHealthStatus
{
    Healthy,
    Raw,
    Inaccessible,
    Unmounted,
    Unknown
}

public enum DeviceIdentityMatch
{
    Match,
    Different,
    Indeterminate
}

public enum StorageOperationKind
{
    Diagnostic,
    QuickScan,
    DeepScan,
    Recovery,
    MalwareScan,
    MalwareCleanup,
    UsbProtectionRead,
    UsbProtectionChange,
    SafeRepair,
    DestructiveRepair
}

public enum RecoveryConfidence
{
    High,
    Medium,
    Low
}

public enum RecoveryState
{
    Pending,
    Recovered,
    Failed,
    Partial
}

public enum ScanType
{
    Quick,
    Deep
}

public enum DeviceProblemKind
{
    None,
    MissingPhysicalPath,
    SizeUnavailable,
    MissingDriveLetter,
    RawFileSystem,
    InaccessibleVolume,
    RawReadBlocked,
    PhysicalReadFailure,
    Unknown
}

public enum EvidenceState
{
    Unknown,
    No,
    Yes
}

public enum DeviceDiagnosticCondition
{
    Unknown,
    MountedAndReadable,
    MissingDriveLetter,
    OfflineDisk,
    ReadOnlyDisk,
    NoMedia,
    NoPartitionTable,
    UnallocatedDisk,
    MissingPartition,
    RawFileSystem,
    UnsupportedFileSystem,
    CorruptedFileSystem,
    InaccessibleVolume,
    PartitionTableProblem,
    CapacityMismatch,
    SuspiciousCapacity,
    MalwareSymptoms,
    ActiveMalwareThreat,
    IntermittentConnection,
    ReadErrorsDetected,
    SevereIoFailure,
    LikelyPhysicalDamage,
    DeviceRemoved,
    DeviceIdentityChanged,
    AnalysisIncomplete
}

public enum DiagnosticConfidence
{
    Unknown,
    Low,
    Medium,
    High
}

public enum DiagnosticSeverity
{
    Information,
    Caution,
    Warning,
    Critical
}

public enum DiagnosticActionKind
{
    None,
    RefreshDevices,
    ReconnectDevice,
    TryDifferentUsbPort,
    TryDifferentComputer,
    RunQuickScan,
    RunDeepScan,
    RecoverFiles,
    RunDefenderUsbScan,
    CleanMalwareArtifacts,
    EnableUsbProtection,
    TrySafeRepair,
    ConsiderDestructiveRepair,
    StopUsingDevice,
    SeekProfessionalRecovery,
    InspectInDiskManagement
}

public enum DeviceAnalysisStage
{
    RevalidatingDevice,
    ReadingDiskInformation,
    ReadingPartitionInformation,
    ReadingVolumeInformation,
    CheckingAccessibility,
    RunningReadProbe,
    CheckingSecurityIndicators,
    RevalidatingIdentity,
    EvaluatingEvidence,
    Completed
}

public enum DiagnosticFailureCategory
{
    None,
    Unknown,
    AccessDenied,
    DeviceNotReady,
    DeviceRemoved,
    IoFailure,
    Timeout,
    UnsupportedFileSystem,
    EvidenceUnavailable,
    IdentityMismatch
}
