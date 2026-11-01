# Current Task State

> **Last Updated**: 2026-11-01 ~17:00Z (checkpoint). origin/work/ebilling-r1 = 9b6bd559ee (master merged at f660d84303, level with origin/master 0e3962069b). Master PR **#2507** open.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | (1) Adversarial review (opus) of 151c-1a, range `d566e5f7fd..4e4d45ac23` on `task/ebill-151c1a-email-route` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-ab1a1dc9a62b799db`). (2) uac-r2's access review of the same range, which covers owner, no copies, the negative test, thread/participant index/sweep, and Sent Items ingestion. uac-r2 asked us to **hold the merge** until its findings arrive. |
| **Next action** | (1) #2507: when ontology-r1 approves, run `gh pr merge 2507 --auto --merge`, then watch for MERGED and tell word-add-in. If CI fails, fix it on work. (2) 151c-1a: when both reviews are in, fix the F-class findings on the branch, merge into work, run the gate (5 builds, ArchTests, broad filter, integration), and push. (3) After uac-r2 sends "move done", send notices and run the two column scripts on dev, then `Deploy-EbillingFormsViews.ps1`, then the -Verify scripts below. (4) Then 151c-1b (UI) and 151c-2 (the `sprk_billingexport` reference on `sprk_communication`). |
| **Master PR** | #2507, auto-merge OFF until ontology-r1 approves (the ruleset needs 0 approvals). The body carries 097b and 097c together and gives the counts and the publish delta (+0.19 MB compressed). |

## Gate on 9b6bd559ee
- ArchTests 1897/1. The failure is ISS-005 (#2469), which passes alone (129/0).
- Broad filter 9891/1/7. The failure is D134 todo-pair-only, which also fails on master: **#2506**. uac-r2 bisected it to #2477 and is fixing it on `fix/uac-r2-2506-todo-pair-owner`.
- Integration 199/0/8, Spe 353/0/23, LoadTests 5/0.

## 151c-1a (task/ebill-151c1a-email-route, head 4e4d45ac23)
- Routes: `POST /api/finance/export-batches/{id}/email` (200 `{communicationId, sentTo, cc, sentToApDate, sentToApDateSet}`) and `POST .../mark-sent-to-ap`.
- Columns: `sprk_apexportemail` (settings) and `sprk_senttoapdate` (batch); -WhatIf only so far. Deploy order: columns, then forms and views, then the BFF.
- I renamed `senttoapDateSet` to `sentToApDateSet` (4e4d45ac23).
- Notes: `projects/ebilling-r1/notes/151c1a-email-route.md` on that branch.

## Owner decisions this stretch
- EB-D-182: server-only byte attachments; one Sent to AP Date on the batch; the batch link is a plain `sprk_billingexport` reference (151c-2, which uac-r2 agreed); batch owner = the creator's default (task 150 unchanged).

## Dev state
- **Hold:** uac-r2 is moving the dev users and both BFF application users into BU1 (owner-approved). Hold the 151c schema runs until uac-r2 sends "move done", or agree a time with them first. After "move done", run the -Verify scripts: `Set-EbillingFindingsJsonFieldSecurity`, `Set-EbillingFirmReasonFieldSecurity`, `Set-EbillingResubmissionOfFieldSecurity`, and `Seed-BillingSystemPolicy -Verify -RequireRows -CustomerBusinessUnit 'Spaarke Business Unit 1'`.
- BFF fcf01480f4 (R3), our six keys intact. The ApExport code page and the 151b ribbons and views are NOT deployed (the dev import needs the owner's OK).

## Peer state
- word-add-in: told #2507. Waits for the merge.
- ontology-r1: review of #2507 requested, and told about #2506.
- uac-r2: reviewing 151c-1a; fixing #2506.

## Open items
- Older notes name the owner in plain text (003, 004, 011, 029, 091). Clean up in a docs pass.
- 151b: 053 must add `sprk_trackingnumber` to `sprk_Invoice/Entity.xml` before any non-dev import.
- 050a §9 items 2–4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (warn of ~1 min of invalid-column faults on column deletes). StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). If builds report a locked file, run `dotnet build-server shutdown` and rebuild. Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P. Remove worktrees with `pwsh -File scripts/worktree/Remove-WorktreeSafe.ps1 -Path <one path>`.
