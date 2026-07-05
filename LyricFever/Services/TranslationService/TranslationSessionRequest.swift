//
//  TranslationSessionRequest.swift
//  Lyric Fever
//
//

import Translation

extension TranslationSession.Request {
    init(lyric: LyricLine) {
        self.init(sourceText: lyric.words, clientIdentifier: lyric.id.uuidString)
    }
}
