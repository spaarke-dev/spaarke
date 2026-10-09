# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–86) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Owner checks: `notes/owner-hands-on-checklist-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-08 late (checkpoint #24, before /compact).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Batch 5/6. **174** (PR #1481) is merging: the background job **`bp5mztsrl`** waits for Router, squash-merges, deploys the BFF from C:\wtR2, then imports TrackingFieldTrio **1.0.43** (built in the PR). Everything else is waiting on the owner's checks or on 174. |
| **Agents (idle, resumable by SendMessage)** | `exec174` (C:\wt174), `exec173` (C:\wt173), `exec153` (C:\wt153), `exec067` (C:\wt067), `fix1410` (C:\wt1410), `exec113`, `exec064`. None running. |
| **Next Action** | (1) When `bp5mztsrl` reports: confirm MERGED + `bff exit=0` + `healthz=200` + `TrackingFieldTrioSolution 1.0.43`. Then ask `exec174` to run the live gate on dev: a CIAM contact with an org-wide grant on a NOT-flagged work assignment under a secure matter loses it in the SPA (record before/after in the batch5 notes); the Manage Access banner names the direct parent. No prod KV reads. Delete everything and read it back as 404. (2) Then start, in this order (they share files): **175** (deps 174; also absorb #1478's items), then **136**, then **099** (AccessGrantModal; can run beside 175 if the files don't overlap — check first). Use task-execute and opus executors in fresh `C:\wtNNN` worktrees from origin/master. Two verifier passes for auth/security tags, one otherwise. (3) When the owner reports checklist results, close the tasks (POML `<status>` + TASK-INDEX row + `scripts/check-task-status-drift.ps1`; the 9 known drift false-positives are expected) and sync the board (Project #808 item `PVTI_lAHODW0Pv84BEgWuzg3V-P0`, Tasks Completed field `PVTF_lAHODW0Pv84BEgWuzhWPlLY`, Task Count field `PVTF_lAHODW0Pv84BEgWuzhWPlLU`). |

## Merge / deploy procedure (current practice)
- **Merge jobs** are scratchpad scripts (`scratchpad/mergeNNNN.sh`):
  - wait for `Router` = pass with no pending check, up to **360 × 20 s** (Build & Test takes about 1.5 h);
  - **tolerate only** "Server tests (office scope)" when its job conclusion is `cancelled` (the 20-min timeout, #1474);
  - check that the PR head equals the local head;
  - then `gh pr merge --squash`.
- **BFF:** from `C:\wtR2` (clean → `git checkout --detach origin/master`) run `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`. Only ONE job may use C:\wtR2 at a time.
- **PCF:** `Solution/pack.ps1`, then `pac.cmd solution import --path <zip> --publish-changes` (full path `C:\Users\RalphSchroeder\AppData\Local\Microsoft\PowerAppsCLI\pac.cmd`; the bash `pac` shim does nothing).
- **Form scripts:** `-Apply` then `-Verify`. A systemforms GET returns the PUBLISHED XML, so publish before any read-back (PR #1473).

## Dev state (2026-10-08)
- **Deployed today:**
  - #1411 (064), #1406 (113), #1419 (#1410 fix);
  - #1434 (067), then TrackingFieldTrio 1.0.40;
  - #1450 (153): banner web resource, forms registered and published (-Verify PASS), TrackingFieldTrio **1.0.41**;
  - #1458 (173): web resource `sprk_accesspermission_inherited`, form lock on To Do / Event / Message / Document (-Verify PASS); the backfill converged (411 rows).
  - Merged: ADR-006 amendment 2.1 (#1462), the script fix (#1473), the e2e spec fixes (#1440) and the hook cap of 20k (#1433).
- **App settings (round 79):**
  - `ExternalAccess__Reconciliation__WritesEnabled=true`;
  - `Communication__OwnershipHoldAlertUserIds__0` = ralph.schroeder@spaarke.com.
- **154 gap:** `AddAppComponents` did not add `sprk_noaccessentry` to the Matter Management app. The owner may need to add it in the app designer (checklist §1 g).
- **Records left for the owner:**
  - project `edb87d10…` + grant `144b5110…` (105, now closed, so delete both);
  - No Access entry `33a4e845…` (154 (o) check, then delete).
- **Containers:** `notes/Remove-TestContainers.ps1` lists 25. The owner runs it as SharePoint admin, `-WhatIfOnly` first.
- **Form snapshots:** `scratchpad/snapshots/` (153 and 173).

## Task status
- **Totals:** 126 of 157 in scope are done (172 tasks; 15 cancelled or deferred).
- **Done today:** 064, 113, 146, 047, 105.
- **On dev, waiting on OWNER checks** (checklist sections):
  - 067 (§6), 153 (§7), 173 (§8);
  - 154 (§1);
  - 114 and 171 (§1–§5);
  - batch 4: 137, 140, 142, 143, 147, 150, 157, 162, 163, 164, 165, 166, 168, 169 (§1–§5; their agent-runnable gates ran today and passed — see the gatesA section of batch5 notes).
- **In review / merging:** 174.
- **Not started:**
  - 175 (deps 174; #1478 adds 5+4 stored-flag readers to make follow the cascade);
  - 136 (shares files with 174);
  - 099;
  - 036 (after 136; ADR-034 path B approved; fold in #1414);
  - 090 (wrap-up).
- **Bookkeeping only:** 013, 037, 039 (close with 136); 058 (with 090); 066 (with 067).

## Owner decisions this session (recorded)
- **R81:** children show the parent's Access Permission; a parentless child keeps its own (record only; may revisit).
- **R82:** the parent's permissions control a filed child.
- **R83:** decisions 1–11, recommendations accepted. Follow-ups still to schedule:
  - (1) No Access picker via RegardingResolver "link only";
  - (2) 101 views tweak (Granted By beside Created By; overdue in the 30-day view);
  - (4) #1396 secure ownership for grant rows;
  - (6) #1350 replace share-link with an open-record link.
- **R84:** a child's access follows the parent BOTH ways, locked; replaces round 6 item 4.
- **R85:**
  - evidence stand-ins accepted (047, 143 v, 162 i/j, 146, 166 f, 105);
  - NO prod Key Vault reads for dev work;
  - 153 wording.
- **R86:** ADR-006 amended for thin form-event scripts (path B).
- **173 owner questions:** answered by default (no objection) — an unfiled thread counts as a parent; the control goes on the Message and Document main forms only.

## Open owner items
- Work through the checklist (`notes/owner-hands-on-checklist-2026-10-08.md` §1–§8). §6–§8 close 067, 153 and 173.
- Run `Remove-TestContainers.ps1`.
- 140 needs 4 CIAM identities. 171 needs a licensed external user, a new BU1 user (D3) and a demo self-registration (J4). 165 needs the SPE Admin app role granted to testuser1 plus approval of the write probes.

## Issues filed (not yet scheduled)

| Issue | What it is |
|---|---|
| #1478 | Stored-flag readers that 175's cascade fixes (9 items) |
| #1482 | `/unshare-user` has no Restricted carve-out for an external last reader |
| #1474 | CI office-scope job hits its 20-min timeout repo-wide |
| #1449 | Pill and indicator stale after Make Secure until reload |
| #1435–#1439 | Small batch-4 defects (playbook share display, raw 403, log noise, Graph 403→401) |
| #1441 | Invitation-onboarding e2e spec can't pass |
| #1443 | Compose check-changes shares one change state per container |
| #1444 | Dev runs older PCF bundles under master's version (next RegardingResolver = 1.6.2) |
| #1445 | Deleting a provisioned record leaves container/files/JIT roles |
| #1426 | Synchronizer checks only the root and direct parents (possible) |
| #1394 / #1396 / #1397 | Grant-row name, secure grant rows in the root BU, Granted By |
| #1404 / #1409 / #1414 / #1378 / #1379 | Earlier |

#1313: do NOT mint a secret.

## Coordination
- **#1355 (ontology-r1):** their task 039 PR will come to us for review; their task 046 comes separately.
- **Word add-in (r1):** the round-81 reply was sent. An optional follow-up naming task 173 / #1423 is drafted in this session's chat.

## Worktrees
- **Active:** C:\wt174 (merging).
- **Deploy:** C:\wtR2.
- **Removable after the merges:** C:\wt064, C:\wt067, C:\wt113, C:\wt153, C:\wt173, C:\wt1410, C:\wt154, C:\wt105, C:\wt101, C:\wt114u. Run `cmd /c rmdir` on any node_modules junction first, never `rm -rf` through a junction.
