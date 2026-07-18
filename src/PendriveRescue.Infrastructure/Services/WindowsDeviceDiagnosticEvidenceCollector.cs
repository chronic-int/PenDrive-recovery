using System.Collections;
using System.Globalization;
using System.Management;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public sealed class WindowsDeviceDiagnosticEvidenceCollector : IDeviceDiagnosticEvidenceCollector
{
    private const long CapacityToleranceFloor = 16L * 1024 * 1024;

    public Task<DeviceDiagnosticEvidence> CollectAsync(
        ValidatedStorageDevice device,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Collect(device, cancellationToken), cancellationToken);
    }

#pragma warning disable CA1416
    private static DeviceDiagnosticEvidence Collect(
        ValidatedStorageDevice validated,
        CancellationToken cancellationToken)
    {
        var device = validated.Device;
        var warnings = new List<string>();
        int? partitionCount = null;
        var partitionMetadataAvailable = EvidenceState.Unknown;
        var hasPartitionTable = EvidenceState.Unknown;
        var hasAllocatedPartition = EvidenceState.Unknown;
        var hasUnallocatedCapacity = EvidenceState.Unknown;
        var isReadOnly = EvidenceState.Unknown;
        var isOffline = EvidenceState.Unknown;
        var isNoMedia = EvidenceState.Unknown;
        long? reportedCapacity = device.TotalBytes > 0 ? device.TotalBytes : null;

        cancellationToken.ThrowIfCancellationRequested();
        if (device.DiskNumber >= 0)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(
                    scope,
                    new ObjectQuery(
                        $"SELECT Number, Size, AllocatedSize, LargestFreeExtent, NumberOfPartitions, PartitionStyle, IsReadOnly, IsOffline FROM MSFT_Disk WHERE Number = {device.DiskNumber}"));
                using var results = searcher.Get();
                var disk = results.Cast<ManagementObject>().FirstOrDefault();
                if (disk is null)
                {
                    warnings.Add("Windows disk metadata was unavailable (EvidenceUnavailable).");
                }
                else
                {
                    partitionMetadataAvailable = EvidenceState.Yes;
                    partitionCount = ToNullableInt32(disk["NumberOfPartitions"]);
                    var partitionStyle = ToNullableInt32(disk["PartitionStyle"]);
                    hasPartitionTable = partitionStyle.HasValue
                        ? ToState(partitionStyle.Value != 0)
                        : EvidenceState.Unknown;
                    hasAllocatedPartition = partitionCount.HasValue
                        ? ToState(partitionCount.Value > 0)
                        : EvidenceState.Unknown;
                    var largestFreeExtent = ToNullableInt64(disk["LargestFreeExtent"]);
                    hasUnallocatedCapacity = largestFreeExtent.HasValue
                        ? ToState(largestFreeExtent.Value > CapacityToleranceFloor)
                        : EvidenceState.Unknown;
                    isReadOnly = ToState(disk["IsReadOnly"]);
                    isOffline = ToState(disk["IsOffline"]);
                    reportedCapacity = ToNullableInt64(disk["Size"]) ?? reportedCapacity;
                    isNoMedia = reportedCapacity.HasValue ? ToState(reportedCapacity.Value <= 0) : EvidenceState.Unknown;
                }
            }
            catch (ManagementException)
            {
                warnings.Add("Windows disk and partition metadata could not be read (EvidenceUnavailable).");
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add("Windows disk attributes could not be read (AccessDenied).");
            }
        }
        else
        {
            warnings.Add("Windows did not provide a physical disk number (EvidenceUnavailable).");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var driveLetter = device.DriveLetter.TrimEnd('\\');
        var hasMountedVolume = ToState(!string.IsNullOrWhiteSpace(driveLetter));
        var hasVolume = partitionCount switch
        {
            0 => EvidenceState.No,
            > 0 => EvidenceState.Yes,
            _ => EvidenceState.Unknown
        };
        var volumeMetadataAvailable = EvidenceState.Unknown;
        var volumeAccessible = EvidenceState.Unknown;
        var rootReadable = EvidenceState.Unknown;
        var accessFailure = DiagnosticFailureCategory.None;
        long? volumeCapacity = null;
        long? freeSpace = null;
        var fileSystem = device.FileSystem;
        var volumeLabel = device.VolumeLabel;

        if (!string.IsNullOrWhiteSpace(driveLetter))
        {
            var root = driveLetter + "\\";
            try
            {
                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    volumeMetadataAvailable = EvidenceState.Yes;
                    volumeAccessible = EvidenceState.No;
                    rootReadable = EvidenceState.No;
                    accessFailure = DiagnosticFailureCategory.DeviceNotReady;
                }
                else
                {
                    volumeMetadataAvailable = EvidenceState.Yes;
                    volumeAccessible = EvidenceState.Yes;
                    volumeCapacity = drive.TotalSize;
                    freeSpace = drive.AvailableFreeSpace;
                    fileSystem = drive.DriveFormat;
                    volumeLabel = drive.VolumeLabel;

                    using var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
                    _ = entries.MoveNext();
                    rootReadable = EvidenceState.Yes;
                }
            }
            catch (UnauthorizedAccessException)
            {
                volumeMetadataAvailable = EvidenceState.Yes;
                volumeAccessible = EvidenceState.No;
                rootReadable = EvidenceState.No;
                accessFailure = DiagnosticFailureCategory.AccessDenied;
            }
            catch (IOException ex)
            {
                volumeMetadataAvailable = EvidenceState.Yes;
                volumeAccessible = EvidenceState.No;
                rootReadable = EvidenceState.No;
                accessFailure = CategorizeAccessFailure(ex);
            }
        }
        else if (partitionMetadataAvailable == EvidenceState.Yes)
        {
            // The absence of a mounted drive letter is itself usable volume evidence.
            volumeMetadataAvailable = EvidenceState.Yes;
            volumeAccessible = EvidenceState.No;
        }

        var raw = IsRaw(fileSystem);
        var recognized = string.IsNullOrWhiteSpace(fileSystem)
            ? EvidenceState.Unknown
            : raw
                ? EvidenceState.No
                : ToState(IsRecognizedFileSystem(fileSystem));
        var capacityConsistency = CompareCapacity(
            validated.Device.Identity.CapacityBytes,
            reportedCapacity,
            volumeCapacity,
            out var capacityReason);

        return new DeviceDiagnosticEvidence
        {
            CollectedAtUtc = DateTimeOffset.UtcNow,
            DeviceIdentity = device.Identity,
            DevicePresent = EvidenceState.Yes,
            IdentityRevalidated = EvidenceState.Yes,
            IdentityValidationReason = validated.Validation.Reason,
            IsRemovable = ToState(device.IsRemovable),
            IsSystemDisk = ToState(device.IsSystemDisk),
            IsBootDisk = ToState(device.IsBootDisk),
            PhysicalDiskNumber = device.Identity.PhysicalDiskNumber ?? device.DiskNumber,
            PhysicalPath = device.PhysicalPath,
            Model = device.Identity.Model ?? string.Empty,
            BusType = device.Identity.BusType ?? device.InterfaceType,
            ReportedDiskCapacityBytes = reportedCapacity,
            InitialReportedCapacityBytes = device.Identity.CapacityBytes > 0 ? device.Identity.CapacityBytes : null,
            PartitionCount = partitionCount,
            PartitionMetadataAvailable = partitionMetadataAvailable,
            HasPartitionTable = hasPartitionTable,
            HasAllocatedPartition = hasAllocatedPartition,
            HasUnallocatedCapacity = hasUnallocatedCapacity,
            HasVolume = hasVolume,
            HasMountedVolume = hasMountedVolume,
            DriveLetter = driveLetter,
            MountPoints = device.Identity.MountPoints,
            VolumeLabel = volumeLabel,
            FileSystem = fileSystem,
            VolumeCapacityBytes = volumeCapacity,
            FreeSpaceBytes = freeSpace,
            VolumeMetadataAvailable = volumeMetadataAvailable,
            VolumeAccessible = volumeAccessible,
            RootDirectoryReadable = rootReadable,
            AccessFailureCategory = accessFailure,
            IsReadOnly = isReadOnly,
            ReadOnlyEvidenceSource = isReadOnly == EvidenceState.Unknown ? string.Empty : "Windows disk attribute",
            IsOffline = isOffline,
            IsNoMedia = isNoMedia,
            IsRawFileSystem = ToState(raw),
            FileSystemRecognized = recognized,
            CapacityEvidenceIsConsistent = capacityConsistency,
            CapacityEvidenceReason = capacityReason,
            CollectionWarnings = warnings
        };
    }
