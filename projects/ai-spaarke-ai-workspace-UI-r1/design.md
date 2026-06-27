# AI SpaarkeAi Workspace UI — R1

> **Status**: Discovery (2026-06-08). Ad-hoc UX/UI improvements project — no spec.md, no full project pipeline.
> **Branch**: master (no worktree per operator decision)
> **Purpose**: Improve ease-of-use, look-and-feel, and consistency across SpaarkeAi widgets and Dashboard sections. 7 objectives spanning Calendar polish, Daily Briefing prefs, Documents grid migration, pinned-persistence test, auth investigation, personal-workspace indicator, and a metrics dashboard.

---

## 1. Operator decisions captured (2026-06-08)

- Project folder at `projects/ai-spaarke-ai-workspace-UI-r1/` with `design.md` + `notes/`. No `spec.md`, no `/project-pipeline`.
- **Objective #4 expansion** — build a *reusable* Dataverse-entity-view widget; instantiate as 4 system widgets: Documents, Projects, Invoices, **Work Assignment** (added to scope).
- **Objective #1 Calendar range UX** — first click = From, second click = To. Remove the form-field From/To date inputs.

---

## 2. Objectives (operator's list)

1. **Calendar widget polish** — filter boxes → dropdowns; spacing; remove From/To date form fields; range via two-click on calendar; subtle color; ensure event grid uses latest dataset grid. *Note: lost prior changes — see §4.1.*
2. **Daily Briefing** — expose preferences, add Save button with save confirmation.
3. **Pinned widgets/workspaces persistence** — test + validate.
4. **Documents widget** — replace card view with the new reusable Dataverse-entity-view widget. Same pattern for Projects, Invoices, Work Assignment.
5. **Auth sign-in on new workspace** — investigate; benign or bug?
6. **Personal-workspace indicator** — small person icon next to user-owned layouts in the Workspaces dropdown.
7. **Metrics dashboard** — use shared UI components; explore VisualHost reuse.

---

## 3. Unifying architectural decision: Pattern D dual-use

The LegalWorkspace dashboard framework **can** embed a Dataverse entity view as a section. `ContentSectionConfig.renderContent: () => ReactNode` accepts any React node. Calendar already does this (`calendar.registration.ts:57`).

So the design unifies around **one widget, two mount paths**:

- **Build once** — `<DataverseEntityViewWidget>` in `@spaarke/ai-widgets`.
- **Mount as Direct widget** — 4 registrations in `WorkspaceWidgetRegistry` (`documents`, `projects`, `invoices`, `work-assignment`). Each = standalone tab via `widget_load` dispatch.
- **Mount as Dashboard section** — 4 thin section shims (60 lines each) returning `ContentSectionConfig` that mounts the same widget. Users can then mix any of them into custom Dashboard layouts via `WorkspaceLayoutWizard`.

