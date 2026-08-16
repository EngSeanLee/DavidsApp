# tools/mock-api

A mock of the `doPost` JSON envelope contract (see
[`../../docs/api-contract.md`](../../docs/api-contract.md)), so the MAUI client can be built and
tested before the real Apps Script deployment — or an OpenAI key — exist.

## Status: done

Zero npm dependencies — `node server.js` (or `npm start`) and it's running on
`http://localhost:4127/` (port deliberately not 3000/8080 — picked to avoid colliding with other
local dev servers). In-memory only; state resets on restart.

All 9 actions are implemented. `parseFinding` is canned rather than real NLP — the transcript text
itself selects the response, so the client's full state machine can be exercised on demand:

| Transcript contains | Response |
|---|---|
| `"missing"` | `missing_field` (field: `reading`) |
| `"unknown"` | `unknown_value` (category: `ROOM`, rawValue: `Attic`) |
| `"error"` | `error` (errorCode: `mock_error`) |
| anything else | `confirm`, with a canned complete finding |

Auth: checks `apiKey` against `dev-mock-key` (override via `MOCK_API_KEY` env var), matching
`ApiClientOptions`'s default in the client.

## Run it

```
node server.js
# or: npm start
```
