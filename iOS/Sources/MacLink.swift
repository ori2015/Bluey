import Foundation
import Network
import SwiftUI
import Security
import CryptoKit
import UIKit

// Internal name retained so existing face/settings views need minimal changes.
final class MacLink: ObservableObject {
    @Published private(set) var connected = false
    @Published private(set) var macName: String?
    @Published private(set) var macs: [String] = []
    @Published private(set) var currentMac: String?
    @Published private(set) var preferredMac = UserDefaults.standard.string(forKey: "preferredMac")
    @Published private(set) var needsPairing = false
    @Published private(set) var fingerprint = ""
    @Published private(set) var pairingError: String?
    var onCommand: ((String) -> Void)?
    var onEvent: ((Packet) -> Void)?
    var onDisconnect: (() -> Void)?
    var onFace: ((FaceState) -> Void)?
    private var browser: NWBrowser?
    private var link: LineConnection?
    private var retry: DispatchWorkItem?
    private var retryCount = 0
    private var keepalive: Timer?
    private var lastPacket = Date()
    private var pairSubmitted = false
    private var pendingHost: String?
    private var credential: DesktopCredential?
    private var enabled = false
    private static let verifyQueue = DispatchQueue(label: "Googly.TLS.verify")
    private let deviceID = DesktopKeychain.deviceID

    func start() {
        enabled = true
        guard browser == nil else { return }
        let browser = NWBrowser(for: .bonjour(type: GooglyService.type, domain: nil), using: .googly)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            guard let self else { return }
            self.macs = results.compactMap(Self.serviceName).sorted()
            self.connectIfNeeded()
        }
        browser.stateUpdateHandler = { [weak self] state in
            if case .failed = state { self?.browser?.cancel(); self?.browser = nil; self?.scheduleRetry() }
        }
        self.browser = browser
        browser.start(queue: .main)
    }
    func stop() {
        enabled = false; retry?.cancel(); keepalive?.invalidate(); keepalive = nil
        browser?.cancel(); browser = nil; link?.cancel(); link = nil
        if connected { onDisconnect?() }
        connected = false
    }
    private func scheduleRetry() {
        guard enabled else { return }
        retry?.cancel(); retryCount = min(retryCount + 1, 5)
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.enabled else { return }
            if self.browser == nil { self.start() } else { self.connectIfNeeded() }
        }
        retry = work
        DispatchQueue.main.asyncAfter(deadline: .now() + min(20, pow(1.8, Double(retryCount))) + Double.random(in: 0...0.3), execute: work)
    }
    func choose(_ name: String) {
        preferredMac = name; UserDefaults.standard.set(name, forKey: "preferredMac")
        if currentMac != name { link?.cancel() }
        connectIfNeeded()
    }
    func pair(code: String) {
        guard needsPairing, fingerprint.count == 64, code.count == 6 else { return }
        pairingError = nil; pairSubmitted = true
        link?.send(Packet(hello: UIDevice.current.name, command: "pair", protocolVersion: 2, deviceID: deviceID, code: code))
    }
    func forgetDesktop() {
        if let name = currentMac { DesktopKeychain.delete(name) }
        credential = nil; link?.cancel()
    }
    private static func serviceName(_ result: NWBrowser.Result) -> String? {
        if case let .service(name, _, _, _) = result.endpoint { return name }; return nil
    }
    private func connectIfNeeded() {
        guard enabled, link == nil, let results = browser?.browseResults, !results.isEmpty else { return }
        let result = results.first { Self.serviceName($0) == preferredMac } ?? results.sorted { (Self.serviceName($0) ?? "") < (Self.serviceName($1) ?? "") }.first!
        let name = Self.serviceName(result) ?? "Windows"
        currentMac = name; credential = DesktopKeychain.read(name)
        needsPairing = false; fingerprint = ""; pairingError = nil; pairSubmitted = false
        let pin = credential?.fingerprint
        let tls = NWProtocolTLS.Options()
        sec_protocol_options_set_min_tls_protocol_version(tls.securityProtocolOptions, .TLSv12)
        sec_protocol_options_set_verify_block(tls.securityProtocolOptions, { [weak self] _, trust, completion in
            let secTrust = sec_trust_copy_ref(trust).takeRetainedValue()
            guard let certificates = SecTrustCopyCertificateChain(secTrust) as? [SecCertificate], let certificate = certificates.first else { completion(false); return }
            let bytes = SecCertificateCopyData(certificate) as Data
            let candidate = SHA256.hash(data: bytes).map { String(format: "%02X", $0) }.joined()
            let accepted = pin == nil || pin == candidate
            DispatchQueue.main.async {
                self?.fingerprint = candidate
                if !accepted { self?.pairingError = "Desktop certificate changed. Verify Windows and forget the saved desktop before pairing again." }
            }
            completion(accepted) // Unpaired connection has no authority until explicit fingerprint/code approval.
        }, Self.verifyQueue)
        let tcp = NWProtocolTCP.Options(); tcp.noDelay = true; tcp.enableKeepalive = true; tcp.keepaliveIdle = 15
        let parameters = NWParameters(tls: tls, tcp: tcp)
        let connection = LineConnection(NWConnection(to: result.endpoint, using: parameters))
        connection.onState = { [weak self, weak connection] state in
            guard let self, let connection, connection === self.link else { return }
            switch state {
            case .ready:
                self.lastPacket = Date()
                connection.send(Packet(hello: UIDevice.current.name, protocolVersion: 2, deviceID: self.deviceID))
                self.keepalive?.invalidate()
                self.keepalive = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
                    guard let self else { return }
                    if Date().timeIntervalSince(self.lastPacket) > 60 { self.link?.cancel() }
                    else { self.link?.send(Packet(command: "ping", callID: UUID().uuidString)) }
                }
            case .failed, .cancelled:
                if self.connected { self.onDisconnect?() }
                self.connected = false; self.needsPairing = false; self.macName = nil
                self.keepalive?.invalidate(); self.keepalive = nil; self.link = nil; self.scheduleRetry()
            case .waiting: connection.cancel()
            default: break
            }
        }
        connection.onPacket = { [weak self] packet in self?.receive(packet) }
        link = connection; connection.start()
    }
    private func receive(_ packet: Packet) {
        lastPacket = Date()
        if let name = packet.hello { macName = name }
        switch packet.command {
        case "pair_challenge":
            guard packet.protocolVersion == 2, let host = packet.hostID, let nonce = packet.nonce else { link?.cancel(); return }
            pendingHost = host
            if let saved = credential, saved.hostID == host, let secret = Data(base64Encoded: saved.secret) {
                let message = Data("googly-v2\n\(host)\n\(deviceID)\n\(nonce)".utf8)
                let proof = Data(HMAC<SHA256>.authenticationCode(for: message, using: SymmetricKey(data: secret))).base64EncodedString()
                link?.send(Packet(command: "authenticate", protocolVersion: 2, deviceID: deviceID, proof: proof))
            } else { needsPairing = true }
        case "pair_required": needsPairing = true
        case "pair_error": pairingError = packet.text; pairSubmitted = false
        case "pair":
            guard pairSubmitted, let secret = packet.secret, Data(base64Encoded: secret)?.count == 32,
                  let host = packet.hostID, host == pendingHost, let name = currentMac else { link?.cancel(); return }
            let record = DesktopCredential(hostID: host, fingerprint: fingerprint, secret: secret)
            guard DesktopKeychain.save(name, record) else { pairingError = "Couldn't save pairing in Keychain."; link?.cancel(); return }
            credential = record; preferredMac = name; UserDefaults.standard.set(name, forKey: "preferredMac")
        case "paired": connected = true; needsPairing = false; retryCount = 0
        case "ping": link?.send(Packet(command: "pong", callID: packet.callID))
        default: break
        }
        guard connected else { return }
        if let face = packet.face { onFace?(face) }
        onEvent?(packet)
        if let command = packet.command { onCommand?(command) }
    }
    func send(_ packet: Packet) { if connected { link?.send(packet) } }
}

