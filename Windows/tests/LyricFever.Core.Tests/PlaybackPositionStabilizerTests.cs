using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Tests;

public sealed class PlaybackPositionStabilizerTests
{
    [Fact]
    public void SingleBackwardGlitchIsIgnoredAndDoesNotMoveLyricsBack()
    {
        var stabilizer = new PlaybackPositionStabilizer();

        Assert.Equal(10_200, stabilizer.Observe(10_200, isPlaying: true));
        Assert.Null(stabilizer.Observe(8_900, isPlaying: true));
        Assert.Equal(10_700, stabilizer.Observe(10_700, isPlaying: true));
    }

    [Fact]
    public void RealBackwardSeekIsAcceptedAfterAConfirmingSample()
    {
        var stabilizer = new PlaybackPositionStabilizer();

        Assert.Equal(60_000, stabilizer.Observe(60_000, isPlaying: true));
        Assert.Null(stabilizer.Observe(20_000, isPlaying: true));
        Assert.Equal(20_500, stabilizer.Observe(20_500, isPlaying: true));
    }

    [Fact]
    public void PausedSeekIsAcceptedImmediately()
    {
        var stabilizer = new PlaybackPositionStabilizer();

        Assert.Equal(25_000, stabilizer.Observe(25_000, isPlaying: true));
        Assert.Equal(7_000, stabilizer.Observe(7_000, isPlaying: false));
    }

    [Fact]
    public void LoopOrSeekToBeginningIsAcceptedImmediately()
    {
        var stabilizer = new PlaybackPositionStabilizer();

        Assert.Equal(120_000, stabilizer.Observe(120_000, isPlaying: true));
        Assert.Equal(1_000, stabilizer.Observe(1_000, isPlaying: true));
    }

    [Fact]
    public void SmallBackwardClockNoiseIsIgnored()
    {
        var stabilizer = new PlaybackPositionStabilizer();

        Assert.Equal(10_000, stabilizer.Observe(10_000, isPlaying: true));
        Assert.Null(stabilizer.Observe(9_600, isPlaying: true));
        Assert.Equal(10_100, stabilizer.Observe(10_100, isPlaying: true));
    }

    [Fact]
    public void SmallBackwardSeekNeedsThreeCoherentSamples()
    {
        var stabilizer = new PlaybackPositionStabilizer();

        Assert.Equal(20_000, stabilizer.Observe(20_000, isPlaying: true));
        Assert.Null(stabilizer.Observe(17_000, isPlaying: true));
        Assert.Null(stabilizer.Observe(17_500, isPlaying: true));
        Assert.Equal(18_000, stabilizer.Observe(18_000, isPlaying: true));
    }
}
