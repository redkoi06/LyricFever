//
//  ChineseConversion.swift
//  Lyric Fever
//
//

enum ChineseConversion: Int, CaseIterable, Identifiable {
    case none = 0
    case simplified
    case traditionalNeutral
    case traditionalTaiwan
    case traditionalHK
    
    var id: Self {
        return self
    }
    
    var description: String {
        switch self {
            case .none:
                "None"
            case .simplified:
                "Simplified"
            case .traditionalNeutral:
                "Traditional (Neutral)"
            case .traditionalTaiwan:
                "Traditional (Taiwan)"
            case .traditionalHK:
                "Traditional (HK)"
        }
    }
}
