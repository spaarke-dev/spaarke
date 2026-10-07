# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite this file at each checkpoint, never prepend; keep it ≤10 KB. Standing rules live in the project `CLAUDE.md` ("Standing directives & gotchas"). Decisions live in `notes/session27-owner-decisions-and-research.md` (numbered rounds). Narrative goes in checkpoint commit messages. The old journal is `notes/handoff-history/current-task-archive-2026-10-06.md`: grep it, never load it.

> **Last Updated**: 2026-10-06 ~23:50 UTC (checkpoint #20, before /compact).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Batch 5 started. **Task 171** (broker-only SPE document bytes + JIT Office edit) is DRAFT PR **#1333**, branch `task/uac-r2-171`, worktree `C:\wt171`. 116/168 tasks complete. |
| **Status** | **171 ESCALATED to owner (2026-10-06):** fix round 2 (`581b4f2a3`, pushed, no PR change) re-verified; verdict F-findings open. **V1 F1:** `DocumentContainerRelocator` never reads `sprk_graphitemidbound`, so Make Secure / migration / relocate-if-misplaced re-points a forged row with a MATCHING copy and the settle step can delete the forged-to item. V2 F2: share-link refusal misses Restricted reached via child links (email archives/attachments via `sprk_relatedcommunication`) and is open when derivation is undecided. V3/V4 F3 missing tests; V5 deploy order must be "before MERGE" (shared dev BFF); V6 dead helpers; V7 backfill `-eq` → `-ceq`. Round 3 is not allowed without the owner. **Task 172** (#1011): `exec172` running in `C:\wt172`. |
| **Next Action** | (1) When `exec171` reports: run ONE narrow re-verification of the round-2 diff only, plus its direct callers and callees (171 is security work: 2 verifier passes allowed, both now used). Any F1 left open is ESCALATED, never a round 3. Then apply its F4 schema/FLS script and backfill on dev in the order it gives (owner-approved by round 72), mark #1333 ready, merge on Router green, deploy the BFF and the DocumentUploadWizard code page, and run 171's live checklist (`notes/task-171-broker-only-document-bytes.md` on that branch: A–J, D0, C2). (2) **The owner is ready for the screen session NOW** (list below); run it right after /compact. (3) The owner approved deleting the 20 empty test SPE containers: give the owner SharePoint-admin commands (`Remove-SPOContainer`), because the BFF has no delete route. (4) Then batch 5: 154 → 113, 114 (round 67), 105, 101 → 064 → 153, 067 → 099 → 036 → 090. |

## Owner decisions
- **Decided in round 72:**
  - F4: add the locked `sprk_graphitemidbound` copy.
  - F10: refuse share-link on secure and Restricted records.
  - "Modified by" = the BFF app is accepted.
  - The test containers are to be deleted.
- **Owner said "not following, but ok" (2026-10-06). Explain plainly and confirm the concrete action before changing any app setting (a change restarts the BFF; do it AFTER the screen session):**
  - G146-3: name the admin who receives ownership-hold alerts (`Communication__OwnershipHoldAlertUserIds__0`; proposed: the owner's own systemuser).
  - 137: set `ExternalAccess:Reconciliation:WritesEnabled=true` on dev (the report shows 0 changes; it also enables R4's deactivations).
- **Still pending:**
  - 036's ADR-034 amendment (path B), before 036 merges.
  - Accept 133(e)'s `sharesRestored` as proven by tests.
  - Close 166's reporting gates as "not configured" and Redis-down as proven by tests.
  - Is the Copilot agent deployed in dev (164 j/d)?

## Owner screen session (~2.5–3 h)
- **CIAM:** 136/037/039/135, 137's observations, 140's invite flows (needs 3–4 CIAM identities), 157's grid walk.
- **Workforce sign-in:** 141 G-8 (test.user@demo.spaarke.com; a guest token).
- **Wizards and forms:** 047 (one secure Create Project), 142 G-7 + UX, 147 G147-4/6, 150 G-10 + UI tests, 168 (h), 169 probes (the invoice form has a To Do subgrid), 163 d/e, 164 b/g/f/l/m, 166 27 (wizard).
- **Setup:** a BU1 SPE admin user (165 leaf gates); a second non-admin login (reset `uac.child.user` or another user); a second non-admin WITHOUT the Create privilege (166 f).
- **Before the CIAM session (status unknown):** `POST /api/v1/external-access/invite-and-grant` returned 500 on 2026-09-25, with two 500 paths (onboard vs grant). Reproduce with an ALREADY-invited email (a fresh one sends a real invitation). Nothing records a fix.
- **API smoke checks still owed (dev writes on test records):**
  - 098: `set-record-share-expiry`, then read back `sprk_expiresdate` for the exact date.
  - R15: create and read through `POST /api/v1/external/projects/{id}/documents`. The field names and the case-sensitive `sprk_Project@odata.bind` have never run live, and gate 23 now locks `sprk_graphdriveid`, which that route writes.

## Branches and environment
- **Work branch** `work/unified-access-control-r2` holds notes and tasks only, and is **314 commits BEHIND master** (2026-10-06). Never build here (CLAUDE.md).
- **Master and dev BFF:** `cc96ea6d7` (#1332). Deployed today, in order: #1312 → #1314 → #1319 → #1320 → #1322 → #1327 + #1328 → #1332. Deploy from a fresh short-path master worktree.
- **171:** `task/uac-r2-171`, worktree `C:\wt171`, master merged in; builds clean; 1,950 overlap-area tests + 809 arch tests pass after the merge.
- **Live gates:** run as `testuser1@spaarke.com` (non-admin, BU1, `sprk_isexternal` = false). Token via `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`. Gate helpers and the per-group results G1–G9 are in the session scratchpad `gates\` (COMMON.md, INVENTORY*.md). The durable record is `notes/batch4-live-gates-2026-10-06.md`.
- **Held by the owner:** the Power BI workspace id stays unset (reporting answers 503); `PowerBi__ClientSecret` stays a plain setting.
- **Kept on purpose:** `C:\wtD\scripts\logs\` holds the batch-4 deploy backups and manifests. Don't remove `C:\wtD`.

## Task status
- **🔄 wip, completes at live gates (15):** 137, 140, 142, 143, 146, 147, 150, 157, 162, 163, 164, 165, 166, 168, 169. Each list above names what is left. 143's gate 14 needs task 154 first.
- **🔲 open:**
  - 171 (PR #1333);
  - batch-5 KEEPs 154, 113, 114, 105, 101, 064, 153, 067, 099, 036;
  - merged-into-gate items 013 (141 G-8), 037/039/136 (CIAM session), 047 (wizard), 058 (090), 066 (067);
  - 090 wrap-up.
- **Completed 2026-10-06:** 003, 132, 133, 148, 149, 156, 158, 159, 160, 161, 167.

## Open follow-ups and issues
- **cpo-r1 hand-offs** INCOMING-141 and INCOMING-145, plus the batch-4 schema check: delivered on #1094 2026-10-06 (issuecomment-6028915048). Track until acknowledged.
- **#1313:** the Model 1 SPE config has no secret. Do NOT mint one (CLAUDE.md; cpo-r1 D16).
- **Other issues:**
  - #1317: insert-template returns raw XSLT (email-r5).
  - #1318: provisioning seeder lookup filter.
  - #1324: notification Condition node.
  - #1325: AI catalog drift.
  - #1326: matter-health-single schema/index.
  - #1293: DRAFT, open; rebase after #1123.
  - #969: CI lane.
  - #974: open.
  - #1290 and #1303–1307, #1310.
- **Minor (f) fix candidates found 2026-10-06:**
  - playbook sharing info shows a revoked team as Read (mask 0 → Read);
  - shared teams show "Unknown Team" (`/api/data/teams(id)` is missing `v9.2/`);
  - a failed push child shows a raw 403 message.
- **Known limits (2026-10-06):**
  - inherited-share ledger rows (158) of a deleted filed work assignment aren't retired;
  - R4 doesn't scan `sprk_invoice`/`sprk_recordtype` grant lookups;
  - 171's limits are listed in its note.
- **#1314 follow-ups:**
  - `/disable` doesn't survive a restart;
  - the `pac` bash shim;
  - `deploy-spaarke-ai.yml` fails ("Could not resolve react");
  - orphan `sprk_OpenDocumentQuickCreate` and the empty "Upload Documents" form;
  - the `Create_Task_From_Email` schema (missing `dueDate`);
  - the thread `sprk_regardingreportcard` fault on send.
- **Unticked items in `notes/batch4-integration-steps.md`:** one F3 check, 6 external-SPA `tsc` errors, the 160 A5 note, and CHANGELOG entries. Reconcile them at 090.
- **Deferred tests and notes for 090:** ISS-032 (search-filter oid regression test), ISS-033 (bare 401 ProblemDetails), in `notes/defer-issues.md`.
- **Housekeeping:**
  - consumed worktrees `C:\wt4i`, `C:\wv*`/`C:\wvs*`;
  - ~169 agent worktrees under `C:/code_files/spaarke/.claude/worktrees`;
  - undeletable `wf_*` folders.

## Key notes
- `notes/session27-owner-decisions-and-research.md` (rounds 1–71)
- `notes/batch4-live-gates-2026-10-06.md`
- `notes/batch5-scope-review-2026-10-05.md`
- `notes/handoffs/INCOMING-141*.md`, `INCOMING-145*.md`
- `notes/handoff-history/2026-10-06-conversion-review.md` (with this session's resolutions)
