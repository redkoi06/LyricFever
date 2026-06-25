//
//  MenubarLabelView.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-08-05.
//

import AppKit
import SwiftUI

struct MenubarLabelView: View {
    let statusItemController: MenubarStatusItemController

    @AppStorage("karaoke") private var karaoke = true
    @AppStorage("truncationLength") private var storedTruncationLength = 10
    @Environment(ViewModel.self) var viewmodel

    private var truncationLength: Int {
        min(max(storedTruncationLength, 10), 20)
    }

    private var karaokeEnabled: Bool {
        if let value = UserDefaults.standard.object(forKey: "karaoke") as? Bool {
            return value
        }
        return karaoke
    }

    private var lyricDisplayWidth: CGFloat {
        let sampleText = String(repeating: "宽", count: truncationLength) as NSString
        let font = NSFont.menuBarFont(ofSize: 0)
        return ceil(sampleText.size(withAttributes: [.font: font]).width)
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
              !karaokeEnabled,
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

    private var labelContent: MenubarStatusLabelContent {
        if viewmodel.mustUpdateUrgent {
            return .text(String(localized: "⚠️ Please Update (Click Check Updates)"))
        } else if !viewmodel.userDefaultStorage.hasOnboarded {
            return .text(String(localized: "⚠️ Complete Setup (Click Settings)"))
        } else if let currentLyricText {
            return .lyric(currentLyricText)
        } else if viewmodel.isPlaying {
            if karaokeEnabled {
                return .icon("music.note.list")
            }
            return viewmodel.showLyrics ? .placeholderIcon("music.note") : .icon("music.note.list")
        } else if viewmodel.userDefaultStorage.showSongDetailsInMenubar,
                  let currentlyPlayingName = viewmodel.currentlyPlayingName,
                  let currentlyPlayingArtist = viewmodel.currentlyPlayingArtist {
            return .text(String(localized: "Now Paused: \(currentlyPlayingName) - \(currentlyPlayingArtist)"))
        } else {
            return .icon("music.note.list")
        }
    }

    private var snapshot: MenubarStatusSnapshot {
        MenubarStatusSnapshot(
            content: labelContent,
            truncationLength: truncationLength,
            lyricWidth: lyricDisplayWidth
        )
    }

    var body: some View {
        Group {
            switch labelContent {
            case .icon(let systemName):
                Image(systemName: systemName)

            case .text(let text):
                Text(verbatim: text.trunc(length: truncationLength))
                    .lineLimit(1)

            case .lyric, .placeholderIcon:
                Color.clear
                    .frame(width: 1, height: 1)
            }
        }
        .onAppear {
            statusItemController.update(snapshot)
        }
        .onChange(of: snapshot) {
            statusItemController.update(snapshot)
        }
        .onReceive(NotificationCenter.default.publisher(for: UserDefaults.didChangeNotification)) { _ in
            statusItemController.update(snapshot)
        }
    }
}

enum MenubarStatusLabelContent: Equatable {
    case icon(String)
    case lyric(String)
    case placeholderIcon(String)
    case text(String)
}

struct MenubarStatusSnapshot: Equatable {
    let content: MenubarStatusLabelContent
    let truncationLength: Int
    let lyricWidth: CGFloat
}

@MainActor
final class MenubarStatusItemController {
    private weak var statusItem: NSStatusItem?
    private let lyricView = MenubarStatusLyricView()
    private var currentSnapshot: MenubarStatusSnapshot?

    func attach(_ statusItem: NSStatusItem) {
        self.statusItem = statusItem
        if let currentSnapshot {
            apply(currentSnapshot)
        }
    }

    func update(_ snapshot: MenubarStatusSnapshot) {
        currentSnapshot = snapshot
        guard statusItem != nil else {
            return
        }
        apply(snapshot)
    }

    func resetToNativeLabel() {
        currentSnapshot = nil
        restoreNativeLabel()
    }

    private func restoreNativeLabel() {
        lyricView.stopScrolling()
        lyricView.removeFromSuperview()
        guard let statusItem else {
            return
        }

        statusItem.length = NSStatusItem.variableLength
        statusItem.button?.toolTip = nil
    }

    private func configureButtonIfNeeded() {
        guard let button = statusItem?.button else {
            return
        }

        if lyricView.superview !== button {
            lyricView.frame = button.bounds
            lyricView.autoresizingMask = [.width, .height]
            button.addSubview(lyricView)
        }
    }

    private func apply(_ snapshot: MenubarStatusSnapshot) {
        guard let statusItem, let button = statusItem.button else {
            return
        }

        switch snapshot.content {
        case .icon:
            restoreNativeLabel()

        case .text:
            restoreNativeLabel()

        case .lyric(let text):
            configureButtonIfNeeded()
            statusItem.length = snapshot.lyricWidth
            button.image = nil
            button.title = ""
            button.toolTip = text
            button.setAccessibilityLabel(text)
            lyricView.isHidden = false
            lyricView.update(text: text, width: snapshot.lyricWidth)

        case .placeholderIcon(let systemName):
            configureButtonIfNeeded()
            statusItem.length = snapshot.lyricWidth
            button.image = nil
            button.title = ""
            button.toolTip = nil
            button.setAccessibilityLabel(String(localized: "Song"))
            lyricView.isHidden = false
            lyricView.update(systemName: systemName, width: snapshot.lyricWidth)
        }
    }
}

private final class MenubarStatusLyricView: NSView {
    private let font = NSFont.menuBarFont(ofSize: 0)
    private let scrollSpeed: CGFloat = 42
    private let frameInterval: TimeInterval = 0.1
    private let initialPause: TimeInterval = 1.0

