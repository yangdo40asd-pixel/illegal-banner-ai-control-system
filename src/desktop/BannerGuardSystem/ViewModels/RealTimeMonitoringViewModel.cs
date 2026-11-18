using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Linq;
using System.Windows.Data;
using BannerGuardSystem.Helpers;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.ViewModels;

public class RealTimeMonitoringViewModel : BaseViewModel
{
    private readonly WebSocketService _wsService;
    private readonly DatabaseService _dbService;
    private readonly SystemMonitorService _systemMonitor;
    public AlertService AlertService { get; }
    public CctvMonitoringViewModel CctvViewModel { get; }

    public int TodayDetectionCount => AlertService.TodayDetectionCount;
    public int PendingCount => AlertService.PendingCount;
    public int CompletedCount => AlertService.CompletedCount;

    private string _feedFilter = "All";

    public bool IsFilterAll
    {
        get => _feedFilter == "All";
        set { if (value) FeedFilter = "All"; }
    }
    public bool IsFilterEmergency
    {
        get => _feedFilter == "Emergency";
        set { if (value) FeedFilter = "Emergency"; }
    }
    public bool IsFilterNormal
    {
        get => _feedFilter == "Normal";
        set { if (value) FeedFilter = "Normal"; }
    }

    public string FeedFilter
    {
        get => _feedFilter;
        set
        {
            if (SetProperty(ref _feedFilter, value))
            {
                FilteredAlertFeed.Refresh();
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterEmergency));
                OnPropertyChanged(nameof(IsFilterNormal));
            }
        }
    }

    public ICollectionView FilteredAlertFeed { get; }

    private BannerDetectionEventDto? _activeAlert;
    public BannerDetectionEventDto? ActiveAlert
    {
        get => _activeAlert;
        set
        {
            if (SetProperty(ref _activeAlert, value))
            {
                OnPropertyChanged(nameof(HasActiveAlert));
                OnPropertyChanged(nameof(HasNoActiveAlert));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                if (value != null)
                {
                    var ch = CctvViewModel.Channels.FirstOrDefault(c => c.ChannelId == value.CctvId);
                    if (ch != null) CctvViewModel.SelectedChannel = ch;
                    _mapQuery = LocationMapHelper.ResolveMapQuery(value);
                    OnPropertyChanged(nameof(MapQuery));
                }
            }
        }
    }

    private bool _isSavingVerdict;
    public bool IsSavingVerdict
    {
        get => _isSavingVerdict;
        private set
        {
            if (SetProperty(ref _isSavingVerdict, value))
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool HasActiveAlert => ActiveAlert != null;
    public bool HasNoActiveAlert => ActiveAlert == null;

    public CctvChannel? SelectedChannel => CctvViewModel.SelectedChannel;

    private string _mapQuery = "인천광역시 연수구 송도동";
    public string MapQuery
    {
        get => _mapQuery;
        set => SetProperty(ref _mapQuery, value);
    }

    public ICommand SelectAlertCommand { get; }
    public ICommand ConfirmLegalCommand { get; }
    public ICommand ConfirmIllegalCommand { get; }
    public ICommand RequestReviewCommand { get; }

    public RealTimeMonitoringViewModel(
        WebSocketService wsService,
        DatabaseService dbService,
        SystemMonitorService systemMonitor,
        AlertService alertService,
        CctvMonitoringViewModel cctvViewModel)
    {
        _wsService = wsService;
        _dbService = dbService;
        _systemMonitor = systemMonitor;
        AlertService = alertService;
        CctvViewModel = cctvViewModel;

        FilteredAlertFeed = new CollectionViewSource { Source = AlertService.AlertFeed }.View;
        FilteredAlertFeed.Filter = FilterAlert;

        AlertService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AlertService.TodayDetectionCount)) OnPropertyChanged(nameof(TodayDetectionCount));
            if (e.PropertyName == nameof(AlertService.PendingCount)) OnPropertyChanged(nameof(PendingCount));
            if (e.PropertyName == nameof(AlertService.CompletedCount)) OnPropertyChanged(nameof(CompletedCount));
        };
        CctvViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CctvViewModel.SelectedChannel))
            {
                OnPropertyChanged(nameof(SelectedChannel));
            }
        };

        SelectAlertCommand = new RelayCommand(p =>
        {
            if (p is BannerDetectionEventDto alert)
            {
                ActiveAlert = alert;
            }
        });

        ConfirmLegalCommand = new RelayCommand(p => HandleVerdict("LEGAL", "00", p as string), _ => !IsSavingVerdict && ActiveAlert != null);
        ConfirmIllegalCommand = new RelayCommand(p => HandleVerdict("ILLEGAL", "01", p as string), _ => !IsSavingVerdict && ActiveAlert != null);
        RequestReviewCommand = new RelayCommand(p => HandleVerdict("REVIEW", "02", p as string), _ => !IsSavingVerdict && ActiveAlert != null);
    }

    private bool FilterAlert(object item)
    {
        if (item is BannerDetectionEventDto alert)
        {
            if (FeedFilter == "All") return true;
            if (FeedFilter == "Emergency") return alert.IsEmergency;
            if (FeedFilter == "Normal") return !alert.IsEmergency;
        }
        return true;
    }

    private void HandleVerdict(string internalVerdict, string statusCode, string? label)
    {
        var alert = ActiveAlert;
        if (alert == null || IsSavingVerdict) return;

        IsSavingVerdict = true;
        try
        {
            string aiStatus = alert.IsVlmTriggered ? "02" : "01";
            string dbStatus = "10";
            string dbVerdict = internalVerdict switch
            {
                "LEGAL" => "Legal",
                "ILLEGAL" => "Illegal",
                _ => "Review"
            };

            string displayLabel = label ?? (internalVerdict switch
            {
                "LEGAL" => "합법 확인",
                "ILLEGAL" => "불법 확정",
                _ => "재검토 요청"
            });

            alert.VlmAnalysis = string.IsNullOrWhiteSpace(alert.VlmAnalysis)
                ? displayLabel
                : $"{alert.VlmAnalysis}\n{displayLabel}";

            // 단일 트랜잭션으로 원자적 저장 (human_reviews + tracked_banners + enforcement_records)
            bool saved = _dbService.SaveVerdictTransaction(
                alert.Id,
                alert.CctvId,
                alert.SourceImage,
                aiStatus,
                internalVerdict,
                statusCode,
                alert.OcrText,
                dbStatus,
                dbVerdict,
                Environment.UserName,
                displayLabel
            );

            if (!saved)
            {
                System.Windows.MessageBox.Show(
                    "판정 결과를 데이터베이스에 저장하는 데 실패했습니다.\n잠시 후 다시 시도해 주세요.",
                    "판정 저장 오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            alert.StatusCode = internalVerdict == "ILLEGAL" ? "ILLEGAL_CONTENT" : (internalVerdict == "LEGAL" ? "LEGAL_PUBLIC" : "MANUAL_REVIEW");
            AlertService.NotifyEnforcementListChanged();

            _ = _wsService.SendCommandAsync("human_verdict", new
            {
                command = "human_verdict",
                alertId = alert.Id,
                cctvId = alert.CctvId,
                sourceImage = alert.SourceImage,
                verdict = internalVerdict, // "LEGAL", "ILLEGAL", "REVIEW" 영문 표준 코드 전달
                statusCode,
                aiStatusCode = aiStatus,
                reviewer = Environment.UserName,
                ocrText = alert.OcrText,
                note = displayLabel
            });

            // Feed 전체 상태를 기반으로 KPI 일관 동기화 (수동 ++/-- 연산 제거)
            AlertService.SyncVlmKpiFromFeed();
        }
        finally
        {
            IsSavingVerdict = false;
        }
    }
}
