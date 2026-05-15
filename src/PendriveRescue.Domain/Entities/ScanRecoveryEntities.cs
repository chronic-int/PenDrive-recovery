using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Entities;

public class ScanResult
{
    public List<RecoverableFile> FilesFound { get; set; } = new();
    public int Errors { get; set; }
    public TimeSpan Duration { get; set; }
    public ScanType Type { get; set; }
}

public class RecoveryJob
{
    public List<RecoverableFile> SourceFiles { get; set; } = new();
    public string DestinationPath { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public RecoveryState State { get; set; }
}
