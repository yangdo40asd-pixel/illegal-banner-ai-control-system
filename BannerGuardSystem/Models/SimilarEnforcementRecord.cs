using System;

namespace BannerGuardSystem.Models;

public class SimilarEnforcementRecord
{
    public DateTime DetectedDate { get; set; }
    public string Location { get; set; } = string.Empty;
    public string BannerSummary { get; set; } = string.Empty;
    public int SimilarityPercent { get; set; }

    public string DetectedDateText => DetectedDate.ToString("yyyy-MM-dd");
    public string SimilarityDisplay => $"{SimilarityPercent}%";
}