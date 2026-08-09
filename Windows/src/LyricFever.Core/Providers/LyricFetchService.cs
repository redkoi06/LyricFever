using LyricFever.Core.Lyrics;
using LyricFever.Core.Storage;

namespace LyricFever.Core.Providers;

/// <summary>歌词获取结果。</summary>
public sealed class LyricFetchResult
{
    public List<LyricLine> Lyrics { get; init; } = new();
    public int ColorData { get; init; } = -1;
    public bool FromCache { get; init; }
    public string? Language { get; init; }
}

/// <summary>
/// 歌词获取编排（对应 macOS ViewModel 的 fetchLyrics 链路）：
/// 缓存 → LRCLIB → NetEase 兜底链；成功后写缓存与颜色缓存。
/// </summary>
public sealed class LyricFetchService
{
    private readonly LyricsRepository _repo;
    private readonly IReadOnlyList<ILyricProvider> _providers;

    public LyricFetchService(LyricsRepository repo, IEnumerable<ILyricProvider> providers)
    {
        _repo = repo;
        _providers = providers.ToList();
    }

    public async Task<LyricFetchResult> FetchAsync(
        string trackId, string trackName, string? artistName, string? albumName,
        CancellationToken cancellationToken = default)
    {
        // 1. 离线缓存优先
        try
        {
            var cached = _repo.GetLyrics(trackId);
            if (cached != null)
            {
                return new LyricFetchResult
                {
                    Lyrics = cached,
                    ColorData = _repo.GetColor(trackId) ?? -1,
                    FromCache = true
                };
            }
        }
        catch (LyricsRepositoryCorruptEntryException)
        {
            // 损坏条目已删除，继续走网络
        }

        // 2. 依次尝试各歌词源
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.FetchNetworkLyricsAsync(
                    trackName, trackId, artistName, albumName, cancellationToken);
                if (result.Lyrics.Count == 0) continue;

                _repo.Upsert(result.Lyrics, trackId, trackName);
                if (result.ColorData >= 0)
                {
                    _repo.SetColor(trackId, result.ColorData);
                }

                return new LyricFetchResult
                {
                    Lyrics = result.Lyrics,
                    ColorData = result.ColorData,
                    Language = null // provider 未提供语言时由上层检测
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A provider's HttpClient timeout must not cancel later providers. Only an
                // explicitly cancelled caller token is allowed to abort the complete chain.
                System.Diagnostics.Debug.WriteLine(
                    $"[LyricFever][Fetch] {provider.ProviderName} timed out for {trackName}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LyricFever][Fetch] {provider.ProviderName} failed for {trackName}: {ex.Message}");
                // 单个 provider 失败不阻断兜底链
            }
        }

        return new LyricFetchResult();
    }
}
