using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public class MockStatisticsRepository : IStatisticsRepository
{
    public StatisticsReportData GetReport(ReportPeriodType periodType, string periodLabel)
    {
        var scale = periodType switch
        {
            ReportPeriodType.Weekly => 0.25,
            ReportPeriodType.Quarterly => 3.0,
            ReportPeriodType.Yearly => 12.0,
            _ => 1.0
        };

        int S(int v) => Math.Max(1, (int)Math.Round(v * scale));

        return new StatisticsReportData
        {
            Kpi = new StatisticsKpiSummary
            {
                TotalDetections = S(147),
                TotalDetectionsDeltaPct = 23,
                TotalFineManwon = S(4820),
                AvgFinePerCaseManwon = 32.8,
                RepeatViolationZoneCount = S(12),
                RepeatViolationZoneDelta = 2,
                RepeatViolationThreshold = 3,
                ProcessingRatePct = 91.2,
                AvgProcessingDays = 3.8,
                FalsePositiveRatePct = 2.1,
                AiConfidencePct = 97.9
            },
            Districts =
            [
                new() { DistrictName = "연수구", CurrentCount = S(52), PreviousCount = S(41), BarColorHex = "#DC2626" },
                new() { DistrictName = "남동구", CurrentCount = S(38), PreviousCount = S(33), BarColorHex = "#EA580C" },
                new() { DistrictName = "부평구", CurrentCount = S(24), PreviousCount = S(21), BarColorHex = "#EAB308" },
                new() { DistrictName = "서구", CurrentCount = S(19), PreviousCount = S(17), BarColorHex = "#16A34A" },
                new() { DistrictName = "미추홀구", CurrentCount = S(14), PreviousCount = S(12), BarColorHex = "#2563EB" }
            ],
            Categories =
            [
                new() { Category = ContentCategory.Commercial, Count = S(62), RatioPct = 42 },
                new() { Category = ContentCategory.Politics, Count = S(41), RatioPct = 28 },
                new() { Category = ContentCategory.Event, Count = S(26), RatioPct = 18 },
                new() { Category = ContentCategory.Personal, Count = S(18), RatioPct = 12 },
                new() { Category = ContentCategory.Religion, Count = 0, RatioPct = 0 },
                new() { Category = ContentCategory.Civil, Count = 0, RatioPct = 0 }
            ],
            HourlyPattern = BuildHourlyPattern(scale),
            TopViolationZones = BuildTopViolationZones(scale),
            MonthlyTrend =
            [
                new() { MonthLabel = "1월", DetectionCount = S(98), CumulativeFineManwon = S(2940) },
                new() { MonthLabel = "2월", DetectionCount = S(112), CumulativeFineManwon = S(3360) },
                new() { MonthLabel = "3월", DetectionCount = S(125), CumulativeFineManwon = S(3750) },
                new() { MonthLabel = "4월", DetectionCount = S(119), CumulativeFineManwon = S(3570) },
                new() { MonthLabel = "5월", DetectionCount = S(147), CumulativeFineManwon = S(4820) },
                new() { MonthLabel = "6월", DetectionCount = S(132), CumulativeFineManwon = S(4356) }
            ],
            RepeatViolationBanners = BuildRepeatViolationBanners(scale)
        };
    }

    public IReadOnlyList<DrillDownItem> GetDrillDown(ContentCategory category) => category switch
    {
        ContentCategory.Politics =>
        [
            new() { Label = "더불어민주당", Count = 18, RatioPct = 44 },
            new() { Label = "국민의힘", Count = 14, RatioPct = 34 },
            new() { Label = "무소속 후보", Count = 6, RatioPct = 15 },
            new() { Label = "기타 정당", Count = 3, RatioPct = 7 }
        ],
        ContentCategory.Commercial =>
        [
            new() { Label = "부동산", Count = 28, RatioPct = 45 },
            new() { Label = "학원", Count = 15, RatioPct = 24 },
            new() { Label = "병원", Count = 8, RatioPct = 13 },
            new() { Label = "음식점", Count = 7, RatioPct = 11 },
            new() { Label = "기타 상업", Count = 4, RatioPct = 7 }
        ],
        ContentCategory.Event =>
        [
            new() { Label = "축제/행사", Count = 14, RatioPct = 54 },
            new() { Label = "동호회", Count = 8, RatioPct = 31 },
            new() { Label = "기타 모임", Count = 4, RatioPct = 15 }
        ],
        ContentCategory.Personal =>
        [
            new() { Label = "개인 홍보", Count = 10, RatioPct = 56 },
            new() { Label = "소상공인", Count = 8, RatioPct = 44 }
        ],
        _ =>
        [
            new() { Label = "해당 분류 세부 데이터 없음", Count = 0, RatioPct = 0 }
        ]
    };

    public IReadOnlyList<string> GetPeriodOptions(ReportPeriodType periodType) => periodType switch
    {
        ReportPeriodType.Weekly =>
        [
            "2026년 6월 4주차", "2026년 6월 3주차", "2026년 6월 2주차", "2026년 6월 1주차"
        ],
        ReportPeriodType.Quarterly =>
        [
            "2026년 2분기", "2026년 1분기", "2025년 4분기"
        ],
        ReportPeriodType.Yearly =>
        [
            "2026년", "2025년", "2024년"
        ],
        _ =>
        [
            "2026년 6월", "2026년 5월", "2026년 4월", "2026년 3월", "2026년 2월", "2026년 1월"
        ]
    };

    private static List<HourlyPatternPoint> BuildHourlyPattern(double scale)
    {
        int[] weekday = [2, 3, 4, 5, 8, 12, 18, 22, 28, 32, 35, 38, 36, 34, 38, 35, 28, 22, 18, 14, 10, 6, 4, 3];
        int[] weekend = [1, 2, 2, 3, 4, 6, 8, 10, 12, 14, 15, 16, 14, 13, 14, 16, 18, 20, 22, 18, 12, 8, 5, 3];
        return Enumerable.Range(0, 24)
            .Select(h => new HourlyPatternPoint
            {
                Hour = h,
                WeekdayCount = Math.Max(0, (int)Math.Round(weekday[h] * scale)),
                WeekendCount = Math.Max(0, (int)Math.Round(weekend[h] * scale))
            })
            .ToList();
    }

    private static List<RepeatViolationBannerItem> BuildRepeatViolationBanners(double scale)
    {
        var items = new[]
        {
            (1, "연수구", "송도동 센트럴로", "◆송도 최고의 부동산◇ 전세·매매 전문 032-7XX-XXXX", 14, 4200, 5, RepeatViolationRiskLevel.Critical, "06.24", "상업광고", false, "#DC2626", "#FFFFFF"),
            (2, "남동구", "구월동 로데오거리", "○○○ 후보 인천광역시장 출마 ○○○ 함께합니다", 11, 3300, 4, RepeatViolationRiskLevel.Critical, "06.22", "정치광고", false, "#1D4ED8", "#FEF08A"),
            (3, "부평구", "부평역 광장", "대입 수학전문 ○○학원 6월 개강 특별할인 모집중", 9, 2700, 4, RepeatViolationRiskLevel.Warning, "06.20", "상업광고", false, "#B91C1C", "#FFFFFF"),
            (4, "서구", "검단신도시 해맞이로", "검단 ○○축제 대박나세요! 6월 28일 해맞이광장", 7, 2100, 3, RepeatViolationRiskLevel.Warning, "06.18", "행사홍보", true, "#FACC15", "#1E293B"),
            (5, "미추홀구", "주안역 상권", "주안역 맛집 ○○식당 오픈기념 전 메뉴 20% 할인", 6, 1800, 3, RepeatViolationRiskLevel.Warning, "06.15", "상업광고", false, "#EA580C", "#FFFFFF"),
            (6, "계양구", "계양역 인근", "계양 ○○공인중개사 이전오픈 기념 전세·월세 전문", 5, 1500, 3, RepeatViolationRiskLevel.Watch, "06.12", "개인홍보", true, "#FFFFFF", "#DC2626")
        };
        return items.Select(t => new RepeatViolationBannerItem
        {
            Rank = t.Item1,
            District = t.Item2,
            ZoneName = t.Item3,
            BannerText = t.Item4,
            DetectionCount = Math.Max(1, (int)Math.Round(t.Item5 * scale)),
            TotalFineManwon = Math.Max(1, (int)Math.Round(t.Item6 * scale)),
            ConsecutiveMonths = t.Item7,
            RiskLevel = t.Item8,
            LastDetectionDate = t.Item9,
            PrimaryCategory = t.Item10,
            IsNewThisMonth = t.Item11,
            BannerBgHex = t.Item12,
            BannerTextColorHex = t.Item13
        }).ToList();
    }

    private static List<RepeatViolationZoneItem> BuildTopViolationZones(double scale)
    {
        var items = new[]
        {
            (1, "연수구", "송도동 센트럴로 일대", 14, 4200, 5),
            (2, "남동구", "구월동 로데오거리", 11, 3300, 4),
            (3, "부평구", "부평역 광장 인근", 9, 2700, 4),
            (4, "서구", "검단신도시 해맞이로", 7, 2100, 3),
            (5, "미추홀구", "주안역 상권", 6, 1800, 3)
        };
        int max = items[0].Item4;
        return items.Select(t => new RepeatViolationZoneItem
        {
            Rank = t.Item1,
            District = t.Item2,
            ZoneName = t.Item3,
            DetectionCount = Math.Max(1, (int)Math.Round(t.Item4 * scale)),
            TotalFineManwon = Math.Max(1, (int)Math.Round(t.Item5 * scale)),
            ConsecutiveMonths = t.Item6,
            BarRatio = t.Item4 / (double)max
        }).ToList();
    }
}