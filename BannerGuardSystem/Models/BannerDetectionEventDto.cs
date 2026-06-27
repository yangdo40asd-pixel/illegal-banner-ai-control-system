using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BannerGuardSystem.Models;

public struct BBox
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public BBox(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

public class BannerDetectionEventDto : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Basic Info
    private string _id = string.Empty;
    public string Id { get => _id; set { if (_id != value) { _id = value; OnPropertyChanged(); } } }
    
    private string _cctvId = string.Empty;
    public string CctvId { get => _cctvId; set { if (_cctvId != value) { _cctvId = value; OnPropertyChanged(); OnPropertyChanged(nameof(CctvName)); } } }
    
    private string _locationAddress = string.Empty;
    public string LocationAddress { get => _locationAddress; set { if (_locationAddress != value) { _locationAddress = value; OnPropertyChanged(); } } }
    
    private DateTime _timestamp = DateTime.Now;
    public DateTime Timestamp { get => _timestamp; set { if (_timestamp != value) { _timestamp = value; OnPropertyChanged(); OnPropertyChanged(nameof(DetectedAtText)); } } }
    
    private TimeSpan _timestampOffset = TimeSpan.Zero;
    public TimeSpan TimestampOffset { get => _timestampOffset; set { if (_timestampOffset != value) { _timestampOffset = value; OnPropertyChanged(); } } }

    // Tier 1 (YOLO)
    private bool _hasBanner;
    public bool HasBanner { get => _hasBanner; set { if (_hasBanner != value) { _hasBanner = value; OnPropertyChanged(); } } }
    
    private BBox _boundingBox;
    public BBox BoundingBox { get => _boundingBox; set { _boundingBox = value; OnPropertyChanged(); OnPropertyChanged(nameof(YoloCoordsText)); } }
    
    private double _confidence;
    public double Confidence { get => _confidence; set { if (_confidence != value) { _confidence = value; OnPropertyChanged(); } } }

    // Tier 2 (OCR)
    private string _ocrText = string.Empty;
    public string OcrText { get => _ocrText; set { if (_ocrText != value) { _ocrText = value; OnPropertyChanged(); } } }
    
    private double _ocrConfidence;
    public double OcrConfidence { get => _ocrConfidence; set { if (_ocrConfidence != value) { _ocrConfidence = value; OnPropertyChanged(); } } }

    // Tier 3 (VLM)
    private bool _isVlmTriggered;
    public bool IsVlmTriggered { get => _isVlmTriggered; set { if (_isVlmTriggered != value) { _isVlmTriggered = value; OnPropertyChanged(); } } }
    
    private string _vlmAnalysis = string.Empty;
    public string VlmAnalysis { get => _vlmAnalysis; set { if (_vlmAnalysis != value) { _vlmAnalysis = value; OnPropertyChanged(); } } }

    // Repeat Offender Flags
    private bool _isRepeatOffender;
    public bool IsRepeatOffender
    {
        get => _isRepeatOffender;
        set
        {
            if (_isRepeatOffender != value)
            {
                _isRepeatOffender = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEmergency));
                OnPropertyChanged(nameof(PriorityLabel));
            }
        }
    }
    
    private int _repeatCount;
    public int RepeatCount { get => _repeatCount; set { if (_repeatCount != value) { _repeatCount = value; OnPropertyChanged(); } } }

    // Image Path
    private string _cropImageUri = string.Empty;
    public string CropImageUri { get => _cropImageUri; set { if (_cropImageUri != value) { _cropImageUri = value; OnPropertyChanged(); } } }
    
    private string _croppedImageBase64 = string.Empty;
    public string CroppedImageBase64 { get => _croppedImageBase64; set { if (_croppedImageBase64 != value) { _croppedImageBase64 = value; OnPropertyChanged(); } } }
    
    private string _fullImageBase64 = string.Empty;
    public string FullImageBase64 { get => _fullImageBase64; set { if (_fullImageBase64 != value) { _fullImageBase64 = value; OnPropertyChanged(); } } }

    private string _sourceImage = string.Empty;
    public string SourceImage { get => _sourceImage; set { if (_sourceImage != value) { _sourceImage = value; OnPropertyChanged(); } } }

    public void UpdateFrom(BannerDetectionEventDto other)
    {
        Id = other.Id;
        LocationAddress = other.LocationAddress;
        Timestamp = other.Timestamp;
        TimestampOffset = other.TimestampOffset;
        HasBanner = other.HasBanner;
        BoundingBox = other.BoundingBox;
        Confidence = other.Confidence;
        OcrText = other.OcrText;
        OcrConfidence = other.OcrConfidence;
        IsVlmTriggered = other.IsVlmTriggered;
        VlmAnalysis = other.VlmAnalysis;
        IsRepeatOffender = other.IsRepeatOffender;
        RepeatCount = other.RepeatCount;
        CropImageUri = other.CropImageUri;
        CroppedImageBase64 = other.CroppedImageBase64;
        FullImageBase64 = other.FullImageBase64;
        SourceImage = other.SourceImage;
        StatusCode = other.StatusCode;
        OnPropertyChanged(nameof(IsEmergency));
        OnPropertyChanged(nameof(PriorityLabel));
    }

    /// <summary>긴급 Feed·알람 집계 기준 (VLM 분석 대기는 제외)</summary>
    public bool IsEmergency =>
        IsRepeatOffender
        || StatusCode is "ILLEGAL_CONTENT" or "ILLEGAL_COMMERCIAL" or "MANUAL_REVIEW";

    public string PriorityLabel => IsEmergency ? "긴급" : "일반";

    // UI Helpers matching MainWindow.xaml bindings to resolve binding errors
    public string CctvName => CctvId switch
    {
        "CCTV_01" => "CCTV_01 송도동",
        "CCTV_02" => "CCTV_02 구월동",
        "CCTV_03" => "CCTV_03 부평동",
        "CCTV_04" => "CCTV_04 주안동",
        _ when CctvId.StartsWith("VLM_") => $"VLM {SourceImage}",
        _ => $"{CctvId} 채널"
    };

    public string DetectedAtText => Timestamp.ToString("HH:mm:ss");

    public string ObjectIdText
    {
        get
        {
            if (string.IsNullOrEmpty(Id)) return "B_UNKNOWN";
            if (Id.Length < 5) return $"B_{Id.ToUpper()}";
            return $"B_{Id.Substring(0, 5).ToUpper()}";
        }
    }

    private string _statusCode = "PENDING_VLM";
    public string StatusCode 
    { 
        get => _statusCode; 
        set 
        { 
            if (_statusCode != value) 
            { 
                _statusCode = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(VerdictColor)); 
                OnPropertyChanged(nameof(VerdictIcon));
                OnPropertyChanged(nameof(IsEmergency));
                OnPropertyChanged(nameof(PriorityLabel));
            } 
        } 
    }

    public string YoloCoordsText => $"[{BoundingBox.X}, {BoundingBox.Y}, {BoundingBox.Width}, {BoundingBox.Height}]";

    /// <summary>YOLO 박스 전용 라벨 (Tier 1만, VLM 무관)</summary>
    public string YoloDetectionLabelText =>
        $"현수막 (Banner) - {YoloConfidenceText} | Tier 1: 탐지 (Detected)";

    /// <summary>VLM 검증 화면 뱃지용 — CCTV 박스에는 미사용</summary>
    public System.Windows.Media.SolidColorBrush VerdictColor =>
        StatusCode switch
        {
            "LEGAL_PUBLIC" or "LEGAL_COMMERCIAL" or "EXEMPT_ELECTION"
                => new(System.Windows.Media.Color.FromRgb(0x2E, 0xD5, 0x73)),
            "LEGAL_POLITICS"
                => new(System.Windows.Media.Color.FromRgb(0x2E, 0x86, 0xDE)),
            "PENDING_VLM"
                => new(System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA)),
            "MANUAL_REVIEW"
                => new(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
            "ILLEGAL_CONTENT"
                => new(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26)),
            "ILLEGAL_COMMERCIAL"
                => new(System.Windows.Media.Color.FromRgb(0xFF, 0x47, 0x57)),
            _ => new(System.Windows.Media.Color.FromRgb(0x94, 0xA3, 0xB8))
        };

    public string VerdictColorLegend => StatusCode switch
    {
        "LEGAL_PUBLIC" => "공익성",
        "LEGAL_POLITICS" => "정당",
        "LEGAL_COMMERCIAL" => "상업",
        "EXEMPT_ELECTION" => "정당 (선거)",
        "PENDING_VLM" => "VLM 분석 대기",
        "MANUAL_REVIEW" => "수동 검토",
        "ILLEGAL_CONTENT" => "불법",
        "ILLEGAL_COMMERCIAL" => "불법",
        _ => "판정 미확정"
    };

    public string VerdictIcon =>
        StatusCode switch
        {
            "LEGAL_PUBLIC" => "🟢 공익성",
            "LEGAL_POLITICS" => "🔵 정당",
            "LEGAL_COMMERCIAL" => "🟡 상업",
            "EXEMPT_ELECTION" => "🔵 정당",
            "PENDING_VLM" => "⏳ 분석 중",
            "MANUAL_REVIEW" => "⚠️ 수동 검토",
            "ILLEGAL_CONTENT" => "⛔ 불법",
            "ILLEGAL_COMMERCIAL" => "⛔ 불법",
            _ => "❓ 미확정"
        };

    public string AiReason
    {
        get
        {
            if (!string.IsNullOrEmpty(VlmAnalysis) && VlmAnalysis != "AI 분석 중...")
                return VlmAnalysis;
            if (!IsVlmTriggered)
                return "YOLO 탐지 완료 — VLM 문자·맥락 분석 대기 중";
            return "AI 분석 결과 대기 중";
        }
    }

    // Dynamic stage computation based on actual model run (OcrConfidence > 0 check)
    public int Stage => IsVlmTriggered ? 3 : (OcrConfidence > 0 ? 2 : 1);

    public string BoundingBoxLabelText => Stage switch
    {
        2 => $"현수막 (Banner) - {YoloConfidenceText} | Tier 2: 판독 (Read)",
        3 => $"현수막 (Banner) - {YoloConfidenceText} | Tier 3: 분석 (Analyzed)",
        _ => $"현수막 (Banner) - {YoloConfidenceText} | Tier 1: 탐지 (Detected)"
    };

    public string YoloConfidenceText => $"{(Confidence > 0 ? Confidence : 0.952) * 100:F1}%";

    public string StageText => Stage switch
    {
        2 => "[Tier 2] 문자 판독 (Text Read)",
        3 => "[Tier 3] 맥락 분석 (Context Analyze)",
        _ => "[Tier 1] 탐지 (Detected)"
    };

    public string ConfidenceText => YoloConfidenceText;

    public string OcrPreviewText => !string.IsNullOrEmpty(OcrText)
        ? $"추출: {OcrText.Replace("\r", "").Replace("\n", " ")}"
        : string.Empty;

    public bool HasOcrPreview => !string.IsNullOrEmpty(OcrText);

    public string OcrConfidenceText
    {
        get
        {
            if (OcrConfidence > 0)
                return $"{NormalizeConfidencePercent(OcrConfidence):F1}%";

            if (!IsVlmTriggered)
                return "분석 중";

            return "—";
        }
    }

    private static double NormalizeConfidencePercent(double value) =>
        value > 1.0 ? value : value * 100.0;

    public string StayDurationText => TimestampOffset.TotalSeconds > 0
        ? $"{(int)TimestampOffset.TotalSeconds}s"
        : "0s";

    public string Tier2StatusText => (OcrConfidence > 0 || IsVlmTriggered) ? "수행 완료" : "생략됨";
    public string Tier3StatusText => Stage >= 3 ? "수행 완료" : "수행 전";
}
