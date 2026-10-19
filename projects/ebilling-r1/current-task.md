# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 50/79 complete. 049 in progress. On the branch: map; parts A-C, C2, C3 (D-131); part D (bbea1e1f7, 168f43b1c, 8e3930c8d); 650109d97 SignalWriter.CloseActedByAutomatedDecisionAsync (ontology-owned, D-122 close). ontology-r1 reviewed part D: FINDINGS (F4-1 terminal from stored state, F4-2 terminal only while Processing, F1-3 permanent Signal refusals; D-122 hash on pre-D-122 records; tests; K1 IsInForceOn, K2 signalPlan). Running: impl049e (sonnet, base a65b0abdc: part E handler; resume calls IInvoiceSubmissionAfterSteps.ResumeAsync(submissionId, correlationId, ct) with a NotImplemented STUB — take impl049core's version at pick); impl049pre (sonnet, base 650109d97: SignalWriter.PreflightRefusal, ontology-owned, separate commit); impl049core (opus, base 650109d97: A stored terminal + ResumeAsync + totals refusal in findings, B conditional terminal, F IsInForceOn, G signalPlan required, E test fixes) |
| **Step** | HEAD 650109d97 (local; origin is 33723dbdc; ask the owner before any push). Last full verify at 8e3930c8d: build 0/0; broad filter 7889/0; ArchTests 1214/0. ADR-010 ceiling 169. Dev BFF is R3 283466d40 (no 015 e0df77339: the 015 delete script must not run) |
| **Status** | in-progress (parallel mode) |
| **Next Action** | Sequence after impl049core: (1) D-122 wiring: after Signals read the open set on invoice+lines ONCE via the dedupe-key lookup (latest episode, incl. ones Acted by this record), store with setReadAtUtc in findings JSON BEFORE the record; record over stored set; close each via CloseActedByAutomatedDecisionAsync (LeftAsIs listed + Info log; ChangedSinceRead → Retry); existing record with no stored set → empty set (pre-D-122 hash); a listed Signal deleted later → OwnerRefused is permanent, handle it; test fake must tell findings writes from terminal writes. (2) F1-3: code-shape/cap firmCode, the D-91 read invoice number (no spaces, up to 40 chars, else cite no value), raw-text rule facts; run PreflightRefusal on every plan entry at persist; deterministic refusals + permanent skip codes = permanent; a permanently refused Signal does not block the record (log, count, record in findings). (3) Held presentation for Blocked: headline "Invoice held for review", sentence "The rejection rule would reject this invoice: <deciding reason>", Held evidence text ebilling.submission.blocked; CODE_PATTERN pin vs RuleReview ruleTypes.ts:63 (or reword); one after-steps test over the real SignalWriter harness; notes for K4 planner, Held reason stripped on read, K2 raw codes for 130/133, K3 interleaving. (4) Pick part E (replace the stub), full verify, send all to ontology-r1, one Opus adversarial review, publish delta, mark 049 done |

### Ranges out for review
- ontology-r1 PASS: D-130 closed (review round ae5197e25 + re-check fixes 77690aa4f). PASS 2026-10-19: 452acb6c0 PreflightRefusal, 07a2f67b3 D-170 sweep; 650109d97 close method approved once the F3-1..3 tests land. Earlier PASS: 072 (3ce07e0e3), 049 D-104 (295aa332a), D-125 (183c2cf47). Part D 29e91a285..8e3930c8d under their review (C:\wts-eb130); D-122 close path spec received from them.

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
| **Task ID** | 049 (D-122 follow-up running; part E next) |

## Next Action

See Quick Recovery.
