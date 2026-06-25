//
//  OtherSettingsView.swift
//  Lyric Fever
//
//  Created by Codex on 2026-06-25.
//

import SwiftUI

struct OtherSettingsView: View {
    @Environment(ViewModel.self) private var viewmodel
    @State private var cacheInfo = ViewModel.LyricCacheInfo.empty
    @State private var didClearCache = false

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("其他")
                .font(.system(size: 15, weight: .bold))

            VStack(alignment: .leading, spacing: 12) {
                Text("歌词缓存")
                    .font(.headline)

                cacheInfoRow(title: "当前缓存大小", systemImage: "externaldrive", value: cacheInfo.formattedSize)
                cacheInfoRow(title: "已缓存歌曲", systemImage: "music.note.list", value: "\(cacheInfo.songCount)")
                cacheInfoRow(title: "歌词行数", systemImage: "text.alignleft", value: "\(cacheInfo.lineCount)")

                Button(role: .destructive) {
                    cacheInfo = viewmodel.clearLyricCache()
                    didClearCache = true
                } label: {
                    Label("清空歌词缓存", systemImage: "trash")
                }
                .buttonStyle(.borderedProminent)
                .tint(.red)
                .disabled(cacheInfo.songCount == 0)

                if didClearCache {
                    Text("已清空歌词缓存")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .frame(width: 320, alignment: .leading)

            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .padding(.horizontal)
        .padding(.top, 100)
        .onAppear {
            cacheInfo = viewmodel.currentLyricCacheInfo()
            didClearCache = false
        }
    }

    private func cacheInfoRow(title: String, systemImage: String, value: String) -> some View {
        HStack {
            Label(title, systemImage: systemImage)
            Spacer()
            Text(value)
                .monospacedDigit()
                .foregroundStyle(.secondary)
        }
    }
}
