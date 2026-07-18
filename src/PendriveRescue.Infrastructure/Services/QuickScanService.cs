using System.Diagnostics;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class QuickScanService : IQuickScanService
{
    private readonly IStorageDeviceOperationGuard _operationGuard;

    public QuickScanService(IStorageDeviceOperationGuard operationGuard)
    {
        _operationGuard = operationGuard;
    }

    public async Task<ScanResult> ScanAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress)
    {
        var validated = await _operationGuard.RevalidateAsync(device, StorageOperationKind.QuickScan, cancellationToken);
        device = validated.Device;
        var sw = Stopwatch.StartNew();
        var result = new ScanResult
        {
            Type = ScanType.Quick,
            SourceDeviceIdentity = device.Identity,
            IdentityValidation = validated.Validation
        };

        if (device.Status != DeviceHealthStatus.Healthy || string.IsNullOrWhiteSpace(device.DriveLetter))
        {
            // Quick scan only works on healthy/accessible file systems for MVP.
            // If RAW, it should return empty and user should use Deep Scan.
            return result;
        }

        try
        {
            await Task.Run(() =>
            {
                var root = device.DriveLetter.EndsWith('\\') ? device.DriveLetter : device.DriveLetter + "\\";
                var directoryInfo = new DirectoryInfo(root);
                if (!directoryInfo.Exists)
                {
                    return;
                }

                int currentFile = 0;
                foreach (var file in EnumerateFilesSafe(directoryInfo, result, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    result.FilesFound.Add(new RecoverableFile
                    {
                        FileName = Path.GetFileNameWithoutExtension(file.Name),
                        Extension = file.Extension,
                        SourcePath = file.FullName,
                        SizeBytes = file.Length,
                        Confidence = RecoveryConfidence.High,
                        State = RecoveryState.Pending,
                        StartOffset = -1
                    });

                    currentFile++;
                    progress.Report(Math.Min(95, currentFile % 100));
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            result.Errors++;
        }

        progress.Report(100);
        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(
        DirectoryInfo directory,
        ScanResult result,
        CancellationToken cancellationToken)
    {
        IEnumerable<FileInfo> files;
        try
        {
            files = directory.EnumerateFiles();
        }
        catch
        {
            result.Errors++;
            yield break;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }

        IEnumerable<DirectoryInfo> children;
        try
        {
            children = directory.EnumerateDirectories();
        }
        catch
        {
            result.Errors++;
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var file in EnumerateFilesSafe(child, result, cancellationToken))
            {
                yield return file;
            }
        }
    }
}
