namespace LyricFever.Core.Interfaces;

/// <summary>歌词源语言（macOS 版用 NLLanguageRecognizer 自动判定，Windows 侧按设置/歌词内容判定）。</summary>
public enum LyricLanguage
{
    Unknown,
    English,
    Japanese,
    Chinese,
    Other
}

/// <summary>单条翻译请求（与 macOS 版 TranslationSession.Request 对应）。</summary>
public sealed record TranslationRequest(string LineId, string Text, LyricLanguage SourceLanguage);

/// <summary>单条翻译结果（LineId 用于与原始歌词行对齐）。</summary>
public sealed record TranslationResponse(string LineId, string TranslatedText, bool IsSuccess);

/// <summary>
/// 平台无关翻译接口。
/// Windows 实现：CTranslate2TranslationProvider（OPUS-MT en-zh / ja-zh INT8）。
/// macOS 实现：包装 Apple TranslationSession。
/// 约定：批量提交、按 LineId 对齐、源语言已知（不自动检测）。
/// </summary>
public interface ITranslationProvider
{
    /// <summary>批量翻译。实现方负责按 LineId 恢复顺序；调用方负责任务取消与切歌校验。</summary>
    Task<IReadOnlyList<TranslationResponse>> TranslateAsync(
        IReadOnlyList<TranslationRequest> requests,
        string targetLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>当前是否已加载模型（供 UI 显示加载状态）。</summary>
    bool IsLoaded { get; }

    /// <summary>加载/卸载模型。卸载后释放内存。</summary>
    Task LoadAsync(LyricLanguage sourceLanguage, CancellationToken cancellationToken = default);
    Task UnloadAsync();
}
