# Client UX State Model

Carried over from the prior build's proven design (spec §6) — this is a preserved contract, not open
for casual redesign. `Services/StateMachine/CaptureStateMachine.cs` in the MAUI client implements this
exactly, and should be unit-testable independent of speech/UI.

```
Idle → Listening/Parsing → Confirm | Missing Field | Unknown Vocabulary → Saved
                                                                        ↳ Deleted (undo)
```

## States

- **Idle** — a project is selected/active, ready to capture. Continuous listening is running (see
  `docs/decisions/` for the always-on-while-foregrounded decision) but no utterance is in flight.
- **Listening/Parsing** — client sends `{ projectId, transcript, lastRow }` to `parseFinding`.
- **Confirm** — API returned a complete `pendingRow`; client previews it, speaks a confirmation via
  TTS, and waits for a "save"/"confirm" voice command (or button/tap) to call `saveFinding`.
- **Missing Field** — API returned a specific missing field + prompt (`status: "missing_field"`).
  **Hard rule, preserved from a previously hard-learned bug:** the client's *next* speech/button input
  routes to `resolveMissingField`, carrying `pendingRowSoFar` — never to a fresh `parseFinding` call.
  This can chain (a resolved field may still leave another missing) until the response is `confirm`.
- **Unknown Vocabulary** — API returned `status: "unknown_value"`; client offers a voice yes/no plus
  category confirmation, submits via `resolveVocabulary`, which returns to the in-progress `pendingRow`.
- **Saved** — `saveFinding` succeeded; `_lastSavedRow` updates client-side, state resets to **Idle**,
  still attached to the active project.
- **Deleted** — `deleteLastFinding` succeeded; previous last-saved-row context (`previousLastRow`)
  reloads so shorthand parsing ("same reading 1.2") keeps working against the new most-recent row.

## Speech-state indicators (UI + TTS)

`Ready, Listening, Processing, Needs Response, Ready to Save, Speech Failed` — map each
`CaptureStateMachine` state to one of these for the on-screen indicator; `Needs Response` covers both
Missing Field and Unknown Vocabulary.

## Interruption handling (new — not in the original spec, added during technical review)

Because listening is always-on-while-foregrounded (no wake word, no background service), the OS can
suspend the mic mid-capture: screen lock, an incoming call, the user switching apps. On any such
interruption, the state machine must transition to an explicit **Paused** sub-state (not silently drop
the in-flight utterance or pending row) and resume into whatever state it was in before, once the app
regains focus. `pendingRowSoFar` / in-progress Missing-Field context must survive this transition.

## Voice command routing (spec §5.1)

Control phrases are matched **before** a transcript is treated as finding content, and only against
short utterances (≤4 words, near-exact match) — never substring-contains against arbitrary dictated
text, to avoid a finding that happens to contain the word "pause" mid-sentence triggering the command.

| Phrase | Effect |
|---|---|
| "pause" / "hold on" | stop actively parsing speech into the transcript |
| "resume" / "go ahead" | resume capturing |
| "cancel" / "scratch that" | discard current in-progress utterance (confirm/debounce before discarding a populated `pendingRow`) |
| "save" / "confirm" | trigger `saveFinding` when in **Confirm** |
| "repeat" | re-speak the last TTS status/prompt |

## Speech session hygiene (spec §5.3)

- Stop/reset the previous recognition session before starting a new one; don't reuse a `SpeechRecognizer`
  instance across restarts.
- Reset the cancellation token between sessions.
- TTS and STT never run concurrently: TTS stop/complete → STT start → STT complete → API call →
  TTS response.
- Manual keyboard entry is always available as a fallback path into the same state machine (it doesn't
  bypass Missing Field / Unknown Vocabulary routing).
