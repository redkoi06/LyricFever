//
//  MenubarLabelView.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-08-05.
//

import SwiftUI

struct MenubarLabelView: View {
    @Environment(ViewModel.self) var viewmodel

    private enum LabelContent {
        case icon(String)
        case text(String)
    }

    private var truncationLength: Int {
        max(viewmodel.userDefaultStorage.truncationLength, 1)
    }

    private func displayableLyric(_ lyric: String?) -> String? {
        guard let lyric else {
            return nil
        }
        let trimmed = lyric.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            return nil
        }
        let musicalPlaceholders = CharacterSet(charactersIn: "♪♫♩♬")
        guard !trimmed.unicodeScalars.allSatisfy({
            musicalPlaceholders.contains($0) || CharacterSet.whitespacesAndNewlines.contains($0)
        }) else {
            return nil
        }
        return trimmed
    }

    private var currentLyricText: String? {
        guard !viewmodel.fullscreen,
              !viewmodel.userDefaultStorage.karaoke,
              viewmodel.isPlaying,
              viewmodel.showLyrics,
              let currentlyPlayingLyricsIndex = viewmodel.currentlyPlayingLyricsIndex,
              let currentLyric = viewmodel.currentlyPlayingLyrics[safe: currentlyPlayingLyricsIndex] else {
            return nil
        }

        if let translatedLyric = displayableLyric(
            viewmodel.translatedLyric[safe: currentlyPlayingLyricsIndex]
        ) {
            return translatedLyric
        }

        if viewmodel.userDefaultStorage.romanize,
           let romanizedLyric = displayableLyric(
            viewmodel.romanizedLyric(at: currentlyPlayingLyricsIndex)
           ) {
            return romanizedLyric
        }

        if let convertedLyric = displayableLyric(
            viewmodel.chineseConversionLyrics[safe: currentlyPlayingLyricsIndex]
        ) {
            return convertedLyric
        }

        return displayableLyric(currentLyric.words)
    }

    private var labelContent: LabelContent {
        if viewmodel.mustUpdateUrgent {
            return .text(String(localized: "⚠️ Please Update (Click Check Updates)"))
        } else if !viewmodel.userDefaultStorage.hasOnboarded {
            return .text(String(localized: "⚠️ Complete Setup (Click Settings)"))
        } else if let currentLyricText {
            return .text(currentLyricText)
        } else if viewmodel.isPlaying {
            return .icon("music.note")
        } else if viewmodel.userDefaultStorage.showSongDetailsInMenubar,
                  let currentlyPlayingName = viewmodel.currentlyPlayingName,
                  let currentlyPlayingArtist = viewmodel.currentlyPlayingArtist {
            return .text(String(localized: "Now Paused: \(currentlyPlayingName) - \(currentlyPlayingArtist)"))
        } else {
            return .icon("music.note.list")
        }
    }

    var body: some View {
        switch labelContent {
        case .icon(let systemName):
            Image(systemName: systemName)
        case .text(let text):
            Text(verbatim: text.trunc(length: truncationLength))
                .lineLimit(1)
        }
    }
}
