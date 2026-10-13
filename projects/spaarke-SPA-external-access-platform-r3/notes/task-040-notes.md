# Task 040 — Workforce client switch (FR-16): notes

**Date:** 2026-10-10 · **Rigor:** FULL (auth) · **Status at hand-off:** code done; **blocked on the new client registration** (owner live steps below). Not committed (wave rule).

Binding rule: ADR-028 Amendment A8 (owner-approved 2026-10-10) — one single-tenant Spaarke public client in Spaarke's tenant, authority `https://login.microsoftonline.com/{Spaarke tenant id}`, Teams = NAA then MSAL popup, no `webApplicationInfo`, no Teams-SSO fallback, scope `api://{customer BFF app id}/user_impersonation`.

## 1. What changed (src/client/external-spa)

| File | Change |
|---|---|
| `src/config.ts` | `getWorkforceEnvConfig()` now returns `{ clientId, tenantId, bffScope }`. New key `VITE_TEAMS_MSAL_TENANT_ID`. Fails loud on a missing/placeholder value, a non-GUID tenant (`organizations`, `common`, a domain), or a scope that is not `api://{id}/user_impersonation`. New helpers `isTenantId`, `isUserImpersonationScope`. |
| `src/auth/msal-config.ts` | Kind `workforce-multitenant` → `workforce`. `workforceAuthorityConfig({ clientId, tenantId, bffScope })` builds `https://login.microsoftonline.com/{tenantId}`; the `/organizations` default and the authority override are gone. `buildMsalConfiguration` refuses a workforce authority that is not `https://login.microsoftonline.com/{GUID}` and a workforce scope other than `…/user_impersonation` (defence in depth). New `popupRedirectUri()` = `{origin}/auth-callback.html`, and an optional per-instance `redirectUri`. CIAM path unchanged (same config object, default redirect = origin). |
| `src/auth/msal-auth.ts` | Teams: NAA first (unchanged ladder: silent → ssoSilent → brokered popup on interaction-required), then a **popup fallback** on a standard `PublicClientApplication` mirroring `OfficeNaaStrategy._tryAcquireWith` (silent with cached account → `acquireTokenPopup` with `loginHint` / `prompt: 'select_account'`). Both Teams instances use redirect `{origin}/auth-callback.html`. `acquireBffTokenViaTeamsSso` and the `authentication` import are deleted. `TeamsWorkforceAuthError.cause` is now `{ naaError, popupError }`. Once the popup path has signed the user in, later calls go to that instance's silent cache instead of re-failing NAA on every BFF call. New `getTeamsWorkforceSignedInInstance()`. |
| `src/auth/standalone-plane.ts` | Browser "My organization" passes `tenantId` (redirect flow, redirect = origin). |
| `src/host/TeamsHostAdapter.ts` | Hands `MsalProvider` the instance that actually signed the user in (NAA or popup fallback) so `useMsal().accounts[0]` (App.tsx portal user) is populated either way. |
| `appPackage/manifest.json` | `webApplicationInfo` removed. |
| `appPackage/env.dev.json` | `AAD_APP_ACCESS_AS_USER_SCOPE` → `AAD_APP_BFF_USER_IMPERSONATION_SCOPE`; `AAD_APP_TENANT_ID` added; `$comment` marks the client id INTERIM. (Toolkit env file; not packaged, no consumer in the repo.) |
| `.env.example` / `.env.development` | Workforce keys documented: client id, `VITE_TEAMS_MSAL_TENANT_ID`, `…/user_impersonation` scope. Dev: tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2` (config/environments.json dev.tenantId), scope `api://1e40baad-…/user_impersonation` (dev BFF app's exposed scope, environments.json), client id **INTERIM `1e40baad-…`** (clearly marked; replace after owner step 1). |
| `public/auth-callback.html` (new) | Popup redirect page (see §2). |
| `staticwebapp.config.json` | `/auth-callback.html` added to `navigationFallback.exclude`. |
| `tests/workforce-auth.test.ts` (new) | 37 tests (§4). |

## 2. Deviation: auth-callback.html is not a plain copy of the add-in page

The add-in's page has no script because the add-in runs **MSAL v3**, whose opener polls the popup's same-origin URL. The external SPA runs **@azure/msal-browser 5.5.0**: `PopupClient` and `SilentIframeClient` wait for the redirect page to post the response over a `BroadcastChannel` (`BrowserUtils.waitForBridgeResponse`; the page must call `broadcastResponseToMainFrame()` from `@azure/msal-browser/redirect-bridge`). A literal copy would make every popup time out (60 s) — the fallback would never work.

So the page inlines MSAL's own redirect bridge **verbatim** (`node_modules/@azure/msal-browser/lib/redirect-bridge/msal-redirect-bridge.min.js`, 6.3 KB, v5.5.0) between `<!-- msal-redirect-bridge:begin/end -->` markers and calls `msalRedirectBridge.broadcastResponseToMainFrame()`. It creates no `PublicClientApplication` and holds no tokens. Why inline: Vite copies `public/` unbundled and the build is a single IIFE entry, so the page cannot `import` the module; a second public file was outside the task's file list. **Drift guard:** a test fails when the inlined copy differs from the installed bridge (e.g. after an msal-browser upgrade) — re-copy the file between the markers.

Also: the add-in page's hard-coded colours were replaced by CSS system colours (`Canvas`/`CanvasText`, `color-scheme: light dark`) for ADR-021 (no hard-coded colours, dark mode). A static page cannot use Fluent tokens.

