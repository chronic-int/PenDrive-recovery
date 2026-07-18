using System.Diagnostics;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

/// <summary>
/// Performs best-effort non-destructive repair steps for a removable flash drive.
/// It never emits DiskPart commands that erase data, such as clean, format, create partition, or convert.
/// </summary>
public class SafeFlashRepairService : ISafeFlashRepairService
{
    private readonly IStorageDeviceOperationGuard _operationGuard;

    public SafeFlashRepairService(IStorageDeviceOperationGuard operationGuard)
    {
        _operationGuard = operationGuard;
    }

    public async Task<SafeRepairResult> TryRepairAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        var validated = await _operationGuard.RevalidateAsync(
            device,
            StorageOperationKind.SafeRepair,
            cancellationToken);
        device = validated.Device;
        ValidateSafeRepairTarget(device);
        progress.Report(5);

        var output = new StringBuilder();
        var diskPartScript = BuildSafeDiskPartScript(device);
        var diskPartResult = await RunProcessWithInputAsync("diskpart.exe", diskPartScript, cancellationToken);
        output.AppendLine(diskPartResult.Output);
        progress.Report(55);

        if (!string.IsNullOrWhiteSpace(device.DriveLetter))
        {
            // CHKDSK /F modifies file-system metadata in-place, but does not wipe or format the volume.
            var chkdskResult = await RunProcessAsync(
                "chkdsk.exe",
                $"{device.DriveLetter} /F /X",
                cancellationToken);
            output.AppendLine(chkdskResult.Output);
        }
        else
        {
            output.AppendLine("CHKDSK skipped because this disk does not currently have a drive letter.");
        }

        progress.Report(100);
        var combinedOutput = output.ToString();
        return new SafeRepairResult
        {
            TargetIdentity = device.Identity,
            IdentityValidation = validated.Validation,
            Success = !combinedOutput.Contains("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase),
            Message = "Safe repair attempt completed. Refresh devices and check whether Windows mounted the drive.",
            Output = combinedOutput
        };
    }

    /// <summary>
    /// Builds a non-destructive DiskPart script. Tests assert that this script never contains wipe or format commands.
    /// </summary>
    public static string BuildSafeDiskPartScript(StorageDevice device)
    {
        ValidateSafeRepairTarget(device);

        var commands = new List<string>
        {
            $"select disk {device.DiskNumber}",
            "attributes disk clear readonly",
            "online disk",
            "rescan"
        };

        if (string.IsNullOrWhiteSpace(device.DriveLetter))
        {
            commands.AddRange(new[]
            {
                "select partition 1",
                "assign"
            });
        }

        commands.Add("exit");
        commands.Add(string.Empty);
        return string.Join(Environment.NewLine, commands);
    }

    /// <summary>
    /// Prevents safe repair from accidentally targeting the system disk or a non-removable disk.
    /// </summary>
    public static void ValidateSafeRepairTarget(StorageDevice device)
    {
        if (device.DiskNumber <= 0)
        {
            throw new InvalidOperationException("Safe repair is blocked because the selected disk number is invalid or points to Disk 0.");
        }

        if (!device.IsRemovable)
        {
            throw new InvalidOperationException("Safe repair is blocked because the selected device is not marked as removable.");
        }

        if (string.IsNullOrWhiteSpace(device.PhysicalPath))
        {
            throw new InvalidOperationException("Safe repair is blocked because the selected device has no physical disk path.");
        }

        if (device.IsSystemDisk
            || device.IsBootDisk
            || device.ContainsPageFile
            || device.ContainsCrashDump
            || device.ContainsHibernationFile)
        {
            throw new InvalidOperationException("Safe repair is blocked because the selected disk contains protected Windows system data.");
        }
    }

    private static async Task<CommandResult> RunProcessWithInputAsync(string fileName, string input, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        using var process = CreateProcess(fileName, string.Empty, output);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, output.ToString());
    }

    private static async Task<CommandResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        using var process = CreateProcess(fileName, arguments, output);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        return new CommandResult(process.ExitCode, output.ToString());
    }

    private static Process CreateProcess(string fileName, string arguments, StringBuilder output)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

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

        process.StartInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return process;
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
