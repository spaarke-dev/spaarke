# Current Task — spaarke-side-pane-navigation-history-r1

> Tracks the ACTIVE task only. History lives in `tasks/TASK-INDEX.md` + per-task POMLs.

## Status: Wave B — code done; 021 owner-gated
- ✅ **001** GO (spike) · ✅ **010** xrmContext typings · ✅ **011** SprkSidePaneHost + registry · ✅ **020** sprk_navitem schema (author-only)
- ⏸ **021** deploy sprk_navitem to spaarkedev1 — **owner-gated** (first org schema mutation); script ready, I have auth
- 011 note: `DataGridSidePaneOrchestrator` generalized IN PLACE (additive optional fields; escalation did NOT fire); DataGrid suite 64/64 green; EventsPage consumer unchanged.

## Key carry-forward (from spike 001 + Wave A)
- Capture (030): **never cache Xrm — re-read `window.top.Xrm` each poll**; primary signal = top-window URL (`pagetype/etn/id`), getPageContext as cross-check; poll 1.5s; WebApi `retrieveMultipleRecords`, `_modifiedby_value eq {userId}` for FR-03 Edited.
- 010 widened `xrmContext.ts`: `SidePanesApi.getPane`, `SidePane.select()`, `PageInput.webresourceName`, `data: string|Record`, 3-frame `getXrm()` (never throws). 28/28 tests, tsc clean.
- 020: `sprk_navitem` UserOwned; `sprk_targetid` = String(100) per `sprk_todo.sprk_regardingrecordid` precedent (revisit at 030). Deploy script idempotent, option-sets-before-picklists.
- **086 (auto-launch) de-risked**: spaarkedev1 app ribbon STILL has the dormant `sprk.Global.SidePaneManager` command+enable-rule → `Spaarke.SidePaneManager.initialize`/`$webresource:sprk_SidePaneManager`. Recreate that WR to reactivate — likely NO ribbon import. See `notes/task-001-spike-report.md`.

## Next waves
- After 011 (+021): **Wave C** = 030 (capture) ‖ 040 (NavigatorPane code page). Then D (tabs/pins/views), E, F, deploy, wrap.

## Context recovery pointers
- [`plan.md`](plan.md) · [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) · [`CLAUDE.md`](CLAUDE.md) · [`spec.md`](spec.md) · spike harness in `spike/` (deployed to spaarkedev1)

## Decisions log
- 2026-08-12 Path B (owner). 2026-08-13 OQ-9 resolved; SpaarkeAi widget deferred (owner). Task 001 GO. Spike harness = no-build HTML (ADR-006 spike exception §6.5-A). Wave A (010,020) done via parallel sonnet subagents.
