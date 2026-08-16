# 0001 — Continuous speech-to-text approach

**Status:** decided

## Context

The app needs always-on-while-foregrounded speech capture, not tap-to-talk. `CommunityToolkit.Maui`'s
`ISpeechToText` (including its Offline variant) is single-utterance only: it starts a recognizer
session and returns one final result, then stops on Android's built-in silence timeout. No first-party
or well-maintained NuGet package does true continuous dictation on Android today.

## Decision

- **Android:** wrap `Android.Speech.SpeechRecognizer` + `RecognitionListener` directly (Android SDK
  bindings, no extra NuGet). On `OnResults` / `OnError` (`ERROR_NO_MATCH`, `ERROR_SPEECH_TIMEOUT`),
  immediately tear down the recognizer instance and start a fresh one — the standard workaround for
  Android's ~5s silence timeout, since there's no public "always listening" API. Tune
  `ExtraSpeechInputCompleteSilenceLengthMillis` / `ExtraSpeechInputPossiblyCompleteSilenceLengthMillis`
  to reduce premature cutoffs. Never reuse a `SpeechRecognizer` across restarts.
- **Windows (dev/test target):** use the genuinely continuous
  `Windows.Media.SpeechRecognition.SpeechRecognizer` + `ContinuousRecognitionSession` — no restart-loop
  hack needed.
- Both sit behind a shared `Services/Speech/IContinuousSpeechRecognizer` interface so the state machine
  and command-word detection are platform-agnostic.
- `CommunityToolkit.Maui` is still added as a dependency, but only for fast prototyping and the manual
  tap-to-talk / keyboard fallback path — not the production always-on loop.

## Consequences

Small gaps (tens–hundreds of ms) between Android recognition sessions are expected and can miss
speech at the seam; repeated restarts may produce audible start/stop chimes unless explicitly muted.
Needs on-device validation (not just emulator) before assuming the restart-loop is acceptable for real
field use — see the build plan's risk list.
