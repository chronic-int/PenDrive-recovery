using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class AnalyzeDeviceUseCase
{
    private readonly IDeviceDiagnosticService _deviceDiagnosticService;

    public AnalyzeDeviceUseCase(IDeviceDiagnosticService deviceDiagnosticService)
    {
        _deviceDiagnosticService = deviceDiagnosticService;
    }

    public Task<DeviceDiagnosticResult> ExecuteAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<DeviceAnalysisProgress> progress)
    {
        return _deviceDiagnosticService.AnalyzeAsync(device, cancellationToken, progress);
    }
}
