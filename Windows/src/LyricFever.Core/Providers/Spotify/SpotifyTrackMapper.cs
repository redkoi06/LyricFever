using LyricFever.Core.Storage;

namespace LyricFever.Core.Providers.Spotify;

/// <summary>
/// SMTC 元数据 → Spotify track ID 映射（Windows 版无 ScriptingBridge 的 spotifyUrl，按歌手+歌名搜索）。
/// 结果缓存到 SQLite（SpotifyTrackMap 表），避免每次切歌都搜索。
/// </summary>
public sealed class SpotifyTrackMapper
{
    private readonly SpotifyLyricProvider _provider;
    private readonly SqliteDatabase _db;

    public SpotifyTrackMapper(SpotifyLyricProvider provider, SqliteDatabase db)
    {
        _provider = provider;
        _db = db;
    }

    /// <summary>
    /// 解析 track ID。失败返回 null（上层走 LRCLIB/NetEase 兜底）。
    /// 搜索词取歌手+歌名（有专辑名时附加专辑），与 macOS 版 searchTerm 构造一致。
    /// </summary>
    public async Task<SpotifyTrackInfo?> ResolveAsync(string title, string artist, string? album,
        CancellationToken cancellationToken = default)
    {
        var key = $"{artist}|{title}";

        // 1. 缓存命中
        var cached = GetCached(key);
        if (cached != null) return cached;

        // 2. 网络搜索
        if (string.IsNullOrEmpty(title)) return null;
        var searchTerm = string.IsNullOrEmpty(album)
            ? $"{title} {artist}".Trim()
            : $"{title} {album} {artist}".Trim();

        SpotifyTrackInfo? result;
        try
        {
            result = await _provider.SearchSpotifyTrackAsync(searchTerm, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][TrackMap] search failed: {ex.Message}");
            return null;
        }
        if (result == null) return null;

        PutCached(key, result);
        return result;
    }

    private SpotifyTrackInfo? GetCached(string key)
    {
        try
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT trackId, songName, artistName, albumName FROM SpotifyTrackMap WHERE artistTitleKey = $key LIMIT 1";
            cmd.Parameters.AddWithValue("$key", key);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return new SpotifyTrackInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][TrackMap] cache read failed: {ex.Message}");
            return null;
        }
    }

    private void PutCached(string key, SpotifyTrackInfo info)
    {
        try
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO SpotifyTrackMap (artistTitleKey, trackId, songName, artistName, albumName, updatedAt)
                VALUES ($key, $trackId, $name, $artist, $album, $date)
                ON CONFLICT(artistTitleKey) DO UPDATE SET
                    trackId = excluded.trackId, songName = excluded.songName,
                    artistName = excluded.artistName, albumName = excluded.albumName,
                    updatedAt = excluded.updatedAt
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$trackId", info.TrackId);
            cmd.Parameters.AddWithValue("$name", info.Name);
            cmd.Parameters.AddWithValue("$artist", info.Artist);
            cmd.Parameters.AddWithValue("$album", info.Album);
            cmd.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][TrackMap] cache write failed: {ex.Message}");
        }
    }
}
