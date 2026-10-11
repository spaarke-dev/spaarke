# Task 042 — Out-of-plane pages and production mock identity (FR-18, #1566): notes

**Date:** 2026-10-11 · **Rigor:** FULL (auth, frontend) · **Status at hand-off:** partial. The upload page, the mock-identity removal and the 401/403 states are done, with tests and checks, but not committed (the main session commits by path). **The POML escalation trigger fired twice** (playbook and search), so those two surfaces are untouched and wait on the owner. POML step 8 (TASK-INDEX / POML status / drift script) was not run: it is outside this agent's edit scope, and the task is not complete.

## 1. Escalation — owner decision needed (STOP trigger, both conditions)

Checked against the BFF source (read-only):

- **Playbook.** The external scoped-read seam (`/api/v1/external/api/dataverse/*`, `ExternalModuleDataEndpoints.cs`) serves only entities with a registered external module (`ExternalAccessModule.cs`): `sprk_project`, `sprk_document`, `sprk_invoice`, `sprk_workassignment`, `sprk_matter`, `sprk_gridconfiguration` and `sprk_servicerequest`. It has **no `sprk_analysisplaybook`**. It is also read-only (fetch / record / metadata / savedquery). `PlaybookLibraryShell` additionally:
  - reads through the OData-shaped `/api/dataverse/{entity}` (`bffDataServiceAdapter`), a different shape from the seam's;
  - creates the analysis through `POST /api/ai/analysis/create` (`components/Playbook/analysisService.ts`), which has no external route.
- **Search.** No search route exists under `/api/v1/external` (everything mounted on `externalGroup`: me, me/entitlements, project data and record documents, `api/dataverse`, messages, contact-grants). `SemanticSearch.tsx` posts to `/api/ai/search` (default scheme).

**Owner question 1 (PlaybookLibraryPage, `/playbooks/:entityType/:entityId`):**
- (a) hide the route on the external host;
- (b) open a BFF task: an external playbook-list read (a module for `sprk_analysisplaybook` or a dedicated route) plus an external analysis-create route.

Recommendation: **(a)**. Nothing in the SPA links to the route, and the page already fails closed on the workforce plane (task 041).

**Owner question 2 (SemanticSearch):**
- (a) delete or hide it;
- (b) open a BFF task for an external, Tier-2-scoped search route.

Recommendation: **(a)**. The component is exported from `components/index.ts` but rendered nowhere (it is not in the bundle: `/api/ai/search` occurs 0 times in `dist`).

Until decided, both files are listed in `PENDING_OWNER_DECISION` in `tests/out-of-plane-routes.test.ts`. That list is a ratchet:
- a new violation anywhere else fails;
- an entry that stops violating fails until it is removed.

The allow-list `ALLOWED` is empty, per POML step 6.

## 2. What changed (all under `src/client/external-spa`)

| File | Change |
|---|---|
| `src/pages/DocumentUploadPage.tsx` | Uploads each file with `bffApiCall` to `POST /api/v1/external/{projects\|matters\|workassignments\|servicerequests}/{id}/documents` (task 031), as multipart field `file`. This uses the active backend's URL and token on both planes. Removed: the page-built `acquireBffToken()` fetch, `getActiveBackend()` during render, and the shared `createBffUploadService` adapter (OBO route). `entityType` must be one of the four logical names, and `entityId` must be a GUID; otherwise an error shows and no request is made. `containerId` is ignored: the server derives the container (#858). A 401 or 403 replaces the wizard with `BffAccessState`; 409 and 422 are per-file messages. Copy and the return target follow the record type (project → `/project/{id}`, others → `/`). New prop `teamsHost`. |
| `src/components/auth/BffAccessState.tsx` (new) | The sign-in state (401) and the denied state (403), plus `accessFailureOf(err)`. Sign-in: in the browser, `captureReturnTo()` + `loginRedirect({ scopes: [getActiveLoginScope()] })`, the same request AuthGuard makes; in Teams, `window.location.reload()` (never a redirect in the iframe, NFR-04). Shows no user. Fluent v9 MessageBar, semantic tokens only. |
| `src/dev-mock.ts` (new) | `isDevMockEnabled()` = `import.meta.env.DEV && VITE_DEV_MOCK === 'true'`, the single mock switch. |
| `src/App.tsx`, `src/main.tsx`, `src/auth/bff-client.ts`, `src/components/AuthGuard.tsx` | Read `isDevMockEnabled()` instead of `VITE_DEV_MOCK`. App passes `teamsHost` to the upload route. |
| `src/api/me-client.ts` | Dev personas ('Sam Rivera', 'Dana Okafor', 'Alex Reyes') now live only inside the dev-mock branch. The production rollout fallback (5xx / network) returns the plane's default modules with an **empty identity** (`PLANE_DEFAULTS`). A **401 or 403 now rejects** instead of falling back. |
| `src/pages/WorkspaceHomePage.tsx` | A 401 or 403 from `/me/entitlements` renders `BffAccessState` (sign-in / denied), not a workspace. |
| `vite.config.ts` | New plugin `spaarke-refuse-dev-mock-in-production` and exported `refuseDevMockInProductionBuild(command, mode, flag)`. `vite build` in production mode with `VITE_DEV_MOCK=true` (from `.env*` or the process environment, via `loadEnv`) throws. |

