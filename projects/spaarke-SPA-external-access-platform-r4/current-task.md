# Current Task State

> **Last Updated**: 2026-11-22 (task 024 done + committed; master merged; PR next)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **024** ✅ — shared EmptyState (FR-17). Notes `notes/task-024-notes.md`. POML completed, TASK-INDEX ✅, drift clean, portfolio synced (#2680: 46 / 23). |
| **Step** | Committed `65f60cf42f` (024), merged origin/master `ff65c019b2` (conflicts only = our own 023c squash → branch side), fix `eae345869a` (two tests the merge had reverted to 023c text). Verified after merge: lib tsc, DataGrid jest 20/178, client-lint ok, SPA tsc, 9 touched SPA test files green, ArchTest grid guard 231/231. Full SPA vitest 1935/1935 + build ok (pre-merge). |
| **Status** | Push + PR in progress (body: scratchpad `pr-024.md`). Nothing from 023/024 is deployed. |
| **Next Action** | Owner's merge go for the 024 PR, with sign-off on its two shared-grid changes (`emptyState` host option; `useHostRowSource` no-empty-flash fix). Arm auto-merge only after that go. Then tell uac-r2 024 is ready (live BFF still lacks #2941) → task 028. |
| **Side** | #1906b → ebilling-r1; #2866 landed on master (duplication sweep). |

## Merge trap (seen 2026-11-22)
- Merging master after a squash of our own work: a line 023c changed and 024 changed BACK looks unchanged from the merge base, so git silently takes master's 023c text. After such merges, for files where `origin/master` == the pre-squash branch tip, restore `HEAD^1`'s copy.

## Owner questions open
- Merge go for the 024 PR, with sign-off on its two shared-grid changes.
- Older: show "The client is reviewing this invoice." beyond the Information field?
- K: add `useHostRowSource` to `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval).

## Filed this session
- D-24 #2939, D-25 #2960, D-26 #2961, D-27 #2962, D-28 #2963 (config row not applied by Deploy-Release / provisioning — do it in or before 028), D-29 #2964 (SPA suite flake; full run green this time), D-30 #2975 (remaining hand-rolled empty states, Idea).

## Task 028 reminders (P2 deploy)
- Waits for uac-r2's live notice of a dev BFF containing #2941 (`9461dc0bb3`). uac-r2 picks the SHA and asks the owner itself — never name a SHA to the owner.
- Then, each with owner go + stated rollback: apply `scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1` (-WhatIf, apply, -Verify -RequireRow; rollback = delete row `286be8b4-f96f-4d5a-b1d5-82fd83ca9693`); grid PATCH 3ff4102c; SPA deploy of the approved SHA via `deploy-external-spa.yml` (builds master HEAD — if master moved, tell owner + uac-r2 first); E2E incl. 1000px.

## Live state
- **Dev BFF** `c8ab2cba9e` (uac-r2, 2026-11-20). Rollback `5ae2804884`. Neither has #2941. uac-r2: #2941 rides its next deploy (fm task 055 from master, or a master candidate if 055 isn't on master when we're ready — tell uac-r2 then); it pings before the swap and sends a live notice after its 30-min gate. `/healthz/catalog` 503 until ebilling seeds EB-D-256 (not a regression).
- **Dev SPA** master `2963d1565b` (live 2026-11-19). Rollback = redeploy `ea7ba661d6`, then `ed3a5c2752`.

## Coordination
- uac-r2 reviews BFF PRs and deploys the dev BFF. Peer messages are not owner approval.

## Open items
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823; D-19 #2854, D-20 #2860. Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill.
- Phase 2 remaining: 028. Task 016 🔄 (owner retest 3–7).
- CI: office-scope legs sit at their 20-min cap (known #1474/#1718/#2767). Re-run on a cancel; not ours.
