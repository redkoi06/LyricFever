//
//  viewModel.swift
//  SpotifyLyricsInMenubar
//
//  Created by Avi Wadhwa on 14/08/23.
//

import Foundation
#if os(macOS)
#endif
import CoreData
import AmplitudeSwift
import SwiftUI
import MediaPlayer
#if os(macOS)
import WebKit
import Translation
import KeyboardShortcuts
import MediaRemoteAdapter
#endif

@MainActor
@Observable class ViewModel {
    static let shared = ViewModel()
    
    // Apple Music Tahoe broken AppleScript workaround
    let musicController = MediaController()
//    var appleMusicUniqueIdentifier: String?

    var currentlyPlaying: String?
    
    var currentVolume: Int = 0
    var isStopped = false
    
    var artworkImage: NSImage?
    var currentArtworkURL: URL?

    var duration: Int = 0
    var currentTime = CurrentTimeWithStoredDate(currentTime: 0)
    
    var formattedCurrentTime: String {
        let baseTime = currentTime.currentTime
        let totalSeconds = Int(baseTime) / 1000
        let formatter = DateComponentsFormatter()
        formatter.allowedUnits = [.minute, .second]
        formatter.zeroFormattingBehavior = [.pad]
        return formatter.string(from: TimeInterval(totalSeconds)) ?? "0:00"
    }
    private func initAppleMusicWorkaround() {
        musicController.onTrackInfoReceived = { (data: TrackInfo?) in
            print("Track info received application=\(data?.payload.applicationName ?? "nil") hasArtwork=\(data?.payload.artwork != nil)")
            Task { @MainActor in
//                if self.appleMusicUniqueIdentifier == data.payload.uniqueIdentifier {
//                    print("Apple Music Artwork Workaround: Ignoring artwork for existing song")
//                    return
//                } else {
//                    self.appleMusicUniqueIdentifier = data.payload.uniqueIdentifier
//                }
                guard self.currentPlayer == .appleMusic else {
                    return
                }
                guard let artwork = data?.payload.artwork else {
                    if self.currentlyPlaying == nil {
                        self.artworkImage = nil
                    }
                    print("Apple Music Artwork Workaround: Ignoring No Artwork")
                    return
                }
                // MediaRemoteAdapter's applicationName can vary between macOS releases.
                // currentPlayer already scopes this path to Apple Music for Lyric Fever.
                self.artworkImage = artwork
            }
            // This will only be called for Apple Music events
        }
        musicController.startListening()
    }
    
    func formattedCurrentTime(for date: Date) -> String {
        let baseTime = currentTime.currentTime
        let delta = date.timeIntervalSince(currentTime.storedDate)
//        print("Formatted Current Time: delta is \(delta)")
        let totalSeconds = Int((baseTime + delta) / 1000)
//        print("total seconds should be \(totalSeconds)")
        let formatter = DateComponentsFormatter()
        formatter.allowedUnits = [.minute, .second]
        formatter.zeroFormattingBehavior = [.pad]
        return formatter.string(from: TimeInterval(totalSeconds)) ?? "0:00"
    }
    
    var formattedDuration: String {
        let totalSeconds = duration / 1000
        let formatter = DateComponentsFormatter()
        formatter.allowedUnits = [.minute, .second]
        formatter.zeroFormattingBehavior = [.pad]
        return formatter.string(from: TimeInterval(totalSeconds)) ?? "0:00"
    }
    
    #if os(macOS)
    var updaterService = UpdaterService()
    var appleMusicPlayer = AppleMusicPlayer()
    var spotifyPlayer = SpotifyPlayer()
    #else
    var currentTab = TabType.nowPlaying
    var spotifyPlayer = TVSpotifyPlayer()
    var hasWebApiOnboarded = false
    #endif
    
    var currentPlayerInstance: Player {
        #if os(macOS)
        switch currentPlayer {
            case .appleMusic:
                return appleMusicPlayer
            case .spotify:
                return spotifyPlayer
        }
        #else
        return spotifyPlayer
        #endif
    }
    
    #if os(macOS)
    var translationSessionConfig: TranslationSession.Configuration?
    #endif
    var userDefaultStorage = UserDefaultStorage()
    
    #if os(macOS)
    // Karaoke Font
    var karaokeFont: NSFont
    
    // nil to deal with previously saved songs that don't have lang saved with them
    // or for LRCLIB
    var currentBackground: Color? = nil
    
    var animatedDisplay: Bool {
        get {
            displayKaraoke || fullscreen
        }
        set {
            
        }
    }
    
    var canDisplayLyrics: Bool {
        showLyrics && !lyricsIsEmptyPostLoad
    }

    var displayKaraoke: Bool {
        get {
            showLyrics && isPlaying && userDefaultStorage.karaoke && !karaokeModeHovering
        }
        set {
            
        }
    }
    var displayFullscreen: Bool {
        get {
            fullscreen
        }
        set {
            if fullscreen {
                NSApp.windows.first {$0.identifier?.rawValue == "fullscreen"}?.makeKeyAndOrderFront(self)
                NSApplication.shared.activate(ignoringOtherApps: true)
            } else {
                fullscreen = true
                NSApp.setActivationPolicy(.regular)
            }
        }
    }
    var currentlyPlayingAppleMusicPersistentID: String? = nil
    #endif
    
    var currentlyPlayingName: String?
    var currentlyPlayingArtist: String?
    var currentAlbumName: String?
    var currentlyPlayingLyrics: [LyricLine] = []
    var currentlyPlayingLyricsIndex: Int?
    var isPlaying: Bool = false
    var romanizedLyrics: [String] = []
    var chineseConversionLyrics: [String] = []
    var translatedLyric: [String] = []
    var showLyrics = true
    #if os(macOS)
    var fullscreen = false
    var spotifyConnectDelay: Bool = false
    var airplayDelay: Bool = false
    #endif
    var isFetchingTranslation = false
    var translationExists: Bool { !translatedLyric.isEmpty}
    
    // CoreData container (for saved lyrics)
    let coreDataContainer: NSPersistentContainer
    
    // Logging / Analytics
    let amplitude = Amplitude(configuration: .init(apiKey: amplitudeKey))
    
    var isHearted = false
    
    // Async Tasks (Lyrics fetch, Apple Music -> Spotify ID fetch, Lyrics Updater)
    private var currentFetchTask: Task<[LyricLine], Error>?
    private var currentLyricsUpdaterTask: Task<Void,Error>?
    private var currentLyricsDriftFix: Task<Void,Error>?
    private var currentArtworkFetchTask: Task<Void, Never>?
    private var currentAppleMusicWatchdogTask: Task<Void, Never>?
    private var currentSpotifyWatchdogTask: Task<Void, Never>?
    private var currentSpotifyEmptyLyricsRetryTask: Task<Void, Never>?
    private var currentRomanizationTask: Task<Void, Never>?
    private var spotifyEmptyLyricsRetryCount = 0
    private var lastAppleMusicWatchdogTrackID: String?
    private var lastAppleMusicWatchdogPosition: TimeInterval?
    private var lastSpotifyWatchdogTrackID: String?
    private var lastSpotifyWatchdogPosition: TimeInterval?
    private let spotifyEmptyLyricsRetryLimit = 2
    private let spotifyEmptyLyricsRetryDelay: UInt64 = 3_000_000_000
    var isFetching = false
    private var currentAppleMusicFetchTask: Task<Void,Error>?
    
    // Songs are translated to user locale
    let systemLocale: Locale
    let systemLocaleString: String
    var translationSourceLanguage: Locale.Language?
//    var translationTargetLanguage: Locale.Language?
    var userLocaleLanguage: Locale.Language {
        if let translationTargetLanguage = userDefaultStorage.translationTargetLanguage {
            return translationTargetLanguage
        } else {
            return systemLocale.language
        }
    }
    var userLocaleLanguageString: String {
        if let translationTargetLanguage = userDefaultStorage.translationTargetLanguage, let translationTargetLanguageString = Locale.current.localizedString(forIdentifier: translationTargetLanguage.minimalIdentifier) {
            return translationTargetLanguageString
        } else {
            return systemLocaleString
        }
    }

    // Override menubar with an update message
    var mustUpdateUrgent: Bool = false

    // Delayed variable to hook onto for fullscreen (whether to display lyrics or not)
    // Prevents flickering that occurs when we directly bind to currentlyPlayingLyrics.isEmpty()
    var lyricsIsEmptyPostLoad: Bool = true
    
    #if os(macOS)
    // UI element used to hide if karaokeModeHoveringSetting is true
    var karaokeModeHovering: Bool = false
    
    #endif
    
    #if os(macOS)
    var currentPlayer: PlayerType {
        get {
            if self.userDefaultStorage.spotifyOrAppleMusic {
                return .appleMusic
            } else {
                return .spotify
            }
        } set {
            if newValue == .appleMusic {
                self.userDefaultStorage.spotifyOrAppleMusic = true
            } else {
                self.userDefaultStorage.spotifyOrAppleMusic = false
            }
        }
    }
    #else
    @ObservationIgnored var currentPlayer: Player {
        return spotifyPlayer
    }
    #endif
    
