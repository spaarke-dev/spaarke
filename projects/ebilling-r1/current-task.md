# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-20
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. #1911 PUSHED and MERGEABLE (draft); uac-r2 re-checked the census and ceiling. Origin is at d42ae3c40. Local tip 89d027e3b7 is NOT pushed: it holds the human-decision Signal close (8494a9e9f..248995034 plus ontology round-1 fixes 91c156b123..89d027e3b7) and the 050a review-1 fixes (9feb489db, 3e9f56e47). Gates on the local tip are green: build 0/0, ArchTests 1,244/0, unit 9,329/0. D-135 Workforce role applied on dev (658→660). D-136/137/138: rule-set scope model; design note and tasks 080–085 done (ontology-r1 accepted; uac-r2 review queued). |
| **Step** | Agents running from 89d027e3b7: **fixclose3** (ontology 2nd-pass F1-a: 105 must refuse invoiceId != InvoiceIdFor(submissionId); F1-b: refuse a planted policy code; K1 createdon skew; K2 page-2 test; K3 warning) and **fix050a2** (uac-r2 050a items 2–6: conditional document touch in the persister transaction plus the post-commit window, the document_rejected check in the re-file, the already-isolated re-file test and the token-honouring fake, 503 on the pre-intake read, notes; PLUS item 1 per owner D-140: confirm follows the email's matter through sprk_relatedcommunication's regarding, then the parent document; also check sprk_matter vs sprk_relatedmatter). |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) fixclose3 done → pick, gates, send the fix diff to ontology-r1 (scoped re-check) → on PASS, push. (2) fix050a2 done → pick, gates, send to uac-r2 (it also fixes its own resolver owner-write race); record D-140 if the agent didn't. (3) File issues: 105 planted empty set (K); 105 Signal deleted between set store and record → stuck 409 (no shrink path); junk set blocks a decision. (4) After the pushes: 100/105 ✅ only after a real Core User check on dev (F1-2 note) and #1911 merge planning. (5) Owner-open: run Deploy-EbillingOverridePrivilegeSchema.ps1 on dev? Which role holds prvWritesprk_ebillingoverride? FR-39 Return notice? The 080 schema approval when it's ready. |

### Ranges out for review
- ontology-r1 PASS: D-130 closed (review round ae5197e25 + re-check fixes 77690aa4f). PASS 2026-10-19: 452acb6c0 PreflightRefusal, 07a2f67b3 D-170 sweep; 650109d97 close method approved once the F3-1..3 tests land. Earlier PASS: 072 (3ce07e0e3), 049 D-104 (295aa332a), D-125 (183c2cf47). Part D review FINDINGS addressed in pieces; core fixes PASS 5a04a41fb.

### Owner decisions this session
D-111 to D-120 are in the decisions log.
- D-117: an expense rule rejects only on the firm-billed code.
- D-118: client workforce users may use the partner portal.
- D-119: a bare "$" is USD.
- D-121: credit amounts may be negative (lower sprk_amount min). D-122: an automatic rejection resolves every open Signal on the invoice. D-123: D-91 / coding findings become Signals under a system billing policy (needs ontology-r1). D-124: new sprk_invoice tracking-number column.
- D-129: credit memo arithmetic. D-130: rules on uncoded; spend = approved total; submitter stamped; Processing until after-steps.
- Owner 2026-10-19 (to ontology-r1): when a billing policy is turned off or retired, its OPEN Signals close (PolicyRetired-style); closed ones untouched. ontology-r1 asked to own the sweep change.
- D-131: the invoice records the external submitter contact in the new sprk_submittedby (Submitted By, contact lookup, created live on dev); a submitter with no linked systemuser still gets an invoice.
- D-125: SubmissionIntegrity rule type for the system policy; an admin may disable it. D-126: a read but uncoded submission still creates its invoice, held. D-127: negative invoice totals (credit memos). D-128: a reviewer may disallow a credit line.
- D-120: core-record delete. Finance history blocks the delete. A rate card scoped 1:1 to the record is deleted with it; a shared card is kept. Budgets are kept, unlinked, and never block. uac-r2 adopted it.

### Queued owner actions (live writes need the owner's OK)
- Re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1; 073 prompt change).
- The 074 live seam test.
- The IfRowVersionMatches probe (076).
- Run the schema scripts on dev, with the owner's OK and notices before and after to ontology-r1 and uac-r2, BEFORE a BFF that reads them: rejection reason; D-104 sprk_pendingspend; D-121 Deploy-EbillingBillingEventAmountRange.ps1; D-124 Deploy-EbillingInvoiceTrackingNumberSchema.ps1; D-125 rule-type option and Seed-BillingSystemPolicy.ps1 (fix #2107, the owning team, first); D-127/D-129 Deploy-EbillingInvoiceTotalsRange.ps1 (seven columns, including sprk_billingexport total). The pending-spend query shapes are already verified read-only on dev.
- D-131: export sprk_invoice.sprk_submittedby (created live on dev 2026-10-19) into the SpaarkeMaster solution source.
- Add the 7 new tables to the apps.
- Grant Write on sprk_ruleset before 079.

### Critical Context
- **049 constraints** (in its POML):
  - ReceivedOn = submission createdon (DateOnly, UTC);
  - Signal episode n+1 and supersede on a fingerprint change;
  - HeaderConfidence["Currency"];
  - CoreRecord comes from the same submission read that files the invoice (re-evaluate on change);
  - pass the tracking number;
  - build the DR snapshot from the scrubbed mapping;
  - the BillingDecisionRecorder caller obligations;
  - D-104 Pending Spend (ontology-r1 reviews).
- **100:** render billing reasons from codes. SignalReadService strips non-witness evidence text; ontology-r1 will not exempt it.
- **015 deploy sequence:**
  - R3 deployed dev BFF 44f802a1d on 2026-10-19 11:37Z; it still reads sprk_invoicestatus, so it is not an attestation.
  - Remaining: 015 reaches master → a BFF with 015 → -Delete with both attestations (-WhatIf first) → remove the transitional allow-list entries.
- **ontology-r1:**
  - no seeding of dev invoices until REAL-2026-123456.01 is answered;
  - after 074 reaches master they add the immutability ArchTest entry, the 5 comments and the ExistingPolicyCodesAsync comment.
- **uac-r2:** #2100 (K-i: any Secure-BU team, with task 178). Secure Record Owner role: dev holds our 4 Reads (submission, approval, external identifier, correction; 013, D-86, 35ce457f2) that master's config/secure-record-owner-role.json lacks until #1911 merges. uac-r2 will not run the §5.4 "outside master's file" strip until then; no separate PR (agreed 2026-10-19).
- **Standing rules:**
  - agents work only in their own worktree, never in the main worktree;
  - run filtered tests; then the broad filter after multi-range picks;
  - remove worktrees with Remove-WorktreeSafe.ps1;
  - only Import-SolutionScoped.ps1 imports;
  - no line upload route before #2047;
  - the dev BFF and SPA deploy only through SPA-r3;
  - never use bare git stash or --no-verify.
- **Open:** G-18 (lint-staged shared stash, not filed); DEF-015; ISS-009; #2010, #2011, #2045, #2077, #2087, #2090, #2100, #2106, #2107, #2108, #2118 (DEF-026, billing Duplicate detail after a later Secure transition; ontology DI-120).

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | #1911 master merge; then the human-decision close + 050a |

## Next Action

See Quick Recovery.
