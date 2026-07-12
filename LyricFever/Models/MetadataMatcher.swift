//
//  MetadataMatcher.swift
//  Lyric Fever
//

import Foundation

enum MetadataMatcher {
    static func normalized(_ value: String) -> String {
        value
            .folding(options: [.caseInsensitive, .diacriticInsensitive, .widthInsensitive], locale: .current)
            .unicodeScalars
            .filter(CharacterSet.alphanumerics.contains)
            .map(String.init)
            .joined()
    }

    static func titleCandidates(for title: String) -> [String] {
        let stripped = title
            .replacingOccurrences(
                of: #"[\s　]*[\(\（\[\【].*?[\)\）\]\】][\s　]*"#,
                with: " ",
                options: .regularExpression
            )
            .trimmingCharacters(in: .whitespacesAndNewlines)

        return [title, stripped]
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .reduce(into: []) { result, candidate in
                if !result.contains(candidate) {
                    result.append(candidate)
                }
            }
    }

    static func relevance(of result: SongResult, trackName: String, artistName: String) -> Int {
        let resultTitle = normalized(result.songName)
        let queryTitles = titleCandidates(for: trackName)
            .map(normalized)
            .filter { !$0.isEmpty }
        guard !resultTitle.isEmpty, !queryTitles.isEmpty else {
            return 0
        }

        let titleScore: Int
        if queryTitles.contains(resultTitle) {
            titleScore = 100
        } else if queryTitles.contains(where: { containsMatch($0, resultTitle, minimumRatioTenths: 4) }) {
            titleScore = 70
        } else {
            return 0
        }

        let queryArtist = normalized(artistName)
        let resultArtist = normalized(result.artistName)
        guard !queryArtist.isEmpty, !resultArtist.isEmpty else {
            return titleScore
        }
        if queryArtist == resultArtist {
            return titleScore + 30
        }
        if queryArtist.contains(resultArtist) || resultArtist.contains(queryArtist) {
            return titleScore + 15
        }
        return titleScore
    }

    static func filteredAndSorted(
        _ results: [SongResult],
        trackName: String,
        artistName: String
    ) -> [SongResult] {
        let scored = deduplicated(results).compactMap { result -> (result: SongResult, score: Int)? in
            guard !result.lyrics.isEmpty else { return nil }
            let score = relevance(of: result, trackName: trackName, artistName: artistName)
            return score > 0 ? (result, score) : nil
        }
        return scored.sorted { $0.score > $1.score }.map(\.result)
    }

    static func plausiblyMatches(_ source: String, _ candidate: String) -> Bool {
        containsMatch(normalized(source), normalized(candidate), minimumRatioTenths: 6)
    }

    private static func deduplicated(_ results: [SongResult]) -> [SongResult] {
        var seen: Set<String> = []
        return results.filter { result in
            let key = [
                result.lyricType,
                normalized(result.songName),
                normalized(result.artistName),
                normalized(result.albumName)
            ].joined(separator: "|")
            return seen.insert(key).inserted
        }
    }

    private static func containsMatch(
        _ source: String,
        _ candidate: String,
        minimumRatioTenths: Int
    ) -> Bool {
        guard !source.isEmpty, !candidate.isEmpty else {
            return false
        }
        if source == candidate {
            return true
        }
        let shorterCount = min(source.count, candidate.count)
        let longerCount = max(source.count, candidate.count)
        return shorterCount * 10 >= longerCount * minimumRatioTenths
            && (source.contains(candidate) || candidate.contains(source))
    }
}
