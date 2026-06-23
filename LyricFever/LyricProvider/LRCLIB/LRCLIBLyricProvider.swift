//
//  LRCLIBLyricProvider.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-06-16.
//

import Foundation

class LRCLIBLyricProvider: LyricProvider {
    var providerName = "LRCLIB Lyric Provider"
    // LRCLIB User Agent
    let LRCLIBUserAgentConfig = URLSessionConfiguration.default
    let LRCLIBUserAgentSession: URLSession

    init() {
//        LRCLIBUserAgentConfig.httpAdditionalHeaders = ["User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_6_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15"]
        LRCLIBUserAgentConfig.httpAdditionalHeaders = ["User-Agent": "Lyric Fever v3.3 (https://github.com/aviwad/LyricFever)"]
        LRCLIBUserAgentSession = URLSession(configuration: LRCLIBUserAgentConfig)
    }

    func fetchNetworkLyrics(trackName: String, trackID: String, currentlyPlayingArtist: String?, currentAlbumName: String?) async throws -> NetworkFetchReturn {
        guard let currentlyPlayingArtist else {
            print("artist missing")
            return NetworkFetchReturn(lyrics: [], colorData: nil)
        }

        if let currentAlbumName, !currentAlbumName.isEmpty {
            do {
                let exactLyrics = try await fetchExactLyrics(
                    trackName: trackName,
                    artistName: currentlyPlayingArtist,
                    albumName: currentAlbumName
                )
                if !exactLyrics.lyrics.isEmpty {
                    return exactLyrics
                }
            } catch {
                print("LRCLIB /api/get failed; falling back to /api/search: \(error)")
            }
        }

        let searchResults = try await search(trackName: trackName, artistName: currentlyPlayingArtist)
        guard let fallbackResult = bestAutomaticSearchResult(
            from: searchResults,
            trackName: trackName,
            artistName: currentlyPlayingArtist
        ) else {
            print("LRCLIB /api/search fallback found no safe automatic match")
            return NetworkFetchReturn(lyrics: [], colorData: nil)
        }
        print("LRCLIB /api/search fallback selected \(fallbackResult.songName)")
        return NetworkFetchReturn(lyrics: fallbackResult.lyrics, colorData: nil)
    }

    @MainActor
    private func fetchExactLyrics(trackName: String, artistName: String, albumName: String) async throws -> NetworkFetchReturn {
        guard let url = makeComponents(path: "/api/get", items: [
            URLQueryItem(name: "artist_name", value: artistName),
            URLQueryItem(name: "track_name", value: trackName),
            URLQueryItem(name: "album_name", value: albumName)
        ]).url else {
            return NetworkFetchReturn(lyrics: [], colorData: nil)
        }
        print("LRCLIB /api/get: \(url.absoluteString)")
        let req = URLRequest(url: url)
        let urlResponseAndData = try await LRCLIBUserAgentSession.data(for: req)
        let lrcLyrics = try JSONDecoder().decode(LRCLIBLyrics.self, from: urlResponseAndData.0)
        return NetworkFetchReturn(lyrics: lrcLyrics.lyrics, colorData: nil)
    }

    func fetchNetworkLyrics2(trackName: String, trackID: String, currentlyPlayingArtist: String?, currentAlbumName: String?) async throws -> NetworkFetchReturn {
        let artist = currentlyPlayingArtist?.replacingOccurrences(of: "&", with: "")
        let album = currentAlbumName?.replacingOccurrences(of: "&", with: "")
        let trackName = trackName.replacingOccurrences(of: "&", with: "")
        if let artist = artist, let album = album, let url = URL(string: "https://lrclib.net/api/get?artist_name=\(artist)&track_name=\(trackName)&album_name=\(album)") {
            print("the lrclib call is \(url.absoluteString)")
            let request = URLRequest(url: url)
            let urlResponseAndData = try await LRCLIBUserAgentSession.data(for: request)
            print(String(describing: urlResponseAndData.0))
            let lrcLyrics = try JSONDecoder().decode(LRCLIBLyrics.self, from: urlResponseAndData.0)
            return NetworkFetchReturn(lyrics: lrcLyrics.lyrics, colorData: nil)
        }
        return NetworkFetchReturn(lyrics: [], colorData: nil)
    }

    func search(trackName: String, artistName: String) async throws -> [SongResult] {
        guard let url = makeComponents(path: "/api/search", items: [
            URLQueryItem(name: "track_name", value: trackName),
            URLQueryItem(name: "artist_name", value: artistName)
        ]).url else {
            print("MassSearch: LRCLIB: failed to generate URl")
            return []
        }
        print("LRCLIB /api/search: \(url.absoluteString)")
        let req = URLRequest(url: url)
        print("The request is \(req)")
        let urlResponseAndData = try await LRCLIBUserAgentSession.data(for: req)
        let lrcLyrics = try JSONDecoder().decode(PluralLRCLIBLyrics.self, from: urlResponseAndData.0)
        print("lrc downloaded")
        var results: [SongResult] = []
        for lyric in lrcLyrics.lyrics {
            print("lrc lyric: \(lyric.name)")
            if !lyric.lyrics.isEmpty {
                results.append(SongResult(lyricType: "LRCLIB", songName: lyric.trackName, albumName: lyric.albumName, artistName: lyric.artistName, lyrics: lyric.lyrics))
            }
        }
        return results
    }

    func makeComponents(path: String, items: [URLQueryItem]) -> URLComponents {
        var comps = URLComponents()
        comps.scheme = "https"
        comps.host = "lrclib.net"
        comps.path = path
        comps.queryItems = items
        return comps
    }

    private func bestAutomaticSearchResult(from results: [SongResult], trackName: String, artistName: String) -> SongResult? {
        results.first { result in
            !result.lyrics.isEmpty
                && metadataMatches(trackName, result.songName)
                && metadataMatches(artistName, result.artistName)
        }
    }

    private func metadataMatches(_ source: String, _ candidate: String) -> Bool {
        let source = normalizedMetadata(source)
        let candidate = normalizedMetadata(candidate)
        guard !source.isEmpty, !candidate.isEmpty else {
            return false
        }
        if source == candidate {
            return true
        }
        let shorterCount = min(source.count, candidate.count)
        let longerCount = max(source.count, candidate.count)
        return shorterCount * 10 >= longerCount * 6
            && (source.contains(candidate) || candidate.contains(source))
    }

    private func normalizedMetadata(_ value: String) -> String {
        value
            .folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .unicodeScalars
            .filter(CharacterSet.alphanumerics.contains)
            .map(String.init)
            .joined()
    }
}
