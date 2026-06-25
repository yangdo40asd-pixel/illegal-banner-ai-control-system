using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Controls;

public partial class CctvChannelControl : UserControl
{
    private Point _startPoint;
    private bool _isDrawing;

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
    }

    private void CctvChannelControl_Loaded(object sender, RoutedEventArgs e)
    {
        VideoContainerGrid.SizeChanged += VideoContainerGrid_SizeChanged;
    }

    private void VideoContainerGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBoundingBox();
    }

    private void CctvChannelControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CctvChannel oldChannel)
        {
            oldChannel.PropertyChanged -= Channel_PropertyChanged;
        }

        if (e.NewValue is CctvChannel channel)
        {
            VlcVideoView.MediaPlayer = channel.MediaPlayer;
            channel.PropertyChanged += Channel_PropertyChanged;
            UpdateBoundingBox();
        }
    }

    private void Channel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CctvChannel.CurrentAlert) || e.PropertyName == nameof(CctvChannel.Status))
        {
            Dispatcher.BeginInvoke(new Action(UpdateBoundingBox));
        }
    }

    private void UpdateBoundingBox()
    {
        if (DataContext is not CctvChannel channel || channel.Status != ChannelStatus.Alerting || channel.CurrentAlert == null)
        {
            YoloCanvas.Visibility = Visibility.Collapsed;
            YoloCanvas.Children.Clear();
            return;
        }

        var alert = channel.CurrentAlert;
        var bbox = alert.BoundingBox;

        double containerW = VideoContainerGrid.ActualWidth;
        double containerH = VideoContainerGrid.ActualHeight;

        if (containerW <= 0 || containerH <= 0)
        {
            YoloCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        // Default native image size (4032x2268 fallback for asset mockups)
        double imgW = 4032;
        double imgH = 2268;

        if (PlaceholderImage.Source is System.Windows.Media.Imaging.BitmapSource bitmap)
        {
            imgW = bitmap.PixelWidth;
            imgH = bitmap.PixelHeight;
        }

        // Uniform scaling calculation (fixes coordinates shifting)
        double scaleX = containerW / imgW;
        double scaleY = containerH / imgH;
        double scale = Math.Min(scaleX, scaleY); // Math.Min preserves aspect ratio without cropping

        double renderedW = imgW * scale;
        double renderedH = imgH * scale;
        double offsetX = (containerW - renderedW) / 2;
        double offsetY = (containerH - renderedH) / 2;

        // Map box coordinates
        double left = bbox.X * scale + offsetX;
        double top = bbox.Y * scale + offsetY;
        double width = bbox.Width * scale;
        double height = bbox.Height * scale;

        YoloCanvas.Children.Clear();
        YoloCanvas.Visibility = Visibility.Visible;

        // Create the border representing the bounding box
        var border = new Border
        {
            BorderBrush = alert.VerdictColor,
            BorderThickness = new Thickness(2),
            Width = width,
            Height = height
        };

        var grid = new Grid();
        border.Child = grid;

        // Top Label (Dynamic Bounding Box Label)
        var topLabelBorder = new Border
        {
            Background = alert.VerdictColor,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-2, -18, 0, 0),
            Padding = new Thickness(4, 1, 4, 1),
            CornerRadius = new CornerRadius(1)
        };
        var topLabelText = new TextBlock
        {
            Text = alert.BoundingBoxLabelText,
            Foreground = Brushes.White,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        topLabelBorder.Child = topLabelText;
        grid.Children.Add(topLabelBorder);

        // Bottom Label (Dynamic Object ID)
        var bottomLabelBorder = new Border
        {
            Background = alert.VerdictColor,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -18),
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(1)
        };
        var bottomLabelText = new TextBlock
        {
            Text = $"[ID: {alert.ObjectIdText}]",
            Foreground = Brushes.White,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        bottomLabelBorder.Child = bottomLabelText;
        grid.Children.Add(bottomLabelBorder);

        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        YoloCanvas.Children.Add(border);
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
        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var width = Math.Abs(_startPoint.X - currentPoint.X);
        var height = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(TempRoiRect, x);
        Canvas.SetTop(TempRoiRect, y);
        TempRoiRect.Width = width;
        TempRoiRect.Height = height;
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
            StrokeThickness = 1
        };

        Canvas.SetLeft(finalRect, Canvas.GetLeft(TempRoiRect));
        Canvas.SetTop(finalRect, Canvas.GetTop(TempRoiRect));
        finalRect.Width = TempRoiRect.Width;
        finalRect.Height = TempRoiRect.Height;

        var menu = new ContextMenu();
        var deleteItem = new MenuItem { Header = "제외 구역 삭제" };
        deleteItem.Click += (s, ev) => RoiCanvas.Children.Remove(finalRect);
        menu.Items.Add(deleteItem);
        finalRect.ContextMenu = menu;

        RoiCanvas.Children.Add(finalRect);
        TempRoiRect.Visibility = Visibility.Collapsed;

        System.Diagnostics.Debug.WriteLine($"[ROI Mask Added] X:{Canvas.GetLeft(finalRect)}, Y:{Canvas.GetTop(finalRect)}, W:{finalRect.Width}, H:{finalRect.Height}");
    }
}