Same component, same props contract. Per [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §2.3 + §4 (dual-use Pattern D). This is Calendar's precedent applied four times.

### 3.1 `<DataverseEntityViewWidget>` design

```tsx
<DataverseEntityViewWidget
  entityName="sprk_document"
  viewName="Active Documents"        // OR viewId="<guid>"
  parentContextFilter={...}          // optional matter/project/etc. overlay
/>
```

| Concern | Decision |
|---|---|
| Library home | `@spaarke/ai-widgets` (workspace-bound; uses Xrm frame-walk) |
| Data access | `XrmDataverseClient` (wraps `Xrm.WebApi`); ADR-028-compliant (no Bearer for Dataverse) |
| View resolution | Reuse `ViewService.getViewByName(entity, viewName)` / `getViewById(viewId)` — already in `@spaarke/ui-components/services/ViewService.ts` |
| Grid | Reuse **Spaarke DataGrid Framework** `<DataGrid configId={…} />` — already supports `source.type='savedquery'` |
| Config | Generate `sprk_configjson` at runtime from the resolved saved query — no per-widget Dataverse `sprk_gridconfiguration` row required |
| Frame-walk | Reuse pattern from `WorkspaceLayoutWidget.tsx:91-135` |
| Surface area | ~150 LOC widget + ~50 LOC config generator + 4 × ~15-line registrations |

This is the canonical "shared-lib widget + thin shim" model. No new architecture introduced.

---

## 4. Investigation findings (2026-06-08)

### 4.1 Calendar lost changes (#1)

**Hypothesis** — Task 033b (commit `caf144e5`, 2026-06-03, DataGrid migration) replaced R3 tasks 137–140's Griffel atomic-CSS spacing with **inline `marginRight: 28` styles** on the filter row. Polish wasn't deleted; it was overwritten during the migration.

**Current Calendar state** (`src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`):
- Filter UI (lines 684–785): Event Type, Event Status, Filter-by-Date-Field are **already dropdowns**; From/To are HTML `<Input type="date">`. The user's "convert filter boxes to dropdowns" likely refers to the From/To inputs — which we're removing anyway.
- Event grid (lines 833–842): **already on Spaarke DataGrid Framework** (configId `e15c2b93-a05f-f111-a825-70a8a59455f4`). Objective #1's "latest dataset grid" sub-bullet is **already done**.
- Color treatment: not present.
- Calendar strip: single-click day filter today; needs two-click from/to range.

**Recommended fixes**:
1. Revert task 033b's inline-style regression — restore Griffel tokens for filter-row spacing.
2. Remove the From/To `<Input type="date">` controls.
3. Extend `CalendarSection` to support two-click range mode (first click = From, second = To, third resets).
4. Add subtle color tokens to filter labels + event-card titles (Fluent v9 brand/accent semantic tokens, dark-mode safe).

### 4.2 Documents widget (#4)

Current: 2-col × 10-row CSS grid card view via `DocumentsTab.tsx`. View picker in section title (`DocumentsViewMenuTitle`). Hard 20-card cap with "View all..." overflow.

Change: Replace `DocumentsTab` mount in `documents.registration.ts:328-363` with `<DataverseEntityViewWidget entityName="sprk_document" viewName="Active Documents" />`. The framework's built-in `ViewSelector` replaces the current view picker — fewer LOC, consistent UX.

### 4.3 Daily Briefing preferences (#2)

**No user preferences UI exists today.** Component (`@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/`) has a 5-min TTL in-memory cache and a `loadNotificationContext` callback, but no exposed toggles, no Save button, no persistence target, no save-confirmation UI.

Building this requires:
- New `useDailyBriefingPreferences` hook
- New preferences panel (modal or settings cog)
- A persistence target — **gap**: no `/api/me/preferences` BFF endpoint exists. See §6 open question.
- Save button + Fluent v9 toast via `useToastController()`

**Caveat**: this is the largest scope item. Recommend splitting Phase 1 (localStorage-only) from Phase 2 (BFF/Dataverse persistence).

### 4.4 Personal-workspace indicator (#6)

`WorkspacePaneMenu.tsx` lists layouts via `orderedLayouts` (lines 601–623), each rendered as MenuItem (lines 730–743). **No system-vs-personal differentiation today.** Layout DTO carries `sprk_issystem` (boolean).

Add:
```tsx
{!layout.sprk_issystem && (
  <Tooltip content="Personal workspace" relationship="label">
    <PersonRegular />
  </Tooltip>
)}
```

Slot: right side of layout row, between pin button and active checkmark. Verify `sprk_issystem` is in the BFF response DTO; if not, extend `useWorkspaceLayouts` projection.

### 4.5 Pinned persistence (#3)

Implementation is correct:
- Write: `pinnedWorkspaces.ts:100-121` (`pinWorkspace`)
- Read: `WorkspacePane.tsx:494-544` (cold-load auto-open effect)
- Storage: `localStorage["spaarke:workspace:pinned-list"]` = `[{layoutId, layoutName}, ...]` (ordered)

**Gap**: stale-pin cleanup. If a pinned layout is deleted server-side, the entry persists in localStorage and the auto-open dispatches `widget_load` against a non-existent layoutId.

**Fix**: filter stored pins against the current layouts list (from `useWorkspaceLayouts`) before the auto-open effect dispatches.

**Manual test path**: DevTools → Application → Local Storage → `spaarke:workspace:pinned-list` → pin a layout via the dropdown → verify entry → refresh → verify auto-open → unpin → verify entry removed.

### 4.6 Auth sign-in on new workspace (#5)

**Real bug per ADR-028 INV-5.**

Root cause: `WorkspacePaneMenu.tsx:339-351` launches `WorkspaceLayoutWizard` via `Xrm.Navigation.navigateTo({ ..., target: 2 })` — a popup window. The popup has its own per-origin MSAL localStorage cache, which is empty on first open. `BrowserMsalStrategy.acquire()` (Spaarke.Auth) tries `acquireTokenSilent()` (no accounts) → `ssoSilent()` (may fail without loginHint) → falls back to `acquireTokenPopup()` (line 175-178). That's the sign-in the user sees.

**Recommended fix (Option 1, preferred)** — add `requireSilentOnly: true` flag to `IAuthConfig`. When set, `acquire()` returns null on cache miss instead of falling back to interactive. Let `authenticatedFetch` retry the 401. Apply this flag specifically in `WorkspaceLayoutWizard/src/main.tsx:141-147`.

**Alternative (Option 2)** — convert wizard from popup Code Page to in-page modal/drawer; shares SpaarkeAi auth state, eliminates the cache-isolation issue entirely. Bigger change.

**Document either way**: the cache-isolation popup is an inherent MSAL behavior in popup windows; ADR-028 INV-5 is the binding constraint.

### 4.7 Metrics dashboard via VisualHost (#7)

`VisualHost` IS a Spaarke PCF — `src/client/pcf/VisualHost/`. It renders charts from configuration records (cards + chart-definition rows), part of the DataGrid Framework family. Components:
- `VisualHostRoot.tsx` — composition root
- `ChartRenderer.tsx`, `CardChrome.tsx`, `MetricCardMatrix.tsx` — rendering primitives
- Services: `ConfigurationLoader`, `DataAggregationService`, `FieldPivotService`, `ClickActionHandler`

**Options for objective #7**:

| Option | Approach | Pro | Con |
|---|---|---|---|
| **A (recommended)** | Hoist `ChartRenderer` from PCF-private into `@spaarke/ui-components`; build a thin `<MetricsDashboardWidget>` workspace widget around it; reuse `DataAggregationService` | Full reuse, no PCF mount complexity inside Code Page | Requires confirming `ChartRenderer` has no PCF-only deps |
| **B** | Mount the VisualHost PCF inside a workspace via PCF dataset hosting | Zero refactor | PCF mount inside a Code Page is non-trivial; may not be supported by SpaarkeAi shell |
| **C** | Build parallel widget reusing only services (`DataAggregationService`, configuration records) | Clean separation | Re-implements rendering — duplication |

**Recommendation: Option A.** Step 1 audit `ChartRenderer.tsx` for PCF-only dependencies (manifest props, `ComponentFramework.Context`, etc.). If clean, hoist; if not, factor out the deps and hoist a pure-React variant.

---

## 5. Implementation plan (proposed batches)

### Batch 1 — Quick wins (low risk, no architectural change)

| Item | Files | Effort |
|---|---|---|
| #6 Personal-workspace icon in WorkspacePaneMenu | `WorkspacePaneMenu.tsx` + DTO verify | S |
| #3 Pinned persistence — stale-pin cleanup + manual test note | `WorkspacePane.tsx` cold-load effect | S |
| #5 Auth bug — `requireSilentOnly` flag | `@spaarke/auth/IAuthConfig` + `BrowserMsalStrategy.acquire` + wizard `main.tsx` | M |
| #1 Calendar polish — restore Griffel spacing, remove From/To inputs, add color | `CalendarWorkspaceWidget.tsx` filter row | M |

### Batch 2 — The Dataverse-entity-view widget + Calendar range mode

| Item | Files | Effort |
|---|---|---|
| `<DataverseEntityViewWidget>` build | new file in `@spaarke/ai-widgets`; reuses `ViewService`, `XrmDataverseClient`, `<DataGrid>` | M |
| Direct widget registrations × 4 (Documents, Projects, Invoices, Work Assignment) | `register-workspace-widgets.ts` | S |
| Dashboard section shims × 4 | `src/solutions/LegalWorkspace/src/sections/{documents,projects,invoices,workAssignment}.registration.ts` | S each |
| Documents card-view retirement | `documents.registration.ts:328-363` | S |
| Calendar two-click range mode | `CalendarSection` in `@spaarke/events-components` | M |

### Batch 3 — Metrics dashboard + Daily Briefing preferences

| Item | Files | Effort |
|---|---|---|
| #7 ChartRenderer hoist + `<MetricsDashboardWidget>` | `@spaarke/ui-components` (new), `@spaarke/ai-widgets` (new widget) | L |
| #2 Daily Briefing prefs Phase 1 (localStorage + UI + Save + toast) | `DailyBriefingSection` + new prefs hook | M |
| #2 Daily Briefing prefs Phase 2 (BFF `/api/me/preferences` + Dataverse user-pref entity) | BFF + Dataverse — gate on operator approval | L |

---

## 6. Resolved scope decisions (2026-06-08, operator)

1. **Objective #1 tail** ("fit the…") = **fit the event grid to the widget container** (grid fills remaining vertical space; no overflow above the widget edge).
2. **Daily Briefing preferences** = **Phase 1 + Phase 2 in this project**. Build prefs UI + Save + toast (Phase 1, localStorage) AND the `/api/me/preferences` BFF endpoint + Dataverse user-pref persistence (Phase 2).
3. **Work Assignment entity** = **`sprk_workassignment`** with its default view. Confirm exact view name from Dataverse metadata at build time.
4. **Session pacing** = **work through all three batches in this session**.

### Still open (will resolve at build time)

- Documents view-picker — keep `DocumentsViewMenuTitle` or use framework's built-in `ViewSelector`? (Recommendation: framework's; lower LOC, consistent UX.)
- VisualHost `ChartRenderer` hoist — confirm no PCF-only deps before refactoring out of `src/client/pcf/VisualHost/`.

---

## 7. References

- [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../../docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — two-wrapper model + Pattern D
- [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → render pipeline
- [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) — DataGrid Framework (the latest dataset grid)
- [`LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](../../docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — host contract; informs auth/config patterns
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 (drives #5 fix)
- VisualHost PCF — `src/client/pcf/VisualHost/`
- Calendar Pattern D precedent — `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts`

---

## 8. Changelog

- **2026-06-08** (discovery): Initial publication. Captures operator decisions on structure + Dataverse-view widget + Calendar range UX. Synthesizes 4 parallel investigation findings (Calendar lost-changes audit, Documents+DailyBriefing+PaneMenu+pinned audit, SemanticSearch + Dataverse-view-embed research, Auth-popup investigation). Confirms VisualHost is reusable and Pattern D applies four times.
