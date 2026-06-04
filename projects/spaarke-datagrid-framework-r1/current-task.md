# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-03 (task 033 complete — both 033a + 033b shipped)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 034 — Phase D deploy (EventsPage HTML + sprk_event configjson + Calendar widget) |
| **Step** | not-started |
| **Status** | pending |
| **Next Action** | Open `tasks/034-*.poml`; invoke `task-execute` skill. Phase D deploy bundles: (1) EventsPage `dist/index.html` (1,258 KB Vite single-file output), (2) the `sprk_gridconfiguration` Event record `e15c2b93-a05f-f111-a825-70a8a59455f4` (already in DEV — verify), (3) deployment of the rebuilt SpaarkeAi Calendar workspace widget through whatever the legal-workspace consumer path is. |

---

## Recently completed (this session)

| Task | Commit | What |
|---|---|---|
| 033a | `cbe393d4` | `hostFilters` framework extension — `HostFilterCondition` + `overlayHostFilters` + `<DataGrid hostFilters/>` prop + arch doc + configuration guide. Permanent third FetchXML composition layer. |
| 033b | (this commit) | CalendarWorkspaceWidget rewrite (1220 → 887 lines): filter row + calendar strip + EventsPageProvider preserved; toolbar + 6 bulk-status callbacks + helpers deleted; `<GridSection/>` swap → `<DataGrid configId hostFilters onRecordsLoaded onRecordOpen/>`. DEFERRED GridSection deletion (from task 032 D-032-01) completed. |

---

## Phase D progress

| Task | Status | Commit |
|---|---|---|
| 030 — sprk_event config record | ✅ | `48be0b0a` |
| 031 — EventsPage App.tsx rewrite (1868 → 161 lines) | ✅ | `da9262c3` |
| 032 — Retire @spaarke/events-components/{Assigned/Record/Status}Filter (GridSection deferred to 033b) | ✅ | `e3f0e585` (partial) + this commit (GridSection deletion) |
| 033a — `hostFilters` framework extension | ✅ | `cbe393d4` |
| 033b — Calendar widget migrate + GridSection deletion | ✅ | (this commit) |
| 034 — Phase D deploy | 🔲 | — |
| 035 — Phase D UAT (record-link bug closure verification) | 🔲 | — |

---

## TASK-INDEX status snapshot

| Phase | Status |
|---|---|
| Phase A — Foundation (001-009) | ✅ All complete |
| Phase B — BFF passthrough (010-016) | ✅ 010-016 complete; 017 deploy ⏸ deferred (insights-engine-r2 master merge dependency) |
| Phase C — Matter drill-throughs (020-026) | ✅ All complete |
| Phase D — EventsPage migration | ✅ 030, 031, 032, 033 (a+b); 🔲 034 (deploy), 035 (UAT) |
| Phase E — SemanticSearch (040-042) | 🔲 not started |
| Phase F — Legacy retirement (050-054) | 🔲 not started |
| Wrap-up (090) | 🔲 not started |

---

## What task 034 (next) does

Per the POML, Phase D deploy:

1. Build EventsPage `dist/index.html` (already verified green: 1,258 KB single-file).
2. Confirm DEV's `sprk_gridconfiguration` record `e15c2b93-a05f-f111-a825-70a8a59455f4` is intact + has the v1.0 configjson with `rowOpen.type: "webResource"` (load-bearing for the record-link bug closure).
3. Deploy EventsPage `index.html` as a web resource (the EventsPage Code Page surfaces in MDA via web resource binding per ADR-026).
4. Deploy the rebuilt SpaarkeAi Calendar workspace widget into the legal-workspace solution. (The widget lives in `@spaarke/events-components`; its consumer mount in `@spaarke/legal-workspace/src/sections/calendar.registration.ts` doesn't change, but the bundled output now contains the DataGrid framework instead of GridSection.)
5. Smoke check: open EventsPage in dialog mode, open Calendar widget in workspace shell, verify both render.

**Critical record-link bug verification target** (deferred to task 035 UAT but worth knowing now): in the previous build, clicking an event link in the EventsPage grid (modal mode) opened the side pane BEHIND the dialog — record was invisible. Fix: configjson `rowOpen.type = "webResource"` routes to `Xrm.Navigation.navigateTo({pageType: "webresource"...})` which opens IN FRONT.

---

## Resume protocol when next session opens

1. **READ this file first**. Quick Recovery + Phase D progress.
2. **Run `git status`** + `git log --oneline -5` to verify clean state on `work/spaarke-datagrid-framework-r1`.
3. **Open `tasks/034-*.poml`** and invoke `task-execute` skill on it.
4. **Deploy via the appropriate `Deploy-*.ps1`** (script-aware skill should route this — `Deploy-EventsPage.ps1` likely; check `scripts/README.md`).

---

## Files modified this session

Task 033a (commit `cbe393d4`):
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts` (+ `HostFilterCondition` + `overlayHostFilters`)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (+ `hostFilters` prop + `onRecordsLoaded` callback + plumbing)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/index.ts` (barrel re-exports)
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` (composition diagram + Host filters subsection)
- `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` (Step 4b)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/033a-deviations.md`

Task 033b (this commit):
- `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` (1220 → 887 lines; full rewrite)
- `src/client/shared/Spaarke.Events.Components/src/components/index.ts` (removed `GridSection` + `IEventRecord` re-exports)
- `src/client/shared/Spaarke.Events.Components/src/types/index.ts` (removed `IEventRecord` re-export)
- DELETED: `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` (directory)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/033b-deviations.md`
- `projects/spaarke-datagrid-framework-r1/tasks/033-spaarkeai-calendar-widget-migrate.poml` (status → completed)
- `projects/spaarke-datagrid-framework-r1/tasks/TASK-INDEX.md` (032 + 033 → ✅)
- This file

---

## Important reminders for next session

- **PR #329** is the active PR. Verify `gh pr view 329` before resuming.
- **CI status**: verify after this commit lands.
- **DEV environment**: `spaarkedev1.crm.dynamics.com`. sprk_event config record `e15c2b93-a05f-f111-a825-70a8a59455f4`.
- **Don't skip task 035 UAT** — the record-link bug closure (the immediate user-facing motivation for Phase D) requires visual verification in dialog mode after the deploy.

---

*This file is the primary source of truth for active work state. Keep it updated.*