    var currentDuration: Int? {
        currentPlayerInstance.duration
    }
    var isPlayerRunning: Bool {
        currentPlayerInstance.isRunning
    }
    
    var spotifyLyricProvider = SpotifyLyricProvider()
    var lRCLyricProvider = LRCLIBLyricProvider()
    var netEaseLyricProvider = NetEaseLyricProvider()
    #if os(macOS)
    var localFileUploadProvider = LocalFileUploadProvider()
    #endif
    @ObservationIgnored lazy var allNetworkLyricProviders: [LyricProvider] = [spotifyLyricProvider, lRCLyricProvider, netEaseLyricProvider]
    
    // custom order because LRCLIB is tweaking for the time being
    @ObservationIgnored lazy var allNetworkLyricProvidersForSearch: [LyricProvider] = [spotifyLyricProvider, netEaseLyricProvider, lRCLyricProvider]
    
    var isFirstFetch = true
    
    init() {
        // Set our user locale for translation language
        systemLocale = Locale.preferredLocale()
        systemLocaleString = Locale.preferredLocaleString() ?? ""
        
        #if os(macOS)
        // Generate user-saved font and load it
        let karaokeFontSize: Double = UserDefaults.standard.double(forKey: "karaokeFontSize")
        let karaokeFontName: String? = UserDefaults.standard.string(forKey: "karaokeFontName")
        if let karaokeFontName, karaokeFontSize != 0, let ourKaraokeFont = NSFont(name: karaokeFontName, size: karaokeFontSize) {
            karaokeFont = ourKaraokeFont
        } else {
            karaokeFont = NSFont.boldSystemFont(ofSize: 30)
        }
        #endif
        
        
        // Load our CoreData container for Lyrics
        coreDataContainer = NSPersistentContainer(name: "Lyrics")
        
        initAppleMusicWorkaround()
        
        coreDataContainer.loadPersistentStores { description, error in
            if let error = error {
                print("[LyricFever][CoreData] persistent store failed to load: \(error.localizedDescription)")
                return
            }
            self.coreDataContainer.viewContext.mergePolicy = NSMergePolicy.overwrite
        }
        #if os(macOS)
        migrateTimestampsIfNeeded(context: coreDataContainer.viewContext)
        
        
        // Check if user must urgently update (overrides menubar)
        Task {
            mustUpdateUrgent = await updaterService.urgentUpdateExists
        }
        
        // onAppear()
        print("on appear running")
        if userDefaultStorage.latestUpdateWindowShown < 23 {
            return
        }
        #endif
        if userDefaultStorage.cookie.count == 0 {
            print("Setting hasOnboarded to false due to empty cookie")
            userDefaultStorage.hasOnboarded = false
            return
        }
        guard userDefaultStorage.hasOnboarded else {
            return
        }
        guard isPlayerRunning else {
            return
        }
        print("Application just started. lets check whats playing")
        
        isPlaying = currentPlayerInstance.isPlaying
        userDefaultStorage.hasOnboarded = currentPlayerInstance.isAuthorized
        KeyboardShortcuts.onKeyUp(for: .init("karaoke")) { [self] in
            userDefaultStorage.karaoke.toggle()
        }
        KeyboardShortcuts.onKeyUp(for: .init("lyrics")) { [self] in
            showLyrics.toggle()
        }
        KeyboardShortcuts.onKeyUp(for: .init("translate")) { [self] in
            userDefaultStorage.translate.toggle()
        }
        KeyboardShortcuts.onKeyUp(for: .init("romanize")) { [self] in
            userDefaultStorage.romanize.toggle()
        }
        KeyboardShortcuts.onKeyUp(for: .init("fullscreen")) { [self] in
            displayFullscreen.toggle()
        }
        guard userDefaultStorage.hasOnboarded else {
            return
        }
        
    }
    
    @MainActor
    func fetchAllNetworkLyrics() async -> NetworkFetchReturn {
        guard let currentlyPlaying, let currentlyPlayingName else {
            spotifySyncLog("fetchAllNetworkLyrics aborted: missing currentlyPlaying or name")
            return NetworkFetchReturn(lyrics: [], colorData: nil)
        }
        for networkLyricProvider in allNetworkLyricProviders {
            do {
                spotifySyncLog("fetchAllNetworkLyrics provider=\(networkLyricProvider.providerName) start trackID=\(currentlyPlaying)")
                let lyrics = try await networkLyricProvider.fetchNetworkLyrics(trackName: currentlyPlayingName, trackID: currentlyPlaying, currentlyPlayingArtist: currentlyPlayingArtist, currentAlbumName: currentAlbumName)
                if !lyrics.lyrics.isEmpty {
                    amplitude.track(eventType: "\(networkLyricProvider.providerName) Fetch")
                    spotifySyncLog("fetchAllNetworkLyrics provider=\(networkLyricProvider.providerName) success count=\(lyrics.lyrics.count)")
                    // thats how i save to coredata
                    let _ = SongObject(from: lyrics.lyrics, with: coreDataContainer.viewContext, trackID: currentlyPlaying, trackName: currentlyPlayingName)
                    saveCoreData()
                    return lyrics
                } else if networkLyricProvider is SpotifyLyricProvider {
                    spotifySyncLog("fetchAllNetworkLyrics provider=\(networkLyricProvider.providerName) empty")
                    handleSpotifyNoLyricsFallback()
                } else {
                    spotifySyncLog("fetchAllNetworkLyrics provider=\(networkLyricProvider.providerName) empty")
                }
            } catch {
                spotifySyncLog("fetchAllNetworkLyrics provider=\(networkLyricProvider.providerName) failed error=\(error)")
            }
        }
        spotifySyncLog("fetchAllNetworkLyrics exhausted providers; returning empty")
        return NetworkFetchReturn(lyrics: [], colorData: nil)
    }
    
    #if os(macOS)
    func refreshLyrics() async throws {
        // todo: romanize
        if currentPlayer == .appleMusic {
            print("Refresh Lyrics: Calling Apple Music Network fetch")
            refreshAppleMusicMetadataFromPlayer()
            await appleMusicStarter()
        }
        guard let currentlyPlaying, let currentlyPlayingName, let currentDuration = currentPlayerInstance.durationAsTimeInterval else {
            return
        }
        print("Calling refresh lyrics")
        guard let finalLyrics = await self.fetch(for: currentlyPlaying, currentlyPlayingName, checkCoreDataFirst: false) else {
            print("Refresh Lyrics: Failed to run network fetch")
            return
        }
        setNewLyricsColorTranslationRomanizationAndStartUpdater(with: finalLyrics)
//        currentlyPlayingLyrics = finalLyrics
//        setBackgroundColor()
//        romanizeDidChange()
//        reloadTranslationConfigIfTranslating()
//        lyricsIsEmptyPostLoad = currentlyPlayingLyrics.isEmpty
//        print("HELLOO")
//        if isPlaying, !currentlyPlayingLyrics.isEmpty, showLyrics, userDefaultStorage.hasOnboarded {
//            startLyricUpdater()
//        }
        // we call this in self.fetch
//        callColorDataServiceOnLyricColorOrArtwork(colorData: finalLyrics.colorData)
    }

    func callColorDataServiceOnLyricColorOrArtwork(colorData: Int32?) {
        if currentPlayer == .appleMusic {
            if let currentlyPlaying, let backgroundColor = artworkImage?.findWhiteTextLegibleMostSaturatedDominantColor() {
                ColorDataService.saveColorToCoreData(trackID: currentlyPlaying, songColor: backgroundColor)
                print("ViewModel Refresh Lyrics: New color \(backgroundColor) saved for track \(currentlyPlaying)")
            }
        } else {
            if let currentlyPlaying, let backgroundColor = colorData {
                ColorDataService.saveColorToCoreData(trackID: currentlyPlaying, songColor: backgroundColor)
                print("ViewModel Refresh Lyrics: New color \(backgroundColor) saved for track \(currentlyPlaying)")
            }
        }
    }
    
    // Run only on first 2.1 run. Strips whitespace from saved lyrics, and extends final timestamp to prevent karaoke mode racecondition (as well as song on loop race condition)
    func migrateTimestampsIfNeeded(context: NSManagedObjectContext) {
        if !userDefaultStorage.hasMigrated {
            let fetchRequest: NSFetchRequest<SongObject> = SongObject.fetchRequest()
            do {
                let objects = try context.fetch(fetchRequest)
                for object in objects {
                    var timestamps = object.lyricsTimestamps
                    if let lastIndex = timestamps.indices.last {
                        timestamps[lastIndex] = timestamps[lastIndex] + 5000
                        object.lyricsTimestamps = timestamps
                    }
                    var strings = object.lyricsWords
                    let indicesToRemove = strings.indices.filter { strings[$0].isEmpty }
                    strings.removeAll { $0.isEmpty }
                    for index in indicesToRemove.reversed() {
                        timestamps.remove(at: index)
                    }

                    // Update the object properties
                    object.lyricsWords = strings
                    object.lyricsTimestamps = timestamps
                }
                try context.save()
                
                // Mark migration as done
                userDefaultStorage.hasMigrated = true
            } catch {
                print("Error migrating data: \(error)")
            }
        }
    }
    
