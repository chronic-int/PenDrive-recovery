using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;
using PendriveRescue.Infrastructure.Services;

namespace PendriveRescue.Tests;

public class RecoveryServiceTests
{
    [Fact]
    public async Task RecoverFilesAsync_CopiesLogicalFileUsingSourcePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "PendriveRescueTests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var destinationRoot = Path.Combine(root, "destination");
        var nestedSource = Path.Combine(sourceRoot, "nested");
        Directory.CreateDirectory(nestedSource);
        Directory.CreateDirectory(destinationRoot);

        var sourceFile = Path.Combine(nestedSource, "photo.jpg");
        await File.WriteAllTextAsync(sourceFile, "image bytes");

        try
        {
            var service = new RecoveryService(new FakeRawReadService(), new PassThroughStorageDeviceOperationGuard());
            var file = new RecoverableFile
            {
                FileName = "photo",
                Extension = ".jpg",
                SourcePath = sourceFile,
                StartOffset = -1,
                State = RecoveryState.Pending,
                IsSelected = true
            };

            var job = await service.RecoverFilesAsync(
                new[] { file },
                new StorageDevice { DriveLetter = "Z:" },
                CreateDestination(destinationRoot),
                CancellationToken.None,
                new Progress<double>());

            Assert.Equal(RecoveryState.Recovered, job.State);
            Assert.True(File.Exists(Path.Combine(destinationRoot, "photo.jpg")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecoverFilesAsync_BlocksRecoveryToSourceDrive()
    {
        var service = new RecoveryService(
            new FakeRawReadService(),
            new PassThroughStorageDeviceOperationGuard
            {
                RecoveryException = new InvalidOperationException("Cannot recover files to the source physical disk.")
            });
        var destinationRoot = Path.Combine("Z:" + Path.DirectorySeparatorChar, "Recovered");
        var file = new RecoverableFile
        {
            FileName = "photo",
            Extension = ".jpg",
            StartOffset = -1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecoverFilesAsync(
                new[] { file },
                new StorageDevice { DriveLetter = "Z:" },
                CreateDestination(destinationRoot),
                CancellationToken.None,
                new Progress<double>()));
    }

    [Fact]
    public async Task RecoverFilesAsync_CreatesUniqueSanitizedNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "PendriveRescueTests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var destinationRoot = Path.Combine(root, "destination");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(destinationRoot);

        var sourceFile = Path.Combine(sourceRoot, "photo.jpg");
        await File.WriteAllTextAsync(sourceFile, "image bytes");
        await File.WriteAllTextAsync(Path.Combine(destinationRoot, "bad_name.jpg"), "existing");

        try
        {
            var service = new RecoveryService(new FakeRawReadService(), new PassThroughStorageDeviceOperationGuard());
            var file = new RecoverableFile
            {
                FileName = "bad:name",
                Extension = ".jpg",
                SourcePath = sourceFile,
                StartOffset = -1
            };

            var job = await service.RecoverFilesAsync(
                new[] { file },
                new StorageDevice { DriveLetter = "Z:" },
                CreateDestination(destinationRoot),
                CancellationToken.None,
                new Progress<double>());

            Assert.Equal(RecoveryState.Recovered, job.State);
            Assert.True(File.Exists(Path.Combine(destinationRoot, "bad_name_1.jpg")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecoverFilesAsync_HonorsCancellation()
    {
        var service = new RecoveryService(new FakeRawReadService(), new PassThroughStorageDeviceOperationGuard());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var file = new RecoverableFile
        {
            FileName = "raw",
            Extension = ".bin",
            StartOffset = 0,
            SizeBytes = 1024
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RecoverFilesAsync(
                new[] { file },
                new StorageDevice { DriveLetter = "Z:", PhysicalPath = @"\\.\PHYSICALDRIVE1" },
                CreateDestination(Path.Combine(Path.GetTempPath(), "PendriveRescueTests", Guid.NewGuid().ToString("N"))),
                cts.Token,
                new Progress<double>()));
    }

    private sealed class FakeRawReadService : IRawReadService
    {
        public Task<byte[]> ReadBlockAsync(string physicalPath, long offset, int blockSize, CancellationToken cancellationToken)
        {
            return Task.FromResult(Array.Empty<byte>());
        }
    }

    private static RecoveryDestinationSelection CreateDestination(string path)
    {
        return new RecoveryDestinationSelection(
            path,
            new StorageDeviceIdentity
            {
                PhysicalDiskNumber = 999,
                PhysicalDevicePath = @"\\.\PhysicalDrive999",
                PnpDeviceId = "TEST\\DESTINATION",
                Model = "Test destination",
                CapacityBytes = 1
            },
            DateTimeOffset.UtcNow);
    }
}
