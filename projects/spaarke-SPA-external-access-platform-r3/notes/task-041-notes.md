# Task 041 — Run-time backend selection on both planes (FR-17, NFR-02): notes

**Date:** 2026-10-10 · **Rigor:** FULL (auth, tenant isolation) · **Status at hand-off:** code + tests done, not committed (the main session commits by path). POML step 8 (TASK-INDEX / POML status / drift script) not run — outside this agent's edit scope.

Binding rules: spec FR-17 / NFR-02; ADR-028 A7 ("`api://{customer BFF app id}/user_impersonation` on **both** planes … the customer BFF app id and server URL come from Spaarke-owned data: the T240c directory, or dev configuration until it ships … never from a link, query string or other client-supplied input"); review 040 K3 (CIAM scope must become `user_impersonation`, remove the `SDAP.Access` default).

## 1. Design

`src/auth/backend-selection.ts` is the single chooser. Everything that needs a BFF URL or scope reads the **active backend** from it.

| Piece | What it does |
|---|---|
| `Backend` | `{ customerKey, displayName, bffBaseUrl, bffAppId }` (POML step 1). `ActiveBackend` adds `plane` and `bffScope = api://{bffAppId}/user_impersonation`. |
| `BackendDirectory` | `resolveWorkforce(): Promise<Backend[]>`, `resolveByKey(key): Promise<Backend \| null>` (POML step 1). |
| `DevConfigDirectory(plane)` | Dev data until T240c: `VITE_BACKENDS` (JSON list) or, when unset, ONE entry (`customerKey: "default"`) from `VITE_BFF_API_URL` + the plane's scope key (`VITE_TEAMS_MSAL_BFF_SCOPE` workforce, via `getWorkforceEnvConfig`; `VITE_MSAL_BFF_SCOPE` CIAM). Answers both lookups from that list. |
| `DirectoryServiceClient(plane)` | Stub behind `VITE_USE_DIRECTORY_SERVICE=true`, `TODO(provisioning task 240c)`. Throws, so that build fails closed. |
| `resolveBackendSelection(plane)` | Candidates only from the directory/config: workforce = `resolveWorkforce()`; CIAM = `resolveWorkforce()` + `resolveByKey()` for the `?customer=` key, the tab's active key and the remembered keys. Then: valid `?customer=` among candidates → it; else the tab's active key; else the only candidate; several → `pick`; none / directory error → `none` (fail closed). Every record is re-validated; invalid ones are dropped. |
| `activateBackend` / `chooseBackend` | Re-validate, derive the scope, store the key (tab), remember the key (browser). The picker can only choose an offered key. |
| `getActiveBackend()` | Throws when nothing is active — no request without a backend. |
| `activeBffScope(plane)` | The active backend's scope; throws when the active backend is for the other plane. Before any activation (direct calls outside the bootstrap, e.g. the 040 tests) it falls back to the dev config's **single** backend for the plane, else throws. |
| `bffRequestUrl(backend, path)` | Path must be root-relative; the final URL must stay on the backend origin (`//host`, `@host`, `\\host`, absolute URLs refused). |

Wiring:
- `bff-client.ts`: `bffApiCall`/`bffApiBlob` share `authorizedFetch`, which snapshots the active backend ONCE and uses that record for both the URL and `acquireActiveBffToken(backend.bffScope)` (also on the 401 retry). A backend switch mid-request cannot pair one backend's token with another's host. No active backend → reject before any token or fetch.
- `msal-auth.ts`: `BffTokenAcquirer` is now `(scope?) => Promise<string>`; the default CIAM acquirer uses the passed scope or `activeBffScope('ciam')`. `acquireBffToken()` stays **argument-free** (pages hand it to libraries as a callback; an argument can never become a scope). `getActiveLoginScope()` defaults to `activeBffScope('ciam')`. `MSAL_BFF_SCOPE` import removed.
- `standalone-plane.ts`: scope = `activeBffScope(realm)`; workforce client/tenant from the new `getWorkforceClientConfig()` (no scope needed). The acquirer uses the passed scope, else the plane's.
- `TeamsHostAdapter.ts`: `cfg = { ...getWorkforceClientConfig(), bffScope: activeBffScope('workforce') }`; the installed acquirer uses the passed scope, else the active workforce scope at call time.
- `services/gridDataverseClient.ts`: was built at module load from `BFF_API_URL` (before any selection). Now `gridDataverseClient` is a stable `IDataverseClient` that delegates each call to a `BffDataverseClient` for the backend active at call time; its fetch refuses a URL off that backend's origin and asks for that backend's scope. Fixed here (not 042): it is the main read path and would otherwise send a selected backend's token to the build's host.
- `main.tsx`: browser bootstrap = realm → selection → (picker | fail-closed error | activate) → plane → MSAL init → App. Teams bootstrap = selection (workforce) → (picker | "No organization available" screen | activate) → adapter. Dev-mock mode activates the single CIAM backend when there is one. "Choose a different sign-in" also clears the active backend.
- `components/auth/CustomerPicker.tsx`: Fluent v9, mirrors `RealmChooser` (centred card, `Title2`, large buttons), semantic tokens only, display names only.
- `config.ts`: `BFF_API_URL` and `MSAL_BFF_SCOPE` constants removed (no build-time URL/scope); new `readOptionalEnv`, `getWorkforceClientConfig`. `getWorkforceEnvConfig` unchanged (now feeds only the dev fallback). `msal-config.ts`: `ciamAuthorityConfig.bffScope` = `VITE_MSAL_BFF_SCOPE` or `''` (informational); `buildMsalConfiguration` requires `bffScope` only for the workforce kind.

