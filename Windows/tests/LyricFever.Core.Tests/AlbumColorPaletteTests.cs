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

    [Fact]
    public void SelectDominantColor_DoesNotLetSmallRedDetailBeatBlueArtworkGradient()
    {
        var blueShades = new[]
        {
            new ArtworkColorSample(160, 218, 240),
            new ArtworkColorSample(126, 195, 226),
            new ArtworkColorSample(93, 166, 207),
            new ArtworkColorSample(68, 137, 184),
            new ArtworkColorSample(48, 111, 158),
            new ArtworkColorSample(34, 88, 139),
            new ArtworkColorSample(25, 68, 116),
            new ArtworkColorSample(20, 52, 92)
        };
        var samples = Enumerable.Repeat(new ArtworkColorSample(239, 244, 247), 60)
            .Concat(blueShades.SelectMany(color => Enumerable.Repeat(color, 4)))
            .Concat(Enumerable.Repeat(new ArtworkColorSample(218, 35, 55), 8));

        var result = AlbumColorPalette.SelectDominantColor(samples);

        Assert.True(result.Blue > result.Red * 1.3,
            $"Expected the blue artwork family, got rgb({result.Red}, {result.Green}, {result.Blue}).");
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
