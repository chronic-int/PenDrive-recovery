using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PendriveRescue.App.Services;
using PendriveRescue.App.ViewModels;
using PendriveRescue.Application.UseCases;
using PendriveRescue.Domain.Interfaces;
using PendriveRescue.Infrastructure.Services;
using Serilog;

namespace PendriveRescue.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private string _logDirectory = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        ConfigureGlobalErrorHandling();

        try
        {
            ConfigureLogging();

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Pendrive Rescue failed during startup.");
            ShowFatalStartupError(ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDeviceDetectionService, DeviceDetectionService>();
        services.AddSingleton<IDeviceDiagnosticService, DeviceDiagnosticService>();
        services.AddSingleton<IRawReadService, RawReadService>();
        services.AddSingleton<FileCarver>();
        services.AddSingleton<IQuickScanService, QuickScanService>();
        services.AddSingleton<IDeepScanService, DeepScanService>();
        services.AddSingleton<IRecoveryService, RecoveryService>();
        services.AddSingleton<IFlashRepairService, FlashRepairService>();
        services.AddSingleton<ISafeFlashRepairService, SafeFlashRepairService>();
        services.AddSingleton<IUsbMalwareCleanupService, UsbMalwareCleanupService>();
        services.AddSingleton<IUsbProtectionService, UsbProtectionService>();
        services.AddSingleton<IMalwareScanService, MicrosoftDefenderScanService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        services.AddTransient<AnalyzeDeviceUseCase>();
        services.AddTransient<RefreshStorageDevicesUseCase>();
        services.AddTransient<RunQuickScanUseCase>();
        services.AddTransient<RunDeepScanUseCase>();
        services.AddTransient<RecoverFilesUseCase>();
        services.AddTransient<RepairFlashDriveUseCase>();
        services.AddTransient<TrySafeRepairUseCase>();
        services.AddTransient<CleanUsbMalwareArtifactsUseCase>();
        services.AddTransient<ProtectUsbDriveUseCase>();
        services.AddTransient<RunMalwareScanUseCase>();
        services.AddTransient<ExportReportUseCase>();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
    }

    private void ConfigureLogging()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PendriveRescue",
            "logs");

        Directory.CreateDirectory(_logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(_logDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }

    private void ConfigureGlobalErrorHandling()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "A UI error was handled globally.");
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"Pendrive Rescue handled an unexpected UI error and will keep running.\n\n{e.Exception.Message}\n\nLogs: {_logDirectory}",
            "Pendrive Rescue",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "A fatal application error occurred.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "A background task failed after it was no longer observed.");
        e.SetObserved();
    }

    private void ShowFatalStartupError(Exception ex)
    {
        System.Windows.MessageBox.Show(
            $"Pendrive Rescue could not start safely.\n\n{ex.Message}\n\nLogs: {_logDirectory}",
            "Pendrive Rescue startup error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
