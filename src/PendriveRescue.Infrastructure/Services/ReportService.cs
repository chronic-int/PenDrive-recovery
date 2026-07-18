using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class ReportService : IReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<bool> ExportReportAsync(ScanResult result, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
            var report = new
            {
                ReportType = "Scan",
                CreatedAt = DateTimeOffset.Now,
                result.Type,
                FilesFound = result.FilesFound.Count,
                result.Errors,
                DurationSeconds = Math.Round(result.Duration.TotalSeconds, 2),
                SourceDevice = ToReportIdentity(result.SourceDeviceIdentity),
                IdentityValidation = ToReportValidation(result.IdentityValidation),
                Files = result.FilesFound
            };

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(report, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ExportReportAsync(RecoveryJob job, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
            var report = new
            {
                ReportType = "Recovery",
                CreatedAt = DateTimeOffset.Now,
                job.DestinationPath,
                job.ProgressPercentage,
                job.State,
                SourceDevice = ToReportIdentity(job.SourceDeviceIdentity),
                DestinationDevice = ToReportIdentity(job.DestinationDeviceIdentity),
                IdentityValidation = ToReportValidation(job.IdentityValidation),
                TotalFiles = job.SourceFiles.Count,
                RecoveredFiles = job.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Recovered),
                FailedFiles = job.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Failed),
                PartialFiles = job.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Partial),
                Files = job.SourceFiles
            };

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(report, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? ToReportIdentity(StorageDeviceIdentity? identity)
    {
        if (identity is null)
        {
            return null;
        }

        return new
        {
            identity.PhysicalDiskNumber,
            identity.PhysicalDevicePath,
            identity.Model,
            identity.CapacityBytes,
            identity.BusType,
            SerialNumberHash = HashSerial(identity.SerialNumber),
            identity.VolumeGuidPaths,
            identity.MountPoints
        };
    }

    private static object? ToReportValidation(DeviceIdentityValidation? validation)
    {
        return validation is null
            ? null
            : new
            {
                validation.Match,
                validation.Reason,
                validation.ValidatedAtUtc
            };
    }

    private static string? HashSerial(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serialNumber.Trim()));
        return Convert.ToHexString(hash)[..12];
    }
}
