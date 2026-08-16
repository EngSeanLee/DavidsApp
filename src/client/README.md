# src/client

.NET MAUI client. Two projects:

- **`DavidsApp.Client.Core`** (`net10.0`, no MAUI dependency) — `Models/`, `Services/Api/`
  (`IApiClient`/`ApiClient`), `Services/StateMachine/` (`CaptureStateMachine`), and the
  `IContinuousSpeechRecognizer` interface. Split out from the MAUI app specifically so it's
  unit-testable without the Android/Windows workloads — see `DavidsApp.Client.Core.Tests`.
- **`DavidsApp.Client`** (`net10.0-android` + `net10.0-windows10.0.19041.0`) — the actual MAUI
  app: UI, DI wiring (`MauiProgram.cs`), and platform-specific speech recognizer implementations
  under `Platforms/`.

## Status: Phase 2 in progress

Done:
- Both projects scaffolded and building clean for both targets (`dotnet build -f net10.0-android`,
  `dotnet build -f net10.0-windows10.0.19041.0`)
- Full data model (`Models/`) matching [`../../docs/api-contract.md`](../../docs/api-contract.md)
- `ApiClient` — posts the `{apiKey, action, payload}` envelope, never throws (network/deserialize
  failures come back as a `Status.Error` envelope, same code path as a real backend error)
- `CaptureStateMachine` implementing [`../../docs/state-machine.md`](../../docs/state-machine.md)
  end to end, including the missing-field routing invariant, Pause/Resume, and SpeechFailed/Retry
  — see `DavidsApp.Client.Core.Tests` (14 tests, including one live against a running
  `tools/mock-api`)
- Wired to `tools/mock-api` by default (`MauiProgram.cs` — see `ApiClientOptions`)

Not yet done:
- UI (still the MAUI template's default `MainPage`) — capture screen, state indicators, project
  select/start
- `Services/Speech` platform implementations (Android `SpeechRecognizer` wrapper, Windows
  `ContinuousRecognitionSession`) — interface exists in Core, nothing implements it yet
- Wiring the real deployed Apps Script URL (currently mock-only) — swap `ApiClientOptions` via a
  gitignored local config once needed; never hardcode the real URL/secret here (repo is public)

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
