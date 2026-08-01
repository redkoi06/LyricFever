using System.Text.Json;
using LyricFever.Core.Lyrics;
using LyricFever.Core.Storage;
using Xunit;

namespace LyricFever.Core.Tests;

/// <summary>对照 macOS LyricsRepositoryTests（CoreData）移植为 SQLite 版本。</summary>
public class LyricsRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public LyricsRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lyricfever-test-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    private (SqliteDatabase Db, LyricsRepository Repo) Create()
    {
        var db = new SqliteDatabase(_dbPath);
        return (db, new LyricsRepository(db));
    }

    [Fact]
    public void UpsertReplacesExistingLyricsWithoutDuplicateRows()
    {
        var (db, repo) = Create();
        repo.Upsert(new List<LyricLine> { new(1000, "First") }, "track", "Song");
        repo.Upsert(new List<LyricLine> { new(2000, "Updated") }, "track", "Song");

        var lyrics = repo.GetLyrics("track");
        Assert.Equal(new[] { "Updated" }, lyrics!.Select(l => l.Words));
        Assert.Equal(1, repo.CacheInfo().SongCount);
    }

    [Fact]
    public void CorruptParallelArraysAreDeletedAndReported()
    {
        var (db, repo) = Create();
        // 直接构造损坏数据：时间戳 2 个、歌词 1 个（对应 macOS 测试的 SongObject 直接插入）
        using (var conn = db.OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO SongObject (id, title, language, lyricsTimestamps, lyricsWords, downloadDate)
                VALUES ($id, $title, '', $times, $words, $date)
                """;
            cmd.Parameters.AddWithValue("$id", "corrupt");
            cmd.Parameters.AddWithValue("$title", "Corrupt");
            cmd.Parameters.AddWithValue("$times", JsonSerializer.Serialize(new[] { 1000.0, 2000.0 }));
            cmd.Parameters.AddWithValue("$words", JsonSerializer.Serialize(new[] { "Only one" }));
            cmd.Parameters.AddWithValue("$date", DateTimeOffset.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        var ex = Assert.Throws<LyricsRepositoryCorruptEntryException>(() => repo.GetLyrics("corrupt"));
        Assert.Equal("corrupt", ex.Message.Split(" ").Last());
        Assert.Null(repo.GetLyrics("corrupt"));
    }
}
