using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace BannerGuardSystem.Services;

public class SystemMonitorService : INotifyPropertyChanged
{
    private readonly WebSocketService _wsService;
    private readonly DispatcherTimer _clockTimer;

    private string _connectionStatus = "Disconnected";
    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private string _systemModeText = "데모 버전 (송도동 한정)";
    public string SystemModeText
    {
        get => _systemModeText;
        set => SetProperty(ref _systemModeText, value);
    }

    private string _aiStatusText = "수신 대기 중 (Listening)";
    public string AiStatusText
    {
        get => _aiStatusText;
        set => SetProperty(ref _aiStatusText, value);
    }

    private string _dbStatus = "Offline";
    public string DbStatus
    {
        get => _dbStatus;
        set => SetProperty(ref _dbStatus, value);
    }

    private string _currentTimeText = string.Empty;
    public string CurrentTimeText
    {
        get => _currentTimeText;
        set => SetProperty(ref _currentTimeText, value);
    }

    private string _cpuUsageText = "0%";
    public string CpuUsageText
    {
        get => _cpuUsageText;
        set => SetProperty(ref _cpuUsageText, value);
    }

    private string _ramUsageText = "0MB";
    public string RamUsageText
    {
        get => _ramUsageText;
        set => SetProperty(ref _ramUsageText, value);
    }

    private string _gpuUsageText = "0%";
    public string GpuUsageText
    {
        get => _gpuUsageText;
        set => SetProperty(ref _gpuUsageText, value);
    }

    private bool _isAutomationEnabled = true;
    public bool IsAutomationEnabled
    {
        get => _isAutomationEnabled;
        set
        {
            if (!SetProperty(ref _isAutomationEnabled, value)) return;
            if (IsConnected)
                _ = _wsService.SendCommandAsync("toggle_automation", value);
            AiStatusText = value ? "수신 대기 중 (Listening)" : "자동화 OFF — 분석 정지";
        }
    }

    private bool _isImageCropEnabled = true;
    public bool IsImageCropEnabled
    {
        get => _isImageCropEnabled;
        set => SetProperty(ref _isImageCropEnabled, value);
    }

    private DateTime _lastCpuTime = DateTime.MinValue;
    private TimeSpan _lastTotalProcessorTime;
    private int _gcTickCount = 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public SystemMonitorService(WebSocketService wsService)
    {
        _wsService = wsService;
        _wsService.ConnectionChanged += OnConnectionChanged;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
    }

    private void OnConnectionChanged(bool connected)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected" : "Disconnected";
            if (!connected)
            {
                AiStatusText = "연결 끊김 (Disconnected)";
            }
            else
            {
                AiStatusText = IsAutomationEnabled ? "수신 대기 중 (Listening)" : "자동화 OFF — 분석 정지";
                _ = _wsService.SendCommandAsync("toggle_automation", IsAutomationEnabled);
            }
        });
    }

    public void ResetAiStatusAfterDelay()
    {
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) =>
        {
            if (IsConnected) AiStatusText = "수신 대기 중 (Listening)";
            timer.Stop();
        };
        timer.Start();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        _gcTickCount++;
        if (_gcTickCount % 5 == 0)
        {
            GC.Collect();
        }

        try
        {
            using Process process = Process.GetCurrentProcess();
            DateTime now = DateTime.Now;
            TimeSpan totalProcessorTime = process.TotalProcessorTime;

            if (_lastCpuTime != DateTime.MinValue)
            {
                double totalMsPassed = (now - _lastCpuTime).TotalMilliseconds;
                double cpuMsUsed = (totalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                double cpuUsage = (cpuMsUsed / (Environment.ProcessorCount * totalMsPassed)) * 100;
                CpuUsageText = $"{Math.Round(cpuUsage, 1)}%";
            }

            _lastCpuTime = now;
            _lastTotalProcessorTime = totalProcessorTime;

            long workingSet64 = process.WorkingSet64;
            RamUsageText = $"{workingSet64 / 1024 / 1024}MB";
        }
        catch { }
    }
}
