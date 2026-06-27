using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.ViewModels;

public class EnforcementStatusViewModel : BaseViewModel
{
    private List<EnforcementRecord> _allRecords = [];
    private string _searchText = string.Empty;
    private EnforcementRecord? _selectedRecord;
    private string _mapQuery = "서울특별시 강남구 역삼동";

    public event Action<EnforcementRecord>? DetailDialogRequested;

    public EnforcementStatusViewModel()
    {
        Records = new ObservableCollection<EnforcementRecord>();
        ViolatorHistory = new ObservableCollection<ViolatorPastRecord>();

        SearchCommand = new SimpleCommand(ApplyFilter);
        ExportExcelCommand = new SimpleCommand(OnExportExcel);
        PrintCommand = new SimpleCommand(OnPrint);
        DetailCommand = new RelayCommand(OnDetail);
        SecondaryActionCommand = new RelayCommand(OnSecondaryAction);
        CloseDialogCommand = new SimpleCommand(() => IsDialogOpen = false);

        LoadMockData();
        ApplyFilter();
    }

    public ObservableCollection<EnforcementRecord> Records { get; }
    public ObservableCollection<ViolatorPastRecord> ViolatorHistory { get; }

    public int TodayCount { get; private set; } = 12;
    public string TodayDeltaText { get; } = "전일 대비 +32%";
    public int WeekCount { get; private set; } = 47;
    public string TotalFineText { get; } = "3,240만원";
    public int AppealCount { get; private set; } = 2;
    public string AppealSubText { get; } = "처리중 1건";
    public string UnpaidFineText { get; } = "480만원";
    public string CollectionRateText { get; } = "수납률 85.2%";

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
            UpdateViolatorPanel();
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

    public string SelectedViolatorName => SelectedRecord?.ViolatorName ?? "—";
    public string SelectedViolatorBusiness => SelectedRecord?.BusinessName ?? "";
    public string SelectedRiskSummary => SelectedRecord?.RiskSummary ?? "이력 없음";
    public bool HasHighRiskViolator => SelectedRecord?.IsHighRisk == true;

    public ICommand SearchCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand DetailCommand { get; }
    public ICommand SecondaryActionCommand { get; }
    public ICommand CloseDialogCommand { get; }

