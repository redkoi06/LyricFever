//
//  OnboardingWindow.swift
//  SpotifyLyricsInMenubar
//
//

import SwiftUI
import SDWebImage
import ScriptingBridge
import MusicKit
import WebKit

struct OnboardingWindow: View {
    @State var spotifyPermission: Bool = false
    @Environment(\.dismiss) var dismiss
    @Environment(ViewModel.self) private var viewmodel

    var body: some View {
        @Bindable var viewmodel = viewmodel

        TabView(selection: $viewmodel.selectedSettingsTab) {
            MainSettingsView()
                .tag(SettingsTab.main)
                .tabItem {
                    Label("Main Settings", systemImage: "person.crop.circle")
                }
            TranslationSettingsView()
                .tag(SettingsTab.translation)
                .tabItem {
                    Label("翻译", systemImage: "translate")
                }
            KaraokeSettingsView()
                .padding(.horizontal, 100)
                .tag(SettingsTab.karaoke)
                 .tabItem {
                     Label("Karaoke Window", systemImage: "person.crop.circle")
                 }
            GlobalKeyboardShortcutsView()
                .padding(.horizontal, 100)
                .tag(SettingsTab.shortcuts)
                 .tabItem {
                     Label("Keyboard Shortcuts", systemImage: "keyboard")
                 }
            OtherSettingsView()
                .padding(.horizontal, 100)
                .tag(SettingsTab.other)
                 .tabItem {
                     Label("其他", systemImage: "gearshape.2")
                 }
        }
    }
}