The SWA CSP sets only `frame-ancestors`, so the inline script is allowed. The ArchTest `ExternalSpaGridViewSelectorGuardTests` checks inline code only in `index.html`; the bridge cannot import the grid (no bundling).

## 3. Decisions

- **Env key names kept** (`VITE_TEAMS_MSAL_*`) — already wired in CI and `.env.production`; the browser workforce plane reads them too. New key: `VITE_TEAMS_MSAL_TENANT_ID`.
- **Tenant must be a GUID** (not a verified domain) — simplest unambiguous pin; refuses every multi-tenant alias.
- **NAA instance redirect** changed from origin to `{origin}/auth-callback.html`. NAA requests do not carry a redirect URI (checked `NestedAppAuthAdapter`); it matters only when the host offers no NAA bridge and `createNestablePublicClientApplication` returns a standard instance — then its silent iframe and popup land on the bridge page instead of timing out.
- **Browser plane keeps the redirect flow** (redirect = origin), unchanged apart from the tenant pin.
- **CIAM unchanged**: `ciamAuthorityConfig`, the `msalInstance` singleton and `acquireBffToken` untouched; CIAM's scope stays `SDAP.Access` (A8's "user_impersonation on both planes" for CIAM is not this task — the POML says the CIAM plane is unchanged; task 041/T240d).

## 4. Tests and checks (run 2026-10-10, src/client/external-spa)

- **Fail-before** (original 10 files restored from HEAD, new test file kept): `npx vitest run tests/workforce-auth.test.ts` → **33 failed / 4 passed**. The 4 that pass before are regression guards for unchanged behaviour (CIAM config ×2, missing client id refused, fail-loud outside Teams).
- **Pass-after:** `npx vitest run tests/workforce-auth.test.ts` → **37 passed**.
- `npm run typecheck` → exit 0. `npm run lint` → exit 0, no warnings.
- `npx vitest run` (full) → **7 files, 118 passed** (baseline before the task: 6 files, 81 passed).
- `npm run build` → exit 0; `dist/auth-callback.html` emitted, byte-identical to `public/`.
- `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~ExternalSpaGridViewSelectorGuardTests|FullyQualifiedName~ClientUploadRouteAgreementTests|FullyQualifiedName~ClientDocumentPointerWriteGuardTests|FullyQualifiedName~RouteAuthorizationGuardTests"` → **372 passed**; `ExternalSpaGridViewSelectorGuardTests` alone → **220 passed**. (No ArchTest scans external-spa auth/Bearer usage.)
- `grep -rn access_as_user src/client/external-spa` (excl. node_modules/dist) → no matches. The test file assembles the string so it does not match either.

Test cases: tenant-pinned authority built; tenants `organizations`/`common`/`consumers`/empty/domain refused; hand-made workforce authorities `/organizations`, `/common`, `/consumers`, `/{tid}/v2.0`, other host, `http:` refused; non-`user_impersonation` scopes refused; old `workforce-multitenant` kind refused; env config read + 6 refusals; browser plane authority/redirect/login scope + `organizations` refused; Teams NAA success (authority, client id, redirect, no popup instance); NAA failure → popup (order, config, `select_account`); NAA creation failure → popup; popup path reused silently; both fail → `TeamsWorkforceAuthError{naaError,popupError}`; outside Teams fails loud; adapter hands the popup instance to `MsalProvider`; every recorded token request has exactly one scope ending `/user_impersonation`; `getAuthToken` never called (mock present); CIAM config identical to before; source scan: no `getAuthToken(`/Teams `authentication` import; no retired scope anywhere; manifest has no `webApplicationInfo`; SWA excludes `/auth-callback.html`; bridge verbatim + no MSAL instance in the page.

## 5. Owner live steps (NOT run) — with rollback

Spaarke tenant: `a221a95e-6abc-4434-aecc-e48338a1b2f2`. Dev BFF app: `1e40baad-e065-4aea-a8d4-4b7ab273458c` (SDAP-BFF-SPE-API). Dev SWA: `green-dune-0c4f1221e.7.azurestaticapps.net`. Run in Spaarke's tenant (`az login --tenant a221a95e-…`). Save every "before" JSON to a file first — it is the rollback input.

**Step 0 — read current state (read-only):**
```
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "{id:id, scopes:api.oauth2PermissionScopes[].{v:value,id:id}, preAuth:api.preAuthorizedApplications, spa:spa.redirectUris, audience:signInAudience}" -o json > before-1e40baad.json
```
Note `<UI_SCOPE_ID>` = the id of `user_impersonation`, `<AAU_SCOPE_ID>` = the id of the A2-era Teams scope, `<BFF_OBJECT_ID>` = `id`.

