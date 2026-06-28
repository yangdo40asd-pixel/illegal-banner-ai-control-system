using System;
using System.ComponentModel;
using System.Windows.Input;
using BannerGuardSystem.Helpers;
using BannerGuardSystem.Services;

namespace BannerGuardSystem.ViewModels;

public class ShellViewModel : BaseViewModel
{
    private readonly SystemMonitorService _systemMonitor;
    private readonly INavigationService _navigationService;

    public ShellViewModel(SystemMonitorService systemMonitor, INavigationService navigationService, RealTimeMonitoringViewModel rtVm)
    {
        _systemMonitor = systemMonitor ?? throw new ArgumentNullException(nameof(systemMonitor));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        
        _systemMonitor.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

        NavigateCommand = new RelayCommand(p =>
        {
            if (p is string viewKey)
            {
                _navigationService.Navigate(viewKey);
            }
        });

        _navigationService.Navigated += (view) => CurrentView = view;

        ApproveCommand = rtVm.ConfirmIllegalCommand;
        DismissCommand = rtVm.RequestReviewCommand;

        // Initialize default view
        _navigationService.Navigate("RealTimeMonitoring");
    }

    private object? _currentView;
    public object? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public bool IsConnected
    {
        get => _systemMonitor.IsConnected;
        set => _systemMonitor.IsConnected = value;
    }

    public string ConnectionStatus => _systemMonitor.ConnectionStatus;
    public string SystemModeText => _systemMonitor.SystemModeText;
    public string AiStatusText => _systemMonitor.AiStatusText;
    public string CpuUsageText => _systemMonitor.CpuUsageText;
    public string RamUsageText => _systemMonitor.RamUsageText;
    public string GpuUsageText => _systemMonitor.GpuUsageText;
    public string CurrentTimeText => _systemMonitor.CurrentTimeText;

    public bool IsAutomationEnabled
    {
        get => _systemMonitor.IsAutomationEnabled;
        set => _systemMonitor.IsAutomationEnabled = value;
    }

    public bool IsImageCropEnabled
    {
        get => _systemMonitor.IsImageCropEnabled;
        set => _systemMonitor.IsImageCropEnabled = value;
    }

    public ICommand NavigateCommand { get; }

    public ICommand ApproveCommand { get; }
    public ICommand DismissCommand { get; }
}
