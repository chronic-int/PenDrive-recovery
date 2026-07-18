using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public sealed class UsbProtectionService : IUsbProtectionService
{
    private const string AutorunDirectoryName = "autorun.inf";
    private const string MarkerFileName = ".pendrive-rescue-protection";
    private const string MarkerContents = "Pendrive Rescue AutoRun protection\r\nVersion=1\r\n";
    private readonly IStorageDeviceOperationGuard _operationGuard;

    public UsbProtectionService(IStorageDeviceOperationGuard operationGuard)
    {
        _operationGuard = operationGuard;
    }

    public async Task<bool> IsProtectedAsync(StorageDevice device, CancellationToken cancellationToken)
    {
        var validated = await _operationGuard.RevalidateAsync(
            device,
            StorageOperationKind.UsbProtectionRead,
            cancellationToken);
        device = validated.Device;
        ValidateTarget(device);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var autorunDirectory = Path.Combine(GetDriveRoot(device), AutorunDirectoryName);
            return IsManagedProtectionDirectory(autorunDirectory);
        }, cancellationToken);
    }

    public async Task<UsbProtectionResult> EnableAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        var validated = await _operationGuard.RevalidateAsync(
            device,
            StorageOperationKind.UsbProtectionChange,
            cancellationToken);
        device = validated.Device;
        ValidateTarget(device);

        return await Task.Run(() =>
        {
            var result = new UsbProtectionResult
            {
                TargetIdentity = device.Identity,
                IdentityValidation = validated.Validation
            };
            var autorunDirectory = Path.Combine(GetDriveRoot(device), AutorunDirectoryName);
            var markerPath = Path.Combine(autorunDirectory, MarkerFileName);

            progress.Report(10);
            cancellationToken.ThrowIfCancellationRequested();

            var isManagedProtection = IsManagedProtectionDirectory(autorunDirectory);

            if (File.Exists(autorunDirectory))
            {
                result.Errors = 1;
                result.Message = "An autorun.inf file already exists. Normalize the USB before enabling protection.";
                return result;
            }

            if (Directory.Exists(autorunDirectory) && !isManagedProtection)
            {
                result.Errors = 1;
                result.Message = "An unmanaged autorun.inf folder already exists. Normalize the USB before enabling protection.";
                return result;
            }

            try
            {
                var wasProtected = isManagedProtection;
                Directory.CreateDirectory(autorunDirectory);
                cancellationToken.ThrowIfCancellationRequested();

                if (wasProtected)
                {
                    File.SetAttributes(markerPath, FileAttributes.Normal);
                    File.SetAttributes(autorunDirectory, FileAttributes.Directory);
                }

                File.WriteAllText(markerPath, MarkerContents);
                File.SetAttributes(markerPath, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
                File.SetAttributes(
                    autorunDirectory,
                    FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);

                progress.Report(100);
                result.Success = true;
                result.IsProtected = true;
                result.Changed = !wasProtected;
                result.Message = wasProtected
                    ? "AutoRun blocker is already enabled and was refreshed."
                    : "Basic AutoRun blocker enabled. Keep antivirus enabled on every computer you use.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors = 1;
                result.Message = $"AutoRun blocker could not be enabled: {ex.Message}";
            }

            return result;
        }, cancellationToken);
    }

    public async Task<UsbProtectionResult> DisableAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        var validated = await _operationGuard.RevalidateAsync(
            device,
            StorageOperationKind.UsbProtectionChange,
            cancellationToken);
        device = validated.Device;
        ValidateTarget(device);

        return await Task.Run(() =>
        {
            var result = new UsbProtectionResult
            {
                TargetIdentity = device.Identity,
                IdentityValidation = validated.Validation
            };
            var autorunDirectory = Path.Combine(GetDriveRoot(device), AutorunDirectoryName);
            var markerPath = Path.Combine(autorunDirectory, MarkerFileName);

            progress.Report(10);
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(autorunDirectory))
            {
                result.Success = true;
                result.Message = "Managed AutoRun blocker is not enabled on this USB drive.";
                return result;
            }

            if (!IsManagedProtectionDirectory(autorunDirectory))
            {
                result.Errors = 1;
                result.Message = "The autorun.inf folder is not managed by Pendrive Rescue and was left unchanged.";
                return result;
            }

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(autorunDirectory))
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                }

                File.SetAttributes(autorunDirectory, FileAttributes.Directory);
                Directory.Delete(autorunDirectory, recursive: true);

                progress.Report(100);
                result.Success = true;
                result.Changed = true;
                result.Message = "Managed AutoRun blocker removed.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Errors = 1;
                result.IsProtected = true;
                result.Message = $"AutoRun blocker could not be removed: {ex.Message}";
            }

            return result;
        }, cancellationToken);
    }

    private static void ValidateTarget(StorageDevice device)
    {
        UsbMalwareCleanupService.ValidateCleanupTarget(device);
    }

    internal static bool IsManagedProtectionDirectory(string autorunDirectory)
    {
        if (!Directory.Exists(autorunDirectory))
        {
            return false;
        }

        var markerPath = Path.Combine(autorunDirectory, MarkerFileName);
        try
        {
            if (!File.Exists(markerPath) || !File.ReadAllText(markerPath).Equals(MarkerContents, StringComparison.Ordinal))
            {
                return false;
            }

            return Directory
                .EnumerateFileSystemEntries(autorunDirectory)
                .All(entry => entry.Equals(markerPath, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string GetDriveRoot(StorageDevice device)
    {
        var root = device.DriveLetter.EndsWith('\\') ? device.DriveLetter : device.DriveLetter + "\\";
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Drive root was not found: {root}");
        }

        return root;
    }
}
