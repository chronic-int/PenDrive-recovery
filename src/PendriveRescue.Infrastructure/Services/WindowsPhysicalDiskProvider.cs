using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Infrastructure.Services;

/// <summary>
/// Isolates Windows disk inventory and volume-to-disk resolution so safety policy can be tested without hardware.
/// </summary>
public interface IWindowsPhysicalDiskProvider
{
    Task<IReadOnlyList<StorageDevice>> GetPhysicalDisksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<int>> ResolvePathDiskNumbersAsync(string path, CancellationToken cancellationToken);
}

public sealed class WindowsPhysicalDiskProvider : IWindowsPhysicalDiskProvider
{
    public Task<IReadOnlyList<StorageDevice>> GetPhysicalDisksAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => EnumeratePhysicalDisks(cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<int>> ResolvePathDiskNumbersAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() => WindowsVolumeResolver.ResolveDiskNumbers(path, cancellationToken), cancellationToken);
    }

#pragma warning disable CA1416
    private static IReadOnlyList<StorageDevice> EnumeratePhysicalDisks(CancellationToken cancellationToken)
    {
        var devices = new List<StorageDevice>();
        var pageFileRoots = GetPageFileRoots();
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? string.Empty;

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, Index, Model, Size, InterfaceType, MediaType, PNPDeviceID, SerialNumber FROM Win32_DiskDrive");

        foreach (ManagementObject disk in searcher.Get())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var physicalPath = ReadString(disk, "DeviceID");
            var diskNumber = Convert.ToInt32(disk["Index"], CultureInfo.InvariantCulture);
            var model = ReadString(disk, "Model", "Storage Device");
            var capacity = Convert.ToInt64(disk["Size"] ?? 0, CultureInfo.InvariantCulture);
            var interfaceType = ReadString(disk, "InterfaceType");
            var mediaType = ReadString(disk, "MediaType");
            var pnpDeviceId = NullIfEmpty(ReadString(disk, "PNPDeviceID"));
            var serialNumber = NullIfEmpty(ReadString(disk, "SerialNumber"));
            var volumes = GetLogicalVolumesForDisk(disk).ToList();
            var primaryVolume = volumes.FirstOrDefault();
            var mountPoints = volumes
                .SelectMany(volume => volume.MountPoints)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var volumeGuidPaths = volumes
                .Select(volume => volume.VolumeGuidPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var isUsb = interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase)
                || (pnpDeviceId?.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase) ?? false)
                || model.Contains("USB", StringComparison.OrdinalIgnoreCase)
                || model.Contains("Flash", StringComparison.OrdinalIgnoreCase)
                || model.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase);
            var isRemovable = isUsb
                || mediaType.Contains("removable", StringComparison.OrdinalIgnoreCase);
            var storageFlags = GetStorageFlags(diskNumber);
            var containsSystemVolume = mountPoints.Any(mount =>
                mount.TrimEnd('\\').Equals(systemRoot, StringComparison.OrdinalIgnoreCase));
            var containsPageFile = mountPoints.Any(mount =>
                pageFileRoots.Contains(mount.TrimEnd('\\')));

            var identity = new StorageDeviceIdentity
            {
                PhysicalDiskNumber = diskNumber,
                PhysicalDevicePath = physicalPath,
                DeviceInstanceId = pnpDeviceId,
                PnpDeviceId = pnpDeviceId,
                SerialNumber = serialNumber,
                Model = model,
                CapacityBytes = capacity,
                BusType = interfaceType,
                VolumeGuidPaths = volumeGuidPaths,
                MountPoints = mountPoints
            };

            devices.Add(new StorageDevice
            {
                Identity = identity,
                DisplayName = BuildDisplayName(diskNumber, model, primaryVolume?.DriveLetter),
                VolumeLabel = primaryVolume?.VolumeLabel ?? string.Empty,
                DriveLetter = primaryVolume?.DriveLetter ?? string.Empty,
                PhysicalPath = physicalPath,
                DiskNumber = diskNumber,
                InterfaceType = interfaceType,
                MediaType = mediaType,
                TotalBytes = capacity,
                FreeBytes = primaryVolume?.FreeBytes ?? 0,
                FileSystem = primaryVolume?.FileSystem ?? "RAW/Unknown",
                IsRemovable = isRemovable,
                IsUsbConnected = isUsb,
                IsSystemDisk = storageFlags.IsSystem || containsSystemVolume,
                IsBootDisk = storageFlags.IsBoot || containsSystemVolume,
                ContainsPageFile = containsPageFile,
                ContainsCrashDump = containsSystemVolume,
                ContainsHibernationFile = containsSystemVolume,
                Status = GetStatus(volumes, primaryVolume?.FileSystem ?? "RAW/Unknown")
            });
        }

        return devices;
    }

    private static IEnumerable<LogicalVolumeInfo> GetLogicalVolumesForDisk(ManagementObject disk)
    {
        foreach (ManagementObject partition in disk.GetRelated("Win32_DiskPartition"))
        {
            foreach (ManagementObject logicalDisk in partition.GetRelated("Win32_LogicalDisk"))
            {
                var driveLetter = ReadString(logicalDisk, "DeviceID");
                var root = string.IsNullOrWhiteSpace(driveLetter) ? string.Empty : driveLetter + "\\";
                var volumeGuidPath = WindowsVolumeResolver.TryGetVolumeGuidPath(root);
                var mountPoints = (string.IsNullOrWhiteSpace(volumeGuidPath)
                        ? Array.Empty<string>()
                        : WindowsVolumeResolver.GetVolumeMountPoints(volumeGuidPath))
                    .Append(root)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                yield return new LogicalVolumeInfo(
                    driveLetter,
                    ReadString(logicalDisk, "FileSystem", "RAW/Unknown"),
                    ReadString(logicalDisk, "VolumeName"),
                    Convert.ToInt64(logicalDisk["FreeSpace"] ?? 0, CultureInfo.InvariantCulture),
                    volumeGuidPath,
                    mountPoints);
            }
        }
    }

    private static HashSet<string> GetPageFileRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PageFileUsage");
            foreach (ManagementObject pageFile in searcher.Get())
            {
                var root = Path.GetPathRoot(ReadString(pageFile, "Name"))?.TrimEnd('\\');
                if (!string.IsNullOrWhiteSpace(root))
                {
                    roots.Add(root);
                }
            }
        }
        catch (ManagementException)
        {
        }

        return roots;
    }

    private static DiskStorageFlags GetStorageFlags(int diskNumber)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery($"SELECT IsSystem, IsBoot FROM MSFT_Disk WHERE Number = {diskNumber}"));
            using var results = searcher.Get();
            var disk = results.Cast<ManagementObject>().FirstOrDefault();
            return disk is null
                ? new DiskStorageFlags(false, false)
                : new DiskStorageFlags(
                    Convert.ToBoolean(disk["IsSystem"] ?? false, CultureInfo.InvariantCulture),
                    Convert.ToBoolean(disk["IsBoot"] ?? false, CultureInfo.InvariantCulture));
        }
        catch (ManagementException)
        {
            return new DiskStorageFlags(false, false);
        }
        catch (UnauthorizedAccessException)
        {
            return new DiskStorageFlags(false, false);
        }
    }
