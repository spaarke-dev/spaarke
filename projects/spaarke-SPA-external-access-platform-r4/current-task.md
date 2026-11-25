# Current Task State

> **Last Updated**: 2026-11-25 ~01:45Z (task 038: deploys done, E2E mostly passed, fixing grid sort defect)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 038** (P3 dev deploy + E2E), STANDARD rigor, prescriptive. Steps 0-5 done. Step 6 (E2E) mostly done. Record: `notes/task-038-deploy-record.md`. |
| **Live on dev** | SPA `3067fb3bdc` (run 45506591396; owner go "Yes, deploy 3067fb3bdc"; rollback `9d08576e63`). BFF `3067fb3bdc` (uac-r2, live 00:09:50Z 2026-11-25, GATE PASS). |
| **E2E results** | uac-r2's (a)-(c) all PASS; uac-r2 confirmed (b) after the 01:22Z probe; its missing App Insights rows are #2158. FR-18, FR-19 (widths, sort persisted, Clear all), FR-20 (money sort, export text), FR-22 threads, dark theme: PASS. **Not run:** View Only (the test account has no View Only grant), FR-21 "Show new messages" (needs a message posted from a second session; that sends a message, so it needs the owner's OK). |
| **Owner go (2026-11-25)** | "Yes fix here": fix the grid sort defect before closing 038. |
| **In progress** | Sort fix in shared DataGrid. A normal click on a column name opens the column menu (`HeaderCellContent` stops the click), and the menu's "A to Z"/"Z to A" were no-ops, so sort was reachable only from the header-cell edge. Pre-existing since `c1c428e92b` (2026-06-05). |
| **Committed** | Sort fix `55801fbfbb` on the work branch: DataGrid holds the sort and always hands Fluent a controlled `sortState`; the menu sorts only when `enableSorting`; HeaderCellContent stops Enter (keydown) / Space (keyup) on the menu trigger (review finding: keyboard re-sorted). `__tests__/DataGrid.headerMenuSort.test.tsx` 5 tests. DataGrid folder 304/304, SPA grid tests 95/95, tsc clean in both. |
| **Next action** | Merge origin/master, push, open the PR from the work branch (regression note); merge only on the owner's go. Then a SPA redeploy (owner go per SHA, uac-r2 told) and re-test the menu sort in Chrome (tab 1800355809 in the MCP group). Then close 038: TASK-INDEX ✅, POML completed, drift check, `task-038-notes.md`, defer View Only + FR-21 if the owner says so, portfolio sync (Tasks Completed → 31). |
| **Other observations (tell the owner, not yet filed)** | Excel export follows the loaded order, not the on-screen sort. |
| **ebilling-r1** | PR #3084 (SPA invoice-result links, `recordAccess.ts` export) waits on the owner's merge go; no conflict; ebilling will tell us when it merges. It ships only with a later SPA deploy. |

## Owner questions open (older)
- Track the residual export-hardening gaps privately (chat only, never in the repo)?
- Should a docx preview be filed as an Idea?
- Show "The client is reviewing this invoice." beyond the Information field?
- Add a `useHostRowSource` row to `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-30 #2975; D-31 #3008; D-32 #3009; D-33 #3010 closed.
- D-34 #3045 done (PR #3055); D-35 #3046 (unlabelled currency = USD); D-36 #3050 (MDA grids don't read OData label annotations).

## Merge trap (seen 2026-11-22)
When merging master after a squash of our own work, watch for a line that one task changed and a later task changed back: it looks unchanged from the merge base, so git takes master's text. For files where `origin/master` equals the pre-squash branch tip, restore `HEAD^1`'s copy.
