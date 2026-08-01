using System.IO;
using LyricFever.Core.Interfaces;

namespace LyricFever.Windows.App.Services.Translation;

/// <summary>
/// 日语罗马音实现（用户定案：Kawazu + LibNMeCab + IPADic，与 macOS 版 Mecab 同词库）。
/// 输出与原文等长，无注音行返回空字符串（与 macOS RomanizerService 约定一致）。
/// KawazuConverter 实例复用（构造开销大），转换在后台线程执行。
/// </summary>
public sealed class KawazuRomanizationProvider : IRomanizationProvider
{
    private readonly object _lock = new();
    private readonly Kawazu.KawazuConverter _converter;

    public KawazuRomanizationProvider()
    {
        var dicDir = Path.Combine(AppContext.BaseDirectory, "IpaDic");
        _converter = new Kawazu.KawazuConverter(dicDir);
    }

    public Task<IReadOnlyList<string>> RomanizeAsync(IReadOnlyList<string> lines,
        CancellationToken cancellationToken = default)
    {
        // 逐行转换在后台线程执行，避免阻塞 UI；converter 非线程安全，串行化
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var result = new List<string>(lines.Count);
            lock (_lock)
            {
                foreach (var line in lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.Add(ConvertLine(line));
                }
            }
            return result;
        }, cancellationToken);
    }

    private string ConvertLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        try
        {
            return _converter.Convert(line, Kawazu.To.Romaji, Kawazu.Mode.Spaced,
                Kawazu.RomajiSystem.Hepburn, "", "").GetAwaiter().GetResult() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
