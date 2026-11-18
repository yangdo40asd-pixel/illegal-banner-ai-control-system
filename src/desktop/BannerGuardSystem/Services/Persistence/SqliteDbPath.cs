using System.IO;

namespace BannerGuardSystem.Services;

internal static class SqliteDbPath
{
    public static string ResolveBannerTrackingDb()
    {
        var path = BannerGuardSystem.Helpers.RuntimePaths.Resolve("DatabasePath", "runtime/db/banner_tracking.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
