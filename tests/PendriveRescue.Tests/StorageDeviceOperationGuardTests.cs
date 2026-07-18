using PendriveRescue.Application;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Tests;

public class StorageDeviceOperationGuardTests
{
    [Fact]
    public async Task ValidateRecoveryAsync_BlocksDifferentPartitionOnSourceDisk()
    {
        var source = CreateDevice(3, "USB\\SOURCE", "E:");
        var destination = source.Identity with { MountPoints = new[] { "F:\\" } };
        var guard = CreateGuard(source, destination);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.ValidateRecoveryAsync(
                source,
                SelectDestination(destination),
                false,
                CancellationToken.None));

        Assert.Contains("same physical disk", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateRecoveryAsync_AllowsDifferentPhysicalDisk()
    {
        var source = CreateDevice(3, "USB\\SOURCE", "E:");
        var destination = CreateIdentity(4, "DISK\\DESTINATION");
        var guard = CreateGuard(source, destination);

        var validation = await guard.ValidateRecoveryAsync(
            source,
            SelectDestination(destination),
            false,
            CancellationToken.None);

        Assert.Equal(4, validation.DestinationIdentity.PhysicalDiskNumber);
        Assert.Equal(DeviceIdentityMatch.Match, validation.Source.Validation.Match);
    }

    [Fact]
    public async Task RevalidateAsync_BlocksReassignedDriveLetter()
    {
        var selected = CreateDevice(3, "USB\\SOURCE", "E:");
        var current = CreateDevice(3, "USB\\SOURCE", "F:");
        var guard = CreateGuard(current, current.Identity);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.RevalidateAsync(selected, StorageOperationKind.DeepScan, CancellationToken.None));

        Assert.Contains("changed or is no longer connected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateRecoveryAsync_BlocksMountedSourcePathReassignedToAnotherDisk()
    {
        var selected = CreateDevice(3, "USB\\SOURCE", "E:");
        var replacementIdentity = CreateIdentity(4, "USB\\REPLACEMENT");
        var guard = CreateGuard(selected, replacementIdentity);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.ValidateRecoveryAsync(
                selected,
                SelectDestination(replacementIdentity),
                true,
                CancellationToken.None));

        Assert.Contains("changed or is no longer connected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevalidateAsync_BlocksRemovedDevice()
    {
        var selected = CreateDevice(3, "USB\\SOURCE", "E:");
        var guard = CreateGuard(null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.RevalidateAsync(selected, StorageOperationKind.DeepScan, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateRecoveryAsync_BlocksDestinationIdentityChangedAfterSelection()
    {
        var source = CreateDevice(3, "USB\\SOURCE", "E:");
        var selectedDestination = CreateIdentity(4, "DISK\\DESTINATION_A");
        var currentDestination = CreateIdentity(5, "DISK\\DESTINATION_B");
        var guard = CreateGuard(source, currentDestination);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.ValidateRecoveryAsync(
                source,
                SelectDestination(selectedDestination),
                false,
                CancellationToken.None));

        Assert.Contains("destination has changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateRecoveryAsync_BlocksDestinationDiskNumberChangedAfterSelection()
    {
        var source = CreateDevice(3, "USB\\SOURCE", "E:");
        var selectedDestination = CreateIdentity(4, "DISK\\DESTINATION");
        var currentDestination = CreateIdentity(5, "DISK\\DESTINATION");
        var guard = CreateGuard(source, currentDestination);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.ValidateRecoveryAsync(
                source,
                SelectDestination(selectedDestination),
                false,
                CancellationToken.None));
    }

    [Fact]
    public async Task RevalidateAsync_BlocksPhysicalDiskNumberReuse()
    {
        var selected = CreateDevice(3, "USB\\DEVICE_A", "E:");
        var replacement = CreateDevice(3, "USB\\DEVICE_B", "E:");
        var guard = CreateGuard(replacement, replacement.Identity);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.RevalidateAsync(selected, StorageOperationKind.DestructiveRepair, CancellationToken.None));
    }

    [Fact]
    public async Task RevalidateAsync_BlocksSameDeviceWhenDiskNumberChanges()
    {
        var selected = CreateDevice(3, "USB\\SOURCE", "E:");
        var current = CreateDevice(4, "USB\\SOURCE", "E:");
        var guard = CreateGuard(current, current.Identity);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.RevalidateAsync(selected, StorageOperationKind.DestructiveRepair, CancellationToken.None));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RevalidateAsync_BlocksSystemAndBootDisks(bool isSystem, bool isBoot)
    {
        var selected = CreateDevice(3, "USB\\SOURCE", "E:");
        selected.IsSystemDisk = isSystem;
        selected.IsBootDisk = isBoot;
        var guard = CreateGuard(selected, selected.Identity);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.RevalidateAsync(selected, StorageOperationKind.DestructiveRepair, CancellationToken.None));

        Assert.Contains("used by Windows", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevalidateAsync_BlocksAmbiguousIdentity()
    {
        var identity = new StorageDeviceIdentity { Model = "Generic USB", CapacityBytes = 8_000_000_000 };
        var selected = CreateDevice(identity, "E:");
        var guard = CreateGuard(selected, identity);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.RevalidateAsync(selected, StorageOperationKind.DeepScan, CancellationToken.None));

        Assert.Contains("could not verify", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevalidateAsync_AllowsUnchangedIdentity()
    {
        var selected = CreateDevice(3, "USB\\SOURCE", "E:");
        var guard = CreateGuard(selected, selected.Identity);

        var validated = await guard.RevalidateAsync(
            selected,
            StorageOperationKind.DeepScan,
            CancellationToken.None);

        Assert.Same(selected, validated.Device);
        Assert.Equal(DeviceIdentityMatch.Match, validated.Validation.Match);
    }

    [Fact]
    public async Task ValidateRecoveryAsync_BlocksJunctionResolvingBackToSourceDisk()
    {
        var source = CreateDevice(3, "USB\\SOURCE", "E:");
        var junctionTargetIdentity = source.Identity with { MountPoints = new[] { "C:\\RecoveryLink" } };
        var guard = CreateGuard(source, junctionTargetIdentity);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            guard.ValidateRecoveryAsync(
                source,
                SelectDestination(junctionTargetIdentity),
                false,
                CancellationToken.None));
    }

    private static StorageDeviceOperationGuard CreateGuard(
        StorageDevice? currentDevice,
        StorageDeviceIdentity? pathIdentity)
    {
        return new StorageDeviceOperationGuard(
            new FakeIdentityService(currentDevice, pathIdentity),
            new RecordingAuditService());
    }

    private static RecoveryDestinationSelection SelectDestination(StorageDeviceIdentity identity)
    {
        return new RecoveryDestinationSelection(
            Path.GetTempPath(),
            identity,
            DateTimeOffset.UtcNow);
    }

    private static StorageDevice CreateDevice(int diskNumber, string pnpId, string driveLetter)
    {
        return CreateDevice(CreateIdentity(diskNumber, pnpId), driveLetter);
    }

    private static StorageDevice CreateDevice(StorageDeviceIdentity identity, string driveLetter)
    {
        return new StorageDevice
        {
            Identity = identity,
            DiskNumber = identity.PhysicalDiskNumber ?? -1,
            PhysicalPath = identity.PhysicalDevicePath,
            DriveLetter = driveLetter,
            TotalBytes = identity.CapacityBytes,
            IsRemovable = true,
            IsUsbConnected = true,
            Status = DeviceHealthStatus.Healthy
        };
    }

    private static StorageDeviceIdentity CreateIdentity(int diskNumber, string pnpId)
    {
        return new StorageDeviceIdentity
        {
            PhysicalDiskNumber = diskNumber,
            PhysicalDevicePath = $@"\\.\PhysicalDrive{diskNumber}",
            DeviceInstanceId = pnpId,
            PnpDeviceId = pnpId,
            SerialNumber = $"SERIAL-{pnpId}",
            Model = "Test USB",
            CapacityBytes = 8_000_000_000,
            BusType = "USB"
        };
    }

    private sealed class FakeIdentityService : IStorageDeviceIdentityService
    {
        private readonly StorageDevice? _currentDevice;
        private readonly StorageDeviceIdentity? _pathIdentity;

        public FakeIdentityService(StorageDevice? currentDevice, StorageDeviceIdentity? pathIdentity)
        {
            _currentDevice = currentDevice;
            _pathIdentity = pathIdentity;
        }

        public Task<StorageDevice?> ResolveCurrentDeviceAsync(
            StorageDeviceIdentity identity,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_currentDevice);
        }

        public DeviceIdentityMatch RepresentsSamePhysicalDisk(
            StorageDeviceIdentity first,
            StorageDeviceIdentity second)
        {
            return StorageDeviceIdentityComparer.Compare(first, second);
        }

        public Task<StorageDeviceIdentity?> ResolvePathIdentityAsync(
            string path,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_pathIdentity);
        }
    }

    private sealed class RecordingAuditService : IDeviceSafetyAuditService
    {
        public void RecordValidation(StorageOperationKind operation, DeviceIdentityValidation validation)
        {
        }

        public void RecordDestinationValidation(
            StorageDeviceIdentity source,
            StorageDeviceIdentity? destination,
            DeviceIdentityMatch outcome,
            string reason)
        {
        }
    }
}
