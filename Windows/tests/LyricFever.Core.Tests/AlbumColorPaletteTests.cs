using LyricFever.Core.Appearance;

namespace LyricFever.Core.Tests;

public sealed class AlbumColorPaletteTests
{
    [Fact]
    public void SelectBackground_PrefersDominantColorAreaOverIsolatedVividPixel()
    {
        var samples = Enumerable.Repeat(new ArtworkColorSample(178, 164, 145), 99)
            .Append(new ArtworkColorSample(255, 0, 220));

        var result = AlbumColorPalette.SelectBackground(samples);

        Assert.InRange(Math.Abs(result.Red - result.Green), 0, 35);
        Assert.InRange(Math.Abs(result.Green - result.Blue), 0, 35);
    }

    [Fact]
    public void SelectBackground_PromotesSubstantialColorfulAlbumAccent()
    {
        var samples = Enumerable.Repeat(new ArtworkColorSample(125, 125, 128), 65)
            .Concat(Enumerable.Repeat(new ArtworkColorSample(35, 92, 205), 35));

        var result = AlbumColorPalette.SelectBackground(samples);

        Assert.True(result.Blue > result.Red * 1.8);
        Assert.True(result.Blue > result.Green * 1.25);
    }

    [Theory]
    [InlineData(245, 220, 40)]
    [InlineData(245, 80, 80)]
    [InlineData(30, 230, 100)]
    [InlineData(20, 45, 235)]
    public void NormalizeForWhiteText_MeetsContrastTarget(byte red, byte green, byte blue)
    {
        var result = AlbumColorPalette.NormalizeForWhiteText(new ArtworkColor(red, green, blue));

        Assert.True(AlbumColorPalette.ContrastWithWhite(result) >= AlbumColorPalette.MinimumWhiteContrast);
    }

    [Fact]
    public void NormalizeForWhiteText_AccountsForTranslucentCardOverWhiteDesktop()
    {
        const double opacity = 0.82;
        var result = AlbumColorPalette.NormalizeForWhiteText(new ArtworkColor(232, 174, 72), opacity);

        Assert.True(AlbumColorPalette.ContrastWithWhite(result, opacity) >=
                    AlbumColorPalette.MinimumWhiteContrast);
    }

    [Fact]
    public void NormalizeForWhiteText_LiftsVeryDarkColorWithoutLosingHue()
    {
        var result = AlbumColorPalette.NormalizeForWhiteText(new ArtworkColor(8, 20, 45));

        Assert.True(result.Blue > 70);
        Assert.True(result.Blue > result.Green);
        Assert.True(result.Green > result.Red);
    }
}
