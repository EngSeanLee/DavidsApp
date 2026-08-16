# backend/apps-script

Google Apps Script backend, managed via [`clasp`](https://github.com/google/clasp). Implements the
JSON envelope contract in [`../../docs/api-contract.md`](../../docs/api-contract.md).

## Status: Phases 1, 3, and 4 complete — all 9 actions live

Bound to a Google Sheet + Apps Script project under `eng.lee785@gmail.com`, deployed as a Web App
(Execute as: Me, Who has access: Anyone), schema bootstrapped, and smoke-tested end-to-end.

- `Router.js`, `Auth.js`, `Sheets.js`, `Logging.js`, `Envelope.js` — routing, auth, Sheets access,
  logging (all done)
- `Projects.js` (`startProject`, `listProjects`), `Findings.js` (`getLastSavedRow`, `saveFinding`,
  `deleteLastFinding`), `Vocabulary.js` (`resolveVocabulary`) — CRUD actions (all done)
- `Report.js`'s `generateReport` — **done**: builds a Google Doc (project header + findings table
  grouped by Room) via `DocumentApp`, exports to PDF via `DriveApp`, sets the PDF to "anyone with
  the link can view" (the MAUI client has no Google auth of its own), returns the Drive URL.
  Verified end-to-end: live curl call against the deployed Web App, PDF opened and visually checked.
- `AiClient.js` + `Findings.js`'s `parseFinding` / `resolveMissingField` — **done**, via Google
  Gemini (not OpenAI — see `docs/decisions/0003-gemini-instead-of-openai.md` for why, and for a
  detailed account of getting a brand-new, very-recently-launched API working: the endpoint shape,
  two real model-quality bugs found and fixed, all with a verified trail). Full extraction,
  shorthand ("same reading 1.2"), missing-field, and unknown-vocabulary paths all verified live
  against the deployed Web App.
- `STATE` vocabulary (`Vocabulary.js`) is seeded with a **placeholder default**
  (`Intact` / `Fair` / `Poor`) — the spec never enumerated exact values; confirm/edit before relying
  on it for real inspections.

Both OAuth consent grants (Drive/Docs for reports, external-request for Gemini) needed granting
beyond Phase 1's original Sheets-only consent — see the version history in **Deploy → Manage
deployments** (currently Version 4).

The live deployment URL and the `SHARED_SECRET` script property value are **not committed** (this repo
is public — see `docs/decisions/0002-auth-and-secrets.md`). Get them from the Apps Script project's
**Deploy → Manage deployments** page and **Project Settings → Script Properties**, signed in as
`eng.lee785@gmail.com`.

## Local setup (already done once; for a second machine/dev)

1. `npm install -g @google/clasp`
2. `clasp login` (interactive, human-only)
3. From this directory: `clasp pull` to sync down the bound project (scriptId is in `.clasp.json`,
   already committed)
4. `clasp push` after any local edit; Web App deployments are **versioned snapshots** — pushing to
   HEAD does not update the live URL's behavior until you also edit the deployment
   (**Deploy → Manage deployments → pencil icon → Version: New version → Deploy**)
5. To bootstrap/re-verify the Sheets schema, run `setupSheets` (in `Sheets.js`) once from the Apps
   Script editor's function picker — it's idempotent, safe to re-run
