using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class DeviceSecurityEvidenceProviderTests
{
    [Fact]
    public async Task CollectAsync_DetectsRootLauncherAndShortcutPatternWithoutRecursion()
    {
        var root = Path.Combine(Path.GetTempPath(), "PendriveSecurityEvidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var hiddenFolder = Path.Combine(root, "Documents");
        Directory.CreateDirectory(hiddenFolder);
        File.SetAttributes(hiddenFolder, FileAttributes.Hidden);
        await File.WriteAllTextAsync(Path.Combine(root, "Documents.lnk"), "shortcut");
        await File.WriteAllTextAsync(Path.Combine(root, "Pictures.lnk"), "shortcut");
        await File.WriteAllTextAsync(Path.Combine(root, "Music.lnk"), "shortcut");
        await File.WriteAllTextAsync(Path.Combine(root, "file.js"), "launcher");
        await File.WriteAllTextAsync(Path.Combine(root, "autorun.inf"), "open=file.js");

        try
        {
            var evidence = await new DeviceSecurityEvidenceProvider().CollectAsync(
                CreateDevice(root),
                CancellationToken.None);

            Assert.Equal(EvidenceState.Yes, evidence.Collected);
            Assert.Equal(EvidenceState.Yes, evidence.SuspiciousAutorunDetected);
            Assert.Equal(EvidenceState.Yes, evidence.SuspiciousShortcutPatternDetected);
            Assert.True(evidence.SuspiciousLauncherCount >= 1);
            Assert.Equal(EvidenceState.Unknown, evidence.DefenderThreatRequiresAction);
        }
        finally
        {
            File.SetAttributes(hiddenFolder, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static ValidatedStorageDevice CreateDevice(string root)
    {
        var identity = new StorageDeviceIdentity
        {
            PhysicalDiskNumber = 2,
            PhysicalDevicePath = @"\\.\PHYSICALDRIVE2",
            PnpDeviceId = "USBSTOR\\TEST",
            Model = "Test USB",
            CapacityBytes = 8_000_000_000
        };
        return new ValidatedStorageDevice(
            new StorageDevice
            {
                Identity = identity,
                DriveLetter = root,
                PhysicalPath = identity.PhysicalDevicePath,
                IsRemovable = true
            },
            new DeviceIdentityValidation
            {
                OriginalIdentity = identity,
                CurrentIdentity = identity,
                Match = DeviceIdentityMatch.Match
            });
    }
}
