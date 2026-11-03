# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-11-02: BU1 move closed).
> - **Defer list:** `notes/defer-issues.md`. **History:** git log of this file (checkpoints #88–#89 on 2026-11-03).

> **Last Updated**: 2026-11-03 ~21:30Z (checkpoint #89: dev-deploy hand-over from R3 confirmed by the owner; Email-to-AP test in progress).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green: `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys and settings changes need the owner's go IN THIS SESSION. |
| **Active task** | None in progress. Reviewing and gating for peers. |
| **NEW ROLE (owner confirmed in this session, 2026-11-03)** | **uac-r2 orchestrates dev BFF deploys** after R3's final deploy and my 30-minute gate on it. Procedure: `docs/procedures/dev-bff-deploy-coordination.md`. Scripts: `scripts/Save-BffDevRollbackSnapshot.ps1` and `scripts/Test-BffDevDeployReadback.ps1`. Record: R3 `notes/contact-identity-dev-rollout-record.md` §9–16. All arrive on master with PR #2546. R3 (session `spaarke-wt-spa-external-access-platform-f2`) runs one last deploy: merged #2546 master SHA = 5796774091 plus 2 R3 fixes. Peers' OKs, then the owner's go. Acceptance and the conditions below were sent to R3 at ~21:35Z. |
| **Dev state** | BFF = **d763dc120e** since 22:26:31Z (R3's final deploy; my 30-minute gate PASSED, `$TEMP/uacmerge/uacg/gated763_30m.txt`, script `gated763.sh`; rollback: R3's snapshot `wwwroot-20261103T220358Z`, or redeploy 5796774091). **From now on I orchestrate dev BFF deploys.** Dev SPA = b083d62183. The census has reported findings since 19:45Z. Cause: the two new mailbox user accounts in the root business unit (see owner items). This predates the deploy. |
| **Next action** | 1. Owner decision on the two mailbox users (owner items). 2. Wait for ebilling's Email-to-AP send time, then run `$TEMP/uacmerge/uacg/apsend_check.sh SINCE UNTIL BATCHID` and report to the owner. 3. Review diffs as they arrive (list below). 4. #2524 re-file link write (my code). |

## Email-to-AP test (owner go, 2026-11-03)
- AP address: `sprk_apexportemail` = **ap@finance.spaarke.com** on settings row 92acd624 (read back 20:42:32Z). Not monitored: receive-enabled accounts are mailbox-central@ and testuser1@ only. ap@ must never be receive-enabled; invoices@ is free to become intake. Memory: test-mailbox-invoices.
- Both invoices are **Approved** by the owner's script: INV-2025-05166 (9723a5aa) and INV #2025023029 (7c200dd1).
- The first export (20:43Z) failed with 503: the owner's account 1d02f31c is in the ROOT business unit, so the CSV Stored File's hard-deny owner was refused (`AnswerIsRootBusinessUnit`). Failed batch f6788e95 is kept, owned by the root default team, with no file (owner: leave it).
- **Demo export succeeded:** batch **14680609-b198-48e2-8391-3ada84fda689** (21:55:05Z, Completed, 2 invoices, $20,517.83, owned by the BU1 team). **Blocker:** `sprk_billingexport` is in no app on dev; the source has it in the Matter Management AppModule.xml:49 and sitemap `subarea_eb_export`. The ribbon is deployed (13:35Z). Asked ebilling to prepare the app-module and sitemap import; the owner's go comes in this session. The owner has a direct form link to try.
- **Owner decision (done):** demo@demo.spaarke.com runs `-ExportOnly` (export only, no approvals). The owner then opens the batch as themselves, checks the AP emails grid has no New button, and clicks Email to AP. ebilling sends me the time and batch id.
- ebilling's fix for the 2 findings (a 409 `ebilling.export.owner_unit_not_allowed`, and an owner pre-check before any write), diff 82786ce7f3..fad65a79ad, merged to work/ebilling-r1 at a6ff149cd9: **four-point check PASSED**, sent ~21:40Z. `sprk_billingexport` is not an ownership parent, so the pre-check never reads the batch that doesn't exist yet. `no_default_team` and `acting_user_unresolved` stay 503. Note: live proof for a BU1 caller comes with the first BU1 export after the deploy (record it in 153's notes). The fix reaches dev only through a master PR and a deploy that I orchestrate.

## Dev deploy role: items handed over by R3 (hand-over complete; gate PASS recorded in R3 rollout record §17, PR #2554)
- **Each deploy:** the owner's go per build in THIS session; peer OKs; pre-checks (`Set-ChildRecordCreatorPersonSchema.ps1 -Verify`, the 225-name settings check, ebilling's 6 keys, both write switches true); readback; my 30-minute gate; the record in `docs/procedures/dev-bff-deploy-coordination.md`.
- **Next deploy carries** PR #2550 (restores the ExternalUserContextEndpoint `</summary>`; ledger cite :46/:75; comments and test text only).
- **Rollback for d763dc120e:** R3's snapshot `deploy/rollback/wwwroot-20261103T220358Z` (kept until my next deploy passes its gate), or redeploy 5796774091.
- **Watches:**
  - ebilling 095a/095b: answered at bf85f93aee. ExternalInvoiceVisibility is unchanged against master and does not read `sprk_sourceemaildocument`, so a restricted source email still hides the invoice through the Stored File. Any later change that makes it read that column is an access change for me and the owner;
  - ebilling's Matter Management app source sync (f5354caebf..bf85f93aee): reviewed OK. It adds only entity components and the `area_finance` sitemap; no role-map change;
  - field-mapping FR-40: live proof after the next real upload or create;
  - word-add-in: the Office smoke test after the owner's two Outlook saves.
- **Not deployed:** the SPA registration message change (85fbba3f64), due with the next SPA deploy (`deploy-external-spa.yml` on a tag).
- #2543 (my provisioning timeout test flakes under load) and FAILURE-MODES G-24 are on my list.

## Reviews owed (waiting for diffs)
- **ebilling:**
  - 095a PR: my 4 conditions plus the attribute and relationship in source;
  - 095c: schema notice; the tracking-scope column is refused in ParentLineage with a typed writer only, and is never a visibility filter; I need the key columns, the backfill and the race behaviour;
  - 095d: full review. Conditions: Write on the invoice plus AppendTo on the firm; the core record derived from the invoice; Unreadable is 503; assign only, In Review only; ParentLineage names the typed writer;
  - 089b/089c: full review. Gate: step 0, record Write/AppendTo, Read on the Procedure, `Admin()`; change rule adds `CreateRuleSet()` plus the policy Create privileges;
  - the NFR-05 census after the next deploy;
  - 086's follow-up adds `prvReadsprk_StoredFile` to `config/secure-record-owner-role.json`.
- **field-mapping:**
  - 042a, and 042b with a full review;
  - 043 (the #2524 create-path link write);
  - any #2523 change to how grants run.
  - 041 merged (#2541) and 020 is OK with notes.

## My own work
- **#2524:** the task 195 re-file core writes the `sprk_Project_Matter_nn` link for the new matter: app-only, idempotent, Warning on failure, after the AppendTo check.
- **Gate null-column hardening:** `SecureRootFilingGate.CheckAsync` and `PlanCreateAsync` must see null-valued named columns. Callers that pass `WritesOf`: `DataverseUpdateRecordHandler:299`, `ProjectMatterFiling:256`. Defense in depth: those PATCHes run as the user under field security.
- **Census item:** the Secure Record Owner role holds 4 SharePoint privileges outside the role file (SharePointData C/R/W Global, SharePointDocument R Global); their origin is unknown.
- **#2543:** flaky provisioning timeout test (real-clock 30 s).
- **#2292:** open; fails closed.
- **Parked; branches pushed 2026-11-03:**
  - 193 (C:/wt193, fix/uac-193-add-person @ 1bccd88c81);
  - 197 (C:/wt197);
  - #2427 (C:/wt2427);
  - 196a/196b.
- **TASK-INDEX is stale** (196/197 missing; many statuses old): reconcile it at wrap-up 090.

## Owner items outstanding
- **NEW (private, details in session only):** what to do with the invoices@ and ap@ Dataverse users (root business unit, no direct roles): move them to BU1, disable them, or other. Until then the census keeps reporting its verdict.
- Owner decisions:
  - no-firm invoices: hide them externally until a firm is set? (Today they are visible, by option A.)
  - 095a: may a reader see the lookup name of a Restricted .eml?
- Click-tests: the 195 click-test, and the AP emails grid with no New button (in today's test).
- Q1 private items (`scratchpad/q1_owner_private.md`) and the Q4 private clean-up.
- Whether chat should send invites (through field-mapping).
- Older decisions:
  - #2343, #2245, #2250, #2260, K5/#2270, #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5;
  - #2505.
- Waiting-on-owner tasks: 140 (CIAM identities), 165 (SPE Admin role and write probes), 153 (banner UI checks).
- Optional: move the admin account into BU1 (root-BU creates of customer rows fail closed).

## Peers
- R3 (closing): `spaarke-wt-spa-external-access-platform-f2`.
- ebilling: `spaarke-wt-ebilling-r1-7b`.
- field-mapping: `spaarke-wt-field-mapping-server-write-pa-5c`.
- ontology: `spaarke-wt-spaarke-ontology-platform-r1-be`.
- Others: word-add-in; provisioning; audit `spaarke-f6`.
