namespace BannerGuardSystem.Models;

public class StatisticsKpiSummary
{
    public int TotalDetections { get; set; }
    public double TotalDetectionsDeltaPct { get; set; }
    public int TotalFineManwon { get; set; }
    public double AvgFinePerCaseManwon { get; set; }
    public int RepeatViolationZoneCount { get; set; }
    public int RepeatViolationZoneDelta { get; set; }
    public int RepeatViolationThreshold { get; set; } = 3;
    public double ProcessingRatePct { get; set; }
    public double AvgProcessingDays { get; set; }
    public double FalsePositiveRatePct { get; set; }
    public double AiConfidencePct { get; set; }
}

public class DistrictViolationItem
{
    public string DistrictName { get; set; } = string.Empty;
    public int CurrentCount { get; set; }
    public int PreviousCount { get; set; }
    public string BarColorHex { get; set; } = "#DC2626";
}

public class CategoryRatioItem
{
    public ContentCategory Category { get; set; }
    public int Count { get; set; }
    public double RatioPct { get; set; }
    public string DisplayName => Category.ToDisplayName();
    public string ColorHex => Category.ToColorHex();
    public string LegendText => Count > 0 ? $"{DisplayName}  {Count}건 ({RatioPct:F0}%)" : $"{DisplayName}  —";
}

public class HourlyPatternPoint
{
    public int Hour { get; set; }
    public int WeekdayCount { get; set; }
    public int WeekendCount { get; set; }
    public string HourLabel => $"{Hour}시";
}

public class RepeatViolationZoneItem
{
    public int Rank { get; set; }
    public string District { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public int DetectionCount { get; set; }
    public int TotalFineManwon { get; set; }
    public int ConsecutiveMonths { get; set; }
    public double BarRatio { get; set; }
    public string DisplayName => $"{District} · {ZoneName}";
    public string StatsText => $"{DetectionCount}건 · {ConsecutiveMonths}개월 연속";
    public string FineText => $"{TotalFineManwon:N0}만원";
    public string BarColorHex => Rank switch
    {
        1 => "#DC2626",
        2 => "#EA580C",
        3 => "#EAB308",
        4 => "#FDE047",
        _ => "#D1D5DB"
    };
}

public class MonthlyTrendItem
{
    public string MonthLabel { get; set; } = string.Empty;
    public int DetectionCount { get; set; }
    public int CumulativeFineManwon { get; set; }
}

public enum RepeatViolationRiskLevel
{
    Critical,
    Warning,
    Watch
}

public class RepeatViolationBannerItem
{
    public int Rank { get; set; }
    public string District { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string BannerText { get; set; } = string.Empty;
    public int DetectionCount { get; set; }
    public int TotalFineManwon { get; set; }
    public int ConsecutiveMonths { get; set; }
    public RepeatViolationRiskLevel RiskLevel { get; set; }
    public string LastDetectionDate { get; set; } = string.Empty;
    public string PrimaryCategory { get; set; } = string.Empty;
    public bool IsNewThisMonth { get; set; }
    public string BannerBgHex { get; set; } = "#DC2626";
    public string BannerTextColorHex { get; set; } = "#FFFFFF";

    public string LocationText => $"{District} · {ZoneName}";
    public string RiskLabel => RiskLevel switch
    {
        RepeatViolationRiskLevel.Critical => "긴급",
        RepeatViolationRiskLevel.Warning => "주의",
        _ => "관찰"
    };
    public string FineText => $"{TotalFineManwon:N0}만원";
    public string StatsText => $"상습 {DetectionCount}회 · {ConsecutiveMonths}개월 연속";
    public string MetaText => $"{LocationText} · {LastDetectionDate} 적발";
}

public class DrillDownItem
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public double RatioPct { get; set; }
    public string DisplayText => $"{Label}  {Count}건 ({RatioPct:F0}%)";
}