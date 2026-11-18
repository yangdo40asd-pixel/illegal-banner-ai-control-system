namespace BannerGuardSystem.Models;

public enum ContentCategory
{
    Commercial,
    Politics,
    Event,
    Personal,
    Religion,
    Civil
}

public static class ContentCategoryExtensions
{
    public static string ToDisplayName(this ContentCategory category) => category switch
    {
        ContentCategory.Commercial => "상업/부동산",
        ContentCategory.Politics => "정치/선거",
        ContentCategory.Event => "행사/모임",
        ContentCategory.Personal => "개인/사업",
        ContentCategory.Religion => "종교",
        ContentCategory.Civil => "민원/항의",
        _ => category.ToString()
    };

    public static string ToColorHex(this ContentCategory category) => category switch
    {
        ContentCategory.Commercial => "#DC2626",
        ContentCategory.Politics => "#EA580C",
        ContentCategory.Event => "#EAB308",
        ContentCategory.Personal => "#9CA3AF",
        ContentCategory.Religion => "#7C3AED",
        ContentCategory.Civil => "#2563EB",
        _ => "#6B7280"
    };
}