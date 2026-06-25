using System;

namespace BannerGuardSystem.Models;

public class EnforcementHistoryDto
{
    public int HistoryId { get; set; }
    public DateTime DetectedDate { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public string MatchedText { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public string Status { get; set; } = string.Empty;

    // UI Helper Bindings
    public bool IsWarning => SimilarityScore >= 0.8;
    public string DetectedDateText => DetectedDate.ToString("yyyy-MM-dd HH:mm");
    public string SimilarityText => $"{SimilarityScore * 100:F0}%";
}
