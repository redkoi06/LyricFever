using LyricFever.Core.Lyrics;
using Xunit;

namespace LyricFever.Core.Tests;

public class LyricSyncEngineTests
{
    private static LyricSyncEngine MakeEngine()
    {
        var engine = new LyricSyncEngine
        {
            Lyrics = new List<LyricLine>
            {
                new(1000, "First"),
                new(5000, "Second"),
                new(9000, "Third")
            }
        };
        return engine;
    }

    [Fact]
    public void PositionBeforeFirstLineReturnsNull()
    {
        var engine = MakeEngine();
        Assert.Null(engine.IndexForPosition(0));
        Assert.Null(engine.IndexForPosition(999));
    }

    [Fact]
    public void PositionFindsLatestLine()
    {
        var engine = MakeEngine();
        Assert.Equal(0, engine.IndexForPosition(1000));
        Assert.Equal(0, engine.IndexForPosition(4999));
        Assert.Equal(1, engine.IndexForPosition(5000));
        Assert.Equal(2, engine.IndexForPosition(9000));
        Assert.Equal(2, engine.IndexForPosition(999999));
    }

    [Fact]
    public void EmptyLyricsReturnNull()
    {
        var engine = new LyricSyncEngine { Lyrics = new List<LyricLine>() };
        Assert.Null(engine.IndexForPosition(1000));
    }

    [Fact]
    public void InvalidPositionReturnsNull()
    {
        var engine = MakeEngine();
        Assert.Null(engine.IndexForPosition(double.NaN));
        Assert.Null(engine.IndexForPosition(double.PositiveInfinity));
    }

    [Fact]
    public void NewLyricsResetCurrentIndex()
    {
        var engine = MakeEngine();
        engine.UpdatePosition(6000);
        Assert.Equal(1, engine.CurrentIndex);

        engine.Lyrics = new List<LyricLine> { new(0, "New") };
        Assert.Null(engine.CurrentIndex);
    }

    [Fact]
    public void UpdatePositionReportsIndexChange()
    {
        var engine = MakeEngine();
        Assert.True(engine.UpdatePosition(2000));
        Assert.False(engine.UpdatePosition(2000)); // 同索引不重复通知
        Assert.True(engine.UpdatePosition(6000));
        Assert.Equal(1, engine.CurrentIndex);
    }
}