    // Runs once user has completed Spotify log-in. Attempt to extract cookie
    func checkIfLoggedIn() {
        WKWebsiteDataStore.default().httpCookieStore.getAllCookies { cookies in
            if let temporaryCookie = cookies.first(where: {$0.name == "sp_dc"}) {
                print("found the sp_dc cookie")
                self.userDefaultStorage.cookie = temporaryCookie.value
                NotificationCenter.default.post(name: Notification.Name("didLogIn"), object: nil)
            }
        }
    }
    
    func openSettings(_ openWindow: OpenWindowAction) {
        openWindow(id: "onboarding")
        NSApplication.shared.activate(ignoringOtherApps: true)
//        // send notification to check auth
//        NotificationCenter.default.post(name: Notification.Name("didClickSettings"), object: nil)
    }
    #endif
    
    func toggleLyrics() {
        if showLyrics {
            startLyricUpdater()
        } else {
            stopLyricUpdater()
        }
    }
    
    func openTranslationHelpOnFirstRun(_ openURL: OpenURLAction) {
        if !userDefaultStorage.hasTranslated {
            openURL(URL(string: "https://aviwadhwa.com/TranslationHelp")!)
        }
        userDefaultStorage.hasTranslated = true
    }
    
    @MainActor
    func translationTask(_ session: TranslationSession) async {
        isFetchingTranslation = true
        let translationResponse = await TranslationService.translationTask(session, request: currentlyPlayingLyrics.map { TranslationSession.Request(lyric: $0) })
        
        switch translationResponse {
            case .success(let array):
                print("Translation Service: isFetchingTranslation set to false due to success")
                isFetchingTranslation = false
                if currentlyPlayingLyrics.count == array.count {
                    translatedLyric = array.map {
                        $0.targetText
                    }
                }
            case .needsConfigUpdate(let language):
                // TODO: why do i sleep?
//                try? await Task.sleep(for: .seconds(1))
                translationSessionConfig = TranslationSession.Configuration(source: language, target: userLocaleLanguage)
            case .failure:
                print("Translation Service: isFetchingTranslation set to false due to failure")
                isFetchingTranslation = false
                return
        }
    }
    
    func romanizeDidChange() {
        if romanizedLyrics.count != currentlyPlayingLyrics.count {
            regenerateRomanizedLyrics()
        }
    }

    func romanizedLyric(at index: Int) -> String? {
        guard let lyric = romanizedLyrics[safe: index], !lyric.isEmpty else {
            return nil
        }
        return lyric
    }

    private func resetRomanization() {
        currentRomanizationTask?.cancel()
        currentRomanizationTask = nil
        romanizedLyrics = []
    }

    private func regenerateRomanizedLyrics() {
        currentRomanizationTask?.cancel()

        let trackID = currentlyPlaying
        let lyricsSnapshot = currentlyPlayingLyrics
        let sourceLyrics = lyricsSnapshot.map(\.words)

        guard !sourceLyrics.isEmpty else {
            romanizedLyrics = []
            currentRomanizationTask = nil
            return
        }

        romanizedLyrics = Array(repeating: "", count: lyricsSnapshot.count)
        currentRomanizationTask = Task {
            let generated = await Task.detached(priority: .userInitiated) {
                RomanizerService.generateRomanizedLyrics(sourceLyrics)
            }.value

            guard !Task.isCancelled,
                  self.currentlyPlaying == trackID,
                  self.currentlyPlayingLyrics == lyricsSnapshot,
                  generated.count == lyricsSnapshot.count else {
                return
            }

            self.romanizedLyrics = generated
            self.currentRomanizationTask = nil
        }
    }
    
    // Only called when Romanize is true
//    func romanizeMetadata() {
//        // Generate romanized metadata from name & artist
//        if userDefaultStorage.romanizeMetadata, let currentlyPlayingName, let romanizedName = RomanizerService.generateRomanizedString(currentlyPlayingName), let currentlyPlayingArtist, let romanizedArtist = RomanizerService.generateRomanizedString(currentlyPlayingArtist) {
//            self.currentlyPlayingName = romanizedName
//            self.currentlyPlayingArtist = romanizedArtist
//        }
//    }
    
    func romanizeName(_ currentlyPlayingName: String) -> String? {
        if let romanizedName = RomanizerService.generateRomanizedString(currentlyPlayingName) {
            return romanizedName
        }
        return nil
    }
    
    func romanizeArtist(_ currentlyPlayingArtist: String) -> String? {
        if let romanizedArtist = RomanizerService.generateRomanizedString(currentlyPlayingArtist) {
            return romanizedArtist
        }
        return nil
    }
    
    func chinesePreferenceDidChange() {
        if let chinesePreference = ChineseConversion(rawValue: userDefaultStorage.chinesePreference), chinesePreference != .none {
            print("Generating Chinese conversion for song \(String(describing: currentlyPlaying)) to chinese style \(chinesePreference.description)")
            //TODO: check if Task was cancelled
            let chineseConversionLyrics: [String] = currentlyPlayingLyrics.map({
                switch chinesePreference {
                    case .none:
                        return $0.words
                    case .simplified:
                        return RomanizerService.generateMainlandTransliteration($0) ?? $0.words
                    case .traditionalNeutral:
                        return RomanizerService.generateTraditionalNeutralTransliteration($0) ?? $0.words
                    case .traditionalTaiwan:
                        return RomanizerService.generateTaiwanTransliteration($0) ?? $0.words
                    case .traditionalHK:
                        return RomanizerService.generateHongKongTransliteration($0) ?? $0.words
                }
            })
            //TODO: check if Task was cancelled
            if !Task.isCancelled {
                self.chineseConversionLyrics = chineseConversionLyrics
                regenerateRomanizedLyrics()
            }
        } else {
            chineseConversionLyrics = []
            regenerateRomanizedLyrics()
        }
    }
    
    #if os(macOS)
    func saveKaraokeFontOnTermination() {
        // This code will be executed just before the app terminates
     UserDefaults.standard.set(karaokeFont.fontName, forKey: "karaokeFontName")
     UserDefaults.standard.set(Double(karaokeFont.pointSize), forKey: "karaokeFontSize")
    }
    
    func appleMusicPlaybackDidChange(_ notification: Notification) {
        guard currentPlayer == .appleMusic else {
            return
        }
        ensureAppleMusicWatchdog()
        if appleMusicPlayer.isPlaying {
            print("is playing")
            isPlaying = true
        } else {
            print("paused. timer canceled")
            isPlaying = false
            // manually cancels the lyric-updater task bc media is paused
        }
        refreshAppleMusicMetadataFromPlayer()
    }
    
    func spotifyPlaybackDidChange(_ notification: Notification) {
        guard currentPlayer == .spotify else {
            return
        }
        ensureSpotifyWatchdog()
        let notificationTrackID = (notification.userInfo?["Track ID"] as? String)?.spotifyProcessedUrl()
        let notificationTrackName = notification.userInfo?["Name"] as? String
        let playerState = notification.userInfo?["Player State"] as? String
        let scriptPosition = spotifyPlayer.currentTime.map { String($0) } ?? "nil"
        let notificationSummary = [
            "notification state=\(playerState ?? "nil")",
            "notificationTrackID=\(notificationTrackID ?? "nil")",
            "notificationName=\(notificationTrackName ?? "nil")",
            "scriptTrackID=\(spotifyPlayer.trackID ?? "nil")",
            "scriptName=\(spotifyPlayer.trackName ?? "nil")",
            "artist=\(spotifyPlayer.artistName ?? "nil")",
            "position=\(scriptPosition)",
            "internalTrackID=\(currentlyPlaying ?? "nil")"
        ].joined(separator: " ")
        spotifySyncLog(notificationSummary)
        if playerState == "Stopped" {
            currentLyricsDriftFix?.cancel()
            isPlaying = false
            isStopped = true
            stopLyricUpdater()
            return
        }
        isStopped = false
        if playerState == "Playing" {
            print("is playing")
            isPlaying = true
        } else {
            print("paused. timer canceled")
            isPlaying = false
            // manually cancels the lyric-updater task bc media is paused
        }
        let currentlyPlaying = notificationTrackID ?? spotifyPlayer.trackID
        let currentlyPlayingName = notificationTrackName ?? spotifyPlayer.trackName
        if currentlyPlaying != "", currentlyPlayingName != "", let duration = currentPlayerInstance.duration {
            if self.currentlyPlaying != currentlyPlaying {
                spotifySyncLog("track changed via notification \(self.currentlyPlaying ?? "nil") -> \(currentlyPlaying ?? "nil")")
                resetSpotifyEmptyLyricsRetry()
                stopLyricUpdater()
            }
            self.currentlyPlaying = currentlyPlaying
            self.currentlyPlayingName = currentlyPlayingName
            self.currentlyPlayingArtist = spotifyPlayer.artistName
            self.currentAlbumName = spotifyPlayer.albumName
            self.duration = duration
            if let currentTime = spotifyPlayer.currentTime {
                self.currentTime = CurrentTimeWithStoredDate(currentTime: currentTime)
            }
            refreshArtworkForCurrentTrack(reason: "spotify notification")
        } else {
            spotifySyncLog("ignored notification because track metadata was incomplete")
        }
    }

    private func spotifySyncLog(_ message: String) {
        print("[LyricFever][SpotifySync] \(message)")
    }

