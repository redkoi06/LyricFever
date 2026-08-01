namespace LyricFever.Core.Lyrics;

/// <summary>歌词搜索结果（对应 macOS SongResult）。</summary>
public sealed class SongResult
{
    public string LyricType { get; init; } = "";
    public string SongName { get; init; } = "";
    public string AlbumName { get; init; } = "";
    public string ArtistName { get; init; } = "";
    public List<LyricLine> Lyrics { get; init; } = new();

    public override string ToString() => $"[{LyricType}] {SongName} - {ArtistName} ({Lyrics.Count} lines)";
}
