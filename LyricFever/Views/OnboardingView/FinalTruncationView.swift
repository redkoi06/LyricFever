//
//  FinalTruncationView.swift
//  Lyric Fever
//
//  Created by Avi Wadhwa on 2025-07-26.
//

import SwiftUI



struct FinalTruncationView: View {
    @Environment(\.dismiss) var dismiss
    @State var truncationLength: Int = min(max(UserDefaults.standard.integer(forKey: "truncationLength"), 10), 20)
    @Environment(\.controlActiveState) var controlActiveState
    let allTruncations = Array(10...20)
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            StepView(title: "Set the Lyric Size", description: "This depends on how much free space you have in your menu bar!")
            
            HStack {
                Spacer()
                Text("\(truncationLength)")
                    .font(.system(size: 42, weight: .semibold, design: .rounded))
                    .monospacedDigit()
                    .frame(width: 120, height: 72)
                    .background(.quaternary, in: RoundedRectangle(cornerRadius: 10))
                    .onAppear() {
                        if truncationLength == 0 {
                            truncationLength = 10
                        }
                    }
                Spacer()
            }
            
            HStack {
                Spacer()
                Picker("Truncation Length", selection: $truncationLength) {
                    ForEach(allTruncations, id:\.self) { oneThing in
                        Text("\(oneThing) Characters")
                    }
                }
                .pickerStyle(.radioGroup)
                Spacer()
            }
            
            HStack {
                Button("Back") {
                    dismiss()
                }
                Spacer()
                Button("Done") {
                    NSApplication.shared.keyWindow?.close()
                    
                }
                .buttonStyle(.borderedProminent)
            }
            .padding(.vertical, 15)
            
        }
        .onChange(of: truncationLength) {
            UserDefaults.standard.set(truncationLength, forKey: "truncationLength")
        }
        .padding(.horizontal, 20)
        .navigationBarBackButtonHidden(true)
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.willCloseNotification)) { newValue in
            dismiss()
            dismiss()
            dismiss()
        }
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.willMiniaturizeNotification)) { newValue in
            dismiss()
            dismiss()
            dismiss()
        }
    }
}
