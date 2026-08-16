# API Contract — Apps Script Backend

Single source of truth for the JSON envelope both the real Apps Script backend
(`backend/apps-script/`) and the mock (`tools/mock-api/`) implement, and that the MAUI client
(`src/client/`) codes against. If this file and the code disagree, this file wins until both are
updated together.

Status: **draft — implement against this before wiring the real deployment.**

## Transport

- Single endpoint: the Apps Script Web App's deployment URL, `POST` only (`doPost(e)`).
- Content-Type: `application/json`. Every request is one JSON object; every response is one JSON object.
- **Correction to the original spec:** Apps Script's `doPost(e)` does not expose custom HTTP request
  headers — only `e.parameter`, `e.postData.contents`, and `e.queryString` are available. Auth
  therefore cannot be a header. See Auth below.

## Auth

Every request body includes a top-level `apiKey` field carrying the shared-secret value:

```json
{ "apiKey": "<shared-secret>", "action": "...", "payload": { ... } }
```

`Auth.js` checks this against `PropertiesService.getScriptProperties().getProperty('SHARED_SECRET')`
before dispatch. A missing/incorrect key short-circuits to `status: "error"` with `errorCode:
"unauthorized"` — no action runs, nothing is logged to `API_Log` beyond the failed attempt itself
(never log the raw key value).

The deployment URL + this secret together are a bearer credential. Neither is committed to the repo;
both live in gitignored local config on the client and in Script Properties on the backend. See
`docs/decisions/` for the full auth-pattern rationale.

## Request envelope

```json
{
  "apiKey": "<shared-secret>",
  "action": "<one of the actions below>",
  "payload": { }
}
```

## Response envelope

```json
{
  "status": "confirm | missing_field | unknown_value | error",
  "data": { },
  "message": "human-readable, safe to speak via TTS or show in UI",
  "errorCode": "present only when status is error"
}
```

- `confirm` — the action completed and/or produced a complete result ready for the user to act on
  (e.g. a fully-parsed pending finding, ready to save).
- `missing_field` — a required field could not be determined; `data.field` names it and `message` is
  the prompt to speak/show. The client's **next** input must route to `resolveMissingField`, not a
  fresh `parseFinding` — see `docs/state-machine.md`.
- `unknown_value` — a value was recognized but isn't in the controlled vocabulary yet; `data` carries
  enough for the client to offer a yes/no + category resolution flow via `resolveVocabulary`.
- `error` — anything else (auth failure, validation failure, upstream Gemini failure, etc.);
  `errorCode` is a short machine-readable slug, `message` is user-facing.

## Actions

### `startProject`
**payload →** `{ testingAddress, testingDate, jobNumber }`
**data ←** `{ projectId, testingAddress, testingDate, jobNumber, startTime, createdOn }`
Creates a new `Projects` row. `startTime` is set server-side to now.

### `listProjects`
**payload →** `{}`
**data ←** `{ projects: [ { projectId, testingAddress, testingDate, jobNumber, startTime, stopTime, createdOn } ] }`

### `getLastSavedRow`
**payload →** `{ projectId }`
**data ←** `{ lastRow: { ...Findings row... } | null }`
Used to seed shorthand-parsing context ("same reading 1.2") and to restore state after app restart /
project reselection.

### `parseFinding`
**payload →** `{ projectId, transcript, lastRow }`
**data ←** (on `confirm`) `{ pendingRow: { room, wall, pos, color, substrate, state, component, reading } }`
(on `missing_field`) `{ field, pendingRowSoFar }`
(on `unknown_value`) see Unknown Vocabulary shape below
Calls `AiClient.js` (Google Gemini, via the Interactions API — see
`docs/decisions/0003-gemini-instead-of-openai.md`). Returns `status: "error", errorCode:
"not_configured"` if `GEMINI_API_KEY` isn't set.

### `resolveMissingField`
**payload →** `{ projectId, field, value, pendingRowSoFar }`
**data ←** same shape as `parseFinding`'s response (may itself return another `missing_field` if more
than one field is missing — client keeps routing here until `confirm`).
Also Gemini-backed, same as `parseFinding`.

### `resolveVocabulary`
**payload →** `{ projectId, category, rawValue, accepted, normalizedValue?, pendingRowSoFar }`
`category` is one of `ROOM | POS | COLOR | SUBSTRATE | COMPONENT` (dynamic) — `STATE` is closed and
never reaches this action; it maps 1:1 to the matching `pendingRow` field (`room`, `pos`, `color`,
`substrate`, `component`). If `accepted` is true, appends a new `Settings` row
(`Type=category, Value=rawValue, Normalized=normalizedValue`) so it's recognized going forward, and
sets that field on `pendingRowSoFar` to the normalized value; if `accepted` is false, the field is
left unset and `message` prompts for a replacement value.
**data ←** `{ pendingRow: { ... } }` — the finding continues from where it left off (client passes
`pendingRowSoFar` back in on the next `parseFinding`/`resolveMissingField` call the same way it does
today, per `docs/state-machine.md`).
Does not require Gemini — pure Sheets read/write.

### `saveFinding`
**payload →** `{ projectId, row: { room, wall, pos, color, substrate, state, component, reading } }`
**data ←** `{ savedRow: { ...Findings row incl. #, projectId, enteredOn... } }`
Appends to `Findings`, assigns the next `#` for that `ProjectID`, updates `_lastSavedRow`. Wrapped in
`LockService.getScriptLock()`.

### `deleteLastFinding`
**payload →** `{ projectId }`
**data ←** `{ deletedRow, previousLastRow }`
Removes the most recent `Findings` row for the project; `previousLastRow` lets the client restore
shorthand context. Wrapped in `LockService.getScriptLock()`.

### `generateReport`
**payload →** `{ projectId }`
**data ←** `{ reportUrl }` — a Drive-shareable PDF link.
Builds a Google Doc from the `Projects` header row + `Findings` rows (grouped by Room), exports to
PDF via `DriveApp`. Independent of the Gemini key.

## Error codes (non-exhaustive, extend as needed)

`unauthorized`, `validation_failed`, `not_configured` (`GEMINI_API_KEY` missing), `upstream_error`
(Gemini/UrlFetchApp), `not_found` (unknown projectId), `internal_error`.

## Data model reference

See `docs/Lead_Testing_Voice_Entry_Build_Spec.md` §3 for the authoritative Sheets schema
(`Projects`, `Findings`, `Settings`, `API_Log`). Column names above use `camelCase` in the JSON
envelope; `Sheets.js` is responsible for mapping to/from the Sheet's header row names.
