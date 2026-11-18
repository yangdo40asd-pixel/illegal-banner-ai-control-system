using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

public interface ISettingsRepository
{
    SystemSettingsRoot Load();
    void Save(SystemSettingsRoot settings);
    SystemSettingsRoot Reset();
    SystemSettingsRoot ResetTab(SettingsTab tab);
}
