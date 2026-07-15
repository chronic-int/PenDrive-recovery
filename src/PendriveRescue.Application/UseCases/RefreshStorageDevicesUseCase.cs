using System.Diagnostics;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Application.UseCases;

public sealed class RefreshStorageDevicesUseCase
{
    private readonly IDeviceDetectionService _deviceDetectionService;

    public RefreshStorageDevicesUseCase(IDeviceDetectionService deviceDetectionService)
    {
        _deviceDetectionService = deviceDetectionService;
    }

    public async Task<StorageDeviceRefreshResult> ExecuteAsync(
        StorageDevice? preferredDevice,
        TimeSpan mountTimeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        IProgress<double> progress)
    {
        if (mountTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(mountTimeout));
        }

        if (pollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        var timer = Stopwatch.StartNew();
        IReadOnlyList<StorageDevice> devices = [];
        StorageDevice? matchedDevice;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            devices = (await _deviceDetectionService.GetRemovableDevicesAsync())
                .OrderBy(device => device.DiskNumber)
                .ThenBy(device => device.DriveLetter)
                .ToList();

            matchedDevice = preferredDevice is null
                ? devices.FirstOrDefault()
                : FindMatchingDevice(preferredDevice, devices);

            if (mountTimeout == TimeSpan.Zero || IsMountedAndReady(matchedDevice))
            {
                progress.Report(100);
                return new StorageDeviceRefreshResult(devices, matchedDevice);
            }

            if (timer.Elapsed >= mountTimeout)
            {
                break;
            }

            progress.Report(Math.Min(95, timer.Elapsed.TotalMilliseconds / mountTimeout.TotalMilliseconds * 100));
            var remaining = mountTimeout - timer.Elapsed;
            var delay = pollInterval <= remaining ? pollInterval : remaining;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
        while (true);

        progress.Report(100);
        return new StorageDeviceRefreshResult(devices, matchedDevice);
    }

    public static StorageDevice? FindMatchingDevice(
        StorageDevice preferredDevice,
        IEnumerable<StorageDevice> candidates)
    {
        var candidateList = candidates.ToList();

        if (!string.IsNullOrWhiteSpace(preferredDevice.PhysicalPath))
        {
            return candidateList.FirstOrDefault(candidate =>
                candidate.PhysicalPath.Equals(preferredDevice.PhysicalPath, StringComparison.OrdinalIgnoreCase));
        }

        if (preferredDevice.DiskNumber >= 0)
        {
            var diskMatch = candidateList.FirstOrDefault(candidate => candidate.DiskNumber == preferredDevice.DiskNumber);
            if (diskMatch is not null)
            {
                return diskMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredDevice.DriveLetter))
        {
            return candidateList.FirstOrDefault(candidate =>
                candidate.DriveLetter.Equals(preferredDevice.DriveLetter, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public static bool IsMountedAndReady(StorageDevice? device)
    {
        if (device is null ||
            string.IsNullOrWhiteSpace(device.DriveLetter) ||
            device.Status is DeviceHealthStatus.Raw or DeviceHealthStatus.Unmounted or DeviceHealthStatus.Inaccessible)
        {
            return false;
        }

        var root = device.DriveLetter.EndsWith(Path.DirectorySeparatorChar)
            ? device.DriveLetter
            : device.DriveLetter + Path.DirectorySeparatorChar;

        try
        {
            return Directory.Exists(root) && new DriveInfo(root).IsReady;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed record StorageDeviceRefreshResult(
    IReadOnlyList<StorageDevice> Devices,
    StorageDevice? MatchedDevice);
