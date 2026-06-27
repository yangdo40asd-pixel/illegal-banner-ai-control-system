using BannerGuardSystem.Models;

namespace BannerGuardSystem.Services;

internal static class EnforcementDataMapper
{
    public static string ResolveGu(string cctvId) => cctvId switch
    {
        "CCTV_01" or "CCTV_02" => "부평구",
        "CCTV_03" or "CCTV_04" => "남동구",
        _ when cctvId.StartsWith("VLM_") => "검증구역",
        _ => "미분류"
    };

    public static string ResolveDong(string cctvId) => cctvId switch
    {
        "CCTV_01" => "삼산동",
        "CCTV_02" => "삼산동",
        "CCTV_03" => "구월동",
        "CCTV_04" => "구월동",
        _ when cctvId.StartsWith("VLM_") => "VLM검증",
        _ => "—"
    };

    public static string ResolveLocation(string cctvId, string? sourceImage, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (!string.IsNullOrWhiteSpace(sourceImage))
            return $"VLM 검증 ({sourceImage})";
        return $"{ResolveGu(cctvId)} {ResolveDong(cctvId)}";
    }

    public static EnforcementProcessingStatus ParseProcessingStatus(string? value) =>
        value?.Trim() switch
        {
            "FineImposed" => EnforcementProcessingStatus.FineImposed,
            "Appeal" => EnforcementProcessingStatus.Appeal,
            "Received" => EnforcementProcessingStatus.Received,
            "Delinquent" => EnforcementProcessingStatus.Delinquent,
            "Urgent" => EnforcementProcessingStatus.Urgent,
            _ => EnforcementProcessingStatus.Urgent
        };

    public static string ToDbStatus(EnforcementProcessingStatus status) => status.ToString();

    public static ContentCategory MapStatusToCategory(string? aiStatusCode, string? ocrText)
    {
        var code = aiStatusCode ?? string.Empty;
        var text = ocrText ?? string.Empty;

        if (code is "LEGAL_COMMERCIAL" or "ILLEGAL_COMMERCIAL")
            return ContentCategory.Commercial;
        if (code is "LEGAL_POLITICS" or "EXEMPT_ELECTION")
            return ContentCategory.Politics;
        if (code is "LEGAL_PUBLIC")
            return ContentCategory.Civil;

        if (text.Contains("후보") || text.Contains("선거") || text.Contains("교육감") || text.Contains("정당"))
            return ContentCategory.Politics;
        if (text.Contains("분양") || text.Contains("임대") || text.Contains("학원") || text.Contains("할인") || text.Contains("오픈"))
            return ContentCategory.Commercial;
        if (text.Contains("축제") || text.Contains("행사") || text.Contains("모집"))
            return ContentCategory.Event;
        if (text.Contains("교회") || text.Contains("성당") || text.Contains("절"))
            return ContentCategory.Religion;

        return ContentCategory.Personal;
    }

    public static int DefaultFineManwon(string? aiStatusCode) =>
        aiStatusCode is "ILLEGAL_CONTENT" or "ILLEGAL_COMMERCIAL" ? 300 : 200;
}