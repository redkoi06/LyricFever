//
//  LyricsRepository.swift
//  Lyric Fever
//

import CoreData
import Foundation

enum LyricsRepositoryError: Error {
    case corruptEntry(trackID: String)
}

struct LyricsRepositoryCacheInfo {
    let byteCount: Int64
    let songCount: Int
    let lineCount: Int
}

@MainActor
final class LyricsRepository {
    private let context: NSManagedObjectContext

    init(context: NSManagedObjectContext) {
        self.context = context
    }

    func upsert(_ lyrics: [LyricLine], trackID: String, trackName: String) throws {
        guard !lyrics.isEmpty else {
            try delete(trackID: trackID)
            return
        }

        let request: NSFetchRequest<SongObject> = SongObject.fetchRequest()
        request.predicate = NSPredicate(format: "id == %@", trackID)
        request.fetchLimit = 1
        let object = try context.fetch(request).first ?? SongObject(context: context)
        object.id = trackID
        object.title = trackName
        object.downloadDate = .now
        if object.isInserted {
            object.language = ""
        }
        object.lyricsTimestamps = lyrics.map(\.startTimeMS)
        object.lyricsWords = lyrics.map(\.words)
        try saveIfNeeded()
    }

    func lyrics(for trackID: String) throws -> [LyricLine]? {
        let request: NSFetchRequest<SongObject> = SongObject.fetchRequest()
        request.predicate = NSPredicate(format: "id == %@", trackID)
        request.fetchLimit = 1
        guard let object = try context.fetch(request).first else {
            return nil
        }

        guard object.lyricsTimestamps.count == object.lyricsWords.count else {
            context.delete(object)
            try saveIfNeeded()
            throw LyricsRepositoryError.corruptEntry(trackID: trackID)
        }
        guard !object.lyricsWords.isEmpty else {
            return nil
        }
        return zip(object.lyricsTimestamps, object.lyricsWords).map {
            LyricLine(startTime: $0, words: $1)
        }
    }

    func delete(trackID: String) throws {
        let request: NSFetchRequest<SongObject> = SongObject.fetchRequest()
        request.predicate = NSPredicate(format: "id == %@", trackID)
        let objects = try context.fetch(request)
        objects.forEach(context.delete)
        try saveIfNeeded()
    }

    func deleteAll() throws {
        let request: NSFetchRequest<SongObject> = SongObject.fetchRequest()
        try context.fetch(request).forEach(context.delete)
        try saveIfNeeded()
    }

    func cacheInfo() throws -> LyricsRepositoryCacheInfo {
        let request: NSFetchRequest<SongObject> = SongObject.fetchRequest()
        let objects = try context.fetch(request)
        var byteCount: Int64 = 0
        var songCount = 0
        var lineCount = 0

        for object in objects {
            let words = object.lyricsWords
            let timestamps = object.lyricsTimestamps
            guard !words.isEmpty || !timestamps.isEmpty else { continue }
            songCount += 1
            lineCount += max(words.count, timestamps.count)
            byteCount += words.reduce(into: Int64(0)) { size, lyric in
                size += Int64(lyric.lengthOfBytes(using: .utf8))
            }
            byteCount += Int64(timestamps.count * MemoryLayout<TimeInterval>.size)
        }

        return LyricsRepositoryCacheInfo(
            byteCount: byteCount,
            songCount: songCount,
            lineCount: lineCount
        )
    }

    private func saveIfNeeded() throws {
        if context.hasChanges {
            try context.save()
        }
    }
}