    func refreshArtworkForCurrentTrack(reason: String) {
        guard let targetTrackID = currentlyPlaying, !targetTrackID.isEmpty else {
            print("[LyricFever][Artwork] refresh skipped reason=\(reason) trackID=nil")
            artworkImage = nil
            return
        }

        let targetPlayer = currentPlayer
        let targetArtist = currentlyPlayingArtist
        let targetAlbum = currentAlbumName
        let targetName = currentlyPlayingName ?? "nil"

        currentArtworkFetchTask?.cancel()
        currentArtworkFetchTask = Task { @MainActor in
            print("[LyricFever][Artwork] refresh start reason=\(reason) trackID=\(targetTrackID) name=\(targetName)")
            let retryDelays: [UInt64] = [0, 600_000_000, 1_800_000_000]

            for (attempt, delay) in retryDelays.enumerated() {
                if delay > 0 {
                    try? await Task.sleep(nanoseconds: delay)
                }
                if Task.isCancelled {
                    return
                }
                guard self.currentlyPlaying == targetTrackID, self.currentPlayer == targetPlayer else {
                    print("[LyricFever][Artwork] stale refresh ignored target=\(targetTrackID) current=\(self.currentlyPlaying ?? "nil")")
                    return
                }
                if let artworkImage = await self.currentPlayerInstance.artworkImage {
                    guard self.currentlyPlaying == targetTrackID, self.currentPlayer == targetPlayer else {
                        print("[LyricFever][Artwork] stale player artwork ignored target=\(targetTrackID) current=\(self.currentlyPlaying ?? "nil")")
                        return
                    }
                    print("[LyricFever][Artwork] player artwork success attempt=\(attempt + 1) trackID=\(targetTrackID)")
                    self.artworkImage = artworkImage
                    return
                }
                print("[LyricFever][Artwork] player artwork missing attempt=\(attempt + 1) trackID=\(targetTrackID)")
            }

            guard self.currentlyPlaying == targetTrackID, self.currentPlayer == targetPlayer else {
                print("[LyricFever][Artwork] stale fallback ignored target=\(targetTrackID) current=\(self.currentlyPlaying ?? "nil")")
                return
            }
            if let targetArtist, let targetAlbum,
               let mbid = await MusicBrainzArtworkService.findMbid(albumName: targetAlbum, artistName: targetArtist),
               let artworkImage = await MusicBrainzArtworkService.artworkImage(for: mbid) {
                guard self.currentlyPlaying == targetTrackID, self.currentPlayer == targetPlayer else {
                    print("[LyricFever][Artwork] stale MusicBrainz artwork ignored target=\(targetTrackID) current=\(self.currentlyPlaying ?? "nil")")
                    return
                }
                print("[LyricFever][Artwork] MusicBrainz artwork success trackID=\(targetTrackID)")
                self.artworkImage = artworkImage
                return
            }
            print("[LyricFever][Artwork] artwork unavailable trackID=\(targetTrackID)")
        }
    }

    private func resetLyricStateForTrackChange() {
        stopLyricUpdater()
        currentlyPlayingLyricsIndex = nil
        currentlyPlayingLyrics = []
        translatedLyric = []
        resetRomanization()
        chineseConversionLyrics = []
        lyricsIsEmptyPostLoad = false
        isFetching = true
    }

    private func resetSpotifyEmptyLyricsRetry() {
        currentSpotifyEmptyLyricsRetryTask?.cancel()
        currentSpotifyEmptyLyricsRetryTask = nil
        spotifyEmptyLyricsRetryCount = 0
    }

