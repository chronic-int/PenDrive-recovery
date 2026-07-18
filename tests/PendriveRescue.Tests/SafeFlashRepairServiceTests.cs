using PendriveRescue.Domain.Entities;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class SafeFlashRepairServiceTests
{
    [Fact]
    public void BuildSafeDiskPartScript_DoesNotContainDestructiveCommands()
    {
        var device = new StorageDevice
        {
            DiskNumber = 1,
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE1"
        };

        var script = SafeFlashRepairService.BuildSafeDiskPartScript(device);

        Assert.Contains("select disk 1", script);
        Assert.Contains("attributes disk clear readonly", script);
        Assert.Contains("online disk", script);
        Assert.Contains("assign", script);
        Assert.DoesNotContain("clean", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("format", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create partition", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("convert", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSafeDiskPartScript_SkipsAssignWhenDriveLetterAlreadyExists()
    {
        var device = new StorageDevice
        {
            DiskNumber = 2,
            DriveLetter = "E:",
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE2"
        };

        var script = SafeFlashRepairService.BuildSafeDiskPartScript(device);

        Assert.DoesNotContain("assign", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSafeRepairTarget_RejectsDiskZero()
    {
        var device = new StorageDevice
        {
            DiskNumber = 0,
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE0"
        };

        Assert.Throws<InvalidOperationException>(() => SafeFlashRepairService.ValidateSafeRepairTarget(device));
    }

    [Fact]
    public void ValidateSafeRepairTarget_RejectsBootDiskEvenWhenMarkedRemovable()
    {
        var device = new StorageDevice
        {
            DiskNumber = 2,
            IsRemovable = true,
            IsBootDisk = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE2"
        };

        Assert.Throws<InvalidOperationException>(() => SafeFlashRepairService.ValidateSafeRepairTarget(device));
    }
}
