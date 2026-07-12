import CoreData
import XCTest

final class LyricsRepositoryTests: XCTestCase {
    @MainActor
    private func makeRepository() throws -> (NSPersistentContainer, LyricsRepository) {
        let bundle = Bundle(for: Self.self)
        guard let modelURL = bundle.url(forResource: "Lyrics", withExtension: "mom"),
              let model = NSManagedObjectModel(contentsOf: modelURL) else {
            throw CocoaError(.fileNoSuchFile)
        }
        let container = NSPersistentContainer(name: "Lyrics", managedObjectModel: model)
        let description = NSPersistentStoreDescription()
        description.type = NSInMemoryStoreType
        container.persistentStoreDescriptions = [description]

        var loadError: Error?
        container.loadPersistentStores { _, error in loadError = error }
        if let loadError { throw loadError }
        container.viewContext.mergePolicy = NSMergePolicy.overwrite
        return (container, LyricsRepository(context: container.viewContext))
    }

    @MainActor
    func testUpsertReplacesExistingLyricsWithoutDuplicateRows() throws {
        let (_, repository) = try makeRepository()
        try repository.upsert(
            [LyricLine(startTime: 1000, words: "First")],
            trackID: "track",
            trackName: "Song"
        )
        try repository.upsert(
            [LyricLine(startTime: 2000, words: "Updated")],
            trackID: "track",
            trackName: "Song"
        )

        let lyrics = try repository.lyrics(for: "track")
        XCTAssertEqual(lyrics?.map(\.words), ["Updated"])
        XCTAssertEqual(try repository.cacheInfo().songCount, 1)
    }

    @MainActor
    func testCorruptParallelArraysAreDeletedAndReported() throws {
        let (container, repository) = try makeRepository()
        let object = SongObject(context: container.viewContext)
        object.id = "corrupt"
        object.title = "Corrupt"
        object.downloadDate = .now
        object.language = ""
        object.lyricsTimestamps = [1000, 2000]
        object.lyricsWords = ["Only one"]
        try container.viewContext.save()

        XCTAssertThrowsError(try repository.lyrics(for: "corrupt")) { error in
            guard case LyricsRepositoryError.corruptEntry(let trackID) = error else {
                return XCTFail("Unexpected error: \(error)")
            }
            XCTAssertEqual(trackID, "corrupt")
        }
        XCTAssertNil(try repository.lyrics(for: "corrupt"))
    }
}
