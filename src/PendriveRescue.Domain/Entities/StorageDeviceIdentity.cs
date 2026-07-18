using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Entities;

/// <summary>
/// Immutable snapshot of the Windows physical disk that owns one or more volumes.
/// Drive letters and mount points are descriptive only and are never sufficient identity by themselves.
/// </summary>
public sealed record StorageDeviceIdentity
{
    public int? PhysicalDiskNumber { get; init; }
    public string PhysicalDevicePath { get; init; } = string.Empty;
    public string? DeviceInstanceId { get; init; }
    public string? SerialNumber { get; init; }
    public string? PnpDeviceId { get; init; }
    public string? Model { get; init; }
    public long CapacityBytes { get; init; }
    public string? BusType { get; init; }
    public IReadOnlyList<string> VolumeGuidPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MountPoints { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Records the identity decision made immediately before an operation starts.
/// </summary>
public sealed record DeviceIdentityValidation
{
    public StorageDeviceIdentity OriginalIdentity { get; init; } = new();
    public StorageDeviceIdentity? CurrentIdentity { get; init; }
    public DeviceIdentityMatch Match { get; init; } = DeviceIdentityMatch.Indeterminate;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset ValidatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A freshly enumerated device plus the identity evidence that authorized its use.
/// </summary>
public sealed record ValidatedStorageDevice(
    StorageDevice Device,
    DeviceIdentityValidation Validation);

/// <summary>
/// Validated source and destination identities for a recovery operation.
/// </summary>
public sealed record ValidatedRecoveryTarget(
    ValidatedStorageDevice Source,
    StorageDeviceIdentity DestinationIdentity);

/// <summary>Physical identity captured when the user chooses a recovery destination.</summary>
public sealed record RecoveryDestinationSelection(
    string Path,
    StorageDeviceIdentity Identity,
    DateTimeOffset SelectedAtUtc);

/// <summary>Single conservative comparison algorithm shared by detection, refresh, and operation guards.</summary>
public static class StorageDeviceIdentityComparer
{
    public static DeviceIdentityMatch Compare(StorageDeviceIdentity first, StorageDeviceIdentity second)
    {
        if (HasConflict(first.PnpDeviceId, second.PnpDeviceId)
            || HasConflict(first.DeviceInstanceId, second.DeviceInstanceId)
            || HasConflict(first.SerialNumber, second.SerialNumber))
        {
            return DeviceIdentityMatch.Different;
        }

        var modelMatches = Matches(first.Model, second.Model);
        var capacityMatches = first.CapacityBytes > 0
            && second.CapacityBytes > 0
            && first.CapacityBytes == second.CapacityBytes;
        if (BothPresent(first.Model, second.Model) && !modelMatches)
        {
            return DeviceIdentityMatch.Different;
        }

        if (first.CapacityBytes > 0 && second.CapacityBytes > 0 && !capacityMatches)
        {
            return DeviceIdentityMatch.Different;
        }

        var pnpMatches = Matches(first.PnpDeviceId, second.PnpDeviceId)
            || Matches(first.DeviceInstanceId, second.DeviceInstanceId);
        var diskNumberMatches = first.PhysicalDiskNumber.HasValue
            && second.PhysicalDiskNumber.HasValue
            && first.PhysicalDiskNumber == second.PhysicalDiskNumber;
        if (diskNumberMatches && pnpMatches)
        {
            return DeviceIdentityMatch.Match;
        }

        if (pnpMatches && modelMatches && capacityMatches)
        {
            return DeviceIdentityMatch.Match;
        }

        if (Matches(first.PhysicalDevicePath, second.PhysicalDevicePath) && modelMatches && capacityMatches)
        {
            return DeviceIdentityMatch.Match;
        }

        var serialMatches = Matches(first.SerialNumber, second.SerialNumber);
        if (serialMatches && modelMatches && capacityMatches)
        {
            return DeviceIdentityMatch.Match;
        }

        if (first.PhysicalDiskNumber.HasValue
            && second.PhysicalDiskNumber.HasValue
            && first.PhysicalDiskNumber != second.PhysicalDiskNumber
            && !pnpMatches
            && !serialMatches)
        {
            return DeviceIdentityMatch.Different;
        }

        return DeviceIdentityMatch.Indeterminate;
    }

    private static bool BothPresent(string? first, string? second)
    {
        return !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second);
    }

    private static bool HasConflict(string? first, string? second)
    {
        return BothPresent(first, second) && !Matches(first, second);
    }

    private static bool Matches(string? first, string? second)
    {
        return BothPresent(first, second)
            && Normalize(first!).Equals(Normalize(second!), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.Trim().TrimEnd('\\').Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
