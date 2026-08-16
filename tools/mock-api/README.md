# tools/mock-api

A mock of the `doPost` JSON envelope contract (see
[`../../docs/api-contract.md`](../../docs/api-contract.md)), so the MAUI client can be built and
tested before the real Apps Script deployment — or an OpenAI key — exists.

Not yet implemented — needed by Phase 2 of the build plan. Should return canned `confirm` /
`missing_field` / `unknown_value` responses for `parseFinding`/`resolveMissingField` so the client's
state machine (`docs/state-machine.md`) can be exercised end-to-end, including the multi-step
missing-field chain.
