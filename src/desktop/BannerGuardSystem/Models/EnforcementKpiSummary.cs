namespace BannerGuardSystem.Models;

public class EnforcementKpiSummary
{
    public int TodayCount { get; set; }
    public int YesterdayCount { get; set; }
    public int WeekCount { get; set; }
    public int TotalFineManwon { get; set; }
    public int AppealCount { get; set; }
    public int UnpaidFineManwon { get; set; }
    public double CollectionRatePct { get; set; }

    public string TodayDeltaText
    {
        get
        {
            if (YesterdayCount <= 0)
                return TodayCount > 0 ? "전일 대비 신규" : "전일 대비 —";
            var pct = (TodayCount - YesterdayCount) * 100.0 / YesterdayCount;
            var sign = pct >= 0 ? "+" : "";
            return $"전일 대비 {sign}{pct:F0}%";
        }
    }
}