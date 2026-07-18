using System.Diagnostics;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public sealed class DeviceReadProbe : IDeviceReadProbe
{
    private readonly IRawReadService _rawReadService;

    public DeviceReadProbe(IRawReadService rawReadService)
    {
        _rawReadService = rawReadService;
    }

    public async Task<DeviceReadProbeResult> ProbeAsync(
        ValidatedStorageDevice device,
        DeviceReadProbeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);
        if (options.BytesToRead <= 0 || options.BlockSize <= 0 || options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Read-probe sizes and timeout must be positive.");
        }

        var physicalPath = device.Device.PhysicalPath;
        if (string.IsNullOrWhiteSpace(physicalPath))
        {
            return new DeviceReadProbeResult
            {
                FailureCategory = DiagnosticFailureCategory.EvidenceUnavailable
            };
        }

        var stopwatch = Stopwatch.StartNew();
        long bytesRead = 0;
        try
        {
            while (bytesRead < options.BytesToRead)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remainingTime = options.Timeout - stopwatch.Elapsed;
                if (remainingTime <= TimeSpan.Zero)
                {
                    return TimedOut(options, bytesRead, stopwatch.Elapsed);
                }

                var blockSize = (int)Math.Min(options.BlockSize, options.BytesToRead - bytesRead);
                var readTask = _rawReadService.ReadBlockAsync(
                    physicalPath,
                    bytesRead,
                    blockSize,
                    cancellationToken);
                byte[] block;
                try
                {
                    block = await readTask.WaitAsync(remainingTime, cancellationToken);
                }
                catch (TimeoutException)
                {
                    ObserveLateFailure(readTask);
                    return TimedOut(options, bytesRead, stopwatch.Elapsed);
                }

                bytesRead += block.Length;
                if (block.Length < blockSize)
                {
                    return new DeviceReadProbeResult
                    {
                        Attempted = true,
                        BytesRequested = options.BytesToRead,
                        BytesRead = bytesRead,
                        Duration = stopwatch.Elapsed,
                        IoErrorCount = 1,
                        FailureCategory = DiagnosticFailureCategory.IoFailure
                    };
                }
            }

            return new DeviceReadProbeResult
            {
                Attempted = true,
                Success = true,
                BytesRequested = options.BytesToRead,
                BytesRead = bytesRead,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(options, bytesRead, stopwatch.Elapsed, DiagnosticFailureCategory.AccessDenied);
        }
        catch (IOException ex)
        {
            var category = CategorizeIoFailure(ex);
            return Failed(
                options,
                bytesRead,
                stopwatch.Elapsed,
                category,
                deviceRemoved: category == DiagnosticFailureCategory.DeviceRemoved);
        }
    }

    private static DeviceReadProbeResult TimedOut(
        DeviceReadProbeOptions options,
        long bytesRead,
        TimeSpan duration)
    {
        return new DeviceReadProbeResult
        {
            Attempted = true,
            BytesRequested = options.BytesToRead,
            BytesRead = bytesRead,
            Duration = duration,
            IoErrorCount = 1,
            TimedOut = true,
            FailureCategory = DiagnosticFailureCategory.Timeout
        };
    }

    private static DeviceReadProbeResult Failed(
        DeviceReadProbeOptions options,
        long bytesRead,
        TimeSpan duration,
        DiagnosticFailureCategory category,
        bool deviceRemoved = false)
    {
        return new DeviceReadProbeResult
        {
            Attempted = true,
            BytesRequested = options.BytesToRead,
            BytesRead = bytesRead,
            Duration = duration,
            IoErrorCount = category == DiagnosticFailureCategory.AccessDenied ? 0 : 1,
            DeviceRemoved = deviceRemoved,
            FailureCategory = category
        };
    }

    private static DiagnosticFailureCategory CategorizeIoFailure(IOException exception)
    {
        var win32Code = exception.HResult & 0xFFFF;
        return win32Code switch
        {
            5 => DiagnosticFailureCategory.AccessDenied,
            21 => DiagnosticFailureCategory.DeviceNotReady,
            1112 or 1167 => DiagnosticFailureCategory.DeviceRemoved,
            _ => DiagnosticFailureCategory.IoFailure
        };
    }

    private static void ObserveLateFailure(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
