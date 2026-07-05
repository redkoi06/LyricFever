//
//  ColoredThinProgressViewStyle.swift
//  Lyric Fever
//
//

import SwiftUI

struct ColoredThinProgressViewStyle: ProgressViewStyle {
    var color: Color
    var thickness: CGFloat

    func makeBody(configuration: Configuration) -> some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule()
                    .foregroundColor(color.opacity(0.3))
                    .frame(height: thickness)
                Capsule()
                    .foregroundColor(color)
                    .frame(width: CGFloat(configuration.fractionCompleted ?? 0) * geometry.size.width, height: thickness)
            }
        }
        .frame(height: thickness)
    }
}
