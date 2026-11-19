# Current Task State

> **Last Updated**: 2026-11-19 (task 016: dev SPA `2963d1565b` live on dev BFF `5ae2804884`; owner retest items 3–7 pending)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **016** Event forms deploy + owner E2E, `tasks/016-event-forms-deploy-e2e.poml`. In progress (STANDARD, prescriptive) |
| **Step** | Step 3: owner retest. Steps 1–2 done: dev SPA deployed from master `ed3a5c2752` (run 44153057447). Retest found a 500 on typed event create (D-21 #2892); fix PR #2894 merged as `08161cfb0e`; dev BFF `326781c50f` live 22:03Z with the fix; owner retest of items 1–6 next |
| **Status** | 015 ✅ done: PR #2886 squash-merged to master as `242696586f` (CI 32 pass / 0 fail). Notes: `notes/task-015-notes.md` |
| **Next Action** | Record the owner's retest results in `notes/task-016-notes.md` (table of 8 checks); file findings via /project-defer-issue-tracking or a follow-up task; then mark 016 done. SPA rollback = rerun deploy-external-spa.yml (dev) on master `ed3a5c2752` |

## Task 015 outcome (for 016's E2E)
- Add event: Event Type required (options = `creatableEventTypes`), Due Date required by type, Priority, Assigned To ("Me" first), Description. Add event hidden when no type is offered.
- Event record: row opens a child record from row data; deep link `?record=event:{id}` (token amended from `sprk_event:`).
- Edit / Change status per `canEdit` / `canChangeStatus` / `allowedStatuses`; Delete (creator only).
- Known limits: Edit can't mark Due Date required (server 400 shown); multi-parent events: one parent's assignees (400 shown), Copy link may open not-found; truncated parent list → not-found on re-read; 403 then failed re-read replaces the page.

## Live state
- **Dev BFF** `5ae2804884` (uac-r2 deploy, live since ~2026-11-19T02:01Z; owner go in uac-r2): live `326781c50f` + master `fe51619cfc` (#2904 ebilling 131/132/135, ISS-040); mcp spike kept. Rollback = `326781c50f`. `/healthz/catalog` 503 until ebilling seeds EB-D-256 (not a regression). uac-r2 asks spa-r4 before any restart.
- **Dev SPA** master `2963d1565b` (run 44441594640, success, live 2026-11-19T02:05:53Z; owner go 2026-11-18 "and then also deploy portal"). Agreed target was `fe51619cfc`; the 8 later master commits don't touch `src/client/external-spa`, and the only shared file the SPA imports that changed (`themeStorage.ts`) is type-only. Live bundle: Billing Guidelines widget + `ebilling/billing-guidelines` route present, both plane titles present, welcome band absent, `assignable-people` present. Rollback = redeploy master `ea7ba661d6` (portal chrome build), then `ed3a5c2752`. Roll back to `04dbd8df05` only together with BFF `5a9b687a0b`.
- **Portal chrome (#2900):** title by plane (partner "Outside Counsel Collaboration", internal "Legal Service Request Portal"); nav strip and welcome band removed; no Quick Start tab on the CIAM plane (`hasQuickStartFor`), workforce/admin keep it; assistant starts collapsed. The partners-only reading of "remove Quick Start" was not separately confirmed by the owner.
- **ebilling-r1 task 135** (Billing Guidelines widget, `task/ebilling-135` `f054d8a00e`): SPA review PASS with notes N1–N4 (2026-11-18). Per-record view (`coreRecordId`) has no caller; if the owner wants it on records, it is an R4 follow-up in the record template.

## Coordination
- uac-r2 (session `uac-r2`) reviews BFF PRs as a gate and deploys the dev BFF. The owner gives the merge go each time. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823; D-19 #2854, D-20 #2860.
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 (020/022/023/024/028) still open.
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
