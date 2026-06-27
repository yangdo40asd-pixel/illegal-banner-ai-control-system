using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BannerGuardSystem.Models;
using BannerGuardSystem.Services;
using Microsoft.Win32;

namespace BannerGuardSystem.ViewModels;

public class SystemSettingsViewModel : BaseViewModel
{
    private readonly ISettingsRepository _repository;
    private SystemSettingsRoot _working = SystemSettingsDefaults.Create();
    private SystemSettingsRoot _snapshot = SystemSettingsDefaults.Create();
    private bool _showSaveToast;

    public SystemSettingsViewModel(MainViewModel? mainVm = null)
        : this(new JsonSettingsRepository(), mainVm)
    {
    }

    public SystemSettingsViewModel(ISettingsRepository repository, MainViewModel? mainVm = null)
    {
        _repository = repository;
        MainVm = mainVm;

        SaveCommand = new SimpleCommand(Save);
        CancelCommand = new SimpleCommand(CancelChanges);
        ResetDataCommand = new SimpleCommand(ResetDataSettings);
        ManualBackupCommand = new SimpleCommand(ManualBackup);
        PurgeDataCommand = new SimpleCommand(PurgeData);
        ResetAllDataCommand = new SimpleCommand(ResetAllData);
        ResetSettingsOnlyCommand = new SimpleCommand(ResetSettingsOnly);

        LoadFromRepository();
    }

    public MainViewModel? MainVm { get; }

    public bool ShowSaveToast
    {
        get => _showSaveToast;
        set => SetField(ref _showSaveToast, value);
    }

    public string AutoBackupCycle
    {
        get => _working.DataManagement.AutoBackupCycle switch
        {
            BackupCycle.Daily => "매일",
            BackupCycle.Weekly => "매주",
            BackupCycle.Monthly => "매월",
            _ => "매일"
        };
        set
        {
            _working.DataManagement.AutoBackupCycle = value switch
            {
                "매주" => BackupCycle.Weekly,
                "매월" => BackupCycle.Monthly,
                _ => BackupCycle.Daily
            };
            OnPropertyChanged();
        }
    }

    public string BackupTime
    {
        get => _working.DataManagement.BackupTime;
        set { _working.DataManagement.BackupTime = value; OnPropertyChanged(); }
    }

    public string BackupPath
    {
        get => _working.DataManagement.BackupPath;
        set { _working.DataManagement.BackupPath = value; OnPropertyChanged(); }
    }

    public string DetectionLogRetention
    {
        get => _working.DataManagement.DetectionLogRetention;
        set { _working.DataManagement.DetectionLogRetention = value; OnPropertyChanged(); }
    }

    public string ReportRetention
    {
        get => _working.DataManagement.ReportRetention;
        set { _working.DataManagement.ReportRetention = value; OnPropertyChanged(); }
    }

    public string RecordingRetention
    {
        get => _working.DataManagement.RecordingRetention;
        set { _working.DataManagement.RecordingRetention = value; OnPropertyChanged(); }
    }

    public bool ExportBeforeDelete
    {
        get => _working.DataManagement.ExportBeforeDelete;
        set { _working.DataManagement.ExportBeforeDelete = value; OnPropertyChanged(); }
    }

    public string[] BackupCycleOptions { get; } = ["매일", "매주", "매월"];
    public string[] RetentionOptions { get; } = ["6개월", "1년", "2년", "3년", "영구"];
    public string[] RecordingRetentionOptions { get; } = ["7일", "14일", "30일"];

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ResetDataCommand { get; }
    public ICommand ManualBackupCommand { get; }
    public ICommand PurgeDataCommand { get; }
    public ICommand ResetAllDataCommand { get; }
    public ICommand ResetSettingsOnlyCommand { get; }

    private void LoadFromRepository()
    {
        _working = _repository.Load();
        _snapshot = Clone(_working);
        NotifyDataProperties();
    }

    private void Save()
    {
        _repository.Save(_working);
        _snapshot = Clone(_working);
        ShowToast();
        MessageBox.Show("데이터 관리 설정이 저장되었습니다.", "저장", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelChanges()
    {
        _working = Clone(_snapshot);
        NotifyDataProperties();
    }

    private void ResetDataSettings()
    {
        if (MessageBox.Show("데이터 관리 설정을 기본값으로 되돌리시겠습니까?", "초기화",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _working = _repository.ResetTab(SettingsTab.Data);
        _snapshot = Clone(_working);
        NotifyDataProperties();
    }

    private void ShowToast()
    {
        ShowSaveToast = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { ShowSaveToast = false; timer.Stop(); };
        timer.Start();
    }

    private void ManualBackup()
    {
        try
        {
            Directory.CreateDirectory(_working.DataManagement.BackupPath);
            var zip = Path.Combine(_working.DataManagement.BackupPath, $"backup_{DateTime.Now:yyyyMMdd_HHmm}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            var db = SqliteDbPath.ResolveBannerTrackingDb();
            if (File.Exists(db)) archive.CreateEntryFromFile(db, "banner_tracking.db");
            MessageBox.Show($"백업 완료:\n{zip}", "수동 백업", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"백업 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PurgeData()
    {
        if (MessageBox.Show("보관 기간 경과 데이터를 삭제하시겠습니까?", "데이터 정리",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        MessageBox.Show("데이터 정리가 완료되었습니다. (데모)", "정리", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetAllData()
    {
        if (MessageBox.Show("전체 데이터를 초기화합니다. 계속하시겠습니까?", "1차 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (MessageBox.Show("이 작업은 되돌릴 수 없습니다. 정말 초기화하시겠습니까?", "2차 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes) return;
        MessageBox.Show("전체 데이터 초기화 완료 (데모)", "초기화", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetSettingsOnly()
    {
        if (MessageBox.Show("설정만 기본값으로 초기화하시겠습니까?", "설정 초기화",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _working = _repository.Reset();
        _snapshot = Clone(_working);
        NotifyDataProperties();
    }

    private static SystemSettingsRoot Clone(SystemSettingsRoot src) =>
        System.Text.Json.JsonSerializer.Deserialize<SystemSettingsRoot>(
            System.Text.Json.JsonSerializer.Serialize(src)) ?? SystemSettingsDefaults.Create();

    private void NotifyDataProperties()
    {
        OnPropertyChanged(nameof(AutoBackupCycle));
        OnPropertyChanged(nameof(BackupTime));
        OnPropertyChanged(nameof(BackupPath));
        OnPropertyChanged(nameof(DetectionLogRetention));
        OnPropertyChanged(nameof(ReportRetention));
        OnPropertyChanged(nameof(RecordingRetention));
        OnPropertyChanged(nameof(ExportBeforeDelete));
    }
}