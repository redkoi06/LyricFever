using LyricFever.Core.Interfaces;
using LyricFever.Core.Lyrics;
using Xunit;

namespace LyricFever.Core.Tests;

public class LanguageDetectorTests
{
    [Fact]
    public void JapaneseLyricsDetected()
    {
        var lang = LanguageDetector.Detect(new List<string>
        {
            "君の声が聞こえる",
            "涙があふれる",
            "夜空の星を見上げて"
        });
        Assert.Equal(LyricLanguage.Japanese, lang);
    }

    [Fact]
    public void EnglishLyricsDetected()
    {
        var lang = LanguageDetector.Detect(new List<string>
        {
            "I hear your voice",
            "Tears are falling",
            "Looking up at the stars"
        });
        Assert.Equal(LyricLanguage.English, lang);
    }

    [Fact]
    public void ChineseLyricsDetected()
    {
        var lang = LanguageDetector.Detect(new List<string>
        {
            "我听得到你的声音",
            "眼泪在流淌",
            "仰望着星空"
        });
        Assert.Equal(LyricLanguage.Chinese, lang);
    }

    [Fact]
    public void EmptyLyricsUnknown()
    {
        Assert.Equal(LyricLanguage.Unknown, LanguageDetector.Detect(new List<string>()));
    }
}
