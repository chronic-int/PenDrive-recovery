using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class UsbProtectionServiceTests
{
    [Fact]
    public async Task EnableAsync_CreatesManagedBlockerAndCanRemoveIt()
    {
        var root = CreateDriveRoot();

        try
        {
            var service = new UsbProtectionService(new PassThroughStorageDeviceOperationGuard());
            var device = CreateDevice(root);

            var enabled = await service.EnableAsync(
                device,
                CancellationToken.None,
                new Progress<double>());

            Assert.True(enabled.Success);
            Assert.True(enabled.IsProtected);
            Assert.True(enabled.Changed);
            Assert.True(await service.IsProtectedAsync(device, CancellationToken.None));
            Assert.True(File.Exists(Path.Combine(root, "autorun.inf", ".pendrive-rescue-protection")));

            var disabled = await service.DisableAsync(
                device,
                CancellationToken.None,
                new Progress<double>());

            Assert.True(disabled.Success);
            Assert.False(disabled.IsProtected);
            Assert.True(disabled.Changed);
            Assert.False(Directory.Exists(Path.Combine(root, "autorun.inf")));
        }
        finally
        {
            DeleteDriveRoot(root);
        }
    }

    [Fact]
    public async Task EnableAsync_IsIdempotent()
    {
        var root = CreateDriveRoot();

        try
        {
            var service = new UsbProtectionService(new PassThroughStorageDeviceOperationGuard());
            var device = CreateDevice(root);

            await service.EnableAsync(device, CancellationToken.None, new Progress<double>());
            var secondResult = await service.EnableAsync(device, CancellationToken.None, new Progress<double>());

            Assert.True(secondResult.Success);
            Assert.True(secondResult.IsProtected);
            Assert.False(secondResult.Changed);
            Assert.True(await service.IsProtectedAsync(device, CancellationToken.None));
        }
        finally
        {
            DeleteDriveRoot(root);
        }
    }

    [Fact]
    public async Task EnableAndDisable_DoNotModifyUnmanagedAutorunDirectory()
    {
        var root = CreateDriveRoot();
        var autorunDirectory = Path.Combine(root, "autorun.inf");
        Directory.CreateDirectory(autorunDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(autorunDirectory, ".pendrive-rescue-protection"),
            "Pendrive Rescue AutoRun protection\r\nVersion=1\r\n");
        var existingFile = Path.Combine(autorunDirectory, "existing.txt");
        await File.WriteAllTextAsync(existingFile, "keep me");

        try
        {
            var service = new UsbProtectionService(new PassThroughStorageDeviceOperationGuard());
            var device = CreateDevice(root);

            var enabled = await service.EnableAsync(device, CancellationToken.None, new Progress<double>());
            var disabled = await service.DisableAsync(device, CancellationToken.None, new Progress<double>());

            Assert.False(enabled.Success);
            Assert.Equal(1, enabled.Errors);
            Assert.False(disabled.Success);
            Assert.Equal(1, disabled.Errors);
            Assert.False(disabled.Changed);
            Assert.True(File.Exists(existingFile));
        }
        finally
        {
            DeleteDriveRoot(root);
        }
    }

    [Fact]
    public async Task IsProtectedAsync_RejectsUnmountedDrive()
    {
        var service = new UsbProtectionService(new PassThroughStorageDeviceOperationGuard());
        var device = new StorageDevice
        {
            IsRemovable = true,
            Status = DeviceHealthStatus.Unmounted
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IsProtectedAsync(device, CancellationToken.None));
    }

    private static string CreateDriveRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PendriveRescueUsbProtection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static StorageDevice CreateDevice(string root)
    {
        return new StorageDevice
        {
            DriveLetter = root + Path.DirectorySeparatorChar,
            IsRemovable = true,
            Status = DeviceHealthStatus.Healthy
        };
    }

    private static void DeleteDriveRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(entry, Directory.Exists(entry) ? FileAttributes.Directory : FileAttributes.Normal);
        }

        File.SetAttributes(root, FileAttributes.Directory);
        Directory.Delete(root, recursive: true);
    }
}
