//
//  accessTokenJSON.swift
//  Lyric Fever
//
//

import Foundation


struct AccessTokenJSON: Codable {
    let accessToken: String
    let accessTokenExpirationTimestampMs: TimeInterval
    let isAnonymous: Bool
}
