using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class RecoverFilesUseCase
{
    private readonly IRecoveryService _recoveryService;

    public RecoverFilesUseCase(IRecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
    }

    public Task<RecoveryJob> ExecuteAsync(
        IEnumerable<RecoverableFile> files,
        StorageDevice sourceDevice,
        RecoveryDestinationSelection destination,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return _recoveryService.RecoverFilesAsync(files, sourceDevice, destination, cancellationToken, progress);
    }
}
