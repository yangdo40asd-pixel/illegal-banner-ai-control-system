namespace BannerGuardSystem.Models;

public class StatisticsReportData
{
    public StatisticsKpiSummary Kpi { get; set; } = new();
    public List<DistrictViolationItem> Districts { get; set; } = [];
    public List<CategoryRatioItem> Categories { get; set; } = [];
    public List<HourlyPatternPoint> HourlyPattern { get; set; } = [];
    public List<RepeatViolationZoneItem> TopViolationZones { get; set; } = [];
    public List<MonthlyTrendItem> MonthlyTrend { get; set; } = [];
    public List<RepeatViolationBannerItem> RepeatViolationBanners { get; set; } = [];
}