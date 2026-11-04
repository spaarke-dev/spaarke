# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file (checkpoints #88–#90, 2026-11-03/04).

> **Last Updated**: 2026-11-04 ~13:00 local (checkpoint #91: core-pair fix round done ce2bed5f70, pass 2 next; duplicate sessions closed; many peer rulings).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green: `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys and settings changes need the owner's go IN THIS SESSION. Peers (ebilling, field-mapping) take the owner's go for THEIR live writes in THEIR own sessions. |
| **Active task** | None in progress. Reviewing and gating for peers; orchestrating dev BFF deploys. |
| **Dev state** | BFF = **d763dc120e** since 2026-11-03 22:26:31Z (R3's final deploy; my gate PASSED: `$TEMP/uacmerge/uacg/gated763_30m.txt`, script `gated763.sh`). Rollback: R3's `deploy/rollback/wwwroot-20261103T220358Z` (kept until my next deploy passes its gate) or redeploy 5796774091. Dev SPA = b083d62183. |
| **Next action** | 1. Core-pair fix (C:/wtcore, `fix/uac-core-pair-update`, commits f90529a016, 2b8667a691, ce2bed5f70, NOT pushed): launch adversarial pass 2 (Opus, security); allow-list entry TopicRegistryWorkProductPersister stays K (pass 1: host types fixed allow-list, corerecordid 100 chars vs JSON envelope). Then push, PR (neutral wording; Router + Client lint green; merge-tree; squash --match-head-commit), queue for next dev deploy. 2. Owner: Email to AP diagnostics still owed (URL, InPrivate, ribbondebug Command checker on the Completed batch 14680609). 3. Reviews as diffs arrive: fm 042c (B' + K2 order theory), fm 031, ebilling 089e, 082b-d, 095d-1. |

## Dev BFF deploy role (owner confirmed 2026-11-03; R3 rollout record §17, PR #2554)
- **Each deploy:** owner's go per build in THIS session; peer OKs on the master SHA; pre-checks (`Set-ChildRecordCreatorPersonSchema.ps1 -Verify`, 225-name settings check, ebilling's 6 keys, both write switches true); `scripts/Save-BffDevRollbackSnapshot.ps1`; deploy; `scripts/Test-BffDevDeployReadback.ps1`; my 30-minute gate (copy `gated763.sh`); record in `docs/procedures/dev-bff-deploy-coordination.md`.
- **Queued for the next deploy:** PR #2550 (R3: restores ExternalUserContextEndpoint `</summary>`; comments/tests only); #2551 (field-mapping 020, aef86b89f7; reviewed OK with notes); #2562 (field-mapping 042b, 0d4aa87ee0, my AC15 OK with notes; new fail-closed 409s per their §15.4 rows 5-8; they run read-only App Insights checks after the deploy). field-mapping post-deploy checks: 5xx on POST /api/v1/child-records/*; test-row creates need the owner's OK in field-mapping's session.
- **ebilling's master PR** (wide: 60+ files incl. 086/089a/089b/089d/095a-c/hand-made batch fix/app sync). 095c-5 + 089c merged into ebilling work 85c835dad0 (2 passes running). **Binding pre-check (095c-5):** sprk_invoice.sprk_firmhold must exist on dev (field-secured, expected profiles) BEFORE the BFF build carrying 095c-5; absent = 503 on approve/Return/correction/AP export, so do not deploy. -WhatIf backfill count 0. When it comes: review the whole PR diff; deploy request must carry the pre-deploy count of Stored Files on invoices of Secure records (dev: 1) and the K4 backfill-after-gate if > 0; `StoredFileReconcileEnabled` stays false until that gate and the NFR-05 census pass; 095c-4 approval refusal is a precondition before P2 is on anywhere. Order after the BFF gate: `Deploy-EbillingFormsViews.ps1` (legacy CSV/JSON Document fields off the batch form; owner go in ebilling's session).
- **Watches:** field-mapping FR-40 live proof after the next real upload/create; word-add-in Office smoke after the owner's two Outlook saves; SPA registration message (85fbba3f64) waits for the next SPA deploy (`deploy-external-spa.yml` on a tag).

## Email-to-AP test (owner go, 2026-11-03)
- AP address `sprk_apexportemail` = **ap@finance.spaarke.com** (settings row 92acd624). Not receive-enabled; must never be. Memory: test-mailbox-invoices.
- Demo export OK: batch **14680609-b198-48e2-8391-3ada84fda689** (21:55:05Z, Completed, 2 invoices, $20,517.83, owned by BU1 team; 2 Stored Files csv/json). Failed batches f6788e95 (root caller) and ac07ec43 are kept.
- Owner added the billing tables in the Matter Management app designer at 22:13Z; ebilling synced source to match (reviewed OK, no role-map change).
- Billing Exports ribbon hides applied by ebilling 04:36Z (owner go in ebilling's session); I verified: 7 hidden controls absent from Form/HomepageGrid/SubGrid ribbons; 4 ApExport form buttons present. Rollback `%TEMP%\BillingExportRibbons-rollback-20261103233623`.
- Owner still sees no ApExport form buttons after clearing the cache (11-04). Server side verified OK: published Form ribbon has all 4 buttons + rules; script/icon present; batchIsCompleted returns true (VM run of live JS); no appaction override; both imports published. Published = unpublished script (same hash); only one main form, carries sprk_exportstatus. Asked owner for: URL (org/app), InPrivate retry, `&ribbondebug=true` Command checker on Email to AP. ebilling told.
- ebilling AP export owner-unit fix (82786ce7f3..fad65a79ad): four-point check PASSED; live BU1 proof comes with the first export after its deploy (record in 153's notes).

## Done this stretch (owner go in this session)
- invoices@ (6d013768) and ap@ (33a198ff) systemusers moved root → BU1 (PATCH 204; read back: BU1, no roles). Owner rule: mailbox accounts go in BU1. Root-unit rule already in INCOMING-145 T4/T5; dev root default team holding Basic User is an accepted dev artifact (round 5).

## Reviews owed / in flight
- **ebilling** (`spaarke-wt-ebilling-r1-7b`):
  - **095c-5** column APPLIED on dev 2026-11-04 14:16Z (owner go in ebilling's session): my read-back: sprk_invoice.sprk_firmhold String, IsSecured; SysAdmin 4/4/4, Writers 4/4/4, Readers 4/0/0 → pre-check passes. Owner decisions: 089d = option R (my 5 conditions); 503 on missing column accepted. Code (a8cee1ae4e, pass 2 running): field-secured `sprk_invoice.sprk_firmhold`; held = invoice hold OR any linked submission hold; binding: read app-only (OBO returns null), tests unlink/delete/reason+firm rewrite → still held; `sprk_invoice.sprk_vendororg` added to generic writers' refused list. `-Apply` needs owner go in ebilling's session, with notices to me.
  - **089d option R** merged into ebilling work 743bf32010. Role "Billing Rule Reader" (root bed10b10) CREATED on dev 2026-11-04 15:55Z (owner go in ebilling's session); my read-back: 6 copies isinherited 0, 0 users/0 teams each, only prvReadsprk_Policy Deep. Not assigned; live-proof child unit still needs owner go. Earlier 089d (43a5b37051): OK with 2 binding notes (role `isinherited`=0 in -Apply/-Verify/guard; live negative proof before the first customer with a child unit). Who adds members = owner question via ebilling. **Option R** (direct role assignment in the customer top unit's copy; needs `EnableOwnershipAcrossBusinessUnits`) is acceptable: the census anchors direct grants at the role copy's unit (`SecureBuRoleDepthAssertion` :801, :999). Conditions sent: org setting is a precondition plus -Verify; holder checks; find any BFF code grading reach by home unit; live proof stays.
  - 089b/089c full review (Exclude keeps Read on `sprk_policies`, fail-closed 403; Billing Rule Readers team is 089d).
  - **095d owner decisions (2026-11-04, my session, relayed):** option 1 (apply then record; stored decision in the apply transaction; 503 incomplete + repeat finishes; firm_missing Signal open until record; census reports applied-no-record); AI re-runs only where a rule's outcome can change; rename all five number places; FINAL REVIEW before merge (their passes + my full review + ontology K-3).
  - **089e** (task/ebilling-089e; owner approved in ebilling's session): rule-set by-id routes (9) = caller Read on set (A) + record access evaluation incl. Restricted/Secure vetoes for record-scoped sets (B), uniform 404 else, 409 `ruleset.record_scoped` only after both; Billing Admin gets Read sprk_procedure Deep (dev apply with notices). Review diff when SHA arrives.
  - **082a** reviewed OK with notes (extraction equivalent; disclosure test for Secure parent in reader output → 082b/c; NoLock comment; access-suite copies of pair-id tests).
  - 095a PR conditions; 095c schema notice; 095d full review (Write on invoice + AppendTo on firm; core from invoice; Unreadable 503; assign only, In Review only; typed writer in ParentLineage).
  - Accepted known limits: 086 K1/K-P1 (my 191)/K-P2/K-P3; 089a+086 K3/K4; Add Existing Communication on AP emails subgrid.
  - Owner decisions taken in ebilling's session: no-firm invoices stay externally visible; Billing Administrator may read rules org-wide (delivered by 089d, not Global).
- **field-mapping** (`spaarke-wt-field-mapping-server-write-pa-5c`):
  - **042b** merged as #2562 (0d4aa87ee0) with my verdict line; row 12 added. **042c** (#2561: core pair via `CallerWriteAuthorization.PairTargetAsync`, core first in the owner question) under way; AC2 amended (Secure core → 409 NoOwnerSource as row 8, OwnerDecided unchanged) OK'd by me with 4 conditions (AppendTo before owner question, uniform refusal for Secure/nonexistent/unknown type/ordinary + catalog fault 503; 409 only with AppendTo; no-core + writers must-not-fire; BU-Y parity). Owner accepted the Secure-core refusal (fm -69). Fix-round rulings sent: additive `coreRecord` on ForRow OK (6 conditions: CorePairTables-only, core first, null byte-identical, equality vs ForExistingRowAsync, no ratchet entry, doc cite); blank core id → 400 OK pending owner; DI-P3 K1 accepted. **Private (not public):** fm reported one further core-record item; details by message only; I classify and take to the owner. Diff to me before merge.
  - **#2561** class F, follow-up task right after 042b: core record first in step 7's owner question (catalog) **plus** the caller's AppendTo check on the named core (confirmed missing). Tests: Secure core + ordinary subject → core's owner; no AppendTo on core → refused; agree → unchanged; Signal and DR each.
  - **Owner decisions in fm's session 2026-11-04 (conditions sent):** 031 `RouteAuthorizedLookupColumns` (closed allow-list, route-set only, uniform refusal; review before merge); 042c blank-id → fill from 6½ plan or clear type, FAULT = 503 never clear; 044 field security on core pair (profile change routed to me, owner go here); 045 regarding-pair inventory; 046 roles matrix → role changes to me, owner go here per change set.
  - 042a; 043 (create-path link write, #2524 sibling); any #2523 change.

## My own work
- **Core pair fixed after create (private item, owner go 2026-11-04, tracked privately only):** branch `fix/uac-core-pair-update` in C:/wtcore (from 0d4aa87ee0); Opus implementer running (update-only `RecordTableRules` rule; chat update tool, playbook Update Record node, DataverseUpdateHandler if it takes caller payloads; must-fire/must-not-fire; shape-only). Second commit: SecureBuRoleDepthAssertion :712-714 comment (ebilling flagged). Then: one adversarial pass x2 (security), PR with neutral wording, next dev deploy. Native edit path (field security) = later owner decision.
- **#2560** (replaces deleted #2559; before task 191): share mirror passes only Read (+AppendTo if proven needed) onto Secure-owned rows of `RecordOwnershipResolver.RestrictedContentTables` (`RecordShareLevels.ChildMirrorMask` / `SecureChildShareSynchronizer`); must-fire/must-not-fire tests; census check. Keep public wording neutral (no scenarios).
- **#2524:** task 195 re-file core writes the `sprk_Project_Matter_nn` link for the new matter (app-only, idempotent, Warning on failure, after AppendTo).
- Gate null-column hardening (`SecureRootFilingGate` with `WritesOf` callers `DataverseUpdateRecordHandler:299`, `ProjectMatterFiling:256`).
- Census item: Secure Record Owner role holds 4 SharePoint privileges outside the role file; origin unknown.
- #2543 flaky provisioning timeout test; #2292 open (fails closed); K-P1 for task 191.
- Parked, branches pushed: 193 (C:/wt193 @ 1bccd88c81), 197 (C:/wt197), #2427 (C:/wt2427), 196a/196b.
- TASK-INDEX stale: reconcile at wrap-up 090.

## Owner items outstanding (this session)
- Email to AP click on batch 14680609 (see top).
- 095a: may a reader see the lookup name of a Restricted .eml?
- Click-test 195; Q1 private items (`scratchpad/q1_owner_private.md`); Q4 private clean-up; whether chat should send invites (through field-mapping).
- Older: #2343, #2245, #2250, #2260, K5/#2270, #2262, #2396; 191 F1/S4; 190 Apply; #2382 UAT check 5; #2505. Waiting-on-owner tasks 140, 165, 153. Optional: move the admin account into BU1.
- ebilling's held notice to customer-provisioning (session 09) needs the owner to release it (permission modes).

## Peers
- (2026-11-04 restart; duplicates closed on owner go) this session = `spaarke-wt-unified-access-control-r2-20`; ebilling `-c7`; field-mapping `-69`; ontology `-38`; provisioning `-b8`; word-add-in `-7b`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; R3 closed (`spaarke-wt-spa-external-access-platform-f2`); others: word-add-in, provisioning, audit `spaarke-f6`.
