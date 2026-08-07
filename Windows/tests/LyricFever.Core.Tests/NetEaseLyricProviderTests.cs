using LyricFever.Core.Lyrics;
using LyricFever.Core.Providers;

namespace LyricFever.Core.Tests;

public sealed class NetEaseLyricProviderTests
{
    [Fact]
    public void AlignTranslations_MergesPlatformSplitLinesIntoReferenceInterval()
    {
        var reference = new List<LyricLine>
        {
            new(45_690, "二人だけのDream Timeください"),
            new(53_240, "お気に入りのうさちゃん抱いて今夜もオヤスミ"),
            new(64_000, "ふわふわタイム")
        };
        var source = new List<LyricLine>
        {
            new(45_690, "二人だけのDream Timeください"),
            new(53_390, "お気に入りのうさちゃん抱いで"),
            new(58_820, "今夜もお休み"),
            new(64_000, "ふわふわタイム")
        };
        var translated = new List<LyricLine>
        {
            new(45_690, "请给我们两个人的Dream Time"),
            new(53_390, "抱着心爱的小兔子"),
            new(58_820, "今晚也进入梦中"),
            new(64_000, "轻飘飘时间")
        };

        var result = NetEaseLyricProvider.AlignTranslations(reference, source, translated);

        Assert.Equal(reference.Count, result.Count);
        Assert.Equal("抱着心爱的小兔子 今晚也进入梦中", result[1]);
    }

    [Fact]
    public void AlignTranslations_ReusesCompleteTranslationForSplitReferenceLines()
    {
        var reference = new List<LyricLine>
        {
            new(10_000, "もう誰も届かない"),
            new(12_500, "深くまで降りてきた"),
            new(16_000, "暗闇の底へ")
        };
        var source = new List<LyricLine>
        {
            new(10_000, "もう誰も届かない 深くまで降りてきた"),
            new(16_000, "暗闇の底へ")
        };
        var translated = new List<LyricLine>
        {
            new(10_000, "无论谁都已经传达不到 降至了这般深邃之处"),
            new(16_000, "沉入黑暗深处")
        };

        var result = NetEaseLyricProvider.AlignTranslations(reference, source, translated);

        Assert.Equal(translated[0].Words, result[0]);
        Assert.Equal(translated[0].Words, result[1]);
        Assert.Equal(translated[1].Words, result[2]);
    }

    [Fact]
    public void AlignTranslations_ReusesPreviousTranslationWhenProviderTranslationSpansTwoSourceLines()
    {
        var lyrics = new List<LyricLine>
        {
            new(10_000, "もう誰も届かない"),
            new(12_500, "深くまで降りてきた"),
            new(16_000, "暗闇の底へ")
        };
        var translated = new List<LyricLine>
        {
            new(10_000, "无论谁都已经传达不到 降至了这般深邃之处"),
            new(16_000, "沉入黑暗深处")
        };

        var result = NetEaseLyricProvider.AlignTranslations(lyrics, lyrics, translated);

        Assert.Equal(translated[0].Words, result[0]);
        Assert.Equal(translated[0].Words, result[1]);
        Assert.Equal(translated[1].Words, result[2]);
    }

    [Fact]
    public void SelectBestSong_AcceptsDecoratedAppleMusicArtistButRejectsWrongCover()
    {
        var correct = Song("ふわふわ時間", "桜高軽音部", "ふわふわ時間", 1317256457);
        var wrongCover = Song("ふわふわ時間", "放課後カバー部", "Anime Covers", 2);

        var result = NetEaseLyricProvider.SelectBestSong(
            [wrongCover, correct],
            "ふわふわ時間",
            "桜高軽音部[平沢唯・秋山澪・田井中律・琴吹紬]",
            "ふわふわ時間");

        Assert.Same(correct, result);
    }

    [Fact]
    public void SelectBestSong_RejectsUnrelatedSameTitle()
    {
        var unrelated = Song("Home", "Another Artist", "Another Album", 9);

        var result = NetEaseLyricProvider.SelectBestSong(
            [unrelated], "Home", "The Original Artist", "Original Album");

        Assert.Null(result);
    }

    [Fact]
    public void HumanTranslationQualityGate_RequiresSixtyPercentCoverage()
    {
        var reference = Enumerable.Range(0, 10)
            .Select(index => new LyricLine(index * 1000, $"line {index}"))
            .ToList();

        Assert.False(NetEaseLyricProvider.HasSufficientCoverage(
            reference, ["一", "二", "三", "四", "五", "", "", "", "", ""]));
        Assert.True(NetEaseLyricProvider.HasSufficientCoverage(
            reference, ["一", "二", "三", "四", "五", "六", "", "", "", ""]));
    }

    [Fact]
    [Trait("Category", "Network")]
    public async Task OfficialEndpoint_FuwaFuwaTime_ReturnsAcceptableHumanTranslation()
    {
        if (Environment.GetEnvironmentVariable("LYRICFEVER_RUN_NETWORK_TEST") != "1") return;

        var reference = new List<LyricLine>
        {
            new(45_690, "二人だけのDream Timeください"),
            new(53_240, "お気に入りのうさちゃん抱いて今夜もオヤスミ"),
            new(64_000, "ふわふわタイム")
        };
        var provider = new NetEaseLyricProvider();

        var result = await provider.FetchTranslationAsync(
            "ふわふわ時間",
            "桜高軽音部[平沢唯・秋山澪・田井中律・琴吹紬]",
            "ふわふわ時間",
            reference);

        Assert.NotNull(result);
        Assert.Contains("兔子", result![1]);
        Assert.Contains("今晚", result[1]);
        Assert.DoesNotContain("鸡巴", result[1]);

        var bundle = await provider.FetchHumanLyricBundleAsync(
            "ふわふわ時間",
            "桜高軽音部[平沢唯・秋山澪・田井中律・琴吹紬]",
            "ふわふわ時間");
        Assert.NotNull(bundle);
        Assert.Equal(bundle!.SourceLyrics.Count, bundle.TranslatedLyrics.Count);
        Assert.True(NetEaseLyricProvider.HasSufficientCoverage(
            bundle.SourceLyrics, bundle.TranslatedLyrics));
    }

    private static NetEaseSong Song(string title, string artist, string album, int id) => new()
    {
        Id = id,
        Name = title,
        Album = new NetEaseAlbum { Name = album },
        Artists = [new NetEaseArtist { Name = artist }]
    };
}
