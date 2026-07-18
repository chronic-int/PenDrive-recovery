using PendriveRescue.Application.UseCases;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Tests;

public class RefreshStorageDevicesUseCaseTests
{
    [Fact]
    public void FindMatchingDevice_FollowsPhysicalDiskWhenDriveLetterChanges()
    {
        var original = CreateDevice(@"\\.\PHYSICALDRIVE4", "E:", 4, DeviceHealthStatus.Healthy);
        var remounted = CreateDevice(@"\\.\PHYSICALDRIVE4", "G:", 4, DeviceHealthStatus.Healthy);
        var unrelated = CreateDevice(@"\\.\PHYSICALDRIVE5", "E:", 5, DeviceHealthStatus.Healthy);

        var match = RefreshStorageDevicesUseCase.FindMatchingDevice(original, [unrelated, remounted]);

        Assert.Same(remounted, match);
    }

    [Fact]
    public void FindMatchingDevice_DoesNotUseStaleLetterWhenPhysicalDiskIsGone()
    {
        var original = CreateDevice(@"\\.\PHYSICALDRIVE4", "E:", 4, DeviceHealthStatus.Healthy);
        var unrelated = CreateDevice(@"\\.\PHYSICALDRIVE5", "E:", 5, DeviceHealthStatus.Healthy);

        var match = RefreshStorageDevicesUseCase.FindMatchingDevice(original, [unrelated]);

        Assert.Null(match);
    }

    [Fact]
    public async Task ExecuteAsync_WaitsForSamePhysicalDiskToBecomeMounted()
    {
        var mountedRoot = Path.Combine(Path.GetTempPath(), "PendriveRescueRemount", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mountedRoot);

        try
        {
            var original = CreateDevice(@"\\.\PHYSICALDRIVE4", "E:", 4, DeviceHealthStatus.Healthy);
            var temporarilyUnmounted = CreateDevice(
                @"\\.\PHYSICALDRIVE4",
                string.Empty,
                4,
                DeviceHealthStatus.Unmounted);
            var remounted = CreateDevice(
                @"\\.\PHYSICALDRIVE4",
                mountedRoot,
                4,
                DeviceHealthStatus.Healthy);
            var detection = new SequencedDetectionService(
                [temporarilyUnmounted],
                [remounted]);
            var useCase = new RefreshStorageDevicesUseCase(detection);

            var result = await useCase.ExecuteAsync(
                original,
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero,
                CancellationToken.None,
                new Progress<double>());

            Assert.Same(remounted, result.MatchedDevice);
            Assert.True(RefreshStorageDevicesUseCase.IsMountedAndReady(result.MatchedDevice));
            Assert.Equal(2, detection.CallCount);
        }
        finally
        {
            Directory.Delete(mountedRoot);
        }
    }

    private static StorageDevice CreateDevice(
        string physicalPath,
        string driveLetter,
        int diskNumber,
        DeviceHealthStatus status)
    {
        return new StorageDevice
        {
            Identity = new StorageDeviceIdentity
            {
                PhysicalDiskNumber = diskNumber,
                PhysicalDevicePath = physicalPath,
                PnpDeviceId = $"TEST\\DISK{diskNumber}",
                DeviceInstanceId = $"TEST\\DISK{diskNumber}",
                Model = "Test USB",
                CapacityBytes = 8_000_000_000
            },
            PhysicalPath = physicalPath,
            DriveLetter = driveLetter,
            DiskNumber = diskNumber,
            IsRemovable = true,
            Status = status
        };
    }

    private sealed class SequencedDetectionService : IDeviceDetectionService
    {
        private readonly Queue<IReadOnlyList<StorageDevice>> _responses;
        private IReadOnlyList<StorageDevice> _lastResponse = [];

        public SequencedDetectionService(params IReadOnlyList<StorageDevice>[] responses)
        {
            _responses = new Queue<IReadOnlyList<StorageDevice>>(responses);
        }

        public int CallCount { get; private set; }

        public Task<IEnumerable<StorageDevice>> GetRemovableDevicesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (_responses.Count > 0)
            {
                _lastResponse = _responses.Dequeue();
            }

            return Task.FromResult(_lastResponse.AsEnumerable());
        }
    }
}
