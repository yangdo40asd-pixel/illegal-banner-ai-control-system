using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public interface IStatisticsExcelExportService
{
    void Export(StatisticsReportData data, string periodLabel, string filePath);
}
