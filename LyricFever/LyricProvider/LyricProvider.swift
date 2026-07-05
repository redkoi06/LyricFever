//
//  LyricProvider.swift
//  Lyric Fever
//
//

protocol LyricProvider {
    var providerName: String { get }
    
    @MainActor
    func fetchNetworkLyrics(trackName: String, trackID: String, currentlyPlayingArtist: String?, currentAlbumName: String? ) async throws -> NetworkFetchReturn

    @MainActor
    func search(trackName: String, artistName: String) async throws -> [SongResult]
}

