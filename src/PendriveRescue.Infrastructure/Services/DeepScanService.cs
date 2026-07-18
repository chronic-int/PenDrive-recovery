using System.Diagnostics;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class DeepScanService : IDeepScanService
{
    private readonly IRawReadService _rawReadService;
    private readonly FileCarver _fileCarver;
    private readonly IStorageDeviceOperationGuard _operationGuard;

    public DeepScanService(
        IRawReadService rawReadService,
        FileCarver fileCarver,
        IStorageDeviceOperationGuard operationGuard)
    {
        _rawReadService = rawReadService;
        _fileCarver = fileCarver;
        _operationGuard = operationGuard;
    }

    public async Task<ScanResult> ScanAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress)
    {
        var validated = await _operationGuard.RevalidateAsync(device, StorageOperationKind.DeepScan, cancellationToken);
        device = validated.Device;
        var sw = Stopwatch.StartNew();
        var result = new ScanResult
        {
            Type = ScanType.Deep,
            SourceDeviceIdentity = device.Identity,
            IdentityValidation = validated.Validation
        };
        
        long totalBytes = device.TotalBytes;
        if (totalBytes <= 0) return result; // Invalid device size
        if (string.IsNullOrWhiteSpace(device.PhysicalPath))
        {
            result.Errors++;
            return result;
        }

        int blockSize = 64 * 1024; // 64KB chunks
        long currentOffset = 0;

        try
        {
            while (currentOffset < totalBytes && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    byte[] buffer = await _rawReadService.ReadBlockAsync(device.PhysicalPath, currentOffset, blockSize, cancellationToken);
                    var foundFiles = _fileCarver.Carve(buffer, currentOffset);
                    result.FilesFound.AddRange(foundFiles);
                }
                catch (IOException)
                {
                    result.Errors++;
                }

                currentOffset += blockSize;
                double currentProgress = (double)currentOffset / totalBytes * 100;
                progress.Report(Math.Min(100, currentProgress));
            }
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled, return what we have
        }

        sw.Stop();
        result.Duration = sw.Elapsed;
        return result;
    }
}
