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

/// <summary>
/// 可提供人工校对译词的歌词源。返回值必须与 referenceLyrics 等长；没有可靠匹配时返回 null。
/// </summary>
public interface IHumanTranslationProvider
{
    Task<HumanLyricBundle?> FetchHumanLyricBundleAsync(
        string trackName,
        string? artistName,
        string? albumName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>?> FetchTranslationAsync(
        string trackName,
        string? artistName,
        string? albumName,
        IReadOnlyList<LyricLine> referenceLyrics,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A matched original-lyric and human-translation pair from one platform.
/// Both lists use the same timeline and must have identical lengths.
/// </summary>
public sealed record HumanLyricBundle(
    IReadOnlyList<LyricLine> SourceLyrics,
    IReadOnlyList<string> TranslatedLyrics);

/// <summary>歌词源类型。</summary>
public enum LyricProviderType
{
    Spotify,
    Lrclib,
    NetEase
}
