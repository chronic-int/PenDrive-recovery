using System.Windows;
using System.Windows.Media.Imaging;
using PendriveRescue.App.ViewModels;
using Serilog;

namespace PendriveRescue.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TryLoadWindowIcon();
    }

    private void TryLoadWindowIcon()
    {
        try
        {
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the window icon. The application will continue without it.");
        }
    }
}
