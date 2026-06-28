using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using BannerGuardSystem.Helpers;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.ViewModels;

public class CctvMonitoringViewModel : BaseViewModel
{
    private readonly WebSocketService _wsService;
    private readonly AppSettings _settings;
    private readonly SystemMonitorService _systemMonitor;
    public AlertService AlertService { get; }

    public ObservableCollection<BannerDetectionEventDto> AlertFeed => AlertService.AlertFeed;

    public ObservableCollection<CctvChannel> Channels { get; } = new();

    private CctvChannel? _selectedChannel;
    public CctvChannel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (!SetProperty(ref _selectedChannel, value))
                return;

            foreach (var channel in Channels)
                channel.IsSelected = channel == value;
        }
    }

    public ICommand SelectChannelCommand { get; }

    private DispatcherTimer? _liveYoloTimer;
    private bool _liveYoloSyncRunning;

    public CctvMonitoringViewModel(WebSocketService wsService, SystemMonitorService systemMonitor, AlertService alertService, AppSettings settings)
    {
        _wsService = wsService;
        _systemMonitor = systemMonitor;
        AlertService = alertService;
        _settings = settings;

        SelectChannelCommand = new RelayCommand(p =>
        {
            if (p is string id)
            {
                var ch = Channels.FirstOrDefault(c => c.ChannelId == id);
                if (ch != null) SelectedChannel = ch;
            }
            else if (p is CctvChannel chObj)
            {
                SelectedChannel = chObj;
            }
        });

        LoadDefaultChannels();
        SelectedChannel = Channels.FirstOrDefault();

        _wsService.OnChannelBatchReceived += OnChannelBatchReceived;
    }

    /// <summary>채널 메타(이름·구역)만 동기화. 인스턴스 유지 → CurrentAlerts 보존.</summary>
    public void SyncChannelMetadata()
    {
        if (Channels.Count == 0)
        {
            LoadDefaultChannels();
            SelectedChannel = Channels.FirstOrDefault();
        }
        else
        {
            foreach (var seed in CctvMediaCatalog.DefaultChannels)
            {
                var channel = Channels.FirstOrDefault(c => c.ChannelId == seed.ChannelId);
                if (channel == null)
                    continue;

                channel.Name = seed.Name;
                channel.ZoneLabel = seed.ZoneLabel;
                channel.Address = seed.Address;
            }
        }

        Services.BackendManager.Log(
            $"[CctvMonitoring] Channels: {string.Join(", ", Channels.Select(c => c.Name))}");
    }

    /// <summary>CCTV 탭 진입 시 YOLO channel_batch 재요청 (자동화 ON일 때).</summary>
    public void RequestCctvAnalysisRefresh()
    {
        if (!_systemMonitor.IsAutomationEnabled)
            return;

        _ = _wsService.SendCommandAsync("toggle_automation", true);
    }

    /// <summary>재생 중 MP4 시각에 맞춰 YOLO 박스 갱신 (3초 주기).</summary>
    public void StartLiveYoloSync()
    {
        if (_liveYoloTimer != null)
            return;

        _liveYoloTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _liveYoloTimer.Tick += LiveYoloTimer_Tick;
        _liveYoloTimer.Start();
        Services.BackendManager.Log("[CctvMonitoring] LIVE YOLO sync started (3s)");
    }

    public void StopLiveYoloSync()
    {
        if (_liveYoloTimer == null)
            return;

        _liveYoloTimer.Stop();
        _liveYoloTimer.Tick -= LiveYoloTimer_Tick;
        _liveYoloTimer = null;
        Services.BackendManager.Log("[CctvMonitoring] LIVE YOLO sync stopped");
    }

    private async void LiveYoloTimer_Tick(object? sender, EventArgs e)
        => await SyncLiveYoloAsync();

    private async Task SyncLiveYoloAsync()
    {
        if (_liveYoloSyncRunning || !_systemMonitor.IsAutomationEnabled || !_wsService.IsConnected)
            return;

        _liveYoloSyncRunning = true;
        try
        {
            foreach (var channel in Channels)
            {
                if (channel.PlaybackPositionMs < 0)
                    continue;

                await _wsService.RequestCctvFrameAnalysisAsync(channel.ChannelId, channel.PlaybackPositionMs);
                await Task.Delay(120);
            }
        }
        finally
        {
            _liveYoloSyncRunning = false;
        }
    }

    private void LoadDefaultChannels()
    {
        Channels.Clear();

        foreach (var seed in CctvMediaCatalog.DefaultChannels)
        {
            var videoPath = CctvMediaCatalog.ResolveVideo(seed.VideoFileName);
            var channel = new CctvChannel
            {
                ChannelId = seed.ChannelId,
                Name = seed.Name,
                ZoneLabel = seed.ZoneLabel,
                Address = seed.Address,
                Status = ChannelStatus.Offline,
                VideoSource = new LocalFileVideoSource(videoPath),
                DefaultImagePath = seed.PosterAssetsFile
            };
            Channels.Add(channel);
        }
    }

    private void OnChannelBatchReceived(ChannelDetectionBatchDto batch)
    {
        BaseViewModel.RunOnUi(() =>
        {
            _systemMonitor.AiStatusText = "AI 분석 수신 완료 (Running)";
            Services.BackendManager.Log(
                $"[CctvMonitoring] channel_batch {batch.CctvId}: {batch.Detections?.Count ?? 0} detections");

            var channel = Channels.FirstOrDefault(c => c.ChannelId == batch.CctvId);
            if (channel != null)
                channel.ApplyDetectionBatch(batch);

            _systemMonitor.ResetAiStatusAfterDelay();
        });
    }
}
