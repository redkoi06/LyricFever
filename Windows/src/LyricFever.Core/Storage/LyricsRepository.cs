using System.Text.Json;
using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Storage;

public sealed class LyricsRepositoryCorruptEntryException : Exception
{
    public LyricsRepositoryCorruptEntryException(string trackId)
        : base($"Corrupt cache entry for track {trackId}") { }
}

public sealed record LyricsRepositoryCacheInfo(long ByteCount, int SongCount, int LineCount);

/// <summary>
/// 歌词离线缓存（对应 macOS LyricsRepository，SQLite 实现）。
/// 行为对齐：空歌词删除条目；时间戳与歌词数量不匹配视为损坏并删除。
/// </summary>
public sealed class LyricsRepository
{
    private readonly SqliteDatabase _db;

    public LyricsRepository(SqliteDatabase db) => _db = db;

    public void Upsert(List<LyricLine> lyrics, string trackId, string trackName)
    {
        if (lyrics.Count == 0)
        {
            Delete(trackId);
            return;
        }

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SongObject (id, title, language, lyricsTimestamps, lyricsWords, downloadDate)
            VALUES ($id, $title, $lang, $times, $words, $date)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                lyricsTimestamps = excluded.lyricsTimestamps,
                lyricsWords = excluded.lyricsWords,
                downloadDate = excluded.downloadDate
            """;
        cmd.Parameters.AddWithValue("$id", trackId);
        cmd.Parameters.AddWithValue("$title", trackName);
        cmd.Parameters.AddWithValue("$lang", "");
        cmd.Parameters.AddWithValue("$times", JsonSerializer.Serialize(lyrics.Select(l => l.StartTimeInMs).ToList()));
        cmd.Parameters.AddWithValue("$words", JsonSerializer.Serialize(lyrics.Select(l => l.Words).ToList()));
        cmd.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<LyricLine>? GetLyrics(string trackId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT lyricsTimestamps, lyricsWords FROM SongObject WHERE id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", trackId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var timestamps = JsonSerializer.Deserialize<List<double>>(reader.GetString(0)) ?? new List<double>();
        var words = JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? new List<string>();

        if (timestamps.Count != words.Count)
        {
            conn.Close();
            Delete(trackId);
            throw new LyricsRepositoryCorruptEntryException(trackId);
        }
        if (words.Count == 0) return null;

        return timestamps.Select((t, i) => new LyricLine(t, words[i])).ToList();
    }

    public void Delete(string trackId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SongObject WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", trackId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteAll()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SongObject";
        cmd.ExecuteNonQuery();
    }

    public LyricsRepositoryCacheInfo CacheInfo()
    {
        long byteCount = 0;
        var songCount = 0;
        var lineCount = 0;

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT lyricsTimestamps, lyricsWords FROM SongObject";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var words = JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? new List<string>();
            var timestamps = JsonSerializer.Deserialize<List<double>>(reader.GetString(0)) ?? new List<double>();
            if (words.Count == 0 && timestamps.Count == 0) continue;
            songCount++;
            lineCount += Math.Max(words.Count, timestamps.Count);
            byteCount += words.Sum(w => (long)System.Text.Encoding.UTF8.GetByteCount(w));
            byteCount += timestamps.Count * sizeof(double);
        }
        return new LyricsRepositoryCacheInfo(byteCount, songCount, lineCount);
    }

    // ---- 颜色缓存（IDToColor） ----

    public int? GetColor(string trackId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT songColor FROM IDToColor WHERE id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", trackId);
        return cmd.ExecuteScalar() as int?;
    }

    public void SetColor(string trackId, int color)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO IDToColor (id, songColor) VALUES ($id, $color)
            ON CONFLICT(id) DO UPDATE SET songColor = excluded.songColor
            """;
        cmd.Parameters.AddWithValue("$id", trackId);
        cmd.Parameters.AddWithValue("$color", color);
        cmd.ExecuteNonQuery();
    }
}
