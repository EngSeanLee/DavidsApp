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
OpenAI (natural-language extraction / interpretation)
    ↓
Google Sheets (Projects | Findings | Settings | API_Log)
```

Principle: the client renders API state and collects the next action; Apps Script stays authoritative
for field requirements, normalization, vocabulary, numbering, and save/delete semantics. The client
does not re-implement business rules.

## Repo layout

```
docs/            Shared API contract, state machine spec, and design decisions (ADRs)
backend/apps-script/   Google Apps Script backend (managed via clasp)
src/client/      .NET MAUI client
tools/mock-api/  Mock of the doPost JSON contract, for client development before the real
                 backend (or an OpenAI key) exists
```

## Status

Early scaffold — see `docs/decisions/` for build sequencing and the current phase. Nothing is
deployed yet.

## Setup (in progress)

- **.NET SDK**: 10.0.400+ with `android`, `maui-windows` workloads (`ios`/`maccatalyst` optional).
- **Node**: for `@google/clasp` (Apps Script CLI) and `tools/mock-api`.
- **Google account**: `eng.lee785@gmail.com` owns the Apps Script project + Sheet. `clasp login` is an
  interactive step only a human can complete.
- **OpenAI API key**: required for `parseFinding` / `resolveMissingField`; stored server-side only
  (Apps Script Script Properties), never committed.
