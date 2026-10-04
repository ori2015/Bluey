# Build and run Googly Eyes on Windows

This port builds for Windows 11 x64 and iOS 17+. It is awaiting physical-device
acceptance, listed in TEST_RESULTS.md. Running it needs Windows and iPhone only.
Building/signing the native iPhone app still requires Xcode on a Mac or a macOS
build runner. Windows cannot compile an iOS binary with Apple's SDK; the included
GitHub Actions job builds an unsigned simulator target, not an installable phone app.

## 1. Install Windows prerequisites

* Windows 11, an ordinary user account, and access to a private Wi-Fi network.
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
* Git. Visual Studio with .NET desktop development is optional for editing WPF.
* For local transcription: Visual Studio Build Tools with **Desktop development
  with C++**, CMake, and a multilingual whisper.cpp model. The WPF project itself
  does not require C++ or administrator privileges.
* An eligible ChatGPT account with plan usage permission. Availability and limits
  are controlled by OpenAI; sign-in/discovery alone are not proof of inference.

## 2. Build the Windows app

From PowerShell in this repository:

```powershell
dotnet --version
dotnet restore Windows/GooglyWindows.sln
dotnet build Windows/GooglyWindows.sln -c Release
dotnet run --project Windows/GooglyWindows.Tests -c Release
# Optional distributable folder, with .NET runtime included:
dotnet publish Windows/GooglyWindows -c Release -r win-x64 --self-contained true -o artifacts/windows
.\artifacts\windows\GooglyWindows.exe
```

For development, `dotnet run --project Windows/GooglyWindows` launches it directly.
The tray icon opens the panel. Closing the panel keeps it running; Quit in the
tray exits. Ctrl+Alt+S cancels pending work and disables computer control.

A self-contained x64 publish was produced during development on Linux, but its
Windows executable was not run here. CI has Windows build and macOS iOS build jobs.

## 3. Configure local transcription (required by the current OpenAI preview)

The official ChatGPT plan preview excludes audio input and transcription routes.
This app intentionally sends **no** audio request to OpenAI. Speech stays local
until the resulting transcript enters Responses.

Build the tested whisper.cpp release in a **Developer PowerShell for VS**:

```powershell
git clone --branch v1.9.4 --depth 1 https://github.com/ggml-org/whisper.cpp.git Windows/runtime/whisper.cpp
cmake -S Windows/runtime/whisper.cpp -B Windows/runtime/whisper.cpp/build -A x64 -DWHISPER_BUILD_TESTS=OFF
cmake --build Windows/runtime/whisper.cpp/build --config Release --target whisper-cli
New-Item -ItemType Directory -Force Windows/runtime/models
Invoke-WebRequest 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin' -OutFile Windows/runtime/models/ggml-base.bin
```

In Settings → Local AI select:

* `Windows/runtime/whisper.cpp/build/bin/Release/whisper-cli.exe`
* `Windows/runtime/models/ggml-base.bin`

CMake generator layouts can vary; locate `whisper-cli.exe` under the build folder
if it is not in `bin/Release`. Keep the generated DLLs next to that executable.
Use a multilingual model without `.en`. Base is a small starting model; select a
larger multilingual model for Hebrew accuracy if the computer is fast enough.
No audio/model download occurs automatically in the companion.
A prebuilt self-contained companion archive is available locally at
`build/GooglyEyes-Windows-x64.zip`; it does not bundle Whisper/Tesseract runtimes or models.

