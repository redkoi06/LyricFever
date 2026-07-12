//
//  SearchWindow.swift
//  Lyric Fever
//
//

import SwiftUI

struct SearchWindow: View {
    @Environment(ViewModel.self) var viewmodel
    @State private var trackName: String = ""
    @State private var currentProvider: String = ""
    @State private var artistName: String = ""
    @State private var searchResults: [SongResult] = []
    @State private var isFetching = false
    @State private var selectedLyric: UUID? = nil
    @State private var lyricsAreApplied: Bool = false
    @State private var searchTask: Task<Void, Never>? = nil
    @State private var searchRevision: UInt = 0
    @State private var searchMessage: String? = nil
    
    private let overlayHeight: CGFloat = 250
    
    @ViewBuilder
    var searchControlsView: some View {
        HStack(spacing: 12) {
            TextField("Song Name", text: $trackName)
                .textFieldStyle(.roundedBorder)
                .accessibilityLabel("Song Name")
            TextField("Artist Name", text: $artistName)
                .textFieldStyle(.roundedBorder)
                .accessibilityLabel("Artist Name")
            Button {
                startSearchTask()
            } label: {
                Label("Search", systemImage: "magnifyingglass")
            }
            .disabled(isFetching || trackName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            .keyboardShortcut(.defaultAction)
            .tint(.primary)
        }
    }
    
    @ViewBuilder
    var searchResultsView: some View {
        SearchResultsNSTableView(results: searchResults, selectedID: $selectedLyric)
    }
    
    @ViewBuilder
    var selectedLyricView: some View {
        if let selectedLyric, let selectedLyricLyric = searchResults.first(where: { $0.id == selectedLyric}) {
            HStack {
                LyricPreviewNSTableView(lyrics: selectedLyricLyric.lyrics)
                              .frame(width: 400)
                Spacer()
                Button {
                    let cleanLyrics = NetworkFetchReturn(lyrics: selectedLyricLyric.lyrics, colorData: nil).processed(withSongName: trackName, duration: viewmodel.duration).lyrics
                    
                    if let currentIndex = viewmodel.currentlyPlayingLyricsIndex, currentIndex >= cleanLyrics.count {
                        // set currentindex to nil to prevent out of bounds index access with existing UI
                        viewmodel.currentlyPlayingLyricsIndex = nil
                    }
                    
                    viewmodel.setNewLyricsColorTranslationRomanizationAndStartUpdater(with: cleanLyrics)
                    guard let spotifyID = viewmodel.currentlyPlaying else {
                        return
                    }
                    viewmodel.saveLyricsToCache(cleanLyrics, trackID: spotifyID, trackName: trackName)
                    lyricsAreApplied = true
                } label: {
                    Label(lyricsAreApplied ? "Lyrics were applied!" : "Click to Use", systemImage: "checkmark")
                        .bold()
                        .frame(width: 230)
                }
                .buttonStyle(.borderedProminent)
                .disabled(lyricsAreApplied)
                .tint(lyricsAreApplied ? .gray : .green)
            }
            .padding()
//            .id(selectedLyric)
            .transition(.move(edge: .bottom))
            .frame(maxWidth: .infinity)
            .frame(height: overlayHeight)
            .background(
                .thinMaterial
            )
        }
    }
    
    @ViewBuilder
    var searchWindow: some View {
        VStack {
            searchControlsView
            ZStack {
                searchResultsView
                loadingView
            }
        }
        // Reserve space when the bottom overlay is visible so rows aren’t hidden
        .padding(.bottom, selectedLyric != nil ? overlayHeight : 0)
        .padding()
    }
    
    @ViewBuilder
    var loadingView: some View {
        if isFetching {
            VStack(spacing: 10) {
                ProgressView()
                Text(currentProvider.isEmpty ? "Searching…" : "Searching \(currentProvider)…")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            .padding(20)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
        } else if searchResults.isEmpty, let searchMessage {
            VStack(spacing: 8) {
                Image(systemName: "text.magnifyingglass")
                    .font(.title)
                    .foregroundStyle(.secondary)
                Text(searchMessage)
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)
            }
            .padding()
        }
    }
    
    private func searchLyrics(
        trackName: String,
        artistName: String,
        revision: UInt
    ) async throws -> [SongResult] {
        var allResults: [SongResult] = []
        var lastError: Error?
        var completedRequestCount = 0
        let trackNameCandidates = MetadataMatcher.titleCandidates(for: trackName)

        for lyricProvider in viewmodel.allNetworkLyricProvidersForSearch {
            try Task.checkCancellation()
            guard searchRevision == revision else { throw CancellationError() }
            currentProvider = lyricProvider.providerName

            for candidateTrackName in trackNameCandidates {
                try Task.checkCancellation()
                do {
                    let results = try await lyricProvider.search(
                        trackName: candidateTrackName,
                        artistName: artistName
                    )
                    try Task.checkCancellation()
                    guard searchRevision == revision else { throw CancellationError() }
                    completedRequestCount += 1
                    allResults.append(contentsOf: results)
                } catch is CancellationError {
                    throw CancellationError()
                } catch {
                    lastError = error
                }
            }
        }

        if completedRequestCount == 0, let lastError {
            throw lastError
        }
        return MetadataMatcher.filteredAndSorted(
            allResults,
            trackName: trackName,
            artistName: artistName
        )
    }

    private func startSearchTask() {
        searchTask?.cancel()
        searchRevision &+= 1
        let revision = searchRevision
        let requestedTrackName = trackName.trimmingCharacters(in: .whitespacesAndNewlines)
        let requestedArtistName = artistName.trimmingCharacters(in: .whitespacesAndNewlines)

        selectedLyric = nil
        searchResults = []
        searchMessage = nil
        currentProvider = ""
        lyricsAreApplied = false

        guard !requestedTrackName.isEmpty else {
            isFetching = false
            searchMessage = "Enter a song name to search."
            return
        }

        isFetching = true
        searchTask = Task { @MainActor in
            defer {
                if searchRevision == revision {
                    isFetching = false
                    currentProvider = ""
                }
            }
            do {
                let results = try await searchLyrics(
                    trackName: requestedTrackName,
                    artistName: requestedArtistName,
                    revision: revision
                )
                guard searchRevision == revision else { return }
                searchResults = results
                if results.isEmpty {
                    searchMessage = "No matching lyrics found."
                }
            } catch is CancellationError {
                return
            } catch {
                print("Search Task Error: \(error)")
                guard searchRevision == revision else { return }
                searchMessage = "Search failed. Check your network connection and try again."
            }
        }
    }

    private func syncTrackFieldsFromViewModel() {
        if viewmodel.currentPlayer == .appleMusic {
            viewmodel.refreshAppleMusicMetadataFromPlayer()
        }
        trackName = viewmodel.currentlyPlayingName ?? ""
        artistName = viewmodel.currentlyPlayingArtist ?? ""
    }

    var body: some View {
        searchWindow
            .onExitCommand {
                selectedLyric = nil
            }
            .overlay(
                VStack {
                    selectedLyricView.ignoresSafeArea()
                }
                    .animation(.snappy(duration: 0.2), value: selectedLyric)
                , alignment: .bottom)
            .onAppear {
                syncTrackFieldsFromViewModel()
                startSearchTask()
            }
            .onChange(of: selectedLyric) {
                lyricsAreApplied = false
            }
            .onChange(of: viewmodel.currentlyPlaying) {
                if viewmodel.currentlyPlaying == nil {
                    return
                }
                syncTrackFieldsFromViewModel()
                startSearchTask()
            }
            .onChange(of: viewmodel.currentlyPlayingName) { oldName, newName in
                if let newName {
                    trackName = newName
                }
            }
            .onChange(of: viewmodel.currentlyPlayingArtist) { oldArtist, newArtist in
                if let newArtist {
                    artistName = newArtist
                }
            }
            .onDisappear {
                searchTask?.cancel()
                searchRevision &+= 1
                isFetching = false
            }
            .tint(viewmodel.currentBackground)
        .navigationTitle("Searching for \(viewmodel.currentlyPlayingName ?? "-") by \(viewmodel.currentlyPlayingArtist ?? "-")")
        .presentedWindowToolbarStyle(.unified)
    }
}
