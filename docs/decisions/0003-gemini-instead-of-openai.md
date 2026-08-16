# 0003 — Google Gemini instead of OpenAI, via the Interactions API

**Status:** decided (Phase 3 implementation)

## Context

The original build spec assumed OpenAI for `parseFinding`/`resolveMissingField`'s natural-language
extraction throughout (architecture diagram, `OpenAiClient.js` naming, Section 9's "whether OpenAI
usage/cost is a concern"). The user supplied a Google AI Studio (Gemini) API key instead when Phase
3 started. This ADR documents the substitution and everything discovered getting it working, since
most of it isn't something a training-cutoff model would already know.

## Decision

Use Google Gemini, called from `backend/apps-script/src/AiClient.js`, with the key stored as the
`GEMINI_API_KEY` script property (same pattern as `SHARED_SECRET` — never in source, this repo is
public).

## What we learned getting there (August 2026)

**Google GA'd a new "Interactions API" in June 2026** and is retiring the older `generateContent`
REST endpoint for new accounts — confirmed via a live 404 from the actual key in use:
`"This model models/gemini-2.5-flash is no longer available to new users... use the Interactions
API."` `generateContent` itself still works for existing integrations, but a brand-new API key on a
brand-new account gets routed to Interactions-only for at least some models.

**Confirmed request/response shape** (verified against live raw HTTP responses, not just docs —
web-search/fetch summaries of Google's own migration docs disagreed with each other on details):

- Endpoint: `POST https://generativelanguage.googleapis.com/v1beta/interactions`
- Auth: `x-goog-api-key` header (not a `?key=` query param, unlike the old endpoint)
- Request: `{ model, input, generation_config: { thinking_level }, response_format: { type: "text", mime_type: "application/json", schema } }`
- Response: `{ id, status, usage, steps: [...], object: "interaction", model }` — `steps` is a
  chronological array. A `thought` step's `signature` field is an opaque signed blob (not
  human-readable reasoning text — that's by design). The actual output is in a `model_output` step:
  `{ type: "model_output", content: [{ type: "text", text: "<the JSON string>" }] }`.
- `AiClient.js`'s `extractInteractionsText_` implements exactly this, with a fallback to the legacy
  `candidates[]` shape in case a future key/account ever routes there instead.

**Two real quality problems, both fixed, both worth knowing about if this breaks again:**

1. **Thinking token exhaustion.** `gemini-3.6-flash` defaults to heavy "thinking" — observed 146 of
   164 total tokens spent thinking on a *"say hello in one word"* test prompt. For the full
   finding-extraction prompt this was starving the actual output, producing garbled/truncated
   results (verbose reasoning-style prose leaking into a field value, most fields missing). Fixed
   by setting `generation_config.thinking_level: "low"`. (Google's docs confirm `thinking_level` for
   `gemini-3-flash-preview`; it worked for `gemini-3.6-flash` too, though that exact model wasn't
   listed.)
2. **The model "satisficing" on an incomplete object.** With no `required` array on the JSON
   schema, and a prompt saying "leave a field out if not determinable," the model returned `{room,
   wall}` only — and the raw response showed `status: "completed"`, not a truncation/error status.
   It genuinely considered a 2-field object a valid complete response. **Fixed by marking every
   schema property `required`** (forces constrained decoding to populate all of them) **and
   changing the prompt to say `""` for undeterminable fields instead of omitting them** — omission
   isn't valid under a `required` schema anyway. This mattered far more than the thinking-level fix;
   after both fixes, a full 8-field transcript reliably extracts all 8 fields correctly, and
   shorthand ("same reading 1.2" inheriting all other fields from the previous finding) works too.

**Design principle preserved from the rest of the backend:** Gemini's only job is extraction
(including resolving shorthand against context it's given). It does not decide what's missing or
what's unknown vocabulary — the already-existing, already-tested `validateFindingRow_` does that
deterministically against the real Settings sheet. This kept the fix cycle above fast (nothing to
re-derive in the model's behavior once extraction was reliable) and means model quirks can't cause
a vocabulary rule to silently drift.

## Consequences

- This is bleeding-edge (the Interactions API and `gemini-3.6-flash` are both very recent as of
  this writing) — expect the exact request/response shape to need revisiting if Google changes it
  again. `extractInteractionsText_`'s doc comment and this ADR are the trail to follow.
- `gemini-2.5-flash` is not available to this account via either endpoint — don't reintroduce it as
  a fallback without checking first.
- Model name, thinking level, and the required-fields schema trick are all in one place
  (`AiClient.js`) specifically so a future model swap only touches one file.
