using System;

namespace BannerGuardSystem.Models;

public class SimilarEnforcementRecord
{
    public DateTime DetectedDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public string BannerSummary { get; set; } = string.Empty;
    public int ImageSimilarityPercent { get; set; }
    public int TextSimilarityPercent { get; set; }

    public string DetectedDateText => DetectedDate.ToString("yyyy-MM-dd");
    public string ImageSimilarityDisplay => $"{ImageSimilarityPercent}%";
    public string TextSimilarityDisplay => $"{TextSimilarityPercent}%";
    public string SimilarityDisplay => $"이미지 {ImageSimilarityPercent}% · 텍스트 {TextSimilarityPercent}%";
}