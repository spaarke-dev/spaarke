# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–86) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-07.md`, `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-08 (checkpoint #23, before /compact; batch 5 in flight).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Batch 5. Done: 101. On dev, waiting on owner checks: 154, 105. **113** (PR #1406): both verifier passes clean; doc-only fixes in progress, then merge. **064** (PR #1411): pass 2 found one F3 (ReadCoverageAsync drops the secure-parent path for an entry also listed directly, so an org wall on a not-yet-secure filed child reads doesNotApply); **exec064 fixing**. **#1410 fix**: PR #1419 open (batched secure-ancestor walk in the read veto; suites green, +0.012 MB); pass 1 done: F1 (not-yet-secure child never walked; round 82), F4 cost text, P1 enforcer gate (fold in) → **fix1410 fixing**; then pass 2. Next: 153, 067 → 099 → 036 (ADR-034 path B approved; fold in #1414) → 090. |
| **Agents running (background, notify on completion)** | `exec113`: doc/comment F4s on #1406; reports a commit. `exec064`: pass-2 F3 fix on #1411 (re-check the fix diff + callers only, then merge). `fix1410`: pass-1 fixes + P1 on #1419 (merges master after #1411 before touching the enforcer). Expect a textual conflict with task 036 in `AccessibleRecordSetService.cs` (constructor + veto). Idle and resumable through SendMessage: `exec154`, `exec105`, `exec101`. |
| **Next Action** | (1) **113:** on exec113's report, re-check its fix diff, then merge #1406 on Router green and deploy the BFF. (2) **064:** on exec064's report, re-check the F3 fix diff and its callers; merge #1411 on Router green and deploy the BFF. #1406 and #1411 both carry the same one-line master compile fix (`EventRoutesLiveTests.cs:407`), so the second merge may need a trivial rebase. (3) **#1410 / PR #1419:** MERGED (65177354f) + DEPLOYED to dev; `fix1410` running its live check; close #1410 on PASS. **174**: PR #1481; pass-1 F1-a..d + F2 fixed (52ac9c579; 18,808/0; +40 KB); **verifier pass 2 running** (`verify174b`); then merge, BFF deploy, TrackingFieldTrio rebuild/import (AccessGrantModal changed), live gate (CIAM contact + org-wide grant on a non-flagged WA under a secure matter). Filed #1482 (/unshare-user Restricted carve-out); #1478 gained 2 items for 175. **136** after 174 (shared files). (1c) **113 DONE** (#1406 merged d4bedd39c, deployed, live gates PASS). Hook cap PR #1433 MERGED. (1b) **064 DONE.** Sequence it against #1411, since both touch No Access coverage. (4) **067**: PR #1434 MERGED (fc639d2c1) after 2 verifier passes; TrackingFieldTrio **1.0.40 IMPORTED to dev**. Waiting on the owner's 7 UI checks (checklist §6); then 067 and 066 close. **153**: PR #1450 MERGED (21f645a6f) after ADR-006 2.1 (#1462 MERGED); banner web resource deployed; forms registered + published, -Verify PASS; TrackingFieldTrio **1.0.41 imported**. Script read-back bug fixed in PR #1473 (merging, job `bta0agirf`). Owner UI checks: checklist §7. **173**: MERGED + DEPLOYED; backfill converged (411 rows); live G-2a/b/c + G-3 PASS; original UAT To Do now Restricted. Owner G-1/G-4 (checklist §8) then close. **173** RUNNING (`exec173`, C:\wt173, opus). **153** waits for 067 merge + owner O1 (is the red banner itself clickable? rec: BOTH, non-clickable banner + clickable TrackingFieldTrio indicator). Owner checklist: `notes/owner-hands-on-checklist-2026-10-08.md`. gatesA DONE (results in batch5-live-gates-2026-10-08.md, gatesA section): 21 containers SAFE (owner may run Remove-TestContainers.ps1, 23 ids); 105 API PASS; 114(3), 142 G-4, 147 G147-6, 154 (h)(b)(d)(j), 163 (b), 166 (d)/23/21, 169 version PASS; 164 (j) not available. Left for the owner: project edb87d10 + grant 144b5110 (105 SPA check, then delete), No Access entry 33a4e845 (154 (o), then delete). PR #1440 MERGED (229b196ff); 047 waits only on owner question A. Rounds 83 (decisions 1-11) and 84 (child follows parent both ways, locked) recorded; tasks 174 and 175 created. (5) **173** running (see 4). **174** waits for #1419; **175** waits for 174. |

