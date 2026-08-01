namespace LyricFever.Core.Interfaces;

/// <summary>
/// 平台无关罗马音接口。
/// Windows 实现：KawazuRomanizationProvider（Kawazu + LibNMeCab + IPADic）。
/// macOS 实现：现有 RomanizerService（Mecab-Swift）。
/// 与翻译相互独立：翻译失败时罗马音仍可显示。
/// </summary>
public interface IRomanizationProvider
{
    /// <summary>批量生成日文罗马音。返回与原数组等长结果，无注音行返回空字符串。</summary>
    Task<IReadOnlyList<string>> RomanizeAsync(
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken = default);
}
