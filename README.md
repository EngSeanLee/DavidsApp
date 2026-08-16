# DavidsApp — Lead Testing Voice Entry

Field-ready, hands-free data-entry app for on-site lead testing. An inspector captures structured
findings by voice while walking a property; findings are parsed into a fixed schema, validated
against controlled vocabulary, saved, and later turned into a report.

Full design spec: [`docs/Lead_Testing_Voice_Entry_Build_Spec.md`](docs/Lead_Testing_Voice_Entry_Build_Spec.md)

## Architecture

```
MAUI Client (Android primary, Windows for dev/testing)
    ↓
Google Apps Script Web App (doPost router, JSON envelope)
    ↓
Google Gemini (natural-language extraction / interpretation)
    ↓
Google Sheets (Projects | Findings | Settings | API_Log)
```

Principle: the client renders API state and collects the next action; Apps Script stays authoritative
for field requirements, normalization, vocabulary, numbering, and save/delete semantics. The client
does not re-implement business rules.

## Repo layout

```
docs/            Shared API contract, state machine spec, and design decisions (ADRs)
backend/apps-script/   Google Apps Script backend (managed via clasp) — deployed
src/client/      .NET MAUI client (Core logic library + MAUI app)
tools/mock-api/  Mock of the doPost JSON contract, for client development without hitting
                 the real backend/quota
```

## Status

All 9 API actions implemented and deployed (Phases 0–4 of the build sequence in the spec):

- **Backend**: schema, CRUD actions, AI-backed `parseFinding`/`resolveMissingField` (Gemini via the
  Interactions API — see `docs/decisions/0003-gemini-instead-of-openai.md`), and `generateReport`
  (Google Doc → PDF via Drive) are all live on the deployed Web App.
- **Client**: MAUI app with the full hands-free capture state machine, speech recognizer
  implementations for Android/Windows (unverified on real hardware — see
  `src/client/README.md`), diagnostic logging, and report generation UI.
- **Deferred**: the physical remote button (spec §5.2) — no hardware chosen yet.

See `backend/apps-script/README.md` and `src/client/README.md` for detailed per-component status,
and `docs/decisions/` for the design decisions and corrections made along the way.

## Setup

- **.NET SDK**: 10.0.400+ with `android`, `maui-windows` workloads (`ios`/`maccatalyst` optional).
- **Node**: for `@google/clasp` (Apps Script CLI) and `tools/mock-api`.
- **Google account**: `eng.lee785@gmail.com` owns the Apps Script project + Sheet. `clasp login` is an
  interactive step only a human can complete.
- **Gemini API key**: required for `parseFinding` / `resolveMissingField`; stored server-side only
  (Apps Script Script Property `GEMINI_API_KEY`), never committed.
- The deployed Web App URL and its shared secret are likewise not committed (public repo) — see
  `docs/decisions/0002-auth-and-secrets.md` for where to find them.
