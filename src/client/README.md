# src/client

.NET MAUI client. Two projects:

- **`DavidsApp.Client.Core`** (`net10.0`, no MAUI dependency) — `Models/`, `Services/Api/`
  (`IApiClient`/`ApiClient`), `Services/StateMachine/` (`CaptureStateMachine`,
  `SpeechStateIndicator`), `Services/Speech/` (`IContinuousSpeechRecognizer`,
  `ITextToSpeechService`, `CommandWordDetector`), and `ViewModels/`
  (`ProjectListViewModel`, `CaptureViewModel`). Split out from the MAUI app specifically so it's
  unit-testable without the Android/Windows workloads — see `DavidsApp.Client.Core.Tests`.
- **`DavidsApp.Client`** (`net10.0-android` + `net10.0-windows10.0.19041.0`) — the MAUI app:
  `Views/` (XAML pages), `MauiProgram.cs` (DI wiring, incl. platform-conditional speech
  recognizer registration), and `Platforms/{Android,Windows}/Speech/` (the two
  `IContinuousSpeechRecognizer` implementations).

## Status: Phase 2 complete + speech hardening (build plan steps 4, 7)

- Both projects build clean for both targets, zero warnings
  (`dotnet build -f net10.0-android`, `dotnet build -f net10.0-windows10.0.19041.0`)
- Full data model, `ApiClient`, and `CaptureStateMachine` — see prior status notes; 34 tests in
  `DavidsApp.Client.Core.Tests` (33 unit + 1 live mock-server integration), all passing
- **`ProjectListPage`** (start/resume a project) → **`CapturePage`** (status indicator, pending-row
  preview, save/cancel/delete-last/repeat, mic toggle, and a manual-entry fallback that routes
  through the exact same `CaptureViewModel` pipeline as real speech)
- `CaptureViewModel` wires `CaptureStateMachine` + `IContinuousSpeechRecognizer` +
  `ITextToSpeechService` + `CommandWordDetector` together: command-phrase detection before
  content routing, TTS/STT mute coordination (spec §5.3 — mic never listens while the app talks),
  a two-step cancel debounce, and yes/no parsing for the unknown-vocabulary flow
- **Actually run and driven end-to-end on Windows** (via UI Automation, not just built) — full
  Idle → Confirm → Saved → Idle cycle through the real UI, real `ApiClient`, and `tools/mock-api`,
  confirmed by screenshot at each step
- Android/Windows `IContinuousSpeechRecognizer` implementations exist and compile — see the
  caveat below
- Wired to `tools/mock-api` by default (`MauiProgram.cs` — see `ApiClientOptions`)

### A crash found and fixed during this pass

Running the app for real (not just building it) surfaced a genuine bug: `CapturePage.OnAppearing`
is `async void` (an unavoidable MAUI lifecycle constraint), and it called the Windows speech
recognizer's startup unguarded. On this dev machine, Windows speech recognition isn't fully set up
(no enrolled recognition language), so `StartListeningAsync` threw — and an unhandled exception in
an `async void` chain is fatal to the *entire process*, not just that screen. Fixed by wrapping the
recognizer startup in `CaptureViewModel.InitializeAsync` in a try/catch that degrades to
manual-entry-only instead of crashing, plus defense-in-depth try/catch around the `async void`
lifecycle methods themselves in `CapturePage`/`ProjectListPage`. Confirmed fixed by re-running the
same UI Automation sequence that originally crashed it.

### Known caveat: speech recognizers are unverified on-device

`Platforms/Android/Speech/AndroidContinuousSpeechRecognizer.cs` and
`Platforms/Windows/Speech/WindowsContinuousSpeechRecognizer.cs` compile clean but **the actual
continuous-listening behavior has not been verified** — no Android emulator/device and no
configured Windows speech recognition were available in the environment this was built in (the
crash above is direct evidence of the latter). Per
[`../../docs/decisions/0001-continuous-stt-approach.md`](../../docs/decisions/0001-continuous-stt-approach.md),
expect this to need real on-device iteration, especially the Android restart-on-silence-timeout
loop.

### Speech hardening + diagnostic logging (added after the initial UI pass)

- **`IDiagnosticLog`** (Core) / **`FileDiagnosticLog`** (MAUI, NDJSON to `FileSystem.AppDataDirectory`)
  — persists timestamp/projectId/action/status/errorCode/pendingRow/lastSavedRow per build plan
  step 7. Raw STT transcripts are logged as their own `raw_stt` event, separate from the parsed
  outcome (spec §5.3) — confirmed on a real run: manual entry correctly does *not* produce a
  `raw_stt` entry, since that event is specifically about speech-recognized text.
- **Session hygiene fixes on both recognizers** (spec §5.3 "stop/reset the previous recognition
  session before starting a new one"): Android now detaches its `RecognitionListener` *before*
  stopping/cancelling/destroying the old session (previously did it after — a queued callback
  could otherwise fire against a session already being discarded and spawn a duplicate restart);
  Windows now flips `IsListening` to false before awaiting `StopAsync()` rather than after, so the
  `Completed` handler's restart guard can't race a call already in flight.
- 6 new `CaptureViewModel`-level tests (`FakeSpeechRecognizer`/`FakeTextToSpeechService`/
  `FakeDiagnosticLog`), including a direct regression guard for the async-void crash described
  above, plus coverage for diagnostic logging, voice-command interception, the manual/voice shared
  routing pipeline, and the cancel debounce. 39 tests total.
- Re-ran the full Windows UI Automation sequence after these changes and confirmed the diagnostic
  log file's actual on-disk contents matched expectations.

### Report generation UI (Phase 4, client side)

- `CaptureViewModel.GenerateReportCommand` calls `generateReport` and opens the returned link via
  a new `IUrlLauncher` interface (Core) / `MauiUrlLauncher` (MAUI, wraps `Launcher.Default`) — same
  MAUI-free-Core pattern as `ITextToSpeechService`.
- "Generate Report" button on `CapturePage`, disabled with a "Generating…" label while in flight.
- 2 new tests (open-on-success, no-open-on-failure); 41 tests total.
- Verified live against a running app: button click → real diagnostic log entry appended to disk
  for the `generateReport` call, confirming the full path fired without crashing.

## Not yet done

- Physical remote button (deferred per earlier decision)
- Wiring the real deployed Apps Script URL (currently mock-only) — swap `ApiClientOptions` via a
  gitignored local config once needed; never hardcode the real URL/secret here (repo is public)
- End-to-end regression testing across the full scenario list in the build plan (build plan step 9)

## Build & test

```
# Core logic — fast, no workload/emulator needed
dotnet test src/client/DavidsApp.Client.Core.Tests

# Client, either target
dotnet build src/client/DavidsApp.Client.csproj -f net10.0-windows10.0.19041.0
dotnet build src/client/DavidsApp.Client.csproj -f net10.0-android
```

Run the mock API first (`node tools/mock-api/server.js`) to exercise the client against something
live, or to run `ApiClientMockServerTests` (skips cleanly if nothing's listening).

**Note:** `CommunityToolkit.Maui` is pinned to `13.0.0`, not latest — newer versions need
`Microsoft.Maui.Controls >= 10.0.60`, but the `maui-windows` workload installed here is at
`10.0.20`. Revisit after a `dotnet workload update`.
