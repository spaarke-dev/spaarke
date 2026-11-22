# Current Task State

> **Last Updated**: 2026-11-22 (023a+023b merged #2941 `9461dc0bb3`; 023c merged #2971 `6a907a5cd4`; #2928 closed)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **023c** ✅ — Documents list on the shared grid for project / matter / WA / service request + ProjectPage; D-23 `readFromParents` (#2928). Notes: `notes/task-023-notes.md` "Task 023c". 023a/023b ✅ merged (#2941). |
| **Step** | 023c merged as `6a907a5cd4` (squash, owner go 2026-11-22 incl. sign-off on the two shared-grid fixes: selection cell, config-error text). Required checks Router + Client lint passed. |
| **Status** | Branch has the pre-squash history; merge origin/master before the next PR (conflicts will be only in R4 project files: keep the branch side). Nothing from 023a/b/c is deployed; the grid config row is not in Dataverse. PR #2937 (quality alerts) merged. |
| **Next Action** | Task 024 (shared EmptyState everywhere, FR-17) via task-execute, then 028 (P2 deploy + E2E: apply grid config 286be8b4 with owner go, grid PATCH 3ff4102c, BFF deploy via uac-r2, SPA deploy of the approved SHA). |
| **Side** | #1906b → ebilling-r1; #2866 later. |

## Owner questions open
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
