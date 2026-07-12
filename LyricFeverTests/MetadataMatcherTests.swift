import XCTest

final class MetadataMatcherTests: XCTestCase {
    func testTitleCandidatesIncludeBracketFreeVersion() {
        XCTAssertEqual(
            MetadataMatcher.titleCandidates(for: "Song Name (Live Version)"),
            ["Song Name (Live Version)", "Song Name"]
        )
    }

    func testExactTitleAndArtistRankHighest() {
        let exact = result(title: "Café", artist: "Artist")
        let partial = result(title: "Café (Live)", artist: "Artist")
        let unrelated = result(title: "Another Song", artist: "Someone Else")

        let sorted = MetadataMatcher.filteredAndSorted(
            [unrelated, partial, exact],
            trackName: "Cafe",
            artistName: "Artist"
        )

        XCTAssertEqual(sorted.map(\.songName), ["Café", "Café (Live)"])
    }

    func testDuplicateProviderResultIsRemoved() {
        let first = result(title: "Song", artist: "Artist")
        let duplicate = result(title: "song", artist: "artist")
        let sorted = MetadataMatcher.filteredAndSorted(
            [first, duplicate],
            trackName: "Song",
            artistName: "Artist"
        )
        XCTAssertEqual(sorted.count, 1)
    }

    func testPlausibleMatchRejectsShortUnrelatedCandidate() {
        XCTAssertFalse(MetadataMatcher.plausiblyMatches("A Very Long Song Name", "Song"))
        XCTAssertTrue(MetadataMatcher.plausiblyMatches("Song Name Remaster", "Song Name Remaster 2024"))
    }

    private func result(title: String, artist: String) -> SongResult {
        SongResult(
            lyricType: "Test",
            songName: title,
            albumName: "Album",
            artistName: artist,
            lyrics: [LyricLine(startTime: 0, words: "Line")]
        )
    }
}
