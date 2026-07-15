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
    private readonly RefreshStorageDevicesUseCase _refreshStorageDevicesUseCase;
    private readonly AnalyzeDeviceUseCase _analyzeDeviceUseCase;
    private readonly RunQuickScanUseCase _runQuickScanUseCase;
    private readonly RunDeepScanUseCase _runDeepScanUseCase;
    private readonly RecoverFilesUseCase _recoverFilesUseCase;
    private readonly RepairFlashDriveUseCase _repairFlashDriveUseCase;
    private readonly TrySafeRepairUseCase _trySafeRepairUseCase;
    private readonly CleanUsbMalwareArtifactsUseCase _cleanUsbMalwareArtifactsUseCase;
    private readonly ProtectUsbDriveUseCase _protectUsbDriveUseCase;
    private readonly RunMalwareScanUseCase _runMalwareScanUseCase;
    private readonly ExportReportUseCase _exportReportUseCase;
    private readonly IFolderPicker _folderPicker;
    private CancellationTokenSource? _operationCts;
    private ScanResult? _lastScanResult;
    private RecoveryJob? _lastRecoveryJob;

    public MainViewModel(
        RefreshStorageDevicesUseCase refreshStorageDevicesUseCase,
        AnalyzeDeviceUseCase analyzeDeviceUseCase,
        RunQuickScanUseCase runQuickScanUseCase,
        RunDeepScanUseCase runDeepScanUseCase,
        RecoverFilesUseCase recoverFilesUseCase,
        RepairFlashDriveUseCase repairFlashDriveUseCase,
        TrySafeRepairUseCase trySafeRepairUseCase,
        CleanUsbMalwareArtifactsUseCase cleanUsbMalwareArtifactsUseCase,
        ProtectUsbDriveUseCase protectUsbDriveUseCase,
        RunMalwareScanUseCase runMalwareScanUseCase,
        ExportReportUseCase exportReportUseCase,
        IFolderPicker folderPicker)
    {
        _refreshStorageDevicesUseCase = refreshStorageDevicesUseCase;
        _analyzeDeviceUseCase = analyzeDeviceUseCase;
        _runQuickScanUseCase = runQuickScanUseCase;
        _runDeepScanUseCase = runDeepScanUseCase;
        _recoverFilesUseCase = recoverFilesUseCase;
        _repairFlashDriveUseCase = repairFlashDriveUseCase;
        _trySafeRepairUseCase = trySafeRepairUseCase;
        _cleanUsbMalwareArtifactsUseCase = cleanUsbMalwareArtifactsUseCase;
        _protectUsbDriveUseCase = protectUsbDriveUseCase;
        _runMalwareScanUseCase = runMalwareScanUseCase;
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
    [NotifyCanExecuteChangedFor(nameof(CleanUsbMalwareArtifactsCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableUsbProtectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableUsbProtectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanUsbForMalwareCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(CleanUsbMalwareArtifactsCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableUsbProtectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableUsbProtectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanUsbForMalwareCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanComputerForMalwareCommand))]
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

    [ObservableProperty]
    private bool protectAfterNormalize = true;

    [ObservableProperty]
    private bool scanWithDefenderDuringNormalize = true;

    [ObservableProperty]
    private bool usePrinterCompatibilityFormat;

    [ObservableProperty]
    private string usbProtectionStatus = "Select a mounted USB drive.";

    [ObservableProperty]
    private string defenderStatus = "Microsoft Defender has not been checked.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableUsbProtectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableUsbProtectionCommand))]
    private bool isUsbProtectionEnabled;

    public int FoundCount => Files.Count;

    public int SelectedFileCount => Files.Count(file => file.IsSelected);

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelectedDevice => SelectedDevice is not null;

    public bool HasFiles => Files.Count > 0;

    public bool UseModernFullCapacityFormat
    {
        get => !UsePrinterCompatibilityFormat;
        set
        {
            if (value)
            {
                UsePrinterCompatibilityFormat = false;
            }
        }
    }

    public string RepairFormatSummary => UsePrinterCompatibilityFormat
        ? "FAT32, one MBR partition. Large drives use a 30 GB compatibility partition."
        : "exFAT, one MBR partition using the full drive capacity.";

    partial void OnUsePrinterCompatibilityFormatChanged(bool value)
    {
        OnPropertyChanged(nameof(UseModernFullCapacityFormat));
        OnPropertyChanged(nameof(RepairFormatSummary));
    }

    partial void OnSelectedDeviceChanged(StorageDevice? value)
    {
        ClearFiles();
        _lastScanResult = null;
        Progress = 0;
        StatusMessage = value is null
            ? "Select a removable drive to begin."
            : $"Selected {value.DisplayName}. Use Deep Scan for RAW, inaccessible, or no-letter devices.";
        UsbProtectionStatus = value is null ? "Select a mounted USB drive." : "Checking protection...";
        DefenderStatus = value is null ? "Select a mounted USB drive." : "USB security scan not run yet.";
        IsUsbProtectionEnabled = false;
        OnPropertyChanged(nameof(FoundCount));
        OnPropertyChanged(nameof(SelectedFileCount));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(HasFiles));
        _ = RefreshUsbProtectionStatusAsync(value);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task RefreshDevicesAsync()
    {
        await RunOperationAsync("Refreshing devices", async ct =>
        {
            var refreshed = await _refreshStorageDevicesUseCase.ExecuteAsync(
                SelectedDevice,
                TimeSpan.Zero,
                TimeSpan.Zero,
                ct,
                new Progress<double>(value => Progress = value));
            ApplyDeviceRefresh(refreshed, refreshed.MatchedDevice ?? refreshed.Devices.FirstOrDefault());
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

        var deviceToRepair = SelectedDevice;
        var usePrinterCompatibilityFormat = UsePrinterCompatibilityFormat;
        var repairOptions = usePrinterCompatibilityFormat
            ? new FlashRepairOptions
            {
                FileSystem = "fat32",
                LimitFat32PartitionForCompatibility = true
            }
            : new FlashRepairOptions();
        var formatDescription = usePrinterCompatibilityFormat
            ? deviceToRepair.TotalBytes > FlashRepairOptions.Fat32CompatibilityThresholdBytes
                ? "Printer and device compatibility: one 30 GB FAT32 partition. Remaining capacity will be unallocated, and individual files cannot exceed 4 GB."
                : "Printer and device compatibility: one FAT32 partition. Individual files cannot exceed 4 GB."
            : "Modern full capacity: one exFAT partition using the available drive capacity.";

        var firstWarning = System.Windows.MessageBox.Show(
            $"Repair Flash Drive will erase the selected pendrive, recreate its partition, format it, and assign a drive letter.\n\nSelected profile: {formatDescription}\n\nRecover files first if you still need data from this device.",
            "Erase and repair flash drive",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (firstWarning != MessageBoxResult.OK)
        {
            return;
        }

        var confirmationText = $"REPAIR DISK {deviceToRepair.DiskNumber}";
        var typedText = Microsoft.VisualBasic.Interaction.InputBox(
            $"Type {confirmationText} to confirm destructive repair of {deviceToRepair.DisplayName}.",
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
                deviceToRepair,
                repairOptions,
                ct,
                new Progress<double>(value => Progress = value * 0.80));

            if (!result.Success)
            {
                StatusMessage = $"{result.Message} {TrimForStatus(result.Output)}";
                return;
            }

            StatusMessage = "Flash drive repair completed. Waiting for Windows to remount the same USB...";
            var refreshed = await _refreshStorageDevicesUseCase.ExecuteAsync(
                deviceToRepair,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromMilliseconds(750),
                ct,
                new Progress<double>(value => Progress = 80d + (value * 0.20)));
            ApplyDeviceRefresh(refreshed, refreshed.MatchedDevice);

            StatusMessage = RefreshStorageDevicesUseCase.IsMountedAndReady(refreshed.MatchedDevice)
                ? $"Flash drive repair completed with {(usePrinterCompatibilityFormat ? "FAT32 compatibility" : "exFAT full capacity")} and Windows mounted it as {refreshed.MatchedDevice!.DriveLetter}."
                : "Flash drive repair completed, but Windows did not remount the USB within 30 seconds. Reconnect it or use Refresh Devices before Normalize.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCleanUsbMalwareArtifacts))]
    private async Task CleanUsbMalwareArtifactsAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var deviceToNormalize = SelectedDevice;

        var answer = System.Windows.MessageBox.Show(
            "Choose how to normalize this USB drive.\n\nYes: keep personal files and quarantine shortcut-virus launchers, scripts, and MAyundo wrappers.\n\nNo: remove user contents without formatting. Windows metadata folders are preserved and hidden.\n\nThe selected Defender and AutoRun blocker options will run automatically.\n\nCancel: do nothing.",
            "Normalize USB",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Cancel)
        {
            return;
        }

        var removeAllContents = answer == MessageBoxResult.No;

        if (removeAllContents)
        {
            var confirm = System.Windows.MessageBox.Show(
                $"This will remove visible and hidden user contents from {deviceToNormalize.DriveLetter}. Windows metadata folders will be preserved and hidden. The drive will not be formatted. Continue?",
                "Remove all USB contents",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.OK)
            {
                return;
            }
        }

        await RunOperationAsync("Normalizing USB", async ct =>
        {
            var summaries = new List<string>();
            var scanEnabled = ScanWithDefenderDuringNormalize;
            var refreshed = await _refreshStorageDevicesUseCase.ExecuteAsync(
                deviceToNormalize,
                TimeSpan.FromSeconds(12),
                TimeSpan.FromMilliseconds(500),
                ct,
                new Progress<double>(value => Progress = value * 0.05));
            var mountedDevice = refreshed.MatchedDevice;
            ApplyDeviceRefresh(refreshed, mountedDevice);

            if (mountedDevice is null || !RefreshStorageDevicesUseCase.IsMountedAndReady(mountedDevice))
            {
                throw new InvalidOperationException(
                    "Windows has not mounted the selected USB yet. Reconnect it or use Refresh Devices, then run Normalize again.");
            }

            if (scanEnabled)
            {
                var computerScan = await _runMalwareScanUseCase.ScanComputerAsync(
                    ct,
                    new Progress<double>(value => Progress = 5d + (value * 0.10)));
                DefenderStatus = BuildDefenderStatus(computerScan);
                summaries.Add($"Computer: {computerScan.Message}");

                if (computerScan.RequiresAction)
                {
                    Progress = 100;
                    summaries.Add("USB cleanup was stopped because this computer can reinfect the drive. Resolve Defender Protection history and run Microsoft Defender Offline before trying again.");
                    StatusMessage = string.Join(" ", summaries);
                    return;
                }

                var beforeScan = await _runMalwareScanUseCase.ScanUsbAsync(
                    mountedDevice,
                    ct,
                    new Progress<double>(value => Progress = 15d + (value * 0.15)));
                DefenderStatus = BuildDefenderStatus(beforeScan);
                summaries.Add($"USB before cleanup: {beforeScan.Message}");
            }

            var cleanupStart = scanEnabled ? 30d : 5d;
            var cleanupEnd = scanEnabled ? 70d : ProtectAfterNormalize ? 80d : 100d;
            var result = await _cleanUsbMalwareArtifactsUseCase.ExecuteAsync(
                mountedDevice,
                new UsbCleanupOptions { RemoveAllDriveContents = removeAllContents },
                ct,
                new Progress<double>(value => Progress = cleanupStart + (value * (cleanupEnd - cleanupStart) / 100d)));

            summaries.Add(
                $"{result.Message} Moved: {result.FilesMoved}, quarantined: {result.ItemsQuarantined}, deleted: {result.ItemsDeleted}, suspicious remaining: {result.SuspiciousItemsRemaining}, errors: {result.Errors}.");

            if (!string.IsNullOrWhiteSpace(result.QuarantinePath))
            {
                summaries.Add($"Quarantine: {result.QuarantinePath}.");
            }

            MalwareScanResult? afterScan = null;
            if (scanEnabled)
            {
                afterScan = await _runMalwareScanUseCase.ScanUsbAsync(
                    mountedDevice,
                    ct,
                    new Progress<double>(value => Progress = 70d + (value * 0.20)));
                DefenderStatus = BuildDefenderStatus(afterScan);
                summaries.Add($"USB after cleanup: {afterScan.Message}");
            }

            if (result.Success && ProtectAfterNormalize && afterScan?.RequiresAction != true)
            {
                var protectionStart = scanEnabled ? 90d : 80d;
                var protectionResult = await _protectUsbDriveUseCase.EnableAsync(
                    mountedDevice,
                    ct,
                    new Progress<double>(value => Progress = protectionStart + (value * (100d - protectionStart) / 100d)));

                IsUsbProtectionEnabled = protectionResult.IsProtected;
                UsbProtectionStatus = protectionResult.IsProtected ? "AutoRun blocker enabled" : "AutoRun blocker unavailable";
                summaries.Add(protectionResult.Message);
            }
            else if (afterScan?.RequiresAction == true && ProtectAfterNormalize)
            {
                summaries.Add("The AutoRun blocker was not applied because Defender still reports a threat requiring action.");
            }

            Progress = 100;
            if (!mountedDevice.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
            {
                summaries.Add(
                    $"The file system remains {mountedDevice.FileSystem}; some printers only recognize FAT32. Use Erase and Repair with the Printer / device compatibility profile when that compatibility is required.");
            }

            summaries.Add("Eject the USB after cleanup before connecting it to another computer.");
            StatusMessage = string.Join(" ", summaries);
        });
    }

    [RelayCommand(CanExecute = nameof(CanScanUsbForMalware))]
    private async Task ScanUsbForMalwareAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunOperationAsync("Scanning USB with Microsoft Defender", async ct =>
        {
            var result = await _runMalwareScanUseCase.ScanUsbAsync(
                SelectedDevice,
                ct,
                new Progress<double>(value => Progress = value));
            DefenderStatus = BuildDefenderStatus(result);
            StatusMessage = result.Message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanScanComputerForMalware))]
    private async Task ScanComputerForMalwareAsync()
    {
        var answer = System.Windows.MessageBox.Show(
            "Microsoft Defender will run a Quick Scan of this computer. Use Defender Offline afterward if USB malware keeps returning.",
            "Scan this computer",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        await RunOperationAsync("Scanning this computer", async ct =>
        {
            var result = await _runMalwareScanUseCase.ScanComputerAsync(
                ct,
                new Progress<double>(value => Progress = value));
            DefenderStatus = BuildDefenderStatus(result);
            StatusMessage = result.Message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanEnableUsbProtection))]
    private async Task EnableUsbProtectionAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunOperationAsync("Enabling USB protection", async ct =>
        {
            var result = await _protectUsbDriveUseCase.EnableAsync(
                SelectedDevice,
                ct,
                new Progress<double>(value => Progress = value));

            IsUsbProtectionEnabled = result.IsProtected;
            UsbProtectionStatus = result.IsProtected ? "AutoRun blocker enabled" : "AutoRun blocker unavailable";
            StatusMessage = result.Message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanDisableUsbProtection))]
    private async Task DisableUsbProtectionAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        await RunOperationAsync("Removing USB protection", async ct =>
        {
            var result = await _protectUsbDriveUseCase.DisableAsync(
                SelectedDevice,
                ct,
                new Progress<double>(value => Progress = value));

            IsUsbProtectionEnabled = result.IsProtected;
            UsbProtectionStatus = result.IsProtected ? "AutoRun blocker enabled" : "AutoRun blocker not enabled";
            StatusMessage = result.Message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanTrySafeRepair))]
    private async Task TrySafeRepairAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var deviceToRepair = SelectedDevice;

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
                deviceToRepair,
                ct,
                new Progress<double>(value => Progress = value * 0.80));

            var refreshed = await _refreshStorageDevicesUseCase.ExecuteAsync(
                deviceToRepair,
                result.Success ? TimeSpan.FromSeconds(15) : TimeSpan.Zero,
                TimeSpan.FromMilliseconds(750),
                ct,
                new Progress<double>(value => Progress = 80d + (value * 0.20)));
            ApplyDeviceRefresh(refreshed, refreshed.MatchedDevice);

            var mountMessage = RefreshStorageDevicesUseCase.IsMountedAndReady(refreshed.MatchedDevice)
                ? $" Windows mounted the USB as {refreshed.MatchedDevice!.DriveLetter}."
                : " Windows has not mounted the USB yet.";
            StatusMessage = $"{result.Message}{mountMessage} {TrimForStatus(result.Output)}";
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

    private bool CanCleanUsbMalwareArtifacts() =>
        !IsBusy &&
        SelectedDevice is not null &&
        SelectedDevice.IsRemovable &&
        !string.IsNullOrWhiteSpace(SelectedDevice.DriveLetter) &&
        SelectedDevice.Status is not Domain.Enums.DeviceHealthStatus.Raw and not Domain.Enums.DeviceHealthStatus.Unmounted;

    private bool CanEnableUsbProtection() => CanManageUsbProtection() && !IsUsbProtectionEnabled;

    private bool CanDisableUsbProtection() => CanManageUsbProtection() && IsUsbProtectionEnabled;

    private bool CanScanUsbForMalware() => CanManageUsbProtection();

    private bool CanScanComputerForMalware() => !IsBusy;

    private bool CanManageUsbProtection() =>
        !IsBusy &&
        SelectedDevice is not null &&
        SelectedDevice.IsRemovable &&
        !string.IsNullOrWhiteSpace(SelectedDevice.DriveLetter) &&
        SelectedDevice.Status is not Domain.Enums.DeviceHealthStatus.Raw and not Domain.Enums.DeviceHealthStatus.Unmounted;

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

    private void ApplyDeviceRefresh(StorageDeviceRefreshResult refreshed, StorageDevice? selectedDevice)
    {
        ClearFiles();
        Devices.Clear();
        foreach (var device in refreshed.Devices)
        {
            Devices.Add(device);
        }

        SelectedDevice = selectedDevice;
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasFiles));
    }

    private async Task RefreshUsbProtectionStatusAsync(StorageDevice? device)
    {
        if (device is null ||
            !device.IsRemovable ||
            string.IsNullOrWhiteSpace(device.DriveLetter) ||
            device.Status is Domain.Enums.DeviceHealthStatus.Raw or Domain.Enums.DeviceHealthStatus.Unmounted)
        {
            UsbProtectionStatus = device is null ? "Select a mounted USB drive." : "Protection requires a mounted readable drive.";
            return;
        }

        try
        {
            var isProtected = await _protectUsbDriveUseCase.IsProtectedAsync(device, CancellationToken.None);
            if (!ReferenceEquals(SelectedDevice, device))
            {
                return;
            }

            IsUsbProtectionEnabled = isProtected;
            UsbProtectionStatus = isProtected ? "AutoRun blocker enabled" : "AutoRun blocker not enabled";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedDevice, device))
            {
                IsUsbProtectionEnabled = false;
                UsbProtectionStatus = $"Protection status unavailable: {ex.Message}";
            }
        }
    }

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

    private static string BuildDefenderStatus(MalwareScanResult result)
    {
        if (!result.Available)
        {
            return "Microsoft Defender unavailable";
        }

        if (result.RequiresAction)
        {
            return "Threat found - action required";
        }

        return result.Success ? "Last Defender scan completed" : "Defender scan failed";
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
