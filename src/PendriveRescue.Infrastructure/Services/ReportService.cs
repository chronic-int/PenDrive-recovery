using System.Text.Json;
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
}
