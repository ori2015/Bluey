import AVFoundation
import Foundation

/// Records only during a hold; all transcription, AI and tools run on the paired desktop.
final class PushToTalkController: NSObject, ObservableObject, AVAudioRecorderDelegate {
    enum State: Equatable { case asleep, waking, listening, asking, thinking, speaking }
    @Published private(set) var state: State = .asleep
    @Published private(set) var caption = ""
    var sendPacket: ((Packet) -> Void)?
    var onCaption: ((String, Bool) -> Void)?
    var onStateChange: ((State) -> Void)?
    var onSessionStart: (() -> Void)?
    var onSessionEnd: (() -> Void)?
    var onUserTurn: ((String, Bool) -> Void)?
    var onUserWords: ((String, String, Bool) -> Void)?
    var onReply: ((String) -> Void)?
    var onReport: ((String) -> Void)?
    private(set) var level: Double = 0
    var volume: Double {
        get { UserDefaults.standard.object(forKey: "volume") as? Double ?? 1 }
        set { UserDefaults.standard.set(newValue, forKey: "volume"); player.volume = Float(newValue) }
    }
    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let playFormat = AVAudioFormat(standardFormatWithSampleRate: 24000, channels: 1)!
    private var pendingBuffers = 0
    private var recorder: AVAudioRecorder?
    private var recordingURL: URL?
    private var holding = false
    override init() {
        super.init()
        let directory = FileManager.default.temporaryDirectory
        for file in (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? [] {
            if file.lastPathComponent.hasPrefix("GooglyVoice-") && file.pathExtension == "wav" { try? FileManager.default.removeItem(at: file) }
        }
    }
    private var releasedAt: Date?
    private var activeCall: String?
    private var replyTimeout: DispatchWorkItem?
    private var permissionAttempt = UUID()
    private func setState(_ value: State) { guard value != state else { return }; state = value; onStateChange?(value) }
    func toggle() { state == .asleep ? wake() : sleep() }
    func wake() {
        guard state == .asleep else { return }
        setState(.waking)
        let attempt = UUID(); permissionAttempt = attempt
        AVAudioApplication.requestRecordPermission { [weak self] granted in
            DispatchQueue.main.async {
                guard let self, self.permissionAttempt == attempt, self.state == .waking else { return }
                guard granted else { self.show("Enable the microphone for Googly Eyes in iPhone Settings.", done: true); self.setState(.asleep); return }
                do {
                    let session = AVAudioSession.sharedInstance()
                    try session.setCategory(.playAndRecord, mode: .default, options: [.defaultToSpeaker, .allowBluetooth])
                    try session.setActive(true)
                    if self.player.engine == nil { self.engine.attach(self.player); self.engine.connect(self.player, to: self.engine.mainMixerNode, format: self.playFormat) }
                    try self.engine.start(); self.player.volume = Float(self.volume); self.player.play()
                    self.onSessionStart?(); self.setState(.listening); self.chirp(syllables: 2)
                    if self.holding { self.startRecording() }
                } catch { self.show("Couldn't start the microphone.", done: true); self.sleep() }
            }
        }
    }
    func sleep() {
        permissionAttempt = UUID(); holding = false
        recorder?.stop(); recorder = nil; removeRecording()
        replyTimeout?.cancel(); replyTimeout = nil
        if let id = activeCall { sendPacket?(Packet(command: "cancel_request", callID: id)) }
        activeCall = nil; player.stop(); engine.stop(); level = 0
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
        if state != .asleep { onSessionEnd?() }; setState(.asleep)
    }
    func disconnected() { show("Your desktop disconnected. Reconnect and hold to ask again.", done: true); sleep() }
    func sayHi() {
        if state == .asleep { wake(); return }
        guard activeCall == nil else { return }
        let id = UUID().uuidString; activeCall = id; setState(.thinking); armTimeout()
        sendPacket?(Packet(command: "text_request", callID: id, text: "Say a cheerful hello in under eight words."))
    }
    func beginAsk() {
        guard activeCall == nil else { return }
        holding = true; show("", done: false)
        if state == .asleep { wake() }
        else if state == .listening || state == .speaking { startRecording() }
    }
    /// The desktop is recording from its own keyboard shortcut: mirror the hold UI without recording here.
    func remoteListening(_ on: Bool) {
        if on, state == .listening { setState(.asking) }
        else if !on, state == .asking, recorder == nil { setState(.listening) }
    }
    private func startRecording() {
        guard holding, recorder == nil else { return }
        player.stop(); level = 0 // No chirp contaminates microphone input.
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("GooglyVoice-" + UUID().uuidString + ".wav")
        do {
            let settings: [String: Any] = [AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: 24000,
                AVNumberOfChannelsKey: 1, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false,
                AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsNonInterleaved: false]
            let recorder = try AVAudioRecorder(url: url, settings: settings)
            self.recorder = recorder; recordingURL = url; recorder.delegate = self
            guard recorder.record(forDuration: 60) else { throw NSError(domain: "GooglyMic", code: 1) }
            setState(.asking)
        } catch { removeRecording(); recorder = nil; holding = false; show("Couldn't record your question.", done: true); setState(.listening) }
    }
    func endAsk() {
        guard holding else { return }; holding = false
        guard let recorder else { return } // Released during permission request: never record after release.
        recorder.stop(); self.recorder = nil
        defer { removeRecording() }
        guard let url = recordingURL, let audio = try? Data(contentsOf: url), audio.count > 1000, audio.count < 3_000_000 else { setState(.listening); return }
        let id = UUID().uuidString; activeCall = id; releasedAt = Date(); onUserTurn?(id, true); setState(.thinking); armTimeout()
        sendPacket?(Packet(command: "voice_request", audio: audio.base64EncodedString(), callID: id))
    }
    func audioRecorderDidFinishRecording(_ recorder: AVAudioRecorder, successfully flag: Bool) {
        DispatchQueue.main.async { [weak self] in
            guard let self, self.recorder === recorder, self.holding else { return }
            if flag { self.endAsk() } else { self.recorder = nil; self.removeRecording(); self.holding = false; self.setState(.listening) }
        }
    }
    private func removeRecording() { if let url = recordingURL { try? FileManager.default.removeItem(at: url) }; recordingURL = nil }
    private func armTimeout() {
        replyTimeout?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, let id = self.activeCall else { return }
            self.sendPacket?(Packet(command: "cancel_request", callID: id)); self.activeCall = nil
            self.show("The request timed out. Hold to try again.", done: true); self.setState(.listening)
        }
        replyTimeout = work; DispatchQueue.main.asyncAfter(deadline: .now() + 240, execute: work)
    }
    func receive(_ packet: Packet) {
        guard let id = activeCall, packet.callID == id else { return }
        switch packet.command {
        case "transcript":
            #if DEBUG
            if let release = releasedAt { NSLog("Googly PTT release_to_transcript_ms=%.0f", Date().timeIntervalSince(release) * 1000) }
            #endif
            onUserWords?(id, packet.text ?? "", true)
        case "assistant_delta":
            let first = state != .speaking; setState(.speaking)
            show(caption + (packet.text ?? ""), done: false)
            if first { player.play(); level = 0.6; chirp(syllables: 3) }
        case "assistant_done":
            show(packet.text ?? caption, done: true); onReply?(caption)
            activeCall = nil; replyTimeout?.cancel(); setState(.listening)
        case "assistant_error":
            show(packet.text ?? "Couldn't finish that request.", done: true)
            activeCall = nil; replyTimeout?.cancel(); setState(.listening)
        default: break
        }
    }
    private func show(_ text: String, done: Bool) { caption = text; onCaption?(text, done) }
    /// A tiny cartoon chirp: a few soft, round, bell-like notes from a happy pentatonic scale,
    /// each with a little upward "boop" at the start, like a small creature humming.
    private func chirp(syllables: Int) {
        guard player.isPlaying else { return }
        let rate = playFormat.sampleRate
        // C major pentatonic, two octaves up high (C6…E7), so it always sounds sweet together.
        let scale = [1046.5, 1174.7, 1318.5, 1568.0, 1760.0, 2093.0, 2349.3, 2637.0]
        var index = Int.random(in: 1...3)
        var samples: [Float] = []
        for i in 0..<syllables {
            if i > 0 { index = min(max(index + [-1, 1, 1, 2].randomElement()!, 0), scale.count - 1) }
            let note = scale[index] * 0.5  // drop an octave: rounder, less piercing
            let duration = i == syllables - 1 ? 0.13 : Double.random(in: 0.07...0.09)
            let count = Int(duration * rate)
            var phase = 0.0
            for n in 0..<count {
                let time = Double(n) / rate
                let t = Double(n) / Double(count)
                // Scoops up into the note over the first 25 ms, with a gentle wobble on the last one.
                let scoop = 1 - 0.18 * exp(-time / 0.012)
                let wobble = i == syllables - 1 ? 1 + 0.012 * sin(time * 2 * .pi * 18) : 1
                phase += 2 * .pi * note * scoop * wobble / rate
                let attack = min(1, time / 0.006)
                let envelope = attack * exp(-t * 3.2) * (1 - pow(t, 6))
                let wave = sin(phase) + 0.12 * sin(2 * phase)
                samples.append(Float(wave * envelope * 0.26))
            }
            samples += [Float](repeating: 0, count: Int(rate * 0.028))
        }
        guard let buffer = AVAudioPCMBuffer(pcmFormat: playFormat, frameCapacity: AVAudioFrameCount(samples.count)),
              let out = buffer.floatChannelData?[0] else { return }
        buffer.frameLength = AVAudioFrameCount(samples.count)
        samples.withUnsafeBufferPointer { out.update(from: $0.baseAddress!, count: samples.count) }
        pendingBuffers += 1
        player.scheduleBuffer(buffer) { [weak self] in
            DispatchQueue.main.async {
                guard let self else { return }
                self.pendingBuffers = max(0, self.pendingBuffers - 1)
                if self.pendingBuffers == 0 { self.level = 0 }
                self.level = 0
            }
        }
    }
}

// Compatibility name for the existing SwiftUI views.
typealias LiveVoice = PushToTalkController
