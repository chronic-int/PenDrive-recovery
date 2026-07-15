namespace PendriveRescue.Domain.Entities;

/// <summary>
/// Options used when rebuilding a removable flash drive with DiskPart.
/// The default format is exFAT because it works well for modern 32 GB and larger USB drives.
/// </summary>
public sealed class FlashRepairOptions
{
    public const long Fat32CompatibilityThresholdBytes = 32_000_000_000;
    public const int Fat32CompatibilityPartitionSizeMegabytes = 30_000;

    public string FileSystem { get; set; } = "exfat";
    public string Label { get; set; } = "PENDRIVE";
    public bool QuickFormat { get; set; } = true;
    public bool LimitFat32PartitionForCompatibility { get; set; }
}

/// <summary>
/// Captures the outcome and command output from a flash repair attempt.
/// The output is shown to users when Windows refuses to rebuild the selected disk.
/// </summary>
public sealed class FlashRepairResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}

/// <summary>
/// Captures a best-effort non-destructive repair attempt. Partial success is expected when Windows can see the disk but not the file system.
/// </summary>
public sealed class SafeRepairResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}

/// <summary>
/// Options used to undo common USB shortcut-virus changes on a mounted drive.
/// </summary>
public sealed class UsbCleanupOptions
{
    public string[] PayloadFolderNames { get; set; } = ["MAyundo"];
    public string[] SuspiciousRootExecutables { get; set; } = ["Explorer.exe"];
    public string[] SuspiciousRootFiles { get; set; } = ["explorer", "file.js"];
    public string[] SuspiciousRootDirectories { get; set; } = ["autorun.inf", "Explorer"];
    public string[] SuspiciousRootExtensions { get; set; } =
        [".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".cmd", ".bat", ".ps1", ".lnk", ".url", ".scr"];
    public string[] UnsafePayloadExtensions { get; set; } =
        [".exe", ".dll", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".mst", ".jar", ".js", ".jse", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".cmd", ".bat", ".ps1", ".lnk", ".url", ".reg", ".inf"];
    public string[] ProtectedSystemDirectories { get; set; } = ["System Volume Information", "$RECYCLE.BIN"];
    public bool RemoveAutorunInf { get; set; } = true;
    public bool RemoveShortcutFiles { get; set; } = true;
    public bool MovePayloadFolderContentsToRoot { get; set; } = true;
    public bool RestoreNormalAttributes { get; set; } = true;
    public bool QuarantineSuspiciousFiles { get; set; } = true;
    public string? QuarantineRootPath { get; set; }
    public bool RemoveAllDriveContents { get; set; }
}

/// <summary>
/// Captures the result of normalising a mounted USB drive after shortcut-virus style infection.
/// </summary>
public sealed class UsbCleanupResult
{
    public bool Success { get; set; }
    public int FilesMoved { get; set; }
    public int ItemsDeleted { get; set; }
    public int ItemsQuarantined { get; set; }
    public int SuspiciousItemsFound { get; set; }
    public int SuspiciousItemsRemaining { get; set; }
    public int AttributesRestored { get; set; }
    public int SystemFoldersHidden { get; set; }
    public int Errors { get; set; }
    public string QuarantinePath { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Captures the result of adding or removing the managed AutoRun blocker on a mounted USB drive.
/// This is a basic defence against common USB malware patterns, not a replacement for endpoint antivirus.
/// </summary>
public sealed class UsbProtectionResult
{
    public bool Success { get; set; }
    public bool IsProtected { get; set; }
    public bool Changed { get; set; }
    public int Errors { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Captures the outcome of a Microsoft Defender scan requested by the application.
/// </summary>
public sealed class MalwareScanResult
{
    public bool Available { get; set; }
    public bool Success { get; set; }
    public bool RequiresAction { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
}
