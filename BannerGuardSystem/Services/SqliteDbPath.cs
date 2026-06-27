using System.IO;

namespace BannerGuardSystem.Services;

internal static class SqliteDbPath
{
    public static string ResolveBannerTrackingDb()
    {
        const string defaultPath = @"D:\138.종합 민원 이미지 AI데이터\inference_test\banner_tracking.db";
        string? dir = Path.GetDirectoryName(defaultPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            return defaultPath;

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "banner_tracking.db");
    }
}