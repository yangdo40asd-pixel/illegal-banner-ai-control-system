using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BannerGuardSystem.Helpers;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;

namespace BannerGuardSystem.ViewModels;

public class StatisticsReportViewModel : BaseViewModel
{
    private readonly IStatisticsRepository _repository;
    private readonly IStatisticsExcelExportService _excelExport;
    private readonly IDialogService _dialog;
    private StatisticsReportData _report = new();

    private ReportPeriodType _selectedPeriod = ReportPeriodType.Monthly;
    private string _selectedPeriodLabel = "2026년 5월";
    private bool _compareWithPrevious = true;
    private ContentCategory? _selectedCategory;
    private string _drillDownTitle = string.Empty;



    public StatisticsReportViewModel(IStatisticsRepository repository, IStatisticsExcelExportService excelExport, IDialogService dialog)
    {
        ChartFontHelper.ConfigureLiveCharts();
        _repository = repository;
        _excelExport = excelExport;
        _dialog = dialog;

        RefreshCommand = new SimpleCommand(Refresh);

        PeriodOptions = new ObservableCollection<string>();
        Categories = new ObservableCollection<CategoryRatioItem>();
        Districts = new ObservableCollection<DistrictViolationItem>();
        TopViolationZones = new ObservableCollection<RepeatViolationZoneItem>();
        MonthlyTrend = new ObservableCollection<MonthlyTrendItem>();
        RepeatViolationBanners = new ObservableCollection<RepeatViolationBannerItem>();
        DrillDownItems = new ObservableCollection<DrillDownItem>();

        SelectPeriodCommand = new RelayCommand(OnSelectPeriod);
        SelectCategoryCommand = new RelayCommand(OnSelectCategory);
        ClearDrillDownCommand = new SimpleCommand(ClearDrillDown);
        DownloadReportCommand = new SimpleCommand(OnDownloadReport);
        PrintCommand = new SimpleCommand(OnPrint);

        ReloadPeriodOptions();
        LoadReport();
    }

    /// <summary>처리 완료 데이터 기준 리포트 갱신</summary>
    public void Refresh()
    {
        ReloadPeriodOptions();
        LoadReport();
    }

    public ObservableCollection<string> PeriodOptions { get; }
    public ObservableCollection<CategoryRatioItem> Categories { get; }
    public ObservableCollection<DistrictViolationItem> Districts { get; }
    public ObservableCollection<RepeatViolationZoneItem> TopViolationZones { get; }
    public ObservableCollection<MonthlyTrendItem> MonthlyTrend { get; }
    public ObservableCollection<RepeatViolationBannerItem> RepeatViolationBanners { get; }
    public ObservableCollection<DrillDownItem> DrillDownItems { get; }

