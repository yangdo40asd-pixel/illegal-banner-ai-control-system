using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace BannerGuardSystem.Helpers;

public static class ChartFontHelper
{
    private static SKTypeface? _koreanTypeface;

    public static SKTypeface KoreanTypeface => _koreanTypeface ??= ResolveKoreanTypeface();

    public static void ConfigureLiveCharts()
    {
        LiveCharts.Configure(config => config.HasGlobalSKTypeface(KoreanTypeface));
    }

    public static SolidColorPaint LabelPaint(float size = 11, string colorHex = "#374151") =>
        new(SKColor.Parse(colorHex))
        {
            SKTypeface = KoreanTypeface,
            SKFontStyle = SKFontStyle.Normal
        };

    public static Axis CreateCategoryAxis(string[] labels, int rotation = 0) =>
        new()
        {
            Labels = labels,
            LabelsRotation = rotation,
            LabelsPaint = LabelPaint(),
            SeparatorsPaint = new SolidColorPaint(SKColors.LightGray.WithAlpha(80))
        };

    public static Axis CreateValueAxis(string? name = null, double minLimit = 0, AxisPosition position = AxisPosition.Start) =>
        new()
        {
            MinLimit = minLimit,
            Name = name,
            NamePaint = LabelPaint(),
            LabelsPaint = LabelPaint(10, "#6B7280"),
            Position = position,
            SeparatorsPaint = new SolidColorPaint(SKColors.LightGray.WithAlpha(60))
        };

    private static SKTypeface ResolveKoreanTypeface()
    {
        string[] candidates =
        [
            "Malgun Gothic",
            "맑은 고딕",
            "NanumGothic",
            "Noto Sans CJK KR",
            "Segoe UI"
        ];

        foreach (var name in candidates)
        {
            var face = SKTypeface.FromFamilyName(name);
            if (face != null && face.FamilyName != "sans-serif")
                return face;
        }

        return SKTypeface.Default;
    }
}