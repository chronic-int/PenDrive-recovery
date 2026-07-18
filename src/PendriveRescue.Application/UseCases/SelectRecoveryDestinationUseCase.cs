using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class SelectRecoveryDestinationUseCase
{
    private readonly IStorageDeviceIdentityService _identityService;

    public SelectRecoveryDestinationUseCase(IStorageDeviceIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<RecoveryDestinationSelection> ExecuteAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new InvalidOperationException("Choose an existing recovery destination folder.");
        }

        StorageDeviceIdentity? identity;
        try
        {
            identity = await _identityService.ResolvePathIdentityAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            identity = null;
        }
        if (identity is null)
        {
            throw new InvalidOperationException(
                "Pendrive Rescue could not verify the physical disk for this destination. Choose a folder on a directly attached disk.");
        }

        return new RecoveryDestinationSelection(
            Path.GetFullPath(path),
            identity,
            DateTimeOffset.UtcNow);
    }
}
