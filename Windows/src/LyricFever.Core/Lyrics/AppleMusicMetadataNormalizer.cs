namespace LyricFever.Core.Lyrics;

/// <summary>
/// Apple Music for Windows 的 SMTC Artist/Album 字段有时会合并成“歌手 — 专辑”。
/// 搜索歌词前拆回独立字段；Spotify 和其他播放器不使用该规则。
/// </summary>
public static class AppleMusicMetadataNormalizer
{
    private static readonly string[] Separators = [" — ", " – "];

    public static (string Artist, string Album) Normalize(string? artist, string? album)
    {
        var normalizedArtist = artist?.Trim() ?? "";
        var normalizedAlbum = album?.Trim() ?? "";

        foreach (var separator in Separators)
        {
            var separatorIndex = normalizedArtist.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex <= 0) continue;

            var actualArtist = normalizedArtist[..separatorIndex].Trim();
            var decoratedAlbum = normalizedArtist[(separatorIndex + separator.Length)..].Trim();
            if (actualArtist.Length == 0) continue;

            if (normalizedAlbum.Length == 0 ||
                string.Equals(normalizedAlbum, normalizedArtist, StringComparison.OrdinalIgnoreCase))
            {
                normalizedAlbum = decoratedAlbum;
            }
            normalizedArtist = actualArtist;
            break;
        }

        if (string.Equals(normalizedAlbum, normalizedArtist, StringComparison.OrdinalIgnoreCase))
            normalizedAlbum = "";

        return (normalizedArtist, normalizedAlbum);
    }
}
