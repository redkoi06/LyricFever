using System.Security.Cryptography;
using System.Text;

namespace LyricFever.Core.Lyrics;

/// <summary>
/// 为 Apple Music 元数据生成稳定的本地缓存键。
/// 歌名通常已包含 Live、Remix 等版本信息；专辑字段可能在 SMTC 首次通知后才补齐，
/// 因此只使用规范化后的歌名与歌手，避免同一首歌在元数据刷新时切换缓存身份。
/// </summary>
public static class TrackCacheKey
{
    public static string Create(string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Track title must not be empty.", nameof(title));

        var identity = $"{NormalizeComponent(artist)}\u001f{NormalizeComponent(title)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"metadata:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string NormalizeComponent(string value)
    {
        var searchable = MetadataMatcher.Normalized(value);
        return searchable.Length > 0
            ? searchable
            : value.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
    }
}
