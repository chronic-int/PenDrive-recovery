using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class ProtectUsbDriveUseCase
{
    private readonly IUsbProtectionService _protectionService;

    public ProtectUsbDriveUseCase(IUsbProtectionService protectionService)
    {
        _protectionService = protectionService;
    }

    public Task<bool> IsProtectedAsync(StorageDevice device, CancellationToken cancellationToken)
    {
        return _protectionService.IsProtectedAsync(device, cancellationToken);
    }

    public Task<UsbProtectionResult> EnableAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _protectionService.EnableAsync(device, cancellationToken, progress);
    }

    public Task<UsbProtectionResult> DisableAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _protectionService.DisableAsync(device, cancellationToken, progress);
    }
}
