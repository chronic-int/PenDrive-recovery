using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Tests;

internal sealed class PassThroughStorageDeviceOperationGuard : IStorageDeviceOperationGuard
{
    public Exception? RecoveryException { get; init; }

    public Task<ValidatedStorageDevice> RevalidateAsync(
        StorageDevice selectedDevice,
        StorageOperationKind operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ValidatedStorageDevice(
            selectedDevice,
            CreateValidation(selectedDevice.Identity)));
    }

    public Task<ValidatedRecoveryTarget> ValidateRecoveryAsync(
        StorageDevice selectedSource,
        RecoveryDestinationSelection destination,
        bool requiresMountedSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RecoveryException is not null)
        {
            return Task.FromException<ValidatedRecoveryTarget>(RecoveryException);
        }

        var source = new ValidatedStorageDevice(
            selectedSource,
            CreateValidation(selectedSource.Identity));
        var destinationIdentity = new StorageDeviceIdentity
        {
            PhysicalDiskNumber = 999,
            PhysicalDevicePath = @"\\.\PhysicalDrive999",
            PnpDeviceId = "TEST\\DESTINATION",
            Model = "Test destination",
            CapacityBytes = 1
        };
        return Task.FromResult(new ValidatedRecoveryTarget(source, destinationIdentity));
    }

    private static DeviceIdentityValidation CreateValidation(StorageDeviceIdentity identity)
    {
        return new DeviceIdentityValidation
        {
            OriginalIdentity = identity,
            CurrentIdentity = identity,
            Match = DeviceIdentityMatch.Match,
            Reason = "Test identity accepted."
        };
    }
}
