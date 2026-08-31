# Side-Pane Navigation & Quick-Access (Navigation History) — r1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-12
> **Source**: `design.md` (owner-approved 2026-08-12; Path B)
> **Project slug**: `spaarke-side-pane-navigation-history-r1`

## Executive Summary

Build a **reusable side-pane framework** (host + registry + thin contributors) for the model-driven app (MDA) shell, and ship **Navigation History ("Navigator")** as its first tenant: a single, always-available docked pane giving one-gesture access from anywhere to **Recent** (Viewed / Edited), **Pinned** (records + bookmarks + a shared Monitored lens), and **Views** — all cross-device via a new per-user `sprk_navitem` entity. Capture runs with **zero per-form web resources and zero Dataverse plugins** by exploiting a persistent app-level side pane that polls `Xrm.Utility.getPageContext()`. **No `Sprk.Bff.Api` code is added** — all data access is host-context `Xrm.WebApi` under the signed-in user.

## Scope

### In Scope
- **`SprkSidePaneHost` + `sidePaneRegistry`** — a reusable docked side-pane framework that **wraps `SidePaneShell` for chrome** and **generalizes `DataGridSidePaneOrchestrator`** for the host logic (createPane, mutual-exclusion, IntersectionObserver close-on-navigate, unload cleanup), owning `Xrm.App.sidePanes` plumbing, right-rail icons, theming, and a registry seam future `spaarke-side-pane-*` features contribute to. (OQ-9 resolved in pipeline Step 2: `SidePaneShell` is presentational-only → wrap, don't extend.)
- **App-startup bootstrap (Path B)** — productionize the recovered `SidePaneManager` **Code Page-injection** path so the pane auto-creates at app load and capture runs from login. (Ribbon enable-rule path is NOT used.)
- **`NavigatorPane` contributor** — 3 tabs + a persistent top search bar:
  - **Recent** — Viewed (passive capture) / Edited (`modifiedby=me` derivation) segmented toggle.
  - **Pinned** — per-user pins in two groups (**Records**, **Bookmarks**) + **"+ Add bookmark"**, plus a distinct **Monitored** group (shared `sprk_monitor` flag, scoped to me).
  - **Views** — live aggregation of the user's saved `userquery` views.
  - **Search bar / quick-switcher** — local fuzzy-match across Recent/Pinned/Views, escalating to live Dataverse lookup, focusable via a keyboard accelerator.
- **`sprk_navitem` entity** — per-user (owner-scoped) store for history rows + pins + bookmarks, with **prune-on-write 30-day retention**.
- **Capture engine** — persistent-pane `getPageContext()` polling → `history` upserts (re-adopts the recovered `contextService` polling loop).
- **Read-time security trimming** — never render a cached name for a record the user can no longer access.

### Out of Scope
- **Change-notification / subscription** ("notify me when this record changes") — N1; fuses with the notification spine later.
- **Field-level audit viewer** ("who changed what field") and the `audit` entity route — N2; rejected.
- **Team activity feed** — N3; scope is me-and-my-activity.
- **A new query builder / rebuilding saved views** — N4; we aggregate existing `userquery`.
- **Browser add-in / browser bookmark-system integration** — N5; our bookmark store is server-side (`sprk_navitem`), not the browser's.
- **Standalone external SPA (external-access) capture** — N6; runs outside the MDA shell, no `Xrm.App`.
- **SpaarkeAi dashboard "Recent & Pinned" widget** — deferred to phase 2 (not built in r1 per owner, 2026-08-12). The Navigator body is still authored as a shared component so a later project can register it as a dashboard widget without rework, but no widget registration ships in r1.
- **Phase-2 items**: SpaarkeAi dashboard widget (above), full-page management view, folders/tags on pins, "back to previous record" navigation *stack*, external-SPA capture, change-notification fusion.

### Affected Areas
- `src/client/shared/Spaarke.UI.Components/src/components/SidePane/` — new `SprkSidePaneHost` (wraps `SidePaneShell`) + `sidePaneRegistry` seam.
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/sidePane/DataGridSidePaneOrchestrator.ts` — generalize its lifecycle logic into `SprkSidePaneHost` (do NOT reinvent createPane/close-on-navigate).
- `src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts` — widen `SidePanesApi` typings (`getPane`/`pane.select`, `webresourceName`) + `getXrm()` to a 3-frame walk (window→parent→top).
- `src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts` — reuse typed `Xrm.App.sidePanes` iface + `getXrm()` frame-walk.
- New code page / solution for the `NavigatorPane` bundle + the app-startup bootstrap (Path B injection).
- New Dataverse entity `sprk_navitem` (schema, security roles).
- `notes/retired-sidepane-code/` — `SidePaneManager.ts` (bootstrap + registry shape), `contextService.ts` (polling capture loop) — re-adopt.
- **`Sprk.Bff.Api` — NOT touched.**
- **`src/solutions/SpaarkeAi/**` and `Spaarke.AI.Widgets` — NOT touched** (no dashboard widget in r1; deferred to phase 2).

## Requirements

### Functional Requirements

1. **FR-00 (SPIKE — gate)**: Re-validate + productionize the recovered Code Page-injection bootstrap (`notes/retired-sidepane-code/SidePaneManager.ts`) on the **current UCI build**: the app-level pane auto-creates at app load, persists across record navigation (incl. while collapsed, via `alwaysRender: true`), and `getPageContext()` polling reliably observes record visits. — **Acceptance**: on the target MDA build, a pane created at app startup survives ≥5 record navigations and ≥1 collapse/expand cycle with JS still running and captures each visit. **Go/no-go gate**: if this fails, fall back to Path A (launch-on-open; capture starts at first-open) and re-baseline FR-02/FR-03.

2. **FR-01**: `SprkSidePaneHost` + `sidePaneRegistry` — a shared docked side-pane host that renders the active contributor selected from a static registry (`{ id, icon, title, order, component }`); one registry entry = one right-rail icon = one job. — **Acceptance**: host renders from registry entries; adding/removing an entry adds/removes exactly one rail icon; theming honors light/dark + `--sprk-ui-scale`.

3. **FR-02**: App-startup bootstrap creates the app-level Navigator pane once per app load via `Xrm.App.sidePanes.createPane` with `canClose:false` + `alwaysRender:true`; idempotent (singleton guard); frame-walk `getXrm()`. — **Acceptance**: pane present in the rail on every app load without user action; re-entry does not create duplicates.

4. **FR-03**: Recent (Viewed) capture — a polling loop (~0.5–2s) reads `Xrm.Utility.getPageContext()`; on page change, upsert a `history` `sprk_navitem` (dedupe by target, bump `sprk_lastvisited`/`sprk_visitcount`). Derived "current page" is recomputed each poll (never cached authoritative); leaving records sets current = null. — **Acceptance**: navigating Matter→Project→Document produces three ordered history rows via `Xrm.WebApi`; no per-form handler exists; capture continues while pane is collapsed.

5. **FR-04**: Recent (Edited) derivation — query `modifiedby eq me order by modifiedon desc` across a **fixed core entity set** (`sprk_matter`, `sprk_project`, `sprk_document`, `sprk_todo`, `sprk_event`, `sprk_communication` — final list confirmed in pipeline), merged + sorted client-side in the pane. No OnSave hook, no audit entity, no plugin. — **Acceptance**: Edited toggle shows the user's recently-modified records across the core set merged by `modifiedon` desc; a record edited via a flow (not the UI) still appears.

6. **FR-05**: Retention — **prune-on-write, 30-day age**: on each history upsert, delete the user's `history` rows older than 30 days. Pins/bookmarks never auto-expire. No scheduled job, no plugin. — **Acceptance**: a history row older than 30 days is gone after the next capture write; pins persist indefinitely.

7. **FR-06**: `sprk_navitem` entity — per-user (`ownerid`-scoped, private) with fields per the data model below. — **Acceptance**: users see only their own rows (standard Dataverse ownership + roles); entity supports history + pin + bookmark + weblink shapes.

8. **FR-07**: Pin gesture — inline star in any pane row creates/removes a **per-user** `sprk_type=pin` `sprk_navitem`; pins are unaffected by any other user and never write `sprk_monitor`. — **Acceptance**: starring a record adds it to the user's Pinned→Records group; another user toggling `sprk_monitor` does not add/remove it.

9. **FR-08**: Bookmarks — two gestures both writing `sprk_type=pin`:
   - **"Pin this page"** (one click) — captures current page from `getPageContext()` (`sprk_source=captured`) as a logical target.
   - **"+ Add bookmark"** (manual) — pastes/types a URL (`sprk_source=manual`); **parse MDA record + entitylist/view URLs** (`etn`/`id`/`pagetype`/`viewid`) into a labeled logical target; **anything that doesn't parse** (external sites, SharePoint docs, custom pages, dashboards) stored raw as `sprk_url` (`sprk_pagetype=weblink`) opening in a new tab.
   — **Acceptance**: "Pin this page" bookmarks the current page with no typing; a pasted MDA record/view URL becomes a labeled, security-trimmable logical target; a pasted external URL is stored raw and opens in a new tab.

10. **FR-09**: Monitored lens (**in r1**) — a distinct Pinned-tab group surfacing the shared record-level `sprk_monitor` flag scoped to the user (`monitor=true AND owned-by/assigned-to me`), visually separate from personal pins, with the shared-flag semantics surfaced in the UI (setting/clearing `monitor` affects everyone; last-writer-wins). — **Acceptance**: the Monitored group lists the user's monitor-flagged records; it is never merged with personal pins; toggling `monitor` elsewhere changes the group.

11. **FR-10**: Views tab — live `Xrm.WebApi.retrieveMultipleRecords('userquery', owner=me)` grouped by `returnedtypecode`; click → `Xrm.Navigation.navigateTo({pageType:'entitylist', entityName, viewId})`; system `savedquery` views are opt-in/pin-only. No new query storage. — **Acceptance**: the tab lists the user's saved views grouped by entity; clicking opens the entity grid with that view selected.

12. **FR-11**: Search bar / quick-switcher — a persistent top-of-pane search box; local fuzzy-match across Recent/Pinned/Views first (instant), escalating to a live Dataverse record/view lookup on no local hit; **Enter navigates to the top result**; a keyboard shortcut focuses the box from anywhere. — **Acceptance**: typing filters local entries live; a query with no local hit finds a live Dataverse record/view; the accelerator focuses the box; Enter navigates.

13. **FR-12**: Read-time security trimming — on render, re-validate access to each history/pin target via a lightweight retrieve; drop/blank rows that 403/404 so a cached name for a now-inaccessible record is never shown. — **Acceptance**: a record the user has lost access to does not display its cached name in history/pins.

14. **FR-13**: Framework-proof — a second (stub) contributor registers against `SprkSidePaneHost` with only `{ id, icon, title, component }`. — **Acceptance**: the stub appears as its own rail icon and renders, with no host code changes.

### Non-Functional Requirements
- **NFR-01 (Zero BFF)**: No code added to `Sprk.Bff.Api` (no endpoint, service, DI, package, background job). All data access host-context `Xrm.WebApi`. Publish-size / CVE / BFF-test obligations **N/A**.
- **NFR-02 (Zero form/plugin burden)**: No per-form OnLoad/OnSave web resources; no Dataverse plugins. The only global JS is the single app-startup bootstrap (FR-02).
- **NFR-03 (Per-user isolation)**: `sprk_navitem` is owner-scoped; no cross-user visibility beyond standard Dataverse security.
- **NFR-04 (Security-trimmed rendering — legal context)**: cached labels for inaccessible records never render (FR-12).
- **NFR-05 (Persistence)**: pane persists across navigation and while collapsed (`alwaysRender:true`); capture unaffected by collapse.
- **NFR-06 (Cross-device)**: history/pins are server-side; visible on another device after sign-in.
- **NFR-07 (Theming/a11y)**: honors light/dark + `--sprk-ui-scale` via a scaled Fluent theme; keyboard-accessible search + navigation.
- **NFR-08 (Reuse)**: wrap `SidePaneShell` for chrome; generalize `DataGridSidePaneOrchestrator` for pane lifecycle; re-adopt retired `contextService.ts` for capture; reuse `ViewService.ts` (userquery), `useSprkMemoRepository`-style `Xrm.WebApi` CRUD, `xrmContext.ts` `getXrm()`, `scaledTheme`/`useUiScale` — no parallel implementations. Author the Navigator body as a shared component so a phase-2 dashboard widget can reuse it without rework.

## Technical Constraints

### Applicable ADRs
- **ADR-006** (PCF over web resources / minimize form web resources) — the pane **body** ships as a code-page bundle hosted as a `webresource` pane (`pageType:'webresource'`, like the shipping `CalendarSidePane`); no per-form handlers. See ADR Tensions.
- **ADR-022** (PCF platform libraries, React 16/17) — applies only if any part ships as a PCF; primary surface is a code page (React 19). Watch shared-lib React-version drift in `@spaarke/ui-components`.
- **ADR-030** (PaneEventBus) — not needed in r1 (the cross-surface widget that would use it is deferred to phase 2); listed only so the phase-2 dashboard widget reuses it rather than adding a new bus.

### MUST Rules
- ✅ MUST access all data host-context via `Xrm.WebApi` under the signed-in user (per `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`).
- ✅ MUST NOT add code to `Sprk.Bff.Api`.
- ✅ MUST NOT add per-form web resources or Dataverse plugins.
- ✅ MUST NOT use the `audit` entity.
- ✅ MUST NOT write `sprk_monitor` from the pin/star gesture (personal pins are `sprk_navitem` only).
- ✅ MUST security-trim cached labels at render time.
- ✅ MUST create the pane via `Xrm.App.sidePanes.createPane` with `canClose:false` + `alwaysRender:true`.

### Existing Patterns to Follow
- `src/client/shared/Spaarke.UI.Components/src/components/SidePane/SidePaneShell.tsx` — presentational chrome; `SprkSidePaneHost` **wraps** it.
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/sidePane/DataGridSidePaneOrchestrator.ts` — the de-facto pane host to generalize (idempotent createPane, `mutuallyExclusiveWith`, `attachVisibilityLifecycle` IntersectionObserver close-on-navigate, unload cleanup).
- `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` — registry ergonomics precedent (Map + lazy factory + metadata); `src/client/shared/Spaarke.UI.Components/src/services/surfaceHandoff/surfaceLaunchRegistry.ts` — code-owned static-table precedent.
- `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts` — closest `Xrm.WebApi` CRUD analog for `sprk_navitem` (retrieveMultiple/create/debounced-update + frame-walk).
- `src/solutions/EventDetailSidePane/src/services/eventService.ts` — side-pane `Xrm.WebApi` CRUD (scalar vs `@odata.bind` PATCH split, error parse).
- `src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts` — `userquery`/`savedquery` querying for the Views tab (FR-10 — reuse, don't re-query).
- `src/solutions/CalendarSidePane/`, `src/solutions/EventDetailSidePane/` — Vite code-page-as-`webresource`-pane reference impls.
- `src/client/shared/Spaarke.UI.Components/src/components/SprkModal/scaledTheme.ts` + `src/hooks/useUiScale.ts` — `--sprk-ui-scale` wiring (NFR-07).
- `projects/spaarke-side-pane-navigation-history-r1/notes/retired-sidepane-code/` — `SidePaneManager.ts` (PANE_REGISTRY + bootstrap), `contextService.ts` (2s poll + sessionStorage capture loop), `ContextSwitchDialog.tsx` (reference only — Navigator captures silently).
- `src/client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts` — typed `Xrm.App.sidePanes` + `getXrm()`.
- `notes/retired-sidepane-code/SidePaneManager.ts` — bootstrap + static `PANE_REGISTRY` (adopt as `sidePaneRegistry`).
- `notes/retired-sidepane-code/contextService.ts` — polling capture loop (re-adopt).
- Registry-shape precedent: `WorkspaceWidgetRegistry`, `surfaceLaunchRegistry`, `SprkModal` presets.

## Data Model — `sprk_navitem` (per-user)

| Field | Type | Purpose |
|---|---|---|
| `sprk_navitemid` | PK | primary key |
| `ownerid` | Owner | per-user scope (private) |
| `sprk_type` | Choice | `history` / `pin` |
| `sprk_source` | Choice | `captured` / `manual` |
| `sprk_targetlogicalname` | Text | e.g. `sprk_matter`, `custompage` (nullable for raw-URL links) |
| `sprk_targetid` | Text/GUID | record id (nullable for pages/links) |
| `sprk_pagetype` | Choice | `entityrecord` / `entitylist` / `custom` / `weblink` |
| `sprk_url` | Text | raw URL for manual bookmarks that don't parse (weblink) |
| `sprk_displayname` | Text | resolved or user-supplied label |
| `sprk_lastvisited` | DateTime | ordering + 30-day retention |
| `sprk_visitcount` | Whole Number | optional dedupe/rank |

> Phase-2 extensibility (omitted now): folders/tags on pins; a `sprk_querydefinition` JSON field only if cross-entity/workspace queries emerge.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>N</bff>                <!-- no Sprk.Bff.Api changes -->
  <spaarkeai>N</spaarkeai>    <!-- no dashboard widget in r1; deferred to phase 2 -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
> BFF=N → §10 publish-size / CVE / BFF-test obligations are N/A. SpaarkeAi=N → the dashboard widget is deferred to phase 2, so this project touches neither `src/solutions/SpaarkeAi/**` nor `Spaarke.AI.Widgets`. **Revisit this declaration if de-risking forces a BFF path** (e.g., external-SPA capture or a server-side retention job) before that code lands.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `SprkSidePaneHost` + `sidePaneRegistry` | `SidePaneShell.tsx` (chrome, presentational-only — confirmed Step 2); `DataGridSidePaneOrchestrator.ts` (de-facto pane host: createPane + mutual-exclusion + IntersectionObserver close-on-navigate + unload cleanup); `WorkspaceWidgetRegistry`/`surfaceLaunchRegistry` (registry precedents); typed `Xrm.App.sidePanes` + `getXrm()` in `xrmContext.ts` | **Yes — wrap `SidePaneShell` + generalize `DataGridSidePaneOrchestrator`** (its own docblock notes it was already lifted from EventsPage to be pane-agnostic); the app-wide multi-contributor registry seam is the genuinely new part | Every future quick-access feature re-implements pane plumbing → divergent panes (the retired `SidePaneManager` is the cautionary example) |
| `sprk_navitem` (new entity) | Native MDA MRU (client-side, not queryable/cross-device); `sprk_monitor` (record-scoped, records-only, shared) | **No** — `sprk_monitor` can't hold pages/views/history and isn't per-user; native MRU isn't server-side | No cross-device history and no page/view pins → the core user need (§1) fails |
| `NavigatorPane` contributor | none (the feature itself) | n/a — it *is* the first tenant extending the host | No Navigator surface exists |

### Placement Justification (BFF)
N/A — **no BFF code**. Data access is personal, single-entity, host-context `Xrm.WebApi` (`sprk_navitem`, `userquery`, `sprk_monitor` reads, security-trim retrieves) — the textbook host-context case in `DATA-ACCESS-DECISION-CRITERIA.md`.

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-006 | "PCF over web resources / minimize form web resources" | The pane body ships as a code-page bundle hosted as a `webresource` pane (`pageType:'webresource'`), and one global JS bootstrap is injected at app load | **C (comply)** | A code page shipped as a web-resource pane is not a "classic HTML web resource" nor a per-form handler — it matches the shipping `CalendarSidePane` pattern; the single app-startup bootstrap is the one global JS, distinct in kind from the per-form handlers we avoid. Flag for `adr-check` to confirm. |
| ADR-022 | "PCF platform libraries (React 16/17)" | Primary surface is a React-19 code page, not a PCF | **C (comply)** | ADR-022 governs PCF only; if any on-form PCF entry point is added it will honor React-16 APIs. Watch `@spaarke/ui-components` shared-lib React-version drift. |
| Superseded side-pane platform (§3a) | The global auto-injection bootstrap was retired as fragile | Reviving it (Path B) revisits a retired mechanism | **A (project-scoped exception, owner-approved 2026-08-12)** | Retirement was **product-driven** (no global embedded AI agent), not a technical failure; the reliable path was Code Page injection (recovered), and FR-00 re-validates it on current UCI with Path A as documented fallback. |

## Success Criteria
1. [ ] From any MDA page, the docked Navigator opens in one click and Recent (Viewed) is populated by navigation with **no form code deployed** — Verify: navigate 3 records, confirm 3 history rows via `Xrm.WebApi`; confirm no form web resource exists.
2. [ ] Recent → Edited shows records the user recently modified, derived from `modifiedon` across the core set, **no audit entity** — Verify: edit a record via flow, confirm it appears.
3. [ ] Starring a record creates a **per-user** pin unaffected by other users; `sprk_monitor` and its on-form toggle are independent — Verify: two-user test.
4. [ ] Pinning a page/view stores a `sprk_navitem` that survives sign-out and appears on another device — Verify: cross-device sign-in.
5. [ ] "Pin this page" bookmarks the current page with no typing; "+ Add bookmark" parses an MDA record/view URL to a labeled logical target and stores a non-Dataverse URL raw (opens new tab) — Verify: three bookmark inputs.
6. [ ] The Monitored group lists the user's monitor-flagged records, distinct from personal pins — Verify: flag a record, confirm it appears only in Monitored.
7. [ ] History older than 30 days is pruned on the next capture write — Verify: seed an old row, capture once, confirm deletion.
8. [ ] The search box fuzzy-matches Recent/Pinned/Views live and finds a live Dataverse record/view on no local hit; the accelerator focuses it; Enter navigates — Verify: typed queries + keyboard.
9. [ ] The Views tab lists saved views grouped by entity; click opens the grid with that view — Verify: click a `userquery`.
10. [ ] A record the user has lost access to does not display its cached name — Verify: revoke access, confirm blank/hidden row.
11. [ ] A second stub contributor registers with only `{ id, icon, title, component }` and renders — Verify: add stub, confirm one new rail icon, no host edits.
12. [ ] No changes to `Sprk.Bff.Api`; no plugin; no per-form web resource — Verify: `git diff` scope + `adr-check`.

## Dependencies

### Prerequisites
- **FR-00 spike passes** (Path B bootstrap reliable on current UCI). If it fails → Path A fallback re-baselines FR-02/FR-03.
- `sprk_navitem` entity + security roles deployed before capture (FR-03) and pins (FR-07).
- Recovered `SidePaneManager.ts` / `contextService.ts` available in `notes/retired-sidepane-code/` (confirmed present).

### External Dependencies
- Target MDA/UCI build behavior for `Xrm.App.sidePanes` persistence + `getPageContext()` polling (validated in the retired platform; re-confirmed by FR-00).
- Optional: Dataverse relevance search — **not** a dependency (OQ-5 resolved to N per-entity queries).

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Monitored lens (OQ-1b) | Include shared Monitored lens in r1 or defer? | **Include in r1** | FR-09 is in-scope; adds one query + a distinct Pinned-tab group with shared-flag semantics surfaced in UI |
| Retention (OQ-2) | Retention policy + where pruning runs (no plugin)? | **Prune-on-write, 30-day age** | FR-05: delete `history` rows >30 days during each capture upsert; no scheduled job, no plugin |
| Edited derivation (OQ-5) | How to derive "edited by me" across entities? | **N per-entity queries over a fixed core set** | FR-04: query `modifiedby=me` across matter/project/document/todo/event/communication, merge client-side; no dependency on relevance search |
| Bookmark parse (OQ-7) | How wide should URL parsing be? | **Parse MDA record + entitylist/view URLs; raw-weblink otherwise** | FR-08: parse etn/id/pagetype + viewid to logical targets; everything else stored raw as weblink opening in new tab |
| Architecture (OQ-8) | Global-docked (Path B) vs contextual (Path A)? | **Path B** (owner, 2026-08-12) | FR-00 re-validates the recovered injection bootstrap; Path A is documented fallback |
| Ethical walls (OQ-3) | Gate r1 on ethical-wall/retention concerns? | **No — documented, not a blocker** | Read-time trimming (FR-12) + per-user isolation cover it; revisit only if a specific compliance requirement surfaces |
| SpaarkeAi widget | Build the dashboard "Recent & Pinned" widget in r1? | **No — defer to phase 2** (owner, 2026-08-12) | Removed FR-13 widget; r1 touches neither `src/solutions/SpaarkeAi/**` nor `Spaarke.AI.Widgets`. Navigator body still authored as a shared component so a phase-2 project can register the widget without rework (`<spaarkeai>N`) |

## Assumptions

- **Core entity set (OQ-5)**: assuming `sprk_matter`, `sprk_project`, `sprk_document`, `sprk_todo`, `sprk_event`, `sprk_communication` for the Edited derivation — final logical names confirmed during pipeline resource discovery.
- **Poll interval**: assuming ~0.5–2s for `getPageContext()` polling (matches the retired platform's ~2s); tune during FR-00.
- **Retention cap**: 30-day age only (no additional count cap), per OQ-2 answer.

## Unresolved Questions

- [ ] **OQ-6** — Custom-page name resolution for `sprk_pagetype=custom` history rows (best-effort label). Blocks: clean labels for custom-page history — resolve during FR-03/FR-04 implementation by inspecting `getPageContext()` output on the target build. (Investigation, not owner decision.)
- [x] **OQ-9 — RESOLVED (pipeline Step 2, 2026-08-13)**: `SidePaneShell` is presentational-only (chrome; no host/registry/createPane). Build approach = **wrap `SidePaneShell` + generalize `DataGridSidePaneOrchestrator`** for host logic + new `sidePaneRegistry`. No longer a blocker.
- [ ] **FR-00 outcome** — Path B bootstrap reliability on current UCI. Blocks: FR-02/FR-03 baseline; a failure triggers the Path A fallback re-baseline. (Spike gate.)

---
*AI-optimized specification. Original design: `design.md`.*
