# Task 023 — BFF Finance auth closure (@spaarke/auth) + healthz + OBO/User

> Executed 2026-08-14 (worktree `spaarke-wt-code-quality-and-assurance-r3`). FULL rigor, security-critical.
> Owner decision FR-09: @spaarke/auth (NOT HMAC, NOT anonymous).

## What changed (code)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Finance/FinanceRollupEndpoints.cs` | Removed `.AllowAnonymous()` from both recalculate endpoints; added `.RequireAuthorization()` at the group level (mirrors `ScorecardCalculatorEndpoints`). Fixed the misleading "web resources cannot acquire Azure AD tokens" class/inline comments. Added `.ProducesProblem(401)`. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` | `/healthz/dataverse` + `/healthz/dataverse/crud`: added `.AllowAnonymous().RequireRateLimiting("anonymous")` (mirrors the doc sibling). Both handler methods (`TestDataverseConnectionAsync`, `TestDataverseCrudOperationsAsync`) now take `ILogger<Program>`, log the exception, and return a generic detail instead of `ex.Message`. MF-3: `/healthz/dataverse/doc/{id}` (`:93`) no longer echoes `ex.Message`/`ex.InnerException?.Message`. |
| `src/server/api/Sprk.Bff.Api/Api/OBOEndpoints.cs` | Added explicit `.RequireAuthorization()` to all 7 OBO endpoints. |
| `src/server/api/Sprk.Bff.Api/Api/UserEndpoints.cs` | Added explicit `.RequireAuthorization()` to `/api/me` and `/api/me/capabilities`. |
| `src/solutions/webresources/sprk_subgrid_parent_rollup.js` | Migrated the caller to acquire an AAD bearer token via MSAL silent SSO and attach `Authorization: Bearer`. v1.0.0 → v2.0.0. |
| `tests/integration/contract/Api/Finance/FinanceRollupEndpointsContractTests.cs` | New negative test: unauthenticated POST to matter/project recalculate → 401 (compiled into `Sprk.Bff.Api.Tests`). |

## Auth-map GAP #2 check — PASS

Per `notes/bff-auth-surface-map.md` §A.6: bare `.RequireAuthorization()` uses the framework DefaultPolicy over the **default workforce scheme**. No fallback policy exists (§A.2), so the explicit attribute is the sole (correct) enforcement. The named `Ciam` scheme never participates (only `CiamExternal`/`ExternalCollaboration` name it) → a CIAM token to OBO/User/Finance correctly 401s (enforces ADR-028 A1 no-OBO-external). Copilot audience-merge is on the default scheme and unaffected. Used plain `.RequireAuthorization()` (NOT any Ciam-naming policy), matching the Scorecard sibling. No adverse interaction.

## Hard-wall escalation — DID NOT FIRE

The classic web resource CAN acquire the token by a standard means: `/api/config/client` (anonymous) for clientId/authority/scope, MSAL.js v2 loaded from the Microsoft CDN, `acquireTokenSilent` → `ssoSilent` against the signed-in user's Entra session. This is the same silent-SSO flow `@spaarke/auth`'s `BrowserMsalStrategy` uses for Code Pages. The only residual requirements are **deployment config** (SPA redirect URI for the Dataverse org origin on the BFF app-reg + admin consent for `api://{clientId}/user_impersonation`) — not a code wall. So the full close proceeded; `.RequireAuthorization()` was added to Finance.

## Web-resource token mechanism (exact approach) + live-validation caveat

