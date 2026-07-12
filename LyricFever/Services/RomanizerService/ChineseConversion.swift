//
//  ChineseConversion.swift
//  Lyric Fever
//
//

enum ChineseConversion: Int, CaseIterable, Identifiable, Sendable {
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
                "不转换"
            case .simplified:
                "简体中文"
            case .traditionalNeutral:
                "繁体中文（通用）"
            case .traditionalTaiwan:
                "繁体中文（台湾）"
            case .traditionalHK:
                "繁体中文（香港）"
        }
    }
}
