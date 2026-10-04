# Bluey: iPhone + Windows port

Status: port implementation and automated checks delivered; physical Windows/iPhone and real-account acceptance remain outstanding.
Audit baseline: `rbrown101010/bluey-by-riley`, commit `5aab7ff`.
Documentation checked: 2026-10-04.

## Existing repository audit

The repository contains 5,320 lines of Swift, an iOS Xcode project, a macOS Swift
package, bundled fonts and visual previews. There is no Windows implementation.

* `FaceView.swift` uses Canvas, springs, gaze, blinks, brows, idle hops and moods.
  `BlobShape.swift`, `Palette.swift`, fonts, MiniBluey and chirp synthesis are
  reusable. Keep their art and animation implementation.
* `GooglyApp.swift` binds double tap and a 300 ms hold to `LiveVoice`; retain the
  gestures and state names. Its token/tool forwarding must move to Windows.
* `LiveVoice.swift` opens a Realtime WebSocket, streams microphone PCM even
  outside a hold, consumes Realtime events and forwards tool calls to Mac.
  Replace its networking and continuous recording with bounded PTT WAV capture.
* `MacLink.swift` discovers `_googly._tcp`, retries connections and correlates
  replies. Retain the internal name initially to limit UI churn; implement TLS,
  persistent pairing, protocol v2, streamed events and keepalive.
* `BlueyAppView.swift`, `SoundButton.swift`, `PairingView.swift` need desktop
  labels, real pairing inputs, captions and privacy settings. `SessionStore.swift`
  can keep its transcript model, with optional persistence and file protection.
* `Shared/GooglyLink.swift` defines optional Packet fields, FaceState, moods and
  newline TCP framing. Extend rather than replace it. Limit frame sizes.
* `RealtimeHost.swift` owns personality, schemas, tool queue, snapshots, pointer
  choreography and captions. Preserve concepts in C#; move the AI loop here.
* `ScreenReader.swift` combines ScreenCaptureKit screenshots, Vision line/word
  boxes, accessibility controls and target IDs. Reimplement on demand on Windows.
* `ControlsReader.swift` walks the front app's AX tree and guards secure fields.
  Replace with bounded UI Automation traversal; never collect password values.
* `ComputerControl.swift` uses CGEvent, Carbon and NSWorkspace. Replace input and
  app discovery. Keep cmd-to-ctrl aliases where appropriate.
* `CursorOverlay.swift` contains curved flights, teardrop art, eyes, comet/string
  trails, press effects, cloud captions and gaze mapping. Port the visual concepts
  to WPF with per-monitor overlays and capture exclusion.
* `PhoneServer.swift` currently accepts every device without authentication.
  Windows v2 requires encrypted transport and paired-device authentication.
* `AppDelegate.swift` owns menus, hotkeys, launch, status and permissions.
  Replace with WPF settings, tray, emergency stop and opt-in user startup.
* `WebResearch.swift` makes a separate API-key request with a fixed model.
  Replace with account/model capability probing through subscription Responses.
* Remaining Mac files: Settings persists preferences; HotKeys wraps Carbon;
  Fonts registers bundled fonts; ReportPanel displays research cards; Keychain
  misleadingly stores plaintext API keys. These stay legacy-only, outside the
  Windows/default iPhone path. Do not migrate their keys.

## Verified OpenAI contract and changes to requested design

Sources:

* https://developers.openai.com/siwc/token-sharing-open-source/sign-in
* https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions
* https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference
* https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations
* https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery
* https://developers.openai.com/api/docs/guides/function-calling

The official preview explicitly excludes audio/video input, transcription and
Files upload. Therefore do **not** probe a known-unsupported audio body or invent
Responses audio fields. Use multilingual local whisper.cpp immediately and show
`Local Whisper` in settings. Record subscription transcription as unsupported by
the documented preview, not as a tested account inference. A future documented
capability change requires updating this plan and implementing a real probe.

Discovery is `GET /v1/models` with the OAuth bearer, reading `models`, filtering
`visibility == "list"`, showing `display_name` and using `slug`. A model entry
does not prove vision/tool access: probe text, vision and namespace function calls
and track observed capabilities. Do not infer capabilities from a model's name.

Use public `POST /v1/responses`, array input, `store:false`, `stream:true`,
instructions, and namespaced function tools. Omit system items,
previous_response_id, temperature, max_output_tokens and other excluded fields.
Retain returned output items (including encrypted reasoning when returned) in
local current-session context. Success requires response.completed; a truncated
or failed stream is an error. Web search is separately tested and disabled on
unsupported/policy errors. No API-key provider or billing fallback exists.

OAuth uses dynamic registration only initially, stable host UUID, agent name,
127.0.0.1 loopback, PKCE S256, state and nonce; issued client ID is persisted
before exchange. Validate ID token signature using discovered JWKS, issuer,
audience, expiry, nonce and account identity. DPAPI protects token records.
Serialize refresh and atomically replace the entire rotating token set.

## Target layout and native API mapping

