//
//  SpotifyResponse.swift
//  Lyric Fever
//
//


struct SpotifyResponse: Codable {
    let tracks: Tracks
    
    struct Tracks: Codable {
        let items: [Item]
        
        struct Item: Codable {
            let type: String
            let name: String
            let artists: [Artist]
            var firstArtistName: String? {
                return artists.first?.name
            }
            let id: String
        }

        struct Artist: Codable {
            let name: String
        }
    }
}
