using System.Security.Cryptography;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;
using Serilog;

namespace PendriveRescue.Infrastructure.Services;

public sealed class StorageDeviceIdentityService : IStorageDeviceIdentityService
{
    private readonly IWindowsPhysicalDiskProvider _diskProvider;

    public StorageDeviceIdentityService(IWindowsPhysicalDiskProvider diskProvider)
    {
        _diskProvider = diskProvider;
    }

    public async Task<StorageDevice?> ResolveCurrentDeviceAsync(
        StorageDeviceIdentity identity,
        CancellationToken cancellationToken)
    {
        var devices = await _diskProvider.GetPhysicalDisksAsync(cancellationToken);
        var matches = devices
            .Where(device => RepresentsSamePhysicalDisk(identity, device.Identity) == DeviceIdentityMatch.Match)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public DeviceIdentityMatch RepresentsSamePhysicalDisk(
        StorageDeviceIdentity first,
        StorageDeviceIdentity second)
    {
        return StorageDeviceIdentityComparer.Compare(first, second);
    }

    public async Task<StorageDeviceIdentity?> ResolvePathIdentityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        var diskNumbers = await _diskProvider.ResolvePathDiskNumbersAsync(path, cancellationToken);
        if (diskNumbers.Count != 1)
        {
            return null;
        }

        var disks = await _diskProvider.GetPhysicalDisksAsync(cancellationToken);
        var matches = disks.Where(device => device.DiskNumber == diskNumbers[0]).ToList();
        return matches.Count == 1 ? matches[0].Identity : null;
    }

}

public sealed class DeviceSafetyAuditService : IDeviceSafetyAuditService
{
    public void RecordValidation(StorageOperationKind operation, DeviceIdentityValidation validation)
    {
        var original = Describe(validation.OriginalIdentity);
        var current = validation.CurrentIdentity is null ? "unresolved" : Describe(validation.CurrentIdentity);
        if (validation.Match == DeviceIdentityMatch.Match)
        {
            Log.Information(
                "Storage safety validation passed for {Operation}. Original={OriginalIdentity}; Current={CurrentIdentity}; Outcome={Outcome}; Reason={Reason}",
                operation,
                original,
                current,
                validation.Match,
                validation.Reason);
            return;
        }

        Log.Warning(
            "Storage safety validation blocked {Operation}. Original={OriginalIdentity}; Current={CurrentIdentity}; Outcome={Outcome}; Reason={Reason}",
            operation,
            original,
            current,
            validation.Match,
            validation.Reason);
    }

    public void RecordDestinationValidation(
        StorageDeviceIdentity source,
        StorageDeviceIdentity? destination,
        DeviceIdentityMatch outcome,
        string reason)
    {
        var sourceDescription = Describe(source);
        var destinationDescription = destination is null ? "unresolved" : Describe(destination);
        if (outcome == DeviceIdentityMatch.Different)
        {
            Log.Information(
                "Recovery destination safety validation passed. Source={SourceIdentity}; Destination={DestinationIdentity}; Outcome={Outcome}; Reason={Reason}",
                sourceDescription,
                destinationDescription,
                outcome,
                reason);
            return;
        }

        Log.Warning(
            "Recovery destination safety validation blocked recovery. Source={SourceIdentity}; Destination={DestinationIdentity}; Outcome={Outcome}; Reason={Reason}",
            sourceDescription,
            destinationDescription,
            outcome,
            reason);
    }

    private static string Describe(StorageDeviceIdentity identity)
    {
        return $"Disk={identity.PhysicalDiskNumber?.ToString() ?? "?"}, "
            + $"Path={identity.PhysicalDevicePath}, "
            + $"Model={identity.Model ?? "?"}, "
            + $"Capacity={identity.CapacityBytes}, "
            + $"SerialHash={HashSerial(identity.SerialNumber)}, "
            + $"Mounts={string.Join(',', identity.MountPoints)}";
    }

    private static string HashSerial(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return "none";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serialNumber.Trim()));
        return Convert.ToHexString(hash)[..12];
    }
}
