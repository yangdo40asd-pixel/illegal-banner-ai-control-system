using BannerGuardSystem.Helpers;
using System.IO;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace BannerGuardSystem.Tests;

[TestClass]
public class RegressionTests
{
    [TestMethod]
    public void StatisticsWorkbookContainsPersistedNumbers()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bannerguard-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            var report = new StatisticsReportData
            {
                Kpi = new StatisticsKpiSummary { TotalDetections = 17, TotalFineManwon = 120 },
                Districts = [new DistrictViolationItem { DistrictName = "시험구", CurrentCount = 9, PreviousCount = 4 }]
            };
            new StatisticsExcelExportService().Export(report, "검증 기간", file);
            using var workbook = new ClosedXML.Excel.XLWorkbook(file);
            Assert.AreEqual(17, workbook.Worksheet("KPI").Cell(2, 2).GetValue<int>());
            Assert.AreEqual(120, workbook.Worksheet("KPI").Cell(3, 2).GetValue<int>());
            Assert.AreEqual("시험구", workbook.Worksheet("지역별").Cell(2, 1).GetString());
            Assert.AreEqual(9, workbook.Worksheet("지역별").Cell(2, 2).GetValue<int>());
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [TestMethod]
    public void MissingDetectionDoesNotFabricateConfidence()
    {
        Assert.AreEqual("미탐지", new BannerDetectionEventDto { HasBanner = false, Confidence = 0 }.YoloConfidenceText);
        Assert.AreEqual("0.0%", new BannerDetectionEventDto { HasBanner = true, Confidence = 0 }.YoloConfidenceText);
    }

    [TestMethod]
    [TestCategory("LegacyRegression")]
    public async Task ExistingSixCategoryRegressionSuite()
    {
        Assert.AreEqual(0, await Program.Main(Array.Empty<string>()));
    }

    [TestMethod]
    public void EmptyBatchRemovesPreviousDetection()
    {
        var channel = new CctvChannel();
        channel.ApplyDetectionBatch(new ChannelDetectionBatchDto
        {
            Detections = [new BannerDetectionEventDto { HasBanner = true, BoundingBox = new BBox(5, 5, 100, 30) }]
        });
        Assert.IsTrue(channel.HasDetections);
        channel.ApplyDetectionBatch(new ChannelDetectionBatchDto());
        Assert.IsFalse(channel.HasDetections);
    }

    [TestMethod]
    public void PathResolutionDoesNotDependOnCurrentDirectory()
    {
        var previous = Environment.CurrentDirectory;
        var expected = RuntimePaths.Resolve("DatabasePath");
        try
        {
            Environment.CurrentDirectory = Path.GetTempPath();
            Assert.AreEqual(expected, RuntimePaths.Resolve("DatabasePath"));
        }
        finally { Environment.CurrentDirectory = previous; }
    }

    [TestMethod]
    public void SharedSchemaWorksWithIsolatedDatabase()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bannerguard-unit-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(file);
            db.Initialize();
            var records = db.GetEnforcementRecords(out var error);
            Assert.IsNull(error);
            Assert.AreEqual(0, records.Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(file); }
    }
}