    private var text = ""
    private var systemName: String?
    private var scrollOffset: CGFloat = 0
    private var measuredTextWidth: CGFloat = 0
    private var lastTickDate: Date?
    private var pauseUntil = Date.distantPast
    private var timer: Timer?
    private var hasReachedEnd = false

    override var isFlipped: Bool {
        true
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        nil
    }

    func update(text: String, width: CGFloat) {
        if self.text != text || frame.width != width {
            self.text = text
            self.systemName = nil
            frame = NSRect(x: 0, y: 0, width: width, height: superview?.bounds.height ?? bounds.height)
            measuredTextWidth = ceil((text as NSString).size(withAttributes: attributes).width)
            resetScrolling()
        }

        if shouldScroll {
            startScrolling()
        } else {
            stopScrolling()
        }

        needsDisplay = true
    }

    func update(systemName: String, width: CGFloat) {
        if self.systemName != systemName || frame.width != width {
            self.text = ""
            self.systemName = systemName
            frame = NSRect(x: 0, y: 0, width: width, height: superview?.bounds.height ?? bounds.height)
            measuredTextWidth = 0
            resetScrolling()
        }

        stopScrolling()
        needsDisplay = true
    }

    func stopScrolling() {
        timer?.invalidate()
        timer = nil
        lastTickDate = nil
        scrollOffset = 0
        hasReachedEnd = false
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        if let systemName {
            drawSystemSymbol(named: systemName)
            return
        }

        guard !text.isEmpty else {
            return
        }

        let drawText = text as NSString
        let textSize = drawText.size(withAttributes: attributes)
        let drawY = floor((bounds.height - textSize.height) / 2)

        if shouldScroll {
            let x = -scrollOffset
            drawText.draw(at: NSPoint(x: x, y: drawY), withAttributes: attributes)
        } else {
            drawText.draw(
                with: NSRect(x: 0, y: drawY, width: bounds.width, height: textSize.height),
                options: [.usesLineFragmentOrigin, .usesFontLeading],
                attributes: centeredAttributes
            )
        }
    }

    private func resetScrolling() {
        scrollOffset = 0
        lastTickDate = nil
        pauseUntil = Date().addingTimeInterval(initialPause)
        hasReachedEnd = false
    }

    private func startScrolling() {
        guard timer == nil, !hasReachedEnd else {
            return
        }

        let timer = Timer(timeInterval: frameInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.tick()
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func tick() {
        let now = Date()
        let maximumOffset = maxScrollOffset
        guard maximumOffset > 0 else {
            stopScrolling()
            return
        }

        guard now >= pauseUntil else {
            lastTickDate = now
            return
        }

        let elapsed = lastTickDate.map { now.timeIntervalSince($0) } ?? 0
        lastTickDate = now
        scrollOffset += scrollSpeed * elapsed

        if scrollOffset >= maximumOffset {
            scrollOffset = maximumOffset
            hasReachedEnd = true
            timer?.invalidate()
            timer = nil
            lastTickDate = nil
        }

        needsDisplay = true
    }

    private var maxScrollOffset: CGFloat {
        max(measuredTextWidth - bounds.width, 0)
    }

    private var shouldScroll: Bool {
        maxScrollOffset > 0
    }

    private func drawSystemSymbol(named systemName: String) {
        guard let symbol = NSImage(systemSymbolName: systemName, accessibilityDescription: nil)?
            .withSymbolConfiguration(.init(pointSize: 14, weight: .semibold)) else {
            return
        }

        let symbolImage = NSImage(size: symbol.size)
        symbolImage.lockFocus()
        let symbolRect = NSRect(origin: .zero, size: symbol.size)
        symbol.draw(in: symbolRect, from: .zero, operation: .sourceOver, fraction: 1)
        NSColor.white.setFill()
        symbolRect.fill(using: .sourceAtop)
        symbolImage.unlockFocus()

        let drawRect = NSRect(
            x: floor((bounds.width - symbol.size.width) / 2),
            y: floor((bounds.height - symbol.size.height) / 2),
            width: symbol.size.width,
            height: symbol.size.height
        )

        symbolImage.draw(
            in: drawRect,
            from: .zero,
            operation: .sourceOver,
            fraction: 1,
            respectFlipped: true,
            hints: nil
        )
    }

    private var attributes: [NSAttributedString.Key: Any] {
        [
            .font: font,
            .foregroundColor: NSColor.labelColor
        ]
    }

    private var centeredAttributes: [NSAttributedString.Key: Any] {
        let paragraphStyle = NSMutableParagraphStyle()
        paragraphStyle.alignment = .center
        paragraphStyle.lineBreakMode = .byClipping

        return [
            .font: font,
            .foregroundColor: NSColor.labelColor,
            .paragraphStyle: paragraphStyle
        ]
    }

}
