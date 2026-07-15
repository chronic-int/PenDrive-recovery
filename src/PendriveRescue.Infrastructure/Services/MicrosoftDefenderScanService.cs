using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public sealed class MicrosoftDefenderScanService : IMalwareScanService
{
    public Task<MalwareScanResult> ScanUsbAsync(
        StorageDevice device,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        UsbMalwareCleanupService.ValidateCleanupTarget(device);
        var root = device.DriveLetter.EndsWith('\\') ? device.DriveLetter : device.DriveLetter + "\\";
        return RunScanAsync(root, isQuickScan: false, cancellationToken, progress);
    }

    public Task<MalwareScanResult> ScanComputerAsync(
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        return RunScanAsync(null, isQuickScan: true, cancellationToken, progress);
    }

    public static ProcessStartInfo BuildScanStartInfo(
        string executablePath,
        string? targetPath,
        bool isQuickScan)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-Scan");
        startInfo.ArgumentList.Add("-ScanType");
        startInfo.ArgumentList.Add(isQuickScan ? "1" : "3");

        if (!isQuickScan)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new ArgumentException("A target path is required for a custom Defender scan.", nameof(targetPath));
            }

            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(targetPath);
        }

        return startInfo;
    }

    public static MalwareScanResult InterpretScanResult(int exitCode, string output)
    {
        return exitCode switch
        {
            0 => new MalwareScanResult
            {
                Available = true,
                Success = true,
                ExitCode = exitCode,
                Message = "Microsoft Defender scan completed with no unresolved threat reported.",
                Output = output
            },
            2 => new MalwareScanResult
            {
                Available = true,
                RequiresAction = true,
                ExitCode = exitCode,
                Message = "Microsoft Defender found malware that still requires action. Open Windows Security and review Protection history.",
                Output = output
            },
            _ => new MalwareScanResult
            {
                Available = true,
                ExitCode = exitCode,
                Message = $"Microsoft Defender scan failed with exit code {exitCode}.",
                Output = output
            }
        };
    }

    private static async Task<MalwareScanResult> RunScanAsync(
        string? targetPath,
        bool isQuickScan,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        var executablePath = FindDefenderExecutable();
        if (executablePath is null)
        {
            return new MalwareScanResult
            {
                Message = "Microsoft Defender command-line scanner is unavailable. Use the active antivirus product before trusting this drive."
            };
        }

        progress.Report(5);
        using var process = new Process
        {
            StartInfo = BuildScanStartInfo(executablePath, targetPath, isQuickScan)
        };

        try
        {
            process.Start();
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            progress.Report(15);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var output = new StringBuilder()
                .AppendLine(await standardOutput)
                .AppendLine(await standardError)
                .ToString()
                .Trim();

            progress.Report(100);
            return InterpretScanResult(process.ExitCode, output);
        }
        catch (Win32Exception ex)
        {
            return new MalwareScanResult
            {
                Message = $"Microsoft Defender could not be started: {ex.Message}"
            };
        }
    }

    private static string? FindDefenderExecutable()
    {
        var platformRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Defender",
            "Platform");

        try
        {
            if (Directory.Exists(platformRoot))
            {
                var latest = Directory
                    .EnumerateDirectories(platformRoot)
                    .Select(directory => Path.Combine(directory, "MpCmdRun.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest is not null)
                {
                    return latest;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to the stable Program Files location.
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender",
            "MpCmdRun.exe");

        return File.Exists(fallback) ? fallback : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