For an NVIDIA GPU, install a compatible CUDA toolkit and configure a separate
build with `-DGGML_CUDA=ON`. For Vulkan, install the Vulkan SDK and configure with
`-DGGML_VULKAN=ON`. Select that build's executable in settings. whisper.cpp handles
its compiled backend; a CPU build always remains usable. See the upstream
[whisper.cpp build instructions](https://github.com/ggml-org/whisper.cpp).

## 4. Enable English and Hebrew OCR

The companion tries Windows OCR engines for `en-US` and `he-IL`. The availability
of the latter is OS-dependent. If either is missing, use local Tesseract with
both `eng.traineddata` and `heb.traineddata` in its `tessdata` directory, and select
`tesseract.exe` in Settings → Local AI. It runs with `-l eng+heb` and never sends
screenshots to an OCR service. Obtain trained data from the official
[Tesseract tessdata repository](https://github.com/tesseract-ocr/tessdata).
The observation reports which OCR source/language packs were available.

## 5. Build and install the iPhone app

On a Mac with Xcode and Apple's iOS SDK:

1. Clone/copy this repository, open `GooglyEyes.xcodeproj`.
2. Select the GooglyEyes target and your development team in Signing & Capabilities.
   The upstream developer's team was removed. Use a unique bundle identifier if
   necessary. The deployment target is iOS 17.
3. Attach your iPhone, enable Developer Mode if Xcode requests it, select it as
   the run destination and build/run. Allow microphone and local network access.
4. Keep the phone in landscape under the Windows monitor.

The project already includes PushToTalkController.swift. If using XcodeGen,
`xcodegen generate` reads project.yml and regenerates the same source layout.
For an unsigned simulator check:

```bash
xcodebuild -project GooglyEyes.xcodeproj -scheme GooglyEyes -sdk iphonesimulator -configuration Debug CODE_SIGNING_ALLOWED=NO build
```

## 6. Sign in with ChatGPT

Open Windows Settings → ChatGPT → Continue with ChatGPT. The system browser opens
OpenAI's authorization page. Choose the account/workspace, grant ChatGPT plan
usage and return to the app. The callback uses a dynamic `127.0.0.1` port.

Windows validates the ID token and granted scopes, discovers the account's model
catalog, then tests actual completed text, image and function-call inference.
Those small tests consume the connected plan allowance. Connected plus a
validated active model enables the agent. If needed choose another model and
click Validate selected model. Test web research is separate and optional.
Manage usage opens `https://chatgpt.com/settings/usage`.

Credentials are DPAPI-encrypted in `%LOCALAPPDATA%\GooglyEyes`. No API key is
requested. Reconnect keeps the issued client registration and host ID. Sign out
tries remote revocation before clearing tokens locally.

## 7. Pair the iPhone

1. Set Windows' network profile to Private. Keep phone and PC on the same LAN;
   disable Wi-Fi client isolation. Allow GooglyWindows.exe in Windows Firewall
   **on private networks** when Windows requests it. Discovery uses multicast
   UDP 5353; the TLS server binds a dynamically chosen TCP port.
2. Open the phone app. It discovers `_googly._tcp.local` over IPv4 automatically.
3. On Windows Settings → iPhone, generate a pairing code. Compare the **entire
   SHA-256 certificate fingerprint** shown on Windows and iPhone. Bonjour names
   and advertised metadata are not proof of identity.
4. On iPhone check The fingerprints match, enter the six-digit code, and Pair.
   The code expires in two minutes and accepts five attempts maximum.
5. The phone becomes Connected. Secret/certificate pins persist in Keychain;
   Windows persists paired secrets and the host certificate with DPAPI.

Restart/reconnect uses TLS certificate pinning and a fresh HMAC challenge. It
never transfers ChatGPT tokens. Forget paired devices and Forget this desktop
remove the respective saved pairing records; pair again afterward.

## 8. Run and verify

Double tap Bluey to wake. Hold for at least 300 ms, speak, release. The microphone
records only during the hold, capped at 60 seconds. WAV is mono PCM16 24 kHz; the
host resamples to 16 kHz for whisper.cpp. A temporary recording is removed after
processing. Only completed assistant answers enter the phone conversation history.

Test in order:

1. Say `תפתח Chrome` and verify Chrome opens through a function call.
2. Say `מה יש לי עכשיו על המסך?` and inspect screenshot/controls/OCR/vision results.
3. Ask to click Settings; check pointer position and the actual control clicked.
4. Repeat at 100%, 125%, 150%, 200% DPI and with a monitor left/above the primary.
5. Say `תפתח Chrome ותיכנס ל-YouTube` and inspect mixed Hebrew/English captions.
6. Request a sensitive action; verify it waits for Allow once on Windows.
7. Toggle Wi-Fi, restart both apps, lock/unlock the phone, and verify reconnection
   without a new pairing and refresh without entering an API key.
8. Run the automated suite for simulated usage-limit and failed-stream cases.

Development timings go to categorical JSONL logs; production omits Debug timings.
The iPhone's debug log measures actual PTT release-to-transcript. Windows logs
request-received-to-transcript, transcript-to-first-delta, tool execution and
last-tool-finished-to-response-resumed. See TEST_RESULTS.md for what has really
been run and what still requires hardware/account access.
