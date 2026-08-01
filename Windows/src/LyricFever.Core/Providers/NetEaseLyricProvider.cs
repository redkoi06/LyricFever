using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Providers;

/// <summary>
/// NetEase 歌词源（对应 macOS NetEaseLyricProvider）：代理 API + Safari UA 伪装。
/// 需 track/artist/album 至少 2 项 75% 相似才接受结果；末行 0ms 视为无歌词。
/// </summary>
public sealed class NetEaseLyricProvider : ILyricProvider
{
    public string ProviderName => "NetEase Lyric Provider";

    private const string Host = "neteasecloudmusicapi-ten-wine.vercel.app";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.3 Safari/605.1.15");
        return client;
    }

    public async Task<NetworkFetchReturn> FetchNetworkLyricsAsync(
        string trackName, string trackId, string? artistName, string? albumName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(artistName) || string.IsNullOrEmpty(albumName))
            return NetworkFetchReturn.Empty;

        var search = await Http.GetFromJsonAsync<NetEaseSearch>(
            $"https://{Host}/search?keywords={Uri.EscapeDataString($"{trackName} {artistName}")}&limit=1",
            cancellationToken);
        var song = search?.Result?.Songs?.FirstOrDefault();
        var artist = song?.Artists?.FirstOrDefault();
        if (song == null || artist == null) return NetworkFetchReturn.Empty;

        var neteaseId = song.Id;
        var conditions = new[]
        {
            StringMetric.Distance(trackName, song.Name) > 0.75,
            StringMetric.Distance(artistName, artist.Name) > 0.75,
            StringMetric.Distance(albumName, song.Album.Name) > 0.75
        };
        if (conditions.Count(c => c) < 2) return NetworkFetchReturn.Empty;

        var lyricsData = await Http.GetFromJsonAsync<NetEaseLyrics>(
            $"https://{Host}/lyric?id={neteaseId}", cancellationToken);
        var lrcText = lyricsData?.Lrc?.Lyric;
        if (string.IsNullOrEmpty(lrcText)) return NetworkFetchReturn.Empty;

        var cleaned = UnescapeHtmlEntities(lrcText);
        var parsed = new LyricsParser(cleaned).Lyrics;
        // NetEase 对仅有曲名/歌手/作曲的无歌词歌曲返回 0.0 时间戳行，需过滤
        if (parsed.Count > 0 && parsed[^1].StartTimeInMs == 0.0) return NetworkFetchReturn.Empty;

        return new NetworkFetchReturn { Lyrics = parsed };
    }

    public async Task<List<SongResult>> SearchAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
    {
        var search = await Http.GetFromJsonAsync<NetEaseSearch>(
            $"https://{Host}/search?keywords={Uri.EscapeDataString($"{trackName} {artistName}")}&limit=5",
            cancellationToken);
        if (search?.Result?.Songs == null) return new List<SongResult>();

        var results = new List<SongResult>();
        foreach (var song in search.Result.Songs)
        {
            var firstArtist = song.Artists.FirstOrDefault();
            if (firstArtist == null) continue;

            try
            {
                var lyricsData = await Http.GetFromJsonAsync<NetEaseLyrics>(
                    $"https://{Host}/lyric?id={song.Id}", cancellationToken);
                var lrcText = lyricsData?.Lrc?.Lyric;
                if (string.IsNullOrEmpty(lrcText)) continue;

                var cleaned = UnescapeHtmlEntities(lrcText);
                var parsed = new LyricsParser(cleaned).Lyrics;
                if (parsed.Count > 0 && parsed[^1].StartTimeInMs == 0.0) continue;

                results.Add(new SongResult
                {
                    LyricType = "NetEase",
                    SongName = song.Name,
                    AlbumName = song.Album.Name,
                    ArtistName = firstArtist.Name,
                    Lyrics = parsed
                });
            }
            catch
            {
                // 单条失败忽略，继续
            }
        }
        return results;
    }

    /// <summary>HTML 实体反转义（对应 macOS unescapeHTMLEntities）。</summary>
    internal static string UnescapeHtmlEntities(string text)
    {
        var s = text
            .Replace("&apos;", "'")
            .Replace("&quot;", "\"")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&#39;", "'")
            .Replace("&#x27;", "'")
            .Replace("\\\n", "\n");
        return s;
    }
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
