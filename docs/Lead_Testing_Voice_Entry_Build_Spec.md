# Lead Testing Voice Entry — Build Spec (Fresh Start)

## Status of this document

This replaces the prior technical review. The previous MAUI codebase is **not** being carried forward — this is a from-scratch build. The prior review's backend design (Google Apps Script + Google Sheets + OpenAI) and data model are being kept because they were proven to work; only the client application is starting over.

**Assumption made here, flag if wrong:** the client framework is still assumed to be .NET MAUI (native, cross-platform, good speech-API access, matches the primary field device being an Android tablet). "Starting over" is read as "no legacy code, not "abandon MAUI" — if you want a different client framework (e.g. a web app, Flutter, native Android/Kotlin), say so before build work starts, since it changes the whole plan below.

---

## 1. Objective

Build a field-ready, hands-free data-entry workflow for on-site lead testing, so an inspector can capture structured findings while walking a property without touching a device for each entry. Captured findings are stored in a structured datastore and later turned into a formatted report.

Core loop:

**Select/start project → speak finding (hands-free) → parse → resolve missing/unknown values (voice) → confirm → save → continue with context from the last saved row → ... → generate report.**

Two things distinguish this build from a basic dictation app:
1. **Hands-free control.** Not just tap-to-talk — continuous listening with voice-commanded pause/resume, with a physical button (Bluetooth or wired remote) as a fallback control when voice commands aren't reliable enough (loud environments, PPE, gloves, etc.).
2. **Structured output, not a transcript.** Every finding is parsed into a fixed schema (Room, Wall, POS, Color, Substrate, State, Component, Reading) and validated against controlled vocabulary before being saved — not a free-text note.

---

## 2. Architecture

```
MAUI Client (Android primary, Windows for dev/testing)
    UI state, microphone, continuous listening, voice
    command detection, text-to-speech, local workflow
    orchestration, physical-button input
        ↓
Google Apps Script API (doPost router, JSON envelope)
    routing, project operations, finding workflow,
    validation, vocabulary resolution, report generation
        ↓
OpenAI Interpretation
    natural-language extraction, contextual interpretation
    ("same reading 1.2" style shorthand)
        ↓
Google Sheets
    Projects | Findings | Settings | API_Log
```

Principle carried over from the prior build: **the client renders API state and collects the next action; Apps Script stays authoritative for field requirements, normalization, vocabulary, numbering, and save/delete semantics.** The client should not re-implement business rules.

---

## 3. Data Model (proven, reused as-is)

### `Projects`
```
ProjectID | Testing Address | Testing Date | Job Number | Start Time | Stop Time | Created On
```

### `Findings`
```
ProjectID | # | Room | Wall | POS | Color | Substrate | State | Component Details | Reading | Entered On
```
Reading format: `0.0`

### `Settings`
```
Type | Value | Normalized
```
Dynamic categories (user-extensible vocabulary): `ROOM, POS, COLOR, SUBSTRATE, COMPONENT`
Fixed category (closed vocabulary): `STATE`

### `API_Log`
Request/response logging for debugging and diagnostics.

---

## 4. API Actions (proven contract, reused as-is)

```
startProject
listProjects
getLastSavedRow
parseFinding
saveFinding
deleteLastFinding
resolveMissingField
resolveVocabulary
```

**New action to design and add:** `generateReport` (see Section 7).

Response statuses the client must handle: `confirm`, `missing_field`, `unknown_value`, `error`.

---

## 5. Hands-Free Interaction Design (new — core focus of this rebuild)

### 5.1 Primary mode: continuous listening + voice pause/resume

Instead of a tap-to-start/tap-to-stop Talk button, the app listens continuously once a project is active, and recognizes a small set of control phrases spoken by the user, e.g.:

```
"pause" / "hold on"     → stop actively parsing speech into the transcript
"resume" / "go ahead"   → resume capturing
"cancel" / "scratch that" → discard current in-progress utterance
"save" / "confirm"      → trigger save when a pending row is ready
"repeat"                → re-speak the last status/prompt via TTS
```

Open design questions to resolve during build:
- Wake-word vs. always-on-while-app-foregrounded (always-on is simpler but drains battery/data faster and risks capturing ambient noise as a control phrase).
- How to avoid false-positive command detection when a control word appears naturally inside a finding (e.g. someone dictating a note that happens to contain "pause").
- Debounce/confirmation before an accidental "cancel" discards real data.

### 5.2 Fallback: physical remote button

A cheap Bluetooth (or wired, phone-jack style) remote shutter/clicker button — the kind sold for camera shutters or presentation slides — mapped to:
```
single press  → start/stop capture (same as old Talk button)
double press  → save
long press    → cancel/undo
```
This is the resilience layer for noisy environments, PPE, or when voice-command recognition is unreliable. MAUI can read Bluetooth HID button events like a standard keypress (most clickers register as a media-key or keyboard-key press), so this doesn't require custom Bluetooth pairing logic in most cases — needs device-specific verification during build.

