//
//  SearchWindow.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-09-02.
//

import SwiftUI

struct SearchWindow: View {
    @Environment(ViewModel.self) var viewmodel
    @State var trackName: String = ""
    @State var currentProvider: String = ""
    @State var artistName: String = ""
    @State private var searchResults: [SongResult] = []
    @State var isFetching = false
    @State private var selectedLyric: UUID? = nil
    @State private var lyricsAreApplied: Bool = false
    @State private var searchTask: Task<Void, Never>? = nil
    
    private let overlayHeight: CGFloat = 250
    
    @ViewBuilder
    var searchControlsView: some View {
        HStack {
            Text("Song Name")
            TextField("", text: $trackName)
                .padding(.trailing, 30)
            Text("Artist Name:")
            TextField("", text: $artistName)
                .padding(.trailing, 30)
            Button {
                startSearchTask()
            } label: {
                Image(systemName: "magnifyingglass")
            }
            .disabled(isFetching)
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
                    // thats how i save to coredata
                    let _ = SongObject(from: cleanLyrics, with: viewmodel.coreDataContainer.viewContext, trackID: spotifyID, trackName: trackName)
                    viewmodel.saveCoreData()
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
    
    // Helper to format milliseconds as mm:ss
    private func formattedTimestamp(ms: TimeInterval) -> String {
        let totalSeconds = Int(ms) / 1000
        let formatter = DateComponentsFormatter()
        formatter.allowedUnits = [.minute, .second]
        formatter.zeroFormattingBehavior = [.pad]
        return formatter.string(from: TimeInterval(totalSeconds)) ?? "00:00"
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
            Rectangle()
                .fill(Color.black.opacity(0.5))
                .frame(width: 80, height: 80)
                .cornerRadius(10)
            ProgressView()
        }
    }
    
    func searchLyrics() async throws {
        selectedLyric = nil
        isFetching = true
        defer { isFetching = false }
        searchResults = []
        let trackNameCandidates = titleSearchCandidates(for: trackName)
        for lyricProvider in viewmodel.allNetworkLyricProvidersForSearch {
            if Task.isCancelled { return }
            currentProvider = lyricProvider.providerName
            var providerResults: [SongResult] = []
            for candidateTrackName in trackNameCandidates {
                if Task.isCancelled { return }
                let results = try await lyricProvider.search(trackName: candidateTrackName, artistName: artistName)
                if Task.isCancelled { return }
                providerResults.append(contentsOf: results)
            }
            searchResults.append(contentsOf: providerResults.filter(isRelevantSearchResult))
            searchResults = deduplicatedSearchResults(searchResults).sorted {
                searchResultRelevance($0) > searchResultRelevance($1)
            }
        }
    }

    private func startSearchTask() {
        selectedLyric = nil
        searchResults = []
        searchTask?.cancel()
        searchTask = Task { @MainActor in
            do {
                try await searchLyrics()
            } catch {
                print("Search Task Error: \(error)")
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

    private func deduplicatedSearchResults(_ results: [SongResult]) -> [SongResult] {
        var seen: Set<String> = []
        return results.filter { result in
            let key = [
                result.lyricType,
                normalizedSearchMetadata(result.songName),
                normalizedSearchMetadata(result.artistName),
                normalizedSearchMetadata(result.albumName)
            ].joined(separator: "|")
            return seen.insert(key).inserted
        }
    }

    private func isRelevantSearchResult(_ result: SongResult) -> Bool {
        searchResultRelevance(result) > 0
    }

    private func searchResultRelevance(_ result: SongResult) -> Int {
        let resultTitle = normalizedSearchMetadata(result.songName)
        guard !resultTitle.isEmpty else {
            return 0
        }

        let titleScore: Int
        let queryTitles = titleSearchCandidates(for: trackName)
            .map(normalizedSearchMetadata)
            .filter { !$0.isEmpty }
        guard !queryTitles.isEmpty else {
            return 0
        }

        if queryTitles.contains(resultTitle) {
            titleScore = 100
        } else {
            guard queryTitles.contains(where: { queryTitle in
                let shorterCount = min(queryTitle.count, resultTitle.count)
                let longerCount = max(queryTitle.count, resultTitle.count)
                return shorterCount * 10 >= longerCount * 4
                    && (queryTitle.contains(resultTitle) || resultTitle.contains(queryTitle))
            }) else {
                return 0
            }
            titleScore = 70
        }

        let queryArtist = normalizedSearchMetadata(artistName)
        let resultArtist = normalizedSearchMetadata(result.artistName)
        guard !queryArtist.isEmpty, !resultArtist.isEmpty else {
            return titleScore
        }
        if queryArtist == resultArtist {
            return titleScore + 30
        }
        if queryArtist.contains(resultArtist) || resultArtist.contains(queryArtist) {
            return titleScore + 15
        }
        return titleScore
    }

    private func normalizedSearchMetadata(_ value: String) -> String {
        value
            .folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .unicodeScalars
            .filter(CharacterSet.alphanumerics.contains)
            .map(String.init)
            .joined()
    }

    private func titleSearchCandidates(for title: String) -> [String] {
        let stripped = title
            .replacingOccurrences(
                of: #"[\s　]*[\(\（\[\【].*?[\)\）\]\】][\s　]*"#,
                with: " ",
                options: .regularExpression
            )
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return [title, stripped]
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .reduce(into: []) { partialResult, candidate in
                if !partialResult.contains(candidate) {
                    partialResult.append(candidate)
                }
            }
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
                // cancel stale search tasks
                searchTask?.cancel()
                isFetching = false
                searchResults = []
                lyricsAreApplied = false
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
            .tint(viewmodel.currentBackground)
        .navigationTitle("Searching for \(viewmodel.currentlyPlayingName ?? "-") by \(viewmodel.currentlyPlayingArtist ?? "-")")
        .presentedWindowToolbarStyle(.unified)
    }
}
