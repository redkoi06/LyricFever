//
//  NetEaseSearch.swift
//  Lyric Fever
//
//

// From LyricsX: NetEase. Adapted for my needs
struct NetEaseSearch: Decodable {
    let result: Result
    let code: Int
    
    struct Result: Decodable {
        let songs: [Song]
        let songCount: Int
        
        struct Song: Decodable {
            let name: String
            let id: Int
            let duration: Int // milliseconds
            let album: Album
            let artists: [Artist]
        }
        
        struct Album: Decodable {
            let name: String
        }
        
        struct Artist: Decodable {
            let name: String
        }
    }
}
