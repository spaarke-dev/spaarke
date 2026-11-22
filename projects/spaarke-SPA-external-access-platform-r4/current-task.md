# Current Task State

> **Last Updated**: 2026-11-22 (023a+023b merged as #2941 `9461dc0bb3`; 023c done and verified, being committed + PR'd)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **023c** ✅ — Documents list on the shared grid for project / matter / WA / service request + ProjectPage; D-23 `readFromParents` (#2928). Notes: `notes/task-023-notes.md` "Task 023c". 023a/023b ✅ merged (#2941). |
| **Step** | Verified: lib jest 19/172, lib tsc, client-lint ui-components ok, SPA tsc, SPA build, SPA full vitest 1917/1933 (16 failures all in the 8 D-29 flake files, which pass 452/452 alone), ArchTest `ExternalSpaGridViewSelectorGuard` 231/231. One adversarial pass (opus) done; all F findings fixed; POML + TASK-INDEX ✅; drift clean. |
| **Status** | Local commits on the branch, NOT pushed: `3fb134c217` (shared grid: no selection checkboxes when selectionMode none) on top of the 023c WIP commit + master merge `add62afe26`. Uncommitted at checkpoint: the review-fix round (DocumentLibrary Delete gate, ProjectPage opener, DataGrid config-error banner + Try again, resolveGridSource warn, tests, notes, defer-issues D-23 done + D-25..D-29). |
| **Next Action** | Commit the fix round (message file, `git commit -F`), push, open the 023c PR (body: "Closes #2928"; ask owner sign-off for the two shared-grid changes made in 023c — selection cell + config-error text — under the 2026-11-22 dataset-grid precedent; list D-25..D-29; K-limits). Then owner merge go. Then task 024 (shared EmptyState), then 028. |
| **Side** | PR #2937 (quality alerts, worktree `C:\code_files\spaarke-wt-quality-alert-fixes`) still needs the owner's merge go. #1906b → ebilling-r1; #2866 later. |

## Owner questions open
- Merge go for the 023c PR (once CI is green), with explicit sign-off on the two shared-grid changes in it.
- Older: show "The client is reviewing this invoice." beyond the Information field?
- K: add `useHostRowSource` to `.claude/patterns/CAPABILITY-INDEX.md` row 31 (needs a `.claude` edit with owner approval).

## Filed this session
- D-24 #2939 (FetchXML page error shows empty list), D-25 #2960 (adopt grid seams: Invoices section, Events, invoice Files), D-26 #2961 (row actions name no row for screen readers), D-27 #2962 (workforce role without Delete is offered Delete), D-28 #2963 (config row `286be8b4` not applied by Deploy-Release / provisioning — do it in or before 028), D-29 #2964 (SPA suite: 8 files time out in a full run).

## Task 028 reminders (P2 deploy)
- Apply `scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1` (-WhatIf, apply, -Verify -RequireRow) with owner go; rollback = delete row `286be8b4-f96f-4d5a-b1d5-82fd83ca9693`. Without the row the Documents list shows "This list could not be loaded" and no Upload.
- Grid PATCH 3ff4102c; BFF deploy via uac-r2 first (owner go per exact SHA); then SPA deploy of the SHA the owner approved.

## Live state
- **Dev BFF** `5ae2804884` (uac-r2 deploy). Rollback `326781c50f`. `/healthz/catalog` 503 until ebilling seeds EB-D-256 (not a regression).
- **Dev SPA** master `2963d1565b` (live 2026-11-19). Rollback = redeploy `ea7ba661d6`, then `ed3a5c2752`.
- Nothing from 023a/b/c is deployed; the grid config row is not in Dataverse.

## Coordination
- uac-r2 reviews BFF PRs and deploys the dev BFF; it passed #2941. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823; D-19 #2854, D-20 #2860. Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 remaining: 024, 028. Task 016 🔄 (owner retest 3–7).
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
