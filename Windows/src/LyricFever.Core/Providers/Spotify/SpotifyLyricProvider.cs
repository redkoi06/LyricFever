using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LyricFever.Core.Lyrics;
using LyricFever.Core.Providers.Spotify;

namespace LyricFever.Core.Providers;

/// <summary>
/// Spotify 歌词源（对应 macOS SpotifyLyricProvider）：
/// sp_dc cookie + 匿名 token 接口（HOTP 认证）→ spclient color-lyrics API。
/// 协议与 macOS 版保持一致（UA 伪装、buildVer/buildDate、401 处理）。
/// </summary>
public sealed class SpotifyLyricProvider : ILyricProvider
{
    public string ProviderName => "Spotify Lyric Provider";

    private const string FakeSafariUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_6_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15";

    private const string SecretUrl = "https://iloveyoulyricfever.github.io/myloveisasecret/mylove.json";
    private const string ServerTimeUrl = "https://open.spotify.com/api/server-time";
    private const string TokenUrlBase = "https://open.spotify.com/api/token";
    private const string LyricsUrlBase = "https://spclient.wg.spotify.com/color-lyrics/v2/track";

    private readonly HttpClient _http;

    /// <summary>sp_dc cookie 值，由上层（登录服务）注入。</summary>
    public string? SpDcCookie { get; set; }

    /// <summary>token 过期回调（401 时触发，上层用于标记需重新登录）。</summary>
    public event Action? Unauthorized;

    private AccessTokenJson? _accessToken;
    private long _lastCounter;

    /// <summary>清除缓存的访问 token（登录状态变化时调用，强制下次重新获取）。</summary>
    public void ClearAccessToken()
    {
        lock (this)
        {
            _accessToken = null;
        }
    }

