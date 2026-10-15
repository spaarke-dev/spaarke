# Current Task — pcf-orphan-cleanup-r1

> Last updated: 2026-10-15 (by context-handoff; task 003 closed)

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | (soak) 7-day wait after task 003, ends **2026-10-22**; next task is 004 |
| **Status** | No active task. Task 003 ✅ (owner smoke tests complete, no cleanup regressions) |
| **Next Action** | Merge PR #1897. On or after 2026-10-22, if no regressions are reported, start task 004 via task-execute. Before 006, get answers to the owner questions below |
| **Branch / PR** | `work/pcf-orphan-cleanup-r1` → PR #1897 (smoke tests done; ready to merge) |

## State

- spaarkedev1 cleanup is done: 4 customcontrols and 3 canvas custom pages deleted (details in `notes/dataverse-cleanup-log.md`). Restore path: `backups-2026-10-14/` + `scripts/Import-SolutionScoped.ps1` (command in the log).
- Smoke-test failures were all pre-existing and filed: #1950 (calendar counts), #1951 (PROJECT BUDGET target), #1952 (hidden user layouts), #1956 (2b, needs the card and the console lines from the owner). Missing system Matters workspace created separately (PR #1955).
- Held controls: SpeDocumentViewer, EventAutoAssociate, EventFormController, RegardingLink, FieldMappingAdmin, AnalysisWorkspace (DEV-002–005).

## Remaining tasks

004 shared-lib `@types/react` peerDep (FULL) → 005 VisualHost React 16 re-pin + deploy (FULL) → 006 spaarkedev2 replay (FULL) → 007 log finalize + inventory refresh.

## Open owner questions (block 006)

1. **AnalysisWorkspace PCF (DEV-005)**: delete it now that the DEV-001 reason is gone?
2. **FieldMappingAdmin (DEV-004)**: retire it, or keep it?
3. **Task 006 scope**: replay only the set cleared on dev1 (SDV/EAA/EFC/RL retirement belongs to `pcf-legacy-retirement-r1` / #1468)?
