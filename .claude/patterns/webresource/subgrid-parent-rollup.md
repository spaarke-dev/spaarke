# Subgrid Parent Rollup Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
When child records in a subgrid should trigger recalculation of parent record fields (KPI rollups, totals, status aggregation) after Quick Create or edit operations.

## Read These Files
1. `src/solutions/webresources/sprk_subgrid_parent_rollup.js` — complete generic implementation: `onLoad`, `_waitForSubgrid`, `_onSubgridChange`, `_callApiAndRefresh`, JSON config format
2. `src/solutions/webresources/sprk_matter_kpi_refresh.js` — concrete usage example with KPI grades
3. `src/solutions/webresources/sprk_kpi_subgrid_refresh.js` — alternate registration pattern

## Constraints
- **ADR-001**: Recalculate endpoint uses Minimal API `MapPost`; MUST use `.AllowAnonymous()` because web resources cannot acquire Azure AD tokens
- **ADR-006**: No legacy JS orchestration logic; rollup trigger lives in web resource, calculation lives in BFF API

## Key Rules
- Listener attaches on the **parent form** `OnLoad`, NOT in Quick Create — UCI Quick Create cannot refresh the parent form
- Row count guard (`count !== lastRowCount`) is MANDATORY — without it, `formContext.data.refresh()` re-fires `addOnLoad` causing an infinite loop
- Debounce API calls with `refreshTimer` — rapid subgrid events fire multiple times
- Delay refresh by `refreshDelayMs` (default 1500ms) after API success — Dataverse needs time to commit updated values
- Registration: pass JSON config as event handler parameter string; `subgridName` is the instance key (supports multiple subgrids per form)
- API endpoint MUST use `RequireRateLimiting` to compensate for anonymous access
