using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Providers;

/// <summary>歌词源（对应 macOS LyricProvider 协议）。</summary>
public interface ILyricProvider
{
    string ProviderName { get; }

    /// <summary>按曲目信息抓取歌词；artist 缺失时返回空结果。</summary>
    Task<NetworkFetchReturn> FetchNetworkLyricsAsync(
        string trackName,
        string trackId,
        string? artistName,
        string? albumName,
        CancellationToken cancellationToken = default);

    /// <summary>搜索候选歌词（手动搜索 / Apple Music 映射用）。</summary>
    Task<List<SongResult>> SearchAsync(
        string trackName,
        string artistName,
        CancellationToken cancellationToken = default);
}

/// <summary>歌词源类型。</summary>
public enum LyricProviderType
{
    Spotify,
    Lrclib,
    NetEase
}