    private func ensureAppleMusicWatchdog() {
        guard currentPlayer == .appleMusic else {
            currentAppleMusicWatchdogTask?.cancel()
            currentAppleMusicWatchdogTask = nil
            return
        }
        guard currentAppleMusicWatchdogTask == nil else {
            return
        }
        currentAppleMusicWatchdogTask = Task { @MainActor in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                if Task.isCancelled {
                    break
                }
                self.appleMusicWatchdogTick()
            }
        }
    }

    private func appleMusicWatchdogTick() {
        guard currentPlayer == .appleMusic else {
            currentAppleMusicWatchdogTask?.cancel()
            currentAppleMusicWatchdogTask = nil
            return
        }
        guard appleMusicPlayer.isRunning else {
            return
        }

        refreshAppleMusicMetadataFromPlayer()
        let trackID = appleMusicPlayer.persistentID
        let position = appleMusicPlayer.currentTime
        let playerIsPlaying = appleMusicPlayer.isPlaying
        isPlaying = playerIsPlaying

        guard playerIsPlaying else {
            lastAppleMusicWatchdogTrackID = trackID
            lastAppleMusicWatchdogPosition = position
            return
        }

        if let position {
            currentTime = CurrentTimeWithStoredDate(currentTime: position)
        }

        let positionJumpedBackward: Bool
        if lastAppleMusicWatchdogTrackID == trackID,
           let previousPosition = lastAppleMusicWatchdogPosition,
           let position {
            positionJumpedBackward = previousPosition > 10_000
                && position + 5_000 < previousPosition
        } else {
            positionJumpedBackward = false
        }

        let lyricIndexIsAheadOfPlayback: Bool
        if let index = currentlyPlayingLyricsIndex,
           currentlyPlayingLyrics.indices.contains(index),
           let position {
            lyricIndexIsAheadOfPlayback = position + 1_000
                < currentlyPlayingLyrics[index].startTimeMS
        } else {
            lyricIndexIsAheadOfPlayback = false
        }

        if lastAppleMusicWatchdogTrackID == trackID,
           !currentlyPlayingLyrics.isEmpty,
           positionJumpedBackward || lyricIndexIsAheadOfPlayback {
            let previousDescription = lastAppleMusicWatchdogPosition.map { String($0) } ?? "nil"
            let positionDescription = position.map { String($0) } ?? "nil"
            let indexDescription = currentlyPlayingLyricsIndex.map { String($0) } ?? "nil"
            print("[LyricFever][AppleMusicSync] position reset detected "
                  + "previous=\(previousDescription) "
                  + "current=\(positionDescription) "
                  + "index=\(indexDescription)")
            currentlyPlayingLyricsIndex = nil
            if showLyrics, userDefaultStorage.hasOnboarded {
                startLyricUpdater()
            }
        }

        lastAppleMusicWatchdogTrackID = trackID
        lastAppleMusicWatchdogPosition = position
    }

    @discardableResult
    func refreshAppleMusicMetadataFromPlayer() -> Bool {
        guard currentPlayer == .appleMusic,
              let trackID = appleMusicPlayer.persistentID,
              let trackName = appleMusicPlayer.trackName,
              !trackName.isEmpty else {
            return false
        }

        let artistName = appleMusicPlayer.artistName
        let albumName = appleMusicPlayer.albumName
        let trackChanged = currentlyPlayingAppleMusicPersistentID != trackID
        let metadataChanged = currentlyPlayingName != trackName
            || currentlyPlayingArtist != artistName
            || currentAlbumName != albumName

        if trackChanged {
            currentAppleMusicFetchTask?.cancel()
            currentlyPlaying = nil
        }

        currentlyPlayingName = trackName
        currentlyPlayingArtist = artistName
        currentAlbumName = albumName
        if let duration = appleMusicPlayer.duration {
            self.duration = duration
        }
        if trackChanged {
            print("[LyricFever][AppleMusicSync] source track changed id=\(trackID) name=\(trackName)")
            currentlyPlayingAppleMusicPersistentID = trackID
        } else if metadataChanged {
            print("[LyricFever][AppleMusicSync] corrected source metadata name=\(trackName)")
        }
        return trackChanged || metadataChanged
    }

    private func ensureSpotifyWatchdog() {
        guard currentPlayer == .spotify else {
            currentSpotifyWatchdogTask?.cancel()
            currentSpotifyWatchdogTask = nil
            return
        }
        guard currentSpotifyWatchdogTask == nil else {
            return
        }
        currentSpotifyWatchdogTask = Task { @MainActor in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 2_000_000_000)
                if Task.isCancelled {
                    break
                }
                self.spotifyWatchdogTick()
            }
        }
    }

    private func spotifyWatchdogTick() {
        guard currentPlayer == .spotify else {
            spotifySyncLog("watchdog stopping because currentPlayer is not Spotify")
            currentSpotifyWatchdogTask?.cancel()
            currentSpotifyWatchdogTask = nil
            return
        }
        guard spotifyPlayer.isRunning else {
            spotifySyncLog("watchdog: Spotify is not running")
            return
        }

        let spotifyTrackID = spotifyPlayer.trackID
        let spotifyTrackName = spotifyPlayer.trackName
        let spotifyPosition = spotifyPlayer.currentTime
        let spotifyIsPlaying = spotifyPlayer.isPlaying
        let watchdogPosition = spotifyPosition.map { String($0) } ?? "nil"
        let watchdogSummary = [
            "watchdog trackID=\(spotifyTrackID ?? "nil")",
            "name=\(spotifyTrackName ?? "nil")",
            "position=\(watchdogPosition)",
            "isPlaying=\(spotifyIsPlaying)",
            "internalTrackID=\(currentlyPlaying ?? "nil")",
            "lyricsCount=\(currentlyPlayingLyrics.count)",
            "isFetching=\(isFetching)",
            "emptyPostLoad=\(lyricsIsEmptyPostLoad)"
        ].joined(separator: " ")
        spotifySyncLog(watchdogSummary)

        isPlaying = spotifyIsPlaying
        guard spotifyIsPlaying else {
            lastSpotifyWatchdogTrackID = spotifyTrackID
            lastSpotifyWatchdogPosition = spotifyPosition
            return
        }

        if let spotifyTrackID, !spotifyTrackID.isEmpty, let spotifyTrackName, !spotifyTrackName.isEmpty {
            if spotifyTrackID != currentlyPlaying {
                spotifySyncLog("watchdog correcting stale internal trackID \(currentlyPlaying ?? "nil") -> \(spotifyTrackID)")
                resetSpotifyEmptyLyricsRetry()
                stopLyricUpdater()
                currentlyPlaying = spotifyTrackID
                currentlyPlayingName = spotifyTrackName
                currentlyPlayingArtist = spotifyPlayer.artistName
                currentAlbumName = spotifyPlayer.albumName
                if let duration = spotifyPlayer.duration {
                    self.duration = duration
                }
                refreshArtworkForCurrentTrack(reason: "spotify watchdog track correction")
            } else if currentlyPlayingLyrics.isEmpty, !isFetching, userDefaultStorage.hasOnboarded {
                scheduleSpotifyEmptyLyricsRetry(for: spotifyTrackID, trackName: spotifyTrackName, reason: "watchdog saw empty lyrics")
            }
        }

        if lastSpotifyWatchdogTrackID == spotifyTrackID,
           let lastPosition = lastSpotifyWatchdogPosition,
           let spotifyPosition,
           lastPosition > 10_000,
           spotifyPosition < 3_000 {
            spotifySyncLog("watchdog detected position reset for same track; restarting lyric updater")
            currentlyPlayingLyricsIndex = nil
            if currentlyPlayingLyrics.isEmpty, let spotifyTrackID, let spotifyTrackName {
                scheduleSpotifyEmptyLyricsRetry(for: spotifyTrackID, trackName: spotifyTrackName, reason: "same-track position reset with empty lyrics")
            } else {
                startLyricUpdater()
            }
        }

        lastSpotifyWatchdogTrackID = spotifyTrackID
        lastSpotifyWatchdogPosition = spotifyPosition
    }

    private func scheduleSpotifyEmptyLyricsRetry(for trackID: String, trackName: String, reason: String) {
        guard currentPlayer == .spotify, userDefaultStorage.hasOnboarded, isPlaying else {
            return
        }
        guard currentlyPlaying == trackID, currentlyPlayingLyrics.isEmpty, !isFetching else {
            return
        }
        guard spotifyEmptyLyricsRetryCount < spotifyEmptyLyricsRetryLimit else {
            spotifySyncLog("empty lyrics retry limit reached for \(trackID)")
            return
        }
        guard currentSpotifyEmptyLyricsRetryTask == nil else {
            return
        }

        let attempt = spotifyEmptyLyricsRetryCount + 1
        spotifySyncLog("scheduling empty lyrics retry #\(attempt) for \(trackID): \(reason)")
        currentSpotifyEmptyLyricsRetryTask = Task { @MainActor in
            try? await Task.sleep(nanoseconds: spotifyEmptyLyricsRetryDelay)
            if Task.isCancelled {
                return
            }
            currentSpotifyEmptyLyricsRetryTask = nil
            guard currentPlayer == .spotify,
                  currentlyPlaying == trackID,
                  currentlyPlayingLyrics.isEmpty,
                  !isFetching,
                  userDefaultStorage.hasOnboarded,
                  isPlaying else {
                spotifySyncLog("empty lyrics retry #\(attempt) skipped because state recovered or changed")
                return
            }

            spotifyEmptyLyricsRetryCount = attempt
            spotifySyncLog("running empty lyrics retry #\(attempt) for \(trackID)")
            guard let lyrics = await fetch(for: trackID, trackName, checkCoreDataFirst: true) else {
                spotifySyncLog("empty lyrics retry #\(attempt) returned nil")
                scheduleSpotifyEmptyLyricsRetry(for: trackID, trackName: trackName, reason: "retry returned nil")
                return
            }
            guard currentlyPlaying == trackID else {
                spotifySyncLog("empty lyrics retry #\(attempt) ignored stale result for \(trackID)")
                return
            }
            setNewLyricsColorTranslationRomanizationAndStartUpdater(with: lyrics)
            if lyrics.isEmpty {
                scheduleSpotifyEmptyLyricsRetry(for: trackID, trackName: trackName, reason: "retry returned empty lyrics")
            }
        }
    }
    
    func onAppear(_ openWindow: OpenWindowAction) {
        setCurrentProperties()
    }
    
    func onCurrentlyPlayingIDChange() async {
        let expectedTrackID = currentlyPlaying
        spotifySyncLog("onCurrentlyPlayingIDChange expectedTrackID=\(expectedTrackID ?? "nil") name=\(currentlyPlayingName ?? "nil") lyricsCount=\(currentlyPlayingLyrics.count) isFetching=\(isFetching) emptyPostLoad=\(lyricsIsEmptyPostLoad)")
        if currentPlayer == .spotify {
            resetSpotifyEmptyLyricsRetry()
            resetLyricStateForTrackChange()
            ensureSpotifyWatchdog()
        } else {
            currentlyPlayingLyricsIndex = nil
            currentlyPlayingLyrics = []
            translatedLyric = []
            resetRomanization()
            chineseConversionLyrics = []
        }

        guard userDefaultStorage.hasOnboarded, let currentlyPlaying = currentlyPlaying, let currentlyPlayingName = currentlyPlayingName else {
            isFetching = false
            spotifySyncLog("onCurrentlyPlayingIDChange skipped because onboarding or metadata is missing")
            return
        }

        if let lyrics = await fetch(for: currentlyPlaying, currentlyPlayingName) {
            guard self.currentlyPlaying == currentlyPlaying else {
                spotifySyncLog("onCurrentlyPlayingIDChange ignored stale lyrics for \(currentlyPlaying); current=\(self.currentlyPlaying ?? "nil")")
                return
            }
            spotifySyncLog("onCurrentlyPlayingIDChange fetched lyrics count=\(lyrics.count) for \(currentlyPlaying)")
            setNewLyricsColorTranslationRomanizationAndStartUpdater(with: lyrics)
            if currentPlayer == .spotify, lyrics.isEmpty {
                scheduleSpotifyEmptyLyricsRetry(for: currentlyPlaying, trackName: currentlyPlayingName, reason: "initial fetch returned empty lyrics")
            }
        } else if currentPlayer == .spotify, self.currentlyPlaying == currentlyPlaying {
            spotifySyncLog("onCurrentlyPlayingIDChange fetch returned nil for \(currentlyPlaying); keeping emptyPostLoad=false and scheduling retry")
            lyricsIsEmptyPostLoad = false
            scheduleSpotifyEmptyLyricsRetry(for: currentlyPlaying, trackName: currentlyPlayingName, reason: "initial fetch returned nil")
//            currentlyPlayingLyrics = lyrics
//            setBackgroundColor()
//            romanizeDidChange()
//            reloadTranslationConfigIfTranslating()
//            lyricsIsEmptyPostLoad = lyrics.isEmpty
//            if isPlaying, !currentlyPlayingLyrics.isEmpty, showLyrics, userDefaultStorage.hasOnboarded {
//                print("STARTING UPDATER")
//                startLyricUpdater()
//            }
        }
    }
    
    private func setCurrentProperties() {
        switch currentPlayer {
            case .appleMusic:
                if let currentTrackName = appleMusicPlayer.trackName, let currentArtistName = appleMusicPlayer.artistName, let duration = appleMusicPlayer.duration, let currentAlbumName = appleMusicPlayer.albumName {
                    // Don't set currentlyPlaying here: the persistentID change triggers the appleMusicFetch which will set spotify's currentlyPlaying
                    if currentTrackName == "" {
                        currentlyPlayingName = nil
                        currentlyPlayingArtist = nil
                        self.currentAlbumName = nil
                    } else {
                        currentlyPlayingName = currentTrackName
                        currentlyPlayingArtist = currentArtistName
                        self.duration = duration
                        self.currentAlbumName = currentAlbumName
                    }
                    print("ON APPEAR HAS UPDATED APPLE MUSIC SONG ID")
                    currentlyPlayingAppleMusicPersistentID = appleMusicPlayer.persistentID
                }
            case .spotify:
                if let currentTrack = spotifyPlayer.trackID, let currentTrackName = spotifyPlayer.trackName, let currentArtistName =  spotifyPlayer.artistName, currentTrack != "", currentTrackName != "", let duration = spotifyPlayer.duration, let currentAlbumName = spotifyPlayer.albumName {
                    currentlyPlaying = currentTrack
                    currentlyPlayingName = currentTrackName
                    currentlyPlayingArtist = currentArtistName
                    self.duration = duration
                    self.currentAlbumName = currentAlbumName
                    self.currentTime = CurrentTimeWithStoredDate(currentTime: 0)
                    print(currentTrack)
                }
        }
    }
    
    #else
    func setCurrentProperties() {
        currentlyPlaying = spotifyPlayer.currentTrack?.uri?.spotifyProcessedUrl()
        currentlyPlayingName = spotifyPlayer.trackName
        currentlyPlayingArtist = spotifyPlayer.artistName
    }
    #endif

    func upcomingIndex(_ currentTime: Double) -> Int? {
        guard !currentlyPlayingLyrics.isEmpty else {
            currentlyPlayingLyricsIndex = nil
            spotifySyncLog("upcomingIndex nil: lyrics array is empty")
            return nil
        }

        if let currentlyPlayingLyricsIndex {
            guard currentlyPlayingLyrics.indices.contains(currentlyPlayingLyricsIndex) else {
                spotifySyncLog("upcomingIndex recovered invalid index=\(currentlyPlayingLyricsIndex) lyricsCount=\(currentlyPlayingLyrics.count)")
                self.currentlyPlayingLyricsIndex = nil
                return currentlyPlayingLyrics.firstIndex(where: { $0.startTimeMS > currentTime })
            }
            let newIndex = currentlyPlayingLyricsIndex + 1
            if newIndex >= currentlyPlayingLyrics.count {
                print("REACHED LAST LYRIC!!!!!!!!")
                // if current time is before our current index's start time, the user has scrubbed and rewinded
                // reset into linear search mode
                if currentTime < currentlyPlayingLyrics[currentlyPlayingLyricsIndex].startTimeMS {
                    spotifySyncLog("upcomingIndex recovered after rewind: currentTime=\(currentTime) currentIndexStart=\(currentlyPlayingLyrics[currentlyPlayingLyricsIndex].startTimeMS)")
                    return currentlyPlayingLyrics.firstIndex(where: {$0.startTimeMS > currentTime})
                }
                // we've reached the end of the song, we're past the last lyric
                spotifySyncLog("upcomingIndex nil: past final lyric currentTime=\(currentTime) lyricsCount=\(currentlyPlayingLyrics.count)")
                return nil
            }
            else if  currentTime > currentlyPlayingLyrics[currentlyPlayingLyricsIndex].startTimeMS, currentTime < currentlyPlayingLyrics[newIndex].startTimeMS {
                print("just the next lyric")
                return newIndex
            }
        }
        // linear search through the array to find the first lyric that's right after the current time
        // done on first lyric update for the song, as well as post-scrubbing
        let nextIndex = currentlyPlayingLyrics.firstIndex(where: {$0.startTimeMS > currentTime})
        if nextIndex == nil {
            spotifySyncLog("upcomingIndex nil: linear search found no later lyric currentTime=\(currentTime) lyricsCount=\(currentlyPlayingLyrics.count)")
        }
        return nextIndex
    }
    
    func lyricUpdater() async throws {
        repeat {
            guard let currentTime = currentPlayerInstance.currentTime else {
                spotifySyncLog("lyricUpdater stopping: currentTime is nil")
                stopLyricUpdater()
                return
            }
            guard let lastIndex: Int = upcomingIndex(currentTime) else {
                spotifySyncLog("lyricUpdater stopping: upcomingIndex returned nil trackID=\(currentlyPlaying ?? "nil") position=\(currentTime) lyricsCount=\(currentlyPlayingLyrics.count)")
                stopLyricUpdater()
                return
            }
            // If there is no current index (perhaps lyric updater started late and we're mid-way of the first lyric, or the user scrubbed and our index is expired)
            // Then we set the current index to the one before our anticipated index
            if currentlyPlayingLyricsIndex == nil && lastIndex > 0 {
                currentlyPlayingLyricsIndex = lastIndex-1
            }
            let nextTimestamp = currentlyPlayingLyrics[lastIndex].startTimeMS
            let diff = nextTimestamp - currentTime
            print("current time: \(currentTime)")
            self.currentTime = CurrentTimeWithStoredDate(currentTime: currentTime)
            print("next time: \(nextTimestamp)")
            print("the difference is \(diff)")
            guard diff.isFinite else {
                spotifySyncLog("lyricUpdater stopping: non-finite timestamp difference current=\(currentTime) next=\(nextTimestamp)")
                stopLyricUpdater()
                return
            }
            if diff <= 0 {
                spotifySyncLog("lyricUpdater recovered non-positive timestamp difference=\(diff) index=\(lastIndex)")
                currentlyPlayingLyricsIndex = lastIndex
                continue
            }
            try await Task.sleep(nanoseconds: UInt64(min(diff * 1_000_000, Double(UInt64.max))))
            print("lyrics exist: \(!currentlyPlayingLyrics.isEmpty)")
            print("last index: \(lastIndex)")
            print("currently playing lryics index: \(currentlyPlayingLyricsIndex)")
            if currentlyPlayingLyrics.count > lastIndex {
                currentlyPlayingLyricsIndex = lastIndex
            } else {
                currentlyPlayingLyricsIndex = nil
                
            }
            print(currentlyPlayingLyricsIndex ?? "nil")
        } while !Task.isCancelled
    }
    
    func startLyricUpdater() {
        spotifySyncLog("startLyricUpdater called isPlaying=\(isPlaying) lyricsCount=\(currentlyPlayingLyrics.count) index=\(currentlyPlayingLyricsIndex.map(String.init) ?? "nil") mustUpdateUrgent=\(mustUpdateUrgent)")
        currentLyricsUpdaterTask?.cancel()
        if !isPlaying || currentlyPlayingLyrics.isEmpty || mustUpdateUrgent {
            spotifySyncLog("startLyricUpdater skipped isPlaying=\(isPlaying) lyricsCount=\(currentlyPlayingLyrics.count) mustUpdateUrgent=\(mustUpdateUrgent)")
            return
        }
        // If an index exists, we're unpausing: meaning we must instantly find the current lyric
        if currentlyPlayingLyricsIndex != nil {
            guard let currentTime = currentPlayerInstance.currentTime, let lastIndex: Int = upcomingIndex(currentTime) else {
                spotifySyncLog("startLyricUpdater failed to determine current lyric; stopping updater")
                stopLyricUpdater()
                return
            }
            // If there is no current index (perhaps lyric updater started late and we're mid-way of the first lyric, or the user scrubbed and our index is expired)
            // Then we set the current index to the one before our anticipated index
            if lastIndex > 0 {
                currentlyPlayingLyricsIndex = lastIndex-1
            }
        } else {
            #if os(macOS)
            if currentPlayer == .spotify {
                currentLyricsDriftFix?.cancel()
                currentLyricsDriftFix =             // Only run drift fix for new songs
                Task {
                    try await spotifyPlayer.fixSpotifyLyricDrift()
                }
                Task {
                    try await currentLyricsDriftFix?.value
                }
            }
            #endif
        }
        currentLyricsUpdaterTask = Task {
            do {
                try await lyricUpdater()
            } catch {
                print("lyrics were canceled \(error)")
            }
        }
        Task {
            try await currentLyricsUpdaterTask?.value
        }
        
    }
    
    func stopLyricUpdater() {
        spotifySyncLog("stopLyricUpdater called trackID=\(currentlyPlaying ?? "nil") lyricsCount=\(currentlyPlayingLyrics.count) index=\(currentlyPlayingLyricsIndex.map(String.init) ?? "nil")")
        currentLyricsUpdaterTask?.cancel()
    }
    
    func saveCoreData() {
        let context = coreDataContainer.viewContext
        if context.hasChanges {
            do {
                try context.save()
                print("Saved CoreData!")
            } catch {
                print("core data error \(error)")
                // Show some error here
            }
        } else {
            print("BAD COREDATA CALL!!")
        }
    }
    
    func fetch(for trackID: String, _ trackName: String, checkCoreDataFirst: Bool = true) async -> [LyricLine]? {
        if isFirstFetch {
            isFirstFetch = false
        }
        spotifySyncLog("fetch start trackID=\(trackID) trackName=\(trackName) checkCoreDataFirst=\(checkCoreDataFirst) internalTrackID=\(currentlyPlaying ?? "nil") isFetching=\(isFetching) emptyPostLoad=\(lyricsIsEmptyPostLoad)")
        currentFetchTask?.cancel()
        // i don't set isFetching to true here to prevent "flashes" for CoreData fetches
        defer {
            isFetching = false
            spotifySyncLog("fetch end trackID=\(trackID) internalTrackID=\(currentlyPlaying ?? "nil") lyricsCount=\(currentlyPlayingLyrics.count) isFetching=\(isFetching) emptyPostLoad=\(lyricsIsEmptyPostLoad)")
        }
        currentFetchTask = Task { try await self.fetchLyrics(for: trackID, trackName, checkCoreDataFirst: checkCoreDataFirst) }
        do {
            return try await currentFetchTask?.value
        } catch {
            spotifySyncLog("fetch failed trackID=\(trackID) error=\(error)")
            return nil
        }
    }

    #if os(macOS)
    func intToRGB(_ value: Int32) -> Color {//(red: Int, green: Int, blue: Int) {
        // Convert negative numbers to an unsigned 32-bit representation
        let unsignedValue = UInt32(bitPattern: value)
        
        // Extract RGB components
        let red = Double((unsignedValue >> 16) & 0xFF)
        let green = Double((unsignedValue >> 8) & 0xFF)
        let blue = Double(unsignedValue & 0xFF)
        return Color(red: red/255, green: green/255, blue: blue/255) //(red, green, blue)
    }
    
    func setBackgroundColor() {
        guard let currentlyPlaying else {
            return
        }
        let fetchRequest: NSFetchRequest<IDToColor> = IDToColor.fetchRequest()
        fetchRequest.predicate = NSPredicate(format: "id == %@", currentlyPlaying) // Replace trackID with the desired value

        do {
            let results = try coreDataContainer.viewContext.fetch(fetchRequest)
            if let idToColor = results.first {
                self.currentBackground = intToRGB(idToColor.songColor)
            } else {
                self.currentBackground = nil
            }
        } catch {
            print("Error fetching SongObject:", error)
        }
    }
    
    func handleSpotifyNoLyricsFallback() {
        // We know Spotify won’t give us a color for this track
        guard let currentlyPlaying else { return }
        
        guard let colorInt = artworkImage?.findWhiteTextLegibleMostSaturatedDominantColor() else {
            return
        }
        
        ColorDataService.saveColorToCoreData(trackID: currentlyPlaying, songColor: colorInt)
        currentBackground = intToRGB(colorInt)
    }
    #endif
    
    func fetchLyrics(for trackID: String, _ trackName: String, checkCoreDataFirst: Bool) async throws -> [LyricLine] {
        let initiatingTrackID = trackID
        spotifySyncLog("fetchLyrics begin trackID=\(trackID) trackName=\(trackName) checkCoreDataFirst=\(checkCoreDataFirst)")
        
        if checkCoreDataFirst, let lyrics = fetchFromCoreData(for: trackID) {
            spotifySyncLog("fetchLyrics CoreData hit trackID=\(trackID) count=\(lyrics.count)")
            try Task.checkCancellation()
            amplitude.track(eventType: "CoreData Fetch")
            // verify non-stale trackID
            if initiatingTrackID != self.currentlyPlaying {
                spotifySyncLog("fetchLyrics CoreData result stale initiated=\(initiatingTrackID) current=\(self.currentlyPlaying ?? "nil")")
                throw FetchError.staleTrack
            }
            return lyrics
        } else {
            spotifySyncLog("fetchLyrics CoreData miss; fetching remote trackID=\(trackID) trackName=\(trackName)")
            isFetching = true
            
            var networkLyrics: NetworkFetchReturn = await fetchAllNetworkLyrics()
            
            // verify non-stale trackID
            if initiatingTrackID != self.currentlyPlaying {
                spotifySyncLog("fetchLyrics network result stale initiated=\(initiatingTrackID) current=\(self.currentlyPlaying ?? "nil")")
                throw FetchError.staleTrack
            }
            
            guard let duration = currentPlayerInstance.duration else {
                spotifySyncLog("fetchLyrics remote failed: duration unavailable")
                return []
            }
            networkLyrics = networkLyrics.processed(withSongName: trackName, duration: duration)
            
            // verify non-stale trackID
            if initiatingTrackID == self.currentlyPlaying {
                callColorDataServiceOnLyricColorOrArtwork(colorData: networkLyrics.colorData)
            } else {
                spotifySyncLog("fetchLyrics skipping color save due to stale track initiated=\(initiatingTrackID) current=\(self.currentlyPlaying ?? "nil")")
                throw FetchError.staleTrack
            }
            spotifySyncLog("fetchLyrics remote finished trackID=\(trackID) count=\(networkLyrics.lyrics.count)")
            return networkLyrics.lyrics
        }
    }
    
    func deleteSongLocalePairing(trackID: String) {
        do {
            let fetchRequest: NSFetchRequest<SongToLocale> = SongToLocale.fetchRequest()
            fetchRequest.predicate = NSPredicate(format: "id == %@", trackID)
            guard let object = try coreDataContainer.viewContext.fetch(fetchRequest).first else { return print("Translation: No songToLocale object could be deleted, doesn't exist for trackID \(trackID)") }
            coreDataContainer.viewContext.delete(object)
            try coreDataContainer.viewContext.save()
        } catch {
            print("Error deleting data: \(error)")
        }
    }

    func deleteLyric(trackID: String) {
        do {
            let fetchRequest: NSFetchRequest<SongObject> = SongObject.fetchRequest()
            fetchRequest.predicate = NSPredicate(format: "id == %@", trackID)
            let object = try coreDataContainer.viewContext.fetch(fetchRequest).first
            object?.lyricsTimestamps.removeAll()
            object?.lyricsWords.removeAll()
            try coreDataContainer.viewContext.save()
            currentlyPlayingLyricsIndex = nil
            currentlyPlayingLyrics = []
            translatedLyric = []
            resetRomanization()
            chineseConversionLyrics = []
            lyricsIsEmptyPostLoad = true
        } catch {
            print("Error deleting data: \(error)")
        }
    }
    
    func fetchFromCoreData(for trackID: String) -> [LyricLine]? {
        let fetchRequest: NSFetchRequest<SongObject> = SongObject.fetchRequest()
        fetchRequest.predicate = NSPredicate(format: "id == %@", trackID) // Replace trackID with the desired value

        do {
            let results = try coreDataContainer.viewContext.fetch(fetchRequest)
            if let songObject = results.first {
                // Found the SongObject with the matching trackID
                let lyricsArray = zip(songObject.lyricsTimestamps, songObject.lyricsWords).map { LyricLine(startTime: $0, words: $1) }
                spotifySyncLog("fetchFromCoreData hit trackID=\(trackID) count=\(lyricsArray.count)")
                return lyricsArray
            } else {
                // No SongObject found with the given trackID
                spotifySyncLog("fetchFromCoreData miss trackID=\(trackID)")
            }
        } catch {
            spotifySyncLog("fetchFromCoreData error trackID=\(trackID) error=\(error)")
        }
        return nil
    }
    
    #if os(macOS)
    func reloadTranslationConfigIfTranslating() -> Bool {
        if userDefaultStorage.translate {
            if translationSessionConfig == TranslationSession.Configuration(source: translationSourceLanguage, target: userLocaleLanguage) {
                translationSessionConfig?.invalidate()
            } else {
                translationSessionConfig = TranslationSession.Configuration(source: translationSourceLanguage, target: userLocaleLanguage)
            }
            return true
        } else {
            return false
        }
    }
    #endif
    
    func fetchTranslationSourceLanguage() {
        guard let currentlyPlaying else {
            print("Translation: ignoring translationSourceLang fetch due to nil currentlyPlaying")
            return
        }
        let fetchRequest: NSFetchRequest<SongToLocale> = SongToLocale.fetchRequest()
        fetchRequest.predicate = NSPredicate(format: "id == %@", currentlyPlaying) // Replace trackID with the desired value

        do {
            let results = try coreDataContainer.viewContext.fetch(fetchRequest)
            if let songToLocale = results.first?.locale {
                self.translationSourceLanguage = Locale.Language(identifier: songToLocale)
            } else {
                self.translationSourceLanguage = nil
            }
        } catch {
            print("Error fetching translationSourceLanguage:", error)
        }
    }
    
    #if os(macOS)
    func setNewLyricsColorTranslationRomanizationAndStartUpdater(with newLyrics: [LyricLine]) {
        spotifySyncLog("setNewLyrics count=\(newLyrics.count) trackID=\(currentlyPlaying ?? "nil") isPlaying=\(isPlaying)")
        stopLyricUpdater()
        currentlyPlayingLyricsIndex = nil
        translatedLyric = []
        resetRomanization()
        chineseConversionLyrics = []
        currentlyPlayingLyrics = newLyrics
        if currentPlayer == .spotify, !newLyrics.isEmpty {
            resetSpotifyEmptyLyricsRetry()
        }
        setBackgroundColor()
        fetchTranslationSourceLanguage()
        let _ = reloadTranslationConfigIfTranslating()
        chinesePreferenceDidChange()
        lyricsIsEmptyPostLoad = currentlyPlayingLyrics.isEmpty
        spotifySyncLog("setNewLyrics done lyricsCount=\(currentlyPlayingLyrics.count) emptyPostLoad=\(lyricsIsEmptyPostLoad)")
        if isPlaying, !currentlyPlayingLyrics.isEmpty, showLyrics, userDefaultStorage.hasOnboarded {
            startLyricUpdater()
        }
    }
    
    @MainActor
    func uploadLocalLRCFile() async throws {
        guard let currentlyPlaying = currentlyPlaying, let currentlyPlayingName = currentlyPlayingName else {
            throw CancellationError()
        }
        let duration = self.duration
        let localLyrics = try await localFileUploadProvider.localFetch(for: currentlyPlaying, currentlyPlayingName)
        let cleanLyrics = NetworkFetchReturn(lyrics: localLyrics, colorData: nil).processed(withSongName: currentlyPlayingName, duration: duration).lyrics
        if self.currentlyPlaying == currentlyPlaying {
            setNewLyricsColorTranslationRomanizationAndStartUpdater(with: cleanLyrics)
        }
        
        // thats how i save to coredata
        let _ = SongObject(from: cleanLyrics, with: coreDataContainer.viewContext, trackID: currentlyPlaying, trackName: currentlyPlayingName)
        saveCoreData()
    }
    #endif
    
    func stepsToTakeAfterSettingsLyrics() async {
        
    }
    
    func didOnboard() {
        guard isPlayerRunning else {
            isPlaying = false
            currentlyPlaying = nil
            currentlyPlayingName = nil
            currentlyPlayingArtist = nil
            #if os(macOS)
            currentlyPlayingAppleMusicPersistentID = nil
            #endif
            return
        }
        print("Application just started (finished onboarding). lets check whats playing")
        if currentPlayerInstance.isPlaying {
            isPlaying = true
        }
        setCurrentProperties()
        switch currentPlayer {
            case .appleMusic:
                ensureAppleMusicWatchdog()
            case .spotify:
                ensureSpotifyWatchdog()
        }
        startLyricUpdater()
    }
}

