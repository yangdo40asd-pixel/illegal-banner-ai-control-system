using Microsoft.Extensions.DependencyInjection;
using BannerGuardSystem.ViewModels;
using BannerGuardSystem.Services;

namespace BannerGuardSystem;

public static class AppBootstrapper
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddTransient<EnforcementStatusViewModel>();
        services.AddTransient<StatisticsReportViewModel>();
        services.AddTransient<SystemSettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        // Register Services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<RealTimeMonitoringViewModel>();
        services.AddSingleton<CctvMonitoringViewModel>();

        // Register Core Backend Services
        services.AddSingleton<WebSocketService>();
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<PdfReportService>();
        services.AddSingleton<SystemMonitorService>();
        services.AddSingleton<AlertService>();

        // Register Repositories and Settings
        services.AddSingleton<BannerGuardSystem.Models.AppSettings>();
        services.AddSingleton<ISettingsRepository, JsonSettingsRepository>();
        services.AddSingleton<IStatisticsRepository, MockStatisticsRepository>();
        services.AddSingleton<IStatisticsExcelExportService, StatisticsExcelExportService>();
        
        // Register UI Services
        services.AddSingleton<IDialogService, WpfDialogService>();
        
        return services.BuildServiceProvider();
    }
}
