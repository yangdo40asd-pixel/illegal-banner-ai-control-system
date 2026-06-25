using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LibVLCSharp.Shared;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.Models;

public enum ChannelStatus
{
    Online,
    Alerting,
    Offline,
    Connecting
}

public class CctvChannel : INotifyPropertyChanged, IDisposable
{
    public string ChannelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Gu { get; set; } = string.Empty;
    public string Dong { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    public string DefaultImagePath { get; set; } = string.Empty;

    public IVideoSource VideoSource { get; set; } = new LocalFileVideoSource(string.Empty);
    public MediaPlayer MediaPlayer { get; }

    private ChannelStatus _status = ChannelStatus.Offline;
    public ChannelStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusDot));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private BannerAlert? _lastAlert;
    public BannerAlert? LastAlert
    {
        get => _lastAlert;
        set
        {
            _lastAlert = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAlert));
        }
    }

    private BannerDetectionEventDto? _currentAlert;
    public BannerDetectionEventDto? CurrentAlert
    {
        get => _currentAlert;
        set
        {
            _currentAlert = value;
            OnPropertyChanged();
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasAlert => LastAlert != null && LastAlert.Status == AlertStatus.Pending;

    public string StatusDot => Status switch
    {
        ChannelStatus.Online    => "🟢",
        ChannelStatus.Alerting  => "🔴",
        ChannelStatus.Offline   => "⚫",
        ChannelStatus.Connecting => "🟡",
        _                       => "⚫"
    };

    public string StatusText => Status switch
    {
        ChannelStatus.Online    => "실시간 송출 중",
        ChannelStatus.Alerting  => "위반 탐지됨!",
        ChannelStatus.Offline   => "오프라인",
        ChannelStatus.Connecting => "연결 중...",
        _                       => "알 수 없음"
    };

    public CctvChannel()
    {
        MediaPlayer = new MediaPlayer(App.LibVLC);
    }

    public void Play()
    {
        var mediaUri = VideoSource.GetMediaUri();
        if (string.IsNullOrEmpty(mediaUri)) return;

        Status = ChannelStatus.Connecting;

        try
        {
            using var media = new Media(App.LibVLC, mediaUri, VideoSource.IsRtsp ? FromType.FromLocation : FromType.FromPath);
            MediaPlayer.Play(media);
            Status = ChannelStatus.Online;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CctvChannel] Play failed: {ex.Message}");
            Status = ChannelStatus.Offline;
        }
    }

    public void Stop()
    {
        try
        {
            MediaPlayer.Stop();
            Status = ChannelStatus.Offline;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CctvChannel] Stop failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            MediaPlayer?.Stop();
            MediaPlayer?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CctvChannel] Dispose failed: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
