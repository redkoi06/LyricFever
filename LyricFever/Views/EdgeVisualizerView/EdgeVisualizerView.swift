//
//  EdgeVisualizerView.swift
//  Lyric Fever
//
//  Created by Codex on 2026-06-19.
//

#if os(macOS)
import SwiftUI

struct EdgeVisualizerView: View {
    @Environment(ViewModel.self) private var viewmodel

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30.0)) { timeline in
            GeometryReader { geometry in
                let idleLevel = viewmodel.edgeVisualizerStatus == nil && viewmodel.isPlaying ? 0.04 : 0.0
                let level = max(viewmodel.edgeVisualizerLevel, idleLevel)
                let color = viewmodel.currentBackground ?? Color.accentColor
                let phase = timeline.date.timeIntervalSinceReferenceDate
                EdgeVisualizerCanvas(level: level, color: color, phase: phase)
                    .frame(width: geometry.size.width, height: geometry.size.height)
            }
        }
        .ignoresSafeArea()
        .allowsHitTesting(false)
    }
}

private struct EdgeVisualizerCanvas: View {
    let level: Double
    let color: Color
    let phase: TimeInterval

    private var clampedLevel: Double {
        min(1.0, max(0.0, level))
    }

    var body: some View {
        GeometryReader { geometry in
            let size = geometry.size
            let baseThickness = 3.0 + clampedLevel * 10.0
            let glowThickness = baseThickness + 18.0 + clampedLevel * 28.0
            let opacity = 0.22 + clampedLevel * 0.58
            let pulseOpacity = 0.18 + clampedLevel * 0.45

            ZStack {
                edgeStrips(size: size, thickness: glowThickness)
                    .foregroundStyle(color.opacity(opacity * 0.55))
                    .blur(radius: 18 + clampedLevel * 16)

                edgeStrips(size: size, thickness: baseThickness)
                    .foregroundStyle(color.opacity(opacity))
                    .blur(radius: 2)

                movingHighlight(size: size, phase: phase, thickness: baseThickness + 6)
                    .foregroundStyle(
                        LinearGradient(colors: [.clear, .white.opacity(pulseOpacity), .clear],
                                       startPoint: .leading,
                                       endPoint: .trailing)
                    )
                    .blur(radius: 8)
            }
            .animation(.smooth(duration: 0.12), value: clampedLevel)
        }
    }

    @ViewBuilder
    private func edgeStrips(size: CGSize, thickness: Double) -> some View {
        Rectangle()
            .frame(width: size.width, height: thickness)
            .position(x: size.width / 2, y: thickness / 2)
        Rectangle()
            .frame(width: size.width, height: thickness)
            .position(x: size.width / 2, y: size.height - thickness / 2)
        Rectangle()
            .frame(width: thickness, height: size.height)
            .position(x: thickness / 2, y: size.height / 2)
        Rectangle()
            .frame(width: thickness, height: size.height)
            .position(x: size.width - thickness / 2, y: size.height / 2)
    }

    @ViewBuilder
    private func movingHighlight(size: CGSize, phase: TimeInterval, thickness: Double) -> some View {
        let horizontalWidth = max(120.0, size.width * 0.22)
        let verticalHeight = max(120.0, size.height * 0.22)
        let progress = (phase.truncatingRemainder(dividingBy: 3.2)) / 3.2
        let x = -horizontalWidth / 2 + (size.width + horizontalWidth) * progress
        let y = -verticalHeight / 2 + (size.height + verticalHeight) * progress

        Rectangle()
            .frame(width: horizontalWidth, height: thickness * 2)
            .position(x: x, y: thickness)
        Rectangle()
            .frame(width: horizontalWidth, height: thickness * 2)
            .position(x: size.width - x, y: size.height - thickness)
        Rectangle()
            .frame(width: thickness * 2, height: verticalHeight)
            .position(x: thickness, y: size.height - y)
        Rectangle()
            .frame(width: thickness * 2, height: verticalHeight)
            .position(x: size.width - thickness, y: y)
    }
}
#endif
