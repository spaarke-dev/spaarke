# Current Task State

> **Last Updated**: 2026-11-24 ~03:25Z (wave 8 merged and closed: 031, 032, 034)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active (wave 9, owner "Yes, start them" 2026-11-24)** | **033** (FULL, sonnet @ high): Money sort + formatted CSV export in the shared DataGrid, run by an implementer agent in this worktree. **035** (FULL, sonnet): STOPPED at step 1, its escalation. The external message DTO has NO thread key (re-verified 2026-11-24: `ExternalMessageDtos.cs` and `messages-client.ts` have none). The owner must choose (A) add the thread key (`_sprk_communicationthread_value`) to the external DTO, with uac-r2 review, or (B) defer FR-22. No 035 code before the answer. 033 also needs the owner's OK on behalf of the dataset-grid owners (recorded in `notes/task-033-notes.md`). |
| **033 status** | COMMITTED `1f7bb5d006` (not yet in a PR). Review done (opus, one pass); fix round 2 applied (F2 unused eslint-disables, K1 overflow → Infinity). Jest 261 pass, tsc/eslint/client-lint clean. Pre-existing defects filed: D-34 #3045 (option-set/lookup CSV as JSON; the POML escalation, asked of the owner), D-35 #3046 (unlabelled currency = USD). Waiting on the owner's OK on behalf of the grid owners (`notes/task-033-notes.md` line 6). |
| **035 status** | Owner chose A (2026-11-24). COMMITTED `8c08c7bcd1` (+ notes `bf488b6a40`): `threadId` on the external message DTO, SPA conversations. uac-r2 APPROVED the design (`f23828d3cb`) and the final diff. Opus review: no leak, no F1; publish delta +2,659 B; conflict-check clean. |
| **PR** | **#3048** (033 + 035), open, CI starting. Merge needs the owner's go. |
| **Next action** | Owner answers: (1) 033 grid-owner OK (in the PR as pending); (2) #3045 fix now or later. Then the merge go → close 033/035 (POML, TASK-INDEX, drift check, board +2, current-task). Then 038 stays gated on a dev BFF ≥ `bd40b57b03` (and ≥ the #3048 merge for threads). |
| **Previously** | Wave 8 is closed. |
| **Last merged** | PR #3033 at 03:17:25Z, merge `bd40b57b03`. It carries tasks 031 (Information fields + 12 ReadableColumns), 032 (grid settings per browser + "Clear all") and 034 (Messages refresh + new-message check). POMLs are completed, TASK-INDEX is ✅, the drift check is clean, and the board shows Tasks Completed = 28. #3010 is closed. |
| **Next in phase 3** | 033 + 035 are in PR #3048 (owner approved 033's grid change 2026-11-24). Then **038** (P3 dev deploy + E2E), on the owner's per-SHA go. 036 is cancelled. |
| **uac-r2** | Dev BFF `0362599b94` (contains `bd40b57b03`) LIVE since ~05:21:45Z 2026-11-24 on the owner's go; uac-r2's live notice lifted the SPA hold. It does NOT carry 035 (`threadId`): threads need a later BFF candidate after #3048 merges (the SPA degrades to an ungrouped list without it). uac-r2 did not test external routes with a CIAM token; 038's E2E checks the Information columns. |

## Deploy order (031 condition MET 2026-11-24)
The hold is lifted: dev BFF `0362599b94` carries 031's ReadableColumns. Every SPA deploy still builds master HEAD and needs the owner's go for the exact SHA, with rollback `9d08576e63`. For 035's threads to show, the BFF must also carry the #3048 merge.

## Live on dev
- SPA: master `9d08576e63` (rollback `2dcbe35e34`). It works with today's BFF.
- BFF: `0362599b94`, live ~05:21:45Z 2026-11-24 (uac-r2; previous `996928732e`).

## Wave 8 outcome (for 038's E2E)
- Information shows the new columns:
  - matter: Open date (`createdon`), Matter type, Practice area, Assigned attorney, Assigned paralegal, Description, Last modified;
  - project: Practice area, Assigned attorney, Assigned paralegal, Description;
  - work assignment: Matter, Matter type, Assigned to, Description.
- Grids remember widths and sort per browser (`sprk.ext.gridSettings.v1.{configId}`). "Clear all" (reset icon) clears widths, sort and column filters. A filtered header is announced as "filtered".
- Messages: a header Refresh icon, a background check at most once a minute, and "Show new messages".
- Known limits: "Open date" is a date-time; contact lookups carry the contact id (UAC OK); a parent's Messages keeps polling under a child modal.

## Owner questions open (older)
- Should a docx preview be filed as an Idea?
- Show "The client is reviewing this invoice." beyond the Information field?
- Add a `useHostRowSource` row to `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-30 #2975.
- D-31 #3008: narrow DataGrid header minimums.
- D-32 #3009: empty band above the Documents grid ("Clear all" now sits in that toolbar).
- D-33 #3010: closed (fixed in #3033).

## Merge trap (seen 2026-11-22)
When merging master after a squash of our own work, watch for a line that one task changed and a later task changed back. It looks unchanged from the merge base, so git takes master's text. For files where `origin/master` equals the pre-squash branch tip, restore `HEAD^1`'s copy. On 2026-11-24 the check came back clean (no trap).
