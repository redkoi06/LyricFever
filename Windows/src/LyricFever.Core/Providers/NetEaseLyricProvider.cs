using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Providers;

/// <summary>
/// 网易云音乐歌词源。使用 music.163.com 官方域名的网页接口，并在曲名、歌手、专辑
/// 匹配可靠时提供成对的时间轴原文与人工译词。
/// </summary>
public sealed class NetEaseLyricProvider : ILyricProvider, IHumanTranslationProvider
{
    private sealed record CachedBundle(NetEaseSong Song, List<LyricLine> Lyrics,
        List<LyricLine> TranslatedLyrics, DateTimeOffset ExpiresAt);

    public string ProviderName => "NetEase Lyric Provider";

    private const string Host = "https://music.163.com";
    private static readonly HttpClient Http = CreateClient();
    private readonly ConcurrentDictionary<string, CachedBundle> _bundleCache = new();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/127 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri($"{Host}/");
        return client;
    }

    public async Task<NetworkFetchReturn> FetchNetworkLyricsAsync(
        string trackName, string trackId, string? artistName, string? albumName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return NetworkFetchReturn.Empty;

        var bundle = await FetchBundleAsync(trackName, artistName, albumName, cancellationToken);
        if (bundle == null || !HasUsableLyrics(bundle.Lyrics)) return NetworkFetchReturn.Empty;

        return new NetworkFetchReturn { Lyrics = bundle.Lyrics };
    }

    public async Task<IReadOnlyList<string>?> FetchTranslationAsync(
        string trackName, string? artistName, string? albumName,
        IReadOnlyList<LyricLine> referenceLyrics,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName) || referenceLyrics.Count == 0) return null;

        var bundle = await FetchBundleAsync(trackName, artistName, albumName, cancellationToken);
        if (bundle == null || bundle.TranslatedLyrics.Count == 0) return null;

        var aligned = AlignTranslations(referenceLyrics, bundle.Lyrics, bundle.TranslatedLyrics);
        return HasSufficientCoverage(referenceLyrics, aligned) ? aligned : null;
    }

    public async Task<List<SongResult>> SearchAsync(
        string trackName, string artistName, CancellationToken cancellationToken = default)
    {
        var songs = await SearchSongsAsync(trackName, artistName, 8, cancellationToken);
        var results = new List<SongResult>();
        foreach (var song in songs.Take(5))
        {
            try
            {
                var lyricsData = await FetchLyricsDataAsync(song.Id, cancellationToken);
                var parsed = ParseTimedLyrics(lyricsData?.Lrc?.Lyric);
                if (!HasUsableLyrics(parsed)) continue;

                results.Add(new SongResult
                {
                    LyricType = "NetEase",
                    SongName = song.Name,
                    AlbumName = song.Album.Name,
                    ArtistName = song.Artists.FirstOrDefault()?.Name ?? "",
                    Lyrics = parsed
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 单条失败不阻断搜索结果。
            }
        }
        return results;
    }

    private async Task<CachedBundle?> FetchBundleAsync(
        string trackName, string artistName, string? albumName, CancellationToken cancellationToken)
    {
        var cacheKey = $"{NormalizeMetadata(trackName)}|{NormalizeMetadata(artistName)}|{NormalizeMetadata(albumName)}";
        if (_bundleCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached;

        var songs = await SearchSongsAsync(trackName, artistName, 8, cancellationToken);
        var song = SelectBestSong(songs, trackName, artistName, albumName);
        if (song == null) return null;

        var lyricsData = await FetchLyricsDataAsync(song.Id, cancellationToken);
        var lyrics = ParseTimedLyrics(lyricsData?.Lrc?.Lyric);
        if (!HasUsableLyrics(lyrics)) return null;

        var translated = ParseTimedLyrics(lyricsData?.Tlyric?.Lyric);
        var bundle = new CachedBundle(song, lyrics, translated, DateTimeOffset.UtcNow.AddMinutes(10));
        _bundleCache[cacheKey] = bundle;
        return bundle;
    }

    private static async Task<List<NetEaseSong>> SearchSongsAsync(
        string trackName, string artistName, int limit, CancellationToken cancellationToken)
    {
        var simplifiedArtist = SimplifyArtist(artistName);
        var query = string.IsNullOrWhiteSpace(simplifiedArtist)
            ? trackName
            : $"{trackName} {simplifiedArtist}";
        var url = $"{Host}/api/search/get/web?s={Uri.EscapeDataString(query)}&type=1&limit={limit}&offset=0";
        var search = await Http.GetFromJsonAsync<NetEaseSearch>(url, cancellationToken);
        return search?.Result?.Songs ?? new List<NetEaseSong>();
    }

    private static Task<NetEaseLyrics?> FetchLyricsDataAsync(int songId, CancellationToken cancellationToken) =>
        Http.GetFromJsonAsync<NetEaseLyrics>(
            $"{Host}/api/song/lyric?id={songId}&lv=1&kv=1&tv=1", cancellationToken);

    internal static NetEaseSong? SelectBestSong(IEnumerable<NetEaseSong> songs,
        string trackName, string artistName, string? albumName)
    {
        NetEaseSong? best = null;
        var bestScore = double.MinValue;
        foreach (var song in songs)
        {
            var titleScore = FlexibleSimilarity(trackName, song.Name);
            var artistScore = song.Artists.Count == 0
                ? 0
                : song.Artists.Max(artist => FlexibleSimilarity(artistName, artist.Name));
            var albumScore = string.IsNullOrWhiteSpace(albumName)
                ? 1
                : FlexibleSimilarity(albumName, song.Album.Name);

            // 曲名必须可靠；歌手或专辑至少一项也必须吻合，防止同名歌曲串台。
            if (titleScore < 0.78 || Math.Max(artistScore, albumScore) < 0.72) continue;
            var score = titleScore * 0.55 + artistScore * 0.35 + albumScore * 0.10;
            if (score <= bestScore) continue;
            bestScore = score;
            best = song;
        }
        return best;
    }

    /// <summary>
    /// 将平台译词按时间轴对齐到当前歌词。不同来源可能把一句拆成两行，因此同一参考区间内
    /// 的多条译文会合并，而不是按数组下标硬配。
    /// </summary>
    internal static List<string> AlignTranslations(IReadOnlyList<LyricLine> referenceLyrics,
        IReadOnlyList<LyricLine> sourceLyrics, IReadOnlyList<LyricLine> translatedLyrics)
    {
        var result = Enumerable.Repeat("", referenceLyrics.Count).ToList();
        if (referenceLyrics.Count == 0 || translatedLyrics.Count == 0) return result;

        var offset = EstimateTimelineOffset(referenceLyrics, sourceLyrics);
        var referenceTimes = referenceLyrics.Select(line => line.StartTimeInMs).ToArray();
        if (referenceTimes.Any(time => double.IsNaN(time) || double.IsInfinity(time))) return result;

        foreach (var translated in translatedLyrics)
        {
            var text = translated.Words.Trim();
            var time = translated.StartTimeInMs + offset;
            if (text.Length == 0 || double.IsNaN(time) || time < referenceTimes[0] - 1500) continue;

            var index = Array.BinarySearch(referenceTimes, time + 250);
            if (index < 0) index = ~index - 1;
            if (index < 0 || index >= result.Count) continue;

            result[index] = result[index].Length == 0 ? text : $"{result[index]} {text}";
        }
        return result;
    }

    internal static bool HasSufficientCoverage(IReadOnlyList<LyricLine> referenceLyrics,
        IReadOnlyList<string> translatedLyrics)
    {
        var referenceContentCount = referenceLyrics.Count(line => !string.IsNullOrWhiteSpace(line.Words));
        var translatedCount = translatedLyrics.Count(text => !string.IsNullOrWhiteSpace(text));
        var minimumCoverage = Math.Max(3, (int)Math.Ceiling(referenceContentCount * 0.60));
        return translatedCount >= minimumCoverage;
    }

    private static double EstimateTimelineOffset(IReadOnlyList<LyricLine> referenceLyrics,
        IReadOnlyList<LyricLine> sourceLyrics)
    {
        var offsets = new List<double>();
        foreach (var reference in referenceLyrics)
        {
            var referenceText = NormalizeLyric(reference.Words);
            if (referenceText.Length < 4 || double.IsNaN(reference.StartTimeInMs)) continue;

            foreach (var source in sourceLyrics)
            {
                var sourceText = NormalizeLyric(source.Words);
                if (sourceText.Length < 4 || double.IsNaN(source.StartTimeInMs)) continue;
                var shorter = Math.Min(referenceText.Length, sourceText.Length);
                var related = referenceText == sourceText ||
                              (shorter >= 5 && (referenceText.Contains(sourceText, StringComparison.Ordinal) ||
                                                sourceText.Contains(referenceText, StringComparison.Ordinal)));
                if (!related) continue;

                var candidate = reference.StartTimeInMs - source.StartTimeInMs;
                if (Math.Abs(candidate) <= 15_000) offsets.Add(candidate);
            }
        }

        if (offsets.Count == 0) return 0;
        offsets.Sort();
        var middle = offsets.Count / 2;
        return offsets.Count % 2 == 1
            ? offsets[middle]
            : (offsets[middle - 1] + offsets[middle]) / 2;
    }

    private static List<LyricLine> ParseTimedLyrics(string? lrcText)
    {
        if (string.IsNullOrWhiteSpace(lrcText)) return new List<LyricLine>();
        return new LyricsParser(UnescapeHtmlEntities(lrcText)).Lyrics
            .Where(line => !double.IsNaN(line.StartTimeInMs) && line.StartTimeInMs > 0)
            .ToList();
    }

    private static bool HasUsableLyrics(IReadOnlyList<LyricLine> lyrics) =>
        lyrics.Count > 1 && lyrics.Any(line => line.StartTimeInMs > 0 && !string.IsNullOrWhiteSpace(line.Words));

    private static string SimplifyArtist(string value)
    {
        var cut = value.IndexOfAny(['[', '(', '（']);
        return (cut > 0 ? value[..cut] : value).Trim();
    }

    private static double FlexibleSimilarity(string? source, string? target)
    {
        var a = NormalizeMetadata(source);
        var b = NormalizeMetadata(target);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        if (Math.Min(a.Length, b.Length) >= 3 &&
            (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
            return 0.92;
        return StringMetric.Distance(a, b);
    }

    private static string NormalizeMetadata(string? value) => Normalize(value, keepWordSymbols: false);
    private static string NormalizeLyric(string? value) => Normalize(value, keepWordSymbols: true);

    private static string Normalize(string? value, bool keepWordSymbols)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var builder = new StringBuilder();
        foreach (var ch in value.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || (keepWordSymbols && (ch == '☆' || ch == '★')))
                builder.Append(ch);
        }
        return builder.ToString();
    }

    internal static string UnescapeHtmlEntities(string text) => WebUtility.HtmlDecode(text)
        .Replace("\\\n", "\n");
}

public sealed class NetEaseSearch
{
    [JsonPropertyName("result")]
    public NetEaseResult? Result { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    public sealed class NetEaseResult
    {
        [JsonPropertyName("songs")]
        public List<NetEaseSong>? Songs { get; set; }

        [JsonPropertyName("songCount")]
        public int SongCount { get; set; }
    }
}

public sealed class NetEaseSong
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("album")]
    public NetEaseAlbum Album { get; set; } = new();

    [JsonPropertyName("artists")]
    public List<NetEaseArtist> Artists { get; set; } = new();
}

public sealed class NetEaseAlbum
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class NetEaseArtist
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class NetEaseLyrics
{
    [JsonPropertyName("lrc")]
    public NetEaseLyric? Lrc { get; set; }

    [JsonPropertyName("klyric")]
    public NetEaseLyric? Klyric { get; set; }

    [JsonPropertyName("tlyric")]
    public NetEaseLyric? Tlyric { get; set; }

    public sealed class NetEaseLyric
    {
        [JsonPropertyName("lyric")]
        public string? Lyric { get; set; }
    }
}
