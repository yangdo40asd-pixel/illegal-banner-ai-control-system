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
    private readonly PdfReportService _pdfService;
    private List<EnforcementRecord> _allRecords = [];
    private string _searchText = string.Empty;
    private EnforcementRecord? _selectedRecord;
    private string _mapQuery = "인천광역시";

    public event Action<EnforcementRecord>? DetailDialogRequested;

    public EnforcementStatusViewModel(DatabaseService db, IDialogService dialog, AlertService alertService, PdfReportService pdfService)
    {
        _db = db;
        _dialog = dialog;
        _alertService = alertService;
        _pdfService = pdfService;
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

    private bool _hasLoadError;
    public bool HasLoadError
    {
        get => _hasLoadError;
        private set
        {
            if (SetField(ref _hasLoadError, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public bool IsEmpty => !HasLoadError && Records.Count == 0;

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
        _allRecords = _db.GetEnforcementRecords(out var err);
        if (!string.IsNullOrEmpty(err))
        {
            HasLoadError = true;
            ErrorMessage = $"데이터베이스 연결 오류가 발생했습니다. 다시 시도해 주세요 ({err})";
        }
        else
        {
            HasLoadError = false;
            ErrorMessage = string.Empty;
        }

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
        var prevNo = _selectedRecord?.EnforcementNo;
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

        OnPropertyChanged(nameof(IsEmpty));

        if (!string.IsNullOrEmpty(prevNo))
            SelectedRecord = Records.FirstOrDefault(r => r.EnforcementNo == prevNo) ?? Records.FirstOrDefault();
        else
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

        if (record.SecondaryAction == "접수")
        {
            bool ok = _db.UpdateEnforcementProcessingStatus(record.EnforcementNo, "Received", "관제관");
            if (ok)
            {
                Refresh();
            }
            else
            {
                _dialog.ShowInfo("상태 갱신에 실패했습니다.", "단속 처리 오류");
            }
        }
        else if (record.SecondaryAction == "검토")
        {
            _db.BuildSimilarReviewsForEnforcement(record.EnforcementNo);
            UpdateSimilarityPanel();
            _db.UpdateEnforcementProcessingStatus(record.EnforcementNo, "Appeal", "관제관");
            Refresh();
        }
    }

    private void OnExportExcel()
    {
        if (_allRecords.Count == 0)
        {
            _dialog.ShowInfo("내보낼 단속 내역이 없습니다.", "엑셀 다운로드");
            return;
        }

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
            FileName = $"단속현황_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                using var workbook = new ClosedXML.Excel.XLWorkbook();
                var worksheet = workbook.Worksheets.Add("단속현황");
                worksheet.Cell(1, 1).Value = "관리번호";
                worksheet.Cell(1, 2).Value = "단속일시";
                worksheet.Cell(1, 3).Value = "위치";
                worksheet.Cell(1, 4).Value = "위반유형";
                worksheet.Cell(1, 5).Value = "내용요약";
                worksheet.Cell(1, 6).Value = "처리상태";

                int row = 2;
                foreach (var r in _allRecords)
                {
                    worksheet.Cell(row, 1).Value = r.EnforcementNoDisplay;
                    worksheet.Cell(row, 2).Value = r.DetectedAtText;
                    worksheet.Cell(row, 3).Value = r.Location;
                    worksheet.Cell(row, 4).Value = r.ViolationTypeDisplay;
                    worksheet.Cell(row, 5).Value = r.BannerSummaryOneLine;
                    worksheet.Cell(row, 6).Value = r.StatusDisplay;
                    row++;
                }

                worksheet.Columns().AdjustToContents();
                workbook.SaveAs(sfd.FileName);
                _dialog.ShowInfo($"엑셀 파일이 성공적으로 저장되었습니다:\n{sfd.FileName}", "엑셀 다운로드 완료");
            }
            catch (Exception ex)
            {
                _dialog.ShowInfo($"엑셀 저장 실패: {ex.Message}", "오류");
            }
        }
    }

    private void OnPrint()
    {
        if (SelectedRecord == null && _allRecords.Count == 0)
        {
            _dialog.ShowInfo("출력할 단속 내역이 없습니다.", "인쇄");
            return;
        }

        var sfd = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF 문서 (*.pdf)|*.pdf",
            FileName = $"단속보고서_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                var target = SelectedRecord ?? _allRecords.First();
                _pdfService.GenerateReport(target, sfd.FileName);
                _dialog.ShowInfo($"단속 보고서가 성공적으로 저장되었습니다:\n{sfd.FileName}", "인쇄 완료");
            }
            catch (Exception ex)
            {
                _dialog.ShowInfo($"보고서 생성 실패: {ex.Message}", "오류");
            }
        }
    }
}