//
//  ErrorWrapper.swift
//  Lyric Fever
//
//


struct ErrorWrapper: Codable {
    struct Error: Codable {
        let code: Int
        let message: String
    }

    let error: Error
}
