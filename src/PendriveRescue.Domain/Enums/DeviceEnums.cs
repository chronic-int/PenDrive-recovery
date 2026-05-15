namespace PendriveRescue.Domain.Enums;

public enum DeviceHealthStatus
{
    Healthy,
    Raw,
    Inaccessible,
    Unmounted,
    Unknown
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
