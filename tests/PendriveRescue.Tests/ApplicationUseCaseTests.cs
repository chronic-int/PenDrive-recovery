using PendriveRescue.Application.UseCases;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Tests;

public class ApplicationUseCaseTests
{
    [Fact]
    public async Task RunQuickScanUseCase_DelegatesToQuickScanService()
    {
        var expected = new ScanResult { Type = ScanType.Quick };
        var service = new FakeQuickScanService(expected);
        var useCase = new RunQuickScanUseCase(service);

        var result = await useCase.ExecuteAsync(new StorageDevice(), CancellationToken.None, new Progress<double>());

        Assert.Same(expected, result);
        Assert.True(service.WasCalled);
    }

    [Fact]
    public async Task RecoverFilesUseCase_DelegatesToRecoveryService()
    {
        var expected = new RecoveryJob { State = RecoveryState.Recovered };
        var service = new FakeRecoveryService(expected);
        var useCase = new RecoverFilesUseCase(service);

        var result = await useCase.ExecuteAsync(
            Array.Empty<RecoverableFile>(),
            new StorageDevice(),
            "C:\\Recovered",
            CancellationToken.None,
            new Progress<double>());

        Assert.Same(expected, result);
        Assert.True(service.WasCalled);
    }

    private sealed class FakeQuickScanService : IQuickScanService
    {
        private readonly ScanResult _result;

        public FakeQuickScanService(ScanResult result)
        {
            _result = result;
        }

        public bool WasCalled { get; private set; }

        public Task<ScanResult> ScanAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<double> progress)
        {
            WasCalled = true;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeRecoveryService : IRecoveryService
    {
        private readonly RecoveryJob _job;

        public FakeRecoveryService(RecoveryJob job)
        {
            _job = job;
        }

        public bool WasCalled { get; private set; }

        public Task<RecoveryJob> RecoverFilesAsync(
            IEnumerable<RecoverableFile> files,
            StorageDevice sourceDevice,
            string destinationPath,
            CancellationToken cancellationToken,
            IProgress<double> progress)
        {
            WasCalled = true;
            return Task.FromResult(_job);
        }
    }
}
