using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application;

/// <summary>
/// Central fail-closed policy for stale selections, disk-number reuse, mounted-path reassignment,
/// unsafe repair targets, and recovery destinations on the source physical disk.
/// </summary>
public sealed class StorageDeviceOperationGuard : IStorageDeviceOperationGuard
{
    public const string DeviceChangedMessage =
        "The selected USB device has changed or is no longer connected. Refresh the device list and select the device again.";

    public const string IdentityIndeterminateMessage =
        "Pendrive Rescue could not verify the physical identity of this disk safely. The operation was cancelled to protect your data.";

    public const string SameDiskDestinationMessage =
        "The selected destination is located on the same physical disk as the source USB device. Choose a folder on a different physical disk to avoid overwriting recoverable data.";

    private readonly IStorageDeviceIdentityService _identityService;
    private readonly IDeviceSafetyAuditService _auditService;

    public StorageDeviceOperationGuard(
        IStorageDeviceIdentityService identityService,
        IDeviceSafetyAuditService auditService)
    {
        _identityService = identityService;
        _auditService = auditService;
    }

    public async Task<ValidatedStorageDevice> RevalidateAsync(
        StorageDevice selectedDevice,
        StorageOperationKind operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedDevice);
        var originalIdentity = selectedDevice.Identity;
        if (_identityService.RepresentsSamePhysicalDisk(originalIdentity, originalIdentity)
            == DeviceIdentityMatch.Indeterminate)
        {
            Block(operation, originalIdentity, null, DeviceIdentityMatch.Indeterminate, "The original identity snapshot is ambiguous.");
            throw new InvalidOperationException(IdentityIndeterminateMessage);
        }

        var currentDevice = await ResolveCurrentDeviceSafelyAsync(originalIdentity, cancellationToken);
        if (currentDevice is null)
        {
            Block(operation, originalIdentity, null, DeviceIdentityMatch.Indeterminate, "The selected physical disk could not be resolved uniquely.");
            throw new InvalidOperationException(DeviceChangedMessage);
        }

        var match = _identityService.RepresentsSamePhysicalDisk(originalIdentity, currentDevice.Identity);
        if (match != DeviceIdentityMatch.Match)
        {
            Block(operation, originalIdentity, currentDevice.Identity, match, "The current disk identity does not match the selection snapshot.");
            throw new InvalidOperationException(match == DeviceIdentityMatch.Indeterminate
                ? IdentityIndeterminateMessage
                : DeviceChangedMessage);
        }

        if (originalIdentity.PhysicalDiskNumber != currentDevice.Identity.PhysicalDiskNumber
            || !SamePath(originalIdentity.PhysicalDevicePath, currentDevice.Identity.PhysicalDevicePath))
        {
            Block(operation, originalIdentity, currentDevice.Identity, DeviceIdentityMatch.Different, "The physical disk number or device path changed after selection.");
            throw new InvalidOperationException(DeviceChangedMessage);
        }

        if (!SameDriveLetter(selectedDevice.DriveLetter, currentDevice.DriveLetter))
        {
            Block(operation, originalIdentity, currentDevice.Identity, DeviceIdentityMatch.Different, "The selected drive letter was removed or reassigned.");
            throw new InvalidOperationException(DeviceChangedMessage);
        }

        if (!currentDevice.IsRemovable && !currentDevice.IsUsbConnected)
        {
            Block(operation, originalIdentity, currentDevice.Identity, DeviceIdentityMatch.Different, "The current target is not classified as removable or USB-connected.");
            throw new InvalidOperationException("The operation was blocked because the selected disk is not a removable USB device.");
        }

        ValidateRepairClassification(currentDevice, operation, originalIdentity);
        if (RequiresMountedPath(operation))
        {
            await ValidateMountedPathAsync(selectedDevice, currentDevice, operation, cancellationToken);
        }

