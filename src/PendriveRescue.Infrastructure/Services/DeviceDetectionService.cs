using System.Globalization;
using System.Management;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class DeviceDetectionService : IDeviceDetectionService
{
    public Task<IEnumerable<StorageDevice>> GetRemovableDevicesAsync()
    {
        return Task.Run(() =>
        {
            var physicalDevices = GetPhysicalUsbOrRemovableDisks().ToList();
            if (physicalDevices.Count > 0)
            {
                return physicalDevices.AsEnumerable();
            }

            return GetMountedRemovableDrives().AsEnumerable();
        });
    }

#pragma warning disable CA1416
    private static IEnumerable<StorageDevice> GetPhysicalUsbOrRemovableDisks()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Index, Model, Size, InterfaceType, MediaType, Partitions FROM Win32_DiskDrive");

        foreach (ManagementObject disk in searcher.Get())
        {
            var interfaceType = Convert.ToString(disk["InterfaceType"], CultureInfo.InvariantCulture) ?? string.Empty;
            var mediaType = Convert.ToString(disk["MediaType"], CultureInfo.InvariantCulture) ?? string.Empty;
            var model = Convert.ToString(disk["Model"], CultureInfo.InvariantCulture) ?? "USB Storage Device";
            var physicalPath = Convert.ToString(disk["DeviceID"], CultureInfo.InvariantCulture) ?? string.Empty;
            var index = Convert.ToInt32(disk["Index"], CultureInfo.InvariantCulture);

            if (!LooksLikeExternalRecoverableDisk(interfaceType, mediaType, model))
            {
                continue;
            }

            var logicalVolumes = GetLogicalVolumesForDisk(disk).ToList();
            var firstVolume = logicalVolumes.FirstOrDefault();
            var totalBytes = Convert.ToInt64(disk["Size"] ?? 0, CultureInfo.InvariantCulture);
            var fileSystem = firstVolume?.FileSystem ?? "RAW/Unknown";
            var driveLetter = firstVolume?.DriveLetter ?? string.Empty;
            var freeBytes = firstVolume?.FreeBytes ?? 0;

            yield return new StorageDevice
            {
                DisplayName = BuildDisplayName(index, model, driveLetter),
                DriveLetter = driveLetter,
                PhysicalPath = physicalPath,
                DiskNumber = index,
                InterfaceType = interfaceType,
                MediaType = mediaType,
                TotalBytes = totalBytes,
                FreeBytes = freeBytes,
                FileSystem = fileSystem,
                IsRemovable = true,
                Status = GetStatus(logicalVolumes, fileSystem)
            };
        }
    }

    private static IEnumerable<LogicalVolumeInfo> GetLogicalVolumesForDisk(ManagementObject disk)
    {
        foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
        {
            foreach (ManagementObject logicalDisk in partition.GetRelated("Win32_LogicalDisk"))
            {
                var driveLetter = Convert.ToString(logicalDisk["DeviceID"], CultureInfo.InvariantCulture) ?? string.Empty;
                var fileSystem = Convert.ToString(logicalDisk["FileSystem"], CultureInfo.InvariantCulture) ?? "RAW/Unknown";
                var volumeName = Convert.ToString(logicalDisk["VolumeName"], CultureInfo.InvariantCulture) ?? string.Empty;
                var freeBytes = Convert.ToInt64(logicalDisk["FreeSpace"] ?? 0, CultureInfo.InvariantCulture);

                yield return new LogicalVolumeInfo(driveLetter, fileSystem, volumeName, freeBytes);
            }
        }
    }
#pragma warning restore CA1416

    private static IEnumerable<StorageDevice> GetMountedRemovableDrives()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Removable))
        {
            var driveLetter = drive.Name.TrimEnd('\\');
            yield return new StorageDevice
            {
                DisplayName = GetVolumeLabel(drive),
                DriveLetter = driveLetter,
                PhysicalPath = string.Empty,
                TotalBytes = GetTotalBytes(drive),
                FreeBytes = GetFreeBytes(drive),
                FileSystem = GetFileSystem(drive),
                IsRemovable = true,
                Status = GetDriveStatus(drive)
            };
        }
    }

    private static bool LooksLikeExternalRecoverableDisk(string interfaceType, string mediaType, string model)
    {
        return interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("removable", StringComparison.OrdinalIgnoreCase)
            || model.Contains("USB", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Flash", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase);
    }

    private static DeviceHealthStatus GetStatus(IReadOnlyCollection<LogicalVolumeInfo> volumes, string fileSystem)
    {
        if (volumes.Count == 0)
        {
            return DeviceHealthStatus.Unmounted;
        }

        return fileSystem.Equals("RAW", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("RAW/Unknown", StringComparison.OrdinalIgnoreCase)
            ? DeviceHealthStatus.Raw
            : DeviceHealthStatus.Healthy;
    }

    private static string BuildDisplayName(int diskNumber, string model, string driveLetter)
    {
        var suffix = string.IsNullOrWhiteSpace(driveLetter) ? "No drive letter" : driveLetter;
        return $"Disk {diskNumber} - {model} ({suffix})";
    }

    private static DeviceHealthStatus GetDriveStatus(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return DeviceHealthStatus.Inaccessible;
            }

            return string.IsNullOrEmpty(drive.DriveFormat) || drive.DriveFormat.Equals("RAW", StringComparison.OrdinalIgnoreCase)
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

    private sealed record LogicalVolumeInfo(
        string DriveLetter,
        string FileSystem,
        string VolumeName,
        long FreeBytes);
}
