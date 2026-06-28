using System;
using System.Windows.Media;

namespace BannerGuardSystem.Models;

public enum AlertStatus
{
    Pending,
    Confirmed,
    Rejected
}

public class BannerAlert
{
    public string AlertId { get; set; } = Guid.NewGuid().ToString();
    public string CctvId { get; set; } = string.Empty;
    public string CctvName { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public string Gu { get; set; } = string.Empty;
    public string Dong { get; set; } = string.Empty;

    public DateTime DetectedAt { get; set; } = DateTime.Now;
    public double Confidence { get; set; }
    public string OcrText { get; set; } = string.Empty;
    public string AiVerdict { get; set; } = string.Empty; // "ILLEGAL", "LEGAL", "UNCERTAIN"
    public string AiReason { get; set; } = string.Empty;
    public int Stage { get; set; } = 1; // 1=YOLO, 2=OCR, 3=VLM

    public string CroppedImagePath { get; set; } = string.Empty;
    public string CroppedImageBase64 { get; set; } = string.Empty;

    public bool IsRecurring { get; set; }
    public int RecurrenceCount { get; set; }
    public string RecurrenceDetail { get; set; } = string.Empty;

    public AlertStatus Status { get; set; } = AlertStatus.Pending;
    public string? ConfirmedByUser { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // UI Helper Bindings
    public string ConfidenceText => $"{Confidence * 100:F1}%";
    public string DetectedAtText => DetectedAt.ToString("HH:mm:ss");
    public string FullAddress => string.IsNullOrEmpty(LocationAddress) ? $"{Gu} {Dong}" : LocationAddress;
    public string StageBadge => Stage switch { 1 => "YOLO", 2 => "OCR", 3 => "VLM", _ => "AI" };
    
    public bool IsIllegal => AiVerdict == "ILLEGAL";
    
    public string VerdictColorKey => AiVerdict switch
    {
        "ILLEGAL" => "#FF4757", // Red
        "LEGAL"   => "#2ED573", // Green
        _         => "#FFA502"  // Orange (Warning)
    };

    public string VerdictIcon => AiVerdict switch
    {
        "ILLEGAL" => "⛔ 불법 의심",
        "LEGAL"   => "✅ 합법",
        _         => "⚠️ 검토 필요"
    };
}
