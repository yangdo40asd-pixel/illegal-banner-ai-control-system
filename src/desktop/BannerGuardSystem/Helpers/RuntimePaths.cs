using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BannerGuardSystem.Helpers;

/// <summary>Shared path contract: environment, local JSON, example JSON. Never resolve against cwd.</summary>
public static class RuntimePaths
{
    private static readonly Lazy<string> RootValue = new(FindRoot);
    public static string Root => RootValue.Value;
    public static string ConfigurationFile => Environment.GetEnvironmentVariable("BANNERGUARD_CONFIG")
        ?? (File.Exists(Path.Combine(Root, "configs", "paths.local.json"))
            ? Path.Combine(Root, "configs", "paths.local.json")
            : Path.Combine(Root, "configs", "paths.example.json"));

    private static string FindRoot()
    {
        var configured = Environment.GetEnvironmentVariable("BANNERGUARD_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        string? nearestConfigurationRoot = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".bannerguard-root"))) return directory.FullName;
            if (nearestConfigurationRoot == null && File.Exists(Path.Combine(directory.FullName, "configs", "paths.example.json"))) nearestConfigurationRoot = directory.FullName;
        }
        return nearestConfigurationRoot ?? AppContext.BaseDirectory;
    }

    public static string Get(string key, string fallback = "")
    {
        var variable = "BANNERGUARD_" + Regex.Replace(key, "([a-z0-9])([A-Z])", "$1_$2").ToUpperInvariant();
        var value = Environment.GetEnvironmentVariable(variable);
        if (value != null) return value;
        string result = fallback;
        foreach (var file in new[] { Path.Combine(Root, "configs", "paths.example.json"), ConfigurationFile }.Distinct())
        {
            if (!File.Exists(file)) continue;
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.TryGetProperty(key, out var item)) result = item.GetString() ?? fallback;
        }
        return result;
    }

    public static string Resolve(string key, string fallback = "")
    {
        var path = Environment.ExpandEnvironmentVariables(Get(key, fallback));
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Root, path));
    }

    public static string RuntimeFile(string category, string fileName)
    {
        var directory = Path.Combine(Resolve("RuntimeDirectory", "runtime"), category);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
