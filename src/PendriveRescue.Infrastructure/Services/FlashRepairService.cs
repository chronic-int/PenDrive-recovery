using System.Diagnostics;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

/// <summary>
/// Uses DiskPart to wipe and recreate a single partition on a removable flash drive.
/// This service is intentionally conservative: it refuses disk 0, missing disk numbers, and non-removable targets.
/// </summary>
public class FlashRepairService : IFlashRepairService
{
    private readonly IStorageDeviceOperationGuard _operationGuard;

    public FlashRepairService(IStorageDeviceOperationGuard operationGuard)
    {
        _operationGuard = operationGuard;
    }

    public async Task<FlashRepairResult> RepairAsync(
        StorageDevice device,
        FlashRepairOptions options,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        var validated = await _operationGuard.RevalidateAsync(
            device,
            StorageOperationKind.DestructiveRepair,
            cancellationToken);
        device = validated.Device;
        ValidateRepairTarget(device);
        progress.Report(5);

        var script = BuildRepairScript(device, options);
        progress.Report(15);

        var result = await RunDiskPartAsync(script, cancellationToken);
        result.TargetIdentity = device.Identity;
        result.IdentityValidation = validated.Validation;
        progress.Report(100);

        return result;
    }

    /// <summary>
    /// Builds the exact DiskPart script used for repair. Kept public so tests can prove the destructive command targets one disk only.
    /// </summary>
    public static string BuildRepairScript(StorageDevice device, FlashRepairOptions options)
    {
        ValidateRepairTarget(device);

        var fileSystem = NormalizeFileSystem(options.FileSystem);
        var label = NormalizeLabel(options.Label);
        var quick = options.QuickFormat ? " quick" : string.Empty;
        var limitFat32Partition = fileSystem == "fat32" &&
                                  options.LimitFat32PartitionForCompatibility &&
                                  device.TotalBytes > FlashRepairOptions.Fat32CompatibilityThresholdBytes;
        var createPartition = limitFat32Partition
            ? $"create partition primary size={FlashRepairOptions.Fat32CompatibilityPartitionSizeMegabytes}"
            : "create partition primary";

        return string.Join(Environment.NewLine, new[]
        {
            $"select disk {device.DiskNumber}",
            "attributes disk clear readonly",
            "online disk",
            "clean",
            "convert mbr",
            createPartition,
            $"format fs={fileSystem}{quick} label=\"{label}\"",
            "assign",
            "exit",
            string.Empty
        });
    }

    /// <summary>
    /// Validates the selected disk before any destructive DiskPart command can be generated or executed.
    /// </summary>
    public static void ValidateRepairTarget(StorageDevice device)
    {
        if (device.DiskNumber <= 0)
        {
            throw new InvalidOperationException("Repair is blocked because the selected disk number is invalid or points to Disk 0.");
        }

        if (!device.IsRemovable)
        {
            throw new InvalidOperationException("Repair is blocked because the selected device is not marked as removable.");
        }

        if (string.IsNullOrWhiteSpace(device.PhysicalPath))
        {
            throw new InvalidOperationException("Repair is blocked because the selected device has no physical disk path.");
        }

        if (device.IsSystemDisk
            || device.IsBootDisk
            || device.ContainsPageFile
            || device.ContainsCrashDump
            || device.ContainsHibernationFile)
        {
            throw new InvalidOperationException("Repair is blocked because the selected disk contains protected Windows system data.");
        }
    }

    private static async Task<FlashRepairResult> RunDiskPartAsync(string script, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "diskpart.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);

        var text = output.ToString();
        return new FlashRepairResult
        {
            Success = process.ExitCode == 0 && !text.Contains("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase),
            Message = process.ExitCode == 0 ? "Flash drive repair completed." : $"DiskPart failed with exit code {process.ExitCode}.",
            Output = text
        };
    }

    private static string NormalizeFileSystem(string fileSystem)
    {
        var normalized = fileSystem.Trim().ToLowerInvariant();
        return normalized is "fat32" or "ntfs" or "exfat"
            ? normalized
            : "exfat";
    }

    private static string NormalizeLabel(string label)
    {
        var normalized = new string(label.Where(character => char.IsLetterOrDigit(character) || character is '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "PENDRIVE" : normalized[..Math.Min(normalized.Length, 11)];
    }
}
