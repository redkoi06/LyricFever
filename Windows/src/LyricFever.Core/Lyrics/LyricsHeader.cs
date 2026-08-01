namespace LyricFever.Core.Lyrics;

/// <summary>LRC 元信息头（对应 macOS LyricsHeader）。</summary>
public sealed class LyricsHeader
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Album { get; set; }
    public string? By { get; set; }
    /// <summary>毫秒偏移。</summary>
    public double Offset { get; set; }
    public string? Editor { get; set; }
    public string? Version { get; set; }
}
