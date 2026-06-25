using System;

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

public class BannerDetectionEventDto
{
    // Basic Info
    public string Id { get; set; } = string.Empty;
    public string CctvId { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public TimeSpan TimestampOffset { get; set; } = TimeSpan.Zero;

    // Tier 1 (YOLO)
    public bool HasBanner { get; set; }
    public BBox BoundingBox { get; set; }
    public double Confidence { get; set; }

    // Tier 2 (OCR)
    public string OcrText { get; set; } = string.Empty;
    public double OcrConfidence { get; set; }

    // Tier 3 (VLM)
    public bool IsVlmTriggered { get; set; }
    public string VlmAnalysis { get; set; } = string.Empty;

    // Repeat Offender Flags
    public bool IsRepeatOffender { get; set; }
    public int RepeatCount { get; set; }

    // Image Path
    public string CropImageUri { get; set; } = string.Empty;
    public string CroppedImageBase64 { get; set; } = string.Empty;

    // UI Helpers matching MainWindow.xaml bindings to resolve binding errors
    public string CctvName => CctvId switch
    {
        "CCTV_01" => "CCTV_01 송도동",
        "CCTV_02" => "CCTV_02 구월동",
        "CCTV_03" => "CCTV_03 부평동",
        "CCTV_04" => "CCTV_04 주안동",
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

    public string StatusCode { get; set; } = "ILLEGAL_COMMERCIAL";

    public string YoloCoordsText => $"[{BoundingBox.X}, {BoundingBox.Y}, {BoundingBox.Width}, {BoundingBox.Height}]";

    public System.Windows.Media.SolidColorBrush VerdictColor
    {
        get
        {
            return StatusCode switch
            {
                "LEGAL_PUBLIC" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xD5, 0x73)), // Green
                "LEGAL_POLITICS" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x86, 0xDE)), // Blue
                "EXEMPT_ELECTION" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9B, 0x59, 0xB6)), // Purple
                _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x47, 0x57)) // Red
            };
        }
    }

    public string VerdictIcon
    {
        get
        {
            return StatusCode switch
            {
                "LEGAL_PUBLIC" => "🟢 공익 안내",
                "LEGAL_POLITICS" => "🔵 정당 정책",
                "EXEMPT_ELECTION" => "🟣 선거 운동",
                _ => "⛔ 불법 의심"
            };
        }
    }

    public string AiReason
    {
        get
        {
            if (IsVlmTriggered && !string.IsNullOrEmpty(VlmAnalysis))
                return VlmAnalysis;
            return "AI 분석에 의한 불법 의심 광고물 탐지";
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

    private string YoloConfidenceText => $"{(Confidence > 0 ? Confidence : 0.952) * 100:F1}%";

    public string StageText => Stage switch
    {
        2 => "[Tier 2] 문자 판독 (Text Read)",
        3 => "[Tier 3] 맥락 분석 (Context Analyze)",
        _ => "[Tier 1] 탐지 (Detected)"
    };

    public string ConfidenceText => Stage switch
    {
        2 => "판독 완료 / 확인 대기",
        3 => "검증 완료 / 확인 대기",
        _ => YoloConfidenceText
    };

    public string OcrPreviewText => !string.IsNullOrEmpty(OcrText)
        ? $"추출: {OcrText.Replace("\r", "").Replace("\n", " ")}"
        : string.Empty;

    public bool HasOcrPreview => !string.IsNullOrEmpty(OcrText);

    public string OcrConfidenceText => OcrConfidence > 0
        ? $"{OcrConfidence * 100:F1}%"
        : "생략됨 (N/A)";

    public string StayDurationText => TimestampOffset.TotalSeconds > 0
        ? $"{(int)TimestampOffset.TotalSeconds}s"
        : "0s";

    public string Tier2StatusText => OcrConfidence > 0 ? "수행 완료" : "생략됨";
    public string Tier3StatusText => Stage >= 3 ? "수행 완료" : "수행 전";
}
