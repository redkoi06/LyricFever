using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Tests;

public sealed class HumanTranslationContinuityTests
{
    [Fact]
    public void ReusePreviousForMissingNextLine_RepairsFuwaFuwaTimeSplit()
    {
        var lyrics = new List<LyricLine>
        {
            new(72_510, "ふとした仕草に今日もハートZUKI★ZUKI"),
            new(77_800, "さりげな笑顔を深読みしぎて Over heat!"),
            new(82_520, "いつか目にしたキミのマジ顔")
        };
        var complete = "意想不到的动作今天也好喜欢~好喜欢 太过深入地解读着若无其事的笑容over heat！";

        var result = HumanTranslationContinuity.ReusePreviousForMissingNextLine(
            lyrics, [complete, "", "你的认真表情 不知何时映入眼帘"]);

        Assert.Equal(complete, result[0]);
        Assert.Equal(complete, result[1]);
        Assert.Equal("你的认真表情 不知何时映入眼帘", result[2]);
    }

    [Fact]
    public void ReusePreviousForMissingNextLine_DoesNotCascadeOrOverwrite()
    {
        var lyrics = new List<LyricLine>
        {
            new(10_000, "first"),
            new(12_000, "second"),
            new(14_000, "third"),
            new(16_000, "fourth")
        };

        var result = HumanTranslationContinuity.ReusePreviousForMissingNextLine(
            lyrics, ["完整译文", "", "", "已有译文"]);

        Assert.Equal("完整译文", result[1]);
        Assert.Equal("", result[2]);
        Assert.Equal("已有译文", result[3]);
    }

    [Fact]
    public void ReusePreviousForMissingNextLine_SkipsInstrumentalAndLongGaps()
    {
        var lyrics = new List<LyricLine>
        {
            new(10_000, "source"),
            new(12_000, "♪"),
            new(30_000, "next verse")
        };

        var result = HumanTranslationContinuity.ReusePreviousForMissingNextLine(
            lyrics, ["译文", "", ""]);

        Assert.Equal("", result[1]);
        Assert.Equal("", result[2]);
    }
}
