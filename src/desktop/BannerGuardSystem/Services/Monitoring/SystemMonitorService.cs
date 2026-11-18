using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BannerGuardSystem.Services;

public class SystemMonitorService : INotifyPropertyChanged
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

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

    private string _aiStatusText = "서버 시작 중";
    public string AiStatusText
    {
        get => _aiStatusText;
        set => SetProperty(ref _aiStatusText, value);
    }

    private string _lastAnalysisTimeText = "대기 중";
    public string LastAnalysisTimeText
    {
        get => _lastAnalysisTimeText;
        set => SetProperty(ref _lastAnalysisTimeText, value);
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

    private string _gpuName = string.Empty;
    public string GpuName
    {
        get => _gpuName;
        private set => SetProperty(ref _gpuName, value);
    }

    private double _gpuUtilization;
    public double GpuUtilization
    {
        get => _gpuUtilization;
        private set => SetProperty(ref _gpuUtilization, value);
    }

    private double _vramUsedMb;
    public double VramUsedMb
    {
        get => _vramUsedMb;
        private set => SetProperty(ref _vramUsedMb, value);
    }

    private double _vramTotalMb;
    public double VramTotalMb
    {
        get => _vramTotalMb;
        private set => SetProperty(ref _vramTotalMb, value);
    }

    private bool _isAiModelReady;
    public bool IsAiModelReady
    {
        get => _isAiModelReady;
        private set => SetProperty(ref _isAiModelReady, value);
    }

    private bool _isAiModelLoading;
    public bool IsAiModelLoading
    {
        get => _isAiModelLoading;
        private set => SetProperty(ref _isAiModelLoading, value);
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

            if (!value)
            {
                AiStatusText = "자동화 OFF — 분석 정지";
            }
            else
            {
                UpdateAiStatusFromTelemetry();
            }
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
    private int _telemetryTickCount = 0;
    private bool _isPollingTelemetry = false;

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
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Action action = () =>
        {
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected" : "Disconnected";
            if (!connected)
            {
                AiStatusText = "연결 끊김 (Disconnected)";
            }
            else
            {
                _ = _wsService.SendCommandAsync("toggle_automation", IsAutomationEnabled);
                UpdateAiStatusFromTelemetry();
            }
        };

        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>
    /// AI 분석 결과 수신 완료 시 호출되어 마지막 분석 시각과 상태 문구를 안정적으로 갱신합니다.
    /// 인위적인 3초 리셋 없이 관제관에게 실제 마지막 분석 성공 시각을 유지합니다.
    /// </summary>
    public void RecordAnalysisCompleted(DateTime? timestamp = null)
    {
        var dt = timestamp ?? DateTime.Now;
        var timeStr = dt.ToString("HH:mm:ss");
        LastAnalysisTimeText = timeStr;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Action action = () =>
        {
            AiStatusText = $"{timeStr} 수신 완료";
        };

        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public void SetAiAnalyzing()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Action action = () =>
        {
            AiStatusText = "분석 중 (추론 진행)...";
        };

        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public void SetAiError(string errorMsg = "오류")
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Action action = () =>
        {
            AiStatusText = $"오류: {errorMsg}";
        };

        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    [Obsolete("Use RecordAnalysisCompleted() instead. Artificial 3-second reset timer has been removed.")]
    public void ResetAiStatusAfterDelay()
    {
        // No-op: Artificial timer removed per 5.3 specifications.
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 5.3: UI 스레드 주기적 프리징을 유발하던 강제 GC.Collect() 완전 제거

        try
        {
            using Process process = Process.GetCurrentProcess();
            DateTime now = DateTime.Now;
            TimeSpan totalProcessorTime = process.TotalProcessorTime;

            if (_lastCpuTime != DateTime.MinValue)
            {
                double totalMsPassed = (now - _lastCpuTime).TotalMilliseconds;
                double cpuMsUsed = (totalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                if (totalMsPassed > 0)
                {
                    double cpuUsage = (cpuMsUsed / (Environment.ProcessorCount * totalMsPassed)) * 100;
                    CpuUsageText = $"{Math.Round(cpuUsage, 1)}%";
                }
            }

            _lastCpuTime = now;
            _lastTotalProcessorTime = totalProcessorTime;

            long workingSet64 = process.WorkingSet64;
            RamUsageText = $"{workingSet64 / 1024 / 1024}MB";
        }
        catch { }

        // 5.2: 2초 주기 백엔드 하드웨어 텔레메트리(/health) 비동기 폴링
        _telemetryTickCount++;
        if (_telemetryTickCount % 2 == 0 && !_isPollingTelemetry)
        {
            _ = PollBackendTelemetryAsync();
        }
    }

    public async Task PollBackendTelemetryAsync(string? healthUrl = null)
    {
        _isPollingTelemetry = true;
        try
        {
            using var response = await _httpClient.GetAsync(healthUrl ?? Helpers.RuntimePaths.Get("ApiBaseUrl", "http://localhost:8000").TrimEnd('/') + "/health").ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool aiReady = root.TryGetProperty("ai_ready", out var ar) && ar.GetBoolean();
                bool aiLoading = root.TryGetProperty("ai_loading", out var al) && al.GetBoolean();
                bool cuda = root.TryGetProperty("cuda_available", out var ca) && ca.GetBoolean();
                string gpuName = root.TryGetProperty("gpu_name", out var gn) ? (gn.GetString() ?? "") : "";
                double gpuUtil = root.TryGetProperty("gpu_utilization", out var gu) ? gu.GetDouble() : 0.0;
                double vramUsed = root.TryGetProperty("vram_used_mb", out var vu) ? vu.GetDouble() : 0.0;
                double vramTotal = root.TryGetProperty("vram_total_mb", out var vt) ? vt.GetDouble() : 0.0;

                ApplyTelemetry(aiReady, aiLoading, cuda, gpuName, gpuUtil, vramUsed, vramTotal);
            }
            else
            {
                ApplyTelemetryOffline();
            }
        }
        catch
        {
            ApplyTelemetryOffline();
        }
        finally
        {
            _isPollingTelemetry = false;
        }
    }

    public void ApplyTelemetry(bool aiReady, bool aiLoading, bool cuda, string gpuName, double gpuUtil, double vramUsed, double vramTotal)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Action action = () =>
        {
            IsAiModelReady = aiReady;
            IsAiModelLoading = aiLoading;
            GpuName = gpuName;
            GpuUtilization = gpuUtil;
            VramUsedMb = vramUsed;
            VramTotalMb = vramTotal;

            if (cuda && !string.IsNullOrWhiteSpace(gpuName))
            {
                GpuUsageText = $"{gpuName}: {Math.Round(gpuUtil)}%";
            }
            else if (cuda)
            {
                GpuUsageText = $"{Math.Round(gpuUtil)}%";
            }
            else
            {
                GpuUsageText = "CPU 모드 (N/A)";
            }

            UpdateAiStatusFromTelemetry();
        };

        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void ApplyTelemetryOffline()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        Action action = () =>
        {
            IsAiModelReady = false;
            IsAiModelLoading = false;
            GpuUsageText = "0% (서버 오프라인)";
            if (!IsConnected)
            {
                AiStatusText = "서버 오프라인 (서버 시작 중)";
            }
        };

        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void UpdateAiStatusFromTelemetry()
    {
        if (!IsAutomationEnabled)
        {
            AiStatusText = "자동화 OFF — 분석 정지";
            return;
        }

        if (IsAiModelLoading)
        {
            AiStatusText = "AI 모델 준비 중 (VLM 로딩)";
            return;
        }

        if (IsAiModelReady)
        {
            // 최근 분석 완료 시각이나 분석 중 상태가 유지되고 있는 경우 덮어쓰지 않음
            if (AiStatusText.Contains("수신 완료") || AiStatusText.StartsWith("분석 중"))
            {
                return;
            }

            AiStatusText = "정상 대기 (추론 준비 완료)";
        }
        else if (IsConnected)
        {
            AiStatusText = "서버 연결됨 (준비 중)";
        }
    }
}
