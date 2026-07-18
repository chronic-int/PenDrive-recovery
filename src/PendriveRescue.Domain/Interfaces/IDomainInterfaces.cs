using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Interfaces;

public interface IDeviceDetectionService
{
    Task<IEnumerable<StorageDevice>> GetRemovableDevicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves and compares physical storage identities. Implementations must return
/// <see cref="DeviceIdentityMatch.Indeterminate"/> whenever the available evidence is ambiguous.
/// </summary>
public interface IStorageDeviceIdentityService
{
    /// <summary>Finds the currently connected device represented by an earlier identity snapshot.</summary>
    Task<StorageDevice?> ResolveCurrentDeviceAsync(
        StorageDeviceIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>Compares physical disks without using a drive letter, label, model, or capacity alone.</summary>
    DeviceIdentityMatch RepresentsSamePhysicalDisk(
        StorageDeviceIdentity first,
        StorageDeviceIdentity second);

    /// <summary>Resolves an existing directory, including volume mount points and reparse targets, to its physical disk.</summary>
    Task<StorageDeviceIdentity?> ResolvePathIdentityAsync(
        string path,
        CancellationToken cancellationToken);
}

/// <summary>
/// Applies the central fail-closed policy immediately before disk-sensitive operations.
/// </summary>
public interface IStorageDeviceOperationGuard
{
    /// <summary>Re-enumerates and validates the selected physical disk for the requested operation.</summary>
    Task<ValidatedStorageDevice> RevalidateAsync(
        StorageDevice selectedDevice,
        StorageOperationKind operation,
        CancellationToken cancellationToken);

    /// <summary>Validates the source disk and resolves the final destination directory to a different physical disk.</summary>
    Task<ValidatedRecoveryTarget> ValidateRecoveryAsync(
        StorageDevice selectedSource,
        RecoveryDestinationSelection destination,
        bool requiresMountedSource,
        CancellationToken cancellationToken);
}

/// <summary>Writes privacy-conscious audit records for physical-disk safety decisions.</summary>
public interface IDeviceSafetyAuditService
{
    void RecordValidation(StorageOperationKind operation, DeviceIdentityValidation validation);

    void RecordDestinationValidation(
        StorageDeviceIdentity source,
        StorageDeviceIdentity? destination,
        DeviceIdentityMatch outcome,
        string reason);
}

/// <summary>
/// Performs read-only checks that classify the selected pendrive problem before the user chooses scan or repair actions.
/// </summary>
public interface IDeviceDiagnosticService
{
    Task<DeviceDiagnosticResult> AnalyzeAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<DeviceAnalysisProgress> progress);
}

public interface IDeviceDiagnosticEvidenceCollector
{
    Task<DeviceDiagnosticEvidence> CollectAsync(
        ValidatedStorageDevice device,
        CancellationToken cancellationToken);
}

public interface IDeviceReadProbe
{
    Task<DeviceReadProbeResult> ProbeAsync(
        ValidatedStorageDevice device,
        DeviceReadProbeOptions options,
        CancellationToken cancellationToken);
}

public interface IDeviceSecurityEvidenceProvider
{
    Task<DeviceSecurityEvidence> CollectAsync(
        ValidatedStorageDevice device,
        CancellationToken cancellationToken);
}

public interface IDeviceDiagnosticEngine
{
    DeviceDiagnosticResult Evaluate(
        Guid analysisId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        DeviceDiagnosticEvidence evidence);
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
    Task<RecoveryJob> RecoverFilesAsync(IEnumerable<RecoverableFile> files, StorageDevice sourceDevice, RecoveryDestinationSelection destination, CancellationToken cancellationToken, IProgress<double> progress);
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
    Task<bool> ExportReportAsync(DeviceDiagnosticResult result, string filePath);
}
