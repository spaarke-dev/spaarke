# Subgrid Parent Rollup Pattern

> **Last Reviewed**: 2026-08-14
> **Reviewed By**: code-quality-and-assurance-r3 task 023 (auth closure — MF-2)
> **Status**: Verified
>
> **⚠️ 2026-08-14 auth change (task 023 / spec FR-09 / ADR-028)**: the recalculate endpoints are **no longer anonymous**. The prior guidance that "web resources cannot acquire Azure AD tokens" is **superseded** — the caller acquires a `@spaarke/auth` bearer token via MSAL silent SSO (see "Auth" below). Do **NOT** reintroduce `.AllowAnonymous()` on a Dataverse-write endpoint.

## When
When child records in a subgrid should trigger recalculation of parent record fields (KPI rollups, totals, status aggregation) after Quick Create or edit operations.

## Read These Files
1. `src/solutions/webresources/sprk_subgrid_parent_rollup.js` — complete generic implementation: `onLoad`, `_waitForSubgrid`, `_onSubgridChange`, `_callApiAndRefresh`, JSON config format
2. `src/solutions/webresources/sprk_matter_kpi_refresh.js` — concrete usage example with KPI grades
3. `src/solutions/webresources/sprk_kpi_subgrid_refresh.js` — alternate registration pattern

## Constraints
- **ADR-028 (auth)**: Recalculate endpoints write to Dataverse under an authenticated identity → they MUST use `.RequireAuthorization()` (NOT `.AllowAnonymous()`). The web-resource caller MUST attach a `@spaarke/auth` bearer token (see "Auth"). This supersedes the pre-2026-08-14 guidance.
- **ADR-001**: Recalculate endpoint uses Minimal API `MapPost` + endpoint filters; mirror `ScorecardCalculatorEndpoints` (the `.RequireAuthorization()` sibling).
- **ADR-006**: No legacy JS orchestration logic; rollup trigger lives in web resource, calculation lives in BFF API

## Auth (the caller must send a bearer token — 2026-08-14, task 023)
The classic web resource acquires an AAD access token via MSAL silent SSO and sends `Authorization: Bearer <token>` (reference impl: `sprk_subgrid_parent_rollup.js` v2.0.0):
1. `GET /api/config/client` (anonymous MSAL bootstrap) → `clientId` / `authority` / `scopes`.
2. Load MSAL.js (browser build) + build a `PublicClientApplication` (`localStorage` + `storeAuthStateInCookie`; `redirectUri = window.location.origin`).
3. `acquireTokenSilent` → fall back to `ssoSilent(loginHint)` using the signed-in UPN (from the MSAL account or `Xrm` `userSettings.userName`). **No interactive popup** (ADR-028 INV-5).
4. On token-acquisition failure → skip the API call gracefully (never throw / never break the form).

**Deployment prerequisites** (one-time, per BFF app-registration): (a) an SPA redirect URI for the Dataverse app origin; (b) admin consent for `api://{clientId}/user_impersonation`. The token flow requires a live Dataverse session to validate — it cannot be verified offline.

## Key Rules
- Listener attaches on the **parent form** `OnLoad`, NOT in Quick Create — UCI Quick Create cannot refresh the parent form
- Row count guard (`count !== lastRowCount`) is MANDATORY — without it, `formContext.data.refresh()` re-fires `addOnLoad` causing an infinite loop
- Debounce API calls with `refreshTimer` — rapid subgrid events fire multiple times
- Delay refresh by `refreshDelayMs` (default 1500ms) after API success — Dataverse needs time to commit updated values
- Registration: pass JSON config as event handler parameter string; `subgridName` is the instance key (supports multiple subgrids per form)
- API endpoint uses `.RequireAuthorization()` (auth is the primary control); `RequireRateLimiting` remains as defense-in-depth against abuse