struct DesktopCredential: Codable { let hostID: String; let fingerprint: String; let secret: String }
enum DesktopKeychain {
    private static func query(_ account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "co.visionairy.googly.desktop-v2", kSecAttrAccount as String: account]
    }
    static func read(_ name: String) -> DesktopCredential? {
        guard let data = readData(name) else { return nil }
        return try? JSONDecoder().decode(DesktopCredential.self, from: data)
    }
    private static func readData(_ name: String) -> Data? {
        var q = query(name); q[kSecReturnData as String] = true; q[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?; guard SecItemCopyMatching(q as CFDictionary, &item) == errSecSuccess else { return nil }; return item as? Data
    }
    static func save(_ name: String, _ record: DesktopCredential) -> Bool {
        guard let data = try? JSONEncoder().encode(record) else { return false }; return saveData(name, data)
    }
    private static func saveData(_ name: String, _ data: Data) -> Bool {
        let q = query(name)
        let attributes: [String: Any] = [kSecValueData as String: data, kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        let status = SecItemUpdate(q as CFDictionary, attributes as CFDictionary)
        if status == errSecSuccess { return true }
        if status != errSecItemNotFound { return false }
        return SecItemAdd(q.merging(attributes) { _, new in new } as CFDictionary, nil) == errSecSuccess
    }
    static func delete(_ name: String) { SecItemDelete(query(name) as CFDictionary) }
    static var deviceID: String {
        if let data = readData("device-id"), let id = String(data: data, encoding: .utf8) { return id }
        let id = UUID().uuidString
        // Failure leaves a transient ID, so pairing can't silently claim persistence.
        _ = saveData("device-id", Data(id.utf8)); return id
    }
}
