using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerGuardSystem.Services;

internal static class BannerSimilarityHelper
{
    public static string ToOneLineSummary(string? text, int maxLen = 48)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(텍스트 미인식)";

        var flat = string.Join(" ", text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (flat.Length <= maxLen)
            return flat;
        return flat[..(maxLen - 1)] + "…";
    }

    public static int SimilarityPercent(string? a, string? b)
    {
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);
        if (tokensA.Count == 0 || tokensB.Count == 0)
            return 0;

        var intersection = tokensA.Intersect(tokensB).Count();
        var union = tokensA.Union(tokensB).Count();
        if (union == 0)
            return 0;

        return (int)Math.Round(intersection * 100.0 / union);
    }

    private static HashSet<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', ',', '.', '·', '|', '/', '(', ')', '[', ']', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .ToHashSet();
    }
}