## 3. Decisions

1. **Two layers for mock mode.**
   - Production builds with the flag **fail** (POML constraint).
   - Every other build **strips** mock mode: `import.meta.env.DEV` is the literal `false` in any `vite build`, so Rollup drops the branches and `mocks/*`.
   - Both layers were proven by builds (§5).
2. **me-client fallback.** It was in scope even though the POML file list does not name it. The built dist contained the three persona names (the old `MOCK_BY_PLANE`, used by the production fallback), and the constraint "a 401/403 shows sign-in/denied; neither shows a mock or default user" applies. The 5xx / network fallback is kept, but without an identity. The rendered app never displayed `me.displayName`, so the only visible change is the 401/403 state.
3. **Scan rules** (`tests/out-of-plane-routes.test.ts`). Comments are stripped first, and every external path token (`/api/v1/external` followed by `/`, a quote or the end) is removed. The scan then flags:
   - any `/api/` not preceded by `.` (so module paths like `../api/x` are skipped);
   - imports of the shared adapters that build non-external routes (`bffDataServiceAdapter`, `bffUploadServiceAdapter`, `PlaybookLibraryShell`, `Playbook/analysisService`);
   - `acquireBffToken`, `acquireActiveBffToken` or a bare or `window.` `fetch(` outside `src/auth/**` and `services/gridDataverseClient.ts` (task 041's external grid client);
   - `VITE_DEV_MOCK` outside `dev-mock.ts` and `vite-env.d.ts`.

   The scanner has 9 must-fire and 9 must-not-fire controls.
4. **On a 401 or 403 the upload stops**: the remaining files would be refused the same way. The page throws to WizardShell, which keeps the wizard from closing, and re-renders the access state.

## 4. Tests

| File | Tests | Covers |
|---|---|---|
| `tests/DocumentUploadPage.externalUpload.test.tsx` (new) | 15 | Real page, WizardShell, FileUploadZone, bff-client and backend selection; fetch and the acquirer are fakes. Covered: the workforce and CIAM planes (URL on the active backend, `Bearer` from the plane's seam, scope `api://{app}/user_impersonation`, multipart field `file`, no Content-Type); all four roots, with only the external route called; `containerId` never sent; 403 → denied, no further file, no user; 401 (request plus retry) → sign-in, button calls `loginRedirect` with the active login scope; Teams → reload, no redirect; no active backend → no token, no request, file reported failed; 409 and 422 per file; unsupported type, malformed id (`../../api/obo`) and missing parameters refused before any request. |
| `tests/out-of-plane-routes.test.ts` (new) | 30 | Source scan (empty allow-list, pending ratchet, DocumentUploadPage clean, scanner controls); production config: the plugin throws for build + production + flag from the environment, allows no-flag and the dev server, plus the 5-case matrix of `refuseDevMockInProductionBuild`. |
| `tests/no-mock-identity.test.tsx` (new) | 9 | Switch on only with DEV and the flag. With `DEV=false` and the flag on (a built bundle), `bffApiCall` reaches the backend and returns no canned data, the App header shows no mock user, and `/me` is live. `/me` 401 and 403 reject; 503 → plane defaults with an empty identity; WorkspaceHomePage 401 → sign-in and 403 → denied. |

**Fail-before.** HEAD sources were copied back and the test files kept; afterwards the sources were restored, checked identical with `cmp`, and all suites re-run green.

- All 8 changed sources at HEAD, with the two new source files removed: upload page 15 / 15 failed; out-of-plane 7 / 30 failed. The failures are the 2 scan tests (DocumentUploadPage clean; no unexpected violation) and the 5 config tests (no guard). The 23 that passed are the scanner controls and the pending-entry check, which test the scanner itself.
- `no-mock-identity` with HEAD consumers and only the new `dev-mock.ts` present: **8 / 9 failed**. The App header showed 'Jane Smith (Mock)', `/me` 401 and 403 resolved to 'Dana Okafor', 503 returned 'Sam Rivera', and no sign-in or denied state appeared. The 1 pass is the switch test itself.
- In total, 30 of the 54 new tests fail before the change; the 24 that pass test the scanner and the switch.

## 5. Command results (src/client/external-spa, 2026-10-11)

| Command | Result |
|---|---|
| `npm run typecheck` | exit 0 |
| `npm run lint` | exit 0, no warnings |
| `npx vitest run` | **14 files, 307 passed** (baseline 11 files, 253; +54) |
| `npm run build` | exit 0 (only the existing > 500 kB chunk warning) |
| `dist/` grep | 0 occurrences each of: `Jane Smith`, `externalfirm`, `(Mock)`, `Sam Rivera`, `Dana Okafor`, `Alex Reyes`, `dv_persona`, `VITE_DEV_MOCK`, `mock-contact-001`, `mock-project-001`, `Acme Corp Acquisition`, `Meridian Restructuring`, `/api/ai/search`, `/api/obo/`, `/api/documents/upload`, `/api/containers/`. The dist before this task had `Sam Rivera` / `Dana Okafor` / `Alex Reyes` (1 each). `/api/dataverse` (6) is `/api/v1/external/api/dataverse` plus the shared `BffDataverseClient` suffix appended to `{host}/api/v1/external`, and `bffDataServiceAdapter` (PlaybookLibraryPage, pending). |
| `VITE_DEV_MOCK=true npx vite build` (production) | **exit 1**: "VITE_DEV_MOCK=true is refused for a production build …"; no output written |
| `VITE_DEV_MOCK=true npx vite build --mode staging` (scratch outDir) | exit 0; 0 occurrences of every mock marker (the strip layer) |
| `prettier --check` (changed files) | clean after `--write` (App.tsx was formatted at HEAD; only the changed line moved) |
| `dotnet test tests/Spaarke.ArchTests --filter ExternalSpaGridViewSelectorGuardTests` (repo root) | 220 / 220 passed |

## 6. Known limits and follow-ups

1. **PlaybookLibraryPage and SemanticSearch are unchanged**, pending §1.
   - PlaybookLibraryPage still builds its own `acquireBffToken()` fetch and calls `getActiveBackend()` during render.
   - Both still name non-external routes.
   - The scan holds them in `PENDING_OWNER_DECISION`.
2. **`usePlaybookExecution` posts to `/api/v1/external/ai/playbook`, which no BFF route maps** (searched `Sprk.Bff.Api`). It sits in the external prefix, so the scan does not flag it. Its only consumer, `AiToolbar`, is rendered nowhere. This should be reported or filed with the playbook decision.
3. **`me-client` with no active backend** falls back to plane defaults (a non-ApiError). This was unchanged here. The bootstrap always activates a backend before App mounts, and every module data call fails closed without one.
4. **Teams sign-in-again reloads the tab.** That re-runs the Teams bootstrap (NAA, then the popup screen). There is no in-place popup from the page, because the adapter is not reachable from routes. To verify in task 071.
5. **UI tests (POML ui-tests)** — upload as a partner, forbidden handling and dark mode were not run (no deploy). They belong with task 070/072. `BffAccessState` uses only Fluent MessageBar and Button with no custom colours.
6. Also noticed and not changed: the comment above `server.proxy` in `vite.config.ts` (task 041 notes) still describes the pre-041 relative-path behaviour.

## 7. Owner live steps

None. This is client-only; there is no app registration, schema or deploy change.

## Review results and fixes (2026-10-11)

All paths under `src/client/external-spa` unless stated. "Fail-before" = the same tests run against the HEAD versions of the changed `src/` files and `vite.config.ts`: **36 failed / 139** in the five affected test files, plus **13 failed / 43** in `tests/out-of-plane-routes.test.ts`. After the fixes: 17 files, **397 passed** (was 14 files, 307).

### Pass 1

| # | Finding | Fix | Tests (fail-before) |
|---|---|---|---|
| F | The mock guard refused only `mode === 'production'`, and the "`import.meta.env.DEV` is false in every vite build" comment was wrong: Vite derives DEV from NODE_ENV. `NODE_ENV=development VITE_DEV_MOCK=true npx vite build --mode staging` shipped Jane Smith / Sam Rivera / mock-project-001. | `vite.config.ts`: `refuseDevMockInBuild(command, flag)` refuses EVERY `build` with `VITE_DEV_MOCK === 'true'`; `devMockBuildDefine` makes every build `define` `import.meta.env.VITE_DEV_MOCK` as `"false"`. `src/dev-mock.ts` compares with the same exact `=== 'true'` (a `String().toLowerCase()` form defeated constant folding: the first attempt still shipped the mock data with the define in place). Both comments corrected. | `out-of-plane-routes.test.ts`: staging / development / test / preview modes, the NODE_ENV=development case, the define result, a 12-row matrix (13 failed before); `no-mock-identity.test.tsx` "honours the exact value true only". Real builds: `NODE_ENV=development VITE_DEV_MOCK=true vite build --mode staging` now exits 1 with the refusal. With the refusal temporarily disabled, the define alone drops every marker (0 hits). Before: the same command produced a bundle with Jane Smith (2), Sam Rivera, Dana Okafor, Alex Reyes, mock-project-001 (18). |
| K3 | No check on the built output. | `scripts/check-dist.mjs` + `npm run check:dist`; a step right after `npm run build` in `.github/workflows/deploy-external-spa.yml` (no workflow value used). Scans `dist/**/*.js` for the mock markers and for `/api/` strings outside `/api/v1/external`, with a ratchet baseline of ONLY today's pending strings: `/api/ai/analysis/create` (PlaybookLibraryShell) and the `/api/dataverse/` family (`bffDataServiceAdapter`: `/api/dataverse/`, `fetch`, `metadata/`, `record/`, `savedqueries/`, `savedquery/`, one each). A new string fails; a baseline entry that disappears or shrinks fails too. `VITE_DEV_MOCK:"false"` (the pinned constant inside Vite's `import.meta.env` object, new since the define) is the one tolerated mention. | `tests/check-dist.test.ts` (37 tests: every marker fires, tolerated constant and lookalikes do not, the ratchet in both directions) and a workflow-order test in `production-package.test.ts`. Fail-before on a real bundle: run against a bundle built from the HEAD config with the staging + NODE_ENV=development + flag command, it reports Jane Smith, Sam Rivera, Dana Okafor, Alex Reyes, dv_persona, VITE_DEV_MOCK, mock-contact-001, mock-project-001 and (Mock). Against the current build it reports clean. |
| K2 | `fetchMeEntitlements` defaulted to the plane's modules for ANY non-401/403 failure, including token-acquisition errors and network faults. | `api/me-client.ts` defaults ONLY for an `ApiError` with `statusCode >= 500`; everything else rejects. `accessFailureOf` now maps a `TeamsWorkforceAuthError` to the sign-in state (a failed Teams sign-in is a sign-in problem); a network fault, a 4xx and `BackendSelectionError` reach the workspace's "Unable to load your workspace" state. | `no-mock-identity.test.tsx`: 500/502/503 default; 404, network fault, TeamsWorkforceAuthError, redirect-in-progress Error, BackendSelectionError reject; the page shows the sign-in state for a Teams auth failure and the load error for a network fault. `BffAccessState.signIn.test.tsx` `accessFailureOf`. |
| K | A 401/403 part-way through a batch hid which files had already uploaded; a batch with nothing uploaded still showed the green checkmark. | `pages/DocumentUploadPage.tsx`: the refusal view lists "Already uploaded before this stopped" and "Not uploaded" beside the sign-in / denied state; `successCount === 0` ends on a `WarningRegular`. | `DocumentUploadPage.externalUpload.test.tsx`: 401 after two files, 403 after one file and a first-file 403, warning vs checkmark; the pinned "no active backend" test now asserts the warning icon and no success icon. |

### Pass 2

| # | Finding | Fix | Tests (fail-before) |
|---|---|---|---|
| F | `uploadFailureMessage` mapped EVERY 409 to "a file named X already exists", but the server also returns 409 `RecordOwnerRefused` ("Record owner unresolved"), and the detail of 503 (`sdap.access.service_request_unreadable`), 400, 413 and 502 was dropped. The same mapping sat in `components/DocumentLibrary.tsx` `UploadDialog`, the path real users take. | Both route a non-auth failure through `problemMessage(err, fallback)` (`auth/bff-client.ts`): the server's ProblemDetails `detail` is shown; the per-status text (409 conflict, 413 too large, 422 no storage, 403 access level, 5xx try again later) is only the fallback for a response with no detail. The server's collision 409 detail already names the file. | `DocumentUploadPage.externalUpload.test.tsx` and the new `DocumentLibrary.uploadFailures.test.tsx`: owner-refusal 409 shows the server detail and never "already exists"; the collision 409 still names the file; 503 / 400 / 413 / 502 show their detail; empty or non-JSON bodies use each status's fallback. |
| K3 | Sign-in loop: bff-client's 401 "fresh token" retry asked the acquirer for the same cached token the BFF had just refused. | The acquirer seam takes `(scope, { forceRefresh })`. Only the 401 retry (`bff-client.ts`, and the identical retry in `services/gridDataverseClient.ts`) passes `{ forceRefresh: true }`; the first attempt still calls `acquirer(scope)`. `forceRefresh: true` reaches the CIAM and workforce silent request (and the Teams NAA / popup silent calls) and is added to the request ONLY then, so the 040 request shapes are unchanged (the existing workforce-auth `toStrictEqual` shape tests still pass). "Sign in again" on the browser plane uses `prompt: 'select_account'`. `handleSignIn` is guarded: a `BackendSelectionError` from `getActiveLoginScope()` or a rejected `loginRedirect` shows "Sign-in could not be started from here" instead of a dead button. | `workforce-auth.test.ts` (CIAM, workforce plane seam and Teams NAA forced refresh, and that normal calls carry no key), `DocumentUploadPage.externalUpload.test.tsx` (acquirer calls are exactly `[scope]` then `[scope, {forceRefresh:true}]`; `select_account`), `BffAccessState.signIn.test.tsx` (4 sign-in tests). |

Notes for the next reader:
- `npm run check:dist` does not flag the `/api/shorthands#...` documentation URLs Fluent includes in a NODE_ENV=development bundle. Only a development-mode build has them; the deploy workflow builds in production mode.
- The old known limit 3 above (me-client with no active backend falls back to plane defaults) is superseded: a `BackendSelectionError` now rejects.
- The `dist/` grep row in section 5 said `VITE_DEV_MOCK` has 0 occurrences. Since the build define it has 1: `VITE_DEV_MOCK:"false"` inside Vite's env object. `check:dist` accepts exactly that.

### Known limits NOT fixed

1. `/upload` has no link in the app. Users upload through DocumentLibrary's dialog; the POML ui-test "upload from the upload page" needs a link or a typed URL.
2. The pending-file ratchet in the source scan (`PENDING_OWNER_DECISION`) is per file, not per string, so a new violation in PlaybookLibraryPage or SemanticSearch is not caught by it. `check:dist` covers the built output per string.
3. `usePlaybookExecution` posts to `/api/v1/external/ai/playbook`, which no BFF route maps. It goes with the playbook decision.
4. "Access denied" for unresolved callers on dev is the intended FR-18 change.
5. The playbook page and SemanticSearch are unchanged (owner decision pending).

### Command results (2026-10-11)

| Command | Result |
|---|---|
| `npm run typecheck` | exit 0 |
| `npm run lint` | exit 0 |
| `npx vitest run` | 17 files, 397 passed |
| `npm run build` | exit 0 (only the > 500 kB chunk warning) |
| `npm run check:dist` | exit 0: 1 file clean, baseline matches |
| `prettier --check` (changed and new files) | clean |
| `dotnet test tests/Spaarke.ArchTests --filter ExternalSpaGridViewSelectorGuardTests` | 220 / 220 passed |
| workflow parsed with js-yaml | steps in order: build, check:dist, stage SWA config |