#pragma warning restore CA1416

#pragma warning disable CA1416
    private static string ReadString(ManagementBaseObject value, string propertyName, string fallback = "")
    {
        return Convert.ToString(value[propertyName], CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } text
            ? text
            : fallback;
    }
#pragma warning restore CA1416

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

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

    private static string BuildDisplayName(int diskNumber, string model, string? driveLetter)
    {
        var suffix = string.IsNullOrWhiteSpace(driveLetter) ? "No drive letter" : driveLetter;
        return $"Disk {diskNumber} - {model} ({suffix})";
    }

    private sealed record LogicalVolumeInfo(
        string DriveLetter,
        string FileSystem,
        string VolumeLabel,
        long FreeBytes,
        string VolumeGuidPath,
        IReadOnlyList<string> MountPoints);

    private sealed record DiskStorageFlags(bool IsSystem, bool IsBoot);
}

internal static class WindowsVolumeResolver
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    public static IReadOnlyList<int> ResolveDiskNumbers(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return Array.Empty<int>();
        }

        var finalPath = GetFinalDirectoryPath(Path.GetFullPath(path));
        var volumePath = new StringBuilder(1024);
        if (!GetVolumePathName(finalPath, volumePath, (uint)volumePath.Capacity))
        {
            return Array.Empty<int>();
        }

        var volumeName = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPoint(volumePath.ToString(), volumeName, (uint)volumeName.Capacity))
        {
            return Array.Empty<int>();
        }

        var volumeHandlePath = volumeName.ToString().TrimEnd('\\');
        using var handle = CreateFile(
            volumeHandlePath,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Array.Empty<int>();
        }

        var bufferSize = 4096;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (!DeviceIoControl(
                    handle,
                    IoctlVolumeGetVolumeDiskExtents,
                    IntPtr.Zero,
                    0,
                    buffer,
                    bufferSize,
                    out _,
                    IntPtr.Zero))
            {
                return Array.Empty<int>();
            }

            var count = Marshal.ReadInt32(buffer);
            var extentOffset = Marshal.OffsetOf<VolumeDiskExtents>(nameof(VolumeDiskExtents.FirstExtent)).ToInt32();
            var extentSize = Marshal.SizeOf<DiskExtent>();
            var diskNumbers = new List<int>(count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extentPointer = IntPtr.Add(buffer, extentOffset + index * extentSize);
                var extent = Marshal.PtrToStructure<DiskExtent>(extentPointer);
                diskNumbers.Add(unchecked((int)extent.DiskNumber));
            }

            return diskNumbers.Distinct().ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string TryGetVolumeGuidPath(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        var volumeName = new StringBuilder(1024);
        return GetVolumeNameForVolumeMountPoint(root, volumeName, (uint)volumeName.Capacity)
            ? volumeName.ToString()
            : string.Empty;
    }

    public static IReadOnlyList<string> GetVolumeMountPoints(string volumeGuidPath)
    {
        var requiredLength = 0u;
        GetVolumePathNamesForVolumeName(volumeGuidPath, null, 0, ref requiredLength);
        if (requiredLength == 0)
        {
            return Array.Empty<string>();
        }

        var paths = new char[requiredLength];
        if (!GetVolumePathNamesForVolumeName(volumeGuidPath, paths, requiredLength, ref requiredLength))
        {
            return Array.Empty<string>();
        }

        return new string(paths)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string GetFinalDirectoryPath(string path)
    {
        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return path;
        }

        var finalPath = new StringBuilder(4096);
        var length = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
        return length > 0 && length < finalPath.Capacity ? finalPath.ToString() : path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeDiskExtents
    {
        public uint NumberOfDiskExtents;
        public DiskExtent FirstExtent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskExtent
    {
        public uint DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathName(
        string fileName,
        StringBuilder volumePathName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint,
        StringBuilder volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathNamesForVolumeName(
        string volumeName,
        [Out] char[]? volumePathNames,
        uint bufferLength,
        ref uint returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathSize,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
