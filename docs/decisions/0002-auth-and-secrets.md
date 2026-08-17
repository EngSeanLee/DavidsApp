# 0002 — Apps Script auth pattern and secret handling

**Status:** decided

## Context

The MAUI client needs to call a personal Apps Script Web App without a full interactive Google OAuth
sign-in on-device (bad UX for field use), and without standing up service-account infrastructure for
a single-inspector tool.

## Decision

- Deploy the Web App as **Execute as: Me, Who has access: Anyone**. In `appsscript.json`, that's
  `"webapp": {"executeAs": "USER_DEPLOYING", "access": "ANYONE_ANONYMOUS"}` — **not** `"ANYONE"`,
  which despite the name still requires a signed-in Google session and returns 401 to a plain
  client. This bit us for real: `clasp deploy` against a `--deploymentId` originally created via the
  Apps Script UI silently dropped the deployment's Web App entry point entirely (confirmed via the
  Apps Script API — `deployments.get` came back with no `entryPoints` at all), because those
  settings live in the manifest for API-driven deploys, not carried over from whatever the UI had
  configured. The live app was down (generic Drive "unable to open the file" page, i.e. no route at
  all) for the several minutes it took to notice, add the `webapp` block above, and redeploy.
  **Any future `clasp deploy` must keep this block in `appsscript.json`** — without it, the next
  redeploy silently un-publishes the Web App the same way.
- Every request body carries a shared-secret value in a top-level `apiKey` field (see
  `docs/api-contract.md`). **Not a header** — Apps Script's `doPost(e)` does not expose custom HTTP
  headers, only `e.parameter` / `e.postData.contents` / `e.queryString`, so the original spec's
  "header/param" phrasing is resolved as body-field.
- `Auth.js` checks `apiKey` against `PropertiesService.getScriptProperties().getProperty('SHARED_SECRET')`
  before any action dispatches.
- The Gemini key and the shared secret are both stored only in Apps Script Script Properties
  (server-side) and, on the client, in a gitignored local config file — never in source control.

## Consequences

- The deployment URL + shared secret together function as a bearer credential: anyone with both can
  call the API. There is no per-caller identity and no built-in rate limiting beyond Apps Script's own
  quotas — acceptable for a single-inspector field tool, but worth restating since the repo itself is
  public.
- Rotating the secret means redeploying a new Web App version via `clasp deploy`.
- `API_Log` must never record the raw `apiKey` value, even on auth failure.