`Windows/GooglyWindows.sln` contains a portable .NET 10 core, a .NET 10 WPF host
and executable automated tests. The core holds protocol, OAuth primitives,
Responses parsing/context, coordinate mapping, tool schemas and risk policy;
the host holds Auth, Networking, Screen, Automation, Overlay, Audio, Security,
Settings, Models and Logging. Credentials never leave the host.

| macOS facility | Windows replacement |
| --- | --- |
| ScreenCaptureKit | Windows.Graphics.Capture, monitor interop, D3D11 |
| Vision OCR | Windows.Media.Ocr; optional local Tesseract eng+heb |
| AXUIElement | System.Windows.Automation |
| CGEvent/warp | SendInput / SetCursorPos |
| Carbon key codes/hotkeys | Win32 virtual keys / RegisterHotKey |
| NSWorkspace | Start Menu shortcuts, App Paths, AppsFolder, running windows |
| NSWindow/CALayer | transparent no-activate WPF windows, animated drawing |
| Bonjour NWListener | DNS-SD/mDNS advertisement + TcpListener/SslStream |
| local keys.json | DPAPI CurrentUser encrypted atomic records |
| login item | HKCU Run value, off by default |

All screen targets use physical virtual-desktop coordinates. One CoordinateMapper
handles monitor-local pixels, per-monitor DPI/DIPs, overlays and normalized 0–1000
AI coordinates. Screenshots contain the same virtual desktop bounds as targets.

Pairing uses TLS with a stable self-signed host certificate; the user compares a
certificate fingerprint on Windows and iPhone before submitting a short-lived
code. Paired secrets use DPAPI and Keychain; reconnect verifies the pinned
certificate and a fresh HMAC challenge. Bonjour names convey no trust.

## Risks and validation boundaries

* This development host is Linux: Windows APIs and iOS frameworks cannot run
  here. Cross-build Windows when supported and run portable tests here. iOS
  signing/build needs Xcode on macOS or a macOS build runner; running the product
  needs only Windows and iPhone. Do not claim physical E2E validation.
* Real OAuth consent, eligibility, inference, language quality and web policy
  require the user's account. Never use the development account's credentials.
* mDNS may be blocked by network isolation/firewall; provide explicit private
  network instructions. TLS pin comparison is essential at first pairing.
* UI Automation providers may stall; bound traversal and report timeouts. UIPI
  prevents ordinary processes controlling elevated apps; never bypass UAC.
* OCR language packs vary. Offer configured local Tesseract when English/Hebrew
  WinRT engines are missing. whisper.cpp runtime and multilingual model must be
  installed/configured; do not pretend transcription is ready before that.
* Heuristic risk classification is conservative but cannot understand every UI;
  guard commits/destructive shortcuts, refuse secure fields, surface the exact
  pending action for human approval and include an emergency stop.
* Exclude every own UI window with WDA_EXCLUDEFROMCAPTURE and require hardware
  screenshot inspection. DRM/secure desktop surfaces may remain unavailable.

## Work sequence

1. Commit audit and this plan before implementation.
2. Portable core, WPF solution, protocol fixtures and framing tests.
3. TLS server, mDNS, pairing persistence, keepalive and reconnect.
4. iPhone PTT/transport wiring retaining face art and gestures.
5. OAuth, protected records, OIDC validation and synchronized refresh.
6. Responses SSE, discovery/capability checks and bounded tool loop.
7. Local Whisper WAV conversion and configurable model/backend.
8. Event-driven capture/UIA/OCR and shared coordinates.
9. Input/app tools, runtime validation, approval and emergency stop.
10. WPF pointer/captions, tray/settings/history/startup.
11. Compile, automated unit/integration checks, security review and fixes.
12. BUILD, ARCHITECTURE, TEST_RESULTS, TROUBLESHOOTING; list every hardware
    acceptance case with its actual status. Commit logical changes locally.

## Implementation outcome

The modular .NET 10 WPF solution, protected OAuth, real TLS server/pairing,
IPv4 mDNS, Responses SSE/function loop, model probes, local Whisper adapter,
Windows.Graphics.Capture, UIA/OCR, computer tools, risk confirmation, overlays,
tray/settings, history, startup and iPhone PTT wiring are implemented. Original
FaceView.swift, BlobShape.swift, Palette.swift and Mac sources are unchanged.

Deliberate scope details: internal MacLink/LiveVoice view names remain compatible;
the recorder is PushToTalkController.swift. Initial pairing uses code plus full
fingerprint comparison, rather than a QR camera flow. Discovery currently covers
IPv4 interfaces. Local Whisper/Tesseract runtimes and models are selected by the
user; they are not downloaded automatically. The current UI exposes one ChatGPT
registration at a time. Overlay visuals preserve the palette/eyes/curved flights
and captions, but aren't a pixel-for-pixel port of all 1,100 lines of Mac effects.
Capture fails closed if any own window cannot request capture exclusion.

A Release cross-build and self-contained win-x64 publish succeeded. Portable
unit/integration tests include actual host OAuth/TLS source and a real local
Whisper audio run. They do not establish the full Definition of Done: see
TEST_RESULTS.md for the physical/account checks still outstanding. CI workflow
files were added but remote CI has not been invoked from this environment.
