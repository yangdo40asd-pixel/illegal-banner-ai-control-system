using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

/// <summary>관제관 처리 완료(human_reviews) + 단속(enforcement_records) 기준 실데이터 통계.</summary>
public class SqliteStatisticsRepository : IStatisticsRepository
{
    private readonly DatabaseService _db;
    private static readonly string[] DistrictPalette = ["#DC2626", "#EA580C", "#EAB308", "#16A34A", "#2563EB", "#7C3AED"];

    public SqliteStatisticsRepository(DatabaseService db)
    {
        _db = db;
    }

    public StatisticsReportData GetReport(ReportPeriodType periodType, string periodLabel)
    {
        var (from, to, prevFrom, prevTo) = ResolvePeriodRange(periodType, periodLabel);
        var reviews = _db.GetProcessedReviews(from, to);
        var prevReviews = _db.GetProcessedReviews(prevFrom, prevTo);
        var completedEnforcement = _db.GetEnforcementRecordsInRange(from, to);

        var illegalReviews = reviews.Where(r => r.HumanVerdict == "ILLEGAL").ToList();
        var totalFine = completedEnforcement.Count(e => e.Status != EnforcementProcessingStatus.Urgent) * 300;
        if (totalFine == 0)
            totalFine = illegalReviews.Count * 300;

        var falsePositive = reviews.Count(r =>
            r.HumanVerdict == "LEGAL"
            && (r.AiStatusCode.StartsWith("ILLEGAL") || r.AiStatusCode == "MANUAL_REVIEW"));

        var report = new StatisticsReportData
        {
            Kpi = new StatisticsKpiSummary
            {
                TotalDetections = reviews.Count,
                TotalDetectionsDeltaPct = CalcDeltaPct(reviews.Count, prevReviews.Count),
                TotalFineManwon = totalFine,
                AvgFinePerCaseManwon = illegalReviews.Count > 0
                    ? Math.Round(totalFine / (double)illegalReviews.Count, 1)
                    : 0,
                RepeatViolationZoneCount = CountRepeatZones(reviews),
                RepeatViolationZoneDelta = 0,
                RepeatViolationThreshold = 3,
                ProcessingRatePct = reviews.Count > 0
                    ? Math.Round(illegalReviews.Count * 100.0 / reviews.Count, 1)
                    : 0,
                AvgProcessingDays = 1.0,
                FalsePositiveRatePct = reviews.Count > 0
                    ? Math.Round(falsePositive * 100.0 / reviews.Count, 1)
                    : 0,
                AiConfidencePct = reviews.Count > 0 ? 92.0 : 0
            },
            Districts = BuildDistricts(reviews, prevReviews),
            Categories = BuildCategories(reviews),
            HourlyPattern = BuildHourly(reviews),
            TopViolationZones = BuildTopZones(reviews),
            MonthlyTrend = BuildMonthlyTrend(),
            RepeatViolationBanners = BuildRepeatBanners(reviews)
        };
        return report;
    }

