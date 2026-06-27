using System.Windows;
using LibVLCSharp.Shared;

namespace BannerGuardSystem;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Services.BackendManager.Log($"[WPF-Crash] {args.Exception}");
            MessageBox.Show($"오류가 발생했습니다.\n{args.Exception.Message}", "BannerGuardSystem",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        
        // WPF 시작 시 AI 서버 자동 기동
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
        // WPF 종료 시 AI 서버 자동 종료
        Services.BackendManager.Stop();
        
        LibVLC?.Dispose();
        base.OnExit(e);
    }
}