## Merge / deploy procedure (current practice)
- **Merge job:** wait for `Router` = pass, with no failing check, for up to 150 × 20 s. Router reports only after Tier 2.
- **Merge:** `gh pr merge N --squash`.
- **BFF deploy:** from `C:\wtR2`. Check it is clean, then `git checkout --detach origin/master`, then `pwsh -File scripts/Deploy-BffApi.ps1 -Environment dev -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev`.
- **External SPA:** `gh workflow run deploy-external-spa.yml --ref master`.
- **Under Git Bash, `~/bin/pac` is a bash shim.** Scripts resolve `$pacExe` since #1407; otherwise use `pac.cmd`.

## Dev state (2026-10-08)
- **Master on the dev BFF:** #1393 (154), #1408 (105), #1395 (101), #1389 (TrackingFieldTrio 1.0.39), #1405, #1407.
- **External SPA:** deployed.
- **154:**
  - The forms, view, NO ACCESS subgrids, site map and hidden Add Existing are applied.
  - O2: Spaarke Core User lost Read on No Access entries. The "Spaarke Access Administrator" role was assigned to ralph.schroeder@spaarke.com.
  - **Gap:** `AddAppComponents` did not add `sprk_noaccessentry` to the Matter Management app. The owner may need to add the table in the app designer.
- **101:** both expiry views are live.
- **App settings (round 79):**
  - `ExternalAccess__Reconciliation__WritesEnabled=true`;
  - `Communication__OwnershipHoldAlertUserIds__0` = ralph.schroeder@spaarke.com.

## Round 81 (2026-10-08): RECORDED (rounds 81 and 82 in the decisions log); task 173 / #1423 created
- **Children with a parent inherit:** To Do, Event, Communication and Document take the parent's `sprk_accesspermission`. The owner added `sprk_accesspermission` (the global choice) to `sprk_document` LIVE; it is not yet in source.
- **Children without a parent:** they keep their own value.
- **Control:** on these child records, but not the TrackingFieldTrio on Communication (email).
- **Explained to the owner:** a child associated with a Secure or Restricted record is protected through the parent. That covers every `sprk_regarding*` lookup, including "Link another" associations. Ownership goes to the Secure team, shares mirror the parent, and Restricted bars external users.
- **Proposed plan:**
  - the server writes the parent's value into the child on create and re-file;
  - the 2-minute reconcile keeps it in step;
  - the field is locked with "inherited from X" when the record has a parent, and editable when it has none;
  - for a parentless record: **record only, no new enforcement**. Owner CONFIRMED 2026-10-08 ("BUT in future we might need to revisit").
