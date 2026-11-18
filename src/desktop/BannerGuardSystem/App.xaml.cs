using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using BannerGuardSystem.ViewModels;

namespace BannerGuardSystem;

public partial class App : Application
{
    public static LibVLC LibVLC { get; private set; } = null!;
    private static Mutex? _singleInstanceMutex;
    private static bool _isFirstInstance;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Global\BannerGuardSystem_SingleInstance_Mutex";
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            _isFirstInstance = createdNew;
        }
        catch (Exception ex)
        {
            Services.BackendManager.Log($"[SingleInstance] Mutex creation warning: {ex.Message}");
            _isFirstInstance = true;
        }

        if (!_isFirstInstance)
        {
            BringExistingWindowToFront();
            MessageBox.Show("BannerGuardSystem이 이미 실행 중입니다.\n기존 실행 중인 관제 창을 활성화합니다.",
                "BannerGuardSystem", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Services.BackendManager.Log($"[WPF-Crash] {args.Exception}");
            MessageBox.Show($"오류가 발생했습니다.\n{args.Exception.Message}", "BannerGuardSystem",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Services.BackendManager.Log($"[AppDomain-Crash] {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.BackendManager.Log($"[TaskScheduler-Crash] {args.Exception}");
            args.SetObserved();
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

    private static void BringExistingWindowToFront()
    {
        try
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            var existingProcess = System.Diagnostics.Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);

            IntPtr hWnd = existingProcess?.MainWindowHandle ?? IntPtr.Zero;
            if (hWnd == IntPtr.Zero)
            {
                hWnd = FindWindow(null, "BannerGuardSystem - 지능형 불법 현수막 통합 관제 시스템");
            }

            if (hWnd != IntPtr.Zero)
            {
                const int SW_RESTORE = 9;
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
        catch (Exception ex)
        {
            Services.BackendManager.Log($"[SingleInstance] Failed to bring window to front: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_isFirstInstance)
        {
            // WPF 종료 시 자신이 관리하는 AI 서버 안전 종료
            Services.BackendManager.Stop();

            LibVLC?.Dispose();

            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }

        base.OnExit(e);
    }
}
