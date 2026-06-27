namespace BannerGuardSystem.Models;

public enum SettingsTab
{
    AiDetection,
    Notification,
    Jurisdiction,
    Data
}

public enum UserRole
{
    Admin,
    Officer,
    Viewer
}

public enum CctvConnectionStatus
{
    Normal,
    Delayed,
    Disconnected
}

public enum NightAlertMode
{
    ScreenOnly,
    VibrationOnly,
    AllOff
}

public enum BackupCycle
{
    Daily,
    Weekly,
    Monthly
}

public class SystemSettingsRoot
{
    public List<CctvConfigItem> CctvItems { get; set; } = [];
    public AiDetectionSettings AiDetection { get; set; } = new();
    public NotificationSettings Notification { get; set; } = new();
    public JurisdictionSettings Jurisdiction { get; set; } = new();
    public List<UserAccountItem> Users { get; set; } = [];
    public DataManagementSettings DataManagement { get; set; } = new();
    public UserRole CurrentUserRole { get; set; } = UserRole.Admin;
}

public class CctvConfigItem
{
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string Gu { get; set; } = string.Empty;
    public string Dong { get; set; } = string.Empty;
    public string ZoneLabel { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "192.168.1.10";
    public string Resolution { get; set; } = "1080p";
    public CctvConnectionStatus Status { get; set; } = CctvConnectionStatus.Normal;
    public bool AiAnalysisEnabled { get; set; } = true;
    public string VideoPath { get; set; } = string.Empty;

    public string LocationDisplay => string.IsNullOrEmpty(ZoneLabel) ? $"{Dong} {Gu}" : ZoneLabel;
    public string StatusDisplay => Status switch
    {
        CctvConnectionStatus.Normal => "정상",
        CctvConnectionStatus.Delayed => "지연",
        CctvConnectionStatus.Disconnected => "끊김",
        _ => "—"
    };
    public string StatusColorHex => Status switch
    {
        CctvConnectionStatus.Normal => "#22C55E",
        CctvConnectionStatus.Delayed => "#F97316",
        CctvConnectionStatus.Disconnected => "#EF4444",
        _ => "#9CA3AF"
    };
}

public class AiDetectionSettings
{
    public double YoloThresholdPct { get; set; } = 70;
    public string MinDurationFilter { get; set; } = "30분";
    public string UrgentDurationFilter { get; set; } = "1시간";
    public bool NightDoubleEnabled { get; set; } = true;
    public bool VlmEnabled { get; set; } = true;
    public string VlmModel { get; set; } = "고성능 (PaliGemma 2.1)";
    public bool ExcludePolitics { get; set; }
    public bool ExcludeCommercial { get; set; }
    public bool ExcludeReligion { get; set; }
    public bool ExcludeEvent { get; set; }
    public bool ExcludePersonal { get; set; }
    public bool ExcludeCivil { get; set; }
}

public class NotificationSettings
{
    public double UrgentConfidencePct { get; set; } = 85;
    public bool UrgentCommercial { get; set; } = true;
    public bool UrgentSchoolZone { get; set; } = true;
    public bool UrgentDuration1h { get; set; } = true;
    public bool PopupEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public string EmailAddress { get; set; } = "admin@city.go.kr";
    public bool SmsEnabled { get; set; }
    public string SmsNumber { get; set; } = "010-0000-0000";
    public string NightStart { get; set; } = "22:00";
    public string NightEnd { get; set; } = "06:00";
    public NightAlertMode NightMode { get; set; } = NightAlertMode.ScreenOnly;
}

public class LegalBoardItem
{
    public string Location { get; set; } = string.Empty;
    public string Spec { get; set; } = string.Empty;
    public string ManagementNo { get; set; } = string.Empty;
}

public class SchoolZoneItem
{
    public string SchoolName { get; set; } = string.Empty;
    public int RadiusMeters { get; set; } = 300;
    public string Address { get; set; } = string.Empty;
}

public class JurisdictionSettings
{
    public string City { get; set; } = "인천광역시";
    public string District { get; set; } = "연수구";
    public List<LegalBoardItem> LegalBoards { get; set; } = [];
    public List<SchoolZoneItem> SchoolZones { get; set; } = [];
}

public class UserAccountItem
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Rank { get; set; } = "주무관";
    public UserRole Role { get; set; } = UserRole.Officer;
    public DateTime LastLogin { get; set; } = DateTime.Now.AddDays(-1);
    public bool IsActive { get; set; } = true;

