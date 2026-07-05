//
//  CurrentTimeWithStoredDate.swift
//  Lyric Fever
//
//

import Foundation

struct CurrentTimeWithStoredDate {
    let currentTime: TimeInterval
    let storedDate: Date
    
    init(currentTime: TimeInterval) {
        self.currentTime = currentTime
        self.storedDate = Date()
    }
    
    func adjustedCurrentTime(for date: Date) -> TimeInterval {
        let delta = date.timeIntervalSince(storedDate) * 1000 // convert seconds to milliseconds
        return currentTime + delta
    }
}