        var validation = new DeviceIdentityValidation
        {
            OriginalIdentity = originalIdentity,
            CurrentIdentity = currentDevice.Identity,
            Match = DeviceIdentityMatch.Match,
            Reason = "The selected physical disk identity was revalidated immediately before the operation."
        };
        _auditService.RecordValidation(operation, validation);
        return new ValidatedStorageDevice(currentDevice, validation);
    }

    public async Task<ValidatedRecoveryTarget> ValidateRecoveryAsync(
        StorageDevice selectedSource,
        RecoveryDestinationSelection destination,
        bool requiresMountedSource,
        CancellationToken cancellationToken)
    {
        var source = await RevalidateAsync(selectedSource, StorageOperationKind.Recovery, cancellationToken);
        if (requiresMountedSource)
        {
            await ValidateMountedPathAsync(
                selectedSource,
                source.Device,
                StorageOperationKind.Recovery,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(destination.Path) || !Directory.Exists(destination.Path))
        {
            _auditService.RecordDestinationValidation(
                source.Device.Identity,
                null,
                DeviceIdentityMatch.Indeterminate,
                "The destination directory does not exist or disappeared before recovery began.");
            throw new InvalidOperationException(
                "Pendrive Rescue could not verify the recovery destination. Choose an existing folder on a different physical disk.");
        }

        var destinationIdentity = await ResolvePathIdentitySafelyAsync(destination.Path, cancellationToken);
        if (destinationIdentity is null)
        {
            _auditService.RecordDestinationValidation(
                source.Device.Identity,
                null,
                DeviceIdentityMatch.Indeterminate,
                "The destination path did not resolve uniquely to one physical disk.");
            throw new InvalidOperationException(
                "Pendrive Rescue could not verify the physical disk for the recovery destination. Choose a folder on a directly attached, different disk.");
        }

        var selectionMatch = _identityService.RepresentsSamePhysicalDisk(
            destination.Identity,
            destinationIdentity);
        if (selectionMatch != DeviceIdentityMatch.Match)
        {
            _auditService.RecordDestinationValidation(
                destination.Identity,
                destinationIdentity,
                selectionMatch,
                "The destination disk identity changed after the folder was selected.");
            throw new InvalidOperationException(selectionMatch == DeviceIdentityMatch.Indeterminate
                ? IdentityIndeterminateMessage
                : "The recovery destination has changed or is no longer connected. Choose the destination folder again.");
        }

        if (destination.Identity.PhysicalDiskNumber != destinationIdentity.PhysicalDiskNumber
            || !SamePath(destination.Identity.PhysicalDevicePath, destinationIdentity.PhysicalDevicePath))
        {
            _auditService.RecordDestinationValidation(
                destination.Identity,
                destinationIdentity,
                DeviceIdentityMatch.Different,
                "The destination physical disk number or device path changed after selection.");
            throw new InvalidOperationException(
                "The recovery destination has changed or is no longer connected. Choose the destination folder again.");
        }

        var destinationMatch = _identityService.RepresentsSamePhysicalDisk(
            source.Device.Identity,
            destinationIdentity);
        _auditService.RecordDestinationValidation(
            source.Device.Identity,
            destinationIdentity,
            destinationMatch,
            destinationMatch == DeviceIdentityMatch.Different
                ? "The destination resolves to a different physical disk."
                : "The destination is the source disk or cannot be distinguished safely.");

        if (destinationMatch == DeviceIdentityMatch.Match)
        {
            throw new InvalidOperationException(SameDiskDestinationMessage);
        }

        if (destinationMatch == DeviceIdentityMatch.Indeterminate)
        {
            throw new InvalidOperationException(IdentityIndeterminateMessage);
        }

        return new ValidatedRecoveryTarget(source, destinationIdentity);
    }

    private async Task ValidateMountedPathAsync(
        StorageDevice selectedDevice,
        StorageDevice currentDevice,
        StorageOperationKind operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedDevice.DriveLetter))
        {
            Block(operation, selectedDevice.Identity, currentDevice.Identity, DeviceIdentityMatch.Indeterminate, "The operation requires a mounted drive letter.");
            throw new InvalidOperationException("The selected USB device is no longer mounted. Refresh the device list and select it again.");
        }

        var root = selectedDevice.DriveLetter.EndsWith(Path.DirectorySeparatorChar)
            ? selectedDevice.DriveLetter
            : selectedDevice.DriveLetter + Path.DirectorySeparatorChar;
        var pathIdentity = await ResolvePathIdentitySafelyAsync(root, cancellationToken);
        if (pathIdentity is null)
        {
            Block(operation, selectedDevice.Identity, null, DeviceIdentityMatch.Indeterminate, "The selected drive letter could not be resolved to one physical disk.");
            throw new InvalidOperationException(DeviceChangedMessage);
        }

        var pathMatch = _identityService.RepresentsSamePhysicalDisk(selectedDevice.Identity, pathIdentity);
        if (pathMatch != DeviceIdentityMatch.Match)
        {
            Block(operation, selectedDevice.Identity, pathIdentity, pathMatch, "The selected drive letter now maps to a different or ambiguous disk.");
            throw new InvalidOperationException(pathMatch == DeviceIdentityMatch.Indeterminate
                ? IdentityIndeterminateMessage
                : DeviceChangedMessage);
        }
    }

    private void ValidateRepairClassification(
        StorageDevice currentDevice,
        StorageOperationKind operation,
        StorageDeviceIdentity originalIdentity)
    {
        if (operation is not StorageOperationKind.SafeRepair and not StorageOperationKind.DestructiveRepair)
        {
            return;
        }

        if (currentDevice.DiskNumber <= 0
            || currentDevice.IsSystemDisk
            || currentDevice.IsBootDisk
            || currentDevice.ContainsPageFile
            || currentDevice.ContainsCrashDump
            || currentDevice.ContainsHibernationFile)
        {
            Block(operation, originalIdentity, currentDevice.Identity, DeviceIdentityMatch.Different, "The target is Disk 0 or contains protected Windows system data.");
            throw new InvalidOperationException(
                "Repair was blocked because this disk is used by Windows for boot, system, paging, crash-dump, or hibernation data.");
        }
    }

    private async Task<StorageDevice?> ResolveCurrentDeviceSafelyAsync(
        StorageDeviceIdentity identity,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _identityService.ResolveCurrentDeviceAsync(identity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<StorageDeviceIdentity?> ResolvePathIdentitySafelyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _identityService.ResolvePathIdentityAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private void Block(
        StorageOperationKind operation,
        StorageDeviceIdentity original,
        StorageDeviceIdentity? current,
        DeviceIdentityMatch match,
        string reason)
    {
        _auditService.RecordValidation(operation, new DeviceIdentityValidation
        {
            OriginalIdentity = original,
            CurrentIdentity = current,
            Match = match,
            Reason = reason
        });
    }

    private static bool RequiresMountedPath(StorageOperationKind operation)
    {
        return operation is StorageOperationKind.QuickScan
            or StorageOperationKind.MalwareScan
            or StorageOperationKind.MalwareCleanup
            or StorageOperationKind.UsbProtectionRead
            or StorageOperationKind.UsbProtectionChange;
    }

    private static bool SameDriveLetter(string first, string second)
    {
        return string.Equals(
            first.TrimEnd('\\'),
            second.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string first, string second)
    {
        return !string.IsNullOrWhiteSpace(first)
            && !string.IsNullOrWhiteSpace(second)
            && first.TrimEnd('\\').Equals(second.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
