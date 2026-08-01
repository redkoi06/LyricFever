using LyricFever.Core.Interfaces;

namespace LyricFever.Core.Lyrics;

/// <summary>
/// 歌词主语言启发式检测（Windows 侧无 NLLanguageRecognizer 的替代）：
/// 含假名 → 日文；汉字占比高 → 中文；字母占比高 → 英文。
/// 用于翻译模型选择（en-zh / ja-zh）与"auto"设置。
/// </summary>
public static class LanguageDetector
{
    public static LyricLanguage Detect(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return LyricLanguage.Unknown;

        var kanaCount = 0;
        var hanCount = 0;
        var letterCount = 0;
        var totalChars = 0;

        foreach (var line in lines)
        {
            foreach (var ch in line)
            {
                totalChars++;
                if (ch is >= '぀' and <= 'ヿ') kanaCount++;   // 平假名 + 片假名
                else if (ch is >= '一' and <= '鿿') hanCount++; // 汉字
                else if (char.IsLetter(ch)) letterCount++;
            }
        }

        if (totalChars == 0) return LyricLanguage.Unknown;
        if (kanaCount >= 1 && kanaCount * 100 >= totalChars * 3) return LyricLanguage.Japanese;
        if (hanCount * 100 >= totalChars * 10) return LyricLanguage.Chinese;
        if (letterCount * 100 >= totalChars * 30) return LyricLanguage.English;
        return LyricLanguage.Other;
    }

    public static LyricLanguage Detect(List<LyricLine> lyrics) =>
        Detect(lyrics.Select(l => l.Words).ToList());
}
