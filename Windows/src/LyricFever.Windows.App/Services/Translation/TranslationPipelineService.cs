using LyricFever.Core.Interfaces;
using LyricFever.Core.Lyrics;
using LyricFever.Core.Providers;
using LyricFever.Core.Storage;

namespace LyricFever.Windows.App.Services.Translation;

/// <summary>
/// 人工译词与罗马音管线：
/// - 优先读取版本化缓存；
/// - 日语和英语只请求经过曲目校验的平台人工译词，不再调用本地机器翻译；
/// - 平台没有可靠译词时返回空占位，界面隐藏译文，绝不猜测；
/// - 罗马音独立生成，任一侧失败都不阻断另一侧。
/// </summary>
public sealed class TranslationPipelineService : IDisposable
{
    private readonly IRomanizationProvider _romanizer;
    private readonly IHumanTranslationProvider _humanTranslator;
    private readonly TranslationCache _cache;

    private const string TargetLanguage = "zh";

    public TranslationPipelineService(IRomanizationProvider romanizer,
        TranslationCache cache, IHumanTranslationProvider humanTranslator)
    {
        _romanizer = romanizer;
        _cache = cache;
        _humanTranslator = humanTranslator;
    }

    public IReadOnlyList<string>? TryGetCachedHumanTranslation(
        string trackId, List<LyricLine> lyrics, LyricLanguage language)
    {
        if (language is not (LyricLanguage.English or LyricLanguage.Japanese)) return null;
        var sourceCode = language == LyricLanguage.Japanese ? "ja" : "en";
        var hit = _cache.Get(trackId, lyrics, sourceCode, TargetLanguage,
            AppSettings.Current.TranslationModelVersion, AppSettings.Current.RomanizationVersion);
        return hit?.TranslationReady == true
            ? HumanTranslationContinuity.ReusePreviousForMissingNextLine(
                lyrics, Pad(hit.Translated, lyrics.Count))
            : null;
    }

    public async Task<(List<string> Translated, List<string> Romanized)> ProcessAsync(
        string trackId, List<LyricLine> lyrics, LyricLanguage language,
        string trackName, string? artistName, string? albumName,
        bool translateEnabled, bool romanizationEnabled,
        Func<bool> isCurrent, CancellationToken cancellationToken = default,
        IReadOnlyList<string>? preferredHumanTranslation = null)
    {
        var count = lyrics.Count;
        var needTranslation = translateEnabled && language is LyricLanguage.English or LyricLanguage.Japanese;
        var needRomanization = romanizationEnabled && language == LyricLanguage.Japanese;

        var translated = new List<string>(count);
        var romanized = new List<string>(count);
        var preferredTranslationReady = needTranslation &&
                                        preferredHumanTranslation?.Count == count;
        var translationReady = preferredTranslationReady;
        var romanizationReady = false;

        if (preferredTranslationReady)
            translated = preferredHumanTranslation!.Select(text => text?.Trim() ?? "").ToList();

        if (needTranslation || needRomanization)
        {
            var sourceCode = language == LyricLanguage.Japanese ? "ja" : "en";
            var hit = _cache.Get(trackId, lyrics, sourceCode, TargetLanguage,
                AppSettings.Current.TranslationModelVersion, AppSettings.Current.RomanizationVersion);
            if (hit != null)
            {
                if (!translationReady) translated = hit.Translated;
                romanized = hit.Romanized;
                translationReady = translationReady || hit.TranslationReady;
                romanizationReady = hit.RomanizationReady;
            }
        }

        var translateNeeded = needTranslation && !translationReady;
        var romanizeNeeded = needRomanization && !romanizationReady;

        var translateTask = translateNeeded
            ? FetchHumanTranslationAsync(trackName, artistName, albumName, lyrics, isCurrent, cancellationToken)
            : Task.FromResult((Data: translated, Ready: translationReady));
        var romanizeTask = romanizeNeeded
            ? _romanizer.RomanizeAsync(lyrics.Select(line => line.Words).ToList(), cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>(romanized);

        await Task.WhenAll(translateTask, romanizeTask);
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException();

        if (translateNeeded)
        {
            translated = translateTask.Result.Data;
            translationReady = translateTask.Result.Ready;
        }
        if (romanizeNeeded)
        {
            romanized = Pad(romanizeTask.Result.ToList(), count);
            romanizationReady = true;
        }

        if (translationReady)
            translated = HumanTranslationContinuity.ReusePreviousForMissingNextLine(
                lyrics, Pad(translated, count));

        if ((translateNeeded || romanizeNeeded || preferredTranslationReady) &&
            (translationReady || romanizationReady))
        {
            var sourceCode = language == LyricLanguage.Japanese ? "ja" : "en";
            _cache.Put(trackId, lyrics, sourceCode, TargetLanguage,
                AppSettings.Current.TranslationModelVersion, AppSettings.Current.RomanizationVersion,
                translated, translationReady, romanized, romanizationReady);
        }

        return (translated, romanized);
    }

    private async Task<(List<string> Data, bool Ready)> FetchHumanTranslationAsync(
        string trackName, string? artistName, string? albumName, List<LyricLine> lyrics,
        Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        try
        {
            var human = await _humanTranslator.FetchTranslationAsync(
                trackName, artistName, albumName, lyrics, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCurrent()) throw new OperationCanceledException();

            if (human == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LyricFever][Translate] no reliable human translation for {trackName}");
                return (Enumerable.Repeat("", lyrics.Count).ToList(), false);
            }

            var result = Pad(human.Select(text => text?.Trim() ?? "").ToList(), lyrics.Count);
            System.Diagnostics.Debug.WriteLine(
                $"[LyricFever][Translate] human coverage={result.Count(text => !string.IsNullOrWhiteSpace(text))}/{lyrics.Count}");
            // 平台译词允许个别器乐/重复行留空；只要提供方通过覆盖率门槛，就作为完整人工产物缓存。
            return (result, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Translate] human source failed: {ex.Message}");
            return (Enumerable.Repeat("", lyrics.Count).ToList(), false);
        }
    }

    private static List<string> Pad(List<string> values, int length)
    {
        while (values.Count < length) values.Add("");
        if (values.Count > length) values = values.GetRange(0, length);
        return values;
    }

    public void Dispose()
    {
        // 当前管线不持有本地模型或非托管资源。
    }
}