#if os(macOS)
// Apple Music Code
extension ViewModel {
    // Similar structure to my other Async functions. Only 1 appleMusic) can run at any given moment
    func appleMusicStarter() async {
        print("apple music test called again, cancelling previous")
        currentAppleMusicFetchTask?.cancel()
        guard let expectedPersistentID = currentlyPlayingAppleMusicPersistentID,
              let sourceTrackName = appleMusicPlayer.trackName,
              let sourceArtistName = appleMusicPlayer.artistName else {
            return
        }
        let sourceAlbumName = appleMusicPlayer.albumName
        let newFetchTask = Task {
            try await self.appleMusicFetch(
                expectedPersistentID: expectedPersistentID,
                sourceTrackName: sourceTrackName,
                sourceArtistName: sourceArtistName,
                sourceAlbumName: sourceAlbumName
            )
        }
        currentAppleMusicFetchTask = newFetchTask
        do {
            return try await newFetchTask.value
        } catch {
            print("error \(error)")
            return
        }
    }
    
    func appleMusicFetch(
        expectedPersistentID: String,
        sourceTrackName: String,
        sourceArtistName: String,
        sourceAlbumName: String?
    ) async throws {
        let sourceFingerprint = appleMusicSourceFingerprint(
            trackName: sourceTrackName,
            artistName: sourceArtistName,
            albumName: sourceAlbumName
        )
        // check coredata for apple music persistent id -> spotify id mapping
        if let coreDataSpotifyID = fetchSpotifyIDFromPersistentIDCoreData(
            persistentID: expectedPersistentID,
            sourceFingerprint: sourceFingerprint
        ) {
            if !Task.isCancelled {
                guard currentlyPlayingAppleMusicPersistentID == expectedPersistentID,
                      appleMusicPlayer.persistentID == expectedPersistentID else {
                    return
                }
                print("Apple Music CoreData Fetch: setting currentlyPlaying to \(coreDataSpotifyID)")
                self.currentlyPlaying = coreDataSpotifyID
                return
            }
        }
        print("Apple Music Fetch: No CoreData val. Fetching from network")
        try await appleMusicNetworkFetch(
            expectedPersistentID: expectedPersistentID,
            sourceTrackName: sourceTrackName,
            sourceArtistName: sourceArtistName,
            sourceAlbumName: sourceAlbumName,
            sourceFingerprint: sourceFingerprint
        )
    }

