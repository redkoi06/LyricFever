//
//  LRCLIBLyrics.swift
//  Lyric Fever
//
//

import Foundation


struct LRCLIBLyrics: Codable {
    let id: Int
    let name, trackName, artistName, albumName: String
    let duration: Double
    let instrumental: Bool
    let plainLyrics, syncedLyrics: String
    let lyrics: [LyricLine]
    
    enum CodingKeys: CodingKey {
        case id
        case name
        case trackName
        case artistName
        case albumName
        case duration
        case instrumental
        case plainLyrics
        case syncedLyrics
//        case lyrics
    }
    
    static func decodeLyrics(input: String) -> [LyricLine] {
        LyricsParser(lyrics: input).lyrics
    }
    
    init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.id = try container.decode(Int.self, forKey: .id)
        self.name = try container.decode(String.self, forKey: .name)
        self.trackName = try container.decode(String.self, forKey: .trackName)
        self.artistName = try container.decode(String.self, forKey: .artistName)
        self.albumName = try container.decode(String.self, forKey: .albumName)
        self.duration = try container.decode(Double.self, forKey: .duration)
        self.instrumental = try container.decode(Bool.self, forKey: .instrumental)
        if instrumental {
            self.plainLyrics = ""
            self.syncedLyrics = ""
            self.lyrics = []
        } else {
            self.plainLyrics = try container.decode(String.self, forKey: .plainLyrics)
            self.syncedLyrics = try container.decode(String.self, forKey: .syncedLyrics)
            self.lyrics = LRCLIBLyrics.decodeLyrics(input: syncedLyrics)
        }
//        self.lyrics = try container.decode([LyricLine].self, forKey: .lyrics)
    }
}
