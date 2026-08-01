using LyricFever.Core.Lyrics;
using Xunit;

namespace LyricFever.Core.Tests;

/// <summary>对照 macOS MetadataMatcherTests 移植。</summary>
public class MetadataMatcherTests
{
    [Fact]
    public void TitleCandidatesIncludeBracketFreeVersion()
    {
        Assert.Equal(
            new[] { "Song Name (Live Version)", "Song Name" },
            MetadataMatcher.TitleCandidates("Song Name (Live Version)"));
    }

    [Fact]
    public void ExactTitleAndArtistRankHighest()
    {
        var exact = Result("Café", "Artist");
        var partial = Result("Café (Live)", "Artist");
        var unrelated = Result("Another Song", "Someone Else");

        var sorted = MetadataMatcher.FilteredAndSorted(
            new List<SongResult> { unrelated, partial, exact },
            "Cafe", "Artist");

        Assert.Equal(new[] { "Café", "Café (Live)" }, sorted.Select(r => r.SongName));
    }

    [Fact]
    public void DuplicateProviderResultIsRemoved()
    {
        var first = Result("Song", "Artist");
        var duplicate = Result("song", "artist");
        var sorted = MetadataMatcher.FilteredAndSorted(
            new List<SongResult> { first, duplicate },
            "Song", "Artist");
        Assert.Single(sorted);
    }

    [Fact]
    public void PlausibleMatchRejectsShortUnrelatedCandidate()
    {
        Assert.False(MetadataMatcher.PlausiblyMatches("A Very Long Song Name", "Song"));
        Assert.True(MetadataMatcher.PlausiblyMatches("Song Name Remaster", "Song Name Remaster 2024"));
    }

    private static SongResult Result(string title, string artist) => new()
    {
        LyricType = "Test",
        SongName = title,
        AlbumName = "Album",
        ArtistName = artist,
        Lyrics = new List<LyricLine> { new(0, "Line") }
    };
}
