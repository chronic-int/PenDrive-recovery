using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class RepairFlashDriveUseCase
{
    private readonly IFlashRepairService _flashRepairService;

    public RepairFlashDriveUseCase(IFlashRepairService flashRepairService)
    {
        _flashRepairService = flashRepairService;
    }

    public Task<FlashRepairResult> ExecuteAsync(
        StorageDevice device,
        FlashRepairOptions options,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _flashRepairService.RepairAsync(device, options, cancellationToken, progress);
    }
}
