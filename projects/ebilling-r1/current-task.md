# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 50/79 complete. 049 in progress. On the branch: the map; parts A, B, C and C2 (6cf404162); spend D-102/D-104 and D-130 approved-total spend (1549ff165) with its review round (ae5197e25); the D-125 system policy; credit totals D-127/D-128/D-129. Running from base ae5197e25: impl049sub (sonnet; D-131 Submitted By: invoice sprk_submittedby = the submission's sprk_submitter contact; a missing systemuser no longer refuses, only "neither recorded" refuses) and impl049d (opus; part D after-commit steps: Signals, the D-122 rejection DR after the Signals, system-policy Signals, indexing, notify, terminal status last, resume; D-125 (a)/(b)) |
| **Step** | HEAD ae5197e25 (local; origin is 33723dbdc; ask the owner before any push). Last full verify at ae5197e25: build 0/0; broad filter (+DerivedBaseColumns) 7827/0; ArchTests 1214/0. ADR-010 ceiling is 168 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) When impl049sub reports: pick, run the broad filter, then add D-131 to the owner-actions list (export sprk_invoice.sprk_submittedby, created live on dev 2026-10-19, into SpaarkeMaster). (2) When impl049d reports: pick (the notes file 049-submission-processing-job.md may conflict with impl049sub; keep both sections), run the broad filter, send the range to ontology-r1 (they review part D and D-125 (a)/(b)). (3) Part E: handler; step-0 re-read (Accepted/Rejected → no-op; linked invoice on Processing → resume after-steps); lock invoice-submission:{id:N}; the download with its container check, copied from InvoiceExtractionJobHandler; part D's preflight before the persister; DI for processor, persister, after-steps and handler; ArchTests; publish delta vs fresh master; CVE; one Opus adversarial review; mark 049 done. (4) Queued: the 077 F1-b note |

### Ranges out for review
- ontology-r1: D-130 review round 0a13eab57..ae5197e25 sent 2026-10-19, awaiting their review. Earlier PASS: 072 (3ce07e0e3), 049 D-104 (295aa332a), D-125 (183c2cf47). Part D goes to them next.

### Owner decisions this session
D-111 to D-120 are in the decisions log.
- D-117: an expense rule rejects only on the firm-billed code.
- D-118: client workforce users may use the partner portal.
- D-119: a bare "$" is USD.
- D-121: credit amounts may be negative (lower sprk_amount min). D-122: an automatic rejection resolves every open Signal on the invoice. D-123: D-91 / coding findings become Signals under a system billing policy (needs ontology-r1). D-124: new sprk_invoice tracking-number column.
- D-129: credit memo arithmetic. D-130: rules on uncoded; spend = approved total; submitter stamped; Processing until after-steps.
- D-131 (being recorded by impl049sub): the invoice records the external submitter contact in the new sprk_submittedby (Submitted By, contact lookup, created live on dev); a submitter with no linked systemuser still gets an invoice.
- D-125: SubmissionIntegrity rule type for the system policy; an admin may disable it. D-126: a read but uncoded submission still creates its invoice, held. D-127: negative invoice totals (credit memos). D-128: a reviewer may disallow a credit line.
- D-120: core-record delete. Finance history blocks the delete. A rate card scoped 1:1 to the record is deleted with it; a shared card is kept. Budgets are kept, unlinked, and never block. uac-r2 adopted it.

### Queued owner actions (live writes need the owner's OK)
- Re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1; 073 prompt change).
- The 074 live seam test.
- The IfRowVersionMatches probe (076).
- Run the schema scripts on dev, with the owner's OK and notices before and after to ontology-r1 and uac-r2, BEFORE a BFF that reads them: rejection reason; D-104 sprk_pendingspend; D-121 Deploy-EbillingBillingEventAmountRange.ps1; D-124 Deploy-EbillingInvoiceTrackingNumberSchema.ps1; D-125 rule-type option and Seed-BillingSystemPolicy.ps1 (fix #2107, the owning team, first); D-127/D-129 Deploy-EbillingInvoiceTotalsRange.ps1 (seven columns, including sprk_billingexport total). The pending-spend query shapes are already verified read-only on dev.
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
- **uac-r2:** #2100 (K-i: any Secure-BU team, with task 178).
- **Standing rules:**
  - agents work only in their own worktree, never in the main worktree;
  - run filtered tests; then the broad filter after multi-range picks;
  - remove worktrees with Remove-WorktreeSafe.ps1;
  - only Import-SolutionScoped.ps1 imports;
  - no line upload route before #2047;
  - the dev BFF and SPA deploy only through SPA-r3;
  - never use bare git stash or --no-verify.
- **Open:** G-18 (lint-staged shared stash, not filed); DEF-015; ISS-009; #2010, #2011, #2045, #2077, #2087, #2090, #2100, #2106, #2107, #2108.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 049 (parts D and C3 running; part E next) |

## Next Action

See Quick Recovery.
