using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class DeviceDetectionService : IDeviceDetectionService
{
    private readonly IWindowsPhysicalDiskProvider _diskProvider;

    public DeviceDetectionService(IWindowsPhysicalDiskProvider diskProvider)
    {
        _diskProvider = diskProvider;
    }

    public async Task<IEnumerable<StorageDevice>> GetRemovableDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StorageDevice> physicalDisks;
        try
        {
            physicalDisks = await _diskProvider.GetPhysicalDisksAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            physicalDisks = Array.Empty<StorageDevice>();
        }
        var externalDisks = physicalDisks
            .Where(device => device.IsUsbConnected || device.IsRemovable)
            .ToList();
        if (externalDisks.Count > 0)
        {
            return externalDisks;
        }

        return await GetMountedRemovableDrivesAsync(physicalDisks, cancellationToken);
    }

    private async Task<IReadOnlyList<StorageDevice>> GetMountedRemovableDrivesAsync(
        IReadOnlyCollection<StorageDevice> physicalDisks,
        CancellationToken cancellationToken)
    {
        var devices = new List<StorageDevice>();
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Removable))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var driveLetter = drive.Name.TrimEnd('\\');
            IReadOnlyList<int> diskNumbers;
            try
            {
                diskNumbers = await _diskProvider.ResolvePathDiskNumbersAsync(drive.Name, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                diskNumbers = Array.Empty<int>();
            }
            var physicalDevice = diskNumbers.Count == 1
                ? physicalDisks.FirstOrDefault(device => device.DiskNumber == diskNumbers[0])
                : null;
            if (physicalDevice is not null)
            {
                physicalDevice.IsRemovable = true;
                devices.Add(physicalDevice);
                continue;
            }

            var totalBytes = GetTotalBytes(drive);
            devices.Add(new StorageDevice
            {
                Identity = new StorageDeviceIdentity
                {
                    CapacityBytes = totalBytes,
                    MountPoints = new[] { drive.Name }
                },
                DisplayName = GetVolumeLabel(drive),
                VolumeLabel = GetVolumeLabel(drive),
                DriveLetter = driveLetter,
                TotalBytes = totalBytes,
                FreeBytes = GetFreeBytes(drive),
                FileSystem = GetFileSystem(drive),
                IsRemovable = true,
                Status = GetDriveStatus(drive)
            });
        }

        return devices;
    }

    private static DeviceHealthStatus GetDriveStatus(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return DeviceHealthStatus.Inaccessible;
            }

            return string.IsNullOrEmpty(drive.DriveFormat)
                || drive.DriveFormat.Equals("RAW", StringComparison.OrdinalIgnoreCase)
                ? DeviceHealthStatus.Raw
                : DeviceHealthStatus.Healthy;
        }
        catch
        {
            return DeviceHealthStatus.Inaccessible;
        }
    }

    private static string GetVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.VolumeLabel
                : "Removable Disk";
        }
        catch
        {
            return "Removable Disk";
        }
    }

    private static long GetTotalBytes(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.TotalSize : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetFreeBytes(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetFileSystem(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.DriveFormat : "RAW/Unknown";
        }
        catch
        {
            return "RAW/Unknown";
        }
    }
}
