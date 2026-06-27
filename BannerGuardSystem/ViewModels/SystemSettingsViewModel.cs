using System.Collections.ObjectModel;
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
    private static readonly Dictionary<string, string[]> CityDistrictMap = new()
    {
        ["인천광역시"] = ["중구", "동구", "미추홀구", "연수구", "남동구", "부평구", "계양구", "서구", "강화군", "옹진군"],
        ["서울특별시"] = ["강남구", "서초구", "송파구", "마포구", "용산구", "영등포구"],
        ["경기도"] = ["수원시", "성남시", "고양시", "부천시", "안양시"]
    };

    private readonly ISettingsRepository _repository;
    private SystemSettingsRoot _working = SystemSettingsDefaults.Create();
    private SystemSettingsRoot _snapshot = SystemSettingsDefaults.Create();
    private SettingsTab _selectedTab = SettingsTab.AiDetection;
    private LegalBoardItem? _selectedBoard;
    private bool _showSaveToast;

    public SystemSettingsViewModel(MainViewModel? mainVm = null)
        : this(new JsonSettingsRepository(), mainVm)
    {
    }

    public SystemSettingsViewModel(ISettingsRepository repository, MainViewModel? mainVm = null)
    {
        _repository = repository;
        MainVm = mainVm;

        DistrictOptions = new ObservableCollection<string>();
        LegalBoards = new ObservableCollection<LegalBoardItem>();
        SchoolZones = new ObservableCollection<SchoolZoneItem>();

        SelectTabCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<SettingsTab>(s, out var tab))
                SelectedTab = tab;
        });
        SaveCommand = new SimpleCommand(Save);
        CancelCommand = new SimpleCommand(CancelChanges);
        ResetTabCommand = new RelayCommand(p =>
        {
            if (p is SettingsTab tab)
                ResetCurrentTab(tab);
            else if (p is string s && Enum.TryParse<SettingsTab>(s, out var parsed))
                ResetCurrentTab(parsed);
        });
        AddBoardCommand = new SimpleCommand(AddBoard);
        DeleteBoardCommand = new SimpleCommand(DeleteBoard, () => SelectedBoard != null);
        AddSchoolCommand = new SimpleCommand(AddSchool);
        ImportBoardCsvCommand = new SimpleCommand(ImportBoardCsv);

        ManualBackupCommand = new SimpleCommand(ManualBackup);
        PurgeDataCommand = new SimpleCommand(PurgeData);
        ResetAllDataCommand = new SimpleCommand(ResetAllData);
        ResetSettingsOnlyCommand = new SimpleCommand(ResetSettingsOnly);

        LoadFromRepository();
    }

    public MainViewModel? MainVm { get; }

    public ObservableCollection<string> DistrictOptions { get; }
    public ObservableCollection<LegalBoardItem> LegalBoards { get; }
    public ObservableCollection<SchoolZoneItem> SchoolZones { get; }

    public SettingsTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetField(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(IsAiTab));
                OnPropertyChanged(nameof(IsNotificationTab));
                OnPropertyChanged(nameof(IsJurisdictionTab));
                OnPropertyChanged(nameof(IsDataTab));
            }
        }
    }

    public bool IsAiTab => SelectedTab == SettingsTab.AiDetection;
    public bool IsNotificationTab => SelectedTab == SettingsTab.Notification;
    public bool IsJurisdictionTab => SelectedTab == SettingsTab.Jurisdiction;
    public bool IsDataTab => SelectedTab == SettingsTab.Data;

    public bool ShowSaveToast
    {
        get => _showSaveToast;
        set => SetField(ref _showSaveToast, value);
    }

    // ── AI Detection (bound nested) ──
    public double YoloThreshold
    {
        get => _working.AiDetection.YoloThresholdPct;
        set { _working.AiDetection.YoloThresholdPct = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpectedDetectionsText)); }
    }
    public string ExpectedDetectionsText => $"시간당 예상 탐지: 약 {(int)(YoloThreshold * 2.05)}건";
    public string MinDurationFilter
    {
        get => _working.AiDetection.MinDurationFilter;
        set { _working.AiDetection.MinDurationFilter = value; OnPropertyChanged(); }
    }
    public string UrgentDurationFilter
    {
        get => _working.AiDetection.UrgentDurationFilter;
        set { _working.AiDetection.UrgentDurationFilter = value; OnPropertyChanged(); }
    }
    public bool NightDoubleEnabled
    {
        get => _working.AiDetection.NightDoubleEnabled;
        set { _working.AiDetection.NightDoubleEnabled = value; OnPropertyChanged(); }
    }
    public bool VlmEnabled
    {
        get => _working.AiDetection.VlmEnabled;
        set { _working.AiDetection.VlmEnabled = value; OnPropertyChanged(); }
    }
    public string VlmModel
    {
        get => _working.AiDetection.VlmModel;
        set { _working.AiDetection.VlmModel = value; OnPropertyChanged(); }
    }
    public bool ExcludePolitics { get => _working.AiDetection.ExcludePolitics; set { _working.AiDetection.ExcludePolitics = value; OnPropertyChanged(); } }
    public bool ExcludeCommercial { get => _working.AiDetection.ExcludeCommercial; set { _working.AiDetection.ExcludeCommercial = value; OnPropertyChanged(); } }
    public bool ExcludeReligion { get => _working.AiDetection.ExcludeReligion; set { _working.AiDetection.ExcludeReligion = value; OnPropertyChanged(); } }
    public bool ExcludeEvent { get => _working.AiDetection.ExcludeEvent; set { _working.AiDetection.ExcludeEvent = value; OnPropertyChanged(); } }
    public bool ExcludePersonal { get => _working.AiDetection.ExcludePersonal; set { _working.AiDetection.ExcludePersonal = value; OnPropertyChanged(); } }
    public bool ExcludeCivil
    {
        get => _working.AiDetection.ExcludeCivil;
        set { _working.AiDetection.ExcludeCivil = value; OnPropertyChanged(); }
    }

    public string[] DurationOptions { get; } = ["10분", "30분", "1시간"];
    public string[] UrgentDurationOptions { get; } = ["30분", "1시간", "2시간"];
    public string[] VlmModelOptions { get; } = ["기본 (Qwen2.5-VL)", "고성능 (PaliGemma 2.1)"];

    // ── Notification ──
    public double UrgentConfidence
    {
        get => _working.Notification.UrgentConfidencePct;
        set { _working.Notification.UrgentConfidencePct = value; OnPropertyChanged(); }
    }
    public bool UrgentCommercial { get => _working.Notification.UrgentCommercial; set { _working.Notification.UrgentCommercial = value; OnPropertyChanged(); } }
    public bool UrgentSchoolZone { get => _working.Notification.UrgentSchoolZone; set { _working.Notification.UrgentSchoolZone = value; OnPropertyChanged(); } }
    public bool UrgentDuration1h { get => _working.Notification.UrgentDuration1h; set { _working.Notification.UrgentDuration1h = value; OnPropertyChanged(); } }
    public bool PopupEnabled => true;
    public bool SoundEnabled { get => _working.Notification.SoundEnabled; set { _working.Notification.SoundEnabled = value; OnPropertyChanged(); } }
    public bool EmailEnabled { get => _working.Notification.EmailEnabled; set { _working.Notification.EmailEnabled = value; OnPropertyChanged(); } }
    public string EmailAddress { get => _working.Notification.EmailAddress; set { _working.Notification.EmailAddress = value; OnPropertyChanged(); } }
    public bool SmsEnabled { get => _working.Notification.SmsEnabled; set { _working.Notification.SmsEnabled = value; OnPropertyChanged(); } }
    public string SmsNumber { get => _working.Notification.SmsNumber; set { _working.Notification.SmsNumber = value; OnPropertyChanged(); } }
    public string NightStart { get => _working.Notification.NightStart; set { _working.Notification.NightStart = value; OnPropertyChanged(); } }
    public string NightEnd { get => _working.Notification.NightEnd; set { _working.Notification.NightEnd = value; OnPropertyChanged(); } }
    public NightAlertMode NightMode
    {
        get => _working.Notification.NightMode;
        set { _working.Notification.NightMode = value; OnPropertyChanged(); }
    }

    // ── Jurisdiction ──
    public string City
    {
        get => _working.Jurisdiction.City;
        set
        {
            if (_working.Jurisdiction.City == value) return;
            _working.Jurisdiction.City = value;
            OnPropertyChanged();
            RefreshDistrictOptions();
            if (!DistrictOptions.Contains(District))
                District = DistrictOptions.FirstOrDefault() ?? string.Empty;
            OnPropertyChanged(nameof(MapQuery));
        }
    }
    public string District
    {
        get => _working.Jurisdiction.District;
        set { _working.Jurisdiction.District = value; OnPropertyChanged(); OnPropertyChanged(nameof(MapQuery)); }
    }
    public string MapQuery => $"{City} {District}";
    public string[] CityOptions { get; } = ["인천광역시", "서울특별시", "경기도"];

    // ── Data ──
    public BackupCycle AutoBackupCycle
    {
        get => _working.DataManagement.AutoBackupCycle;
        set { _working.DataManagement.AutoBackupCycle = value; OnPropertyChanged(); }
    }
    public string BackupTime { get => _working.DataManagement.BackupTime; set { _working.DataManagement.BackupTime = value; OnPropertyChanged(); } }
    public string BackupPath { get => _working.DataManagement.BackupPath; set { _working.DataManagement.BackupPath = value; OnPropertyChanged(); } }
    public string DetectionLogRetention { get => _working.DataManagement.DetectionLogRetention; set { _working.DataManagement.DetectionLogRetention = value; OnPropertyChanged(); } }
    public string ReportRetention { get => _working.DataManagement.ReportRetention; set { _working.DataManagement.ReportRetention = value; OnPropertyChanged(); } }
    public string RecordingRetention { get => _working.DataManagement.RecordingRetention; set { _working.DataManagement.RecordingRetention = value; OnPropertyChanged(); } }
    public bool ExportBeforeDelete { get => _working.DataManagement.ExportBeforeDelete; set { _working.DataManagement.ExportBeforeDelete = value; OnPropertyChanged(); } }
    public string[] RetentionOptions { get; } = ["6개월", "1년", "2년", "3년", "영구"];
    public string[] RecordingRetentionOptions { get; } = ["7일", "14일", "30일"];

    // ── System info (from MainVm) ──
    public string SystemYoloVersion => "v8.2";
    public string SystemVlmModel => _working.AiDetection.VlmModel;
    public string SystemDbVersion => "SQLite 3.45";
    public string ServerStatus => MainVm?.IsConnected == true ? "● 정상" : "● 대기";
    public string CpuText => MainVm?.CpuUsageText ?? "—";
    public string RamText => MainVm?.RamUsageText ?? "—";

    public LegalBoardItem? SelectedBoard
    {
        get => _selectedBoard;
        set => SetField(ref _selectedBoard, value);
    }

    public ICommand SelectTabCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ResetTabCommand { get; }
    public ICommand AddBoardCommand { get; }
    public ICommand DeleteBoardCommand { get; }
    public ICommand AddSchoolCommand { get; }
    public ICommand ImportBoardCsvCommand { get; }
    public ICommand ManualBackupCommand { get; }
    public ICommand PurgeDataCommand { get; }
    public ICommand ResetAllDataCommand { get; }
    public ICommand ResetSettingsOnlyCommand { get; }

    private void LoadFromRepository()
    {
        _working = _repository.Load();
        _snapshot = Clone(_working);
        BindCollections();
        NotifyAll();
    }

    private void BindCollections()
    {
        RefreshDistrictOptions();
        LegalBoards.Clear();
        foreach (var b in _working.Jurisdiction.LegalBoards) LegalBoards.Add(b);
        SchoolZones.Clear();
        foreach (var s in _working.Jurisdiction.SchoolZones) SchoolZones.Add(s);
    }

    private void SyncToWorking()
    {
        _working.Jurisdiction.LegalBoards = LegalBoards.ToList();
        _working.Jurisdiction.SchoolZones = SchoolZones.ToList();
    }

    private void RefreshDistrictOptions()
    {
        DistrictOptions.Clear();
        foreach (var district in GetDistrictsForCity(City))
            DistrictOptions.Add(district);
    }

    private static IEnumerable<string> GetDistrictsForCity(string city) =>
        CityDistrictMap.TryGetValue(city, out var districts) ? districts : CityDistrictMap["인천광역시"];

    private void Save()
    {
        SyncToWorking();
        _repository.Save(_working);
        _snapshot = Clone(_working);
        ShowToast();
        MessageBox.Show("설정이 저장되었습니다.", "저장", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelChanges()
    {
        _working = Clone(_snapshot);
        BindCollections();
        NotifyAll();
    }

    private void ResetCurrentTab(SettingsTab tab)
    {
        if (MessageBox.Show("현재 탭 설정을 기본값으로 되돌리시겠습니까?", "초기화",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        SyncToWorking();
        _working = _repository.ResetTab(tab);
        _snapshot = Clone(_working);
        BindCollections();
        NotifyAll();
    }

    private void ShowToast()
    {
        ShowSaveToast = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { ShowSaveToast = false; timer.Stop(); };
        timer.Start();
    }

    private void AddBoard() =>
        LegalBoards.Add(new LegalBoardItem { Location = "신규 위치", Spec = "3m×1m", ManagementNo = $"LB-{DateTime.Now:yyyyMMdd}" });

    private void DeleteBoard()
    {
        if (SelectedBoard != null) LegalBoards.Remove(SelectedBoard);
    }

    private void AddSchool() =>
        SchoolZones.Add(new SchoolZoneItem { SchoolName = "신규학교", RadiusMeters = 300, Address = District });

    private void ImportBoardCsv()
    {
        var dlg = new OpenFileDialog { Filter = "CSV (*.csv)|*.csv", Title = "합법 게시대 CSV" };
        if (dlg.ShowDialog() != true) return;
        foreach (var line in File.ReadLines(dlg.FileName).Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length >= 3)
                LegalBoards.Add(new LegalBoardItem { Location = parts[0].Trim(), Spec = parts[1].Trim(), ManagementNo = parts[2].Trim() });
        }
        MessageBox.Show("CSV 가져오기 완료", "가져오기", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ManualBackup()
    {
        try
        {
            Directory.CreateDirectory(_working.DataManagement.BackupPath);
            var zip = Path.Combine(_working.DataManagement.BackupPath, $"backup_{DateTime.Now:yyyyMMdd_HHmm}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            var db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "banner_tracking.db");
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
        BindCollections();
        NotifyAll();
    }

    private static SystemSettingsRoot Clone(SystemSettingsRoot src) =>
        System.Text.Json.JsonSerializer.Deserialize<SystemSettingsRoot>(
            System.Text.Json.JsonSerializer.Serialize(src)) ?? SystemSettingsDefaults.Create();

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(YoloThreshold));
        OnPropertyChanged(nameof(ExpectedDetectionsText));
        OnPropertyChanged(nameof(UrgentConfidence));
        OnPropertyChanged(nameof(City));
        OnPropertyChanged(nameof(District));
        OnPropertyChanged(nameof(MapQuery));
        RefreshDistrictOptions();
    }
}