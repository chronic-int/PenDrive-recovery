using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class DeviceDiagnosticServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_ClassifiesRawFileSystem()
    {
        var service = new DeviceDiagnosticService(new FakeRawReadService());
        var device = CreateDevice(DeviceHealthStatus.Raw, driveLetter: "F:", fileSystem: "RAW");

        var result = await service.AnalyzeAsync(device, CancellationToken.None, new Progress<double>());

        Assert.Equal(DeviceProblemKind.RawFileSystem, result.ProblemKind);
        Assert.True(result.ShouldUseDeepScan);
        Assert.True(result.CanAttemptSafeRepair);
    }

    [Fact]
    public async Task AnalyzeAsync_ClassifiesMissingDriveLetter()
    {
        var service = new DeviceDiagnosticService(new FakeRawReadService());
        var device = CreateDevice(DeviceHealthStatus.Unmounted, driveLetter: string.Empty, fileSystem: "RAW/Unknown");

        var result = await service.AnalyzeAsync(device, CancellationToken.None, new Progress<double>());

        Assert.Equal(DeviceProblemKind.MissingDriveLetter, result.ProblemKind);
        Assert.True(result.ShouldUseDeepScan);
    }

    [Fact]
    public async Task AnalyzeAsync_ClassifiesPhysicalReadFailure()
    {
        var service = new DeviceDiagnosticService(new FakeRawReadService(throwOnRead: true));
        var device = CreateDevice(DeviceHealthStatus.Healthy, driveLetter: "F:", fileSystem: "exFAT");

        var result = await service.AnalyzeAsync(device, CancellationToken.None, new Progress<double>());

        Assert.Equal(DeviceProblemKind.PhysicalReadFailure, result.ProblemKind);
        Assert.True(result.IsLikelyPhysicalDamage);
    }

    private static StorageDevice CreateDevice(DeviceHealthStatus status, string driveLetter, string fileSystem)
    {
        return new StorageDevice
        {
            DiskNumber = 1,
            DisplayName = "Test USB",
            DriveLetter = driveLetter,
            FileSystem = fileSystem,
            PhysicalPath = @"\\.\PHYSICALDRIVE1",
            TotalBytes = 32L * 1024 * 1024 * 1024,
            IsRemovable = true,
            Status = status
        };
    }

    private sealed class FakeRawReadService : IRawReadService
    {
        private readonly bool _throwOnRead;

        public FakeRawReadService(bool throwOnRead = false)
        {
            _throwOnRead = throwOnRead;
        }

        public Task<byte[]> ReadBlockAsync(string physicalPath, long offset, int blockSize, CancellationToken cancellationToken)
        {
            if (_throwOnRead)
            {
                throw new IOException("Simulated raw read failure.");
            }

            return Task.FromResult(new byte[blockSize]);
        }
    }
}
