using BannerGuardSystem.Models;
using ClosedXML.Excel;

namespace BannerGuardSystem.Services;

public interface IStatisticsExcelExportService
{
    void Export(StatisticsReportData data, string periodLabel, string filePath);
}

public class StatisticsExcelExportService : IStatisticsExcelExportService
{
    public void Export(StatisticsReportData data, string periodLabel, string filePath)
    {
        using var workbook = new XLWorkbook();

        var kpi = workbook.Worksheets.Add("KPI");
        kpi.Cell(1, 1).Value = "통계 기간";
        kpi.Cell(1, 2).Value = periodLabel;
        kpi.Cell(2, 1).Value = "총 적발 건수";
        kpi.Cell(2, 2).Value = data.Kpi.TotalDetections;
        kpi.Cell(3, 1).Value = "과태료 총액(만원)";
        kpi.Cell(3, 2).Value = data.Kpi.TotalFineManwon;
        kpi.Cell(4, 1).Value = "상습 위반 구역(곳)";
        kpi.Cell(4, 2).Value = data.Kpi.RepeatViolationZoneCount;
        kpi.Cell(4, 3).Value = $"기준 {data.Kpi.RepeatViolationThreshold}회 이상";
        kpi.Cell(5, 1).Value = "처리율(%)";
        kpi.Cell(5, 2).Value = data.Kpi.ProcessingRatePct;
        kpi.Cell(6, 1).Value = "오탐지율(%)";
        kpi.Cell(6, 2).Value = data.Kpi.FalsePositiveRatePct;

        var district = workbook.Worksheets.Add("지역별");
        district.Cell(1, 1).Value = "구";
        district.Cell(1, 2).Value = "당월";
        district.Cell(1, 3).Value = "전월";
        int r = 2;
        foreach (var d in data.Districts)
        {
            district.Cell(r, 1).Value = d.DistrictName;
            district.Cell(r, 2).Value = d.CurrentCount;
            district.Cell(r, 3).Value = d.PreviousCount;
            r++;
        }

        var zones = workbook.Worksheets.Add("상습위반구역");
        zones.Cell(1, 1).Value = "순위";
        zones.Cell(1, 2).Value = "구/군";
        zones.Cell(1, 3).Value = "구역";
        zones.Cell(1, 4).Value = "적발건수";
        zones.Cell(1, 5).Value = "과태료(만원)";
        zones.Cell(1, 6).Value = "연속개월";
        r = 2;
        foreach (var z in data.TopViolationZones)
        {
            zones.Cell(r, 1).Value = z.Rank;
            zones.Cell(r, 2).Value = z.District;
            zones.Cell(r, 3).Value = z.ZoneName;
            zones.Cell(r, 4).Value = z.DetectionCount;
            zones.Cell(r, 5).Value = z.TotalFineManwon;
            zones.Cell(r, 6).Value = z.ConsecutiveMonths;
            r++;
        }

        var banners = workbook.Worksheets.Add("상습적발현수막");
        banners.Cell(1, 1).Value = "순위";
        banners.Cell(1, 2).Value = "현수막 문구";
        banners.Cell(1, 3).Value = "구/군";
        banners.Cell(1, 4).Value = "위치";
        banners.Cell(1, 5).Value = "적발횟수";
        banners.Cell(1, 6).Value = "과태료(만원)";
        banners.Cell(1, 7).Value = "연속개월";
        banners.Cell(1, 8).Value = "위험등급";
        banners.Cell(1, 9).Value = "최근적발";
        banners.Cell(1, 10).Value = "유형";
        r = 2;
        foreach (var b in data.RepeatViolationBanners)
        {
            banners.Cell(r, 1).Value = b.Rank;
            banners.Cell(r, 2).Value = b.BannerText;
            banners.Cell(r, 3).Value = b.District;
            banners.Cell(r, 4).Value = b.ZoneName;
            banners.Cell(r, 5).Value = b.DetectionCount;
            banners.Cell(r, 6).Value = b.TotalFineManwon;
            banners.Cell(r, 7).Value = b.ConsecutiveMonths;
            banners.Cell(r, 8).Value = b.RiskLabel;
            banners.Cell(r, 9).Value = b.LastDetectionDate;
            banners.Cell(r, 10).Value = b.PrimaryCategory;
            r++;
        }

        workbook.SaveAs(filePath);
    }
}