## 2. Decisions

1. **Per-plane app id in dev (`ciamBffAppId`).** Dev's CIAM audience is a separate app in `spaarkeextid` (`4a4d5126`), the workforce one is `1e40baad`; on a stamp (T240d option 3) one customer BFF app serves both. `VITE_BACKENDS` entries take an optional `ciamBffAppId` override; the directory returns a `Backend` already resolved for the plane, so the POML `Backend` shape is unchanged.
2. **Fallback one-entry list reads the existing keys** (POML constraint "the existing single VITE_BFF_API_URL as a one-entry list"); the app id is parsed out of `api://{id}/user_impersonation`. Any other scope (the former CIAM scope, the retired A2-era scope) is refused with a message naming the key.
3. **`resolveWorkforce()` on the CIAM plane.** Dev: the configured list (dev has no invite data, "dev keeps one configured backend"). T240c: documented as `[]` (contacts have no membership lookup; their backends come by key).
4. **Workforce `?customer=`** chooses only among `resolveWorkforce()` (membership); the key lookup is not used on that plane.
5. **Active key per tab in `sessionStorage`** (`spaarke.ext.activeCustomer`) so it survives the MSAL login redirect; **known keys in `localStorage`** (`spaarke.ext.knownCustomers`, ≤ 20, keys only, validated on read). Every access is try/catch.
6. **Customer key syntax** `^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`; **app id** `^[A-Za-z0-9][A-Za-z0-9.-]{0,127}$` (GUIDs in practice; the test env's `bff.test` passes); **base URL** https origin only (http only for localhost/127.0.0.1), no path/query/fragment/credentials.
7. **Workflow edited** (`deploy-external-spa.yml`): `VITE_MSAL_BFF_SCOPE` → `api://4a4d5126-…/user_impersonation`. The POML requires removing the `SDAP.Access` default; leaving it would make the deployed CIAM plane fail closed on the refused scope.
8. **Existing 040 tests unchanged** — all 71 pass as they were (the single-default rule in `activeBffScope` keeps their direct calls working).

## 3. Tests — `tests/backend-selection.test.ts` (59 tests)

- **Dev configuration:** two backends with per-plane app ids; the one-entry fallback; the former CIAM scope refused (selection `none`, `acquireBffToken` rejects, no MSAL request); every scope is `…/user_impersonation`; missing config → `none` and `bffApiCall` sends nothing; 9 malformed `VITE_BACKENDS` entries refused (http non-local, path, query, credentials, `javascript:`, app id with `/`, URL-shaped key, duplicate key, bad CIAM app id); 3 malformed lists (not JSON, empty, object); directory flag on → `none` (T240c), no default.
- **Selection:** two backends → `pick` (both planes); one → selected; stored active key → no picker; `?customer=` configured key → selected (both planes); default read from `window.location`; crafted unknown key ignored → picker (both planes) / single default; an unknown key does not override the active one; tampered stored key ignored; **URL-shaped values in every query parameter never become a host or a scope** (end-to-end through `bffApiCall`); workforce ignores key-lookup-only keys; CIAM key lookup returning another key's record ignored; invalid directory records dropped; directory failure → `none`; picker accepts only offered keys.
- **Requests:** `bffApiCall`/`bffApiBlob` go to the selected host with its scope, and move on re-selection; 401 retry same host + scope; **mismatched origin/scope pair** (backend switched during token acquisition) — request keeps the snapshot pair; no active backend → no token, no fetch; 5 non-root-relative paths refused before any token; grid reads follow the active backend and reject without one.
- **Planes:** workforce browser (login scope, MSAL request scope, host); **CIAM through the same seam** (`?customer=` → plane → login scope → MSAL request `api://{ciamBffAppId}/user_impersonation` → host); default CIAM acquirer; `acquireBffToken` used as a callback ignores its argument; workforce-plane backend never yields a CIAM token request; Teams adapter (NAA requests and host follow the selected workforce backend).
- **Storage:** keys only in `localStorage`; remembered keys come back after a reload and drive the picker (looked up, not trusted); tampered values dropped; `localStorage` + `sessionStorage` throwing → selection, picker choice, `?customer=` and requests still work.
- **Picker:** one button per option, display names only, reports the key; source has no hard-coded colours.
- **Shipped config:** no `SDAP.Access` in the env files or the workflow, dev/workflow CIAM scope is `…/user_impersonation`; no source file reads the BFF URL env key outside `backend-selection.ts` and none uses `BFF_API_URL`.

**Fail-before:**
- With all task-041 sources reverted to HEAD (test file kept): the suite fails to load (`Failed to resolve import "../src/auth/backend-selection"`), 0 tests run.
- Wiring-only revert (HEAD sources + the two NEW files + a temporary `readOptionalEnv` shim in the old `config.ts`): **21 failed / 37 passed** of 58. The 21 are the wiring tests: CIAM scope refusal, missing-config fail-closed, URL-shaped params end-to-end, all 9 request tests, all 5 plane tests, storage-throwing end-to-end, both shipped-config scans. The 37 that pass exercise the new module's own logic (present in that run).
- The 59th test (`acquireBffToken … ignores any argument`, added after review of a callback hazard) fails against the previous `acquireBffToken(scope?)` (`expected 'token:api://evil/user_impersonation' …`) and passes after.
- Restored afterwards; `git status` matches the intended change set.

**Checks (src/client/external-spa, 2026-10-10):**
- `npm run typecheck` exit 0; `npm run lint` exit 0 (no warnings).
- `npx vitest run` → **8 files, 215 passed** (baseline before the task: 7 files, 156 passed), twice in a row.
- `npm run build` exit 0 (only the existing >500 kB chunk warning).
- Prettier: changed/new files formatted (RecordDetailModal.tsx was already unformatted at HEAD; only a comment line changed there).
- Workflow: `actionlint` is not installed; the file parses with Python `yaml.safe_load` and the edited value reads back as `api://4a4d5126-…/user_impersonation`.
- `dotnet`/ArchTests not run (brief: no .NET). The grid guard (`ExternalSpaGridViewSelectorGuardTests`) scans grid imports; `gridDataverseClient.ts` adds only a type import from `@spaarke/ui-components/services/IDataverseClient` — re-run it at commit time to be sure.

## 4. Owner live steps (NOT run) — with rollback

**Step 1 (required before the next dev SWA deploy) — CIAM `user_impersonation` on the dev CIAM BFF API app.** In `spaarkeextid` (`7052feba-bfc4-43e0-b09e-65014b429131`), app **Spaarke External BFF API** `4a4d5126-91b0-4865-8e3a-134b7209013e`:
- read first: `az ad app show --id 4a4d5126-91b0-4865-8e3a-134b7209013e --query "{id:id, scopes:api.oauth2PermissionScopes[].{v:value,id:id}, preAuth:api.preAuthorizedApplications}" -o json > before-4a4d5126.json` (signed in to the CIAM tenant);
- add a delegated scope `user_impersonation` (admin + user consent, enabled) if it is not there;
- grant the CIAM SPA client `bd57e54e-b339-4500-b55c-e451009fd907` that delegated permission and admin-consent it (or pre-authorize `bd57e54e` on the new scope id).
- Why: the SPA now requests `api://4a4d5126-…/user_impersonation` for CIAM (A7). The BFF `Ciam` scheme checks the audience, not the scope name, so no BFF change. Until this is done, CIAM sign-in on a build with this change fails at Entra (invalid/unconsented scope).
- Rollback: remove the grant / pre-authorization and disable then delete the new scope (PATCH back from `before-4a4d5126.json`), and redeploy the previous SWA build. The old `SDAP.Access` scope is untouched by this step.

**Step 2 — FR-17 UI tests on dev (POML ui-tests, task 070/072):** set `VITE_BACKENDS` to two dev entries (e.g. the dev BFF twice under two keys, or a second test BFF), sign in, pick the second → network requests go to its host with its scope; open `/?customer=unknown-key` → ignored, no request to an unlisted host; dark mode on the picker.

Already-known 040 owner steps (workforce client, `/auth-callback.html` registrations) are unchanged.

## 5. Known limits and follow-ups (not changed here)

1. **042 pages** (`DocumentUploadPage`, `PlaybookLibraryPage`): only their URL moved to the active backend (one line each, per the POML escalation rule). They still call `acquireBffToken` (CIAM) directly and bypass `bff-client.ts`; on the workforce plane they now **fail closed** (plane mismatch, no token) instead of attempting a CIAM redirect. `SemanticSearch` builds no BFF URL from config. All three are 042's (#1566).
2. **T240c sign-in ordering.** The dev directory is local, so selection runs before sign-in. The real directory needs a caller token (workforce membership lookup; CIAM-authenticated key lookup), i.e. sign-in with the directory's scope first, then a silent token for the customer scope (T240d review R5). The stub throws; whoever builds T240c must add that ordering in `main.tsx`.
3. **Teams picker / "No organization available" screens** render before `adapter.initialize()`, so `notifyAppLoaded`/`notifySuccess` are not sent on those paths. The manifest has no `showLoadingIndicator`, so the content should show; verify in task 071.
4. **No in-app "switch organization" control.** The picker runs at bootstrap when several backends are known and none is active; a link with `?customer={other configured key}` switches. A switch control (and clearing the tab's active key) is a UI follow-up if wanted.
5. `?customer=` stays in the address bar after selection (harmless: it only chooses among configured keys).
6. `getTeamsWorkforceEnvConfig` (deprecated alias) is now unused in `src`; left in place (no refactor beyond the brief).