    public IReadOnlyList<DrillDownItem> GetDrillDown(ContentCategory category)
    {
        var reviews = _db.GetProcessedReviews();
        var matched = reviews
            .Where(r => EnforcementDataMapper.MapStatusToCategory(r.AiStatusCode, r.OcrText) == category)
            .ToList();
        if (matched.Count == 0)
            return [new DrillDownItem { Label = "처리 완료 데이터 없음", Count = 0, RatioPct = 0 }];

        var groups = matched
            .Select(r => InferDrillLabel(category, r.OcrText))
            .GroupBy(x => x)
            .Select(g => new DrillDownItem { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(6)
            .ToList();
        var total = groups.Sum(g => g.Count);
        foreach (var g in groups)
            g.RatioPct = total > 0 ? Math.Round(g.Count * 100.0 / total, 0) : 0;
        return groups;
    }

    public IReadOnlyList<string> GetPeriodOptions(ReportPeriodType periodType)
    {
        var reviews = _db.GetProcessedReviews();
        if (reviews.Count == 0)
        {
            var now = DateTime.Now;
            return periodType switch
            {
                ReportPeriodType.Weekly => [$"{now:yyyy년 M월} 1주차"],
                ReportPeriodType.Quarterly => [$"{now:yyyy년} {(now.Month - 1) / 3 + 1}분기"],
                ReportPeriodType.Yearly => [$"{now:yyyy년}"],
                _ => [$"{now:yyyy년 M월}"]
            };
        }

        return periodType switch
        {
            ReportPeriodType.Weekly => reviews
                .Select(r => $"{r.ReviewedAt:yyyy년 M월} {WeekOfMonth(r.ReviewedAt)}주차")
                .Distinct()
                .Take(8)
                .ToList(),
            ReportPeriodType.Quarterly => reviews
                .Select(r => $"{r.ReviewedAt:yyyy년} {(r.ReviewedAt.Month - 1) / 3 + 1}분기")
                .Distinct()
                .Take(6)
                .ToList(),
            ReportPeriodType.Yearly => reviews
                .Select(r => $"{r.ReviewedAt:yyyy년}")
                .Distinct()
                .Take(5)
                .ToList(),
            _ => reviews
                .Select(r => $"{r.ReviewedAt:yyyy년 M월}")
                .Distinct()
                .Take(12)
                .ToList()
        };
    }

    private static int WeekOfMonth(DateTime dt) => (dt.Day - 1) / 7 + 1;

    private static double CalcDeltaPct(int current, int previous) =>
        previous <= 0 ? (current > 0 ? 100 : 0) : Math.Round((current - previous) * 100.0 / previous, 0);

    private static int CountRepeatZones(List<ProcessedReviewRow> reviews) =>
        reviews.GroupBy(r => EnforcementDataMapper.ResolveGu(r.CctvId))
            .Count(g => g.Count() >= 3);

    private List<DistrictViolationItem> BuildDistricts(List<ProcessedReviewRow> current, List<ProcessedReviewRow> previous)
    {
        var guList = current.Select(r => EnforcementDataMapper.ResolveGu(r.CctvId))
            .Concat(previous.Select(r => EnforcementDataMapper.ResolveGu(r.CctvId)))
            .Distinct()
            .OrderByDescending(g => current.Count(r => EnforcementDataMapper.ResolveGu(r.CctvId) == g))
            .Take(6)
            .ToList();

        var items = new List<DistrictViolationItem>();
        for (var i = 0; i < guList.Count; i++)
        {
            var gu = guList[i];
            items.Add(new DistrictViolationItem
            {
                DistrictName = gu,
                CurrentCount = current.Count(r => EnforcementDataMapper.ResolveGu(r.CctvId) == gu),
                PreviousCount = previous.Count(r => EnforcementDataMapper.ResolveGu(r.CctvId) == gu),
                BarColorHex = DistrictPalette[i % DistrictPalette.Length]
            });
        }
        if (items.Count == 0)
            items.Add(new DistrictViolationItem { DistrictName = "데이터 없음", BarColorHex = "#9CA3AF" });
        return items;
    }

    private static List<CategoryRatioItem> BuildCategories(List<ProcessedReviewRow> reviews)
    {
        if (reviews.Count == 0)
            return Enum.GetValues<ContentCategory>()
                .Select(c => new CategoryRatioItem { Category = c, Count = 0, RatioPct = 0 })
                .ToList();

        var groups = reviews
            .GroupBy(r => EnforcementDataMapper.MapStatusToCategory(r.AiStatusCode, r.OcrText))
            .ToDictionary(g => g.Key, g => g.Count());
        var total = reviews.Count;
        return Enum.GetValues<ContentCategory>()
            .Select(c =>
            {
                groups.TryGetValue(c, out var count);
                return new CategoryRatioItem
                {
                    Category = c,
                    Count = count,
                    RatioPct = total > 0 ? Math.Round(count * 100.0 / total, 0) : 0
                };
            })
            .ToList();
    }

    private static List<HourlyPatternPoint> BuildHourly(List<ProcessedReviewRow> reviews)
    {
        return Enumerable.Range(0, 24).Select(h => new HourlyPatternPoint
        {
            Hour = h,
            WeekdayCount = reviews.Count(r => r.ReviewedAt.Hour == h && r.ReviewedAt.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday),
            WeekendCount = reviews.Count(r => r.ReviewedAt.Hour == h && r.ReviewedAt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        }).ToList();
    }

    private static List<RepeatViolationZoneItem> BuildTopZones(List<ProcessedReviewRow> reviews)
    {
        var zones = reviews
            .GroupBy(r => (EnforcementDataMapper.ResolveGu(r.CctvId), EnforcementDataMapper.ResolveDong(r.CctvId)))
            .Select(g => new RepeatViolationZoneItem
            {
                District = g.Key.Item1,
                ZoneName = g.Key.Item2,
                DetectionCount = g.Count(),
                TotalFineManwon = g.Count(r => r.HumanVerdict == "ILLEGAL") * 300,
                ConsecutiveMonths = 1
            })
            .OrderByDescending(z => z.DetectionCount)
            .Take(5)
            .ToList();
        var max = zones.FirstOrDefault()?.DetectionCount ?? 1;
        for (var i = 0; i < zones.Count; i++)
        {
            zones[i].Rank = i + 1;
            zones[i].BarRatio = zones[i].DetectionCount / (double)max;
        }
        return zones;
    }

    private List<MonthlyTrendItem> BuildMonthlyTrend()
    {
        var reviews = _db.GetProcessedReviews();
        return reviews
            .GroupBy(r => new { r.ReviewedAt.Year, r.ReviewedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlyTrendItem
            {
                MonthLabel = $"{g.Key.Month}월",
                DetectionCount = g.Count(),
                CumulativeFineManwon = g.Count(r => r.HumanVerdict == "ILLEGAL") * 300
            })
            .TakeLast(6)
            .ToList();
    }

    private static List<RepeatViolationBannerItem> BuildRepeatBanners(List<ProcessedReviewRow> reviews)
    {
        return reviews
            .Where(r => !string.IsNullOrWhiteSpace(r.OcrText))
            .GroupBy(r => r.OcrText.Trim())
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select((g, i) =>
            {
                var first = g.First();
                var risk = g.Count() >= 5
                    ? RepeatViolationRiskLevel.Critical
                    : g.Count() >= 3
                        ? RepeatViolationRiskLevel.Warning
                        : RepeatViolationRiskLevel.Watch;
                return new RepeatViolationBannerItem
                {
                    Rank = i + 1,
                    District = EnforcementDataMapper.ResolveGu(first.CctvId),
                    ZoneName = EnforcementDataMapper.ResolveDong(first.CctvId),
                    BannerText = g.Key.Length > 60 ? g.Key[..60] + "…" : g.Key,
                    DetectionCount = g.Count(),
                    TotalFineManwon = g.Count(r => r.HumanVerdict == "ILLEGAL") * 300,
                    ConsecutiveMonths = 1,
                    RiskLevel = risk,
                    LastDetectionDate = g.Max(r => r.ReviewedAt).ToString("MM.dd"),
                    PrimaryCategory = EnforcementDataMapper.MapStatusToCategory(first.AiStatusCode, first.OcrText).ToDisplayName(),
                    IsNewThisMonth = g.Any(r => r.ReviewedAt.Month == DateTime.Now.Month),
                    BannerBgHex = "#DC2626",
                    BannerTextColorHex = "#FFFFFF"
                };
            })
            .ToList();
    }

    private static string InferDrillLabel(ContentCategory category, string ocrText)
    {
        var t = ocrText ?? "";
        return category switch
        {
            ContentCategory.Commercial when t.Contains("분양") || t.Contains("부동산") => "부동산",
            ContentCategory.Commercial when t.Contains("학원") => "학원",
            ContentCategory.Politics when t.Contains("교육감") => "교육감 선거",
            ContentCategory.Politics when t.Contains("후보") => "후보 캠페인",
            ContentCategory.Event => "행사/모임",
            ContentCategory.Civil => "공익/안내",
            _ => category.ToDisplayName()
        };
    }

    private static (DateTime? from, DateTime? to, DateTime? prevFrom, DateTime? prevTo) ResolvePeriodRange(
        ReportPeriodType periodType, string periodLabel)
    {
        var now = DateTime.Now;
        if (string.IsNullOrWhiteSpace(periodLabel))
            return (new DateTime(now.Year, now.Month, 1), now.AddDays(1), null, null);

        try
        {
            if (periodLabel.Contains("주차") && periodLabel.Contains("년"))
            {
                var parts = periodLabel.Replace("년", " ").Replace("월", " ").Replace("주차", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3
                    && int.TryParse(parts[0], out var y)
                    && int.TryParse(parts[1], out var m)
                    && int.TryParse(parts[2], out var w))
                {
                    var start = new DateTime(y, m, 1).AddDays((w - 1) * 7);
                    var end = start.AddDays(7);
                    return (start, end, start.AddDays(-7), start);
                }
            }
            if (periodLabel.Contains("분기"))
            {
                var y = int.Parse(periodLabel.Split('년')[0]);
                var q = int.Parse(periodLabel.Split(' ')[1].Replace("분기", ""));
                var startMonth = (q - 1) * 3 + 1;
                var start = new DateTime(y, startMonth, 1);
                var end = start.AddMonths(3);
                return (start, end, start.AddMonths(-3), start);
            }
            if (periodLabel.EndsWith("년") && !periodLabel.Contains("월"))
            {
                var y = int.Parse(periodLabel.Replace("년", ""));
                return (new DateTime(y, 1, 1), new DateTime(y + 1, 1, 1), new DateTime(y - 1, 1, 1), new DateTime(y, 1, 1));
            }
            if (periodLabel.Contains("년") && periodLabel.Contains("월"))
            {
                var y = int.Parse(periodLabel.Split('년')[0]);
                var m = int.Parse(periodLabel.Split('년')[1].Replace("월", "").Trim());
                var start = new DateTime(y, m, 1);
                var end = start.AddMonths(1);
                return (start, end, start.AddMonths(-1), start);
            }
        }
        catch
        {
            // fall through
        }

        return (new DateTime(now.Year, now.Month, 1), now.AddDays(1),
            new DateTime(now.Year, now.Month, 1).AddMonths(-1),
            new DateTime(now.Year, now.Month, 1));
    }
}