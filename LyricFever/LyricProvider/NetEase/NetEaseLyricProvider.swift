//
//  NetEaseLyricsProvider.swift
//  Lyric Fever
//
//

import Foundation
import StringMetric

class NetEaseLyricProvider: LyricProvider {
    var providerName = "NetEase Lyric Provider"
    // Fake Spotify User Agent
    // Spotify's started blocking my app's useragent. A win honestly 🤣
    let fakeSpotifyUserAgentconfig = URLSessionConfiguration.default
    let fakeSpotifyUserAgentSession: URLSession
    
    init() {
        // Set user agents for Spotify and LRCLIB
        fakeSpotifyUserAgentconfig.httpAdditionalHeaders = ["User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.3 Safari/605.1.15"]
        fakeSpotifyUserAgentSession = URLSession(configuration: fakeSpotifyUserAgentconfig)
    }
    
    func fetchNetworkLyrics(trackName: String, trackID: String, currentlyPlayingArtist: String?, currentAlbumName: String? ) async throws -> NetworkFetchReturn {
        if let currentlyPlayingArtist, let currentAlbumName,
           let url = makeNetEaseURL(path: "/search", queryItems: [
               URLQueryItem(name: "keywords", value: "\(trackName) \(currentlyPlayingArtist)"),
               URLQueryItem(name: "limit", value: "1")
           ]) {
            let request = URLRequest(url: url)
            let urlResponseAndData = try await fakeSpotifyUserAgentSession.data(for: request)
            let neteasesearch = try JSONDecoder().decode(NetEaseSearch.self, from: urlResponseAndData.0)
            guard let neteaseResult = neteasesearch.result.songs.first, let neteaseArtist = neteaseResult.artists.first else {
                return NetworkFetchReturn(lyrics: [], colorData: nil)
            }
            let neteaseId = neteaseResult.id
            let conditions = [
                trackName.distance(between: neteaseResult.name) > 0.75,
                currentlyPlayingArtist.distance(between: neteaseArtist.name) > 0.75,
                currentAlbumName.distance(between: neteaseResult.album.name) > 0.75
            ]

            let trueCount = conditions.filter { $0 }.count
            // I need at least 2 conditions to be met: track name, or album, or artist name, match 75% of the way
            if trueCount < 2 {
                return NetworkFetchReturn(lyrics: [], colorData: nil)
            }
            guard let lyricURL = makeNetEaseURL(
                path: "/lyric",
                queryItems: [URLQueryItem(name: "id", value: String(neteaseId))]
            ) else {
                return NetworkFetchReturn(lyrics: [], colorData: nil)
            }
            let lyricRequest = URLRequest(url: lyricURL)
            let urlResponseAndDataLyrics = try await fakeSpotifyUserAgentSession.data(for: lyricRequest)
            let neteaseLyrics = try JSONDecoder().decode(NetEaseLyrics.self, from: urlResponseAndDataLyrics.0)
            guard let neteaselrc = neteaseLyrics.lrc, let neteaseLrcString = neteaselrc.lyric else {
                return NetworkFetchReturn(lyrics: [], colorData: nil)
            }
            
            // Sanitize HTML entities and stray escapes before parsing
            let cleaned = unescapeHTMLEntities(in: neteaseLrcString)
            
            let parser = LyricsParser(lyrics: cleaned)
            // NetEase incorrectly advertises lyrics for EVERY song when it only has the name, artist, composer at 0.0 *sigh*
            if parser.lyrics.last?.startTimeMS == 0.0 {
                return NetworkFetchReturn(lyrics: [], colorData: nil)
            }
            return NetworkFetchReturn(lyrics: parser.lyrics, colorData: nil)
        }
        return NetworkFetchReturn(lyrics: [], colorData: nil)
    }
}

// MARK: - HTML entity unescape
private func unescapeHTMLEntities(in text: String) -> String {
    var s = text
    // Common named entities
    s = s.replacingOccurrences(of: "&apos;", with: "'")
    s = s.replacingOccurrences(of: "&quot;", with: "\"")
    s = s.replacingOccurrences(of: "&amp;", with: "&")
    s = s.replacingOccurrences(of: "&lt;", with: "<")
    s = s.replacingOccurrences(of: "&gt;", with: ">")
    // Common numeric entity often used for apostrophe
    s = s.replacingOccurrences(of: "&#39;", with: "'")
    s = s.replacingOccurrences(of: "&#x27;", with: "'")
    // Normalize stray backslashes that sometimes trail lines from API payloads
    // Keep escaped newlines for LyricsParser to convert, but remove trailing backslashes.
    s = s.replacingOccurrences(of: "\\\n", with: "\n")
    // If payload includes escaped newline markers already, LyricsParser handles "\\n" -> "\n".
    return s
}

// MARK: - New: Search implementation
extension NetEaseLyricProvider {
    func search(trackName: String, artistName: String) async throws -> [SongResult] {
        // Ask for up to 5
        guard let url = makeNetEaseURL(path: "/search", queryItems: [
            URLQueryItem(name: "keywords", value: "\(trackName) \(artistName)"),
            URLQueryItem(name: "limit", value: "5")
        ]) else {
            return []
        }
        let request = URLRequest(url: url)
        let urlResponseAndData = try await fakeSpotifyUserAgentSession.data(for: request)
        let neteasesearch = try JSONDecoder().decode(NetEaseSearch.self, from: urlResponseAndData.0)
        
        var results: [SongResult] = []
        for song in neteasesearch.result.songs {
            guard let firstArtist = song.artists.first else { continue }
//            // Similarity checks (reuse thresholds)
//            let conditions = [
//                track.distance(between: song.name) > 0.75,
//                artist.distance(between: firstArtist.name) > 0.75,
//                (album ?? "").distance(between: song.album.name) > 0.75
//            ]
//            let trueCount = conditions.filter { $0 }.count
//            if trueCount < 2 { continue }
            
            // Fetch lyrics
            guard let lyricURL = makeNetEaseURL(
                path: "/lyric",
                queryItems: [URLQueryItem(name: "id", value: String(song.id))]
            ) else { continue }
            do {
                let lyricsData = try await fakeSpotifyUserAgentSession.data(from: lyricURL).0
                let neteaseLyrics = try JSONDecoder().decode(NetEaseLyrics.self, from: lyricsData)
                guard let lrcText = neteaseLyrics.lrc?.lyric else { continue }
                let cleaned = unescapeHTMLEntities(in: lrcText)
                let parsed = LyricsParser(lyrics: cleaned).lyrics
                if parsed.last?.startTimeMS == 0.0 { continue }
                
                results.append(SongResult(lyricType: "NetEase", songName: song.name, albumName: song.album.name, artistName: firstArtist.name, lyrics: parsed))
            } catch {
                // ignore per-item failure
            }
        }
        return results
    }
}

private func makeNetEaseURL(path: String, queryItems: [URLQueryItem]) -> URL? {
    var components = URLComponents()
    components.scheme = "https"
    components.host = "neteasecloudmusicapi-ten-wine.vercel.app"
    components.path = path
    components.queryItems = queryItems
    return components.url
}
