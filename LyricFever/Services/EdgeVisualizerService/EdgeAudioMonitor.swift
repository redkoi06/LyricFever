//
//  EdgeAudioMonitor.swift
//  Lyric Fever
//
//  Created by Codex on 2026-06-19.
//

#if os(macOS)
import AudioToolbox
import CoreMedia
import Foundation
import ScreenCaptureKit

final class EdgeAudioMonitor: NSObject, SCStreamOutput {
    var onLevel: ((Double) -> Void)?
    var onStatus: ((String?) -> Void)?

    private var stream: SCStream?
    private let sampleQueue = DispatchQueue(label: "com.lyricfever.edge-visualizer.audio")
    private var smoothedLevel = 0.0
    private var lastPublishTime = CACurrentMediaTime()

    @MainActor
    func start() async {
        guard stream == nil else { return }

        guard #available(macOS 14.0, *) else {
            onStatus?("Edge visualizer requires macOS 14 or later.")
            return
        }

        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            guard let display = content.displays.first else {
                onStatus?("No display is available for audio capture.")
                return
            }

            let currentBundleIdentifier = Bundle.main.bundleIdentifier
            let excludedApplications = content.applications.filter { app in
                app.bundleIdentifier == currentBundleIdentifier
            }
            let filter = SCContentFilter(display: display,
                                         excludingApplications: excludedApplications,
                                         exceptingWindows: [])
            let configuration = SCStreamConfiguration()
            configuration.width = 2
            configuration.height = 2
            configuration.minimumFrameInterval = CMTime(value: 1, timescale: 1)
            configuration.showsCursor = false
            configuration.capturesAudio = true
            configuration.excludesCurrentProcessAudio = true
            configuration.sampleRate = 48_000
            configuration.channelCount = 2

            let stream = SCStream(filter: filter, configuration: configuration, delegate: nil)
            try stream.addStreamOutput(self, type: .audio, sampleHandlerQueue: sampleQueue)
            try await stream.startCapture()

            self.stream = stream
            onStatus?(nil)
        } catch {
            self.stream = nil
            onStatus?("Allow Screen Recording permission to use the edge visualizer.")
            publish(level: 0)
            print("Edge visualizer audio capture failed: \(error)")
        }
    }

    @MainActor
    func stop() async {
        guard let stream else {
            publish(level: 0)
            onStatus?(nil)
            return
        }

        do {
            try await stream.stopCapture()
        } catch {
            print("Edge visualizer audio capture stop failed: \(error)")
        }
        self.stream = nil
        smoothedLevel = 0
        publish(level: 0)
        onStatus?(nil)
    }

    func stream(_ stream: SCStream,
                didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
                of type: SCStreamOutputType) {
        guard type == .audio, sampleBuffer.isValid else { return }

        let rawLevel = rmsLevel(from: sampleBuffer)
        let normalizedLevel = min(1.0, max(0.0, rawLevel * 5.0))
        let smoothing = normalizedLevel > smoothedLevel ? 0.35 : 0.10
        smoothedLevel += (normalizedLevel - smoothedLevel) * smoothing

        let now = CACurrentMediaTime()
        guard now - lastPublishTime >= 1.0 / 30.0 else { return }
        lastPublishTime = now
        publish(level: smoothedLevel)
    }

    private func publish(level: Double) {
        DispatchQueue.main.async { [onLevel] in
            onLevel?(level)
        }
    }

    private func rmsLevel(from sampleBuffer: CMSampleBuffer) -> Double {
        guard let formatDescription = CMSampleBufferGetFormatDescription(sampleBuffer),
              let streamDescription = CMAudioFormatDescriptionGetStreamBasicDescription(formatDescription) else {
            return 0
        }

        let audioBufferList = AudioBufferList.allocate(maximumBuffers: 8)
        defer {
            audioBufferList.unsafeMutablePointer.deallocate()
        }

        var blockBuffer: CMBlockBuffer?
        let bufferListSize = MemoryLayout<AudioBufferList>.size + MemoryLayout<AudioBuffer>.size * 7
        let status = CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(
            sampleBuffer,
            bufferListSizeNeededOut: nil,
            bufferListOut: audioBufferList.unsafeMutablePointer,
            bufferListSize: bufferListSize,
            blockBufferAllocator: kCFAllocatorDefault,
            blockBufferMemoryAllocator: kCFAllocatorDefault,
            flags: UInt32(kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment),
            blockBufferOut: &blockBuffer
        )

        guard status == noErr else {
            return 0
        }

        let asbd = streamDescription.pointee
        let bytesPerSample = max(1, Int(asbd.mBitsPerChannel / 8))
        let isFloat = (asbd.mFormatFlags & kAudioFormatFlagIsFloat) != 0
        let isSignedInteger = (asbd.mFormatFlags & kAudioFormatFlagIsSignedInteger) != 0

        var squareSum = 0.0
        var sampleCount = 0

        for buffer in audioBufferList {
            guard let data = buffer.mData else { continue }
            let count = Int(buffer.mDataByteSize) / bytesPerSample
            guard count > 0 else { continue }

            if isFloat, bytesPerSample == MemoryLayout<Float>.size {
                let samples = data.assumingMemoryBound(to: Float.self)
                for index in 0..<count {
                    let value = Double(samples[index])
                    squareSum += value * value
                }
                sampleCount += count
            } else if isSignedInteger, bytesPerSample == MemoryLayout<Int16>.size {
                let samples = data.assumingMemoryBound(to: Int16.self)
                for index in 0..<count {
                    let value = Double(samples[index]) / Double(Int16.max)
                    squareSum += value * value
                }
                sampleCount += count
            } else if isSignedInteger, bytesPerSample == MemoryLayout<Int32>.size {
                let samples = data.assumingMemoryBound(to: Int32.self)
                for index in 0..<count {
                    let value = Double(samples[index]) / Double(Int32.max)
                    squareSum += value * value
                }
                sampleCount += count
            }
        }

        guard sampleCount > 0 else {
            return 0
        }

        return sqrt(squareSum / Double(sampleCount))
    }
}
#endif
