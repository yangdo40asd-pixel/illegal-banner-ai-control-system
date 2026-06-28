using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    private string _zoneLabel = "A";
    public string ZoneLabel
    {
        get => _zoneLabel;
        set
        {
            if (_zoneLabel != value)
            {
                _zoneLabel = value;
                OnPropertyChanged();
            }
        }
    }
    public string Gu { get; set; } = string.Empty;
    public string Dong { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    public string DefaultImagePath { get; set; } = string.Empty;

    public IVideoSource VideoSource { get; set; } = new LocalFileVideoSource(string.Empty);

    /// <summary>VLC 재생 위치(ms) — LIVE YOLO 프레임 동기화용.</summary>
    public long PlaybackPositionMs { get; set; }

    public ObservableCollection<BannerDetectionEventDto> CurrentAlerts { get; } = new();

    private int _overlayFrameWidth = 1920;
    public int OverlayFrameWidth
    {
        get => _overlayFrameWidth;
        set
        {
            if (_overlayFrameWidth != value)
            {
                _overlayFrameWidth = value;
                OnPropertyChanged();
            }
        }
    }

    private int _overlayFrameHeight = 1080;
    public int OverlayFrameHeight
    {
        get => _overlayFrameHeight;
        set
        {
            if (_overlayFrameHeight != value)
            {
                _overlayFrameHeight = value;
                OnPropertyChanged();
            }
        }
    }

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
                OnPropertyChanged(nameof(IsAlerting));
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
            OnPropertyChanged(nameof(DisplayImagePath));
            OnPropertyChanged(nameof(DetectionFrameSource));
        }
    }

    public string DisplayImagePath => DefaultImagePath;

    public string DetectionFrameSource =>
        !string.IsNullOrEmpty(_currentAlert?.FullImageBase64)
            ? _currentAlert.FullImageBase64
            : DefaultImagePath;

    public void ApplyDetectionBatch(ChannelDetectionBatchDto batch)
    {
        var valid = batch.Detections
            .Where(d => d.HasBanner && d.BoundingBox.Width > 0 && d.BoundingBox.Height > 0)
            .ToList();

        if (valid.Count == 0)
            return;

        CurrentAlerts.Clear();
        foreach (var detection in valid)
            CurrentAlerts.Add(detection);

        OverlayFrameWidth = batch.FrameWidth > 0 ? batch.FrameWidth : 1920;
        OverlayFrameHeight = batch.FrameHeight > 0 ? batch.FrameHeight : 1080;
        CurrentAlert = CurrentAlerts.FirstOrDefault();

        Status = CurrentAlerts.Count > 0 ? ChannelStatus.Alerting : ChannelStatus.Online;
        OnPropertyChanged(nameof(CurrentAlerts));
        OnPropertyChanged(nameof(CurrentAlert));
        OnPropertyChanged(nameof(HasDetections));
        OnPropertyChanged(nameof(IsAlerting));

        Services.BackendManager.Log(
            $"[CctvChannel] {ChannelId} batch applied: alerts={CurrentAlerts.Count}, frame={OverlayFrameWidth}x{OverlayFrameHeight}");
    }

    public void ClearDetections()
    {
        CurrentAlerts.Clear();
        CurrentAlert = null;
        Status = ChannelStatus.Online;
        OnPropertyChanged(nameof(HasDetections));
    }

    public Uri? VideoFileUri
    {
        get
        {
            var path = VideoSource.GetMediaUri();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            return new Uri(Path.GetFullPath(path));
        }
    }

    public bool HasVideoFile => VideoFileUri != null;

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
        ChannelStatus.Online => "🟢",
        ChannelStatus.Alerting => "🔴",
        ChannelStatus.Offline => "⚫",
        ChannelStatus.Connecting => "🟡",
        _ => "⚫"
    };

    public bool HasDetections => CurrentAlerts.Count > 0;

    public bool IsAlerting => Status == ChannelStatus.Alerting;

    public string StatusText => Status switch
    {
        ChannelStatus.Online => "Online",
        ChannelStatus.Alerting => "Alerting",
        ChannelStatus.Offline => "Offline",
        ChannelStatus.Connecting => "Connecting",
        _ => "Unknown"
    };

    public void Dispose() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}