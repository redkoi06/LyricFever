using LyricFever.Core.Lyrics;
using LyricFever.Core.Storage;
using Xunit;

namespace LyricFever.Core.Tests;

/// <summary>
/// 翻译产物缓存（用户核心要求：命中缓存不重新调用模型）。
/// 覆盖：写入后命中、歌词变化失效、模型版本变化失效、短数组补齐、
/// ready 语义（只翻译/只罗马音互不遮蔽、失败不污染、后补缺失类别）。
/// </summary>
public class TranslationCacheTests : IDisposable
{
    private readonly string _dbPath;

    public TranslationCacheTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"lyricfever-tc-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* ignore */ }
    }

    private TranslationCache MakeCache() => new(new SqliteDatabase(_dbPath));

    private static List<LyricLine> Lyrics(params (double, string)[] lines) =>
        lines.Select(l => new LyricLine(l.Item1, l.Item2)).ToList();

    [Fact]
    public void PutThenGetHits()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好", "世界" }, true,
            new List<string>(), false);

        var hit = cache.Get("track1", lyrics, "en", "zh", 1, 1);
        Assert.NotNull(hit);
        Assert.True(hit!.TranslationReady);
        Assert.False(hit.RomanizationReady);
        Assert.Equal(new[] { "你好", "世界" }, hit.Translated);
    }

    [Fact]
    public void LyricChangeInvalidatesCache()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好", "世界" }, true, new List<string>(), false);

        var changed = Lyrics((1000, "Hello"), (2000, "Different"));
        Assert.Null(cache.Get("track1", changed, "en", "zh", 1, 1));
    }

    [Fact]
    public void ModelVersionChangeInvalidatesCache()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好", "世界" }, true, new List<string>(), false);

        Assert.Null(cache.Get("track1", lyrics, "en", "zh", 2, 1));
    }

    [Fact]
    public void DeleteVersionsOlderThanPurgesRetiredTranslationsOnly()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"));
        cache.Put("old", lyrics, "en", "zh", 1, 1,
            new List<string> { "retired" }, true, new List<string>(), false);
        cache.Put("current", lyrics, "en", "zh", 2, 1,
            new List<string> { "人工译词" }, true, new List<string>(), false);

        var removed = cache.DeleteVersionsOlderThan(2);

        Assert.Equal(1, removed);
        Assert.Null(cache.Get("old", lyrics, "en", "zh", 1, 1));
        Assert.NotNull(cache.Get("current", lyrics, "en", "zh", 2, 1));
    }

    [Fact]
    public void ShortTranslationPaddedAndMissingNextLineReusesPrevious()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好" }, true, new List<string>(), false);

        var hit = cache.Get("track1", lyrics, "en", "zh", 1, 1);
        Assert.NotNull(hit);
        Assert.Equal(new[] { "你好", "你好" }, hit!.Translated);
        Assert.Equal(new[] { "", "" }, hit.Romanized);
    }

    [Fact]
    public void LegacyCachedMissingNextLineIsRepairedOnRead()
    {
        var database = new SqliteDatabase(_dbPath);
        var cache = new TranslationCache(database);
        var lyrics = Lyrics(
            (72_510, "ふとした仕草に今日もハートZUKI★ZUKI"),
            (77_800, "さりげな笑顔を深読みしぎて Over heat!"));
        const string complete = "意想不到的动作今天也好喜欢 太过深入地解读着笑容";
        cache.Put("track1", lyrics, "ja", "zh", 1, 1,
            new List<string> { complete, complete }, true,
            new List<string>(), false);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE TranslationCache
                SET translatedLyrics = '["意想不到的动作今天也好喜欢 太过深入地解读着笑容",""]'
                WHERE trackId = 'track1'
                """;
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var hit = cache.Get("track1", lyrics, "ja", "zh", 1, 1);

        Assert.NotNull(hit);
        Assert.Equal(new[] { complete, complete }, hit!.Translated);
    }

    /// <summary>只开翻译写入后，罗马音不得被视为可用；后续开罗马音时必须重建。</summary>
    [Fact]
    public void TranslationOnlyCacheDoesNotMasqueradeRomanization()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "こんにちは"));
        cache.Put("t1", lyrics, "ja", "zh", 1, 1,
            new List<string> { "你好" }, true, new List<string>(), false);

        var hit = cache.Get("t1", lyrics, "ja", "zh", 1, 1);
        Assert.NotNull(hit);
        Assert.True(hit!.TranslationReady);
        Assert.False(hit.RomanizationReady); // 关键：不能把空罗马音当有效
    }

    /// <summary>翻译失败（not ready）不得污染：后续补写罗马音时旧空译文不覆盖；失败重试仍可写。</summary>
    [Fact]
    public void FailedTranslationNotReadyThenRomanizationAdded()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "こんにちは"));

        // 第一次：翻译失败（not ready），罗马音成功
        cache.Put("t1", lyrics, "ja", "zh", 1, 1,
            new List<string>(), false, new List<string> { "konnichiwa" }, true);

        var hit = cache.Get("t1", lyrics, "ja", "zh", 1, 1);
        Assert.NotNull(hit);
        Assert.False(hit!.TranslationReady);
        Assert.True(hit.RomanizationReady);

        // 第二次：翻译成功，写入有效译文（not ready 的空译文不得覆盖）
        cache.Put("t1", lyrics, "ja", "zh", 1, 1,
            new List<string> { "你好" }, true, new List<string>(), false);

        hit = cache.Get("t1", lyrics, "ja", "zh", 1, 1);
        Assert.True(hit!.TranslationReady);
        Assert.True(hit.RomanizationReady);
        Assert.Equal("你好", hit.Translated[0]);
        Assert.Equal("konnichiwa", hit.Romanized[0]);
    }

    /// <summary>失败结果不得覆盖已有有效产物。</summary>
    [Fact]
    public void FailedRetryDoesNotOverwriteGoodTranslation()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"));
        cache.Put("t1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好" }, true, new List<string>(), false);

        // 失败重试（not ready）：不得覆盖已有译文
        cache.Put("t1", lyrics, "en", "zh", 1, 1,
            new List<string>(), false, new List<string>(), false);

        var hit = cache.Get("t1", lyrics, "en", "zh", 1, 1);
        Assert.True(hit!.TranslationReady);
        Assert.Equal("你好", hit.Translated[0]);
    }
}
