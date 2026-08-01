using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Providers;

/// <summary>
/// LRCLIB 歌词源（对应 macOS LRCLIBLyricProvider）：
/// 优先 /api/get 精确匹配（有专辑名时），失败回退 /api/search。
/// </summary>
public sealed class LrclibLyricProvider : ILyricProvider
{
    public string ProviderName => "LRCLIB Lyric Provider";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lyric Fever v3.3");
        return client;
    }

    public async Task<NetworkFetchReturn> FetchNetworkLyricsAsync(
        string trackName, string trackId, string? artistName, string? albumName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(artistName)) return NetworkFetchReturn.Empty;

        if (!string.IsNullOrEmpty(albumName))
        {
            try
            {
                var exact = await FetchExactLyricsAsync(trackName, artistName, albumName, cancellationToken);
                if (exact.Lyrics.Count > 0) return exact;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LRCLIB /api/get failed; falling back to /api/search: {ex.Message}");
            }
        }

        var searchResults = await SearchAsync(trackName, artistName, cancellationToken);
        var fallback = BestAutomaticSearchResult(searchResults, trackName, artistName);
        return fallback != null
            ? new NetworkFetchReturn { Lyrics = fallback.Lyrics }
            : NetworkFetchReturn.Empty;
    }

    private static async Task<NetworkFetchReturn> FetchExactLyricsAsync(
        string trackName, string artistName, string albumName, CancellationToken ct)
    {
        var url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artistName)}&track_name={Uri.EscapeDataString(trackName)}&album_name={Uri.EscapeDataString(albumName)}";
        var lrc = await Http.GetFromJsonAsync<LrclibLyrics>(url, ct) ?? new LrclibLyrics();
        return new NetworkFetchReturn { Lyrics = lrc.Lyrics };
    }

    public async Task<List<SongResult>> SearchAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
    {
        var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(trackName)}&artist_name={Uri.EscapeDataString(artistName)}";
        var lyricsList = await Http.GetFromJsonAsync<List<LrclibLyrics>>(url, cancellationToken) ?? new();

        var results = new List<SongResult>();
        foreach (var lyric in lyricsList)
        {
            if (lyric.Lyrics.Count > 0)
            {
                results.Add(new SongResult
                {
                    LyricType = "LRCLIB",
                    SongName = lyric.TrackName,
                    AlbumName = lyric.AlbumName,
                    ArtistName = lyric.ArtistName,
                    Lyrics = lyric.Lyrics
                });
            }
        }
        return results;
    }

    private static SongResult? BestAutomaticSearchResult(List<SongResult> results, string trackName, string artistName) =>
        results.FirstOrDefault(r =>
            r.Lyrics.Count > 0 &&
            MetadataMatcher.PlausiblyMatches(trackName, r.SongName) &&
            MetadataMatcher.PlausiblyMatches(artistName, r.ArtistName));
}

/// <summary>LRCLIB /api/get 与 /api/search 响应模型。</summary>
public sealed class LrclibLyrics
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("trackName")]
    public string TrackName { get; set; } = "";

    [JsonPropertyName("artistName")]
    public string ArtistName { get; set; } = "";

    [JsonPropertyName("albumName")]
    public string AlbumName { get; set; } = "";

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("instrumental")]
    public bool Instrumental { get; set; }

    [JsonPropertyName("plainLyrics")]
    public string PlainLyrics { get; set; } = "";

    [JsonPropertyName("syncedLyrics")]
    public string SyncedLyrics { get; set; } = "";

    /// <summary>instrumental 或 syncedLyrics 为空时返回空列表（对应 macOS 解码逻辑）。</summary>
    public List<LyricLine> Lyrics
    {
        get
        {
            if (Instrumental || string.IsNullOrEmpty(SyncedLyrics)) return new List<LyricLine>();
            return new LyricsParser(SyncedLyrics).Lyrics;
        }
    }
}
