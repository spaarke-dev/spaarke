# Phase D UAT Report — task 035

> **Environment**: `https://spaarkedev1.crm.dynamics.com` (Spaarke Development Environment)
> **Deploy commit**: `905a2f10` (task 034 — Phase D deploy)
> **Status**: 🔄 **In progress — awaiting operator (Ralph) sign-off on each checklist item**
> **Operator**: Ralph Schroeder

---

## What was deployed (recap from task 034)

| Asset | What | Size |
|---|---|---|
| `sprk_eventspage.html` | EventsPage thin shell (task 031) + DataGrid framework with hostFilters (task 033a) | 1,229 KB |
| `sprk_corporateworkspace.html` | LegalWorkspace bundle including rebuilt CalendarWorkspaceWidget (task 033b) | 2,162 KB |
| `sprk_gridconfiguration` (`e15c2b93-…`) | sprk_event grid configjson (task 030) — verified `rowOpen.type=webResource` present | 863 bytes |

---

## UAT scaffolding — operator checklist

Per POML `<steps>` + `<ui-tests>`. Fill in the result column as each check is performed. Record screenshots in `notes/uat-screenshots/035-<name>.png`.

### Mode 1 — EventsPage standalone (System mode)

Open the EventsPage Custom Page via the MDA sitemap.

- [ ] **1.1** Grid renders (header card with view selector + command bar; inner card with column headers + rows).
- [ ] **1.2** Filter chips work (click any chevron in a column header → A→Z / Z→A / Filter by / Clear filter / Column width menu).
- [ ] **1.3** `+ New` command opens the Event create form.
- [ ] **1.4** `Delete` command is disabled until a row is selected.
- [ ] **1.5** Row click opens the event detail (modal navigation, not blocked behind anything).
- [ ] **1.6** Refresh command reloads.
- [ ] **1.7** Lazy-load: scroll past 50 rows → next page loads automatically.

### Mode 2 — EventsPage in DIALOG mode (RECORD-LINK BUG CLOSURE — HIGHEST PRIORITY)

This is the headline UAT target. Pre-migration: row clicks opened a side pane BEHIND the dialog, making the record invisible. Post-migration: configjson `rowOpen.type=webResource` routes to `Xrm.Navigation.navigateTo({pageType:"webresource"...})` which opens IN FRONT.

- [ ] **2.1** Open a Matter detail form.
- [ ] **2.2** Find a VisualHost card that drills through to EventsPage (KPIs, Events, Invoices section depending on the chart).
- [ ] **2.3** Click the card's expand button → EventsPage opens in an MDA dialog (~80% × 80%).
- [ ] **2.4** Verify the grid renders with the parent-context filter applied (only events for THIS Matter shown).
- [ ] **2.5** **CRITICAL — Click any row in the grid.**
- [ ] **2.6** **CRITICAL — Verify the event detail OPENS IN FRONT of the dialog and is visible.** If it opens behind the dialog → bug NOT closed → file regression in `notes/drafts/035-deviations.md`.
- [ ] **2.7** Close the event detail → dialog mode EventsPage still functional.

### Mode 3 — EventsPage embedded in iframe on a form

If applicable — check whether any entity form embeds EventsPage via iframe with parent-context envelope.

- [ ] **3.1** Open the parent record form (Matter or relevant entity).
- [ ] **3.2** Verify the embedded EventsPage iframe renders.
- [ ] **3.3** Verify parent-context filter applied (records filtered to the parent).
- [ ] **3.4** Row click opens event detail correctly.

### Mode 4 — EventsPage standalone (direct URL)

- [ ] **4.1** Open the EventsPage URL directly (no MDA wrapper).
- [ ] **4.2** Verify renders without parent context (shows all events).
- [ ] **4.3** Verify command bar + filter chips still work.

### Calendar pane behaviors (EventsPage system mode)

- [ ] **C.1** Open EventsPage system mode.
- [ ] **C.2** Open Calendar side pane (via the Calendar command on EventsPage chrome).
- [ ] **C.3** Calendar pane renders with event-date dots.
- [ ] **C.4** Click an event row → Event Detail pane opens.
- [ ] **C.5** Verify Calendar pane auto-collapses (mutual exclusivity preserved per `calendarPaneOrchestrator.ts`).
- [ ] **C.6** Reopen Calendar → Event Detail auto-collapses.

