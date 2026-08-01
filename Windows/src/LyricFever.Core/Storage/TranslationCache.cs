using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Storage;

/// <summary>
/// 缓存命中结果。TranslationReady/RomanizationReady 分别标记该产物本次是否真正生成过：
/// 防止"只开翻译时写入的罗马音空数组"在"后续开启罗马音"时被当作有效缓存。
/// </summary>
public sealed record TranslationCacheHit(
    List<string> Translated,
    bool TranslationReady,
    List<string> Romanized,
    bool RomanizationReady);

/// <summary>
/// 翻译产物缓存（用户要求：同一首歌再次播放直接读缓存，不重新调用翻译模型）。
/// 缓存键 = trackID + lyricHash + 源/目标语言 + 模型版本 + 罗马音版本。
/// 每类产物带 ready 标志：失败结果不得写 ready，也不得覆盖已有有效产物。
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
    /// 读缓存。键匹配时返回命中（即使某类产物未 ready 也会返回，ready=false 由上层决定是否重建该部分）。
    /// 行数校验失败视为未命中。
    /// </summary>
    public TranslationCacheHit? Get(
        string trackId, List<LyricLine> lyrics, string sourceLanguage,
        string targetLanguage, int modelVersion, int romanizationVersion)
    {
        var key = BuildKey(trackId, lyrics, sourceLanguage, targetLanguage, modelVersion, romanizationVersion);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT originalLyrics, translatedLyrics, romanizedLyrics,
                   translationReady, romanizationReady
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
            var translationReady = reader.GetInt64(3) != 0;
            var romanizationReady = reader.GetInt64(4) != 0;

            // 完整性校验：行数必须与当前歌词一致
            if (original.Count != lyrics.Count ||
                translated.Count != lyrics.Count ||
                romanized.Count != lyrics.Count)
                return null;

            return new TranslationCacheHit(translated, translationReady, romanized, romanizationReady);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 写缓存。ready 标志明确本类产物是否成功生成：
    /// - 失败结果不得标 ready（下次命中时上层会重建缺失部分）
    /// - 不覆盖已有有效产物：只更新本次实际生成的类别
    /// </summary>
    public void Put(string trackId, List<LyricLine> lyrics, string sourceLanguage,
        string targetLanguage, int modelVersion, int romanizationVersion,
        List<string> translated, bool translationReady,
        List<string> romanized, bool romanizationReady)
    {
        var key = BuildKey(trackId, lyrics, sourceLanguage, targetLanguage, modelVersion, romanizationVersion);

        // 规范化：两个数组必须与歌词等长（缺失行用空字符串占位）
        translated = PadToLength(translated, lyrics.Count);
        romanized = PadToLength(romanized, lyrics.Count);

        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO TranslationCache
                (cacheKey, trackId, lyricHash, sourceLanguage, targetLanguage,
                 modelVersion, romanizationVersion, originalLyrics,
                 translatedLyrics, translationReady, romanizedLyrics, romanizationReady, createdAt)
            VALUES
                ($key, $trackId, $hash, $src, $tgt,
                 $modelVer, $romanVer, $original,
                 $translated, $tReady, $romanized, $rReady, $created)
            ON CONFLICT(cacheKey) DO UPDATE SET
                translatedLyrics = CASE WHEN excluded.translationReady THEN excluded.translatedLyrics ELSE translatedLyrics END,
                translationReady = translationReady OR excluded.translationReady,
                romanizedLyrics = CASE WHEN excluded.romanizationReady THEN excluded.romanizedLyrics ELSE romanizedLyrics END,
                romanizationReady = romanizationReady OR excluded.romanizationReady,
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
        cmd.Parameters.AddWithValue("$tReady", translationReady ? 1 : 0);
        cmd.Parameters.AddWithValue("$romanized", JsonSerializer.Serialize(romanized));
        cmd.Parameters.AddWithValue("$rReady", romanizationReady ? 1 : 0);
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

    private static List<string> PadToLength(List<string> values, int length)
    {
        if (values.Count == length) return values;
        var padded = new List<string>(length);
        padded.AddRange(values);
        while (padded.Count < length) padded.Add("");
        if (padded.Count > length) padded = padded.GetRange(0, length);
        return padded;
    }

    private static string BuildKey(string trackId, List<LyricLine> lyrics, string sourceLanguage,
        string targetLanguage, int modelVersion, int romanizationVersion) =>
        $"{trackId}|{HashLyrics(lyrics)}|{sourceLanguage}|{targetLanguage}|{modelVersion}|{romanizationVersion}";
}
