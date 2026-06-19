//
//  EdgeVisualizerPanel.swift
//  Lyric Fever
//
//  Created by Codex on 2026-06-19.
//

#if os(macOS)
import AppKit
import SwiftUI

extension View {
    func edgeVisualizerPanel(isPresented: Binding<Bool>) -> some View {
        modifier(EdgeVisualizerPanelModifier(isPresented: isPresented))
    }
}

private struct EdgeVisualizerPanelModifier: ViewModifier {
    @Binding var isPresented: Bool
    @State private var controller = EdgeVisualizerPanelController()

    func body(content: Content) -> some View {
        content
            .onAppear {
                if isPresented {
                    controller.show()
                }
            }
            .onDisappear {
                controller.close()
            }
            .onChange(of: isPresented) {
                if isPresented {
                    controller.show()
                } else {
                    controller.close()
                }
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didChangeScreenParametersNotification)) { _ in
                controller.refresh()
            }
    }
}

@MainActor
private final class EdgeVisualizerPanelController {
    private var panels: [NSPanel] = []

    func show() {
        close()

        for screen in NSScreen.screens {
            let panel = EdgeVisualizerPanel(screenFrame: screen.frame)
            let hostingView = NSHostingView(rootView: EdgeVisualizerView()
                .environment(ViewModel.shared))
            hostingView.layer?.backgroundColor = NSColor.clear.cgColor
            panel.contentView = hostingView
            panel.orderFrontRegardless()
            panels.append(panel)
        }
    }

    func refresh() {
        guard !panels.isEmpty else { return }
        show()
    }

    func close() {
        for panel in panels {
            panel.orderOut(nil)
            panel.close()
        }
        panels.removeAll()
    }
}

private final class EdgeVisualizerPanel: NSPanel {
    init(screenFrame: NSRect) {
        super.init(contentRect: screenFrame,
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered,
                   defer: false)

        setFrame(screenFrame, display: true)
        isFloatingPanel = true
        level = .screenSaver
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        hidesOnDeactivate = false
        ignoresMouseEvents = true
        isMovable = false
        titleVisibility = .hidden
        titlebarAppearsTransparent = true
    }

    override var canBecomeKey: Bool {
        false
    }

    override var canBecomeMain: Bool {
        false
    }
}
#endif
