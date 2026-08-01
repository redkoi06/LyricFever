using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricFever.Core.Lyrics;

/// <summary>
/// 歌词元数据匹配（对应 macOS MetadataMatcher）：规范化、标题候选、相关性评分。
/// </summary>
public static partial class MetadataMatcher
{
    /// <summary>大小写/变音符/宽度折叠 + 仅保留字母数字（Unicode）。</summary>
    public static string Normalized(string value)
    {
        var folded = value.Normalize(NormalizationForm.FormKD)
            .ToLowerInvariant();
        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>去括号副标题后的标题候选集（如 "Song (feat. X)" → "Song"）。</summary>
    public static List<string> TitleCandidates(string title)
    {
        var stripped = BracketRegex().Replace(title, " ").Trim();
        var result = new List<string>();
        foreach (var candidate in new[] { title, stripped })
        {
            var trimmed = candidate.Trim();
            if (trimmed.Length > 0 && !result.Contains(trimmed)) result.Add(trimmed);
        }
        return result;
    }

    public static int Relevance(SongResult result, string trackName, string artistName)
    {
        var resultTitle = Normalized(result.SongName);
        var queryTitles = TitleCandidates(trackName).Select(Normalized).Where(t => t.Length > 0).ToList();
        if (resultTitle.Length == 0 || queryTitles.Count == 0) return 0;

        int titleScore;
        if (queryTitles.Contains(resultTitle))
        {
            titleScore = 100;
        }
        else if (queryTitles.Any(qt => ContainsMatch(qt, resultTitle, 4)))
        {
            titleScore = 70;
        }
        else
        {
            return 0;
        }

        var queryArtist = Normalized(artistName);
        var resultArtist = Normalized(result.ArtistName);
        if (queryArtist.Length == 0 || resultArtist.Length == 0) return titleScore;
        if (queryArtist == resultArtist) return titleScore + 30;
        if (queryArtist.Contains(resultArtist) || resultArtist.Contains(queryArtist)) return titleScore + 15;
        return titleScore;
    }

    /// <summary>去重 + 评分过滤 + 降序排序（歌词非空且评分 > 0）。</summary>
    public static List<SongResult> FilteredAndSorted(List<SongResult> results, string trackName, string artistName)
    {
        var seen = new HashSet<string>();
        var scored = new List<(SongResult Result, int Score)>();
        foreach (var result in results)
        {
            if (result.Lyrics.Count == 0) continue;
            var key = string.Join("|",
                result.LyricType, Normalized(result.SongName), Normalized(result.ArtistName), Normalized(result.AlbumName));
            if (!seen.Add(key)) continue;

            var score = Relevance(result, trackName, artistName);
            if (score > 0) scored.Add((result, score));
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Select(s => s.Result).ToList();
    }

    public static bool PlausiblyMatches(string source, string candidate) =>
        ContainsMatch(Normalized(source), Normalized(candidate), 6);

    private static bool ContainsMatch(string source, string candidate, int minimumRatioTenths)
    {
        if (source.Length == 0 || candidate.Length == 0) return false;
        if (source == candidate) return true;

        var shorter = Math.Min(source.Length, candidate.Length);
        var longer = Math.Max(source.Length, candidate.Length);
        return shorter * 10 >= longer * minimumRatioTenths
               && (source.Contains(candidate) || candidate.Contains(source));
    }

    [GeneratedRegex(@"[\s　]*[\(\（\[\【].*?[\)\）\]\】][\s　]*")]
    private static partial Regex BracketRegex();
}
