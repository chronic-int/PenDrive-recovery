using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Tests;

internal static class DiagnosticEvidenceFactory
{
    private static readonly StorageDeviceIdentity Identity = new()
    {
        PhysicalDiskNumber = 2,
        PhysicalDevicePath = @"\\.\PHYSICALDRIVE2",
        PnpDeviceId = "USBSTOR\\TEST",
        SerialNumber = "PRIVATE-SERIAL",
        Model = "Test USB",
        CapacityBytes = 8L * 1024 * 1024 * 1024,
        BusType = "USB"
    };

    public static DeviceDiagnosticEvidence Healthy()
    {
        return new DeviceDiagnosticEvidence
        {
            CollectedAtUtc = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
            DeviceIdentity = Identity,
            DevicePresent = EvidenceState.Yes,
            IdentityRevalidated = EvidenceState.Yes,
            FinalIdentityRevalidated = EvidenceState.Yes,
            IdentityValidationReason = "Identity unchanged.",
            IsRemovable = EvidenceState.Yes,
            IsSystemDisk = EvidenceState.No,
            IsBootDisk = EvidenceState.No,
            PhysicalDiskNumber = 2,
            PhysicalPath = Identity.PhysicalDevicePath,
            Model = Identity.Model!,
            BusType = "USB",
            ReportedDiskCapacityBytes = Identity.CapacityBytes,
            InitialReportedCapacityBytes = Identity.CapacityBytes,
            PartitionCount = 1,
            PartitionMetadataAvailable = EvidenceState.Yes,
            HasPartitionTable = EvidenceState.Yes,
            HasAllocatedPartition = EvidenceState.Yes,
            HasUnallocatedCapacity = EvidenceState.No,
            HasVolume = EvidenceState.Yes,
            HasMountedVolume = EvidenceState.Yes,
            DriveLetter = "E:",
            FileSystem = "exFAT",
            VolumeLabel = "PENDRIVE",
            VolumeCapacityBytes = Identity.CapacityBytes,
            FreeSpaceBytes = 6L * 1024 * 1024 * 1024,
            VolumeMetadataAvailable = EvidenceState.Yes,
            VolumeAccessible = EvidenceState.Yes,
            RootDirectoryReadable = EvidenceState.Yes,
            IsReadOnly = EvidenceState.No,
            IsOffline = EvidenceState.No,
            IsNoMedia = EvidenceState.No,
            IsRawFileSystem = EvidenceState.No,
            FileSystemRecognized = EvidenceState.Yes,
            ReadProbeAttempted = EvidenceState.Yes,
            ReadProbeSucceeded = EvidenceState.Yes,
            ReadProbeBytesRequested = DeviceReadProbeOptions.DefaultBytesToRead,
            ReadProbeBytesCompleted = DeviceReadProbeOptions.DefaultBytesToRead,
            ReadProbeDuration = TimeSpan.FromMilliseconds(30),
            TimedOut = EvidenceState.No,
            SecurityEvidenceCollected = EvidenceState.Yes,
            SuspiciousAutorunDetected = EvidenceState.No,
            SuspiciousShortcutPatternDetected = EvidenceState.No,
            SuspiciousLauncherCount = 0,
            DefenderAvailable = EvidenceState.Yes,
            DefenderThreatRequiresAction = EvidenceState.Unknown,
            CapacityEvidenceIsConsistent = EvidenceState.Yes,
            CapacityEvidenceReason = "Capacity values agree."
        };
    }

    public static DeviceDiagnosticEvidence Raw()
    {
        return Healthy() with
        {
            FileSystem = "RAW",
            IsRawFileSystem = EvidenceState.Yes,
            FileSystemRecognized = EvidenceState.No,
            VolumeAccessible = EvidenceState.No,
            RootDirectoryReadable = EvidenceState.No,
            AccessFailureCategory = DiagnosticFailureCategory.DeviceNotReady
        };
    }
}
