import XCTest

final class LyricsParserTests: XCTestCase {
    func testParsesMultipleTimestampsWithSameWords() {
        let lyrics = LyricsParser(lyrics: "[00:01.50][00:02.75]Hello").lyrics
        XCTAssertEqual(lyrics.map(\.startTimeMS), [1500, 2750])
        XCTAssertEqual(lyrics.map(\.words), ["Hello", "Hello"])
    }

    func testOffsetUsesMilliseconds() {
        let lyrics = LyricsParser(lyrics: "[offset:500]\n[00:01.00]Hello").lyrics
        XCTAssertEqual(lyrics.first?.startTimeMS, 1500)
    }

    func testNegativeOffsetDoesNotCreateNegativeTimestamp() {
        let lyrics = LyricsParser(lyrics: "[offset:-2000]\n[00:01.00]Hello").lyrics
        XCTAssertEqual(lyrics.first?.startTimeMS, 0)
    }

    func testMalformedTimestampIsIgnored() {
        XCTAssertTrue(LyricsParser(lyrics: "[invalid]Hello").lyrics.isEmpty)
        XCTAssertTrue(LyricsParser(lyrics: "[00:nope]Hello").lyrics.isEmpty)
    }
}
