using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Enums;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.Infrastructure.Services;

public class RecoveryService : IRecoveryService
{
    private readonly IRawReadService _rawReadService;
    private readonly IStorageDeviceOperationGuard _operationGuard;

    public RecoveryService(
        IRawReadService rawReadService,
        IStorageDeviceOperationGuard operationGuard)
    {
        _rawReadService = rawReadService;
        _operationGuard = operationGuard;
    }

    public async Task<RecoveryJob> RecoverFilesAsync(
        IEnumerable<RecoverableFile> files, 
        StorageDevice sourceDevice, 
        RecoveryDestinationSelection destination,
        CancellationToken cancellationToken, 
        IProgress<double> progress)
    {
        var sourceFiles = files.ToList();
        var validatedTarget = await _operationGuard.ValidateRecoveryAsync(
            sourceDevice,
            destination,
            sourceFiles.Any(file => file.StartOffset == -1),
            cancellationToken);
        sourceDevice = validatedTarget.Source.Device;
        var job = new RecoveryJob
        {
            SourceDeviceIdentity = sourceDevice.Identity,
            DestinationDeviceIdentity = validatedTarget.DestinationIdentity,
            IdentityValidation = validatedTarget.Source.Validation,
            SourceFiles = sourceFiles,
            DestinationPath = destination.Path,
            State = RecoveryState.Pending
        };

        EnsureDestinationHasSpace(destination.Path, job.SourceFiles);

        int totalFiles = job.SourceFiles.Count;
        if (totalFiles == 0)
        {
            job.State = RecoveryState.Recovered;
            progress.Report(100);
            return job;
        }

        int recoveredCount = 0;

        foreach (var file in job.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string targetFilePath = BuildUniqueOutputPath(destination.Path, file);

                if (file.StartOffset == -1)
                {
                    string sourcePath = !string.IsNullOrWhiteSpace(file.SourcePath)
                        ? file.SourcePath
                        : Path.Combine(sourceDevice.DriveLetter + "\\", BuildOutputFileName(file));

                    if (File.Exists(sourcePath))
                    {
                        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        await using var target = new FileStream(targetFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        await source.CopyToAsync(target, cancellationToken);
                        file.State = RecoveryState.Recovered;
                    }
                    else
                    {
                        file.State = RecoveryState.Failed;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(sourceDevice.PhysicalPath))
                    {
                        throw new InvalidOperationException("Cannot recover a carved file because the source device has no physical path.");
                    }

                    // Raw/Carved file recovery
                    await RecoverFromOffsetAsync(sourceDevice.PhysicalPath, file, targetFilePath, cancellationToken);
                    file.State = RecoveryState.Recovered;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                file.State = RecoveryState.Failed;
            }

            recoveredCount++;
            progress.Report((double)recoveredCount / totalFiles * 100);
        }

        job.State = job.SourceFiles.All(f => f.State == RecoveryState.Recovered) 
            ? RecoveryState.Recovered 
            : RecoveryState.Partial;
            
        progress.Report(100);
        return job;
    }

    private static void EnsureDestinationHasSpace(string destinationPath, IReadOnlyCollection<RecoverableFile> files)
    {
        var requiredBytes = files.Where(file => file.SizeBytes > 0).Sum(file => file.SizeBytes);
        if (requiredBytes <= 0)
        {
            return;
        }

        var root = Path.GetPathRoot(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException("The destination drive does not have enough free space for the selected files.");
        }
    }

    private async Task RecoverFromOffsetAsync(string physicalPath, RecoverableFile file, string targetPath, CancellationToken ct)
    {
        // For carved files in MVP, if size is unknown (0), we recover 1MB as a safe bet
        // or until a next signature is found (simplified for MVP).
        long sizeToRecover = file.SizeBytes > 0 ? file.SizeBytes : 1024 * 1024; 

        int bufferSize = 64 * 1024; // 64KB chunks
        using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        
        long bytesRead = 0;
        while (bytesRead < sizeToRecover && !ct.IsCancellationRequested)
        {
            int toRead = (int)Math.Min(bufferSize, sizeToRecover - bytesRead);
            byte[] buffer = await _rawReadService.ReadBlockAsync(physicalPath, file.StartOffset + bytesRead, toRead, ct);
            
            if (buffer.Length == 0) break;

            await fs.WriteAsync(buffer, 0, buffer.Length, ct);
            bytesRead += buffer.Length;
        }
    }

    private static string BuildOutputFileName(RecoverableFile file)
    {
        var fileName = SanitizeFileName(file.FileName);
        var extension = SanitizeExtension(file.Extension);

        if (string.IsNullOrWhiteSpace(extension))
        {
            return fileName;
        }

        return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + extension;
    }

    private static string BuildUniqueOutputPath(string destinationPath, RecoverableFile file)
    {
        var outputFileName = BuildOutputFileName(file);
        var baseName = Path.GetFileNameWithoutExtension(outputFileName);
        var extension = Path.GetExtension(outputFileName);
        var targetFilePath = Path.Combine(destinationPath, outputFileName);
        var suffix = 1;

        while (File.Exists(targetFilePath))
        {
            targetFilePath = Path.Combine(destinationPath, $"{baseName}_{suffix++}{extension}");
        }

        return targetFilePath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName) ? "RecoveredFile" : Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(safeName) ? "RecoveredFile" : safeName;
    }

    private static string SanitizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var safeExtension = extension.StartsWith('.') ? extension : "." + extension;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeExtension = safeExtension.Replace(invalid, '_');
        }

        return safeExtension;
    }
}
