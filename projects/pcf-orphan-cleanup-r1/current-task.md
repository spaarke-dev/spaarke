# Current Task — pcf-orphan-cleanup-r1

> Last updated: 2026-06-22 (Task 001 ✅ complete)

## Active Task

**None — Task 001 just completed.** Next pending = Task 003 (gated by owner triage on DEV-001).

## Session 2 Outcomes (2026-06-22)

### ✅ Task 001 complete

- **Outputs**:
  - 10 baseline solution ZIPs in [`backups-2026-06-22/`](backups-2026-06-22/) (~5 MB total)
  - [`notes/preflight-results-spaarkedev1.md`](notes/preflight-results-spaarkedev1.md) — disposition table for 11 customcontrols + 6 canvas apps
  - [`notes/preflight-deviations.md`](notes/preflight-deviations.md) — DEV-001 flagged
- **Verdict**: 10/11 customcontrols **ready-to-delete**; 4/6 canvas apps ready; **AnalysisWorkspace PCF + canvas held** per DEV-001 (live ribbon ref in `sprk_analysis_commands.js:167`)
- **Branch**: `chore/pcf-orphan-cleanup-preflight`

### 🔲 Remaining (in dependency order)

| Task | Status | Blocker |
|---|---|---|
| 003 — Dataverse cleanup spaarkedev1 (10 controls + 4 canvas apps) | NOT started | DEV-001 owner triage (see [preflight-deviations.md](notes/preflight-deviations.md)); also wants a dedicated 4-6 hour focused block |
| (7-day soak) | — | Calendar gate after 003 |
| 004 — Shared lib `@types/react` peerDep | Blocked on 003 + soak | |
| 005 — VisualHost re-pin React 16 | Blocked on 004 | |
| 006 — Dataverse cleanup spaarkedev2 | Blocked on 005 + soak | |
| 007 — Cleanup-log finalize | Blocked on 006 | |

## Branches in flight

- `chore/pcf-orphan-cleanup-setup` → PR #411 (project scaffolding)
- `chore/pcf-orphan-cleanup-source-delete` → PR #412 (source deletion — 3 PCFs)
- `chore/pcf-orphan-cleanup-preflight` → new PR for Task 001 outputs (current branch)

## DEV-001 quick reference (blocks Task 003 row 4 + canvas 3)

`Spaarke_OpenAnalysisWorkspace()` in [`src/client/webresources/js/sprk_analysis_commands.js:145-189`](../../src/client/webresources/js/sprk_analysis_commands.js#L145) opens canvas page `sprk_analysisworkspace_8bc0b` at line 167 via `Xrm.Navigation.navigateTo({ pageType: 'custom', name: 'sprk_analysisworkspace_8bc0b' })`. The companion file `sprk_AnalysisWorkspaceLauncher.js` documents a form-onload approach that's MEANT to replace the ribbon path, but completion is unverified. Three possible outcomes documented in [`notes/preflight-deviations.md`](notes/preflight-deviations.md):
1. Ribbon still wired → do not delete
2. Ribbon retired → reclassify ready-to-delete
3. Ambiguous → owner decision

## Next Action

After PR #411 + #412 + the upcoming preflight PR are merged AND DEV-001 is resolved:

1. **Schedule a 4-6 hour focused block** for Task 003 (Dataverse cleanup spaarkedev1).
2. Resume execution by saying:
   - "work on task 003" → executes Task 003
   - "continue" → loads TASK-INDEX, picks the next 🔲

## Notes for the next session

- PAC CLI is authenticated to **spaarkedev1** (profile [3]). No re-auth needed.
- 10 baseline ZIPs are committed to git in `backups-2026-06-22/` — recoverable from any branch.
- AnalysisBuilder (PCF dfc82d5b) has NO dedicated solution to back up; lives only in Default. If Task 003 deletes it, resurrection requires either an out-of-env restore or accepting irrecoverability (per project §3.5.4 worst-case path).
- Per-control deletion order for Task 003 (post-deviation): UQC → SDV (mandatory live-form re-check) → AnalysisBuilder → DueDatesWidget → EventAutoAssociate (also the resurrection spot-test) → EventCalendarFilter → EventFormController → FieldMappingAdmin → RegardingLink → LegalWorkspace PCF. Plus canvas apps 1, 2, 4, 5. **HOLD AnalysisWorkspace PCF + canvas 3 per DEV-001.**

## Blockers

- **DEV-001** — AnalysisWorkspace PCF + canvas app held pending live-ribbon resolution. Task 003 row 4 + canvas 3 blocked.

## Recovery Notes

If context is lost mid-execution next session:

1. Read [`CLAUDE.md`](CLAUDE.md) for project context
2. Read this `current-task.md` for session outcomes + DEV-001 summary
3. Read [`notes/preflight-results-spaarkedev1.md`](notes/preflight-results-spaarkedev1.md) for per-control disposition
4. Read [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for what's done / pending
5. Resume via `task-execute` on Task 003 (or any unblocked 🔲)
