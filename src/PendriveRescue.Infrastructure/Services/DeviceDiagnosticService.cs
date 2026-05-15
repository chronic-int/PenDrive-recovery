using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

/// <summary>
/// Classifies pendrive problems using only read-only checks. It never formats, repairs, assigns letters, or writes to the device.
/// </summary>
public class DeviceDiagnosticService : IDeviceDiagnosticService
{
    private readonly IRawReadService _rawReadService;

    public DeviceDiagnosticService(IRawReadService rawReadService)
    {
        _rawReadService = rawReadService;
    }

    public async Task<DeviceDiagnosticResult> AnalyzeAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        progress.Report(10);

        var structuralProblem = AnalyzeKnownState(device);
        if (structuralProblem is not null)
        {
            progress.Report(100);
            return structuralProblem;
        }

        progress.Report(45);
        var rawProbe = await TryReadFirstSectorAsync(device, cancellationToken);
        progress.Report(85);

        if (rawProbe is not null)
        {
            progress.Report(100);
            return rawProbe;
        }

        progress.Report(100);
        return new DeviceDiagnosticResult
        {
            ProblemKind = DeviceProblemKind.None,
            Title = "Drive is readable",
            Details = "Windows reports a mounted file system and Pendrive Rescue can read the physical device.",
            Recommendation = "Use Quick Scan for normal files, or Deep Scan if files are missing.",
            CanAttemptSafeRepair = false,
            ShouldUseDeepScan = false
        };
    }

    private static DeviceDiagnosticResult? AnalyzeKnownState(StorageDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.PhysicalPath))
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.MissingPhysicalPath,
                Title = "Physical disk path not available",
                Details = "The device was detected, but Windows did not expose a physical path such as \\\\.\\PHYSICALDRIVE1.",
                Recommendation = "Refresh devices and run the app as Administrator. If it still has no physical path, the controller may not be exposing the disk correctly.",
                IsLikelyPhysicalDamage = true
            };
        }

        if (device.TotalBytes <= 0)
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.SizeUnavailable,
                Title = "Device size is unavailable",
                Details = "Windows can see the device, but cannot report its capacity.",
                Recommendation = "This often points to controller or hardware failure. Try another USB port first; if the size remains unavailable, recovery may not be possible in software.",
                IsLikelyPhysicalDamage = true
            };
        }

        if (device.Status == DeviceHealthStatus.Raw || device.FileSystem.Equals("RAW", StringComparison.OrdinalIgnoreCase))
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.RawFileSystem,
                Title = "RAW or corrupted file system",
                Details = "Windows sees the disk but does not recognize a usable file system.",
                Recommendation = "Run Deep Scan to recover files first. Try Safe Repair only after recovery if you want Windows to mount it again.",
                CanAttemptSafeRepair = true,
                ShouldUseDeepScan = true
            };
        }

        if (string.IsNullOrWhiteSpace(device.DriveLetter) || device.Status == DeviceHealthStatus.Unmounted)
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.MissingDriveLetter,
                Title = "No drive letter assigned",
                Details = "DiskPart can see the physical disk, but Windows has not mounted it as a normal drive.",
                Recommendation = "Try Safe Repair to assign a drive letter without wiping. Use Deep Scan if you need files before repair.",
                CanAttemptSafeRepair = true,
                ShouldUseDeepScan = true
            };
        }

        if (device.Status == DeviceHealthStatus.Inaccessible)
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.InaccessibleVolume,
                Title = "Volume is inaccessible",
                Details = "Windows assigned a drive letter but cannot access the volume normally.",
                Recommendation = "Run Deep Scan first. Try Safe Repair only if you accept CHKDSK metadata changes.",
                CanAttemptSafeRepair = true,
                ShouldUseDeepScan = true
            };
        }

        return null;
    }

    private async Task<DeviceDiagnosticResult?> TryReadFirstSectorAsync(StorageDevice device, CancellationToken cancellationToken)
    {
        try
        {
            var sector = await _rawReadService.ReadBlockAsync(device.PhysicalPath, 0, 512, cancellationToken);
            return sector.Length == 0
                ? new DeviceDiagnosticResult
                {
                    ProblemKind = DeviceProblemKind.PhysicalReadFailure,
                    Title = "Physical read returned no data",
                    Details = "The disk opened but returned no bytes from the first sector.",
                    Recommendation = "Try another USB port. If the same happens, the flash controller or NAND may be damaged.",
                    IsLikelyPhysicalDamage = true,
                    ShouldUseDeepScan = true
                }
                : null;
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.RawReadBlocked,
                Title = "Raw disk read blocked",
                Details = ex.Message,
                Recommendation = "Run the app as Administrator and close tools that may be locking the pendrive.",
                ShouldUseDeepScan = true
            };
        }
        catch (IOException ex)
        {
            return new DeviceDiagnosticResult
            {
                ProblemKind = DeviceProblemKind.PhysicalReadFailure,
                Title = "Physical read failed",
                Details = ex.Message,
                Recommendation = "This may be hardware damage or a controller problem. Try another USB port before attempting recovery again.",
                IsLikelyPhysicalDamage = true,
                ShouldUseDeepScan = true
            };
        }
    }
}
