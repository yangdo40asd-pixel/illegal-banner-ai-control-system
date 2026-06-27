using System.IO;
using System.Text.Json;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public interface ISettingsRepository
{
    SystemSettingsRoot Load();
    void Save(SystemSettingsRoot settings);
    SystemSettingsRoot Reset();
    SystemSettingsRoot ResetTab(SettingsTab tab);
}

public class JsonSettingsRepository : ISettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public JsonSettingsRepository()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BannerGuard");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "appsettings.local.json");
    }

    public SystemSettingsRoot Load()
    {
        if (!File.Exists(_path))
            return SystemSettingsDefaults.Create();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<SystemSettingsRoot>(json, JsonOptions)
                   ?? SystemSettingsDefaults.Create();
        }
        catch
        {
            return SystemSettingsDefaults.Create();
        }
    }

    public void Save(SystemSettingsRoot settings) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));

    public SystemSettingsRoot Reset()
    {
        var defaults = SystemSettingsDefaults.Create();
        Save(defaults);
        return defaults;
    }

    public SystemSettingsRoot ResetTab(SettingsTab tab)
    {
        var current = Load();
        var defaults = SystemSettingsDefaults.Create();
        switch (tab)
        {
            case SettingsTab.AiDetection: current.AiDetection = defaults.AiDetection; break;
            case SettingsTab.Notification: current.Notification = defaults.Notification; break;
            case SettingsTab.Jurisdiction: current.Jurisdiction = defaults.Jurisdiction; break;
            case SettingsTab.Data: current.DataManagement = defaults.DataManagement; break;
        }
        Save(current);
        return current;
    }
}