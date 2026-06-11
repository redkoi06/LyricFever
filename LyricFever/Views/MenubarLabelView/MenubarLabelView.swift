//
//  MenubarLabelView.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-08-05.
//

import SwiftUI

struct MenubarLabelView: View {
    @Environment(ViewModel.self) var viewmodel
    
    var menuBarTitle: String? {
        // Update message takes priority
        if viewmodel.mustUpdateUrgent {
            return String(localized: "⚠️ Please Update (Click Check Updates)")
        } else if viewmodel.userDefaultStorage.hasOnboarded {
            // Try to work through lyric logic if onboarded
            // NEW: Revert to song name if fullscreen / karaoke activated
            if !viewmodel.fullscreen,
               !viewmodel.userDefaultStorage.karaoke,
               viewmodel.isPlaying,
               viewmodel.showLyrics,
               let currentlyPlayingLyricsIndex = viewmodel.currentlyPlayingLyricsIndex,
               let currentLyric = viewmodel.currentlyPlayingLyrics[safe: currentlyPlayingLyricsIndex] {
                // Attempt to display translations
                if let translatedLyric = viewmodel.translatedLyric[safe: currentlyPlayingLyricsIndex] {
                    // I don't localize, because I deliver the lyric verbatim
                    return translatedLyric
                } else {
                    // Attempt to display Romanization
                    if let romanizedLyric = viewmodel.romanizedLyrics[safe: currentlyPlayingLyricsIndex] {
                        return romanizedLyric
                    } else if let convertedLyric = viewmodel.chineseConversionLyrics[safe: currentlyPlayingLyricsIndex] {
                        return convertedLyric
                    } else {
                        return currentLyric.words
                    }
                }
            // Backup: Display name and artist
            } else if viewmodel.userDefaultStorage.showSongDetailsInMenubar, let currentlyPlayingName = viewmodel.currentlyPlayingName, let currentlyPlayingArtist = viewmodel.currentlyPlayingArtist {
                if viewmodel.isPlaying {
                    return String(localized: "Now Playing: \(currentlyPlayingName) - \(currentlyPlayingArtist)")
                } else {
                    return String(localized: "Now Paused: \(currentlyPlayingName) - \(currentlyPlayingArtist)")
                }
            }
            // Onboarded but app is not open
            return nil
        } else {
            // Hasn't onboarded
            return String(localized: "⚠️ Complete Setup (Click Settings)")
        }
    }

    var body: some View {
        Group {
            if let menuBarTitle {
                Text(menuBarTitle.trunc())
            } else {
                Image(systemName: "music.note.list")
            }
        }
    }
}
