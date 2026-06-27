using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Controls;

public partial class CctvChannelControl : UserControl
{
    private Point _startPoint;
    private bool _isDrawing;
    private CctvChannel? _channel;

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

    private void CctvChannelControl_Loaded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, StartPlayback);

    private void CctvChannelControl_Unloaded(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        _channel = null;
    }

    private void CctvChannelControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _channel = e.NewValue as CctvChannel;
        if (IsLoaded)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, StartPlayback);
    }

    private void StartPlayback()
    {
        if (!IsLoaded || _channel == null) return;

        try
        {
            var uri = _channel.VideoFileUri;
            if (uri == null)
            {
                ShowPlaceholder();
                _channel.Status = ChannelStatus.Offline;
                return;
            }

            _channel.Status = ChannelStatus.Connecting;
            ChannelMediaElement.Source = uri;
            ChannelMediaElement.Position = TimeSpan.Zero;
            ChannelMediaElement.Play();
            PlaceholderImage.Visibility = Visibility.Collapsed;
            _channel.Status = ChannelStatus.Online;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CctvChannelControl] Playback failed: {ex.Message}");
            ShowPlaceholder();
            if (_channel != null)
                _channel.Status = ChannelStatus.Offline;
        }
    }

    private void StopPlayback()
    {
        try
        {
            ChannelMediaElement.Stop();
            ChannelMediaElement.Close();
            ChannelMediaElement.Source = null;
        }
        catch { }
    }

    private void ShowPlaceholder() => PlaceholderImage.Visibility = Visibility.Visible;

    private void ChannelMediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowPlaceholder();
            if (_channel != null)
                _channel.Status = ChannelStatus.Offline;
        });
    }

    private void ChannelMediaElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        try
        {
            ChannelMediaElement.Position = TimeSpan.Zero;
            ChannelMediaElement.Play();
        }
        catch { }
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