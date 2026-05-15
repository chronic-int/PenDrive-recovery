using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class TrySafeRepairUseCase
{
    private readonly ISafeFlashRepairService _safeFlashRepairService;

    public TrySafeRepairUseCase(ISafeFlashRepairService safeFlashRepairService)
    {
        _safeFlashRepairService = safeFlashRepairService;
    }

    public Task<SafeRepairResult> ExecuteAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _safeFlashRepairService.TryRepairAsync(device, cancellationToken, progress);
    }
}
