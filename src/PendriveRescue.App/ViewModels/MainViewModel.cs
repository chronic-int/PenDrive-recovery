using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PendriveRescue.App.Services;
using PendriveRescue.Application.UseCases;
using PendriveRescue.Domain.Entities;
using PendriveRescue.Domain.Interfaces;

namespace PendriveRescue.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDeviceDetectionService _deviceDetectionService;
    private readonly AnalyzeDeviceUseCase _analyzeDeviceUseCase;
    private readonly RunQuickScanUseCase _runQuickScanUseCase;
    private readonly RunDeepScanUseCase _runDeepScanUseCase;
    private readonly RecoverFilesUseCase _recoverFilesUseCase;
    private readonly RepairFlashDriveUseCase _repairFlashDriveUseCase;
    private readonly TrySafeRepairUseCase _trySafeRepairUseCase;
    private readonly ExportReportUseCase _exportReportUseCase;
    private readonly IFolderPicker _folderPicker;
    private CancellationTokenSource? _operationCts;
    private ScanResult? _lastScanResult;
    private RecoveryJob? _lastRecoveryJob;

    public MainViewModel(
        IDeviceDetectionService deviceDetectionService,
        AnalyzeDeviceUseCase analyzeDeviceUseCase,
        RunQuickScanUseCase runQuickScanUseCase,
        RunDeepScanUseCase runDeepScanUseCase,
        RecoverFilesUseCase recoverFilesUseCase,
        RepairFlashDriveUseCase repairFlashDriveUseCase,
        TrySafeRepairUseCase trySafeRepairUseCase,
        ExportReportUseCase exportReportUseCase,
        IFolderPicker folderPicker)
    {
        _deviceDetectionService = deviceDetectionService;
        _analyzeDeviceUseCase = analyzeDeviceUseCase;
        _runQuickScanUseCase = runQuickScanUseCase;
        _runDeepScanUseCase = runDeepScanUseCase;
        _recoverFilesUseCase = recoverFilesUseCase;
        _repairFlashDriveUseCase = repairFlashDriveUseCase;
        _trySafeRepairUseCase = trySafeRepairUseCase;
        _exportReportUseCase = exportReportUseCase;
        _folderPicker = folderPicker;

        _ = RefreshDevicesAsync();
    }

    public ObservableCollection<StorageDevice> Devices { get; } = new();

    public ObservableCollection<RecoverableFile> Files { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(QuickScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeProblemCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeepScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TrySafeRepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairFlashDriveCommand))]
    private StorageDevice? selectedDevice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecoverSelectedCommand))]
    private string destinationPath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    [NotifyCanExecuteChangedFor(nameof(QuickScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeProblemCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeepScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecoverFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(TrySafeRepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepairFlashDriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isBusy;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string statusMessage = "Ready. Select a removable drive to begin.";

    [ObservableProperty]
    private string operationTitle = "Idle";

    public int FoundCount => Files.Count;

    public int SelectedFileCount => Files.Count(file => file.IsSelected);

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool HasFiles => Files.Count > 0;

    partial void OnSelectedDeviceChanged(StorageDevice? value)
    {
        ClearFiles();
        _lastScanResult = null;
        Progress = 0;
        StatusMessage = value is null
            ? "Select a removable drive to begin."
            : $"Selected {value.DisplayName}. Use Deep Scan for RAW, inaccessible, or no-letter devices.";
        OnPropertyChanged(nameof(FoundCount));
        OnPropertyChanged(nameof(SelectedFileCount));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(HasFiles));
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task RefreshDevicesAsync()
    {
        await RunOperationAsync("Refreshing devices", async ct =>
        {
            Devices.Clear();
            ClearFiles();
            var devices = await _deviceDetectionService.GetRemovableDevicesAsync();
            foreach (var device in devices.OrderBy(device => device.DiskNumber).ThenBy(device => device.DriveLetter))
            {
                Devices.Add(device);
            }

            SelectedDevice ??= Devices.FirstOrDefault();
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(HasFiles));
            StatusMessage = Devices.Count == 0
                ? "No removable drives detected."
                : $"Detected {Devices.Count} removable drive(s).";
        });
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task AnalyzeProblemAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunOperationAsync("Analyzing pendrive", async ct =>
        {
            var result = await _analyzeDeviceUseCase.ExecuteAsync(
                SelectedDevice,
                ct,
                new Progress<double>(value => Progress = value));

            StatusMessage = $"{result.Title}. {result.Recommendation}";
            System.Windows.MessageBox.Show(
                BuildDiagnosticMessage(result),
                "Pendrive diagnosis",
                MessageBoxButton.OK,
                result.IsLikelyPhysicalDamage ? MessageBoxImage.Warning : MessageBoxImage.Information);
        });
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task QuickScanAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        if (SelectedDevice.Status != Domain.Enums.DeviceHealthStatus.Healthy || string.IsNullOrWhiteSpace(SelectedDevice.DriveLetter))
        {
            StatusMessage = "Quick scan requires a mounted readable drive. Use Deep Scan for this device.";
            return;
        }

        await RunScanAsync("Quick scan", token =>
            _runQuickScanUseCase.ExecuteAsync(SelectedDevice, token, new Progress<double>(value => Progress = value)));
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task DeepScanAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedDevice.PhysicalPath))
        {
            StatusMessage = "This device has no physical path. Refresh devices or run the app as Administrator.";
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            "Deep scan reads the device sequentially and may take a long time. Do not remove the pendrive during the scan.",
            "Start deep scan",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        await RunScanAsync("Deep scan", token =>
            _runDeepScanUseCase.ExecuteAsync(SelectedDevice, token, new Progress<double>(value => Progress = value)));
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _operationCts?.Cancel();
        StatusMessage = "Cancelling current operation...";
    }

    [RelayCommand]
    private void BrowseDestination()
    {
        var selectedPath = _folderPicker.PickFolder("Choose a destination folder on a different drive");
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DestinationPath = selectedPath;
        }
    }

    [RelayCommand]
    private void SelectAllFiles()
    {
        foreach (var file in Files)
        {
            file.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedFileCount));
        RecoverSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearFileSelection()
    {
        foreach (var file in Files)
        {
            file.IsSelected = false;
        }

        OnPropertyChanged(nameof(SelectedFileCount));
        RecoverSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRecover))]
    private async Task RecoverSelectedAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var selectedFiles = Files.Where(file => file.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            StatusMessage = "Select one or more files to recover.";
            return;
        }

        await RunOperationAsync("Recovering files", async ct =>
        {
            var progress = new Progress<double>(value => Progress = value);
            _lastRecoveryJob = await _recoverFilesUseCase.ExecuteAsync(
                selectedFiles,
                SelectedDevice,
                DestinationPath,
                ct,
                progress);

            var recovered = _lastRecoveryJob.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Recovered);
            StatusMessage = $"Recovery completed: {recovered}/{_lastRecoveryJob.SourceFiles.Count} file(s) recovered.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanRecoverFiles))]
    private async Task RecoverFilesAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var filesToRecover = Files.Where(file => file.IsSelected).ToList();
        if (filesToRecover.Count == 0)
        {
            var answer = System.Windows.MessageBox.Show(
                "No files are selected. Recover all found files?",
                "Recover files",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.OK)
            {
                return;
            }

            filesToRecover = Files.ToList();
        }

        var targetFolder = _folderPicker.PickFolder("Choose where recovered files should be saved");
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            StatusMessage = "Recovery cancelled because no destination folder was selected.";
            return;
        }

        DestinationPath = targetFolder;
        await RecoverFilesToDestinationAsync(filesToRecover, targetFolder);
    }

    [RelayCommand(CanExecute = nameof(CanRepairFlashDrive))]
    private async Task RepairFlashDriveAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var firstWarning = System.Windows.MessageBox.Show(
            "Repair Flash Drive will erase the selected pendrive, recreate one partition, format it as exFAT, and assign a drive letter. Recover files first if you still need data from this device.",
            "Erase and repair flash drive",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (firstWarning != MessageBoxResult.OK)
        {
            return;
        }

        var confirmationText = $"REPAIR DISK {SelectedDevice.DiskNumber}";
        var typedText = Microsoft.VisualBasic.Interaction.InputBox(
            $"Type {confirmationText} to confirm destructive repair of {SelectedDevice.DisplayName}.",
            "Confirm flash repair",
            string.Empty);

        if (!typedText.Equals(confirmationText, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Flash repair cancelled because the confirmation text did not match.";
            return;
        }

        await RunOperationAsync("Repairing flash drive", async ct =>
        {
            // This is the only destructive operation in the app. The service validates the selected disk again before DiskPart runs.
            var result = await _repairFlashDriveUseCase.ExecuteAsync(
                SelectedDevice,
                new FlashRepairOptions(),
                ct,
                new Progress<double>(value => Progress = value));

            StatusMessage = result.Success
                ? "Flash drive repair completed. Refresh devices if Windows has not shown the drive yet."
                : $"{result.Message} {TrimForStatus(result.Output)}";
        });
    }

    [RelayCommand(CanExecute = nameof(CanTrySafeRepair))]
    private async Task TrySafeRepairAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            "Try Safe Repair will not wipe or format the pendrive. It may clear read-only flags, try to assign a drive letter, and run CHKDSK /F if a drive letter exists. Recover important files first if possible.",
            "Try safe repair",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        await RunOperationAsync("Trying safe repair", async ct =>
        {
            var result = await _trySafeRepairUseCase.ExecuteAsync(
                SelectedDevice,
                ct,
                new Progress<double>(value => Progress = value));

            StatusMessage = $"{result.Message} {TrimForStatus(result.Output)}";
        });
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var reportFolder = !string.IsNullOrWhiteSpace(DestinationPath)
            ? DestinationPath
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var reportPath = Path.Combine(reportFolder, $"PendriveRescueReport_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        bool exported = _lastRecoveryJob is not null
            ? await _exportReportUseCase.ExecuteAsync(_lastRecoveryJob, reportPath)
            : _lastScanResult is not null && await _exportReportUseCase.ExecuteAsync(_lastScanResult, reportPath);

        StatusMessage = exported
            ? $"Report exported to {reportPath}."
            : "There is no scan or recovery report to export yet.";
    }

    private async Task RunScanAsync(string title, Func<CancellationToken, Task<ScanResult>> scan)
    {
        await RunOperationAsync(title, async ct =>
        {
            ClearFiles();
            _lastRecoveryJob = null;
            _lastScanResult = await scan(ct);

            foreach (var file in _lastScanResult.FilesFound)
            {
                AddFile(file);
            }

            OnPropertyChanged(nameof(FoundCount));
            OnPropertyChanged(nameof(SelectedFileCount));
            OnPropertyChanged(nameof(HasFiles));
            StatusMessage = $"{title} finished: {Files.Count} candidate file(s), {_lastScanResult.Errors} read error(s).";
        });
    }

    private async Task RecoverFilesToDestinationAsync(IReadOnlyCollection<RecoverableFile> filesToRecover, string targetFolder)
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunOperationAsync("Recovering files", async ct =>
        {
            var progress = new Progress<double>(value => Progress = value);
            _lastRecoveryJob = await _recoverFilesUseCase.ExecuteAsync(
                filesToRecover,
                SelectedDevice,
                targetFolder,
                ct,
                progress);

            var recovered = _lastRecoveryJob.SourceFiles.Count(file => file.State == Domain.Enums.RecoveryState.Recovered);
            StatusMessage = $"Recovery completed: {recovered}/{_lastRecoveryJob.SourceFiles.Count} file(s) recovered.";
        });
    }

    private async Task RunOperationAsync(string title, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            _operationCts = new CancellationTokenSource();
            IsBusy = true;
            Progress = 0;
            OperationTitle = title;
            StatusMessage = $"{title}...";
            await operation(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"{title} cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            System.Windows.MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            IsBusy = false;
            Progress = IsBusy ? Progress : Math.Min(Progress, 100);
            OperationTitle = "Idle";
        }
    }

    private bool CanStartOperation() => !IsBusy;

    private bool CanScan() => !IsBusy && SelectedDevice is not null;

    private bool CanRecover() =>
        !IsBusy &&
        SelectedDevice is not null &&
        !string.IsNullOrWhiteSpace(DestinationPath) &&
        Files.Count > 0;

    private bool CanRecoverFiles() => !IsBusy && SelectedDevice is not null && Files.Count > 0;

    private bool CanTrySafeRepair() =>
        !IsBusy &&
        SelectedDevice is not null &&
        SelectedDevice.IsRemovable &&
        SelectedDevice.DiskNumber > 0 &&
        !string.IsNullOrWhiteSpace(SelectedDevice.PhysicalPath);

    private bool CanRepairFlashDrive() =>
        !IsBusy &&
        SelectedDevice is not null &&
        SelectedDevice.IsRemovable &&
        SelectedDevice.DiskNumber > 0 &&
        !string.IsNullOrWhiteSpace(SelectedDevice.PhysicalPath);

    private bool CanCancel() => IsBusy;

    private void AddFile(RecoverableFile file)
    {
        file.PropertyChanged += OnRecoverableFilePropertyChanged;
        Files.Add(file);
    }

    private void ClearFiles()
    {
        foreach (var file in Files)
        {
            file.PropertyChanged -= OnRecoverableFilePropertyChanged;
        }

        Files.Clear();
    }

    private void OnRecoverableFilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecoverableFile.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedFileCount));
            RecoverSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private static string TrimForStatus(string value)
    {
        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 220 ? singleLine : singleLine[..220] + "...";
    }

    private static string BuildDiagnosticMessage(DeviceDiagnosticResult result)
    {
        return
            $"Problem: {result.Title}\n\n" +
            $"Details: {result.Details}\n\n" +
            $"Recommendation: {result.Recommendation}\n\n" +
            $"Deep Scan suggested: {(result.ShouldUseDeepScan ? "Yes" : "No")}\n" +
            $"Safe Repair possible: {(result.CanAttemptSafeRepair ? "Yes" : "No")}\n" +
            $"Likely physical damage: {(result.IsLikelyPhysicalDamage ? "Yes" : "No")}";
    }
}
