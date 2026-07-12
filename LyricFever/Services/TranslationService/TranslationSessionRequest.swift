//
//  TranslationSessionRequest.swift
//  Lyric Fever
//
//

import Translation

// Translation.Request is a value type whose stored values are Sendable, but the
// framework does not currently declare that conformance. The explicit
// conformance keeps batch requests safe across the framework's async boundary.
extension TranslationSession.Request: @retroactive @unchecked Sendable {}

extension TranslationSession.Request {
    init(lyric: LyricLine) {
        self.init(sourceText: lyric.words, clientIdentifier: lyric.id.uuidString)
    }
}
