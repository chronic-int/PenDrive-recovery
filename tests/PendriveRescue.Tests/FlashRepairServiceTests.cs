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

    [Fact]
    public void ValidateRepairTarget_RejectsSystemDiskEvenWhenMarkedRemovable()
    {
        var device = new StorageDevice
        {
            DiskNumber = 2,
            IsRemovable = true,
            IsSystemDisk = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE2"
        };

        Assert.Throws<InvalidOperationException>(() => FlashRepairService.ValidateRepairTarget(device));
    }

    [Fact]
    public void BuildRepairScript_PrinterProfileUsesLimitedFat32PartitionOnLargeDrive()
    {
        var device = new StorageDevice
        {
            DiskNumber = 3,
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE3",
            TotalBytes = 64_000_000_000
        };

        var script = FlashRepairService.BuildRepairScript(
            device,
            new FlashRepairOptions
            {
                FileSystem = "fat32",
                LimitFat32PartitionForCompatibility = true
            });

        Assert.Contains("convert mbr", script);
        Assert.Contains("create partition primary size=30000", script);
        Assert.Contains("format fs=fat32 quick", script);
    }

    [Fact]
    public void BuildRepairScript_PrinterProfileUsesFullDriveWhenAlreadyUnderLimit()
    {
        var device = new StorageDevice
        {
            DiskNumber = 3,
            IsRemovable = true,
            PhysicalPath = @"\\.\PHYSICALDRIVE3",
            TotalBytes = 16_000_000_000
        };

        var script = FlashRepairService.BuildRepairScript(
            device,
            new FlashRepairOptions
            {
                FileSystem = "fat32",
                LimitFat32PartitionForCompatibility = true
            });

        Assert.Contains("create partition primary", script);
        Assert.DoesNotContain("create partition primary size=", script);
        Assert.Contains("format fs=fat32 quick", script);
    }
}
