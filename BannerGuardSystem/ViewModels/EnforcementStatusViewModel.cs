using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.ViewModels;

public class EnforcementStatusViewModel : BaseViewModel
{
    private readonly DatabaseService _db;
    private readonly IDialogService _dialog;
    private readonly AlertService _alertService;
    private List<EnforcementRecord> _allRecords = [];
    private string _searchText = string.Empty;
    private EnforcementRecord? _selectedRecord;
    private string _mapQuery = "인천광역시";

    public event Action<EnforcementRecord>? DetailDialogRequested;

    public EnforcementStatusViewModel(DatabaseService db, IDialogService dialog, AlertService alertService)
    {
        _db = db;
        _dialog = dialog;
        _alertService = alertService;
        Records = new ObservableCollection<EnforcementRecord>();
        SimilarityRecords = new ObservableCollection<SimilarEnforcementRecord>();

        _alertService.EnforcementListChanged += OnEnforcementListChanged;

        SearchCommand = new SimpleCommand(ApplyFilter);
        ExportExcelCommand = new SimpleCommand(OnExportExcel);
        PrintCommand = new SimpleCommand(OnPrint);
        DetailCommand = new RelayCommand(OnDetail);
        SecondaryActionCommand = new RelayCommand(OnSecondaryAction);
        CloseDialogCommand = new SimpleCommand(() => IsDialogOpen = false);
        RefreshCommand = new SimpleCommand(Refresh);

        Refresh();
    }

    private void OnEnforcementListChanged() => RunOnUi(Refresh);

    public ObservableCollection<EnforcementRecord> Records { get; }
    public ObservableCollection<SimilarEnforcementRecord> SimilarityRecords { get; }

    public int TodayCount { get; private set; }
    public string TodayDeltaText { get; private set; } = "—";
    public int WeekCount { get; private set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                ApplyFilter();
        }
    }

    public EnforcementRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetField(ref _selectedRecord, value))
                return;
            UpdateSimilarityPanel();
            if (value != null && !string.IsNullOrEmpty(value.MapQuery))
                MapQuery = value.MapQuery;
        }
    }

    public string MapQuery
    {
        get => _mapQuery;
        set => SetField(ref _mapQuery, value);
    }

    private bool _isDialogOpen;
    public bool IsDialogOpen
    {
        get => _isDialogOpen;
        set => SetField(ref _isDialogOpen, value);
    }

    private EnforcementRecord? _dialogRecord;
    public EnforcementRecord? DialogRecord
    {
        get => _dialogRecord;
        set => SetField(ref _dialogRecord, value);
    }

    public string SimilaritySummary => SelectedRecord == null
        ? "단속 건을 선택하면 유사 현수막을 검토합니다."
        : SimilarityRecords.Count > 0
            ? $"선택 건과 유사한 과거 단속 {SimilarityRecords.Count}건 (이미지·텍스트 유사도 기준)"
            : SelectedRecord.NeedsSimilarityReview
                ? "유사한 과거 단속 이력이 없습니다."
                : "검토 대상 건이 아닙니다.";

    public bool HasSimilarRecords => SimilarityRecords.Count > 0;

    public ICommand SearchCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand DetailCommand { get; }
    public ICommand SecondaryActionCommand { get; }
    public ICommand CloseDialogCommand { get; }
    public ICommand RefreshCommand { get; }

    public void Refresh()
    {
        _allRecords = _db.GetEnforcementRecords();
        var kpi = _db.GetEnforcementKpi();
        TodayCount = kpi.TodayCount;
        TodayDeltaText = kpi.TodayDeltaText;
        WeekCount = kpi.WeekCount;

        OnPropertyChanged(nameof(TodayCount));
        OnPropertyChanged(nameof(TodayDeltaText));
        OnPropertyChanged(nameof(WeekCount));

        ApplyFilter();
        if (_allRecords.Count == 0)
            MapQuery = "인천광역시";
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allRecords
            : _allRecords.Where(r =>
                r.EnforcementNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Location.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.BannerSummaryOneLine.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.OcrText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Records.Clear();
        foreach (var item in filtered)
            Records.Add(item);

        if (SelectedRecord == null || !Records.Contains(SelectedRecord))
            SelectedRecord = Records.FirstOrDefault();
    }

    private void UpdateSimilarityPanel()
    {
        SimilarityRecords.Clear();
        if (SelectedRecord == null)
        {
            NotifySimilarityProperties();
            return;
        }

        if (SelectedRecord.NeedsSimilarityReview)
        {
            var saved = _db.GetSimilarEnforcementReviews(SelectedRecord.EnforcementNo);
            if (saved.Count == 0)
            {
                _db.BuildSimilarReviewsForEnforcement(SelectedRecord.EnforcementNo);
                saved = _db.GetSimilarEnforcementReviews(SelectedRecord.EnforcementNo);
            }

            foreach (var item in saved)
                SimilarityRecords.Add(item);
        }

        NotifySimilarityProperties();
    }

    private void NotifySimilarityProperties()
    {
        OnPropertyChanged(nameof(SimilaritySummary));
        OnPropertyChanged(nameof(HasSimilarRecords));
    }

    private void OnDetail(object? parameter)
    {
        if (parameter is not EnforcementRecord record) return;
        SelectedRecord = record;
        DialogRecord = record;
        IsDialogOpen = true;
        DetailDialogRequested?.Invoke(record);
    }

    private void OnSecondaryAction(object? parameter)
    {
        if (parameter is not EnforcementRecord record) return;
        SelectedRecord = record;

        if (record.SecondaryAction == "검토")
        {
            _db.BuildSimilarReviewsForEnforcement(record.EnforcementNo);
            UpdateSimilarityPanel();
        }

        _dialog.ShowInfo($"{record.EnforcementNoDisplay} — [{record.SecondaryAction}] 처리를 진행합니다.", "단속 처리");
    }

    private void OnExportExcel()
    {
        _dialog.ShowInfo($"단속 현황 {_allRecords.Count}건 (검증 긴급 연동 데이터)", "엑셀 다운로드");
    }

    private void OnPrint()
    {
        _dialog.ShowInfo($"단속 현황 {_allRecords.Count}건 인쇄 준비", "인쇄");
    }
}