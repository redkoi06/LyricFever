//
//  UserDefaultStorage.swift
//  Lyric Fever
//
//

import Combine
import SwiftUI
//import ObservableDefaults
import ObservableUserDefault


//@ObservableDefaults
@Observable
class UserDefaultStorage {
    @ObservableUserDefault(.init(key: "translate", defaultValue: false, store: .standard))
    @ObservationIgnored var translate: Bool
    @ObservableUserDefault(.init(key: "translationTargetLanguage", store: .standard))
    @ObservationIgnored var translationTargetLanguage: Locale.Language?
//    var furigana = false
    #if os(macOS)
    @ObservableUserDefault(.init(key: "showSongDetailsInMenubar", defaultValue: false, store: .standard))
    @ObservationIgnored var showSongDetailsInMenubar: Bool
    #endif
    @ObservableUserDefault(.init(key: "blurFullscreen", defaultValue: true, store: .standard))
    @ObservationIgnored var blurFullscreen: Bool
    @ObservableUserDefault(.init(key: "animateOnStartupFullscreen", defaultValue: true, store: .standard))
    @ObservationIgnored var animateOnStartupFullscreen: Bool
    @ObservableUserDefault(.init(key: "romanize", defaultValue: false, store: .standard))
    @ObservationIgnored var romanize: Bool
    @ObservableUserDefault(.init(key: "chinesePreference", defaultValue: 0, store: .standard))
    @ObservationIgnored var chinesePreference: Int
    #if os(macOS)
    @ObservableUserDefault(.init(key: "spotifyConnectDelayCount", defaultValue: 400, store: .standard))
    @ObservationIgnored var spotifyConnectDelayCount: Int
    @ObservableUserDefault(.init(key: "hasMigrated", defaultValue: false, store: .standard))
    @ObservationIgnored var hasMigrated: Bool
    
    // User setting: use album art color or user-set currentBackground
    @ObservableUserDefault(.init(key: "karaoke", defaultValue: true, store: .standard))
    @ObservationIgnored var karaoke: Bool
//    var karaokeUseAlbumColor: Bool = true
    @ObservableUserDefault(.init(key: "karaokeShowMultilingual", defaultValue: true, store: .standard))
    @ObservationIgnored var karaokeShowMultilingual: Bool
    @ObservableUserDefault(.init(key: "karaokeShowRomanization", defaultValue: false, store: .standard))
    @ObservationIgnored var karaokeShowRomanization: Bool
    @ObservableUserDefault(.init(key: "karaokeTransparency", defaultValue: 50, store: .standard))
    @ObservationIgnored var karaokeTransparency: Double
//    var fixedKaraokeColorHex: String = "#2D3CCC"
    
    // User setting: hide karaoke on hover
    @ObservableUserDefault(.init(key: "karaokeModeHoveringSetting", defaultValue: false, store: .standard))
    @ObservationIgnored var karaokeModeHoveringSetting: Bool
    #endif

    private var spotifyCookie: String

    var cookie: String {
        get { spotifyCookie }
        set {
            spotifyCookie = newValue
            do {
                try CredentialStore.saveSpotifyCookie(newValue)
                UserDefaults.standard.removeObject(forKey: "spDcCookie")
            } catch {
                print("[LyricFever][Security] failed to update Spotify credential: \(error)")
            }
        }
    }
    
    #if os(macOS)
    // False: Spotify, True: Apple Music
    @ObservableUserDefault(.init(key: "spotifyOrAppleMusic", defaultValue: false, store: .standard))
    @ObservationIgnored var spotifyOrAppleMusic: Bool
    #endif
    @ObservableUserDefault(.init(key: "hasOnboarded", defaultValue: false, store: .standard))
    @ObservationIgnored var hasOnboarded: Bool
    @ObservableUserDefault(.init(key: "hasTranslated", defaultValue: false, store: .standard))
    @ObservationIgnored var hasTranslated: Bool
    @ObservableUserDefault(.init(key: "truncationLength", defaultValue: 10, store: .standard))
    @ObservationIgnored var truncationLength: Int

    init() {
        let legacyCookie = UserDefaults.standard.string(forKey: "spDcCookie") ?? ""
        do {
            if let storedCookie = try CredentialStore.spotifyCookie(), !storedCookie.isEmpty {
                spotifyCookie = storedCookie
                UserDefaults.standard.removeObject(forKey: "spDcCookie")
            } else {
                spotifyCookie = legacyCookie
                if !legacyCookie.isEmpty {
                    try CredentialStore.saveSpotifyCookie(legacyCookie)
                    UserDefaults.standard.removeObject(forKey: "spDcCookie")
                }
            }
        } catch {
            spotifyCookie = legacyCookie
            print("[LyricFever][Security] Spotify credential migration deferred: \(error)")
        }
    }
}
