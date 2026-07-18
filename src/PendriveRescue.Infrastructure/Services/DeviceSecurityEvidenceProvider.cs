using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public sealed class DeviceSecurityEvidenceProvider : IDeviceSecurityEvidenceProvider
{
    private const int MaximumRootEntries = 512;
    private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vbs", ".js", ".jse", ".cmd", ".bat", ".ps1", ".wsf", ".scr"
    };
    private static readonly HashSet<string> SuspiciousNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "usb-explorer.exe", "file.js", "file.vbs", "mayundo.exe"
    };

    public Task<DeviceSecurityEvidence> CollectAsync(
        ValidatedStorageDevice device,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Collect(device.Device, cancellationToken), cancellationToken);
    }

    private static DeviceSecurityEvidence Collect(StorageDevice device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.DriveLetter))
        {
            return new DeviceSecurityEvidence
            {
                Warnings = ["Security metadata inspection requires a mounted readable volume."]
            };
        }

        var root = device.DriveLetter.TrimEnd('\\') + "\\";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = Directory
                .EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Take(MaximumRootEntries)
                .ToArray();
            var directories = Directory
                .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                .Take(MaximumRootEntries)
                .ToArray();
            var autorun = files.Any(path =>
                Path.GetFileName(path).Equals("autorun.inf", StringComparison.OrdinalIgnoreCase));
            var shortcutNames = files
                .Where(path => Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hiddenMatchingDirectories = directories.Count(path =>
                shortcutNames.Contains(Path.GetFileName(path)) && IsHidden(path));
            var suspiciousShortcutPattern = shortcutNames.Count >= 3 && hiddenMatchingDirectories > 0;
            var suspiciousLaunchers = files.Count(path =>
                SuspiciousExtensions.Contains(Path.GetExtension(path))
                || SuspiciousNames.Contains(Path.GetFileName(path)));
            suspiciousLaunchers += directories.Count(path =>
                Path.GetFileName(path).Equals("MAYUNDO", StringComparison.OrdinalIgnoreCase));

            return new DeviceSecurityEvidence
            {
                Collected = EvidenceState.Yes,
                SuspiciousAutorunDetected = ToState(autorun),
                SuspiciousShortcutPatternDetected = ToState(suspiciousShortcutPattern),
                SuspiciousLauncherCount = suspiciousLaunchers,
                DefenderAvailable = ToState(IsDefenderAvailable()),
                DefenderThreatRequiresAction = EvidenceState.Unknown
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Unavailable("USB root security metadata could not be inspected (AccessDenied).");
        }
        catch (IOException)
        {
            return Unavailable("USB root security metadata could not be inspected (IoFailure).");
        }
    }

    private static DeviceSecurityEvidence Unavailable(string warning)
    {
        return new DeviceSecurityEvidence
        {
            DefenderAvailable = ToState(IsDefenderAvailable()),
            Warnings = [warning]
        };
    }

    private static bool IsHidden(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsDefenderAvailable()
    {
        var platformRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Defender",
            "Platform");
        try
        {
            if (Directory.Exists(platformRoot)
                && Directory.EnumerateFiles(platformRoot, "MpCmdRun.exe", SearchOption.AllDirectories).Any())
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender",
            "MpCmdRun.exe"));
    }

    private static EvidenceState ToState(bool value) => value ? EvidenceState.Yes : EvidenceState.No;
}
