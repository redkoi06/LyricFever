using LyricFever.Core.Interfaces;
using LyricFever.Core.Lyrics;
using LyricFever.Core.Storage;
using LyricFever.Windows.App.Services;

namespace LyricFever.Windows.App.Services.Translation;

/// <summary>
/// 翻译/罗马音管线（对应用户定案与执行指挥书 P0-B）：
/// - 产物缓存优先（命中不加载模型），ready 标志区分"真正生成过"与"空占位"
/// - 缺哪一类产物只重建哪一类（先只开翻译、后开罗马音时只补罗马音）
/// - 整首批量翻译（20~60 行一次提交）
/// - 翻译与罗马音并行（相互独立，翻译失败仍显示罗马音）
/// - 模型按需加载、语言切换换模型、空闲自动卸载
/// - 任务版本校验（切歌丢弃旧结果）；所有 native 调用包 Task.Run 不阻塞 UI
/// </summary>
public sealed class TranslationPipelineService : IDisposable
{
    private readonly CTranslate2TranslationProvider _translator;
    private readonly IRomanizationProvider _romanizer;
    private readonly TranslationCache _cache;
    private readonly System.Threading.Timer _unloadTimer;
    private readonly SemaphoreSlim _translateGate = new(1, 1);

    private const string TargetLanguage = "zh";
    private static readonly TimeSpan UnloadIdleTimeout = TimeSpan.FromMinutes(5);

    public TranslationPipelineService(CTranslate2TranslationProvider translator,
        IRomanizationProvider romanizer, TranslationCache cache)
    {
        _translator = translator;
        _romanizer = romanizer;
        _cache = cache;
        _unloadTimer = new System.Threading.Timer(_ => UnloadModel(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// 处理一首歌的翻译与罗马音。任务版本校验：处理过程中 isCurrent() 返回 false 时丢弃结果。
    /// 返回 (译文, 罗马音)，均为歌词等长数组；未请求的类别返回空数组。
    /// </summary>
    public async Task<(List<string> Translated, List<string> Romanized)> ProcessAsync(
        string trackId, List<LyricLine> lyrics, LyricLanguage language,
        bool translateEnabled, bool romanizationEnabled,
        Func<bool> isCurrent, CancellationToken cancellationToken = default)
    {
        var count = lyrics.Count;
        var needTranslation = translateEnabled && language is LyricLanguage.English or LyricLanguage.Japanese;
        var needRomanization = romanizationEnabled && language == LyricLanguage.Japanese;

        // 1. 产物缓存优先（按 ready 判断各类是否可用；缺哪类补哪类）
        var translated = new List<string>(count);
        var romanized = new List<string>(count);
        var translationReady = false;
        var romanizationReady = false;

        if (needTranslation || needRomanization)
        {
            var sourceCode = language == LyricLanguage.Japanese ? "ja" : "en";
            var hit = _cache.Get(trackId, lyrics, sourceCode, TargetLanguage,
                AppSettings.Current.TranslationModelVersion, AppSettings.Current.RomanizationVersion);
            if (hit != null)
            {
                translated = hit.Translated;
                romanized = hit.Romanized;
                translationReady = hit.TranslationReady;
                romanizationReady = hit.RomanizationReady;
            }
        }

        var translateNeeded = needTranslation && !translationReady;
        var romanizeNeeded = needRomanization && !romanizationReady;

        // 2. 并行执行缺失部分（相互独立）
        var translateTask = translateNeeded
            ? TranslateAsync(lyrics, language, isCurrent, cancellationToken)
            : Task.FromResult((Data: translated, Ready: translationReady));

        var romanizeTask = romanizeNeeded
            ? _romanizer.RomanizeAsync(lyrics.Select(l => l.Words).ToList(), cancellationToken)
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
            romanizationReady = true; // 罗马音按行容错，失败行占位仍视为完成
        }

        // 3. 写回缓存：只更新本次实际生成且成功的类别；失败不覆盖有效产物
        if ((translateNeeded || romanizeNeeded) && (translationReady || romanizationReady))
        {
            var sourceCode = language == LyricLanguage.Japanese ? "ja" : "en";
            _cache.Put(trackId, lyrics, sourceCode, TargetLanguage,
                AppSettings.Current.TranslationModelVersion, AppSettings.Current.RomanizationVersion,
                translated, translationReady, romanized, romanizationReady);
        }

        return (translated, romanized);
    }

    private async Task<(List<string> Data, bool Ready)> TranslateAsync(
        List<LyricLine> lyrics, LyricLanguage language,
        Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        try
        {
            // 模型加载与推理串行化；native 调用在后台线程执行
            await _translateGate.WaitAsync(cancellationToken);
            try
            {
                await _translator.LoadAsync(language, cancellationToken);
                ArmUnloadTimer();

                var requests = lyrics.Select(l => new TranslationRequest(l.LineId, l.Words, language)).ToList();
                var responses = await _translator.TranslateAsync(requests, TargetLanguage, cancellationToken);

                // 按行 ID 对齐恢复顺序
                var byId = responses.ToDictionary(r => r.LineId);
                var result = new List<string>(lyrics.Count);
                var ready = true;
                foreach (var line in lyrics)
                {
                    var text = byId.TryGetValue(line.LineId, out var r) ? r.TranslatedText : "";
                    result.Add(text);
                    if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(line.Words))
                        ready = false;
                }
                return (result, ready);
            }
            finally
            {
                _translateGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LyricFever][Translate] failed: {ex.Message}");
            if (!isCurrent()) throw new OperationCanceledException();
            // 翻译失败：返回空译文且 not ready（不污染缓存）
            return (new List<string>(lyrics.Count), false);
        }
    }

    private void ArmUnloadTimer() =>
        _unloadTimer.Change(UnloadIdleTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>空闲卸载：与翻译调用互斥，避免卸载定时器打断推理。</summary>
    private void UnloadModel()
    {
        // 不等待（定时器回调），尽力而为；推理中被占用时本次跳过，下轮空闲再卸
        if (_translateGate.Wait(0))
        {
            try
            {
                _translator.Unload();
            }
            finally
            {
                _translateGate.Release();
            }
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
        _unloadTimer.Dispose();
        _translateGate.Dispose();
        _translator.Unload();
    }
}
