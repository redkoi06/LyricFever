//
//  SongResult.swift
//  Lyric Fever
//
//

import Foundation


struct SongResult: Identifiable {
    let lyricType: String
    let songName: String
    let albumName: String
    let artistName: String
    let id = UUID()
    let lyrics: [LyricLine]
}
