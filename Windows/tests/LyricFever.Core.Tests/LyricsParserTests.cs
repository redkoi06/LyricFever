using LyricFever.Core.Lyrics;
using Xunit;

namespace LyricFever.Core.Tests;

/// <summary>对照 macOS LyricsParserTests 的 4 个用例逐条移植。</summary>
public class LyricsParserTests
{
    [Fact]
    public void ParsesMultipleTimestampsWithSameWords()
    {
        var lyrics = new LyricsParser("[00:01.50][00:02.75]Hello").Lyrics;
        Assert.Equal(new[] { 1500.0, 2750.0 }, lyrics.Select(l => l.StartTimeInMs));
        Assert.Equal(new[] { "Hello", "Hello" }, lyrics.Select(l => l.Words));
    }

    [Fact]
    public void OffsetUsesMilliseconds()
    {
        var lyrics = new LyricsParser("[offset:500]\n[00:01.00]Hello").Lyrics;
        Assert.Equal(1500.0, lyrics[0].StartTimeInMs);
    }

    [Fact]
    public void NegativeOffsetDoesNotCreateNegativeTimestamp()
    {
        var lyrics = new LyricsParser("[offset:-2000]\n[00:01.00]Hello").Lyrics;
        Assert.Equal(0.0, lyrics[0].StartTimeInMs);
    }

    [Fact]
    public void MalformedTimestampIsIgnored()
    {
        Assert.Empty(new LyricsParser("[invalid]Hello").Lyrics);
        Assert.Empty(new LyricsParser("[00:nope]Hello").Lyrics);
    }
}
