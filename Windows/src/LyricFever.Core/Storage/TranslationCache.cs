using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Storage;

/// <summary>翻译/罗马音产物缓存条目。</summary>
public sealed record TranslationCacheEntry(
    string CacheKey,
    string TrackId,
    string LyricHash,
    string SourceLanguage,
    string TargetLanguage,
    int ModelVersion,
    int RomanizationVersion,
    List<LyricLine> OriginalLyrics,
    List<string> TranslatedLyrics,
    List<string> RomanizedLyrics);

/// <summary>
/// 翻译产物缓存（用户要求：同一首歌再次播放直接读缓存，不重新调用翻译模型）。
/// 缓存键 = trackID + lyricHash + 源/目标语言 + 模型版本 + 罗马音版本。
/// </summary>
public sealed class TranslationCache
{
    private readonly SqliteDatabase _db;

    public TranslationCache(SqliteDatabase db) => _db = db;

    /// <summary>稳定歌词哈希：时间戳+歌词序列的 SHA256 前 16 字符。歌词变化即失效。</summary>
    public static string HashLyrics(List<LyricLine> lyrics)
    {
        var sb = new StringBuilder();
        foreach (var line in lyrics)
        {
            sb.Append(line.StartTimeMs).Append('|').Append(line.Words).Append('\n');
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>
    /// 读缓存。命中条件：键匹配且产物行数与原文行数一致。
    /// 翻译/罗马音任一不可用时（如模型版本变化）返回 null，由上层重建。
    /// </summary>
    public (List<string> Translated, List<string> Romanized)? Get(
        string trackId, List<LyricLine> lyrics, string sourceLanguage,
        string targetLanguage, int modelVersion, int romanizationVersion)
    {
        var key = BuildKey(trackId, lyrics, sourceLanguage, targetLanguage, modelVersion, romanizationVersion);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT originalLyrics, translatedLyrics, romanizedLyrics
            FROM TranslationCache WHERE cacheKey = $key LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$key", key);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        try
        {
            var original = JsonSerializer.Deserialize<List<LyricLine>>(reader.GetString(0)) ?? new();
            var translated = JsonSerializer.Deserialize<List<string>>(reader.GetString(1)) ?? new();
            var romanized = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? new();

            // 完整性校验：行数必须与当前歌词一致
            if (original.Count != lyrics.Count ||
                translated.Count != lyrics.Count ||
                romanized.Count != lyrics.Count)
                return null;

            return (translated, romanized);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Put(string trackId, List<LyricLine> lyrics, string sourceLanguage,
        string targetLanguage, int modelVersion, int romanizationVersion,
        List<string> translated, List<string> romanized)
    {
        var key = BuildKey(trackId, lyrics, sourceLanguage, targetLanguage, modelVersion, romanizationVersion);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TranslationCache
                (cacheKey, trackId, lyricHash, sourceLanguage, targetLanguage,
                 modelVersion, romanizationVersion, originalLyrics,
                 translatedLyrics, romanizedLyrics, createdAt)
            VALUES
                ($key, $trackId, $hash, $src, $tgt,
                 $modelVer, $romanVer, $original,
                 $translated, $romanized, $created)
            ON CONFLICT(cacheKey) DO UPDATE SET
                translatedLyrics = excluded.translatedLyrics,
                romanizedLyrics = excluded.romanizedLyrics,
                createdAt = excluded.createdAt
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$trackId", trackId);
        cmd.Parameters.AddWithValue("$hash", HashLyrics(lyrics));
        cmd.Parameters.AddWithValue("$src", sourceLanguage);
        cmd.Parameters.AddWithValue("$tgt", targetLanguage);
        cmd.Parameters.AddWithValue("$modelVer", modelVersion);
        cmd.Parameters.AddWithValue("$romanVer", romanizationVersion);
        cmd.Parameters.AddWithValue("$original", JsonSerializer.Serialize(lyrics));
        cmd.Parameters.AddWithValue("$translated", JsonSerializer.Serialize(translated));
        cmd.Parameters.AddWithValue("$romanized", JsonSerializer.Serialize(romanized));
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteAll()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM TranslationCache";
        cmd.ExecuteNonQuery();
    }

    private static string BuildKey(string trackId, List<LyricLine> lyrics, string sourceLanguage,
        string targetLanguage, int modelVersion, int romanizationVersion) =>
        $"{trackId}|{HashLyrics(lyrics)}|{sourceLanguage}|{targetLanguage}|{modelVersion}|{romanizationVersion}";
}
