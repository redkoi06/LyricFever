//
//  TranslationResult.swift
//  Lyric Fever
//
//

import Translation

enum TranslationResult {
    case success([TranslationSession.Response])
    case needsConfigUpdate(Locale.Language)
    case failure
}
