using PendriveRescue.Domain.Entities;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class FlashRepairServiceTests
{
    [Fact]
    public void BuildRepairScript_TargetsSelectedRemovableDisk()
    {
        var device = new StorageDevice
        {
            DiskNumber = 1,
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE1"
        };

        var script = FlashRepairService.BuildRepairScript(
            device,
            new FlashRepairOptions { FileSystem = "exfat", Label = "MY USB" });

        Assert.Contains("select disk 1", script);
        Assert.Contains("clean", script);
        Assert.Contains("create partition primary", script);
        Assert.Contains("format fs=exfat quick label=\"MYUSB\"", script);
        Assert.Contains("assign", script);
    }

    [Fact]
    public void ValidateRepairTarget_RejectsDiskZero()
    {
        var device = new StorageDevice
        {
            DiskNumber = 0,
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE0"
        };

        Assert.Throws<InvalidOperationException>(() => FlashRepairService.ValidateRepairTarget(device));
    }

    [Fact]
    public void ValidateRepairTarget_RejectsNonRemovableDisk()
    {
        var device = new StorageDevice
        {
            DiskNumber = 2,
            IsRemovable = false,
            PhysicalPath = @"\\.\PHYSICALDRIVE2"
        };

        Assert.Throws<InvalidOperationException>(() => FlashRepairService.ValidateRepairTarget(device));
    }
}
