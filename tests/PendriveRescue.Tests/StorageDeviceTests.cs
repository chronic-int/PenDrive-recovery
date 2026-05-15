using PendriveRescue.Domain.Entities;

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
}
