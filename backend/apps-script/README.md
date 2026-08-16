# backend/apps-script

Google Apps Script backend, managed via [`clasp`](https://github.com/google/clasp). Implements the
JSON envelope contract in [`../../docs/api-contract.md`](../../docs/api-contract.md).

Not yet initialized — Phase 1 of the build plan:

1. `npm install -g @google/clasp`
2. `clasp login` (interactive, human-only — signs in as `eng.lee785@gmail.com`)
3. `clasp create` to bind this directory to a new Apps Script + Sheets project
4. Implement `src/Router.js`, `Auth.js`, `Projects.js`, `Findings.js`, `Vocabulary.js`, `Sheets.js`,
   `Logging.js` (schema + CRUD actions — no OpenAI key required)
5. `src/OpenAiClient.js` and the `parseFinding` / `resolveMissingField` handlers stay stubbed until an
   OpenAI API key exists (Phase 3)
6. `src/Report.js` for `generateReport` (Phase 4)

See `docs/decisions/` for the auth pattern and secret-handling decisions this backend follows.
