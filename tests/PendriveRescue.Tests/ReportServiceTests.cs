using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class ReportServiceTests
{
    [Fact]
    public async Task ExportReportAsync_RecordsIdentityWithoutExposingSerialNumber()
    {
        var reportPath = Path.Combine(
            Path.GetTempPath(),
            "PendriveRescueReports",
            Guid.NewGuid().ToString("N") + ".json");
        var identity = new StorageDeviceIdentity
        {
            PhysicalDiskNumber = 3,
            PhysicalDevicePath = @"\\.\PhysicalDrive3",
            Model = "Test USB",
            CapacityBytes = 8_000_000_000,
            SerialNumber = "SECRET-SERIAL-NUMBER"
        };
        var result = new ScanResult
        {
            Type = ScanType.Deep,
            SourceDeviceIdentity = identity,
            IdentityValidation = new DeviceIdentityValidation
            {
                OriginalIdentity = identity,
                CurrentIdentity = identity,
                Match = DeviceIdentityMatch.Match,
                Reason = "Identity unchanged."
            }
        };

        try
        {
            var exported = await new ReportService().ExportReportAsync(result, reportPath);
            var report = await File.ReadAllTextAsync(reportPath);

            Assert.True(exported);
            Assert.Contains("SerialNumberHash", report, StringComparison.Ordinal);
            Assert.DoesNotContain(identity.SerialNumber, report, StringComparison.Ordinal);
            Assert.Contains("PhysicalDrive3", report, StringComparison.Ordinal);
        }
        finally
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
