using System.Windows;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using BannerGuardSystem.ViewModels;


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
        LibVLC = new LibVLC("--no-osd", "--rtsp-tcp", "-vvv"); // Enable verbose logging
        LibVLC.Log += (s, ev) => Services.BackendManager.Log($"[LibVLC] {ev.Level}: {ev.Message}");

        // Create DI container and resolve ShellViewModel
        var serviceProvider = AppBootstrapper.Build();

        var db = serviceProvider.GetRequiredService<BannerGuardSystem.Services.DatabaseService>();
        db.Initialize();

        var ws = serviceProvider.GetRequiredService<BannerGuardSystem.Services.WebSocketService>();
        _ = ws.ConnectAsync(new BannerGuardSystem.Models.AppSettings().WsUrl);

        var shellViewModel = serviceProvider.GetService<ShellViewModel>();
        var mainWindow = new MainWindow();
        mainWindow.DataContext = shellViewModel;
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
