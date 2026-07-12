//
//  LRCLIBLyricProvider.swift
//  Lyric Fever
//
//

import Foundation

class LRCLIBLyricProvider: LyricProvider {
    var providerName = "LRCLIB Lyric Provider"
    // LRCLIB User Agent
    let LRCLIBUserAgentConfig = URLSessionConfiguration.default
    let LRCLIBUserAgentSession: URLSession

    init() {
//        LRCLIBUserAgentConfig.httpAdditionalHeaders = ["User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_6_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.6 Safari/605.1.15"]
        LRCLIBUserAgentConfig.httpAdditionalHeaders = ["User-Agent": "Lyric Fever v3.3"]
        LRCLIBUserAgentSession = URLSession(configuration: LRCLIBUserAgentConfig)
    }

    func fetchNetworkLyrics(trackName: String, trackID: String, currentlyPlayingArtist: String?, currentAlbumName: String?) async throws -> NetworkFetchReturn {
        guard let currentlyPlayingArtist else {
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
            let request = URLRequest(url: url)
            let urlResponseAndData = try await LRCLIBUserAgentSession.data(for: request)
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
        let req = URLRequest(url: url)
        let urlResponseAndData = try await LRCLIBUserAgentSession.data(for: req)
        let lrcLyrics = try JSONDecoder().decode(PluralLRCLIBLyrics.self, from: urlResponseAndData.0)
        var results: [SongResult] = []
        for lyric in lrcLyrics.lyrics {
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
                && MetadataMatcher.plausiblyMatches(trackName, result.songName)
                && MetadataMatcher.plausiblyMatches(artistName, result.artistName)
        }
    }
}
