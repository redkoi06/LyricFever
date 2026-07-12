//
//  MainSettingsView.swift
//  Lyric Fever
//
//

//
//  MainSettingsView.swift
//  Lyric Fever
//
//

import SwiftUI
import SDWebImageSwiftUI

enum MainSettingsError: Error, Identifiable, CaseIterable {
    case openSpotify
    case openAppleMusic
    case missingAuthorization
    case authorized
    
    var id: Self { self }
    
    var description: LocalizedStringKey {
        switch self {
        case .openSpotify:
            return LocalizedStringKey("Please open Spotify!")
        case .openAppleMusic:
            return LocalizedStringKey("Please open Apple Music!")
        case .missingAuthorization:
            return LocalizedStringKey("Please give required permissions!")
        case .authorized:
            return " "
        }
    }
}

struct MainSettingsView: View {
    @Environment(ViewModel.self) var viewModel
    @State var permissionDenied: Bool = false
    @State var error: MainSettingsError = .openSpotify
    
    @ViewBuilder
    var permissionDeniedView: some View {
        AnimatedImage(name: "newPermissionMac.gif")
            .resizable()
            .frame(width: 397, height: 340)
        HStack {
            Button("Open Automation Panel", action: {
                let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")!
                NSWorkspace.shared.open(url)
            })
        }
    }
    
    @ViewBuilder
    var onboardView: some View {
        Image("hi")
            .resizable()
            .frame(width: 150, height: 150, alignment: .center)
                    
        Text("Welcome to Lyric Fever! 🎉")
            .font(.largeTitle)
                    
        Text("Please pick between Spotify and Apple Music")
            .font(.title)
    }
    
    @ViewBuilder
    var permissionsOrNextButton: some View {
        if error == .authorized {
            NavigationLink("Next", destination: ApiView())
                .font(.headline)
                .controlSize(.large)
                .buttonStyle(.borderedProminent)
        } else {
            switch viewModel.currentPlayer {
            case .spotify:
                Button("Give Spotify Permissions") {
                    if !viewModel.spotifyPlayer.isRunning {
                        print("Spotify not running")
                        error = .openSpotify
                    } else if !viewModel.spotifyPlayer.isAuthorized {
                        error = .openSpotify
                        permissionDenied = true
                    } else {
                        permissionDenied = false
                        error = .authorized
                    }
                }
            case .appleMusic:
                Button("Give Apple Music Permissions") {
                    if !viewModel.appleMusicPlayer.isRunning {
                        error = .openAppleMusic
                    } else if !viewModel.appleMusicPlayer.isAuthorized {
                        error = .openAppleMusic
                        permissionDenied = true
                    } else {
                        permissionDenied = false
                        error = .authorized
                    }
                }
            }
        }
    }

    private func resetPermissionStatus(for player: PlayerType) {
        permissionDenied = false
        error = player == .appleMusic ? .openAppleMusic : .openSpotify
    }
    
    var body: some View {
        @Bindable var viewmodel = viewModel
        NavigationStack {
            VStack(alignment: .center, spacing: 20) {
                Group {
                    if permissionDenied {
                        permissionDeniedView
                    } else {
                        onboardView
                    }
                }
                .transition(.fade)
                
                Picker("", selection: $viewmodel.currentPlayer) {
                    VStack {
                        Image("spotify")
                            .resizable()
                            .frame(width: 70.0, height: 70.0)
                        Text("Spotify")
                    }.tag(PlayerType.spotify)
                    VStack {
                        Image("music")
                            .resizable()
                            .frame(width: 70.0, height: 70.0)
                        Text("Apple Music")
                    }.tag(PlayerType.appleMusic)
                }
                .font(.title2)
                .frame(width: 500)
                .pickerStyle(.radioGroup)
                .horizontalRadioGroupLayout()
                            
                Text(error.description)
                    .transition(.opacity)
                            
                permissionsOrNextButton
                    .frame(height: 40)
            }
            .animation(.bouncy, value: permissionDenied)
            .animation(.bouncy, value: error)
            .onAppear {
                resetPermissionStatus(for: viewModel.currentPlayer)
            }
            .onChange(of: viewModel.currentPlayer) {
                print("Updating permission booleans based on media player change")
                resetPermissionStatus(for: viewModel.currentPlayer)
            }
        }
    }
}
