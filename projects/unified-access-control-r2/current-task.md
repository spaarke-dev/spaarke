# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-11-02: BU1 move closed).
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-11-03 ~14:10Z (checkpoint #88: dev BFF 5796774091 gate PASSED; ebilling ApExport UI on dev; K3 role grant in progress).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green: `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys and settings changes need the owner's go. Shared-dev deploys go through the R3 protocol. |
| **Active task** | None in progress. |
| **Dev state** | BFF = 5796774091 (master, #2526 = ebilling 151c Email to AP) since 2026-11-03 13:29:19Z (R3 deploy; rollback 3740e1a105; my 30-min gate PASSED, log `$TEMP/uacmerge/uacg/gate5796_30m.txt`). AP export routes and the server-only send were NOT exercised in the window: ebilling sends me the time of the first real Email-to-AP send on dev, and I check that operation (route status, server-only send line, no OWNERSHIP-REFUSED). Dev SPA = b083d62183. Deploy checkout C:/wt2468, detached at d0c1c5dc3c. |
| **BU1 move** | **Closed 2026-11-02** (record: `notes/decisions.md`).<br>• In BU1: every interactive human except the administrator 1d02f31c, plus both dev BFF app users (roles = BU1 copies of the same set, profiles unchanged by id).<br>• Customer families: 684 rows assigned to the BU1 team; 119 children followed through the reconciler.<br>• Product configuration: 263 rows went to the root team. The 34 playbooks stay with 1d02f31c until #2505.<br>• Step 3 not done, by owner choice: a self-move needs `DoNotRemoveRolesOnChangeBusinessUnit`, so the administrator stays in the root.<br>• Rollback source: `$TEMP/uacmerge/uacg/rbu1/rbu1s0_snapshot_*`. |
| **Next action** | (0) The #2483 addendum verdict was sent to field-mapping (2026-11-02): OK with conditions (a)–(c); Q1 accept the 409 with no exemption; Q2 accept, skip visible; Q3 fix in 042b. #2502 §14 addendum (13c9e89cf9): OK. Routing was never an access control; the boundary is `IRetrievalAccessTrim` at each consumer. Conditions: reword line 123; keep the log at Information until W3, then Warning; close #2206/#2413 on 041's merge after the dev count of non-default stamps; reconcile 042b's step-8 bullet with 041. Waiting for: the 041 diff (scoped check: no trim call moves) and the 042a/042b diffs (042b gets a full review). (1) Review field-mapping's 039/040 diffs and its #2483 follow-up when they arrive. (2) Verify the state of the parked items below. (3) The 195 owner click-test stays on the checklist. |

## Done this stretch
- **#2506:** a test-seed defect (the pair type was seeded as a string; dev has a lookup). Fixed by PR #2509, merged at c0831038d0, with ontology's review. Issue closed; C:/wt2506 removed.
- **ebilling 151c-1a (Email to AP):** three review rounds.
  - Round 1 found F2 (re-ingestion), F3 (a test proving a stub) and F4 (owner = BU default team).
  - Round 2 found F2' (a fault in the account query let the send through) and F4' (the digest dropped attachments with no inline bytes).
  - Round 3 passed. ebilling merged it into work.
  - Owner decisions on their side: Write on the batch is required; both re-ingestion measures; E1 exception EB-D-184.
- **ebilling #2507 K1:** OK for the access gate (the fallback to the caller is kept as a known limit, accepted by ontology).

## Gates and peers
- **R3:** dev BFF = 3740e1a105 since 2026-11-02 18:52:25Z (R3's deploy; rollback fcf01480f4). My 30-min gate PASSED and was reported to R3 (log `$TEMP/uacmerge/uacg/gate3740_30m.txt`). Census follow-up resolved: the drift is project 1cccf07d (Office quick-create, MAT-000032). No BFF path writes the `sprk_Project_Matter_nn` link; it is display-only and confers nothing. Agreed with field-mapping: the server writes the link once the pair is set. Field-mapping owns the create path (`RecordCreationService` step 12; I review their diff). I own the task 195 re-file core (chat update tool). Filed as #2524; field-mapping links it to their task 043. My part, the re-file core link write, is still to do.
- **Field-mapping 5c:** the junction exception they reported is #2292 (pre-existing; sighting added). #2522 noted. #2523: any change to how the grants run comes to me before merge.
- **ebilling:** 151c-2 CLEARED, and LIVE on dev 2026-11-02 (ebilling session 7b, with the owner's OK): the link column, with its cascade read back as agreed; the ribbon script set to master + `NO_NEW_HOSTS` (checked against master); the batch form's `subgrid_apemails`. Open: a browser click-through, by ebilling or the owner. Pass = no New on a batch's AP emails grid; Add Existing may show (K1). PR #2526 (151c-1a, 1b and 2 → master, head 530286e428) matches what I passed. I told ebilling that 1b was NOT reviewed by me: it is UI only, with no client writes, so no access gate is needed; they are rewording the PR body. Merged to master at 5796774091 (ontology OK). Dev BFF runs it since 5796774091 (2026-11-03).
- **Field-mapping:** #2504 reviewed OK. 039/040 diffs to come.
  - **041 scoped check: PASS with notes** (2026-11-03, 6275553d84..f6124e8fab). Only write-time routing was removed; no trim or search-time selection moved.
    - Note 1: keep the `sprk_document` trigger-2 case as an owner-only test (fix in 041).
    - 020 (Matter `sprk_issecure` refused in the service, as for Project): no objection; the diff comes to me before merge.
- **ebilling ApExport UI (151b, 151c-1b) on dev 2026-11-03** (owner OK): ribbons-only scoped import plus views. My readbacks 1–4 passed: batch form still has `subgrid_apemails`, link cascade unchanged, `NO_NEW_HOSTS` intact. The 7 undeployed task-014 invoice HideCustomActions were left out; ebilling noted it on 014 (21aec9a10b). The owner added `sprk_invoice.sprk_trackingnumber` (Text 100); the 10 missing-column faults and 2 transaction_failed dead-letters at 13:39–13:43 come from that (#2529). I asked ebilling whether the dead-letters need a replay.
- **ebilling 095a (Outlook "already submitted" banner):** new `sprk_invoicesubmission.sprk_sourceemaildocument` (a lookup to the .eml document, RemoveLink, written only by the BFF). No field security is needed. My four conditions are binding in 095a's brief:
  1. not on external outputs (null it in `ExternalView`, with a negative test);
  2. the `internetMessageId` route answers only what the caller can read: an unreadable match looks exactly like no match, and a read fault returns 503;
  3. if the column can ever differ from the stored file's `sprk_sourcedocument`, it joins the `ExternalInvoiceVisibility` source set;
  4. it stays out of the family, lineage and resolver lists.

  Owner decision (through ebilling): whether a submission reader may see the lookup name of a Restricted .eml, the same class as `sprk_document`. Column live on dev; relationship 20762a86 read back (Delete and Archive RemoveLink, rest NoCascade). Binding on ebilling: the 095a PR carries the attribute and relationship in source; any backfill is same-source, id-only, has -WhatIf, and needs the owner's OK plus a before-notice to me. Waiting for: the 095a PR review.
- **ebilling 095c/095d (no-firm Outlook invoices; assign-firm later, D-201)**, verdict sent 2026-11-03.
  - (1) is only partly right. Only the SUBMISSION is firm-gated (`ListAsync` In, read-one). The invoice, its family rows and its documents are scoped by matter/project (option A, task 187 D2), so a no-firm invoice is externally visible from creation. A null-firm hide is a new rule and the owner's call.
  - (2) needs no new visibility rule. Conditions for 095d:
    - Write on the invoice plus AppendTo on the firm;
    - the core record is derived from the invoice;
    - CheckFirmLinkAsync: Unreadable is a 503;
    - assign only (refuse if a firm is already set; In Review only);
    - ParentLineage's refused submission columns get the typed writer named, and the generic refusal stays;
    - app-only writes go through the allocator;
    - tests.
  - 095d gets a full review.
- **ebilling 089 (record Procedure writes)**, answered 2026-11-03.
  - The gate:
    - step 0 (refuse callers flagged external);
    - Write and AppendTo on the record (gate variant);
    - Read on the Procedure;
    - `BillingAdminAuthorization.Admin()` on assign, pin, exclude and remove.
  - Change rule also needs `CreateRuleSet()` and Create on policy/policyversion.
  - N4 covered only the Procedure lifecycle row; ebilling rewords v5.2 #4.
  - EB-D-158 covers the 089 live steps on these conditions:
    - I see the -WhatIf before -Apply;
    - adds on the four 089 tables only;
    - any removal, deeper depth, other table, or another project's pending entry → fresh owner OK.
- **PR #2530** (Office Add In User billing reads plus `sprk_trackingnumber` in source): the role part is OK from me, and the dev role was read back. MERGED to master at 1571b0be9d (2026-11-03); src identical to the reviewed head 7a33014d9c.
- **K3, the Office Add In User role (root 9e6af7ee; the 5 BU copies inherit it):** the owner is granting Read on the billing tables: billingexport and billingevent at Deep; billingcode, billingcodeset, ebillingsetting and ebillingoverride at Global. That matches Basic User and Core User, plus the override table, which holds only a name. It was NOT saved as of 14:00Z: I must re-check it with RetrieveRolePrivilegesRole. Source `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Office Add In User.xml` needs the same privileges; that's ebilling's PR, and I review it.

## Parked (verify state)
- task 193 (C:/wt193), task 197 (C:/wt197);
- #2427 output lookups (C:/wt2427; ebilling waits for its sha);
- 196a/196b.

## Owner items outstanding
- The 195 click-test (checklist), plus the 151c-2 batch "AP emails" grid showing no New.
- The owner-private items in `scratchpad/q1_owner_private.md`; Q4 private clean-up.
- Whether chat should send invites (as its own change, through field-mapping).
- Older:
  - decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5.

## Filed this stretch
- #2505: playbook routes authorize by owner == caller, so a team-owned playbook can't be edited, shared or changed.
- #2493, #2494, #2495, #2478, #2487 (earlier).

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
