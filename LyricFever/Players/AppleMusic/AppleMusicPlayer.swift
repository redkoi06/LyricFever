//
//  AppleMusicPlayer.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-07-18.
//

import ScriptingBridge
import MusicKit
import AppKit

class AppleMusicPlayer: Player {
    private var appleMusicScript: MusicApplication?
    private var runningMusicScript: MusicApplication? {
        guard isRunning else {
            appleMusicScript = nil
            return nil
        }
        if appleMusicScript == nil {
            appleMusicScript = SBApplication(bundleIdentifier: "com.apple.Music")
        }
        return appleMusicScript
    }

    var persistentID: String? {
        guard isRunning else {
            return nil
        }
        return runningMusicScript?.currentTrack?.persistentID
    }
    var alternativeID: String? {
        guard isRunning else {
            return nil
        }
        let baseID = (runningMusicScript?.currentTrack?.artist ?? "") + (runningMusicScript?.currentTrack?.name ?? "")
        return baseID.count == 22 ? baseID + "_" : baseID
    }
    
    var albumName: String? {
        guard isRunning else {
            return nil
        }
        return runningMusicScript?.currentTrack?.album
    }
    var artistName: String? {
        guard isRunning else {
            return nil
        }
        return runningMusicScript?.currentTrack?.artist
    }
    var trackName: String? {
        guard isRunning else {
            return nil
        }
        return runningMusicScript?.currentTrack?.name
    }
    
    @MainActor
    var currentTime: TimeInterval? {
        guard isRunning else {
            return nil
        }
        guard let playerPosition = runningMusicScript?.playerPosition else {
            return nil
        }
        let viewmodel = ViewModel.shared
        return playerPosition * 1000
            + Double(viewmodel.currentManualLyricsOffsetMS)
            + (viewmodel.airplayDelay ? -2000 : 0)
    }
    var duration: Int? {
        guard isRunning else {
            return nil
        }
        guard let seconds = runningMusicScript?.currentTrack?.duration.map(Int.init) else {
            print("Apple Music Player: Couldn't fetch duration")
            return nil
        }
        return seconds * 1000
    }
    
    var isAuthorized: Bool {
        guard isRunning else {
            return false
        }
        if runningMusicScript?.playerState?.rawValue == 0 {
            return false
        }
        return true
    }
    var isPlaying: Bool {
        guard isRunning else {
            return false
        }
        return runningMusicScript?.playerState == .playing
    }
    var isRunning: Bool {
        if NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.Music").first != nil {
            return true
        } else {
            return false
        }
    }
    
    var volume: Int {
        guard isRunning else {
            return 0
        }
        return runningMusicScript?.soundVolume ?? 0
    }
    
    func decreaseVolume() {
        guard isRunning else {
            return
        }
        guard let soundVolume = runningMusicScript?.soundVolume else {
            return
        }
        runningMusicScript?.setSoundVolume?(soundVolume-5)
    }
    func increaseVolume() {
        guard isRunning else {
            return
        }
        guard let soundVolume = runningMusicScript?.soundVolume else {
            return
        }
        runningMusicScript?.setSoundVolume?(soundVolume+5)
    }
    func setVolume(to newVolume: Double) {
        guard isRunning else {
            return
        }
        runningMusicScript?.setSoundVolume?(Int(newVolume))
    }
    func togglePlayback() {
        guard isRunning else {
            return
        }
        runningMusicScript?.playpause?()
    }
    func rewind() {
        guard isRunning else {
            return
        }
        runningMusicScript?.previousTrack?()
    }
    func forward() {
        guard isRunning else {
            return
        }
        runningMusicScript?.nextTrack?()
    }
    
    var artworkImage: NSImage?
    
//    var artworkImage: NSImage? {
//        guard let artworkImage = (appleMusicScript?.currentTrack?.artworks?().firstObject as? MusicArtwork)?.data else {
//            print("AppleMusicPlayer artworkImage: nil data")
//            return nil
//        }
//        return artworkImage
//    }
    
    func activate() {
        guard isRunning else {
            return
        }
        runningMusicScript?.activate()
    }
    var currentHoverItem: MenubarButtonHighlight = .activateAppleMusic
}