### Dark mode

- [ ] **D.1** Switch MDA to dark mode (user settings).
- [ ] **D.2** Re-open EventsPage in modes 1 + 2 (system + dialog).
- [ ] **D.3** Verify NO white-on-white or black-on-black panels (every surface uses `tokens.*`).
- [ ] **D.4** Verify portal surfaces (Filter chip dropdowns, ColumnHeaderMenu chevron menu, dialog confirmations) render correctly in dark.

### SpaarkeAi Calendar widget visual regression

- [ ] **CW.1** Open SpaarkeAi workspace → Calendar section.
- [ ] **CW.2** Filter row: Event Type / Event Status / Filter by Date Field / From / To dropdowns visible + functional (preserved per Q2 sign-off).
- [ ] **CW.3** Apply / Clear buttons work (pending-vs-applied state machine preserved).
- [ ] **CW.4** Calendar strip renders with month nav + responsive month count (1-5 based on width).
- [ ] **CW.5** Calendar strip dot indicators reflect event counts on event dates (handleRecordsLoaded wired through `<DataGrid onRecordsLoaded/>` per task 033b).
- [ ] **CW.6** Day-cell click filters the grid to that day (`hostFilters` overlay per task 033a).
- [ ] **CW.7** **TOOLBAR IS GONE** (Q1 sign-off — DataGrid command bar replaces it).
- [ ] **CW.8** DataGrid command bar shows + New / Delete / Refresh (from sprk_event configjson `commandBar.primary`).
- [ ] **CW.9** Row click opens the event modal at 80% × 80% (preserved from task 130).
- [ ] **CW.10** Collapse chevron + localStorage persistence works.

### Bulk-status operations (EventsPage only — not in widget per Q1)

The EventsPage's `registerEventHandlers.ts` registers 6 framework command handlers (BulkUpdateEventStatus, CompleteEvents, CloseEvents, CancelEvents, OnHoldEvents, ArchiveEvents). Per the current sprk_event configjson `commandBar.primary` (`+ New / Delete / Refresh` only), these don't appear in the EventsPage command bar by default — but they're available for configjson extension. UAT them only if the operator extends configjson; otherwise skip.

- [ ] **B.1** (Conditional) If configjson is extended to add CompleteEvents etc.: select 5 events → run each operation → verify Promise.all behavior + global notification.

---

## Where to record results

1. Tick checkboxes inline above as each test passes/fails.
2. For any FAIL: create `notes/drafts/035-deviations.md` and capture the repro + expected vs. actual + screenshot path.
3. For visual regressions in the Calendar widget: capture pre/post screenshots side-by-side at `notes/uat-screenshots/`.
4. When all checks pass: change Status at top of this file to `✅ complete`, update `TASK-INDEX.md` (035 ✅), and notify project owner.

---

## Critical acceptance gates (must all pass)

1. ✅ Record-link bug closed (Mode 2.5 + 2.6 — the dialog-mode row-click verification). This is the #1 graduation-criteria item for the project.
2. ✅ All 4 EventsPage modes work without UX regression.
3. ✅ Calendar pane mutual exclusivity preserved.
4. ✅ Calendar widget filter row + calendar strip + Apply/Clear preserved AS-IS.
5. ✅ Dark mode parity across all surfaces (no raw-hex regression).

If any of (1)–(5) FAIL: do NOT mark Phase D complete; file the regression in `notes/drafts/035-deviations.md` and decide remediation scope (in-project fix vs. follow-up project).

---

## Why this report is "scaffolding" and not a result

Task 035 is a manual operator UAT — the POML explicitly lists `<tools><tool name="browser">Manual UAT</tool></tools>`. AI session has no Chrome integration in this environment, so the assistant deployed the artifacts (task 034) and prepared this checklist for the operator to execute.

When the operator (Ralph) returns from UAT:
- If all green: edit this file's Status line + check off every box + commit + push.
- If any red: file deviations + decide whether to extend Phase D or graduate to Phase E with a known follow-up.
