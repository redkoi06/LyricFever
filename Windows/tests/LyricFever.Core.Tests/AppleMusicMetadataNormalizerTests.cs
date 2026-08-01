using LyricFever.Core.Lyrics;

namespace LyricFever.Core.Tests;

public sealed class AppleMusicMetadataNormalizerTests
{
    [Fact]
    public void Normalize_SplitsCombinedArtistAndAlbum()
    {
        var result = AppleMusicMetadataNormalizer.Normalize(
            "Luna Haruna — glory days (Movie Version) - Single",
            "Luna Haruna — glory days (Movie Version) - Single");

        Assert.Equal("Luna Haruna", result.Artist);
        Assert.Equal("glory days (Movie Version) - Single", result.Album);
    }

    [Fact]
    public void Normalize_LeavesOrdinaryArtistUntouched()
    {
        var result = AppleMusicMetadataNormalizer.Normalize("Aimer", "Walpurgis");

        Assert.Equal("Aimer", result.Artist);
        Assert.Equal("Walpurgis", result.Album);
    }

    [Fact]
    public void Normalize_DoesNotSplitOrdinaryHyphens()
    {
        var result = AppleMusicMetadataNormalizer.Normalize("SawanoHiroyuki[nZk]:Tielle", "A/Z - Single");

        Assert.Equal("SawanoHiroyuki[nZk]:Tielle", result.Artist);
        Assert.Equal("A/Z - Single", result.Album);
    }
}
