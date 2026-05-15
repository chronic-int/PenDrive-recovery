using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class RunQuickScanUseCase
{
    private readonly IQuickScanService _quickScanService;

    public RunQuickScanUseCase(IQuickScanService quickScanService)
    {
        _quickScanService = quickScanService;
    }

    public Task<ScanResult> ExecuteAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _quickScanService.ScanAsync(device, cancellationToken, progress);
    }
}
