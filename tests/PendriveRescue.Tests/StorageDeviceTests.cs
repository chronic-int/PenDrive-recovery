using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Tests;

public class StorageDeviceTests
{
    [Fact]
    public void DriveLetterDisplay_ReturnsNoDriveLetterForUnmountedDisk()
    {
        var device = new StorageDevice
        {
            DriveLetter = string.Empty,
            PhysicalPath = @"\\.\PHYSICALDRIVE1",
            DiskNumber = 1
        };

        Assert.Equal("No drive letter", device.DriveLetterDisplay);
    }

    [Fact]
    public void CapacitySummary_ReportsFreeAndTotalSpace()
    {
        var device = new StorageDevice
        {
            TotalBytes = 8L * 1024 * 1024 * 1024,
            FreeBytes = 6L * 1024 * 1024 * 1024
        };

        Assert.Equal("6 GB free of 8 GB", device.CapacitySummary);
        Assert.Equal(25d, device.UsedSpacePercentage);
    }

    [Fact]
    public void CapacitySummary_ClampsInvalidFreeSpaceValues()
    {
        var device = new StorageDevice
        {
            TotalBytes = 8L * 1024 * 1024 * 1024,
            FreeBytes = 10L * 1024 * 1024 * 1024
        };

        Assert.Equal("8 GB free of 8 GB", device.CapacitySummary);
        Assert.Equal(0d, device.UsedSpacePercentage);
    }

    [Theory]
    [InlineData(DeviceHealthStatus.Healthy, "Ready")]
    [InlineData(DeviceHealthStatus.Raw, "RAW file system")]
    [InlineData(DeviceHealthStatus.Unmounted, "Not mounted")]
    public void HealthStatusDisplay_UsesReadableDescriptions(DeviceHealthStatus status, string expected)
    {
        var device = new StorageDevice { Status = status };

        Assert.Equal(expected, device.HealthStatusDisplay);
    }
}
