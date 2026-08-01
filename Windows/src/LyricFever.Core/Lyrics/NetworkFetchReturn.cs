namespace LyricFever.Core.Lyrics;

/// <summary>歌词抓取返回值（对应 macOS NetworkFetchReturn）。ColorData 为专辑背景色 ARGB，无则 -1。</summary>
public sealed class NetworkFetchReturn
{
    public List<LyricLine> Lyrics { get; init; } = new();
    public int ColorData { get; init; } = -1;

    public static NetworkFetchReturn Empty { get; } = new();
}
