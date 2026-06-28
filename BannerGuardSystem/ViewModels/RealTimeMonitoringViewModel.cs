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
                if (value != null)
                {
                    var ch = CctvViewModel.Channels.FirstOrDefault(c => c.ChannelId == value.CctvId);
                    if (ch != null) CctvViewModel.SelectedChannel = ch;
                    MapQuery = value.LocationAddress ?? MapQuery;
                }
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
        AlertService.VlmCompletedAlertReceived += OnVlmCompletedAlertReceived;

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

        ConfirmLegalCommand = new RelayCommand(p => HandleVerdict("합법", "00", p as string));
        ConfirmIllegalCommand = new RelayCommand(p => HandleVerdict("불법", "01", p as string));
        RequestReviewCommand = new RelayCommand(p => HandleVerdict("재검토", "02", p as string));
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

    private void OnVlmCompletedAlertReceived(BannerDetectionEventDto alert)
    {
        ActiveAlert = alert;
    }

    private void HandleVerdict(string verdict, string statusCode, string? label)
    {
        var alert = ActiveAlert;
        if (alert == null) return;

        string aiStatus = alert.IsVlmTriggered ? "02" : "01";
        string dbStatus = "10";
        string dbVerdict = verdict == "합법" ? "Legal" : (verdict == "불법" ? "Illegal" : "Review");

        alert.VlmAnalysis = string.IsNullOrWhiteSpace(alert.VlmAnalysis) ? label : $"{alert.VlmAnalysis}\n{label}";

        _dbService.SaveHumanReview(alert.Id, alert.CctvId, alert.SourceImage, aiStatus, verdict, statusCode, alert.OcrText);
        _dbService.UpdateBannerStatusByCctvText(alert.CctvId, alert.OcrText, dbStatus, dbVerdict);
        _dbService.UpdateEnforcementHumanVerdict(alert.Id, verdict, statusCode, Environment.UserName);
        AlertService.NotifyEnforcementListChanged();

        _ = _wsService.SendCommandAsync("human_verdict", new
        {
            command = "human_verdict",
            alertId = alert.Id,
            cctvId = alert.CctvId,
            sourceImage = alert.SourceImage,
            verdict,
            statusCode,
            aiStatusCode = aiStatus,
            reviewer = Environment.UserName,
            ocrText = alert.OcrText,
            note = label
        });

        AlertService.CompletedCount++;
        if (AlertService.PendingCount > 0)
            AlertService.PendingCount--;
    }
}
