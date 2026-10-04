# Test results

Date: 2026-10-04. Environment: Linux x86_64, .NET SDK 10.0.401.
Upstream baseline: 5aab7ff. Port branch: `port/windows-chatgpt`.

**The full Definition of Done is not yet satisfied.** No Windows desktop,
iPhone, Xcode installation or user-authorized ChatGPT account was available in
this workspace. The following distinctions are intentional: compilation,
fixture-based integration, real local audio execution and hardware E2E are
separate evidence.

## Executed checks

| Check | Actual result |
| --- | --- |
| .NET 10 Release solution cross-build | Passed: 0 warnings, 0 errors |
| Self-contained win-x64 publish | Passed; GooglyWindows.exe and runtime dependencies produced |
| Automated standard suite | 33 tests; no OpenAI network inference |
| Suite with real local Whisper audio enabled | **34/34 passed**, including the optional real audio test |
| Real whisper.cpp build | v1.9.4, commit 927cfce34f31707e17f2bff35c349632fb9e2c3a, CPU build completed |
| Real local transcription | Multilingual ggml-base, upstream JFK English WAV converted to PCM16 mono 24 kHz; companion resampled and invoked the real CLI; expected word `country` found |
| Local audio end-to-end latency | 2,633 ms in the final recorded test (Linux CPU, sample audio; not an iPhone latency or Hebrew accuracy measurement) |
| Swift syntax parsing | All 11 iOS/Shared Swift source files parsed with tree-sitter-swift |
| Static project/protocol checks | Xcode source references, Swift/C# packet fields, Bonjour declaration and absence of an iPhone OpenAI client passed |
| Shared Xcode scheme XML | Parsed successfully |
| Original character preservation | FaceView.swift, BlobShape.swift and Palette.swift unchanged against baseline |
| Legacy Mac preservation | Mac/ unchanged against baseline |
| git diff --check | Passed |

Commands used:

```bash
dotnet build Windows/GooglyWindows.sln -c Release --nologo -m:1 -nr:false
dotnet publish Windows/GooglyWindows -c Release -r win-x64 --self-contained true -o /tmp/bluey-windows-publish --nologo -m:1 -nr:false
python3 scripts/check-port.py
BLUEY_WHISPER_EXE=/tmp/bluey-whisper/build/bin/whisper-cli \
BLUEY_WHISPER_MODEL=/tmp/bluey-whisper-base.bin \
BLUEY_WHISPER_WAV=/tmp/bluey-jfk-24k.wav \
dotnet run --project Windows/GooglyWindows.Tests -c Release --no-build
```

The local SDK executable was `/tmp/bluey-dotnet/dotnet`. The fixture audio/model,
SDK and third-party build stayed outside source control. Reproduce the optional
Whisper test with those three environment variables and any 24 kHz mono PCM16
English JFK fixture. Production has no test HTTP, token or credential provider.

## Automated coverage

* PKCE RFC 7636 vector, random verifier format, missing/mismatched state and
  issued-client binding.
* One rotating refresh across 40 concurrent callers; failed storage never
  exposes a replacement; cancellation after refresh still commits rotation.
* Actual production OAuth source: loopback callback, dynamic registration,
  exact resource/redirect/PKCE, persisted issued ID before exchange, discovered
  JWKS validation and restart followed by one refresh across 20 callers.
* Actual production OAuth source rejects invalid nonce, signature, issuer,
  audience and expired ID token. Test identities/tokens and HTTPS metadata are
  controlled fixtures; no real account sign-in was performed.
* Packet casing, optional legacy fields, Hebrew roundtrip; fragmented/coalesced
  NDJSON, truncated packets and hostile oversized unterminated frames.
* Host/device/nonce HMAC binding; actual production TLS server on a loopback TCP
  socket rejects unpaired voice, pairs a test device, pings and authenticates
  after reconnect. Production pairing code attempt limit and restart persistence
  contract are tested with an explicitly in-memory test storage double.
* Negative virtual desktop and DPI 100%, 125%, 150%, 200% coordinate roundtrips.
  These are numerical tests, not physical clicks on scaled monitors.
* Runtime schemas reject wrong types, out-of-range positions, unknown arguments,
  unsafe URL schemes and arbitrary executable paths. Keyboard aliases/F keys,
  target lookup and risk checks include English/Hebrew labels.
* SSE text streaming, completion required, incomplete/failed/disconnected stream
  rejection, subscription usage-limit circuit breaker and exact HTTP request
  constraints. Function loop passes complete outputs and image context before
  the final completed answer, using test-only AI streams/executor.
* Production redaction patterns; WAV format/chunk validation and resampling.

## Physical/account acceptance checklist

| Requested test | Status and remaining evidence |
| --- | --- |
| 1: ChatGPT browser login and plan permission | OAuth harness passed; **real account login and inference not run** |
| 2: iPhone discovers/pairs Windows | TLS/pairing loopback passed; **Bonjour, Network.framework and Keychain on actual devices not run** |
| 3: Hebrew `תפתח כרום` opens Chrome | Native code implemented; **spoken Hebrew + real agent/tool/app launch not run** |
| 4: Describe current screen | WGC/UIA/OCR/vision paths compile; **native capture/OCR and real model answer not run** |
| 5: Point and click Settings | Mapping and validation passed; **overlay alignment and actual click not run** |
| 6: Multiple monitors / real DPI | Numerical cases passed; **100/125/150/200% physical monitor checks not run** |
| 7: Toggle iPhone Wi-Fi and reconnect | Real TCP/TLS reconnect harness passed; **Wi-Fi interruption on phone not run** |
| 8: Restart Windows and retain ChatGPT account | Auth restart/refresh harness passed; **DPAPI on Windows and real rotating token not run** |
| 9: Usage limit, clear UI, no API key fallback | Circuit breaker passed; **WPF visual UI inspection not run** |
| 10: Unsupported subscription transcription → local | Official preview declares audio unsupported, so no unsupported audio request is sent. Local Whisper real English execution passed; **Windows local engine + Hebrew recording not run** |

Also outstanding: actual Xcode compilation/signing, microphone capture on iPhone,
Hebrew/English mixed transcription quality, RTL visual inspection, streamed phone
captions, capture exclusion screenshot inspection, Store-app launch, Unicode
input into Windows apps, UIA provider timeouts, emergency hotkey, elevated-app
refusal, startup/tray lifecycle and web capability tests with an authorized account.

The GitHub Actions workflow was added for Linux tests, Windows build/publish and
unsigned iOS simulator compilation. **Remote CI has not been executed here.**
The Windows archive is a compiled deliverable awaiting Windows runtime acceptance,
not a claim that all E2E scenarios are already working.

## Documented limitations

Subscription audio is disabled according to current official OpenAI documentation;
there is no speculative audio route and no API-key fallback. Local Whisper and
optional Tesseract/model files require setup. The current settings UI handles one
ChatGPT registration and IPv4 LAN discovery. The deterministic risk classifier
is heuristic and cannot prove every sensitive UI workflow safe. Overlay concepts
are ported, but all original Mac effects are not reproduced pixel-for-pixel.
Apple's SDK requirement means building/signing the phone app still requires a
macOS build environment, even though the runtime no longer needs a Mac.
