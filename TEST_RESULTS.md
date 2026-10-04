# Test results

Date: 2026-10-04. Initial environment: Linux x86_64, .NET SDK 10.0.401.
Follow-up: actual Windows 11 x64 host, OS build 26100, accessed through WSL.
Upstream baseline: 5aab7ff. Port branch: `port/windows-chatgpt`.

**The full Definition of Done is not yet satisfied.** The initial validation
used Linux. The subsequent installation request enabled native Windows host
validation and the user signed in through the app. No iPhone or Xcode validation
has been performed. Compilation, fixtures, real account inference, real local
audio and full hardware E2E remain separate evidence.

## Initial Linux checks (before the host installation)

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
  controlled fixtures; real account sign-in is separate evidence below.
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
| 1: ChatGPT browser login and plan permission | **Native host passed:** user browser sign-in, verified saved account, completed text/image/function-call probe and continuation. App reports plan usage validated. |
| 2: iPhone discovers/pairs Windows | TLS/pairing loopback passed; **Bonjour, Network.framework and Keychain on actual devices not run** |
| 3: Hebrew `תפתח כרום` opens Chrome | Native code implemented; **spoken Hebrew + real agent/tool/app launch not run** |
| 4: Describe current screen | WGC/UIA/OCR/vision paths compile; **native capture/OCR and real model answer not run** |
| 5: Point and click Settings | Mapping and validation passed; **overlay alignment and actual click not run** |
| 6: Multiple monitors / real DPI | Numerical cases passed; **100/125/150/200% physical monitor checks not run** |
| 7: Toggle iPhone Wi-Fi and reconnect | Real TCP/TLS reconnect harness passed; **Wi-Fi interruption on phone not run** |
| 8: Restart Windows and retain ChatGPT account | **Native app restart retained the real account through DPAPI and revalidated inference.** Real refresh after expiry and Windows reboot remain untested; rotation fixtures passed. |
| 9: Usage limit, clear UI, no API key fallback | Circuit breaker passed; actual WPF status inspected through UI Automation. **Visual usage-limit screen not manually inspected.** |
| 10: Unsupported subscription transcription → local | Official preview declares audio unsupported, so no unsupported audio request is sent. Real Windows local Whisper English execution passed; **Hebrew phone recording not run** |

Also outstanding: actual Xcode compilation/signing, microphone capture on iPhone,
Hebrew/English mixed transcription quality, RTL visual inspection, streamed phone
captions, capture exclusion screenshot inspection, Store-app launch, Unicode
input into Windows apps, UIA provider timeouts, emergency hotkey, elevated-app
refusal, startup/tray lifecycle and web capability tests with an authorized account.

The GitHub Actions workflow was added for Linux tests, Windows build/publish and
unsigned iOS simulator compilation. **Remote CI has not been executed here.**
The Windows archive has now been installed and launched on the native host.
This is still not a claim that all phone/desktop E2E scenarios are working.

## Documented limitations

Subscription audio is disabled according to current official OpenAI documentation;
there is no speculative audio route and no API-key fallback. Local Whisper and
optional Tesseract/model files require setup. The current settings UI handles one
ChatGPT registration and IPv4 LAN discovery. The deterministic risk classifier
is heuristic and cannot prove every sensitive UI workflow safe. Overlay concepts
are ported, but all original Mac effects are not reproduced pixel-for-pixel.
Apple's SDK requirement means building/signing the phone app still requires a
macOS build environment, even though the runtime no longer needs a Mac.

## Native Windows installation and real account follow-up

* Installed at `%LOCALAPPDATA%\Programs\GooglyEyes` without administrator
  execution of the companion. Desktop and Start Menu shortcuts created; startup
  remains off. The actual WPF process is responsive.
* User completed official browser sign-in. The app retained the account through
  DPAPI across process restarts and discovered the real account catalog. No
  account token, password or account email is copied into this report.
* Real subscription `response.completed` checks passed for text, a valid 64×64
  image, a harmless namespace function call and continuation with its output.
  The selected model came from live discovery, not a hardcoded configuration.
  The installed UI reports an active model and `ChatGPT plan usage validated`.
* The live stream completed with an empty envelope output array while emitting
  function calls through `response.output_item.done`. Added a regression test
  preserving function/namespace and encrypted reasoning data until completion.
* Native Schannel initially failed with EphemeralKeySet. Switched Windows imports
  to UserKeySet without PersistKeySet; the existing production TLS/reconnect test
  then passed unchanged. Certificate cleanup is owned by the runtime.
* The final **Windows suite passed 36/36**, including the Windows-only IPv4 mDNS
  bind test and real Windows Whisper CLI. Production OAuth tests still use
  controlled fixtures; the native app sign-in evidence is separate.
* The final **Linux standard suite passed 35/35**; its Windows-only mDNS check is
  a no-op on Linux and is not counted as Linux network evidence.
* Real CPU Whisper used the official b5130 x64 asset, SHA-256
  `f9ec6c52a2e949b62ab51fa21d0d497958f9e41c3010c157c4e42932d5316f3c`,
  verified against GitHub's release asset digest, and multilingual ggml-base.
  The English JFK fixture passed; observed runs took 3,209–8,122 ms, with the
  final run at 8,122 ms. This is not measured phone latency or Hebrew accuracy.
* Actual installed process listeners were verified for TLS TCP and UDP 5353.
  With explicit user confirmation, the Ethernet profile is Private and scoped
  executable firewall rules allow only Private/LocalSubnet inbound traffic.
  **A real iPhone Bonjour discovery/pairing has not yet been performed.**
* Source solution Release build remains at 0 warnings and 0 errors. Diagnostic
  utilities and test binaries stayed outside the tracked repository.

Raw categorical test results are saved locally in `build/native-windows-tests.txt`.
Native capture, OCR, control input, monitor alignment, emergency stop and phone
interaction still need the acceptance checks above.
