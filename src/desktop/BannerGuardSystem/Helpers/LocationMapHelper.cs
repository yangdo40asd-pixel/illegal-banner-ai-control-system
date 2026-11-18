using System;
using System.IO;
using BannerGuardSystem.Models;

namespace BannerGuardSystem.Helpers;

/// <summary>CCTV/VLM 탐지 건 → Google Maps 검색어(구·동) 변환.</summary>
public static class LocationMapHelper
{
    private static readonly (string Key, string MapQuery)[] KeywordMap =
    [
        ("인천광역시 부평구 삼산동", "인천광역시 부평구 삼산동"),
        ("부평구 삼산동", "인천광역시 부평구 삼산동"),
        ("삼산동", "인천광역시 부평구 삼산동"),
        ("인천광역시 남동구 구월동", "인천광역시 남동구 구월동"),
        ("남동구 구월동", "인천광역시 남동구 구월동"),
        ("구월동", "인천광역시 남동구 구월동"),
        ("인천광역시 미추홀구 용현동", "인천광역시 미추홀구 용현동"),
        ("미추홀구 용현동", "인천광역시 미추홀구 용현동"),
        ("용현동", "인천광역시 미추홀구 용현동"),
        ("인천광역시 미추홀구 주안동", "인천광역시 미추홀구 주안동"),
        ("주안동", "인천광역시 미추홀구 주안동"),
        ("인천광역시 연수구 송도동", "인천광역시 연수구 송도동"),
        ("송도동", "인천광역시 연수구 송도동"),
    ];

    public static string ResolveMapQuery(BannerDetectionEventDto? alert)
    {
        if (alert == null)
            return "인천광역시";

        // 1순위: 명시적으로 저장된 주소 (가장 정확함)
        var fromAddress = NormalizeAddress(alert.LocationAddress);
        if (fromAddress != null)
            return fromAddress;

        // 2순위: VLM 텍스트에서 추출
        var fromText = ExtractFromText(alert.VlmAnalysis, alert.OcrText);
        if (fromText != null)
            return fromText;

        // 3순위: 영상 소스 파일 경로 매칭
        var fromPoster = ResolveFromPosterFile(alert.SourceImage);
        if (fromPoster != null)
            return fromPoster;

        // 4순위: 기본 CCTV 위치 (최후의 보루)
        var fromCctv = ResolveFromCctvId(alert.CctvId);
        if (fromCctv != null)
            return fromCctv;

        return "인천광역시";
    }

    public static string ResolveDisplayAddress(BannerDetectionEventDto alert)
        => ResolveMapQuery(alert);

    private static string? ResolveFromPosterFile(string? sourceImage)
    {
        if (string.IsNullOrWhiteSpace(sourceImage))
            return null;

        var baseName = Path.GetFileName(sourceImage);
        foreach (var seed in CctvMediaCatalog.DefaultChannels)
        {
            var posterName = Path.GetFileName(seed.PosterAssetsFile);
            if (string.Equals(posterName, baseName, StringComparison.OrdinalIgnoreCase))
                return NormalizeAddress(seed.Address) ?? seed.Address;
        }

        return null;
    }

    private static string? ResolveFromCctvId(string? cctvId) => cctvId switch
    {
        "CCTV_01" or "CCTV_02" => "인천광역시 부평구 삼산동",
        "CCTV_03" or "CCTV_04" => "인천광역시 남동구 구월동",
        _ => null
    };

    private static string? NormalizeAddress(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.StartsWith("VLM 검증", StringComparison.OrdinalIgnoreCase))
            return null;

        var text = raw.Trim();
        foreach (var (key, mapQuery) in KeywordMap)
        {
            if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                return mapQuery;
        }

        if (text.Contains('구') || text.Contains('동'))
        {
            if (text.StartsWith("인천 ", StringComparison.Ordinal))
                text = "인천광역시" + text[2..];
            else if (!text.StartsWith("인천광역시", StringComparison.Ordinal))
                text = "인천광역시 " + text;

            return text;
        }

        return null;
    }

    private static string? ExtractFromText(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var (key, mapQuery) in KeywordMap)
            {
                if (text.Contains(key, StringComparison.OrdinalIgnoreCase))
                    return mapQuery;
            }
        }

        return null;
    }
}