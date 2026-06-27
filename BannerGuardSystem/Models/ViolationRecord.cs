using System;
using System.Windows.Media;

namespace BannerGuardSystem.Models;

public class ViolationRecord
{
    public int Id { get; set; }
    public string AlertId { get; set; } = string.Empty;
    public string CctvId { get; set; } = string.Empty;
    public string CctvName { get; set; } = string.Empty;
    public string Gu { get; set; } = string.Empty;
    public string Dong { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public DateTime ViolationDate { get; set; }
    public string OcrText { get; set; } = string.Empty;
    public string AiVerdict { get; set; } = "ILLEGAL";
    public string AiReason { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string CroppedImagePath { get; set; } = string.Empty;
    public string ConfirmedByUser { get; set; } = string.Empty;
    public string OfficerNote { get; set; } = string.Empty;
    public string Status { get; set; } = "단속완료"; // 단속완료 | 행정처분 | 철거완료
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public double Similarity { get; set; }
    public string SimilarityText => $"{Similarity * 100:F0}%";
    public bool IsWarning => Similarity >= 0.8;

    // UI Helper Bindings
    public string ViolationDateText => ViolationDate.ToString("yyyy-MM-dd HH:mm");
    public string ConfidenceText => $"{Confidence * 100:F1}%";
    public string FullAddress => string.IsNullOrEmpty(LocationAddress) ? $"{Gu} {Dong}" : LocationAddress;
    
    public SolidColorBrush StatusColor => Status switch
    {
        "철거완료"   => new SolidColorBrush(Color.FromRgb(0x2E, 0xD5, 0x73)), // Green
        "행정처분" => new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x02)), // Orange
        _            => new SolidColorBrush(Color.FromRgb(0xFF, 0x47, 0x57))  // Red
    };
}
