using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class StorageDeviceIdentityServiceTests
{
    [Fact]
    public void RepresentsSamePhysicalDisk_MatchesDifferentPartitionsOnDiskThree()
    {
        var source = CreateIdentity(3, "USB\\A", @"\\.\PhysicalDrive3", new[] { "E:\\" });
        var destination = source with { MountPoints = new[] { "F:\\" } };

        var match = StorageDeviceIdentityComparer.Compare(source, destination);

        Assert.Equal(DeviceIdentityMatch.Match, match);
    }

    [Fact]
    public void RepresentsSamePhysicalDisk_DistinguishesDifferentPhysicalDisks()
    {
        var source = CreateIdentity(3, "USB\\A", @"\\.\PhysicalDrive3");
        var destination = CreateIdentity(4, "USB\\B", @"\\.\PhysicalDrive4");

        var match = StorageDeviceIdentityComparer.Compare(source, destination);

        Assert.Equal(DeviceIdentityMatch.Different, match);
    }

    [Fact]
    public void RepresentsSamePhysicalDisk_DistinguishesIdenticalModelsWithDifferentPnpIds()
    {
        var first = CreateIdentity(3, "USB\\FIRST", @"\\.\PhysicalDrive3");
        var second = CreateIdentity(4, "USB\\SECOND", @"\\.\PhysicalDrive4");

        var match = StorageDeviceIdentityComparer.Compare(first, second);

        Assert.Equal(DeviceIdentityMatch.Different, match);
    }

    [Fact]
    public void RepresentsSamePhysicalDisk_WorksWithoutSerialWhenPathAndPnpAreStable()
    {
        var first = CreateIdentity(3, "USB\\A", @"\\.\PhysicalDrive3") with { SerialNumber = null };
        var second = first with { MountPoints = new[] { "G:\\" } };

        var match = StorageDeviceIdentityComparer.Compare(first, second);

        Assert.Equal(DeviceIdentityMatch.Match, match);
    }

    [Fact]
    public void RepresentsSamePhysicalDisk_ReturnsIndeterminateForOnlyModelAndCapacity()
    {
        var first = new StorageDeviceIdentity { Model = "Identical USB", CapacityBytes = 8_000_000_000 };
        var second = new StorageDeviceIdentity { Model = "Identical USB", CapacityBytes = 8_000_000_000 };

        var match = StorageDeviceIdentityComparer.Compare(first, second);

        Assert.Equal(DeviceIdentityMatch.Indeterminate, match);
    }

    [Fact]
    public void RepresentsSamePhysicalDisk_RejectsDiskNumberReuseByAnotherPnpDevice()
    {
        var original = CreateIdentity(3, "USB\\DEVICE_A", @"\\.\PhysicalDrive3");
        var replacement = CreateIdentity(3, "USB\\DEVICE_B", @"\\.\PhysicalDrive3");

        var match = StorageDeviceIdentityComparer.Compare(original, replacement);

        Assert.Equal(DeviceIdentityMatch.Different, match);
    }

    [Fact]
    public async Task ResolvePathIdentityAsync_ResolvesMountedFolderToOwningPhysicalDisk()
    {
        var path = CreateTemporaryDirectory();
        try
        {
            var disk = CreateDevice(CreateIdentity(4, "USB\\DEST", @"\\.\PhysicalDrive4"));
            var provider = new FakeWindowsPhysicalDiskProvider([disk], [4]);
            var service = new StorageDeviceIdentityService(provider);

            var identity = await service.ResolvePathIdentityAsync(path, CancellationToken.None);

            Assert.Same(disk.Identity, identity);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public async Task ResolvePathIdentityAsync_ReturnsNullForMultiDiskVolume()
    {
        var path = CreateTemporaryDirectory();
        try
        {
            var provider = new FakeWindowsPhysicalDiskProvider([], [3, 4]);
            var service = new StorageDeviceIdentityService(provider);

            var identity = await service.ResolvePathIdentityAsync(path, CancellationToken.None);

            Assert.Null(identity);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    private static StorageDeviceIdentity CreateIdentity(
        int diskNumber,
        string pnpId,
        string path,
        IReadOnlyList<string>? mountPoints = null)
    {
        return new StorageDeviceIdentity
        {
            PhysicalDiskNumber = diskNumber,
            PhysicalDevicePath = path,
            DeviceInstanceId = pnpId,
            PnpDeviceId = pnpId,
            SerialNumber = $"SERIAL-{pnpId}",
            Model = "Identical USB",
            CapacityBytes = 8_000_000_000,
            BusType = "USB",
            MountPoints = mountPoints ?? Array.Empty<string>()
        };
    }

    private static StorageDevice CreateDevice(StorageDeviceIdentity identity)
    {
        return new StorageDevice
        {
            Identity = identity,
            DiskNumber = identity.PhysicalDiskNumber ?? -1,
            PhysicalPath = identity.PhysicalDevicePath,
            TotalBytes = identity.CapacityBytes,
            IsRemovable = true,
            IsUsbConnected = true
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PendriveRescueIdentity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeWindowsPhysicalDiskProvider : IWindowsPhysicalDiskProvider
    {
        private readonly IReadOnlyList<StorageDevice> _devices;
        private readonly IReadOnlyList<int> _pathDiskNumbers;

        public FakeWindowsPhysicalDiskProvider(
            IReadOnlyList<StorageDevice> devices,
            IReadOnlyList<int> pathDiskNumbers)
        {
            _devices = devices;
            _pathDiskNumbers = pathDiskNumbers;
        }

        public Task<IReadOnlyList<StorageDevice>> GetPhysicalDisksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_devices);
        }

        public Task<IReadOnlyList<int>> ResolvePathDiskNumbersAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(_pathDiskNumbers);
        }
    }
}
