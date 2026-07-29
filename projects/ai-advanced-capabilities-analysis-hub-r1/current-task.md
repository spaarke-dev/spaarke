# Current Task

> Active-task tracker. **Full handoff: `notes/HANDOFF-2026-07-29.md`** (read it first on resume).

**Status**: CODE COMPLETE (24/28) + DEPLOYED to dev (BFF/code-page/ribbon-scripts/subgrids) — finishing the exposure layer.
**Active task**: 071 client deploy — **top item: the Analysis "front door" (section registration + system layout)**.
**Branch**: `work/ai-advanced-capabilities-analysis-hub-r1` · PR **#694** · synced to master.

## Resume in this order (detail in HANDOFF-2026-07-29.md §9)

1. **Front door (§4)** — no `analysisHub.registration.ts` + no `system-layouts.json` "Analysis" entry ⇒ hub not in
   Manage Workspaces or the builder. Build the section registration + system layout + seed `sprk_workspacelayout` +
   Dataverse views + gridconfig → rebuild + redeploy. Owner intent: system workspace named **"Analysis"** (cards→wizard,
   grid→open). OPEN: grid-row = in-place (built) vs modal (owner to confirm).
2. Ribbon buttons — fix the `AnalysisRecordLaunch` `worktype` seam → button = **"Create Analysis"** card selector →
   redeploy ribbon scripts → `/ribbon-edit` import (show XML first).
3. `sprk_analysis` "Analysis main form" still references the retired web resource (blocks the 4th WR delete) — strip it, delete, publish.
4. Tech-debt sweep (form ref, empty `ChatHistory` field on GET, DF-04 dead `BuildContinuationPrompt*`, DF-01/02/03).
5. agreements-r1 subDomain wiring (needs owner `sprk_subdomain` col) — see §7. 6. 072 e2e/UAT → 090 wrap + `/test-diet`.

## Owner must create in Dataverse (HANDOFF §6)

1. 4 saved queries on `sprk_analysis` (All / Agreement 100000000 / Research 100000001 / Patent 100000002).
2. 1 `sprk_gridconfiguration` row → then replace placeholder GUID in `AnalysisHubWidget.tsx`.
3. 1 `sprk_workspacelayout` seed for "Analysis" (I seed after registration).
4. OPTIONAL `sprk_analysis.sprk_subdomain` (Choice) — only if doing agreements-r1 subDomain now.

## Done this session

Worktree synced to master (merge clean) · **BFF deployed+verified** (fork/promote/by-analysis=401, continue/resume=404) ·
**code page + ribbon scripts + subgrids deployed** · 3/4 retired web resources deleted (4th blocked by form ref) ·
Phases 0–6 code ✅ pushed to PR #694 · agreements-r1 coordination assessed. No hard escalations all project.
