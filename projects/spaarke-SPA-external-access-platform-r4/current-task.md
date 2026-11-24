# Current Task State

> **Last Updated**: 2026-11-24 ~03:25Z (wave 8 merged and closed: 031, 032, 034)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active task** | None. Wave 8 is closed. |
| **Last merged** | PR #3033 at 03:17:25Z, merge `bd40b57b03`. It carries tasks 031 (Information fields + 12 ReadableColumns), 032 (grid settings per browser + "Clear all") and 034 (Messages refresh + new-message check). POMLs are completed, TASK-INDEX is ✅, the drift check is clean, and the board shows Tasks Completed = 28. #3010 is closed. |
| **Next in phase 3** | Group D: **033** (DataGrid Money sort + formatted export, FR-20; 032 ✅) and **035** (message threads, FR-22; 034 ✅). Both are parallel-safe, and neither has started. After them: **038** (P3 dev deploy + E2E), which is gated (see below). 036 is cancelled. |
| **Waiting on uac-r2** | A dev BFF at `bd40b57b03` or later, prepared by uac-r2, with the owner's go per exact SHA. uac-r2 has the SHA (sent ~03:20Z). |

## 🔒 HARD DEPLOY ORDER (still in force)
031 is on master, so **no dev SPA deploy may run until a dev BFF carrying 031's ReadableColumns (`bd40b57b03` or later) is live.** Every SPA deploy builds master HEAD. Without that BFF, every matter, project and work-assignment record read answers 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`. The current dev BFF `996928732e` (live since ~03:01Z) does NOT carry 031. The same rule is in task 038's `<gate>`.

## Live on dev
- SPA: master `9d08576e63` (rollback `2dcbe35e34`). It works with today's BFF.
- BFF: `996928732e`, deployed by uac-r2 (the previous one was `22c42efad6`).

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
