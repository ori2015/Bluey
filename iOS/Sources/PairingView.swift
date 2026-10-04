import SwiftUI

/// Shown until the phone finds the Mac: a sleepy blob and how to wake him up.
struct PairingView: View {
    @ObservedObject var link: MacLink
    @State private var code = ""
    @State private var verified = false
    var onPlay: () -> Void

    var body: some View {
        HStack(spacing: 56) {
            SleepyBlob()
                .frame(width: 220, height: 190)

            ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Text("Wake me up from Windows")
                    .font(.fredoka(32))
                    .foregroundStyle(Color(hex: 0xF4F1FA))
                Text("Open Googly Eyes on Windows. Keep both on the same Wi-Fi and I'll find your desktop.")
                    .font(.plexSans(16))
                    .foregroundStyle(Color(hex: Palette.inkSoft))
                    .frame(maxWidth: 380, alignment: .leading)
                if link.macs.count > 1 {
                    Picker("Desktop", selection: Binding(get: { link.currentMac ?? "" }, set: { link.choose($0) })) {
                        ForEach(link.macs, id: \.self) { name in Text(name).tag(name) }
                    }.foregroundStyle(.white)
                }
                if link.needsPairing {
                    Text("On Windows, open iPhone pairing. Compare this fingerprint with the one shown there.")
                        .font(.plexSans(12)).foregroundStyle(.white)
                    Text(link.fingerprint).font(.plexMono(10)).foregroundStyle(Color(hex: Palette.inkSoft))
                        .textSelection(.enabled).lineLimit(3)
                    Toggle("The fingerprints match", isOn: $verified).font(.plexSans(12)).foregroundStyle(.white)
                    HStack {
                        TextField("6-digit pairing code", text: $code).keyboardType(.numberPad)
                            .textFieldStyle(.roundedBorder).frame(width: 190)
                        Button("Pair") { link.pair(code: code) }
                            .disabled(!verified || code.count != 6)
                    }
                }
                if let error = link.pairingError { Text(error).font(.plexSans(12)).foregroundStyle(.pink) }
                HStack(spacing: 10) {
                    ProgressView().tint(Color(hex: Palette.berry1))
                    Text("Looking for your desktop")
                        .font(.plexMono(13))
                        .foregroundStyle(Color(hex: Palette.inkSoft))
                }
                .padding(.top, 4)
                Button(action: onPlay) {
                    Text("Play without the desktop")
                        .font(.plexSans(15).weight(.semibold))
                        .foregroundStyle(Color(hex: 0xF4F1FA))
                        .padding(.horizontal, 18)
                        .frame(minHeight: 44)
                        .background(Color(hex: Palette.panel), in: RoundedRectangle(cornerRadius: 14))
                }
                .buttonStyle(.plain)
            }
            }
            .frame(maxWidth: 420)
        }
        .onChange(of: link.fingerprint) { _, _ in verified = false; code = "" }
        .padding(.horizontal, 48)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.black)
    }
}

private struct SleepyBlob: View {
    var body: some View {
        TimelineView(.animation) { timeline in
            let t = timeline.date.timeIntervalSinceReferenceDate
            ZStack {
                BlobShape()
                    .fill(BlobShape.linear)
                    .overlay(BlobShape().fill(RadialGradient(colors: [.white.opacity(0.5), .white.opacity(0)],
                                                             center: UnitPoint(x: 0.3, y: 0.22), startRadius: 0, endRadius: 80)))
                    .opacity(0.9)
                HStack(spacing: 26) {
                    Capsule().fill(Color(hex: Palette.ink)).frame(width: 46, height: 7)
                    Capsule().fill(Color(hex: Palette.ink)).frame(width: 46, height: 7)
                }
                .offset(y: -18)
                Ellipse().fill(Color(hex: Palette.nose)).frame(width: 18, height: 12).offset(x: 4, y: 21)
                Text("z").font(.fredoka(26)).foregroundStyle(Color(hex: Palette.berry3))
                    .offset(x: 110, y: -95 - 4 * sin(t * 1.5))
                Text("z").font(.fredoka(18)).foregroundStyle(Color(hex: Palette.berry4))
                    .offset(x: 128, y: -122 - 4 * sin(t * 1.5 + 1))
            }
            .scaleEffect(1 + 0.02 * sin(t * 1.1), anchor: .bottom)
        }
    }
}
