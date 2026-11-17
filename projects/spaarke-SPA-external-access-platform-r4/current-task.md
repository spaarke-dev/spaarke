# Current Task State

> **Last Updated**: 2026-11-17 (task 016: dev SPA deployed, owner E2E pending)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **016** Event forms deploy + owner E2E, `tasks/016-event-forms-deploy-e2e.poml`. In progress (STANDARD, prescriptive) |
| **Step** | Step 3: owner retest. Steps 1–2 done: dev SPA deployed from master `ed3a5c2752` (run 44153057447), bundle checked |
| **Status** | 015 ✅ done: PR #2886 squash-merged to master as `242696586f` (CI 32 pass / 0 fail). Notes: `notes/task-015-notes.md` |
| **Next Action** | Record the owner's retest results in `notes/task-016-notes.md` (table of 8 checks); file findings via /project-defer-issue-tracking or a follow-up task; then mark 016 done. SPA rollback = rerun deploy-external-spa.yml (dev) on `c1eb6e4a8e` |

## Task 015 outcome (for 016's E2E)
- Add event: Event Type required (options = `creatableEventTypes`), Due Date required by type, Priority, Assigned To ("Me" first), Description. Add event hidden when no type is offered.
- Event record: row opens a child record from row data; deep link `?record=event:{id}` (token amended from `sprk_event:`).
- Edit / Change status per `canEdit` / `canChangeStatus` / `allowedStatuses`; Delete (creator only).
- Known limits: Edit can't mark Due Date required (server 400 shown); multi-parent events: one parent's assignees (400 shown), Copy link may open not-found; truncated parent list → not-found on re-read; 403 then failed re-read replaces the page.

## Live state
- **Dev BFF** `e46b6b453b` (uac-r2 deploy, live since 2026-11-17T03:40Z): master `3c9a2ff4e1` (014a/b/c) plus mcp spike-v2. Rollback = `be0420d763`. uac-r2 asks spa-r4 before any restart.
- **Dev SPA** master `ed3a5c2752` (015 included; deployed 2026-11-17 ~18:56Z, run 44153057447). Rollback = redeploy `c1eb6e4a8e` (runs against the same BFF). Roll back to `04dbd8df05` only together with BFF `5a9b687a0b`.

## Coordination
- uac-r2 (session `uac-r2`) reviews BFF PRs as a gate and deploys the dev BFF. The owner gives the merge go each time. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823; D-19 #2854, D-20 #2860.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open.
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
