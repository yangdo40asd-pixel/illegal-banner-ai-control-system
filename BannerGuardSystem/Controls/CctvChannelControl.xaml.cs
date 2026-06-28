using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BannerGuardSystem.Models;
using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace BannerGuardSystem.Controls;

public partial class CctvChannelControl : UserControl
{
    private const int MinLayoutSize = 10;
    private const int MaxPlayRetries = 4;
    private const int StaggerMsPerChannel = 200;
    private const long FrameReadyTimeMs = 80;

    private Point _startPoint;
    private bool _isDrawing;
    private CctvChannel? _channel;
    private CctvChannel? _subscribedChannel;
    private VlcMediaPlayer? _mediaPlayer;
    private VlcMedia? _currentMedia;
    private bool _playbackRequested;
    private bool _isPlaying;
    private int _playRetryCount;
    private bool _placeholderHidden;
    private DispatcherTimer? _retryTimer;

    public static readonly DependencyProperty IsRoiModeActiveProperty =
        DependencyProperty.Register(nameof(IsRoiModeActive), typeof(bool), typeof(CctvChannelControl), new PropertyMetadata(false));

    public bool IsRoiModeActive
    {
        get => (bool)GetValue(IsRoiModeActiveProperty);
        set => SetValue(IsRoiModeActiveProperty, value);
    }

    public CctvChannelControl()
    {
        InitializeComponent();
        DataContextChanged += CctvChannelControl_DataContextChanged;
        Loaded += CctvChannelControl_Loaded;
        Unloaded += CctvChannelControl_Unloaded;
    }

    private void CctvChannelControl_Loaded(object sender, RoutedEventArgs e)
    {
        VideoContainerGrid.SizeChanged += VideoContainerGrid_SizeChanged;
        AttachChannel(_channel ?? DataContext as CctvChannel);
        RequestPlayback();
        Dispatcher.BeginInvoke(RefreshYoloOverlay, DispatcherPriority.Loaded);
    }

    private void CctvChannelControl_Unloaded(object sender, RoutedEventArgs e)
    {
        VideoContainerGrid.SizeChanged -= VideoContainerGrid_SizeChanged;
        YoloPopup.IsOpen = false;
        DetachChannel();
        CancelRetryTimer();
        StopPlayback();
        _channel = null;
    }

