//
//  ColorDataService.swift
//  Lyric Fever
//
//
@MainActor
class ColorDataService {
    static func saveColorToCoreData(trackID: String, songColor: Int32) {
        let newColorMapping = IDToColor(context: ViewModel.shared.coreDataContainer.viewContext)
        newColorMapping.id = trackID
        newColorMapping.songColor = songColor
        do {
            try ViewModel.shared.coreDataContainer.viewContext.save()
            #if DEBUG
            print("ColorDataService: Successfully saved color \(songColor) for trackID \(trackID)")
            #endif
        } catch {
            print("ColorDataService: Couldn't save color mapping to CoreData: \(error)")
        }
    }
}
