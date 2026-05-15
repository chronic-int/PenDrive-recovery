using System.ComponentModel;
using System.Runtime.CompilerServices;
using PendriveRescue.Domain.Enums;

namespace PendriveRescue.Domain.Entities;

public class StorageDevice
{
    public string DisplayName { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public int DiskNumber { get; set; } = -1;
    public string InterfaceType { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public string FileSystem { get; set; } = string.Empty;
    public DeviceHealthStatus Status { get; set; }
    public bool IsRemovable { get; set; }
    public string DriveLetterDisplay => string.IsNullOrWhiteSpace(DriveLetter) ? "No drive letter" : DriveLetter;
    public string TotalSizeDisplay => FormatBytes(TotalBytes);
    public string FreeSizeDisplay => FormatBytes(FreeBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "-";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}

public class RecoverableFile : INotifyPropertyChanged
{
    private RecoveryState state;
    private bool isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long StartOffset { get; set; }
    public RecoveryConfidence Confidence { get; set; }

    public RecoveryState State
    {
        get => state;
        set => SetField(ref state, value);
    }

    public bool IsSelected
    {
        get => isSelected;
        set => SetField(ref isSelected, value);
    }

    public string SizeDisplay => SizeBytes <= 0 ? "Unknown" : StorageDeviceSizeFormatter.Format(SizeBytes);

    private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal static class StorageDeviceSizeFormatter
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
