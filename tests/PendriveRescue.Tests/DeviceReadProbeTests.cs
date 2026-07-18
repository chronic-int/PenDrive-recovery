using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class DeviceReadProbeTests
{
    [Fact]
    public async Task ProbeAsync_ReadsOnlyConfiguredBoundedRange()
    {
        var raw = new FakeRawReadService();
        var probe = new DeviceReadProbe(raw);
        var options = new DeviceReadProbeOptions
        {
            BytesToRead = 1024 * 1024,
            BlockSize = 256 * 1024,
            Timeout = TimeSpan.FromSeconds(1)
        };

        var result = await probe.ProbeAsync(CreateDevice(), options, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(options.BytesToRead, result.BytesRead);
        Assert.Equal(4, raw.ReadCount);
    }

    [Fact]
    public async Task ProbeAsync_StopsAfterFirstIoFailure()
    {
        var raw = new FakeRawReadService(throwOnRead: true);
        var probe = new DeviceReadProbe(raw);

        var result = await probe.ProbeAsync(
            CreateDevice(),
            new DeviceReadProbeOptions { BytesToRead = 1024, BlockSize = 512 },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticFailureCategory.IoFailure, result.FailureCategory);
        Assert.Equal(1, raw.ReadCount);
    }

    [Fact]
    public async Task ProbeAsync_CategorizesNativeAccessDeniedAsUnavailableEvidence()
    {
        var raw = new FakeRawReadService(win32Error: 5);
        var probe = new DeviceReadProbe(raw);

        var result = await probe.ProbeAsync(
            CreateDevice(),
            new DeviceReadProbeOptions { BytesToRead = 1024, BlockSize = 512 },
            CancellationToken.None);

        Assert.Equal(DiagnosticFailureCategory.AccessDenied, result.FailureCategory);
        Assert.Equal(0, result.IoErrorCount);
        Assert.Equal(1, raw.ReadCount);
    }

    [Fact]
    public async Task ProbeAsync_ReturnsTimeoutWithoutWaitingIndefinitely()
    {
        var raw = new NeverCompletingRawReadService();
        var probe = new DeviceReadProbe(raw);

        var result = await probe.ProbeAsync(
            CreateDevice(),
            new DeviceReadProbeOptions
            {
                BytesToRead = 1024,
                BlockSize = 512,
                Timeout = TimeSpan.FromMilliseconds(40)
            },
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Equal(DiagnosticFailureCategory.Timeout, result.FailureCategory);
    }

    private static ValidatedStorageDevice CreateDevice()
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
                PhysicalPath = identity.PhysicalDevicePath,
                DiskNumber = 2,
                IsRemovable = true
            },
            new DeviceIdentityValidation
            {
                OriginalIdentity = identity,
                CurrentIdentity = identity,
                Match = DeviceIdentityMatch.Match
            });
    }

    private sealed class FakeRawReadService : IRawReadService
    {
        private readonly bool _throwOnRead;
        private readonly int? _win32Error;

        public FakeRawReadService(bool throwOnRead = false, int? win32Error = null)
        {
            _throwOnRead = throwOnRead;
            _win32Error = win32Error;
        }

        public int ReadCount { get; private set; }

        public Task<byte[]> ReadBlockAsync(
            string physicalPath,
            long offset,
            int blockSize,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            if (_win32Error.HasValue)
            {
                throw new IOException(
                    "Simulated native I/O failure.",
                    unchecked((int)(0x80070000u | (uint)_win32Error.Value)));
            }
            if (_throwOnRead)
            {
                throw new IOException("Simulated I/O failure.");
            }
            return Task.FromResult(new byte[blockSize]);
        }
    }

    private sealed class NeverCompletingRawReadService : IRawReadService
    {
        public Task<byte[]> ReadBlockAsync(
            string physicalPath,
            long offset,
            int blockSize,
            CancellationToken cancellationToken)
        {
            return new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}
