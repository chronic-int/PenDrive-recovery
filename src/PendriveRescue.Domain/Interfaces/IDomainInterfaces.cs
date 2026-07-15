using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Interfaces;

public interface IDeviceDetectionService
{
    Task<IEnumerable<StorageDevice>> GetRemovableDevicesAsync();
}

/// <summary>
/// Performs read-only checks that classify the selected pendrive problem before the user chooses scan or repair actions.
/// </summary>
public interface IDeviceDiagnosticService
{
    Task<DeviceDiagnosticResult> AnalyzeAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress);
}

public interface IRawReadService
{
    Task<byte[]> ReadBlockAsync(string physicalPath, long offset, int blockSize, CancellationToken cancellationToken);
}

public interface IQuickScanService
{
    Task<ScanResult> ScanAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress);
}

public interface IDeepScanService
{
    Task<ScanResult> ScanAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress);
}

public interface IRecoveryService
{
    Task<RecoveryJob> RecoverFilesAsync(IEnumerable<RecoverableFile> files, StorageDevice sourceDevice, string destinationPath, CancellationToken cancellationToken, IProgress<double> progress);
}

/// <summary>
/// Rebuilds a removable flash drive so Windows can mount it again after file recovery is no longer needed.
/// Implementations are destructive and must validate the target before touching the device.
/// </summary>
public interface IFlashRepairService
{
    Task<FlashRepairResult> RepairAsync(StorageDevice device, FlashRepairOptions options, CancellationToken cancellationToken, IProgress<double> progress);
}

/// <summary>
/// Attempts mount and file-system repairs without wiping, formatting, or recreating partitions.
/// This is best-effort: it can help with read-only flags, missing drive letters, and CHKDSK-repairable file-system errors.
/// </summary>
public interface ISafeFlashRepairService
{
    Task<SafeRepairResult> TryRepairAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress);
}

/// <summary>
/// Removes common shortcut-virus artefacts from a mounted USB drive and restores normal file visibility.
/// </summary>
public interface IUsbMalwareCleanupService
{
    Task<UsbCleanupResult> CleanAsync(
        StorageDevice device,
        UsbCleanupOptions options,
        CancellationToken cancellationToken,
        IProgress<double> progress);
}

/// <summary>
/// Manages a removable-drive AutoRun blocker that prevents an autorun.inf file from being created by common USB malware.
/// </summary>
public interface IUsbProtectionService
{
    Task<bool> IsProtectedAsync(StorageDevice device, CancellationToken cancellationToken);

    Task<UsbProtectionResult> EnableAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress);

    Task<UsbProtectionResult> DisableAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress);
}

/// <summary>
/// Runs Microsoft Defender scans without executing any content from the selected USB drive.
/// </summary>
public interface IMalwareScanService
{
    Task<MalwareScanResult> ScanUsbAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress);

    Task<MalwareScanResult> ScanComputerAsync(
        CancellationToken cancellationToken,
        IProgress<double> progress);
}

public interface IReportService
{
    Task<bool> ExportReportAsync(ScanResult result, string filePath);
    Task<bool> ExportReportAsync(RecoveryJob job, string filePath);
}
