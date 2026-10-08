# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–79) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-07.md`, `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-08 (checkpoint #22, batch 5 in flight).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Batch 5. Done: 101. On dev, waiting on owner checks: 154, 105 (plus 171/114 from batch 4/5). In review: **113** (PR #1406), **064** (PR #1411). In progress: **#1410 fix** (secure-parent veto). Next: 153, 067 → 099 → 036 (ADR-034 path B approved) → 090. |
| **Agents running** | `verify113` (pass 1 of 2, auth), `verify064` (pass 1 of 2, auth), `fix1410` (executor, worktree C:\wt1410). Executors `exec113` (C:\wt113) and `exec064` (C:\wt064) are idle and resumable for fix rounds. |
| **Next Action** | (1) As each verifier reports: send its findings to the executor (SendMessage to exec113 / exec064), re-check the fix diff, run pass 2 (both tasks are auth-tagged), then merge on Router green and deploy the BFF (`scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev` from `C:\wtR2` after `git checkout --detach origin/master`; check clean first). (2) #1410 PR: one verifier pass (auth-adjacent → two), merge, deploy. Sequence it against #1411 (both touch No Access coverage). (3) Then start 153 and 067 (both consume 064's route: contract in `notes/phase4-access-report-contract.md` on #1411). |

## Merge / deploy procedure (current practice)
- Merge job: wait for `Router` = pass with no failing check, for up to 150 × 20 s; Router reports only after Tier 2 finishes.
- Required checks: `gh pr merge N --squash`.
- **Run PowerShell scripts that call `pac` from the PowerShell tool, or rely on `$pacExe`.** Under Git Bash, `~/bin/pac` is a bash shim (fixed in #1407 for the ribbon scripts).
- The deploy worktree `C:\wtR2` must be clean. `Set-AccessRibbon -Apply` writes the live work-assignment RibbonDiff into it by design; that file was checked in by #1405.

## Dev state (2026-10-08)
- **Master deployed to the dev BFF:** includes #1393 (154), #1408 (105), #1395 (101), #1389 (TrackingFieldTrio 1.0.39: dark mode for standard PCFs; the lookup pane layers over the modal).
- **External SPA:** deployed (run 37741946955).
- **154 live:**
  - forms, view, the NO ACCESS subgrids, the site map and the hidden Add Existing are applied;
  - O2 roles applied: Spaarke Core User lost Read; "Spaarke Access Administrator" was created and assigned to ralph.schroeder@spaarke.com;
  - **gap:** `AddAppComponents` did not add `sprk_noaccessentry` to the Matter Management app, so the owner may need to add the table in the app designer.
- **101 live:** both expiry views, verified.
- **App settings set 2026-10-08 (round 79):**
  - `ExternalAccess__Reconciliation__WritesEnabled=true`;
  - `Communication__OwnershipHoldAlertUserIds__0` = ralph.schroeder@spaarke.com.

## Waiting on the owner
- **Tests:**
  - TrackingFieldTrio 1.0.39: dark mode, and the lookup on top of Manage Access and the composer;
  - 154: checklist (a)–(p) in `notes/task-154-no-access-management-forms.md`, plus the left-nav item;
  - the 171/114 UI checks listed in `notes/batch5-live-gates-2026-10-07.md`.
- **Decisions:**
  - (a) No Access record picker: recommended to switch to RegardingResolver "link only" mode plus `sprk_objectrecordname` as a follow-up; the alternatives are to switch first or keep the form-script picker.
  - (b) 101: keep overdue shares in View 2; Granted By beside Created By.
  - (c) 105 live gate: OK to seed 250 test documents on dev?
  - (d) #1396: add the secure-ownership layer for grant rows (topology already protects production).
  - (e) 064: Reason stays hidden from Write-holders (recommended).
  - (f) #1350 share-link: retire it or replace it with an in-app link.
  - (g) hook cap: raise to about 20,000.
  - (h) 133(e) / 166: close on test proof?
  - (i) is the Copilot agent in dev?
  - (j) the owner runs `Remove-TestContainers.ps1` (approved; interactive SharePoint admin).

## Issues filed this batch (not this project's code, or not yet scheduled)
- #1394: grant rows are saved with an empty name.
- #1396: secure grant rows in the root BU; topology-limited.
- #1397: no Granted By on user-triggered grants (R-4).
- #1404: FR-12 guard fails open on a regex timeout (email project).
- #1409: external to-do gets an empty regarding name on a read fault.
- #1378: one table's listing failure stalls the secure-child reconcile.
- #1379: census follow-up for the ontology tables.
- **Earlier:** #1339, #1340, #1343–#1345, #1350–#1352, #1313 (do NOT mint a secret), and others.

## Coordination
- **#1355 (ontology-r1 task 039):** our review is accepted. Their PR, adding `SecureChildLineage` and `secure-record-owner-role.json` entries for `sprk_signal` / `sprk_decisionrecord`, comes to us for review.
- **Their task 046:** a work-assignment create route on `RecordCreationService`, on a separate thread.

## Worktrees
- **Active:** C:\wt113, C:\wt064, C:\wt1410.
- **Merged, removable:** C:\wt154, C:\wt105, C:\wt101, C:\wt114u (used for small PRs).
- **Deploy:** C:\wtR2.
- **Older ones:** listed in the previous checkpoint's commit; remove at leisure, deleting `node_modules` junctions with `cmd /c rmdir` first.
