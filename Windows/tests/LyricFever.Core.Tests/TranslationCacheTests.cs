using LyricFever.Core.Lyrics;
using LyricFever.Core.Storage;
using Xunit;

namespace LyricFever.Core.Tests;

/// <summary>
/// 翻译产物缓存（用户核心要求：命中缓存不重新调用模型）。
/// 覆盖：写入后命中、歌词变化失效、模型版本变化失效、行数不一致拒绝。
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
            new List<string> { "你好", "世界" }, new List<string>());

        var hit = cache.Get("track1", lyrics, "en", "zh", 1, 1);
        Assert.NotNull(hit);
        Assert.Equal(new[] { "你好", "世界" }, hit!.Value.Translated);
    }

    [Fact]
    public void LyricChangeInvalidatesCache()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好", "世界" }, new List<string>());

        var changed = Lyrics((1000, "Hello"), (2000, "Different"));
        Assert.Null(cache.Get("track1", changed, "en", "zh", 1, 1));
    }

    [Fact]
    public void ModelVersionChangeInvalidatesCache()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好", "世界" }, new List<string>());

        Assert.Null(cache.Get("track1", lyrics, "en", "zh", 2, 1));
    }

    [Fact]
    public void ShortTranslationPaddedToLyricLength()
    {
        var cache = MakeCache();
        var lyrics = Lyrics((1000, "Hello"), (2000, "World"));
        cache.Put("track1", lyrics, "en", "zh", 1, 1,
            new List<string> { "你好" }, new List<string>());

        // Put 内部补齐等长（缺失行空字符串占位），Get 仍命中
        var hit = cache.Get("track1", lyrics, "en", "zh", 1, 1);
        Assert.NotNull(hit);
        Assert.Equal(new[] { "你好", "" }, hit!.Value.Translated);
        Assert.Equal(new[] { "", "" }, hit.Value.Romanized);
    }
}
