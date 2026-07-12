//
//  TranslationSettingsView.swift
//  Lyric Fever
//

import SwiftUI
@preconcurrency import Translation

struct TranslationSettingsView: View {
    @Environment(ViewModel.self) private var viewmodel
    @State private var supportedLanguages: [Locale.Language] = []
    @State private var sourceLanguageIdentifier: String?
    @State private var targetLanguageIdentifier: String?
    @State private var isLoadingLanguages = true

    private var currentSongDescription: String {
        guard let title = viewmodel.currentlyPlayingName, !title.isEmpty else {
            return "当前没有歌曲"
        }
        return title
    }

    var body: some View {
        @Bindable var viewmodel = viewmodel

        Form {
            Section {
                Toggle("翻译歌词", isOn: $viewmodel.userDefaultStorage.translate)

                Picker("目标语言", selection: $targetLanguageIdentifier) {
                    Text("跟随系统（\(viewmodel.systemLocaleString)）")
                        .tag(nil as String?)
                    ForEach(supportedLanguages, id: \.maximalIdentifier) { language in
                        Text(languageName(language))
                            .tag(language.maximalIdentifier as String?)
                    }
                }
                .disabled(isLoadingLanguages)
            } header: {
                Text("翻译")
            } footer: {
                Text("目标语言对所有歌曲生效。")
            }

            Section {
                LabeledContent("当前歌曲", value: currentSongDescription)

                Picker("原始语言", selection: $sourceLanguageIdentifier) {
                    Text("自动检测")
                        .tag(nil as String?)
                    ForEach(supportedLanguages, id: \.maximalIdentifier) { language in
                        Text(languageName(language))
                            .tag(language.maximalIdentifier as String?)
                    }
                }
                .disabled(isLoadingLanguages || viewmodel.currentlyPlaying == nil)
            } header: {
                Text("当前歌曲")
            } footer: {
                Text("仅在自动检测不准确时手动选择原始语言。")
            }

            Section("文字转换") {
                Toggle("显示罗马音", isOn: $viewmodel.userDefaultStorage.romanize)

                Picker("中文转换", selection: $viewmodel.userDefaultStorage.chinesePreference) {
                    ForEach(ChineseConversion.allCases) { conversion in
                        Text(conversion.description).tag(conversion.rawValue)
                    }
                }
            }

            if isLoadingLanguages {
                HStack(spacing: 8) {
                    ProgressView()
                        .controlSize(.small)
                    Text("正在加载支持的语言…")
                        .foregroundStyle(.secondary)
                }
            }
        }
        .formStyle(.grouped)
        .frame(maxWidth: 520)
        .task {
            sourceLanguageIdentifier = viewmodel.translationSourceLanguage?.maximalIdentifier
            targetLanguageIdentifier = viewmodel.userDefaultStorage.translationTargetLanguage?.maximalIdentifier

            let languages = await LanguageAvailability().supportedLanguages
            supportedLanguages = languages.sorted {
                languageName($0).localizedStandardCompare(languageName($1)) == .orderedAscending
            }
            isLoadingLanguages = false
        }
        .onChange(of: sourceLanguageIdentifier) { _, identifier in
            let language = identifier.map(Locale.Language.init(identifier:))
            guard language?.maximalIdentifier != viewmodel.translationSourceLanguage?.maximalIdentifier else {
                return
            }
            viewmodel.setTranslationSourceLanguage(language)
        }
        .onChange(of: targetLanguageIdentifier) { _, identifier in
            let language = identifier.map(Locale.Language.init(identifier:))
            guard language?.maximalIdentifier != viewmodel.userDefaultStorage.translationTargetLanguage?.maximalIdentifier else {
                return
            }
            viewmodel.userDefaultStorage.translationTargetLanguage = language
        }
        .onChange(of: viewmodel.translationSourceLanguage?.maximalIdentifier) { _, identifier in
            if sourceLanguageIdentifier != identifier {
                sourceLanguageIdentifier = identifier
            }
        }
        .onChange(of: viewmodel.userDefaultStorage.translationTargetLanguage?.maximalIdentifier) { _, identifier in
            if targetLanguageIdentifier != identifier {
                targetLanguageIdentifier = identifier
            }
        }
    }

    private func languageName(_ language: Locale.Language) -> String {
        Locale.current.localizedString(forIdentifier: language.minimalIdentifier)
            ?? language.maximalIdentifier
    }
}
