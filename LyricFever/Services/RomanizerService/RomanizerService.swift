//
//  RegularRomanizer.swift
//  Lyric Fever
//
//


//
//  RegularRomanizer.swift
//  Lyric Fever
//
//

import NaturalLanguage
import Mecab_Swift
import IPADic
import OpenCC

class RomanizerService {
    private static func generateJapaneseRomanizedString(
        _ string: String,
        tokenizer: Tokenizer
    ) -> String? {
        let tokens = tokenizer.tokenize(text: string, transliteration: .romaji)
        guard !tokens.isEmpty else {
            return nil
        }

        var result = ""
        for token in tokens {
            let reading = token.reading.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !reading.isEmpty else {
                continue
            }

            let isPunctuation = token.base.unicodeScalars.allSatisfy {
                CharacterSet.punctuationCharacters.contains($0)
                    || CharacterSet.symbols.contains($0)
            }
            if result.isEmpty || isPunctuation {
                result += reading
            } else {
                result += " " + reading
            }
        }

        return result.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func generateRomanizedString(
        _ string: String,
        language: NLLanguage?,
        japaneseTokenizer: Tokenizer?
    ) -> String? {
        if language == .japanese, let japaneseTokenizer {
            return generateJapaneseRomanizedString(string, tokenizer: japaneseTokenizer)
        }
        return string.applyingTransform(.toLatin, reverse: false)
    }

    private static func comparisonKey(_ string: String) -> String {
        string
            .folding(options: [.caseInsensitive, .diacriticInsensitive], locale: .current)
            .unicodeScalars
            .filter { CharacterSet.alphanumerics.contains($0) }
            .map(String.init)
            .joined()
    }

    static func generateRomanizedLyrics(_ lyrics: [String]) -> [String] {
        guard !lyrics.isEmpty else {
            return []
        }

        let combinedLyrics = lyrics.joined(separator: "\n")
        let language = NLLanguageRecognizer.dominantLanguage(for: combinedLyrics)
        let japaneseTokenizer: Tokenizer?
        if language == .japanese {
            japaneseTokenizer = try? Tokenizer(dictionary: IPADic())
        } else {
            japaneseTokenizer = nil
        }

        return lyrics.map { lyric in
            guard let romanized = generateRomanizedString(
                lyric,
                language: language,
                japaneseTokenizer: japaneseTokenizer
            )?.trimmingCharacters(in: .whitespacesAndNewlines),
            !romanized.isEmpty,
            comparisonKey(romanized) != comparisonKey(lyric) else {
                return ""
            }
            return romanized
        }
    }

    static func generateRomanizedLyric(_ lyric: LyricLine) -> String? {
        let language = NLLanguageRecognizer.dominantLanguage(for: lyric.words)
        let japaneseTokenizer = language == .japanese ? try? Tokenizer(dictionary: IPADic()) : nil
        return generateRomanizedString(
            lyric.words,
            language: language,
            japaneseTokenizer: japaneseTokenizer
        )
    }
    
    static func generateRomanizedString(_ string: String) -> String? {
        let language = NLLanguageRecognizer.dominantLanguage(for: string)
        let japaneseTokenizer = language == .japanese ? try? Tokenizer(dictionary: IPADic()) : nil
        return generateRomanizedString(
            string,
            language: language,
            japaneseTokenizer: japaneseTokenizer
        )
    }


    static func generateChineseConversion(
        _ lyrics: [String],
        preference: ChineseConversion
    ) -> [String]? {
        do {
            let converter: ChineseConverter
            switch preference {
            case .none:
                return lyrics
            case .simplified:
                converter = try ChineseConverter(options: [.simplify])
            case .traditionalNeutral:
                converter = try ChineseConverter(options: [.traditionalize])
            case .traditionalTaiwan:
                converter = try ChineseConverter(options: [.traditionalize, .twStandard, .twIdiom])
            case .traditionalHK:
                converter = try ChineseConverter(options: [.traditionalize, .hkStandard])
            }
            return lyrics.map(converter.convert)
        } catch {
            print("RomanizerService: Chinese conversion error: \(error)")
            return nil
        }
    }
}