    func appleMusicNetworkFetch(
        expectedPersistentID: String,
        sourceTrackName: String,
        sourceArtistName: String,
        sourceAlbumName: String?,
        sourceFingerprint: String
    ) async throws {
        isFetching = true
//        do {
//            print("Apple Music Network Fetch: 3 second sleep")
//            try await Task.sleep(for: .seconds(3))
//        } catch {
//            print("Apple Music Network Fetch cancelled during the 3 seconds of sleep")
//        }
        print("Apple Music Network Fetch: isFetching set to true")
        // coredata didn't get us anything
//        try await spotifyLyricProvider.generateAccessToken()
        
        // Task cancelled means we're working with old song data, so dont update Spotify ID with old song's ID
        
        // search for equivalent spotify song
        if let spotifyResult = try await musicToSpotifyHelper(
            sourceTrackName: sourceTrackName,
            sourceArtistName: sourceArtistName,
            sourceAlbumName: sourceAlbumName
        ) {
            try Task.checkCancellation()
            guard currentlyPlayingAppleMusicPersistentID == expectedPersistentID,
                  appleMusicPlayer.persistentID == expectedPersistentID else {
                return
            }
            self.currentlyPlaying = spotifyResult.SpotifyID
            saveSpotifyMapping(
                persistentID: expectedPersistentID,
                spotifyID: spotifyResult.SpotifyID,
                sourceFingerprint: sourceFingerprint
            )
        } else {
            if let alternativeID = appleMusicPlayer.alternativeID, alternativeID != "" {
                try Task.checkCancellation()
                guard currentlyPlayingAppleMusicPersistentID == expectedPersistentID,
                      appleMusicPlayer.persistentID == expectedPersistentID else {
                    return
                }
                self.currentlyPlaying = alternativeID
            } else {
                lyricsIsEmptyPostLoad = true
            }
        }
    }
    