    public string RoleDisplay => Role switch
    {
        UserRole.Admin => "관리자",
        UserRole.Officer => "담당자",
        UserRole.Viewer => "조회만",
        _ => Role.ToString()
    };
    public string StatusDisplay => IsActive ? "활성" : "비활성";
    public string LastLoginText => LastLogin.ToString("yyyy-MM-dd HH:mm");
}

public class DataManagementSettings
{
    public BackupCycle AutoBackupCycle { get; set; } = BackupCycle.Daily;
    public string BackupTime { get; set; } = "02:00";
    public string BackupPath { get; set; } = @"C:\BannerGuard\Backup";
    public string DetectionLogRetention { get; set; } = "1년";
    public string ReportRetention { get; set; } = "3년";
    public string RecordingRetention { get; set; } = "14일";
    public bool ExportBeforeDelete { get; set; } = true;
}

public static class SystemSettingsDefaults
{
    public static SystemSettingsRoot Create()
    {
        return new SystemSettingsRoot
        {
            CctvItems =
            [
                new() { ChannelId = "CCTV_01", ChannelName = "CCTV-01", ZoneLabel = "역삼동 A구역", Dong = "역삼동", Gu = "강남구", IpAddress = "192.168.1.101", Resolution = "1080p", Status = CctvConnectionStatus.Normal, AiAnalysisEnabled = true },
                new() { ChannelId = "CCTV_02", ChannelName = "CCTV-02", ZoneLabel = "역삼동 B구역", Dong = "역삼동", Gu = "강남구", IpAddress = "192.168.1.102", Resolution = "1080p", Status = CctvConnectionStatus.Normal, AiAnalysisEnabled = true },
                new() { ChannelId = "CCTV_03", ChannelName = "CCTV-03", ZoneLabel = "서초동 C구역", Dong = "서초동", Gu = "서초구", IpAddress = "192.168.1.103", Resolution = "720p", Status = CctvConnectionStatus.Delayed, AiAnalysisEnabled = true },
                new() { ChannelId = "CCTV_04", ChannelName = "CCTV-04", ZoneLabel = "잠실동 D구역", Dong = "잠실동", Gu = "송파구", IpAddress = "192.168.1.104", Resolution = "1080p", Status = CctvConnectionStatus.Disconnected, AiAnalysisEnabled = false }
            ],
            AiDetection = new AiDetectionSettings(),
            Notification = new NotificationSettings(),
            Jurisdiction = new JurisdictionSettings
            {
                City = "인천광역시",
                District = "연수구",
                LegalBoards =
                [
                    new() { Location = "송도동 센트럴로", Spec = "3m×1.2m", ManagementNo = "LB-2024-001" },
                    new() { Location = "연수동 인천대공원 앞", Spec = "2.5m×1m", ManagementNo = "LB-2024-002" }
                ],
                SchoolZones =
                [
                    new() { SchoolName = "송도초등학교", RadiusMeters = 300, Address = "연수구 송도동" },
                    new() { SchoolName = "남동중학교", RadiusMeters = 300, Address = "남동구 구월동" }
                ]
            },
            Users =
            [
                new() { UserId = "admin_kim", Name = "김무주", Rank = "주무관", Role = UserRole.Admin, LastLogin = DateTime.Now.AddHours(-2), IsActive = true },
                new() { UserId = "officer_lee", Name = "이주희", Rank = "계장", Role = UserRole.Officer, LastLogin = DateTime.Now.AddDays(-1), IsActive = true },
                new() { UserId = "viewer_park", Name = "박조회", Rank = "주무관", Role = UserRole.Viewer, LastLogin = DateTime.Now.AddDays(-3), IsActive = true }
            ],
            DataManagement = new DataManagementSettings(),
            CurrentUserRole = UserRole.Admin
        };
    }
}