    private void LoadMockData()
    {
        _allRecords =
        [
            new EnforcementRecord
            {
                EnforcementNo = "E-2026-0627-001",
                DetectedAt = new DateTime(2026, 6, 27, 14, 10, 0),
                Location = "강남구 역삼동 청담로 전봇대 #A-12",
                MapQuery = "서울특별시 강남구 역삼동 청담로",
                ViolationType = EnforcementViolationType.IllegalBanner,
                ViolatorName = "김○○",
                BusinessName = "○○부동산",
                FineAmountManwon = 300,
                Status = EnforcementProcessingStatus.FineImposed,
                Assignee = "김무주",
                PrimaryAction = "상세",
                SecondaryAction = "독촉장",
                IsHighRisk = true,
                PastViolationCount = 3,
                AccumulatedFineManwon = 900,
                PastRecords =
                [
                    new ViolatorPastRecord { DetectedDate = new DateTime(2026, 3, 15), Location = "강남구 역삼동", ViolationType = "불법현수막", FineAmount = "300만원", PaymentStatus = "납부" },
                    new ViolatorPastRecord { DetectedDate = new DateTime(2025, 11, 2), Location = "서초구 서초동", ViolationType = "불법현수막", FineAmount = "300만원", PaymentStatus = "납부" },
                    new ViolatorPastRecord { DetectedDate = new DateTime(2024, 8, 20), Location = "강남구 논현동", ViolationType = "불법현수막", FineAmount = "300만원", PaymentStatus = "납부" }
                ]
            },
            new EnforcementRecord
            {
                EnforcementNo = "E-2026-0626-015",
                DetectedAt = new DateTime(2026, 6, 26, 23, 45, 0),
                Location = "서초구 서초동 가로등 #B-07",
                MapQuery = "서울특별시 서초구 서초동",
                ViolationType = EnforcementViolationType.IllegalBanner,
                ViolatorName = "박○○",
                BusinessName = "○○학원",
                FineAmountManwon = 200,
                Status = EnforcementProcessingStatus.Appeal,
                Assignee = "이주희",
                PrimaryAction = "상세",
                SecondaryAction = "심사"
            },
            new EnforcementRecord
            {
                EnforcementNo = "E-2026-0626-008",
                DetectedAt = new DateTime(2026, 6, 26, 18, 20, 0),
                Location = "송파구 잠실동 삼거리 밀집 지역",
                MapQuery = "서울특별시 송파구 잠실동",
                ViolationType = EnforcementViolationType.IllegalBanner,
                ViolatorName = "최○○",
                BusinessName = "○○마트",
                FineAmountManwon = 150,
                Status = EnforcementProcessingStatus.Delinquent,
                Assignee = "김무주",
                PrimaryAction = "독촉",
                SecondaryAction = "접수"
            },
            new EnforcementRecord
            {
                EnforcementNo = "E-2026-0610-023",
                DetectedAt = new DateTime(2026, 6, 10, 9, 15, 0),
                Location = "마포구 홍대입구역 인근",
                MapQuery = "서울특별시 마포구 홍대입구역",
                ViolationType = EnforcementViolationType.IllegalBanner,
                ViolatorName = "정○○",
                BusinessName = "",
                FineAmountManwon = 80,
                Status = EnforcementProcessingStatus.Urgent,
                Assignee = "박무주",
                PrimaryAction = "상세",
                SecondaryAction = "접수"
            },
            new EnforcementRecord
            {
                EnforcementNo = "E-2026-0608-011",
                DetectedAt = new DateTime(2026, 6, 8, 11, 30, 0),
                Location = "용산구 이태원동 #C-05",
                MapQuery = "서울특별시 용산구 이태원동",
                ViolationType = EnforcementViolationType.IllegalBanner,
                ViolatorName = "한○○",
                BusinessName = "○○카페",
                FineAmountManwon = 250,
                Status = EnforcementProcessingStatus.FineImposed,
                Assignee = "이주희",
                PrimaryAction = "상세",
                SecondaryAction = "독촉장"
            }
        ];
    }

    private void ApplyFilter()
    {
        var query = _searchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allRecords
            : _allRecords.Where(r =>
                r.EnforcementNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Location.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.ViolatorName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Assignee.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        Records.Clear();
        foreach (var item in filtered)
            Records.Add(item);

        if (SelectedRecord == null || !Records.Contains(SelectedRecord))
            SelectedRecord = Records.FirstOrDefault();
    }

    private void UpdateViolatorPanel()
    {
        ViolatorHistory.Clear();
        if (SelectedRecord?.PastRecords != null)
        {
            foreach (var past in SelectedRecord.PastRecords)
                ViolatorHistory.Add(past);
        }

        OnPropertyChanged(nameof(SelectedViolatorName));
        OnPropertyChanged(nameof(SelectedViolatorBusiness));
        OnPropertyChanged(nameof(SelectedRiskSummary));
        OnPropertyChanged(nameof(HasHighRiskViolator));
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
        MessageBox.Show(
            $"{record.EnforcementNoDisplay} — [{record.SecondaryAction}] 처리를 진행합니다.",
            "단속 처리",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnExportExcel()
    {
        MessageBox.Show(
            $"단속 현황 {_allRecords.Count}건을 엑셀로보냅니다.\n(데모: 실제 파일 생성은 추후 연동)",
            "엑셀 다운로드",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnPrint()
    {
        MessageBox.Show("단속 현황 인쇄 미리보기를 준비합니다.\n(데모)", "인쇄",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}