    func fetchSpotifyIDFromPersistentIDCoreData(
        persistentID: String,
        sourceFingerprint: String
    ) -> String? {
        guard UserDefaults.standard.string(
            forKey: appleMusicMappingFingerprintKey(persistentID: persistentID)
        ) == sourceFingerprint else {
            print("Apple Music CoreData Fetch: mapping fingerprint missing or stale for \(persistentID)")
            return nil
        }
        let fetchRequest: NSFetchRequest<PersistentIDToSpotify> = PersistentIDToSpotify.fetchRequest()
        fetchRequest.predicate = NSPredicate(format: "persistentID == %@", persistentID)

        do {
            let results = try coreDataContainer.viewContext.fetch(fetchRequest)
            if let persistentIDToSpotify = results.first {
                // Found the persistentIDToSpotify object with the matching persistentID
                print("Apple Music CoreData Fetch: Found SpotifyID \(persistentIDToSpotify.spotifyID) for \(persistentIDToSpotify.persistentID)")
                return persistentIDToSpotify.spotifyID
            } else {
                // No SongObject found with the given trackID
                print("No spotifyID found with the provided persistentID. \(currentlyPlayingAppleMusicPersistentID)")
            }
        } catch {
            print("Error fetching persistentIDToSpotify:", error)
        }
        return nil
    }
    
    private func musicToSpotifyHelper(
        sourceTrackName: String,
        sourceArtistName: String,
        sourceAlbumName: String?
    ) async throws -> AppleMusicHelper? {
        guard let result = try await spotifyLyricProvider.searchForTrackForAppleMusic(
            artist: sourceArtistName,
            track: sourceTrackName,
            album: sourceAlbumName
        ) else {
            return nil
        }
        guard appleMusicTitlesPlausiblyMatch(sourceTrackName, result.SpotifyName) else {
            print("[LyricFever][AppleMusicSync] rejected Spotify mismatch "
                  + "source=\(sourceTrackName) candidate=\(result.SpotifyName)")
            return nil
        }
        return result
    }

    private func appleMusicTitlesPlausiblyMatch(_ source: String, _ candidate: String) -> Bool {
        let source = normalizedAppleMusicMetadata(source)
        let candidate = normalizedAppleMusicMetadata(candidate)
        guard !source.isEmpty, !candidate.isEmpty else {
            return false
        }
        if source == candidate {
            return true
        }
        let shorterCount = min(source.count, candidate.count)
        let longerCount = max(source.count, candidate.count)
        return shorterCount * 10 >= longerCount * 6
            && (source.contains(candidate) || candidate.contains(source))
    }

    private func normalizedAppleMusicMetadata(_ value: String) -> String {
        value
            .folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .unicodeScalars
            .filter(CharacterSet.alphanumerics.contains)
            .map(String.init)
            .joined()
    }

    private func appleMusicSourceFingerprint(
        trackName: String,
        artistName: String,
        albumName: String?
    ) -> String {
        [
            normalizedAppleMusicMetadata(trackName),
            normalizedAppleMusicMetadata(artistName),
            normalizedAppleMusicMetadata(albumName ?? "")
        ].joined(separator: "|")
    }

    private func appleMusicMappingFingerprintKey(persistentID: String) -> String {
        "appleMusicMappingFingerprint.\(persistentID)"
    }

    private func saveSpotifyMapping(
        persistentID: String,
        spotifyID: String,
        sourceFingerprint: String
    ) {
        let fetchRequest: NSFetchRequest<PersistentIDToSpotify> = PersistentIDToSpotify.fetchRequest()
        fetchRequest.predicate = NSPredicate(format: "persistentID == %@", persistentID)
        let mapping = (try? coreDataContainer.viewContext.fetch(fetchRequest).first)
            ?? PersistentIDToSpotify(context: coreDataContainer.viewContext)
        mapping.persistentID = persistentID
        mapping.spotifyID = spotifyID
        UserDefaults.standard.set(
            sourceFingerprint,
            forKey: appleMusicMappingFingerprintKey(persistentID: persistentID)
        )
        print("Apple Music Network Fetch: Saving persistent id \(persistentID) and Spotify ID \(spotifyID)")
        saveCoreData()
    }
}
#endif
