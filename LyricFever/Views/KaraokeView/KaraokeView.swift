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
    
    func primaryWords(for currentlyPlayingLyricsIndex: Int) -> String? {
        if let convertedLyric = viewmodel.chineseConversionLyrics[safe: currentlyPlayingLyricsIndex] {
            return convertedLyric
        }
        return viewmodel.currentlyPlayingLyrics[safe: currentlyPlayingLyricsIndex]?.words
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
            if let translatedLyric = viewmodel.translatedLyric[safe: currentlyPlayingLyricsIndex] {
                if karaokeShowMultilingual, originalAndTranslationAreDifferent(for: currentlyPlayingLyricsIndex) {
                    annotatedOriginal(currentlyPlayingLyricsIndex, translation: translatedLyric)
                }
                else {
                    Text(verbatim: translatedLyric)
                }
            } else {
                annotatedOriginal(currentlyPlayingLyricsIndex)
            }
        } else {
            Text("")
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
