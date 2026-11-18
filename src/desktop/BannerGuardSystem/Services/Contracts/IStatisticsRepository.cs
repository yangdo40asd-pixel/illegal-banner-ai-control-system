using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public interface IStatisticsRepository
{
    StatisticsReportData GetReport(ReportPeriodType periodType, string periodLabel);
    IReadOnlyList<DrillDownItem> GetDrillDown(ContentCategory category);
    IReadOnlyList<string> GetPeriodOptions(ReportPeriodType periodType);
}