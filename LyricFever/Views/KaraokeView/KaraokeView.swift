//
//  KaraokeView.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2024-10-08.
//

import SwiftUI
import SDWebImage
import ColorKit
import Combine

struct VisualEffectView: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = NSVisualEffectView()

        view.blendingMode = .behindWindow
        view.state = .active
        view.material = .hudWindow
        return view
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {
        //
        nsView.material = .hudWindow
        nsView.blendingMode = .behindWindow
    }
}

struct KaraokeView: View {
    @Environment(ViewModel.self) var viewmodel
    @AppStorage("karaokeTransparency") var karaokeTransparency: Double = 50
    @AppStorage("karaokeShowMultilingual") var karaokeShowMultilingual: Bool = true
    @AppStorage("karaokeShowRomanization") var karaokeShowRomanization: Bool = false
    @AppStorage("karaokeUseAlbumColor") var karaokeUseAlbumColor: Bool = true
    @AppStorage("fixedKaraokeColorHex") var fixedKaraokeColorHex: String = "#2D3CCC"

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
    
    func primaryWords(for currentlyPlayingLyricsIndex: Int) -> String? {
        if let convertedLyric = displayableLyric(
            viewmodel.chineseConversionLyrics[safe: currentlyPlayingLyricsIndex]
        ) {
            return convertedLyric
        }
        return displayableLyric(
            viewmodel.currentlyPlayingLyrics[safe: currentlyPlayingLyricsIndex]?.words
        )
    }

    var noLyricsView: some View {
        Image(systemName: "music.note")
            .font(.system(size: viewmodel.karaokeFont.pointSize, weight: .semibold))
            .accessibilityLabel(Text("No lyrics"))
    }
    
    @ViewBuilder
    func annotatedOriginal(_ currentlyPlayingLyricsIndex: Int, translation: String? = nil) -> some View {
        VStack(spacing: 4) {
            Text(verbatim: primaryWords(for: currentlyPlayingLyricsIndex) ?? "")
            if karaokeShowRomanization,
               let romanizedLyric = viewmodel.romanizedLyric(at: currentlyPlayingLyricsIndex) {
                Text(verbatim: romanizedLyric)
                    .font(.custom(viewmodel.karaokeFont.fontName, size: 0.7 * viewmodel.karaokeFont.pointSize))
                    .opacity(0.82)
            }
            if let translation {
                Text(verbatim: translation)
                    .font(.custom(viewmodel.karaokeFont.fontName, size: 0.82 * viewmodel.karaokeFont.pointSize))
                    .opacity(0.78)
            }
        }
    }
    
    func originalAndTranslationAreDifferent(for currentlyPlayingLyricsIndex: Int) -> Bool {
        guard let originalLyric = viewmodel.currentlyPlayingLyrics[safe: currentlyPlayingLyricsIndex]?.words,
              let translatedLyric = viewmodel.translatedLyric[safe: currentlyPlayingLyricsIndex] else {
            return false
        }
        return originalLyric != translatedLyric
    }
    
    @ViewBuilder
    func lyricsView() -> some View {
        if let currentlyPlayingLyricsIndex = viewmodel.currentlyPlayingLyricsIndex,
           viewmodel.currentlyPlayingLyrics[safe: currentlyPlayingLyricsIndex] != nil {
            let primaryLyric = primaryWords(for: currentlyPlayingLyricsIndex)
            let translatedLyric = displayableLyric(
                viewmodel.translatedLyric[safe: currentlyPlayingLyricsIndex]
            )
            if let translatedLyric {
                if primaryLyric != nil,
                   karaokeShowMultilingual,
                   originalAndTranslationAreDifferent(for: currentlyPlayingLyricsIndex) {
                    annotatedOriginal(currentlyPlayingLyricsIndex, translation: translatedLyric)
                }
                else {
                    Text(verbatim: translatedLyric)
                }
            } else if primaryLyric != nil {
                annotatedOriginal(currentlyPlayingLyricsIndex)
            } else {
                noLyricsView
            }
        } else {
            noLyricsView
        }
    }
    
    @ViewBuilder
    var finalKaraokeView: some View {
        lyricsView()
            .id(viewmodel.currentlyPlayingLyricsIndex)
            .lineLimit(3)
            .foregroundStyle(.white)
            .minimumScaleFactor(0.9)
            .font(.custom(viewmodel.karaokeFont.fontName, size: viewmodel.karaokeFont.pointSize))
            .padding(10)
            .padding(.horizontal, 10)
            .background {
               currentAlbumArt
               .transition(.opacity)
               .opacity(karaokeTransparency/100)
           }
//           .drawingGroup()
           .background(
               VisualEffectView().ignoresSafeArea()
           )
           .cornerRadius(16)
            .onHover { hover in
                if viewmodel.userDefaultStorage.karaokeModeHoveringSetting {
                    viewmodel.karaokeModeHovering = hover
                }
            }
            .multilineTextAlignment(.center)
            .frame(minWidth: 800, maxWidth: 800, minHeight: 130, maxHeight: 130, alignment: .center)
    }
    
    var currentAlbumArt: Color {
        // ensure user wants to use album-derived color, and album-derived color exists
        guard karaokeUseAlbumColor, let currentBackground = viewmodel.currentBackground else {
            return Color(NSColor(hexString: fixedKaraokeColorHex)!)
        }
        return currentBackground
    }
    
    var body: some View {
        finalKaraokeView
    }
}
