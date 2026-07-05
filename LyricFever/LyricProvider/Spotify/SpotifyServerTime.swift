//
//  SpotifyServerTime.swift
//  Lyric Fever
//
//


// Spotify TOTP Login Fix
struct SpotifyServerTime: Decodable {
    let serverTime: Int
}