**Step 1 — create the single-tenant public client "Spaarke Workspace" (Spaarke tenant):**
```
az ad app create --display-name "Spaarke Workspace" --sign-in-audience AzureADMyOrg
az ad sp create --id <NEW_CLIENT_ID>
```
SPA redirect URIs (Graph PATCH on the new app's object id `<NEW_OBJECT_ID>`):
```
az rest --method PATCH --url https://graph.microsoft.com/v1.0/applications/<NEW_OBJECT_ID> --headers Content-Type=application/json --body "{\"spa\":{\"redirectUris\":[\"https://external.spaarke.com\",\"https://external.spaarke.com/auth-callback.html\",\"brk-multihub://external.spaarke.com\",\"https://green-dune-0c4f1221e.7.azurestaticapps.net\",\"https://green-dune-0c4f1221e.7.azurestaticapps.net/auth-callback.html\",\"brk-multihub://green-dune-0c4f1221e.7.azurestaticapps.net\",\"http://localhost:3000\",\"http://localhost:3000/auth-callback.html\"]}}"
```
- Origins: browser redirect flow. `/auth-callback.html`: Teams popup fallback. `brk-multihub://{host}`: Teams NAA (teams-app-r1 lesson: AADSTS700046 without it). Whether the per-broker `brk-1fec8e78-…://` / `brk-5e3ce6c0-…://` URIs are also needed is part of the FR-21 live check (task 071).
- Ids are not secrets: record `<NEW_CLIENT_ID>` here once created.
- **Rollback:** `az ad app delete --id <NEW_CLIENT_ID>` (soft-deleted for 30 days; `az ad app restore`-able via Graph `directory/deletedItems`). Nothing else depends on it until step 4.

**Step 2 — dev BFF app (`1e40baad`) pre-authorizations: ADD on `user_impersonation`** (additive first; the deployed SWA and the current Teams package still request the A2-era scope until step 4):
- pre-authorize `<NEW_CLIENT_ID>` (the H3 `PreAuthorizedClientAppIds` pattern — no consent prompt);
- pre-authorize Teams desktop/mobile `1fec8e78-bce4-4aaf-ab1b-5451cc387264`, Teams web `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` and, if still wanted, the Microsoft Authentication Broker `29d9ed98-a469-4536-ade2-f981bc1d605e` (teams-app-r1 change #4, flagged for review there) on `<UI_SCOPE_ID>` — i.e. move them from `<AAU_SCOPE_ID>` by adding `<UI_SCOPE_ID>` to each entry's `delegatedPermissionIds`.
```
az rest --method PATCH --url https://graph.microsoft.com/v1.0/applications/<BFF_OBJECT_ID> --headers Content-Type=application/json --body @preauth-after-add.json
```
`preauth-after-add.json` = `{"api":{"preAuthorizedApplications":[ ...every existing entry from before-1e40baad.json, each Teams/broker entry with <UI_SCOPE_ID> added..., {"appId":"<NEW_CLIENT_ID>","delegatedPermissionIds":["<UI_SCOPE_ID>"]} ]}}` (the PATCH replaces the whole list — start from the saved one).
- **Rollback:** PATCH `{"api":{"preAuthorizedApplications": <the saved list from before-1e40baad.json>}}`.

**Step 3 — config + code follow-ups (session, not owner):** set `VITE_TEAMS_MSAL_CLIENT_ID=<NEW_CLIENT_ID>` in `.env.development` and `AAD_APP_CLIENT_ID` in `appPackage/env.dev.json`; in `.github/workflows/deploy-external-spa.yml` set `VITE_TEAMS_MSAL_CLIENT_ID`, add `VITE_TEAMS_MSAL_TENANT_ID: a221a95e-…`, change `VITE_TEAMS_MSAL_BFF_SCOPE` to `api://1e40baad-…/user_impersonation`; fix `deploy-teams-app.yml:91-92` (§6); bump `manifest.json` `version`.

**Step 4 — deploy (owner-gated):** SWA via `deploy-external-spa.yml`, Teams package via `deploy-teams-app.yml`, then the FR-21 live check (task 071): member + test guest `ralph@deweycheatham…`, browser + Teams desktop + web; token `tid` = Spaarke, `aud` = dev BFF app, `scp` contains `user_impersonation`. Escalation from the POML: if NAA fails without `webApplicationInfo`, STOP and record it for 071 — do not restore `webApplicationInfo`.
- **Rollback:** redeploy the previous SWA build / previous Teams package (`git revert` this change set, re-run both workflows). Step 2's pre-auths are additive, so the old build keeps working.

**Step 5 — after a verified step 4, REMOVE the A2-era pre-authorizations** (drop `<AAU_SCOPE_ID>` from the Teams/broker entries). Retiring the A2-era scope itself on `1e40baad` is a separate owner/provisioning decision (A8: "MUST NOT request or expose" it for the SPA/Teams host). Optionally remove `1e40baad`'s own SPA redirects for the dev SWA (it is no longer a client).
- **Rollback:** PATCH back the list saved before this step.

## 6. Dev sign-in before the registration exists

Not verified live (no live access in this task). Expected:
- **Local dev, browser "My organization"** (`npm run dev`, `.env.development`): the config is valid (interim client `1e40baad`, Spaarke-tenant authority, its own `user_impersonation` scope, which environments.json lists as exposed). Should sign in **if** `1e40baad` has `http://localhost:3000` as an SPA redirect — main.tsx's dev-mock comment says localhost has none, so likely only the deployed origin works. Unverified.
- **Deployed dev SWA (current workflow):** after this change merges, the workflow still injects the A2-era scope and no tenant id, so the **workforce plane fails loud** ("must be … user_impersonation" / missing `VITE_TEAMS_MSAL_TENANT_ID`) until step 3's workflow edit. CIAM is unaffected.
- **Teams on dev:** expected to **fail** until step 2 (the Teams/broker pre-auths are on the A2-era scope, so NAA for `user_impersonation` will likely hit a consent/pre-auth error) and until `{origin}/auth-callback.html` is registered on the client (popup → AADSTS50011). With the interim client this would mean registering the callback on `1e40baad`; the recommended path is step 1 instead.

## 7. Found but out of this task's edit scope (not changed)

1. `.github/workflows/deploy-external-spa.yml:52` — still `VITE_TEAMS_MSAL_BFF_SCOPE: api://1e40baad-…/<A2-era scope>`, and no `VITE_TEAMS_MSAL_TENANT_ID`. **Acceptance criterion 5 (grep over the workflow) is NOT met** — the wave rule limits this task to `src/client/external-spa`. Needs step 3.
2. `.github/workflows/deploy-teams-app.yml:91-92` — the "Validate Teams app manifest" step **requires** `webApplicationInfo.id/resource`; with A8 it now fails the Teams package job. Invert to assert absence (`jq -e 'has("webApplicationInfo") | not'`).
3. `src/main.tsx:212,238-242` — `extractAuthDiagnostics` reads `cause.ssoError`; the Teams error screen now shows `SSO: (none)` and omits `popupError`. Two-line change to read `popupError` (label "Popup").
4. `src/components/AuthGuard.tsx:25-29,40-49` — comments describe the removed `getAuthToken` fallback (behaviour still correct: Teams path renders children).
5. `src/client/external-spa/README.md:38-39` — describes a "multitenant" client; add `VITE_TEAMS_MSAL_TENANT_ID`.
6. `src/vite-env.d.ts:10-11` — `VITE_TEAMS_MSAL_TENANT_ID` not declared (works via Vite's index signature; cast in config.ts).
7. `.env.production:16-20` — no `VITE_TEAMS_MSAL_TENANT_ID=#{TEAMS_MSAL_TENANT_ID}#` line (CI env vars override it anyway; without either, the workforce plane fails loud by design).
8. **Pre-existing (likely, unverified live):** with msal-browser v5, silent iframe renewal on the standalone browser instances (CIAM and workforce, redirect = origin) lands on `index.html`, which does not run the redirect bridge, so it times out after 10 s; `acquireStandaloneBffToken` (`src/auth/msal-auth.ts:113-141`) rethrows non-interaction errors, so the BFF call fails instead of redirecting. Hits when the SPA refresh token expires (24 h). Not changed (CIAM must stay unchanged here). Candidate fix: `redirectUri: popupRedirectUri()` on silent requests, or treat `timed_out`/`monitor_window_timeout` as interaction-required.
9. **Pre-existing:** `staticwebapp.config.json:12` `"Referrer-Policy": "no-referrer-or-same-origin"` is not a valid policy value; browsers ignore it.
10. **Risk for task 071:** the Teams popup fallback runs at bootstrap (`TeamsHostAdapter.selectAuthStrategy`) without a user gesture; browsers and the Teams desktop webview may block `window.open` (`popup_window_error`). If the live check shows this, the fallback needs a "Sign in" button that starts the popup.

## 8. Skipped (wave rules)

POML step 1 (ask owner for the client id) — escalated via this note; step 8 (TASK-INDEX / POML status / drift script) — not mine to edit; no commit.

## Completion fixes (2026-10-10)

Items 1-7 of the completion round. Section 7 above is now resolved except where noted; section 5 step 3 is done for the workflow edits (the client id in the workflow stays INTERIM until step 1). Not committed (wave rule). Tests live in `tests/workforce-auth.test.ts` (47 tests now; 10 added).

| # | Item | What changed | Tests |
|---|---|---|---|
| 1 | `deploy-teams-app.yml` manifest validation | The two `webApplicationInfo.id/resource` "must exist" checks are replaced by `jq -e 'has("webApplicationInfo") \| not'` (A8 MUST NOT). Rest of the job untouched. | `CI workflows and static config agree with A8 > deploy-teams-app.yml asserts the manifest has NO webApplicationInfo` |
| 2 | `deploy-external-spa.yml` build env | `VITE_TEAMS_MSAL_BFF_SCOPE` is now `api://1e40baad-.../user_impersonation`; new `VITE_TEAMS_MSAL_TENANT_ID: a221a95e-6abc-4434-aecc-e48338a1b2f2`. The workflow hardcodes dev ids as non-secret literals (its established pattern), so the tenant id is hardcoded the same way. `VITE_TEAMS_MSAL_CLIENT_ID` stays `1e40baad-...` (INTERIM; a comment points at section 5 steps 1 and 3 of this file). | `... deploy-external-spa.yml injects the tenant id and the user_impersonation scope, not the retired scope` |
| 3 | Teams error screen | `extractAuthDiagnostics` moved verbatim from `main.tsx` to the new `src/auth/auth-diagnostics.ts` (main.tsx runs the bootstrap on import, so it was untestable) and now reads `{ naaError, popupError }`; it prints `NAA:` and `Popup:` lines and no `SSO:` line. | `Teams error screen diagnostics read the { naaError, popupError } cause` (2 tests) |
| 4 | Declarations and stale docs | `VITE_TEAMS_MSAL_TENANT_ID` declared in `vite-env.d.ts`; `.env.production` gets `VITE_TEAMS_MSAL_TENANT_ID=#{TEAMS_MSAL_TENANT_ID}#` (existing placeholder style, comment rewritten for A8); `AuthGuard.tsx` comments no longer describe `getAuthToken`; `README.md` env table describes the single-tenant client, the tenant id and `user_impersonation`. | `... VITE_TEAMS_MSAL_TENANT_ID is declared in vite-env.d.ts, .env.production and .env.example` |
| 5 | `Referrer-Policy` | `no-referrer-or-same-origin` (invalid, ignored by browsers) is now `strict-origin-when-cross-origin`. | `... the SWA Referrer-Policy is a valid policy value` |
| 6 | Silent renewal (CIAM + workforce browser instances) | CONFIRMED and fixed (below). | `Browser silent renewal (CIAM and workforce) ...` (4 tests) |
| 7 | Popup without a user gesture in Teams | Not changed (below). | - |

### Item 6 evidence and fix

Read in `node_modules/@azure/msal-browser` 5.5.0 (`src/`):
- `StandardController` falls back to a silent iframe renewal when the refresh token is expired, `invalid_grant` or missing (`isSilentlyResolvable && tryIframeRenewal`, around lines 2365-2385; the default `cacheLookupPolicy` allows it).
- `SilentIframeClient.acquireToken` (and `ssoSilent`) end with `BrowserUtils.waitForBridgeResponse(config.system.iframeBridgeTimeout = 10000 ms, ...)`. They wait for a `BroadcastChannel` message posted by `broadcastResponseToMainFrame()` from `@azure/msal-browser/redirect-bridge`, and reject with `BrowserAuthError` `timed_out` otherwise.
- Nothing in the SPA's `index.html` or `src` calls the bridge (grep: no match), and the browser instances used `redirectUri = origin` (index.html). So the iframe landed on the SPA and the renewal timed out after 10 s with `timed_out`, which `acquireStandaloneBffToken` rethrew (it only handled `InteractionRequiredAuthError`). Result: after the refresh-token lifetime the BFF call failed instead of re-authenticating. With a working bridge, a dead SSO session returns `login_required`, i.e. `InteractionRequiredAuthError`, and the existing redirect handles it.

Fix (`src/auth/msal-auth.ts`, `acquireStandaloneBffToken`; serves CIAM and the browser workforce instance):
- The silent request carries `redirectUri: popupRedirectUri()` (`{origin}/auth-callback.html`, the page that runs the bridge). Per-request only: the instance config, the interactive redirect flow and the CIAM singleton keep `redirectUri = origin`; per-tab sessionStorage isolation is untouched (A1). ~~The refresh-token grant does not use `redirect_uri`.~~ **WRONG, corrected in the review fixes below:** an explicitly provided `redirectUri` is forwarded by `SilentRefreshClient` and sent as `redirect_uri` in the refresh-token grant (msal-common `RefreshTokenClient.ts:401`); this fix was superseded.
- A failed silent attempt now falls back to the file's existing interactive path (`acquireTokenRedirect`, route preserved) on `InteractionRequiredAuthError` and on `BrowserAuthError timed_out` (the bridge never answered, e.g. the redirect URI is not yet registered so the iframe shows AADSTS50011). Other errors still rethrow. The `timed_out` case goes slightly beyond "InteractionRequired" in the brief; it keeps the SPA usable until the owner registers the URI.
- Teams instances already used `/auth-callback.html` (unchanged).

Fail-before / pass-after (with `msal-auth.ts` reverted): "asks for the silent renewal on {origin}/auth-callback.html" and "falls back ... when the silent iframe renewal times out" fail before and pass after. "falls back on InteractionRequiredAuthError" and "still rethrows an unrelated failure" pass both ways (regression guards).

### Item 7 - for live check task 071 (not changed)

The Teams popup fallback runs during bootstrap (`TeamsHostAdapter.selectAuthStrategy`) with no user gesture. Browsers and the Teams desktop/web webview may block `window.open` (`popup_window_error` / `popup_blocked`), so on a host with no NAA bridge the fallback could fail even with everything registered. In 071 check Teams desktop, web and mobile with NAA unavailable and a fresh session. Suggested mitigation if it fails: on a popup-blocked error render the sign-in screen with a "Sign in" button whose click handler calls the popup acquisition (`acquireTokenPopup`), then continue bootstrap; the error screen already carries `{ naaError, popupError }` for the diagnosis. Do not restore `webApplicationInfo`.

### Checks (2026-10-10, src/client/external-spa)

- Fail-before (fix files reverted): 9 failed / 38 passed in `tests/workforce-auth.test.ts` (8 genuine; the 9th, the `/auth-callback.html` SWA exclusion test, failed only because my revert also dropped the task-040 exclusion; restored immediately). Pass-after: 47 passed.
- `npm run typecheck` exit 0; `npm run lint` exit 0; `npx vitest run` 7 files / 128 passed; `npm run build` exit 0, `dist/auth-callback.html` identical to `public/`.
- `dotnet test tests/Spaarke.ArchTests --filter "ExternalSpaGridViewSelectorGuardTests"`: 220 passed.
- Workflows: `actionlint` and `jq` are not installed here. Both files parse as YAML (python `yaml.safe_load`), the edited steps were re-read, and the new `jq` predicate's intent is asserted by the test above against the manifest's actual shape.

### New owner live steps (NOT run)

1. Register `{origin}/auth-callback.html` as an SPA redirect URI on the CIAM SPA client `bd57e54e-b339-4500-b55c-e451009fd907` (CIAM tenant `7052feba-bfc4-43e0-b09e-65014b429131`) for each deployed origin: `https://external.spaarke.com/auth-callback.html`, `https://green-dune-0c4f1221e.7.azurestaticapps.net/auth-callback.html`, and `http://localhost:3000/auth-callback.html` for local dev. Until it is registered, silent renewal times out after 10 s and falls back to the redirect login (works, slower). Rollback: remove the URIs (additive; nothing else depends on them).
2. The workforce client ("Spaarke Workspace", section 5 step 1) already lists `/auth-callback.html` for every origin, which covers the browser workforce renewal too. Until step 1 is done, the INTERIM client `1e40baad-...` needs the same URIs for the Teams popup and browser silent renewal.
3. Section 5 stays as written; step 3's workflow edits are done, but `VITE_TEAMS_MSAL_CLIENT_ID` in the workflow must still be replaced with the new client id after step 1, and `manifest.json` `version` bumped.

## Review fixes (2026-10-10)

Two independent adversarial reviews of the 040 change. Not committed (wave rule). Files: `src/auth/msal-auth.ts`, `standalone-plane.ts`, `AuthGuard.tsx`, `App.tsx`, `host/TeamsHostAdapter.ts`, `main.tsx`, new `components/auth/TeamsAuthError.tsx`, `appPackage/manifest.json`, `.github/workflows/deploy-external-spa.yml`, `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`. Tests: `tests/workforce-auth.test.ts` (71 tests; it had 47).

### Correction: the "redirect_uri is not used by the refresh-token grant" claim above was false

Read in msal-browser 5.5.0 / msal-common (node_modules `src/`): `StandardController.acquireTokenSilent` spreads the request; `SilentRefreshClient.acquireToken` (lines 56-62) copies an explicitly provided `request.redirectUri` into the silent request ("only include if explicitly provided"); `RefreshTokenClient.ts:401` then adds `redirect_uri` to the refresh-token grant body. So `redirectUri: popupRedirectUri()` on every silent call changed the request sent to the IdP on both planes, and it was not iframe-only. The earlier "item 6" fix is replaced by the design below.

### F (fix-now, both reviewers): silent renewal

`acquireStandaloneBffToken(instance, bffScope, options?)` now takes `StandaloneAcquireOptions { tenantId?, silentRedirectUri? }`; only the workforce browser plane (`resolveStandalonePlane('workforce').acquireOptions`) supplies them.
- **CIAM** (`acquireBffToken`, no options): ONE `acquireTokenSilent({ scopes, account })`, exactly the pre-040 request (no `redirectUri`, no `cacheLookupPolicy`, `getAllAccounts()` with no filter). On `InteractionRequiredAuthError` or `BrowserAuthError timed_out` (the new fallback; msal's own default-policy iframe lands on index.html and times out) -> the existing `acquireTokenRedirect` with the account, no `redirectUri`.
- **Workforce browser**: first `acquireTokenSilent({ scopes, account, cacheLookupPolicy: AccessTokenAndRefreshToken })` with NO `redirectUri` (msal opens no iframe for that policy: `iFrameRenewalPolicies` = Default, Skip, RefreshTokenAndNetwork). Only if it fails with an error msal itself would resolve by iframe - `isSilentlyResolvableByIframe`, a mirror of `checkIfRefreshTokenErrorCanBeResolvedSilently` (StandardController.ts ~2363-2389): `invalid_grant` / `token_refresh_required` on a non-InteractionRequired error, `no_tokens_found`, `refresh_token_expired` - retry once with `{ cacheLookupPolicy: Skip, redirectUri: popupRedirectUri() }` (iframe answered by the bridge page; `Skip` is in `iFrameRenewalPolicies` and a `Skip` cache miss raises `token_refresh_required`, which then takes the iframe branch). Then the redirect fallback (InteractionRequired / `timed_out`). Anything else rethrows.
- Teams instances keep `redirect = {origin}/auth-callback.html` (unchanged).

Tests (describe `Browser silent renewal: CIAM requests are unchanged on the wire; the workforce iframe retry uses the bridge page`):
- CIAM: `sends exactly { scopes, account } on the silent call: no redirectUri, no cacheLookupPolicy` (`toStrictEqual`, asserted through `acquireBffToken()`); `falls back ... InteractionRequiredAuthError - after ONE silent call`; `... times out (timed_out)`; `still rethrows an unrelated failure`; `the browser CIAM plane carries no acquire options`.
- Workforce: `first silent call has NO redirectUri ...` (strict request incl. `AccessTokenAndRefreshToken`); `retries ONCE through the bridge iframe (policy Skip + redirectUri) only when the refresh token is expired` (only the `Skip` call has `redirectUri`); `also retries through the iframe for` no_tokens_found / invalid_grant / token_refresh_required; `a plain InteractionRequiredAuthError (login_required) goes straight to the redirect: no iframe retry`; iframe-retry `timed_out` -> redirect; iframe-retry interaction required -> redirect; unrelated error rethrows with one call.
- Fail-before: with the 040 sources restored (msal-auth.ts, standalone-plane.ts, AuthGuard.tsx, App.tsx, TeamsHostAdapter.ts, main.tsx) and a stub `TeamsAuthError`, `npx vitest run tests/workforce-auth.test.ts` -> **20 failed / 51 passed** (the 3 CIAM tests that assert no `redirectUri` fail because the old code sent one; the workforce, tenant, Teams and screen tests fail on missing behaviour). The 51 passing before include regression guards (unrelated failure rethrows, NAA fallback after a non-cancel failure, all pre-existing tests). Pass-after: **71 passed**.

### K1: workforce account selection is tenant-scoped

MSAL's account list is not client-scoped and both browser instances share the tab's sessionStorage. Verified in msal-common `CacheManager`: `getAllAccounts({ tenantId })` keeps only accounts that have a tenant profile for that tenant (`getTenantProfilesFromAccountEntity`, `searchTenantProfiles.get(targetTenantId)` - a case-sensitive key, so the id is lower-cased; read from source, not run live). `acquireStandaloneBffToken` uses it when `options.tenantId` is set (CIAM still calls `getAllAccounts()` with no argument), so a cached CIAM account is a no-account case -> the sign-in redirect. The same rule is applied where MSAL's raw list was used: `AuthGuard` (was `useIsAuthenticated`, which counts any account) and `App.tsx` (portal user = `accounts[0]`) now go through `accountsForActivePlane()`, driven by a seam `setActiveAccountTenantId` that `applyStandalonePlane` sets for workforce and clears (null = no filter) for CIAM. Tests: `only counts accounts of the Spaarke tenant: a cached CIAM account is not a workforce sign-in` (filter argument asserted, no silent call, redirect with `{ scopes }` only), `picks the Spaarke-tenant account when a CIAM account is cached next to it`, `AuthGuard / portal-user account lists are scoped to the plane`, `the browser workforce plane supplies the tenant filter and the bridge page`. Fail-before: included in the 20.

### K2: Teams NAA skip and no second popup

teams-js 2.54.0 `nestedAppAuth.isNAAChannelRecommended()` exists (throws before `app.initialize`). `acquireTeamsWorkforceBffToken` skips the NAA step when it returns false (no nestable instance, no 10 s `ssoSilent` iframe; `naaError` records why) and treats a throw as "unknown, try NAA". When NAA fails with `errorCode === 'user_cancelled'` no popup fallback runs; it throws `TeamsWorkforceAuthError{ naaError, popupError: null }`. Tests (describe `Teams NAA step is skipped on a bridge-less host; a cancelled sign-in opens no second popup`): skip when not recommended; still tries NAA when the call throws; `user_cancelled` opens no popup (no standard instance built, only `ssoSilent` recorded); a non-cancel failure still falls back.

### K3: "Sign in" button on the Teams error screen

New `src/components/auth/TeamsAuthError.tsx` (Fluent v9, `makeStyles` + semantic tokens only; replaces the inline markup in `main.tsx`). "Sign in" calls `onSignIn()` synchronously in the click handler (the popup keeps the user gesture). `main.tsx` wires it to the new `ITeamsHostAdapter.signInWithPopup()` -> `signInTeamsWorkforceViaPopup(cfg)` (exported from msal-auth, the same popup ladder as the fallback) -> `completeAuthSetup` (acquirer + signed-in instance) -> `renderTeamsApp`, so recovery needs no reload. A failed click shows the new error and diagnostics and keeps the button. Caveat: if `initialize()` failed BEFORE the auth step (e.g. `getContext`), recovery still renders the app but skips the theme/deep-link wiring - not a path seen in practice. Tests (describe `Teams error screen: a "Sign in" button runs the popup path on click (user gesture)`): not called until clicked and called exactly once synchronously in the click; failure message shown and button kept; `clicking drives the real popup path` (bootstrap popup blocked -> click -> `acquireTokenPopup` on the standard instance -> adapter instance is that popup instance -> `acquireActiveBffToken()` serves a `user_impersonation` token); `signInTeamsWorkforceViaPopup uses the standard instance only`. Fail-before: all fail against the stub component / old msal-auth.

### K4: manifest version

`appPackage/manifest.json` `version` 1.1.0 -> 1.2.0. No existing test pinned it. Test: `the Teams manifest version is bumped past 1.1.0` (fails at 1.1.0, verified).

### K5: stale text

- `.github/workflows/deploy-external-spa.yml` comment ("There is no public/ dir") now says `public/` holds only `auth-callback.html`.
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (the acct-claim paragraph, "Teams SSO included (`webApplicationInfo.id` ...)"): now describes the A8 model (single-tenant Spaarke client, NAA then MSAL popup, `user_impersonation`, no `webApplicationInfo`, no Teams SSO).
- Teams error copy ("your organization may not have consented") -> "Sign in with your work account in Spaarke's directory. If it keeps failing, contact your administrator." (in `TeamsAuthError.tsx`).
- Test: `no stale text: ...` (fails against the 040 `main.tsx`).

### K6: Copilot agent still uses the retired scope - constrain section 5 step 5

`scripts/Deploy-CopilotAgent.ps1:69` (`$BffScopeName = "access_as_user"`, default BFF app `1e40baad-...`, line 66) and `tests/scripts/CopilotAgentPackage.Tests.ps1:136` (`api://1e40baad-.../access_as_user`) still request that scope: the Copilot agent is outside A8's SPA/Teams scope. So section 5 step 5 (removing the A2-era pre-authorizations on `1e40baad`) must remove ONLY the Teams/broker/SPA pre-authorizations, NOT the scope itself and NOT the Copilot agent's pre-authorization. Retiring the scope on `1e40baad` is blocked until the Copilot agent has its own decision (separate owner/provisioning item).

### Review K-items recorded, not fixed here

- Teams `BroadcastChannel` cross-partition risk (the bridge page and the app may sit in different storage partitions in some Teams webviews, so the popup/iframe response may never arrive): not fixable in this client; already added as a constraint to task 071's live check.
- The popup fallback at bootstrap still runs without a gesture (unchanged); K3 is the recovery.
- Real-MSAL behaviour of `getAllAccounts({ tenantId })`, the `Skip` iframe retry and `isNAAChannelRecommended()` is read from source and covered with mocks of MSAL; none was run against a live tenant (no live access).

### Checks (2026-10-10, src/client/external-spa)

- `npm run typecheck` exit 0; `npm run lint` exit 0 (no warnings).
- `npx vitest run` -> 7 files, **152 passed** (was 128).
- `npm run build` exit 0; `dist/auth-callback.html` identical to `public/`.
- `dotnet test tests/Spaarke.ArchTests --filter ExternalSpaGridViewSelectorGuardTests` -> **220 passed**.
- Workflow edit is a comment only (no `actionlint` here).

## Re-check fixes (2026-10-10)

A scoped re-check (sonnet) of the task 040 fix round found one fix-now item and four known-limit items.

### F2 (fixed): a dismissed Teams NAA prompt opened a second popup

`isUserCancelled` (msal-auth.ts) matched only `BrowserAuthErrorCodes.userCancelled` (`user_cancelled`). msal-browser 5.5.0 `NestedAppAuthAdapter.fromBridgeError` maps `BridgeStatusCode.UserCancel` to `new ClientAuthError(ClientAuthErrorCodes.userCanceled)` (`user_canceled`, one `l`), so dismissing the Teams-brokered NAA prompt fell through to the MSAL popup (a double prompt). It now matches both codes (`ClientAuthErrorCodes` is re-exported by msal-browser). Test: `opens no popup fallback when NAA fails with user_canceled (ClientAuthError, the shape NAA emits for a dismissed Teams prompt)`; the `user_cancelled` BrowserAuthError case is kept. Fail-before: against the old `msal-auth.ts` the new case fails (`expected null to be an instance of TeamsWorkforceAuthError` - the popup fallback ran and returned a token).

### K1 (fixed): click path no longer spends the user gesture on a silent attempt

`signInTeamsWorkforceViaPopup` (the error screen's "Sign in" click, via `TeamsHostAdapter.signInWithPopup`) used to run `acquireTokenSilent` for a cached account (possible ~10 s hidden iframe) before `acquireTokenPopup`, outlasting the click's transient user activation (~5 s in Chromium) so the popup could be blocked. The click path now goes straight to `acquireTokenPopup` (`signInViaPopupInstance(cfg, false)`); the bootstrap / renewal path (`acquireTeamsWorkforceBffToken`) keeps the silent-first ladder (`signInViaPopupInstance(cfg, true)`). The standard popup instance is also created and initialised ahead of time when the bootstrap fails (user-cancelled NAA, or a failed popup), so the click does not await `PublicClientApplication.initialize()` either (`prewarmTeamsWorkforcePopupInstance`, best effort, memoized). Tests: `the click path never runs acquireTokenSilent before acquireTokenPopup, even with a cached account (user gesture)` (fail-before: second click returned the silent token), `the bootstrap (non-click) popup path still tries the cached account silently first`; the existing cancel test now asserts the pre-built instance was never asked to open a popup.

### K2 (comment fix)

Comments in msal-auth.ts (`StandaloneAcquireOptions`, `acquireStandaloneBffToken`, `acquireBffToken`, the acquirer seam, the login-scope seam) and bff-client.ts claimed the CIAM behaviour is "byte-for-byte" unchanged. Reworded: the CIAM silent request shape (`{ scopes, account }`, no redirect URI, no cache policy) is unchanged; a `timed_out` silent renewal now routes to the interactive redirect, deliberately. (msal-config.ts, standalone-plane.ts and AuthGuard.tsx "byte-for-byte" comments are about config / scope values and stay accurate.)

### K5 (pin added)

Test `msal-browser assumptions the silent-renewal design relies on`: reads `iFrameRenewalPolicies` from the installed `node_modules/@azure/msal-browser/dist/utils/BrowserConstants.mjs` (not exported publicly) and asserts `CacheLookupPolicy.Skip` is in it and `CacheLookupPolicy.AccessTokenAndRefreshToken` is not. The failure message says to re-verify `acquireSilent()` / the workforce silent-renewal design on an msal-browser upgrade. A pin: it passes before and after this round.

### Deferred to live verification (task 071)

- K3: `notifyFailure(AuthFailed)` is sent before the recovery screen renders - Teams may hide the iframe and the "Sign in" button; and a mid-session popup-mode renewal failure has no sign-in UI until reload.
- K4: Teams web `BroadcastChannel` storage partitioning between the bridge page and the app (already a 071 constraint).

## Live run (dev, 2026-10-11; owner approval "yes proceed 040, 041, 043")

Run by the session as `ralph.schroeder@spaarke.com` in tenant `a221a95e-…`. "Before" snapshots kept in the session scratchpad (not committed); the rollback lists are reproduced from them.

| Step | Result |
|---|---|
| 0 | Snapshot of `1e40baad` (object `c2aab303-50f8-4279-9934-503ab3a4b357`): `user_impersonation` = `18afc847-a660-40b2-8925-747aed613f36`; A2-era `access_as_user` = `7e9e1e5a-3b0b-4153-9753-85b41d48c6fe`; 9 pre-authorized clients; Teams desktop/web and the broker on `access_as_user` only |
| 1 | **"Spaarke Workspace" created**: client id `9f1da340-c647-4266-9d89-e09f10700593`, object `f53c9698-13f9-4d37-b53e-aab75f57a210`, SP `06850da2-912a-4d10-9159-cb9d66868695`, `AzureADMyOrg`; the 8 SPA redirect URIs of step 1 verified. Rollback: `az ad app delete --id 9f1da340-c647-4266-9d89-e09f10700593` |
| 2 | Pre-authorizations PATCHed additively (9 → 10): `18afc847` added to `1fec8e78`, `5e3ce6c0`, `29d9ed98` (keeping `7e9e1e5a`); `9f1da340` added on `18afc847`. Read-back matches the intended list. Rollback: PATCH `preAuthorizedApplications` back to the 9 entries of the step 0 snapshot |
| 3 | `.env.development` and `appPackage/env.dev.json` now use `9f1da340`; GitHub `external-spa-dev` `VITE_TEAMS_MSAL_CLIENT_ID` = `9f1da340` (task 043) |
| 4 | Not run yet: dev SWA + Teams package dispatch, then the FR-21 live check (task 071) |
| 5 | Not run: waits on a verified step 4. Remove only the Teams/broker `7e9e1e5a` entries — never the scope or the Copilot agent's pre-authorization (K6) |
| CIAM `/auth-callback.html` on `bd57e54e` | Not run: the CIAM tenant `7052feba-…` needs an interactive MFA sign-in (AADSTS50076) |
