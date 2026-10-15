# Current Task — pcf-orphan-cleanup-r1

> Last updated: 2026-10-15 02:45 UTC (Task 003, waiting on owner smoke tests)

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | 003 — Dataverse cleanup session, spaarkedev1 |
| **Task file** | `tasks/003-dataverse-cleanup-spaarkedev1.poml` |
| **Status** | 🔄 in-progress (FULL rigor). All Dataverse work is done; **blocked on the owner's browser smoke tests** (POML step 6) |
| **Next Action** | Owner runs the checklist below and records PASS/FAIL in `notes/dataverse-cleanup-log.md` → then mark 003 ✅ (POML `<status>` + TASK-INDEX, then `pwsh scripts/check-task-status-drift.ps1`) and start the 7-day soak before 004 |
| **Branch / PR** | `work/pcf-orphan-cleanup-r1` (see the PR linked in TASK-INDEX notes) |

## What was done (2026-10-15 02:31–02:35 UTC, spaarkedev1 only)

- **Deleted and verified gone**: EventCalendarFilter, AnalysisBuilder PCF, DueDatesWidget, LegalWorkspace PCF, plus canvas `sprk_analysisbuilder_40af8`, `sprk_visualizationdrillthroughworkspace_e48a5`, `sprk_eventspage_786f4`. Customcontrol count 31 → 27.
- **Resurrection drill PASS** (ECF: delete → scoped re-import → new GUID → re-delete).
- **Held**: SpeDocumentViewer (DEV-002), EventAutoAssociate / EventFormController / RegardingLink (DEV-003), FieldMappingAdmin (DEV-004), AnalysisWorkspace (DEV-005: the DEV-001 reason no longer applies; **owner decision needed**).
- **Already absent**: UQC + its solution, `sprk_documentuploaddialog_e52db`, `sprk_analysisworkspace_8bc0b` (DEV-006).
- Backups: `backups-2026-10-14/` (24 ZIPs; the two aggregator exports, SpaarkeCore and SPRKDOCINTELLIGENCE, are local-only).
- Issues: #1895, #1896 filed; #1468 updated.

## Owner smoke-test checklist

Environment: **spaarkedev1** (`https://spaarkedev1.crm.dynamics.com`), app **Spaarke Matter Management**, with browser DevTools (F12) → Console open. Record each result as `PASS` or `FAIL (what you saw)` in `notes/dataverse-cleanup-log.md` → "Smoke tests".

1. **Events calendar (EventCalendarFilter, DueDatesWidget, old Events canvas deleted).** Open any Matter that has events → **CALENDAR** tab (it embeds `sprk_eventspage.html`). Expect: the Events page loads, the calendar filter pane renders, and clicking a date or range filters the event list. No "control not found" or error banner.
2. **Chart drill-through (drill-through canvas and old Events canvas deleted).** On the same Matter, find the **TASKS & EVENTS** card (or **Due Date Card List** / **Events by Event Type**) and click its drill-through or expand action. Expect: an Events page dialog opens, filtered to this matter. *Known pre-existing failure, not a regression*: the **Due Date Count Card** drill-through fails (#1895).
3. **Document upload.** On the same Matter, open the **Documents** subgrid and use its upload / **+ New Document** command. Expect: the **Document Upload Wizard** dialog opens (code page), not a blank or "page not found" dialog.
4. **Spaarke Console (LegalWorkspace PCF deleted).** Open the **Spaarke Console** (`sprk_spaarkeai`) from app navigation. Expect: the workspace dashboard renders its widgets normally.
5. **Analysis entry points (AnalysisBuilder PCF and canvas deleted).** Open any **Document** record:
   - a. command bar → **New Analysis**. Expect: the **Playbook Library** dialog opens.
   - b. in the Document's **Analysis** subgrid, select an existing analysis → **Open Workspace**. Expect: the Spaarke Console opens in a modal on that analysis.
6. **Collateral check on held controls (should be unchanged).**
   - a. On the same Document, the file preview (**SpeDocumentViewer**) renders.
   - b. Open any **Event** record (main form), then from a Matter use **+ New Event** (quick create). Expect: both forms load without a script-error dialog.
   - c. Open **Events → My Events Open** (or **All Tasks Open**). Expect: the Regarding column renders as a clickable link.
7. **Console errors.** During items 1–5, the DevTools Console shows **no** red errors mentioning `EventCalendarFilter`, `DueDatesWidget`, `AnalysisBuilder`, `Spaarke.LegalWorkspace`, `sprk_eventspage_786f4`, `visualizationdrillthroughworkspace` or `sprk_analysisbuilder_40af8`.

**If an item FAILS** because of a deleted component, restore it (about 1 minute, name-stable, new GUID; no tenant-wide publish):
`pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ZipPath projects/pcf-orphan-cleanup-r1/backups-2026-10-14/baseline-<Solution>-unmanaged-pre-cleanup-2026-10-14.zip -SolutionUniqueName <Solution>`
where `<Solution>` is `SpaarkeEventCalendarFilter`, `DueDatesWidget`, `SpaarkeLegalWorkspace`, `PcfOrphanBackupAnalysisBuilder` (AB PCF + canvas) or `PcfOrphanBackupCanvasPages` (drill-through + Events canvas).

## Open owner questions

1. **AnalysisWorkspace PCF (DEV-005)**: the DEV-001 reason is gone (ribbon already opens `sprk_spaarkeai`; canvas host already deleted; `AnalysisWorkspaceSolution` no longer exports). Approve deletion in a follow-up pass and in task 006?
2. **FieldMappingAdmin (DEV-004)**: master's FieldMappingAdminSolution keeps it "to avoid regression". Remove it from that mirror and retire it, or keep it?
3. **Scope reset**: SDV, EAA, EFC and RL are live (DEV-002/003). Retiring them belongs to `pcf-legacy-retirement-r1` (#1171) / #1468, not this project. Should task 006 (spaarkedev2) replay only today's cleared set?

## Files modified this session

- `notes/dataverse-cleanup-log.md` (new), `notes/preflight-deviations.md` (DEV-002–007), `notes/preflight-results-spaarkedev1.md` (stale-banner), `current-task.md`, `CLAUDE.md` (gotchas), `tasks/003-…poml` (status), `tasks/TASK-INDEX.md`, `backups-2026-10-14/*.zip` (20 committed; the 4 SpaarkeCore / SPRKDOCINTELLIGENCE ZIPs are local only)
