using System;

namespace BannerGuardSystem.Services;

public static class RecurrenceAnalyzer
{
    public static double CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            return 0.0;

        source = source.Trim().ToLower();
        target = target.Trim().ToLower();

        if (source == target) return 1.0;

        int stepsToConvert = ComputeLevenshteinDistance(source, target);
        return 1.0 - ((double)stepsToConvert / Math.Max(source.Length, target.Length));
    }

    private static int ComputeLevenshteinDistance(string source, string target)
    {
        int n = source.Length;
        int m = target.Length;
        int[,] d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}
