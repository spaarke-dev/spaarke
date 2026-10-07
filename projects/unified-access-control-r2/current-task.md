# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–77) and `notes/decisions.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-07 ~20:00 UTC (checkpoint #21, before /compact; the owner is about to test on dev).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Batch 5. Tasks 171 (broker-only SPE bytes + JIT Office edit) and 114 (`sprk_isexternal` eligibility + Restricted) are MERGED and DEPLOYED to dev, and wait only on owner UI checks. Task 172 (#1011) is COMPLETE. Index: 117 done · 17 wip · 17 open · 10 cancelled · 5 deferred · 3 escalated. |
| **Status** | **The owner is testing on dev now.** No agent is running. One PR is open: **#1368**, a script/doc change: `Set-AccessRibbon -Apply` retries its post-publish verify, plus the dev-BFF name corrected in `auth-azure-resources.md`. Router passed; one legacy check was pending at checkpoint. |
| **Next Action** | (1) Merge #1368 once no check is pending (`gh pr merge 1368 --squash`). (2) Collect the owner's test results; fix or file every defect found (§3 rule). (3) Then batch 5: 154 → 113, 105, 101 → 064 → 153, 067 → 099 → 036 → 090. 036's ADR-034 amendment is approved (round 75). |

## Dev state (master `dc189faac`, deployed 2026-10-07)
- **Contains:** 171 + hotfix #1353 (upload binding, 0x80048306 = no access, current-version content), 172 (#1337), 114 (#1342) and the ribbon fix #1366.
- **171 rollout done:**
  - `sprk_graphitemidbound` schema + FLS;
  - backfill 544/544;
  - `DocumentPointer__ItemIdBoundBackfillComplete=true`;
  - `sprk_documentuploadwizard` page deployed.
- **114 rollout done:**
  - guest flags on 2 `#EXT#` users, including the owner's hotmail guest, owner-approved;
  - BFF;
  - `access_ribbon.js` 1.6.0 + `assignedaccess_postsave.js` 1.1.0;
  - TrackingFieldTrio 1.0.36;
  - the Access ribbon (`SpaarkeAccessRibbons`, `-SecureTransitionDeployed`), whose read-only `-Verify` PASSED.
- **Live re-tests PASSED:** upload then attach on secure and BU; another user's file 403 `NotTheUploader`; JIT removal after a full unshare (`jit.removed: 1`). Record: `notes/batch5-live-gates-2026-10-07.md`.
- **Test data kept:**
  - PS `31e232ae…` (secure; docs 2b97180e, b02ebe33);
  - BU project `fb73b08c…` (docs 08cbce55, d4300ad0).
  - testuser1 holds the creator share on PS.

## Owner checks still owed (what "wip" waits on)
- **171 (Word, browser):**
  - B1 Word web/desktop editability on a secure doc;
  - B3 SharePoint refusing a Read-only user;
  - C3 how long saves keep working after removal;
  - D3 user create/enable/disable and flag removal;
  - E contact plane (CIAM);
  - G Office add-in save;
  - H desktop change detection;
  - J1 the wizard offers only "Keep both";
  - J4 demo self-registration marker.
- **114 (UI):** Share hidden on a Restricted record's form and in a grid selection that includes one; Manage Access shows "External user — no access"; after the job's first runs, `no-internal-reader` / `owner-is-external` reports go to an admin.
- **Other wip lanes (unchanged):**
  - CIAM: 136/037/039/135, 137's observations, 140's invites, 157's grid walk;
  - 141 G-8 (013);
  - wizards and forms: 047, 142 G-7, 147 G147-4/6, 150 G-10, 168 (h), 169 probes, 163 d/e, 164 b/g/f/l/m, 166 27.
  - 143's gate 14 needs task 154.
- **Setup owed:** a BU1 SPE admin (165 leaf gates); a second non-admin login; a non-admin without Create (166 f).
- **Before CIAM:** reproduce the 2026-09-25 invite-and-grant 500 with an ALREADY-invited email.
- **API smoke checks owed:** 098 `set-record-share-expiry` read-back; R15 `POST /api/v1/external/projects/{id}/documents`.

## Pending owner decisions
- **Hook cap:** raise `.claude/hooks/reinject-project-state.ps1`'s 15,000-char cap (CLAUDE.md §2+§3+§6 are at 14,817), or prune to add rules.
- **Settings:** set `ExternalAccess:Reconciliation:WritesEnabled=true` (137 + R4) and `Communication__OwnershipHoldAlertUserIds__0` (G146-3)? Explain plainly and confirm first; it restarts the BFF.
- **Smaller:** 133(e) proven by tests; 166's reporting gates "not configured" and Redis-down proven by tests; is the Copilot agent deployed in dev (164 j/d)?
- **#1350:** share-link returns 502 on every standard document (SPE refuses the link). Retire the route or replace it with an in-app link?
- **Containers:** the owner runs `scratchpad\gates\Remove-TestContainers.ps1 -WhatIfOnly`, then without the switch, as a SharePoint admin, to delete 21 empty test containers.

## Open issues filed this session (not this project's code, or not yet scheduled)
- #1339: membership `byRole` attribution.
- #1340: role/identity vocabulary.
- #1343: the Spaarke Demo team holds System Administrator; owner said not deliberate.
- #1344: CA2024 breaks `Spaarke.sln -warnaserror`.
- #1345: ui-components jest failures on master.
- #1350: share-link 502.
- #1351: the SPE admin user grant doesn't bind the UPN.
- #1352: the sync job reports Failed because of 65a3fab2/#1313.
- **Earlier:** #1313 (do NOT mint a secret), #1317, #1318, #1324–1326, #1293, #969, #974, #1290, #1303–1307, #1310.
- **Minor fix candidates (2026-10-06):** a playbook sharing revoked team shows as Read; "Unknown Team" (`/api/data/teams(id)` is missing `v9.2/`); a raw 403 on a failed push child.
- **#1094:** cpo-r1 hand-offs INCOMING-141/145 delivered; track until acknowledged.

## Branches and worktrees
- **Work branch:** `work/unified-access-control-r2`, merged with master 2026-10-07 (`9589680a3`). Task and fix work still happens in fresh short-path worktrees from `origin/master`.
- **Deploy worktree:** `C:\wtR2` (master `dc189faac`, clean).
- **Older worktrees:** C:\wt171, C:\wt172, C:\wt172m, C:\wt172b, C:\wt114, C:\wt114s, C:\wt114v, C:\wtatt, C:\wtR, C:\wtDS — all merged or used; remove at leisure (`cmd /c rmdir` any `node_modules` junction first).
- **Keep:** `C:\wtD\scripts\logs\` (batch-4 deploy backups).
- **Remove by hand:** `C:\wt114m` (undeletable leftover).
- **Agents this session (all finished):** exec171, exec172, exec114, and the verifiers.
- **Rule breach to remember:** exec171 once pushed with `locksverify` disabled. It acknowledged, and nothing was changed in config.

## Key notes
- `notes/batch5-live-gates-2026-10-07.md` (171/172/114 live record)
- `notes/task-171-broker-only-document-bytes.md`, `notes/task-114-isexternal-eligibility.md`, `notes/task-172-team-owned-owner-column.md` (on master)
- `notes/decisions.md` (index + superseded rules), `notes/batch4-live-gates-2026-10-06.md`
