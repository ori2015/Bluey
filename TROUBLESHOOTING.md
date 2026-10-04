# Troubleshooting

## Phone doesn't find Windows

Use the same private IPv4 LAN, allow local network access in iOS Settings, and
allow the Windows executable on Private networks in Firewall. Guest Wi-Fi/client
isolation and VPNs can block UDP 5353 multicast. The companion advertises the
original `_googly._tcp` service and a dynamic TLS TCP port. IPv6-only discovery is
not implemented in this version. Quit duplicate instances and reopen the app.

## Certificate changed / pairing code rejected

Never approve a changed certificate based on a Bonjour name. Compare the full
fingerprint on the physical Windows panel and phone. If you intentionally cleared
Windows app data/reinstalled on another machine, forget the saved desktop on the
phone and generate a new code on Windows. Codes expire after two minutes and
stop after five attempts. There is no plaintext or unpaired computer-control mode.

## ChatGPT shows Connected but AI isn't ready

Identity and plan access are separate. Reconnect and grant ChatGPT plan usage.
The issued account/workspace and eligible plan determine availability. Select and
validate a discovered model; completed text, vision and function-call requests
are required. Account registration is retained after sign-out, and Reconnect
requires the original account identity. Adding multiple ChatGPT accounts in one
installation is not exposed by the current UI.

If the browser callback fails, check that `127.0.0.1` loopback is permitted.
Do not replace it with localhost or copy tokens into files. If initial code
exchange fails, reconnect reuses the saved issued registration.

## Usage or subscription errors

| Error | Recovery |
| --- | --- |
| subscription_sharing_usage_limit_exceeded | New plan requests pause. Open Manage usage; choose Resume after resolving the limit. No inferred reset time. |
| subscription_sharing_user_not_eligible | Account/workspace/policy isn't eligible; don't repeatedly sign in. |
| subscription_sharing_usage_unavailable / user_unavailable | Keep credentials; retry later. Transient admission retries are bounded. |
| subscription_sharing_unsupported_capability | Disable/remove the specific unsupported capability; web search is independently optional. |
| subscription_sharing_route_not_supported | Verify the public Responses route; never use private endpoints or audio routes. |
| subscription_sharing_invalid_user / terminal refresh failure | Reconnect the saved account. |
| chatpass_v2_scope_not_authorized / invalid_authorization_context | Reconnect and check the grant/client context. |
| 401 | Sign in again; no billing fallback. |
| 403 with detail only | Admission/policy restriction; model probes won't bypass it. |
| 5xx/network | Preserve credentials; bounded admission backoff. A partial stream is not replayed. |

Sign-out reports if remote revocation couldn't be confirmed; disconnect the app
in ChatGPT Settings. No credentials are sent in diagnostics.

## Speech / Hebrew

Subscription transcription isn't supported by the documented preview. Configure
whisper-cli.exe and a multilingual ggml model in Local AI; keep required backend
DLLs next to it. Do not choose an `.en` model for Hebrew. If a GPU build fails,
select a CPU build. Try a larger multilingual model for mixed Hebrew/English.
The app records only while held, not throughout the awake session.

A brief hold during the first microphone permission prompt is discarded on
release; hold again after granting permission. Empty recordings and holds over
60 seconds do not generate an unbounded request. Wi-Fi loss cancels a pending turn.

## OCR / clicks / elevated applications

Missing Hebrew Windows OCR needs Tesseract with eng+heb data. Check the OCR status
included in screen observation; no paid OCR fallback exists. Capture/OCR run only
on demand. If IDs expired, the agent must call look_at_screen again.

Verify DPI on each monitor, including monitors above/left of primary. Target
positions, capture-exclusion, WGC interop and UI Automation need actual Windows
E2E checks; a successful cross-build is not evidence of these runtime features.

UIPI blocks control of elevated apps/UAC. Open the application normally or do the
action yourself. Do not run the whole companion as Administrator. Password fields
are deliberately refused. Ctrl+Alt+S stops requests and disables computer control;
re-enable it explicitly in Privacy settings.

## Logs and data

Open logs from Settings → Privacy. They contain categorical codes and development
latencies, not audio, screenshots, OAuth tokens or form content. Saved conversation
history is separate, local and optional on both Windows and iPhone. Disabling
Windows history stops future writes; Delete saved Windows history clears existing
files. Disabling iPhone history removes its persisted transcript file.

## Build problems

Use .NET 10, not .NET 8/9. The Windows target restores its Windows desktop/WinRT
reference packs even on Linux. The test harness is run with `dotnet run`, not
`dotnet test`. If MSBuild worker nodes fail in a constrained environment, build
with `-m:1 -nr:false`. iOS compile/signing needs Xcode; the static port check does
not replace it. A signing team is intentionally not checked into the project.
