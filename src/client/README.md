# src/client

.NET MAUI client (`net10.0-android`, `net10.0-windows10.0.19041.0`).

Not yet scaffolded — Phase 2 of the build plan:

1. `dotnet new maui -n DavidsApp.Client -o .`
2. Add `Models/` (`Finding`, `Project`, `ApiEnvelope`, `ApiStatus`) matching
   [`../../docs/api-contract.md`](../../docs/api-contract.md)
3. `Services/Api/{IApiClient,ApiClient}.cs` — point at `../../tools/mock-api` during development, swap
   to the real Apps Script deployment URL once Phase 1 is live (config-driven, not hardcoded)
4. `Services/StateMachine/CaptureStateMachine.cs` implementing
   [`../../docs/state-machine.md`](../../docs/state-machine.md) — unit-test the missing-field routing
   rule independent of speech/UI
5. `Services/Speech/IContinuousSpeechRecognizer.cs` + platform implementations — see
   `docs/decisions/0001-continuous-stt-approach.md`
