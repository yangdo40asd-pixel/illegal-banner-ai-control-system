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
    private static readonly SolidColorBrush BoxBrush = new(Color.FromRgb(0xFF, 0x47, 0x57));
    private static readonly SolidColorBrush LabelBrush = new(Color.FromArgb(0xD9, 0x00, 0x00, 0x00));

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
        BoxBrush.Freeze();
        LabelBrush.Freeze();
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
            or nameof(BannerDetectionEventDto.OcrText))
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

        if (Alert == null)
            return;

        RenderWithFrame(Alert);
    }

    private void RenderOverlayOnly(IReadOnlyList<BannerDetectionEventDto> alerts)
    {
        var (nativeW, nativeH) = ResolveNativeDimensions();
        var anyDrawn = false;

        foreach (var alert in alerts)
        {
            var mapped = YoloOverlayMath.MapToContainer(
                alert.BoundingBox, nativeW, nativeH, ActualWidth, ActualHeight);

            if (mapped.Width <= 0 || mapped.Height <= 0)
                continue;

            OverlayCanvas.Children.Add(CreateBBox(mapped.Left, mapped.Top, mapped.Width, mapped.Height, alert));
            anyDrawn = true;
        }

        if (anyDrawn)
            OverlayCanvas.Visibility = Visibility.Visible;
    }

    private void RenderWithFrame(BannerDetectionEventDto alert)
    {
        var frame = ImageDimensionHelper.LoadBitmap(
            !string.IsNullOrEmpty(FrameSource) ? FrameSource : alert.FullImageBase64);

        double nativeW = frame?.PixelWidth ?? YoloOverlayMath.DefaultImageWidth;
        double nativeH = frame?.PixelHeight ?? YoloOverlayMath.DefaultImageHeight;

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

        var bbox = alert.BoundingBox;
        if (YoloOverlayMath.IsValid(bbox))
            NativeCanvas.Children.Add(CreateBBox(bbox.X, bbox.Y, bbox.Width, bbox.Height, alert));
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

    private FrameworkElement CreateBBox(double left, double top, double width, double height, BannerDetectionEventDto alert)
    {
        var box = new Border
        {
            BorderBrush = BoxBrush,
            BorderThickness = new Thickness(2),
            Width = width,
            Height = height
        };

        var labels = new Grid();
        box.Child = labels;

        labels.Children.Add(new Border
        {
            Background = LabelBrush,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(-2, -18, 0, 0),
            Padding = new Thickness(4, 1, 4, 1),
            CornerRadius = new CornerRadius(1),
            Child = new TextBlock
            {
                Text = alert.YoloDetectionLabelText,
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold
            }
        });

        labels.Children.Add(new Border
        {
            Background = LabelBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, -18),
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(1),
            Child = new TextBlock
            {
                Text = $"[ID: {alert.ObjectIdText}]",
                Foreground = Brushes.White,
                FontSize = 9,
                FontWeight = FontWeights.Bold
            }
        });

        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        return box;
    }
}