    private void CctvChannelControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(e.OldValue, e.NewValue))
            return;

        DetachChannel();
        CancelRetryTimer();
        StopPlayback();
        _channel = e.NewValue as CctvChannel;
        AttachChannel(_channel);
        RefreshYoloOverlay();
        if (IsLoaded)
            RequestPlayback();
    }

    private void AttachChannel(CctvChannel? channel)
    {
        DetachChannel();
        _subscribedChannel = channel;
        if (_subscribedChannel != null)
            _subscribedChannel.PropertyChanged += Channel_PropertyChanged;
    }

    private void DetachChannel()
    {
        if (_subscribedChannel != null)
        {
            _subscribedChannel.PropertyChanged -= Channel_PropertyChanged;
            _subscribedChannel = null;
        }
    }

    private void Channel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CctvChannel.CurrentAlerts)
            or nameof(CctvChannel.HasDetections)
            or nameof(CctvChannel.OverlayFrameWidth)
            or nameof(CctvChannel.OverlayFrameHeight)
            or nameof(CctvChannel.IsAlerting)
            or nameof(CctvChannel.CurrentAlert))
        {
            Dispatcher.BeginInvoke(RefreshYoloOverlay);
        }
    }

    private void RefreshYoloOverlay()
    {
        SyncYoloPopupLayout();
        YoloOverlay.Render();
    }

    private void SyncYoloPopupLayout()
    {
        if (!IsLoaded || _channel == null)
            return;

        if (!HasValidLayout())
        {
            YoloPopup.IsOpen = false;
            return;
        }

        var w = VideoContainerGrid.ActualWidth;
        var h = VideoContainerGrid.ActualHeight;
        YoloOverlay.Width = w;
        YoloOverlay.Height = h;
        YoloPopup.HorizontalOffset = 0;
        YoloPopup.VerticalOffset = 0;
        YoloPopup.IsOpen = _channel.HasDetections;
    }

    private void VideoContainerGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshYoloOverlay();

        if (!HasValidLayout(e.NewSize))
            return;

        if (_playbackRequested && (!_isPlaying || _mediaPlayer == null))
            TryStartPlaybackDeferred();
    }

    private void RequestPlayback()
    {
        _playbackRequested = true;
        _playRetryCount = 0;
        ShowPlaceholder();
        TryStartPlaybackDeferred();
    }

    private void TryStartPlaybackDeferred()
    {
        if (!_playbackRequested || _channel == null)
            return;

        var staggerMs = GetStaggerIndex(_channel) * StaggerMsPerChannel;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!_playbackRequested || _channel == null)
                return;

            if (!HasValidLayout())
            {
                ScheduleRetry(150);
                return;
            }

            if (_isPlaying && _mediaPlayer != null)
                return;

            if (staggerMs > 0)
            {
                var staggerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(staggerMs) };
                staggerTimer.Tick += (_, _) =>
                {
                    staggerTimer.Stop();
                    StartPlayback();
                };
                staggerTimer.Start();
            }
            else
            {
                StartPlayback();
            }
        });
    }

    private void StartPlayback()
    {
        if (!IsLoaded || _channel == null || !_playbackRequested)
            return;

        if (!HasValidLayout())
        {
            ScheduleRetry(150);
            return;
        }

        if (_isPlaying && _mediaPlayer != null)
            return;

        try
        {
            var path = _channel.VideoSource.GetMediaUri();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Services.BackendManager.Log($"[CctvChannelControl] Video not found: {path}");
                ShowPlaceholder();
                _channel.Status = ChannelStatus.Offline;
                return;
            }

            ResetPlayer();
            _channel.Status = ChannelStatus.Connecting;
            ShowPlaceholder();

            var fullPath = System.IO.Path.GetFullPath(path);
            _mediaPlayer = new VlcMediaPlayer(App.LibVLC);
            _mediaPlayer.Playing += MediaPlayer_Playing;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mediaPlayer.Mute = true;
            _mediaPlayer.AspectRatio = null;
            _mediaPlayer.Scale = 0;

            ChannelVideoView.MediaPlayer = _mediaPlayer;

            _currentMedia = new VlcMedia(App.LibVLC, fullPath, FromType.FromPath);
            _currentMedia.AddOption(":input-repeat=65535");

            var started = _mediaPlayer.Play(_currentMedia);
            if (!started)
                throw new InvalidOperationException("MediaPlayer.Play returned false.");

            _ = ParseNativeDimensionsAsync(_currentMedia);
        }
        catch (Exception ex)
        {
            Services.BackendManager.Log($"[CctvChannelControl] Playback failed ({_channel?.ChannelId}, attempt {_playRetryCount + 1}): {ex.Message}");
            ShowPlaceholder();
            if (_channel != null)
                _channel.Status = ChannelStatus.Offline;

            ResetPlayer();
            ScheduleRetry(300);
        }
    }

    private void StopPlayback()
    {
        _playbackRequested = false;
        _isPlaying = false;
        CancelRetryTimer();
        ResetPlayer();
        ShowPlaceholder();
    }

    private void ResetPlayer()
    {
        _placeholderHidden = false;
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
                _mediaPlayer.Playing -= MediaPlayer_Playing;
                _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
                _mediaPlayer = null;
            }

            ChannelVideoView.MediaPlayer = null;
            _currentMedia?.Dispose();
            _currentMedia = null;
        }
        catch
        {
            // VLC dispose can throw if HWND is already gone.
        }
    }

    private void ScheduleRetry(int delayMs)
    {
        if (!_playbackRequested || _channel == null || _playRetryCount >= MaxPlayRetries)
            return;

        _playRetryCount++;
        CancelRetryTimer();

        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        _retryTimer.Tick += (_, _) =>
        {
            CancelRetryTimer();
            StartPlayback();
        };
        _retryTimer.Start();
    }

    private void CancelRetryTimer()
    {
        if (_retryTimer == null)
            return;

        _retryTimer.Stop();
        _retryTimer = null;
    }

    private bool HasValidLayout() => HasValidLayout(new Size(VideoContainerGrid.ActualWidth, VideoContainerGrid.ActualHeight));

    private static bool HasValidLayout(Size size)
        => size.Width > MinLayoutSize && size.Height > MinLayoutSize;

    private static int GetStaggerIndex(CctvChannel channel)
    {
        var id = channel.ChannelId;
        if (id.Length >= 2 && int.TryParse(id.AsSpan(id.Length - 2), out var n))
            return Math.Max(0, n - 1);
        return 0;
    }

    private void ShowPlaceholder()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowPlaceholder);
            return;
        }

        PlaceholderImage.Visibility = Visibility.Visible;
    }

    private void HidePlaceholderWhenReady()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HidePlaceholderWhenReady);
            return;
        }

        PlaceholderImage.Visibility = Visibility.Collapsed;
    }

    private void MediaPlayer_Playing(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_channel == null || _mediaPlayer == null)
                return;

            _isPlaying = true;
            _playRetryCount = 0;
            // 탐지 수신 후 재생되면 Alerting 유지 — Online으로 덮으면 YOLO 박스가 사라짐
            _channel.Status = _channel.CurrentAlerts.Count > 0
                ? ChannelStatus.Alerting
                : ChannelStatus.Online;

            RefreshYoloOverlay();
        });
    }

    private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (_channel != null)
            _channel.PlaybackPositionMs = e.Time;

        if (_placeholderHidden || e.Time < FrameReadyTimeMs)
            return;

        _placeholderHidden = true;
        Dispatcher.BeginInvoke(HidePlaceholderWhenReady);
    }

    private async Task ParseNativeDimensionsAsync(VlcMedia media)
    {
        if (_channel == null)
            return;

        try
        {
            await Task.Run(() => media.Parse(MediaParseOptions.ParseLocal)).ConfigureAwait(false);

            await Dispatcher.InvokeAsync(() =>
            {
                if (_channel == null)
                    return;

                foreach (var track in media.Tracks)
                {
                    if (track.TrackType != TrackType.Video)
                        continue;

                    var w = track.Data.Video.Width;
                    var h = track.Data.Video.Height;
                    if (w > 0 && h > 0)
                    {
                        _channel.OverlayFrameWidth = (int)w;
                        _channel.OverlayFrameHeight = (int)h;
                    }
                    break;
                }
            });
        }
        catch
        {
            // channel_batch 수신 시 FrameWidth/Height로 갱신됨
        }
    }

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Services.BackendManager.Log($"[CctvChannelControl] VLC error: {_channel?.ChannelId}");
            _isPlaying = false;
            ShowPlaceholder();
            if (_channel != null)
                _channel.Status = ChannelStatus.Offline;

            ResetPlayer();
            ScheduleRetry(500);
        });
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e)
    {
        // :input-repeat=65535
    }

    private void RoiCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsRoiModeActive) return;

        _startPoint = e.GetPosition(RoiCanvas);
        _isDrawing = true;

        TempRoiRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(TempRoiRect, _startPoint.X);
        Canvas.SetTop(TempRoiRect, _startPoint.Y);
        TempRoiRect.Width = 0;
        TempRoiRect.Height = 0;
        RoiCanvas.CaptureMouse();
    }

    private void RoiCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;

        var currentPoint = e.GetPosition(RoiCanvas);
        Canvas.SetLeft(TempRoiRect, Math.Min(_startPoint.X, currentPoint.X));
        Canvas.SetTop(TempRoiRect, Math.Min(_startPoint.Y, currentPoint.Y));
        TempRoiRect.Width = Math.Abs(_startPoint.X - currentPoint.X);
        TempRoiRect.Height = Math.Abs(_startPoint.Y - currentPoint.Y);
    }

    private void RoiCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        RoiCanvas.ReleaseMouseCapture();

        if (TempRoiRect.Width < 5 || TempRoiRect.Height < 5)
        {
            TempRoiRect.Visibility = Visibility.Collapsed;
            return;
        }

        var finalRect = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x50, 0x3F, 0x3F, 0x46)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            StrokeThickness = 1,
            Width = TempRoiRect.Width,
            Height = TempRoiRect.Height
        };

        Canvas.SetLeft(finalRect, Canvas.GetLeft(TempRoiRect));
        Canvas.SetTop(finalRect, Canvas.GetTop(TempRoiRect));

        var menu = new ContextMenu();
        var deleteItem = new MenuItem { Header = "제외 구역 삭제" };
        deleteItem.Click += (_, _) => RoiCanvas.Children.Remove(finalRect);
        menu.Items.Add(deleteItem);
        finalRect.ContextMenu = menu;

        RoiCanvas.Children.Add(finalRect);
        TempRoiRect.Visibility = Visibility.Collapsed;
    }
}