using System.Collections.Generic;
using System.IO;

namespace BannerGuardSystem.Helpers;

/// <summary>
/// CCTV 데모 MP4·포스터 경로 (WPF 폴더 기준).
/// </summary>
public static class CctvMediaCatalog
{
    public const string MediaRoot = @"D:\138.종합 민원 이미지 AI데이터\WPF";

    public static string ResolveVideo(string fileName)
        => Path.GetFullPath(Path.Combine(MediaRoot, fileName));

    public static string ResolvePosterInAssets(string assetsFileName)
        => assetsFileName;

    public sealed record ChannelSeed(
        string ChannelId,
        string Name,
        string ZoneLabel,
        string VideoFileName,
        string Address,
        string PosterAssetsFile);

    public static IReadOnlyList<ChannelSeed> DefaultChannels { get; } =
    [
        new("CCTV_01", "CCTV_01 삼산동 A구역", "A", "20260525_133553.mp4", "인천 부평구 삼산동 12", "Assets/20260525_133548.jpg"),
        new("CCTV_02", "CCTV_02 삼산동 B구역", "B", "20260525_133611.mp4", "인천 부평구 삼산동 45", "Assets/20260525_133609.jpg"),
        new("CCTV_03", "CCTV_03 구월동 A구역", "A", "20260525_133838.mp4", "인천 남동구 구월동 78", "Assets/20260525_133852.jpg"),
        new("CCTV_04", "CCTV_04 구월동 B구역", "B", "20260525_133919.mp4", "인천 남동구 구월동 10", "Assets/20260525_133916.jpg"),
    ];

    public static bool VideoExists(string fileName)
        => File.Exists(ResolveVideo(fileName));
}