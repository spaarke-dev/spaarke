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