1. `GET {apiBaseUrl}/api/config/client` (anonymous) → `{ msalClientId, msalAuthority, msalScopes }` (cached module-level).
2. MSAL.js v2 UMD loaded once via a dynamically-injected `<script src="https://alcdn.msauth.net/browser/2.38.4/js/msal-browser.min.js">` (classic web resources cannot bundle npm — no repo web resource loads shared libs today, so a CDN script is the standard load path). Global `window.msal`.
3. `PublicClientApplication` config mirrors `BrowserMsalStrategy` INV-1/INV-2: `cacheLocation: "localStorage"`, `storeAuthStateInCookie: true`, `redirectUri: window.location.origin`.
4. Resolution order: `acquireTokenSilent({account})` → `ssoSilent({loginHint})`. Login hint = MSAL cached account username (UPN) → Xrm `userSettings.userName` fallback → undefined (cookie-only). **NO `acquireTokenPopup`** (ADR-028 INV-5 — a form load is not explicit auth intent).
5. On any failure → returns `null`; the caller logs a warning and **skips** the recalculate call (graceful; never throws, never breaks the form).

⚠️ **CANNOT be validated offline** — needs a live Dataverse form/iframe + signed-in session + the app-reg config above. Marked inline in the web resource header and the "BFF AUTHENTICATION" section. `node --check` passes (syntax valid). Implement-and-verify-live is required.

## KPI-caller disposition (MF-1) → HANDED to task 030 / FR-17

The 3 Scorecard-sibling callers send NO Authorization header and hit the **already-authorized** `ScorecardCalculatorEndpoints` — so they are **already 401 today**, independent of this task. This task did NOT regress them (it did not touch the Scorecard endpoints). They are handed to **task 030 / FR-17** rather than migrated here, because:

- Classic web resources cannot share code (no npm import); cleanly reusing the MSAL helper would require either duplicating the ~120-line, offline-unvalidatable flow into 3 more files, or introducing a **new shared web resource** (`sprk_bff_auth.js`) plus per-form dependency registration — a deployment change that can't be authored/validated in this worktree and expands blast radius well beyond task 023's scope.
- Conservative close: migrate only the one caller whose endpoint this task closes; hand the pre-existing-broken 3 to the security horizontal.

**Files for task 030 / FR-17 to migrate** (apply the same MSAL helper, ideally hoisted into a shared `sprk_bff_auth.js` web resource with form-dependency registration):
- `src/solutions/webresources/sprk_matter_kpi_refresh.js` (`_recalculateAndRefresh`, ~:273 fetch)
- `src/solutions/webresources/sprk_kpi_subgrid_refresh.js` (`_recalculateAndRefresh`, ~:298 fetch)
- `src/solutions/webresources/sprk_kpiassessment_quickcreate.js` (`_callCalculatorApi`, ~:271 fetch; note it already sends `credentials: "include"` — insufficient for AAD bearer)

FR-17 recommendation: create ONE shared `sprk_bff_auth.js` exposing `Spaarke.BffAuth.getToken(apiBaseUrl)` + `Spaarke.BffAuth.authenticatedFetch(...)`, register it as a form-library dependency (loaded before each caller), and collapse the per-file MSAL helper (including the copy now inlined in `sprk_subgrid_parent_rollup.js`) into it.

## MF-2 doc rewrite — ORCHESTRATOR ACTION REQUIRED (main-session only)

`.claude/patterns/webresource/subgrid-parent-rollup.md:16` currently MANDATES the anti-pattern
("MUST use `.AllowAnonymous()` because web resources cannot acquire Azure AD tokens"). This is now
FALSE and, if left, the next agent will re-introduce an anonymous Dataverse-write endpoint. Sub-agents
cannot write to `.claude/` (root CLAUDE.md §3) — **the main session must rewrite it** to document the
@spaarke/auth MSAL silent-SSO path implemented in `sprk_subgrid_parent_rollup.js` v2.0.0.

## Verification still owed by the orchestrator (NOT run here — orchestration limits)

- `dotnet build` (Release) + full `dotnet test` green (the new negative test included).
- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; record compressed size + delta vs the **44.96 MB incl PDBs** net10 baseline (ceiling 60 MB). No new NuGet package was added (MSAL is CDN-loaded client-side; the test uses only already-referenced packages).
- `/conflict-check` on the Finance + auth files before the PR (BFF hot path; contested).
- Live smoke of the web-resource token flow on a Dataverse form (see caveat above).
- TASK-INDEX.md 023 → ✅ (orchestrator; not edited here per instructions).