#pragma warning restore CA1416

    private static EvidenceState CompareCapacity(
        long identityCapacity,
        long? diskCapacity,
        long? volumeCapacity,
        out string reason)
    {
        var values = new[]
            {
                identityCapacity > 0 ? identityCapacity : (long?)null,
                diskCapacity,
                volumeCapacity
            }
            .Where(value => value is > 0)
            .Select(value => value!.Value)
            .ToArray();
        if (values.Length < 2)
        {
            reason = "Fewer than two independent capacity values were available.";
            return EvidenceState.Unknown;
        }

        var maximum = values.Max();
        var minimum = values.Min();
        var tolerance = Math.Max(CapacityToleranceFloor, maximum / 100);
        if (maximum - minimum > tolerance)
        {
            reason = "Reported disk and volume capacities differ by more than the 1% diagnostic tolerance.";
            return EvidenceState.No;
        }

        reason = "Available disk and volume capacities agree within the 1% diagnostic tolerance.";
        return EvidenceState.Yes;
    }

    private static DiagnosticFailureCategory CategorizeAccessFailure(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code switch
        {
            21 => DiagnosticFailureCategory.DeviceNotReady,
            1112 or 1167 => DiagnosticFailureCategory.DeviceRemoved,
            _ => DiagnosticFailureCategory.IoFailure
        };
    }

    private static bool IsRaw(string fileSystem)
    {
        return fileSystem.Equals("RAW", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("RAW/Unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecognizedFileSystem(string fileSystem)
    {
        return fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("FAT", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("exFAT", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("CDFS", StringComparison.OrdinalIgnoreCase)
            || fileSystem.Equals("UDF", StringComparison.OrdinalIgnoreCase);
    }

    private static EvidenceState ToState(bool value) => value ? EvidenceState.Yes : EvidenceState.No;

    private static EvidenceState ToState(object? value)
    {
        return value is null
            ? EvidenceState.Unknown
            : ToState(Convert.ToBoolean(value, CultureInfo.InvariantCulture));
    }

    private static int? ToNullableInt32(object? value)
    {
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static long? ToNullableInt64(object? value)
    {
        return value is null ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}
