namespace LyricFever.Core.Providers;

/// <summary>
/// 规范化 Levenshtein 相似度（对应 autozimu/StringMetric.swift）。
/// distance = 1 - editDistance / maxLen，范围 [0, 1]，1 表示完全相同。
/// </summary>
public static class StringMetric
{
    public static double Distance(string source, string target)
    {
        if (source == target) return 1.0;
        if (source.Length == 0 || target.Length == 0) return 0.0;

        var maxLen = Math.Max(source.Length, target.Length);
        var distance = Levenshtein(source, target);
        return 1.0 - (double)distance / maxLen;
    }

    private static int Levenshtein(string source, string target)
    {
        var n = source.Length;
        var m = target.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        // 两行滚动数组：仅当字符相同时复用上一行结果（字符级，非码点级——与 Swift 版一致）
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++) prev[j] = j;

        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }
}
