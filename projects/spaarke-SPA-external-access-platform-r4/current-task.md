# Current Task State

> **Last Updated**: 2026-11-14
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 018 retest round 2: 012 ✅ and 013 ✅ in PR #2837 (CI pending). Next: merge, SPA dev deploy (owner approval), owner retest. Then 021 (ebilling 093) |
| **Step** | #2837 merged (04dbd8df05). Dev SPA run 43735968667 is live; owner retest round 2 in progress |
| **Status** | Dev BFF `5a9b687a0b` (see Live state). Dev SPA from run 43598875136 (master `2747977f47`), live 13:19Z; bundle has the new strings, no "Can contribute" or "To Do". |
| **Next Action** | Collect the owner retest results and fix anything found. Record them in notes/task-018-notes.md and close 018. Then 021 against the ebilling 093 contract (owner confirmation still pending on pulling it forward). SPA rollback: master `2747977f47` (run 43598875136). |

## Done after merge
- The merge SHA was sent to ebilling-r1 (for their 095 notes) and acknowledged to uac-r2.
- Closed D-6 #2703, D-11 #2718 and D-12 #2769, each with a merge reference.
- The last pre-merge commit `593c75cdb9` adds a reasoned eslint-disable on the DataGrid host-command catch (CI client-lint ratchet). Filed D-16 #2823: two older silent catches in the shared DataGrid. Both are in the suppression baseline.

## Owner retest list (after the SPA deploy)
- Section actions on matter / project / WA records. No To Do anywhere.
- The Submit invoice modal over the record, with the record fixed. On a WA it stays in the header (the owner confirms this).
- Submit invoice in the Invoices grid toolbar, with no standalone button.
- Access labels: View Only / Collaborate / Full Access. "Can contribute" never appears.
- Tasks / Events split, Add task, and Add event without the type select.
- Documents columns and tooltips.
- Unfinished E2E items in `notes/task-018-notes.md`, plus the CMRCL-788888 Submit invoice icon.
- ebilling 095e: invoice names should now read "{firm} - {number}" (#2822 is in BFF `5a9b687a0b`).
- SPA inputs: master `7da54c00a3` vs `bd43110169` show no diff under `src/client/external-spa` or `src/client/shared`, so a master SPA build equals the reviewed rework.

## Live state
- **Dev BFF:** `5a9b687a0b` = master `2747977f47` + mcp spike (uac-r2, live ~15:19Z). BFF rollback `0173e1486e`.
- **Dev SPA:** master `04dbd8df05` (run 43735968667, 2026-11-15; tasks 012 and 013). Previous build: `2747977f47`.
- **Never deploy the BFF directly.** uac-r2 orchestrates the BFF deploy, and the owner gives the go per SHA.

## Open items / issues
- D-13 #2800 (retire the to-do routes after the retest), D-14 #2809 (Task GUID copies), D-15 #2810 (dead header icons), D-16 #2823 (silent DataGrid failures).
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, and the #2786 backfill (uac-r2).

## Uncommitted
- current-task.md (this checkpoint)

## ebilling 093 (invoice files) — consumer review done 2026-11-15
- dc2e5ab86c (not merged): list `{value:[{id,fileName,contentType?,sizeBytes?,createdOn?,scanStatus: Clean|Checking|NotScanned}]}`; content 409 `file_scan_pending`, uniform 404 `sdap.access.deny.record_unavailable`, 503 `file_store_unavailable` / `ebilling.disabled`; no Content-Disposition name (the SPA names the blob from `fileName`). The four `/invoices/{id}/documents` routes are retired in the same commit; the only SPA caller is `FilesSection.tsx`.
- **Deploy order is strict both ways:** uac-r2 swaps the BFF with 093 first, then the 021 SPA right after. NO other SPA deploy may run between merging 021 and that swap: a master SPA build with 021 on the old BFF breaks the invoice Files section.
- Pulling 021 forward ahead of 020/022/023 is pending owner confirmation.
