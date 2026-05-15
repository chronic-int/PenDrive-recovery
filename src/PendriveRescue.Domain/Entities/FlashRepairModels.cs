namespace PendriveRescue.Domain.Entities;

/// <summary>
/// Options used when rebuilding a removable flash drive with DiskPart.
/// The default format is exFAT because it works well for modern 32 GB and larger USB drives.
/// </summary>
public sealed class FlashRepairOptions
{
    public string FileSystem { get; set; } = "exfat";
    public string Label { get; set; } = "PENDRIVE";
    public bool QuickFormat { get; set; } = true;
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
