using System.Windows;
using LibVLCSharp.Shared;

namespace BannerGuardSystem;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Start the background python backend server
        Services.BackendManager.Start();
        
        // Initialize QuestPDF Community License
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        
        // Initialize LibVLC native libraries
        Core.Initialize();
        LibVLC = new LibVLC("--no-osd", "--rtsp-tcp");

        // Create dependency instances
        var mainViewModel = new ViewModels.MainViewModel();

        // Instantiate Window and set DataContext
        var mainWindow = new MainWindow();
        mainWindow.DataContext = mainViewModel;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Stop the background python backend server
        Services.BackendManager.Stop();
        
        LibVLC?.Dispose();
        base.OnExit(e);
    }
}
