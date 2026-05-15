using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class RunDeepScanUseCase
{
    private readonly IDeepScanService _deepScanService;

    public RunDeepScanUseCase(IDeepScanService deepScanService)
    {
        _deepScanService = deepScanService;
    }

    public Task<ScanResult> ExecuteAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _deepScanService.ScanAsync(device, cancellationToken, progress);
    }
}