    private bool IsAccessTokenAlive =>
        _accessToken != null &&
        _accessToken.AccessTokenExpirationTimestampMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public SpotifyLyricProvider()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(FakeSafariUserAgent);
    }

    // ------------------------------------------------------------------
    // Token
    // ------------------------------------------------------------------

    /// <summary>
    /// 生成/复用匿名访问 token（对应 macOS generateAccessToken）：
    /// server-time → HOTP（远程 secret + XOR 混淆）→ token URL（sp_dc cookie）。
    /// </summary>
    public async Task GenerateAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsAccessTokenAlive) return;

        var serverTimeData = await GetWithCheckAsync(ServerTimeUrl, cancellationToken);
        var serverTime = JsonSerializer.Deserialize<SpotifyServerTime>(serverTimeData)?.ServerTime
                         ?? throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);

        var currentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var counter = (ulong)(currentUnix / 30);

        // 远程 secret：XOR 混淆解密（对应 macOS secretData + 内联处理）
        var secretCipher = await GetSecretAsync(cancellationToken);
        var processed = new byte[secretCipher.Count];
        for (var i = 0; i < secretCipher.Count; i++)
        {
            processed[i] = (byte)(secretCipher[i] ^ (i % 33 + 9));
        }
        var processedStr = string.Join("", processed.Select(b => b.ToString()));
        var key = Encoding.UTF8.GetBytes(processedStr);
        var hotp = HotpGenerator.Generate(key, counter);

        var buildVer = "web-player_2025-06-10_1749524883369_eef30f4";
        var buildDate = "2025-06-10";
        var urlString = $"{TokenUrlBase}?reason=init&productType=web-player&totp={hotp}&totpServer={hotp}&totpVer=5&sTime={serverTime}&cTime={currentUnix}&buildVer={{\"{buildVer}\"}}&buildDate={{\"{buildDate}\"}}";

        var request = new HttpRequestMessage(HttpMethod.Get, urlString);
        request.Headers.TryAddWithoutValidation("Cookie", $"sp_dc={SpDcCookie}");

        byte[] tokenData;
        try
        {
            tokenData = await SendWithCheckAsync(request, cancellationToken);
        }
        catch (SpotifyLyricError e) when (e.Kind == SpotifyLyricError.ErrorKind.Unauthorized)
        {
            _accessToken = null;
            Unauthorized?.Invoke();
            throw;
        }

        try
        {
            _accessToken = JsonSerializer.Deserialize<AccessTokenJson>(tokenData)
                           ?? throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);
            _lastCounter = (long)counter;
        }
        catch (JsonException)
        {
            _accessToken = null;
            var wrapped = TryDeserializeError(tokenData);
            if (wrapped?.Code == 401)
            {
                Unauthorized?.Invoke();
                throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.Unauthorized);
            }
            throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);
        }
    }

    private async Task<List<int>> GetSecretAsync(CancellationToken cancellationToken)
    {
        var data = await GetWithCheckAsync(SecretUrl, cancellationToken);
        var secret = JsonSerializer.Deserialize<SecretVersion>(data);
        return secret?.Message ?? throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);
    }

    // ------------------------------------------------------------------
    // Lyrics
    // ------------------------------------------------------------------

    public async Task<NetworkFetchReturn> FetchNetworkLyricsAsync(
        string trackName, string trackId, string? artistName, string? albumName,
        CancellationToken cancellationToken = default)
    {
        // 本地文件无网络歌词
        if (trackId.Length != 22)
        {
            throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.IsLocalFile);
        }

        await GenerateAccessTokenAsync(cancellationToken);
        if (_accessToken == null)
            throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{LyricsUrlBase}/{trackId}?format=json&vocalRemoval=false");
        request.Headers.Add("app-platform", "WebPlayer");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken.AccessToken);

        cancellationToken.ThrowIfCancellationRequested();
        byte[] data;
        try
        {
            data = await SendWithCheckAsync(request, cancellationToken);
        }
        catch (SpotifyLyricError e) when (e.Kind == SpotifyLyricError.ErrorKind.Unauthorized)
        {
            _accessToken = null;
            throw;
        }

        if (data.Length == 0) return NetworkFetchReturn.Empty;

        var parent = JsonSerializer.Deserialize<SpotifyParent>(data)
                     ?? throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);

        // 仅接受 LINE_SYNCED 同步歌词（对应 macOS SpotifyLyrics 解码逻辑）
        var lines = parent.Lyrics.SyncType == "LINE_SYNCED"
            ? parent.Lyrics.Lines?.Select(l => new LyricLine
            {
                StartTimeMs = l.StartTimeMs,
                Words = l.Words
            }).ToList() ?? new List<LyricLine>()
            : new List<LyricLine>();

        return new NetworkFetchReturn
        {
            Lyrics = lines,
            ColorData = parent.Colors.Background
        };
    }

    public Task<List<SongResult>> SearchAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
    {
        // Windows 版无 Apple Music 映射需求；手动搜索在功能范围外。
        return Task.FromResult(new List<SongResult>());
    }

    // ------------------------------------------------------------------
    // Track search（SMTC 不给 Spotify URI，需按 歌手+歌名 搜索 track ID）
    // 对应 macOS searchForTrackForAppleMusic 的 GraphQL searchDesktop 路径。
    // ------------------------------------------------------------------

    public async Task<SpotifyTrackInfo?> SearchSpotifyTrackAsync(
        string searchTerm, CancellationToken cancellationToken = default)
    {
        await GenerateAccessTokenAsync(cancellationToken);
        if (_accessToken == null)
            throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.InvalidResponse);

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api-partner.spotify.com/pathfinder/v2/query");
        request.Headers.Add("app-platform", "WebPlayer");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken.AccessToken);

        var body = new
        {
            variables = new
            {
                searchTerm,
                offset = 0,
                limit = 1,
                numberOfTopResults = 1,
                includeAudiobooks = false,
                includeArtistHasConcertsField = false,
                includePreReleases = false,
                includeLocalConcertsField = false,
                includeAuthors = false
            },
            operationName = "searchDesktop",
            extensions = new
            {
                persistedQuery = new
                {
                    version = 1,
                    sha256Hash = "d9f785900f0710b31c07818d617f4f7600c1e21217e80f5b043d1e78d74e6026"
                }
            }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        cancellationToken.ThrowIfCancellationRequested();
        var data = await SendWithCheckAsync(request, cancellationToken);
        return ParseSearchDesktopResult(data);
    }

    /// <summary>解析 searchDesktop GraphQL 响应中的首个曲目（对应 macOS getDetailsFromSpotifyInternalSearchJSON）。</summary>
    internal static SpotifyTrackInfo? ParseSearchDesktopResult(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var dataObj)) return null;
            if (!dataObj.TryGetProperty("searchV2", out var searchV2)) return null;
            if (!searchV2.TryGetProperty("tracksV2", out var tracksV2)) return null;
            if (!tracksV2.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) return null;

            var firstItem = items[0];
            if (!firstItem.TryGetProperty("item", out var item)) return null;
            if (!item.TryGetProperty("data", out var trackData)) return null;

            var name = trackData.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            var id = trackData.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

            string? album = null;
            if (trackData.TryGetProperty("albumOfTrack", out var albumObj) &&
                albumObj.TryGetProperty("name", out var albumName))
                album = albumName.GetString();

            string? artist = null;
            if (trackData.TryGetProperty("artists", out var artists) &&
                artists.TryGetProperty("items", out var artistItems) &&
                artistItems.GetArrayLength() > 0 &&
                artistItems[0].TryGetProperty("profile", out var profile) &&
                profile.TryGetProperty("name", out var artistName))
                artist = artistName.GetString();

            if (id == null || name == null) return null;

            return new SpotifyTrackInfo(id, name, artist ?? "", album ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // HTTP helpers
    // ------------------------------------------------------------------

    private async Task<byte[]> GetWithCheckAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        CheckStatus(response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<byte[]> SendWithCheckAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        CheckStatus(response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static void CheckStatus(System.Net.HttpStatusCode status)
    {
        var code = (int)status;
        if (code == 401) throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.Unauthorized);
        if (code == 429) throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.TooManyTries);
        if (code is < 200 or >= 300)
            throw new SpotifyLyricError(SpotifyLyricError.ErrorKind.HttpStatus, code);
    }

    private static ErrorWrapper.ErrorInfo? TryDeserializeError(byte[] data)
    {
        try
        {
            return JsonSerializer.Deserialize<ErrorWrapper>(data)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
