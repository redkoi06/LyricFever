using System.Net;
using System.Text;
using LyricFever.Core.Providers;
using LyricFever.Core.Providers.Spotify;
using LyricFever.Core.Storage;
using Xunit;

namespace LyricFever.Core.Tests;

/// <summary>
/// Spotify track 映射与搜索响应（P0-D）：
/// 匹配校验（拒绝错误搜索结果，防止错误曲目被永久缓存）、缓存命中、
/// GraphQL JSON 解析、401/429 状态分类。
/// </summary>
public class SpotifyTrackMapperTests : IDisposable
{
    private readonly string _dbPath;

    public SpotifyTrackMapperTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lyricfever-smap-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    private SpotifyTrackMapper MakeMapper() =>
        new(new SpotifyLyricProvider(), new SqliteDatabase(_dbPath));

    // ---- 匹配校验（MatchesRequest） ----

    [Fact]
    public void MatchesRequest_ExactTitleAndArtist_Accepts()
    {
        var result = new SpotifyTrackInfo("track-1", "Hello", "Adele", "25");
        Assert.True(SpotifyTrackMapper.MatchesRequest(result, "Hello", "Adele"));
    }

    [Fact]
    public void MatchesRequest_CaseAndDiacriticsInsensitive()
    {
        var result = new SpotifyTrackInfo("track-1", "Café", "Björk", "");
        Assert.True(SpotifyTrackMapper.MatchesRequest(result, "café", "bjork"));
    }

    [Fact]
    public void MatchesRequest_FeatSuffixInQuery_MatchesStrippedTitle()
    {
        // 播放器元数据带副标题，Spotify 结果为剥离后的主歌名 → 接受
        var result = new SpotifyTrackInfo("track-1", "Love Story", "Taylor Swift", "");
        Assert.True(SpotifyTrackMapper.MatchesRequest(result, "Love Story (Taylor's Version)", "Taylor Swift"));
    }

    [Fact]
    public void MatchesRequest_MissingResultArtist_AcceptsByTitle()
    {
        var result = new SpotifyTrackInfo("track-1", "Believer", "", "");
        Assert.True(SpotifyTrackMapper.MatchesRequest(result, "Believer", "Imagine Dragons"));
    }

    [Fact]
    public void MatchesRequest_DifferentSong_Rejects()
    {
        var result = new SpotifyTrackInfo("track-1", "Shape of You", "Ed Sheeran", "");
        Assert.False(SpotifyTrackMapper.MatchesRequest(result, "Perfect", "Ed Sheeran"));
    }

    [Fact]
    public void MatchesRequest_DifferentArtist_Rejects()
    {
        var result = new SpotifyTrackInfo("track-1", "Believer", "Imagine Dragons", "");
        Assert.False(SpotifyTrackMapper.MatchesRequest(result, "Believer", "Krewella"));
    }

    // ---- 缓存（命中 / 写入后可读） ----

    [Fact]
    public void PutCached_ThenGetCached_Hits()
    {
        var mapper = MakeMapper();
        var info = new SpotifyTrackInfo("track-42", "Blinding Lights", "The Weeknd", "After Hours");

        mapper.PutCached("The Weeknd|Blinding Lights", info);

        var hit = mapper.GetCached("The Weeknd|Blinding Lights");
        Assert.NotNull(hit);
        Assert.Equal("track-42", hit!.TrackId);
        Assert.Equal("Blinding Lights", hit.Name);
        Assert.Equal("The Weeknd", hit.Artist);
    }

    [Fact]
    public void GetCached_UnknownKey_ReturnsNull()
    {
        var mapper = MakeMapper();
        Assert.Null(mapper.GetCached("Nobody|Nothing"));
    }

    // ---- GraphQL searchDesktop JSON 解析 ----

    [Fact]
    public void ParseSearchDesktopResult_ValidJson_ReturnsFirstTrack()
    {
        var json = """
        {
          "data": {
            "searchV2": {
              "tracksV2": {
                "items": [
                  {
                    "item": {
                      "data": {
                        "id": "4uLU6hMCjMI75M1A2tKUQC",
                        "name": "Shape of You",
                        "albumOfTrack": { "name": "÷ (Deluxe)" },
                        "artists": { "items": [ { "profile": { "name": "Ed Sheeran" } } ] }
                      }
                    }
                  }
                ]
              }
            }
          }
        }
        """;

        var info = SpotifyLyricProvider.ParseSearchDesktopResult(Encoding.UTF8.GetBytes(json));
        Assert.NotNull(info);
        Assert.Equal("4uLU6hMCjMI75M1A2tKUQC", info!.TrackId);
        Assert.Equal("Shape of You", info.Name);
        Assert.Equal("Ed Sheeran", info.Artist);
        Assert.Equal("÷ (Deluxe)", info.Album);
    }

    [Fact]
    public void ParseSearchDesktopResult_EmptyItems_ReturnsNull()
    {
        var json = """{ "data": { "searchV2": { "tracksV2": { "items": [] } } } }""";
        Assert.Null(SpotifyLyricProvider.ParseSearchDesktopResult(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ParseSearchDesktopResult_MissingId_ReturnsNull()
    {
        var json = """
        { "data": { "searchV2": { "tracksV2": { "items": [ { "item": { "data": { "name": "X" } } } ] } } } }
        """;
        Assert.Null(SpotifyLyricProvider.ParseSearchDesktopResult(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void ParseSearchDesktopResult_GarbageJson_ReturnsNull()
    {
        Assert.Null(SpotifyLyricProvider.ParseSearchDesktopResult(Encoding.UTF8.GetBytes("{ not json ]")));
    }

    // ---- HTTP 状态分类（401/429） ----

    [Fact]
    public void CheckStatus_401_ThrowsUnauthorized()
    {
        var ex = Assert.Throws<SpotifyLyricError>(
            () => SpotifyLyricProvider.CheckStatus(HttpStatusCode.Unauthorized));
        Assert.Equal(SpotifyLyricError.ErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public void CheckStatus_429_ThrowsTooManyTries()
    {
        var ex = Assert.Throws<SpotifyLyricError>(
            () => SpotifyLyricProvider.CheckStatus((HttpStatusCode)429));
        Assert.Equal(SpotifyLyricError.ErrorKind.TooManyTries, ex.Kind);
    }

    [Fact]
    public void CheckStatus_2xx_DoesNotThrow()
    {
        SpotifyLyricProvider.CheckStatus(HttpStatusCode.OK);
        SpotifyLyricProvider.CheckStatus(HttpStatusCode.NoContent);
    }

    [Fact]
    public void CheckStatus_OtherError_ThrowsHttpStatus()
    {
        var ex = Assert.Throws<SpotifyLyricError>(
            () => SpotifyLyricProvider.CheckStatus(HttpStatusCode.InternalServerError));
        Assert.Equal(SpotifyLyricError.ErrorKind.HttpStatus, ex.Kind);
    }
}
