//
//  StepView.swift
//  Lyric Fever
//
//

import SwiftUI

struct StepView: View {
    var title: LocalizedStringKey
    var description: LocalizedStringKey
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(.title2)
                .bold()
            
            Text(description)
                .font(.title3)
        }
    }
}
