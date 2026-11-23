# Current Task State

> **Last Updated**: 2026-11-23 ~20:50Z (task 028 complete: P2 deployed to dev and E2E passed; P3 not started)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | None in progress. **030 ✅ completed 2026-11-23**: 18 of 29 columns found, 11 dropped, none secured. The owner said yes to `createdon` as the open date and to all three people columns. The final list for 031 is in `notes/task-030-information-fields.md`. |
| **Next** | Phase 3 Group C: **031** (ReadableColumns + Information section; opus/high; BFF, with UAC review per column via uac-r2; run `/conflict-check` before the BFF PR; include D-33 #3010's comment fix), **032** (DataGrid persisted widths/sort + Reset columns; builds on PR #3000's `userColumnWidths`) and **034** (Messages refresh). Run them through task-execute; Group C tasks can run in parallel. |
| **Previous** | 028 ✅ (record: `notes/task-028-deploy-record.md`). |
| **Live on dev** | **SPA:** master `9d08576e63`, run 45260396182, live 20:34:15Z (rollback: redeploy `2dcbe35e34`). **BFF:** `a48398746d` (uac-r2, gate passed). **Grid config:** row `286be8b4` created; `3ff4102c` has the Review detail column (rollback string in the deploy record). |
| **Phase 2 result** | E2E round 2 (owner as writer + View-only partner): all pass. uac-r2 server readout: PASS (9 requests, all 200, no errors). Fix PR #3000 (Review detail option-set shape + DataGrid column resize) merged. |

## Side work
- ebilling-r1 task 134: R4 review done; nothing pending.
- uac-r2: has both E2E windows and readouts; nothing pending.

## Merge trap (seen 2026-11-22)
- Merging master after a squash of our own work: a line that one task changed and a later task changed BACK looks unchanged from the merge base, so git takes master's text. For files where `origin/master` == the pre-squash branch tip, restore `HEAD^1`'s copy.

## Owner questions open
- Should a docx preview be filed as an Idea? (FR-14 owner decision 2026-11-09 = PDF + images only.)
- Older: show "The client is reviewing this invoice." beyond the Information field?
- Add a `useHostRowSource` row to `.claude/patterns/CAPABILITY-INDEX.md` row 31? (`.claude` edit, needs owner approval.)

## Filed
- D-24 #2939, D-25 #2960, D-26 #2961, D-27 #2962, D-28 #2963, D-29 #2964, D-30 #2975.
- D-31 #3008: narrow DataGrid header minimums.
- D-32 #3009: empty band above the Documents grid header.
- D-33 #3010: FetchService comment drift. Ride it on the next R4 BFF PR.

## Coordination
- uac-r2 deploys the dev BFF only; peer messages are not owner approval.
- An SPA deploy builds master HEAD, so re-check HEAD before dispatch and tell the owner and uac-r2 what it adds.
- Task 032 (DataGrid persisted widths + Reset columns) builds on PR #3000's `userColumnWidths` state in `DataGrid.tsx`.
