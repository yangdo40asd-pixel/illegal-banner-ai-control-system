using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerGuardSystem.Helpers;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Controls;

/// <summary>
/// YOLO 탐지 전용 오버레이. VLM 판정 색/라벨 사용 안 함.
/// OverlayOnly: CCTV — MP4 위 투명 박스(복수). 그 외: Viewbox + 분석 프레임 + 박스.
/// </summary>
public partial class YoloBBoxOverlay : UserControl
{
    private static readonly SolidColorBrush CctvBoxBrush = new(Color.FromRgb(0xFF, 0x47, 0x57));
    private static readonly SolidColorBrush DetailBoxBrush = new(Color.FromRgb(0x00, 0xF5, 0xFF));
    private static readonly SolidColorBrush DetailGlowBrush = new(Color.FromArgb(0x99, 0x00, 0xF5, 0xFF));
    private static readonly SolidColorBrush LabelBrush = new(Color.FromArgb(0xE6, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush DetailLabelBrush = new(Color.FromArgb(0xF2, 0x0F, 0x17, 0x2A));

    private BannerDetectionEventDto? _subscribedAlert;
    private INotifyCollectionChanged? _alertsCollection;

    public static readonly DependencyProperty AlertProperty =
        DependencyProperty.Register(nameof(Alert), typeof(BannerDetectionEventDto), typeof(YoloBBoxOverlay),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty AlertsProperty =
        DependencyProperty.Register(nameof(Alerts), typeof(IEnumerable), typeof(YoloBBoxOverlay),
            new PropertyMetadata(null, OnAlertsChanged));

    public static readonly DependencyProperty FrameSourceProperty =
        DependencyProperty.Register(nameof(FrameSource), typeof(string), typeof(YoloBBoxOverlay),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(YoloBBoxOverlay),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty FrameOpacityProperty =
        DependencyProperty.Register(nameof(FrameOpacity), typeof(double), typeof(YoloBBoxOverlay),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty OverlayOnlyProperty =
        DependencyProperty.Register(nameof(OverlayOnly), typeof(bool), typeof(YoloBBoxOverlay),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty NativeFrameWidthProperty =
        DependencyProperty.Register(nameof(NativeFrameWidth), typeof(double), typeof(YoloBBoxOverlay),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty NativeFrameHeightProperty =
        DependencyProperty.Register(nameof(NativeFrameHeight), typeof(double), typeof(YoloBBoxOverlay),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    static YoloBBoxOverlay()
    {
        CctvBoxBrush.Freeze();
        DetailBoxBrush.Freeze();
        DetailGlowBrush.Freeze();
        LabelBrush.Freeze();
        DetailLabelBrush.Freeze();
    }

    public BannerDetectionEventDto? Alert
    {
        get => (BannerDetectionEventDto?)GetValue(AlertProperty);
        set => SetValue(AlertProperty, value);
    }

    public IEnumerable? Alerts
    {
        get => (IEnumerable?)GetValue(AlertsProperty);
        set => SetValue(AlertsProperty, value);
    }

    public string FrameSource
    {
        get => (string)GetValue(FrameSourceProperty);
        set => SetValue(FrameSourceProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double FrameOpacity
    {
        get => (double)GetValue(FrameOpacityProperty);
        set => SetValue(FrameOpacityProperty, value);
    }

    public bool OverlayOnly
    {
        get => (bool)GetValue(OverlayOnlyProperty);
        set => SetValue(OverlayOnlyProperty, value);
    }

    public double NativeFrameWidth
    {
        get => (double)GetValue(NativeFrameWidthProperty);
        set => SetValue(NativeFrameWidthProperty, value);
    }

    public double NativeFrameHeight
    {
        get => (double)GetValue(NativeFrameHeightProperty);
        set => SetValue(NativeFrameHeightProperty, value);
    }

    public YoloBBoxOverlay()
    {
        InitializeComponent();
        Loaded += OnOverlayLoaded;
        SizeChanged += (_, _) => Render();
        DataContextChanged += (_, _) => Render();
    }

    private void OnOverlayLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeAlertsCollection(null, Alerts);
        Render();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (YoloBBoxOverlay)d;
        if (e.Property == AlertProperty)
            overlay.OnAlertChanged();
        overlay.Render();
    }

    private static void OnAlertsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (YoloBBoxOverlay)d;
        overlay.SubscribeAlertsCollection(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
        overlay.Render();
    }

    private void SubscribeAlertsCollection(IEnumerable? oldAlerts, IEnumerable? newAlerts)
    {
        if (_alertsCollection != null)
            _alertsCollection.CollectionChanged -= Alerts_CollectionChanged;

        _alertsCollection = newAlerts as INotifyCollectionChanged;
        if (_alertsCollection != null)
            _alertsCollection.CollectionChanged += Alerts_CollectionChanged;
    }

    private void Alerts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render();

    private void OnAlertChanged()
    {
        if (_subscribedAlert != null)
            _subscribedAlert.PropertyChanged -= Alert_PropertyChanged;

        _subscribedAlert = Alert;
        if (_subscribedAlert != null)
        {
            _subscribedAlert.PropertyChanged += Alert_PropertyChanged;
            if (!OverlayOnly && !string.IsNullOrEmpty(_subscribedAlert.FullImageBase64))
                FrameSource = _subscribedAlert.FullImageBase64;
        }
    }

    private void Alert_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BannerDetectionEventDto.BoundingBox)
            or nameof(BannerDetectionEventDto.FullImageBase64)
            or nameof(BannerDetectionEventDto.VlmAnalysis)
            or nameof(BannerDetectionEventDto.OcrText)
            or nameof(BannerDetectionEventDto.Confidence)
            or nameof(BannerDetectionEventDto.OcrConfidence)
            or nameof(BannerDetectionEventDto.IsVlmTriggered))
        {
            if (!OverlayOnly && Alert != null && !string.IsNullOrEmpty(Alert.FullImageBase64))
                FrameSource = Alert.FullImageBase64;
            Render();
        }
    }

    private IEnumerable<BannerDetectionEventDto> GetOverlayAlerts()
    {
        if (Alerts != null)
        {
            var fromList = Alerts.Cast<object>()
                .OfType<BannerDetectionEventDto>()
                .Where(a => YoloOverlayMath.IsValid(a.BoundingBox))
                .ToList();
            if (fromList.Count > 0)
                return fromList;
        }

        if (Alert != null && YoloOverlayMath.IsValid(Alert.BoundingBox))
            return new[] { Alert };

        return Enumerable.Empty<BannerDetectionEventDto>();
    }

    public void Render()
    {
        NativeCanvas.Children.Clear();
        OverlayCanvas.Children.Clear();
        ScaleBox.Visibility = Visibility.Collapsed;
        OverlayCanvas.Visibility = Visibility.Collapsed;

        if (!IsActive)
            return;

        if (OverlayOnly)
        {
            var alerts = GetOverlayAlerts().ToList();
            if (alerts.Count == 0)
                return;
            RenderOverlayOnly(alerts);
            return;
        }

        var frameAlerts = GetOverlayAlerts().ToList();
        if (frameAlerts.Count == 0)
            return;

        RenderWithFrame(frameAlerts);
    }

    private void RenderOverlayOnly(IReadOnlyList<BannerDetectionEventDto> alerts)
    {
        var (nativeW, nativeH) = ResolveNativeDimensions();
        var containerW = ActualWidth;
        var containerH = ActualHeight;
        if (containerW <= 1 || containerH <= 1)
        {
            if (Parent is FrameworkElement parent && parent.ActualWidth > 1 && parent.ActualHeight > 1)
            {
                containerW = parent.ActualWidth;
                containerH = parent.ActualHeight;
            }
            else
            {
                return;
            }
        }

        OverlayCanvas.Width = containerW;
        OverlayCanvas.Height = containerH;

        var anyDrawn = false;

        for (var i = 0; i < alerts.Count; i++)
        {
            var alert = alerts[i];
            var mapped = YoloOverlayMath.MapToContainer(
                alert.BoundingBox, nativeW, nativeH, containerW, containerH);

            if (mapped.Width <= 0 || mapped.Height <= 0)
                continue;

            OverlayCanvas.Children.Add(CreateBBox(mapped.Left, mapped.Top, mapped.Width, mapped.Height, alert, i + 1));
            anyDrawn = true;
        }

        if (anyDrawn)
            OverlayCanvas.Visibility = Visibility.Visible;
    }

    private void RenderWithFrame(IReadOnlyList<BannerDetectionEventDto> alerts)
    {
        var primary = alerts[0];
        var frame = ImageDimensionHelper.LoadBitmap(
            !string.IsNullOrEmpty(FrameSource) ? FrameSource : primary.FullImageBase64);

        double nativeW = NativeFrameWidth > 0 ? NativeFrameWidth : frame?.PixelWidth ?? YoloOverlayMath.DefaultImageWidth;
        double nativeH = NativeFrameHeight > 0 ? NativeFrameHeight : frame?.PixelHeight ?? YoloOverlayMath.DefaultImageHeight;

        NativeCanvas.Width = nativeW;
        NativeCanvas.Height = nativeH;

        if (frame != null)
        {
            NativeCanvas.Children.Add(new Image
            {
                Source = frame,
                Width = nativeW,
                Height = nativeH,
                Opacity = FrameOpacity
            });
        }

        for (var i = 0; i < alerts.Count; i++)
        {
            var alert = alerts[i];
            var bbox = alert.BoundingBox;
            if (YoloOverlayMath.IsValid(bbox))
                NativeCanvas.Children.Add(CreateBBox(bbox.X, bbox.Y, bbox.Width, bbox.Height, alert, i + 1));
        }

        ScaleBox.Visibility = Visibility.Visible;
    }

    private (double Width, double Height) ResolveNativeDimensions()
    {
        if (NativeFrameWidth > 0 && NativeFrameHeight > 0)
            return (NativeFrameWidth, NativeFrameHeight);

        var source = !string.IsNullOrEmpty(FrameSource)
            ? FrameSource
            : Alert?.FullImageBase64;

        if (ImageDimensionHelper.TryDecodeFromBase64(source, out var w, out var h))
            return (w, h);

        if (OverlayOnly)
            return (YoloOverlayMath.DefaultVideoWidth, YoloOverlayMath.DefaultVideoHeight);

        return (YoloOverlayMath.DefaultImageWidth, YoloOverlayMath.DefaultImageHeight);
    }

    private FrameworkElement CreateBBox(double left, double top, double width, double height, BannerDetectionEventDto alert, int detectionIndex = 1)
    {
        bool isDetailPanel = !OverlayOnly;
        double borderThickness = isDetailPanel ? 4 : 2;
        var boxBrush = isDetailPanel ? DetailBoxBrush : CctvBoxBrush;
        var labelBrush = isDetailPanel ? DetailLabelBrush : LabelBrush;
        int topFontSize = isDetailPanel ? 12 : 9;
        int bottomFontSize = isDetailPanel ? 11 : 9;

        var root = new Grid { Width = width, Height = height };

        if (isDetailPanel)
        {
            root.Children.Add(new Border
            {
                BorderBrush = DetailGlowBrush,
                BorderThickness = new Thickness(8),
                Width = width,
                Height = height
            });
        }

        var box = new Border
        {
            BorderBrush = boxBrush,
            BorderThickness = new Thickness(borderThickness),
            Width = width,
            Height = height
        };
        root.Children.Add(box);

        if (isDetailPanel)
            AddCornerMarkers(root, width, height, boxBrush, borderThickness);

        var labels = new Grid();
        box.Child = labels;

        labels.Children.Add(new Border
        {
            Background = labelBrush,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-borderThickness, isDetailPanel ? -52 : -18, 0, 0),
            Padding = new Thickness(isDetailPanel ? 8 : 4, isDetailPanel ? 4 : 1, isDetailPanel ? 8 : 4, isDetailPanel ? 4 : 1),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock
            {
                Text = isDetailPanel
                    ? $"YOLO 탐지 {alert.YoloConfidenceText}"
                    : alert.CctvOverlayTopLabel(detectionIndex),
                Foreground = isDetailPanel ? boxBrush : Brushes.White,
                FontSize = topFontSize,
                FontWeight = FontWeights.Bold
            }
        });

        if (isDetailPanel)
        {
            labels.Children.Add(new Border
            {
                Background = labelBrush,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, -borderThickness, -borderThickness, 0),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = alert.IsVlmTriggered
                        ? $"문자 인식률 {alert.OcrConfidenceText}"
                        : "문자 인식률 분석 중…",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
                    FontSize = topFontSize,
                    FontWeight = FontWeights.Bold
                }
            });
        }

        labels.Children.Add(new Border
        {
            Background = labelBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, isDetailPanel ? -28 : -18),
            Padding = new Thickness(isDetailPanel ? 8 : 6, isDetailPanel ? 3 : 1, isDetailPanel ? 8 : 6, isDetailPanel ? 3 : 1),
            CornerRadius = new CornerRadius(3),
            Child = new TextBlock
            {
                Text = isDetailPanel
                    ? $"ID {alert.ObjectIdText} · {alert.StageText}"
                    : alert.CctvOverlayIdLabel(detectionIndex),
                Foreground = Brushes.White,
                FontSize = bottomFontSize,
                FontWeight = FontWeights.Bold
            }
        });

        Canvas.SetLeft(root, left);
        Canvas.SetTop(root, top);
        return root;
    }

    private static void AddCornerMarkers(Grid root, double width, double height, Brush brush, double thickness)
    {
        const double len = 14;
        foreach (var (left, top, w, h) in new[]
        {
            (0.0, 0.0, len, thickness),
            (0.0, 0.0, thickness, len),
            (width - len, 0.0, len, thickness),
            (width - thickness, 0.0, thickness, len),
            (0.0, height - thickness, len, thickness),
            (0.0, height - len, thickness, len),
            (width - len, height - thickness, len, thickness),
            (width - thickness, height - len, thickness, len),
        })
        {
            root.Children.Add(new Border
            {
                Background = brush,
                Width = w,
                Height = h,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(left, top, 0, 0)
            });
        }
    }
}