    public ReportPeriodType SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (SetField(ref _selectedPeriod, value))
            {
                ReloadPeriodOptions();
                LoadReport();
                OnPropertyChanged(nameof(IsWeeklySelected));
                OnPropertyChanged(nameof(IsMonthlySelected));
                OnPropertyChanged(nameof(IsQuarterlySelected));
                OnPropertyChanged(nameof(IsYearlySelected));
            }
        }
    }

    public string SelectedPeriodLabel
    {
        get => _selectedPeriodLabel;
        set
        {
            if (SetField(ref _selectedPeriodLabel, value))
                LoadReport();
        }
    }

    public bool CompareWithPrevious
    {
        get => _compareWithPrevious;
        set
        {
            if (SetField(ref _compareWithPrevious, value))
            {
                RefreshCharts();
                OnPropertyChanged(nameof(ShowComparison));
            }
        }
    }

    public bool ShowComparison => CompareWithPrevious;

    public bool IsWeeklySelected => SelectedPeriod == ReportPeriodType.Weekly;
    public bool IsMonthlySelected => SelectedPeriod == ReportPeriodType.Monthly;
    public bool IsQuarterlySelected => SelectedPeriod == ReportPeriodType.Quarterly;
    public bool IsYearlySelected => SelectedPeriod == ReportPeriodType.Yearly;

    public string PeriodHeaderText => $"{SelectedPeriodLabel} 통계";

    public int TotalDetections => _report.Kpi.TotalDetections;
    public string TotalDetectionsDeltaText => $"전월 대비 +{_report.Kpi.TotalDetectionsDeltaPct:F0}%";
    public string TotalFineText => $"{_report.Kpi.TotalFineManwon:N0}만원";
    public string AvgFineText => $"평균 {_report.Kpi.AvgFinePerCaseManwon:F1}만/건";
    public int RepeatViolationZoneCount => _report.Kpi.RepeatViolationZoneCount;
    public string RepeatViolationZoneDeltaText => $"전월 대비 +{_report.Kpi.RepeatViolationZoneDelta}곳";
    public string RepeatViolationZoneSubText => $"{_report.Kpi.RepeatViolationThreshold}회 이상 반복 적발 구역";
    public string ProcessingRateText => $"{_report.Kpi.ProcessingRatePct:F1}%";
    public string AvgProcessingDaysText => $"평균 처리 {_report.Kpi.AvgProcessingDays:F1}일";
    public string FalsePositiveRateText => $"{_report.Kpi.FalsePositiveRatePct:F1}%";
    public string AiConfidenceText => $"AI 신뢰도 {_report.Kpi.AiConfidencePct:F1}%";
    public string DonutCenterText => $"총 {TotalDetections}건";
    public string PeakHourNote
    {
        get
        {
            var peak = _report.HourlyPattern
                .OrderByDescending(h => h.WeekdayCount + h.WeekendCount)
                .FirstOrDefault();
            if (peak == null) return "※ 시간대별 패턴 데이터 없음";
            return $"※ 피크 {peak.HourLabel} (평일 {peak.WeekdayCount}건 · 주말 {peak.WeekendCount}건)";
        }
    }

    public bool HasDrillDown => _selectedCategory.HasValue;
    public string DrillDownTitle => _drillDownTitle;

    public ISeries[] DistrictSeries { get; private set; } = [];
    public ISeries[] CategorySeries { get; private set; } = [];
    public ISeries[] HourlySeries { get; private set; } = [];
    public ISeries[] MonthlySeries { get; private set; } = [];
    public Axis[] DistrictXAxes { get; private set; } = [];
    public Axis[] DistrictYAxes { get; private set; } = [new Axis { MinLimit = 0 }];
    public Axis[] HourlyXAxes { get; private set; } = [];
    public Axis[] HourlyYAxes { get; private set; } = [new Axis { MinLimit = 0, Name = "건수" }];
    public Axis[] MonthlyXAxes { get; private set; } = [];
    public Axis[] MonthlyYAxes { get; private set; } = [new Axis { MinLimit = 0, Name = "건수" }];
    public Axis[] MonthlyYAxesRight { get; private set; } = [new Axis { MinLimit = 0, Name = "누적(만원)", Position = AxisPosition.End }];

    public ICommand SelectPeriodCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand ClearDrillDownCommand { get; }
    public ICommand DownloadReportCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand RefreshCommand { get; }

    private void ReloadPeriodOptions()
    {
        PeriodOptions.Clear();
        foreach (var opt in _repository.GetPeriodOptions(SelectedPeriod))
            PeriodOptions.Add(opt);
        if (!PeriodOptions.Contains(SelectedPeriodLabel))
            SelectedPeriodLabel = PeriodOptions.FirstOrDefault() ?? SelectedPeriodLabel;
    }

    private void OnSelectPeriod(object? parameter)
    {
        if (parameter is string s && Enum.TryParse<ReportPeriodType>(s, out var period))
            SelectedPeriod = period;
    }

    private void LoadReport()
    {
        _report = _repository.GetReport(SelectedPeriod, SelectedPeriodLabel);
        BindCollections();
        RefreshCharts();
        ClearDrillDown();
        OnPropertyChanged(nameof(PeriodHeaderText));
        OnPropertyChanged(nameof(TotalDetections));
        OnPropertyChanged(nameof(TotalDetectionsDeltaText));
        OnPropertyChanged(nameof(TotalFineText));
        OnPropertyChanged(nameof(AvgFineText));
        OnPropertyChanged(nameof(RepeatViolationZoneCount));
        OnPropertyChanged(nameof(RepeatViolationZoneDeltaText));
        OnPropertyChanged(nameof(RepeatViolationZoneSubText));
        OnPropertyChanged(nameof(PeakHourNote));
        OnPropertyChanged(nameof(ProcessingRateText));
        OnPropertyChanged(nameof(AvgProcessingDaysText));
        OnPropertyChanged(nameof(FalsePositiveRateText));
        OnPropertyChanged(nameof(AiConfidenceText));
        OnPropertyChanged(nameof(DonutCenterText));
    }

    private void BindCollections()
    {
        Categories.Clear();
        foreach (var c in _report.Categories) Categories.Add(c);

        Districts.Clear();
        foreach (var d in _report.Districts) Districts.Add(d);

        TopViolationZones.Clear();
        foreach (var t in _report.TopViolationZones) TopViolationZones.Add(t);

        MonthlyTrend.Clear();
        foreach (var m in _report.MonthlyTrend) MonthlyTrend.Add(m);

        RepeatViolationBanners.Clear();
        foreach (var b in _report.RepeatViolationBanners) RepeatViolationBanners.Add(b);
    }

    private void RefreshCharts()
    {
        var labels = _report.Districts.Select(d => d.DistrictName).ToArray();

        var series = new List<ISeries>
        {
            new ColumnSeries<int>
            {
                Name = "당월",
                Values = _report.Districts.Select(d => d.CurrentCount).ToArray(),
                Fill = new SolidColorPaint(SKColor.Parse("#2563EB")),
                MaxBarWidth = 28
            }
        };

        if (CompareWithPrevious)
        {
            series.Add(new ColumnSeries<int>
            {
                Name = "전월",
                Values = _report.Districts.Select(d => d.PreviousCount).ToArray(),
                Fill = new SolidColorPaint(SKColor.Parse("#D1D5DB")),
                MaxBarWidth = 28
            });
        }

        DistrictSeries = series.ToArray();
        DistrictXAxes = [ChartFontHelper.CreateCategoryAxis(labels)];
        DistrictYAxes = [ChartFontHelper.CreateValueAxis("건수")];

        CategorySeries = _report.Categories
            .Where(c => c.Count > 0)
            .Select(c => new PieSeries<int>
            {
                Name = c.DisplayName,
                Values = [c.Count],
                InnerRadius = 55,
                OuterRadiusOffset = 8,
                Fill = new SolidColorPaint(SKColor.Parse(c.ColorHex)),
                DataLabelsSize = 12,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                DataLabelsPaint = ChartFontHelper.LabelPaint(11),
                DataLabelsFormatter = _ => $"{c.RatioPct:F0}%"
            })
            .Cast<ISeries>()
            .ToArray();

        var hours = _report.HourlyPattern;
        HourlySeries =
        [
            new LineSeries<int>
            {
                Name = "평일",
                Values = hours.Select(h => h.WeekdayCount).ToArray(),
                Stroke = new SolidColorPaint(SKColor.Parse("#7C3AED"), 3),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.6
            },
            new LineSeries<int>
            {
                Name = "주말/공휴일",
                Values = hours.Select(h => h.WeekendCount).ToArray(),
                Stroke = new SolidColorPaint(SKColor.Parse("#F97316"), 2),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.6
            }
        ];
        HourlyXAxes = [ChartFontHelper.CreateCategoryAxis(hours.Select(h => h.HourLabel).ToArray(), 45)];
        HourlyYAxes = [ChartFontHelper.CreateValueAxis("건수")];

        MonthlySeries =
        [
            new ColumnSeries<int>
            {
                Name = "월별 건수",
                Values = _report.MonthlyTrend.Select(m => m.DetectionCount).ToArray(),
                Fill = new SolidColorPaint(SKColor.Parse("#3B82F6"))
            }
        ];
        MonthlyXAxes = [ChartFontHelper.CreateCategoryAxis(_report.MonthlyTrend.Select(m => m.MonthLabel).ToArray())];
        MonthlyYAxes = [ChartFontHelper.CreateValueAxis("건수")];

        OnPropertyChanged(nameof(DistrictSeries));
        OnPropertyChanged(nameof(CategorySeries));
        OnPropertyChanged(nameof(HourlySeries));
        OnPropertyChanged(nameof(MonthlySeries));
        OnPropertyChanged(nameof(DistrictXAxes));
        OnPropertyChanged(nameof(DistrictYAxes));
        OnPropertyChanged(nameof(HourlyXAxes));
        OnPropertyChanged(nameof(HourlyYAxes));
        OnPropertyChanged(nameof(MonthlyXAxes));
        OnPropertyChanged(nameof(MonthlyYAxes));
        OnPropertyChanged(nameof(MonthlyYAxesRight));
    }

    private void OnSelectCategory(object? parameter)
    {
        if (parameter is not ContentCategory category) return;
        _selectedCategory = category;
        _drillDownTitle = $"{category.ToDisplayName()} 상세";
        DrillDownItems.Clear();
        foreach (var item in _repository.GetDrillDown(category))
            DrillDownItems.Add(item);
        OnPropertyChanged(nameof(HasDrillDown));
        OnPropertyChanged(nameof(DrillDownTitle));
    }

    private void ClearDrillDown()
    {
        _selectedCategory = null;
        _drillDownTitle = string.Empty;
        DrillDownItems.Clear();
        OnPropertyChanged(nameof(HasDrillDown));
        OnPropertyChanged(nameof(DrillDownTitle));
    }

    private void OnDownloadReport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "통계 리포트 저장",
            Filter = "Excel 파일 (*.xlsx)|*.xlsx",
            FileName = $"통계리포트_{SelectedPeriodLabel.Replace(" ", "")}_{DateTime.Now:yyyyMMdd}.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _excelExport.Export(_report, SelectedPeriodLabel, dialog.FileName);
            _dialog.ShowInfo($"리포트가 저장되었습니다.\n{dialog.FileName}", "리포트 다운로드");
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"저장 실패: {ex.Message}", "리포트 다운로드");
        }
    }

    private void OnPrint()
    {
        _dialog.ShowInfo($"{SelectedPeriodLabel} 통계 리포트 인쇄 미리보기를 준비합니다.\n(데모)", "인쇄");
    }
}