- **Peer report (spaarkeai-word-add-in-r1, 2026-10-08, relayed by the owner):** "To Do created on a restricted document shows Standard". Traced `POST /api/office/todo` → `OfficeService.CreateTodoAsync` (`Services/Office/OfficeService.cs:2067`).
  - **Ownership is correct:** `_coreAncestors.StampAsync` fails closed, and `ResolveTodoOwnerTeamAsync` → `_ownershipResolver.ResolveOwnerAsync` applies secure-if-any.
  - **No BFF path writes `sprk_todo.sprk_accesspermission`** (Office, the wizard, `ChildRecordEndpoints.cs`), so it stays at the default Standard.
  - **Doc drift to fix in the task:** `docs/data-model/sprk_communication.md:12` says a child carries no permission of its own, and `projects/unified-access-control-r2/unified-access-control-cascade.md:37` says the column isn't on `sprk_todo`. Both are now out of date.
  - **Peer recommendation:** retire or lock the column (the communication model). If it is filled, fill it ONCE in the shared ownership/stamp path (`CoreAncestorResolver` / `RecordOwnershipResolver`), never Office-only.
  - **Our position:** the owner's round-81 direction overrides "retire". The column shows the parent's value, and a parentless record keeps its own. Implement it in the shared stamp path plus the 2-minute reconcile, as they advise.
  - **Reply owed to the peer** (through the owner, or `#1094`-style coordination): "The owner expected the COLUMN to show Restricted. The effective access was already right: PAT-176903 is Restricted, not secure, so external users are barred through the root. Round 81: children with a parent show the parent's value, written once in the shared stamp path (no Office-only write), kept in step by the reconcile job. Parentless children keep their own value. A new uac-r2 task will cover it; Office needs no change unless the shared path changes its call."

## Waiting on the owner
- **Tests:**
  - TrackingFieldTrio 1.0.39: dark mode, and the lookup pane on top of Manage Access and the composer;
  - 154: checklist (a)–(p) in `notes/task-154-no-access-management-forms.md`, plus the left-nav item;
  - the 171/114 UI checks.
- **Decisions:**
  - (b) No Access record picker: recommended follow-up is RegardingResolver in "link only" mode plus `sprk_objectrecordname`.
  - (c) 101: keep overdue shares in View 2; Granted By beside Created By.
  - (d) 105: OK to seed 250 test documents on dev for the live gate?
  - (e) #1396: add the secure-ownership layer for grant rows?
  - (f) 064: Reason hidden from Write holders (recommended).
  - (g) #1350 share-link: retire or replace?
  - (h) Hook cap: raise to ~20,000?
  - (i) 133(e)/166: close on test proof?
  - (j) Is the Copilot agent in dev?
  - (k) The owner runs `Remove-TestContainers.ps1` (approved).

## Issues filed (not yet scheduled)
| Issue | What it is |
|---|---|
| #1394 | Grant rows have an empty name |
| #1396 | Secure grant rows sit in the root BU (limited by BU topology) |
| #1397 | No Granted By on user-triggered grants |
| #1404 | FR-12 guard fails open on a regex timeout (email project) |
| #1409 | External to-do gets an empty regarding name on a read fault |
| #1414 | Timing oracle in 6 other gates; fold into 036 |
| #1378 | Secure-child reconcile stalls when one table's listing fails |
| #1379 | Census follow-up for the ontology tables |
| #1425 | Contact plane ignores a secure parent's No Access list (read veto + grant check); after #1419 and #1406 |
| #1426 | Review: synchronizer checks only the root and direct parents (possible) |
| #1423 | Task 173 (round 81) |
| #1442 / #1425 | Task 174 (round 84) |
| #1435-#1439 | Owed minor defects from batch 4 (playbook share display, raw 403, log noise, Graph 403→401) |
| #1441 | Invitation-onboarding e2e spec can't pass |
| #1443 | Compose check-changes shares one change state per container |
| #1444 | Dev runs older PCF bundles under master's version (next RegardingResolver = 1.6.2) |
| #1445 | Deleting a provisioned record leaves container/files/JIT roles |

Earlier issues are in previous checkpoints. #1313: do NOT mint a secret.

## Coordination
- **#1355 (ontology-r1):** their task 039 PR will come to us for review. Their task 046 (a work-assignment create route) will come on a separate thread.

## Worktrees
- **Active:** C:\wt113, C:\wt064, C:\wt1410.
- **Deploy:** C:\wtR2.
- **Removable:** C:\wt154, C:\wt105, C:\wt101, C:\wt114u. Run `cmd /c rmdir` on node_modules junctions first.
