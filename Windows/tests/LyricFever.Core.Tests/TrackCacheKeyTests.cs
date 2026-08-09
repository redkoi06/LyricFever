using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Tests;

public class TrackCacheKeyTests
{
    [Fact]
    public void EquivalentMetadataProducesSameKey()
    {
        var first = TrackCacheKey.Create("Ｃａｆé!", "Björk");
        var second = TrackCacheKey.Create("cafe", "BJORK");

        Assert.Equal(first, second);
        Assert.StartsWith("metadata:", first);
    }

    [Theory]
    [InlineData("Song", "Artist", "Other Song", "Artist")]
    [InlineData("Song", "Artist", "Song", "Other Artist")]
    [InlineData("Song (Live)", "Artist", "Song", "Artist")]
    public void MateriallyDifferentMetadataProducesDifferentKey(
        string title, string artist, string otherTitle, string otherArtist)
    {
        Assert.NotEqual(
            TrackCacheKey.Create(title, artist),
            TrackCacheKey.Create(otherTitle, otherArtist));
    }

    [Fact]
    public void EmptyTitleIsRejected()
    {
        Assert.Throws<ArgumentException>(() => TrackCacheKey.Create("   ", "Artist"));
    }

    [Fact]
    public void SymbolOnlyTitleStillProducesAKey()
    {
        Assert.StartsWith("metadata:", TrackCacheKey.Create("♪", "Artist"));
    }
}