### 5.3 Speech reliability hardening (carried over from prior findings, still applies)

- Stop/reset the previous recognition session before starting a new one.
- Reset `CancellationTokenSource` between sessions.
- Treat speech as an optional input channel — manual keyboard entry and the Android keyboard mic remain available fallbacks.
- Log raw recognized text before parsing, separate from parser output, to isolate STT errors from parsing errors.
- Coordinate TTS and STT so the mic never listens while the app is speaking (TTS stop/complete → STT start → STT complete → API processing → TTS response).

---

## 6. UX State Model (carried over — proven design, keep this)

```
Idle → Listening/Parsing → Confirm | Missing Field | Unknown Vocabulary → Saved
                                                                        ↳ Deleted (undo)
```

- **Idle** — project selected, ready to capture.
- **Listening/Parsing** — client sends `projectId`, `transcript`, `lastRow` to `parseFinding`.
- **Confirm** — API returns a complete `pendingRow`; client previews and speaks confirmation; user says/presses save.
- **Missing Field** — API returns the specific missing field and prompt; client routes the *next* speech/button input to `resolveMissingField`, not to a new `parseFinding` call. This routing rule was a hard-learned bug in the prior build and must be preserved.
- **Unknown Vocabulary** — API returns a resolution object; client offers voice yes/no and category selection, submits via `resolveVocabulary`.
- **Saved** — pending row persisted, `_lastSavedRow` updated, state resets to Idle, still attached to active project.
- **Deleted** — last finding removed, previous last-saved-row context reloaded.

Suggested speech-state indicators for the UI: `Ready, Listening, Processing, Needs Response, Ready to Save, Speech Failed`.

---

## 7. Report Generation (new — needs design)

Not built previously. Needs to be designed from scratch. Open questions to resolve before implementation:

- **Trigger:** on-demand per project (button in app / Apps Script menu), automatically when a project is marked "Stop Time" set, or both?
- **Format:** PDF is the most likely target for a field report handed to a client; Google Docs (via Apps Script `DocumentApp`) as an intermediate/editable format is the easiest to generate from Sheets data and can be exported to PDF.
- **Content:** likely a per-project summary — project header (address, job number, date, start/stop time) followed by a findings table (Room, Wall, POS, Color, Substrate, State, Component, Reading), possibly grouped by Room.
- **Where it's generated:** recommend doing this in Apps Script (it already has direct Sheets access and can use `DocumentApp`/`DriveApp` to build and export a PDF), with the MAUI client simply triggering `generateReport` and receiving back a shareable file link — keeps the "client renders, Apps Script owns business logic" principle intact.
- **Delivery:** returned as a Drive link the app can open/share, emailed, or both — needs your input.

This section intentionally has more open questions than answers — it should be scoped in detail (a short follow-up conversation or its own planning pass) before implementation begins.

---

## 8. Recommended Build Sequence

1. **Scaffold the new MAUI client** (clean project, no legacy code) with the proven state model (Section 6) and data contracts (Sections 3–4) wired to the existing Apps Script API.
2. **Implement hands-free capture v1:** continuous listening with a small fixed set of voice commands (Section 5.1), plus raw-STT logging before parsing.
3. **Add the physical remote button fallback** (Section 5.2) and confirm HID button mapping on the target Android tablet.
4. **Harden speech session lifecycle** (Section 5.3) — stop/reset between sessions, TTS/STT coordination, user-friendly failure handling.
5. **Wire up text-to-speech status prompts** end-to-end (confirmations, missing-field prompts, errors), non-blocking.
6. **Add automatic action routing after speech** — idle speech → `parseFinding`; missing-field context → `resolveMissingField` automatically, without a manual mode switch.
7. **Structured diagnostic logging** — timestamp, projectId, raw STT text, API action/status, error category, pending row, last-saved-row ID.
8. **Design and implement report generation** (Section 7) once the open questions above are answered.
9. **End-to-end regression testing** — complete findings, shorthand/"same" findings, missing fields, unknown vocabulary, save, delete, project reselection, app restart, speech failure recovery, report generation.

---

## 9. Open Decisions Needed Before/During Build

- Confirm client framework (MAUI assumed — confirm or override).
- Wake-word vs. always-on continuous listening.
- Exact voice command vocabulary and how to avoid false positives.
- Specific physical button product/model to target for HID mapping testing.
- Report format, trigger, content, and delivery mechanism (Section 7).
- Whether OpenAI usage/cost is a concern at field-testing volume (not raised yet, worth confirming).
