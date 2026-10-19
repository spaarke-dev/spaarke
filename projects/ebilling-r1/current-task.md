# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 48/79 complete. 071/072: ontology-r1 re-checked the W fixes, raising F4 (EventId) and F2 (Flag-tie primary); both are fixed in 3ce07e0e3 and that commit is out for re-check, after which 071/072 should PASS. 049: the pipeline agent stopped after writing the step-to-component map (notes/049-submission-processing-job.md, plan parts A to E). The owner answered D-121 to D-124. The spend part has landed (be84b2a8f..bafa0cb9e). Running now: impl049a (sonnet xhigh: ExecuteTransactionAsync, the 014 create-in-status, the notification sink, the D-121 amount script, the D-124 sprk_trackingnumber column) and impl049b (opus: SubmissionReasonCodes, the FinanceBaseValues TODO swap, processor steps 1-5 and the outcome type), both from base bafa0cb9e |
| **Step** | HEAD bafa0cb9e (local; origin is 33723dbdc; ask the owner before any push). Verified: build 0/0; broad filter incl. Spend and Rollup 6196/0; ArchTests 1209/0 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) On ontology-r1 PASS: mark 071/072 ✅ (POML + TASK-INDEX; run check-task-status-drift.ps1). (2) On parts A and B: cherry-pick, run the broad filter, then brief part C (persistence: row builders, ownership and core fields, the transaction, 016 totals, re-evaluate on a CoreRecord change, fault injection) from B's outcome type. Then part D (after commit: Signal supersede, deciding primary, held presentation, the D-122 rejection DR resolving every open Signal under the IIdempotencyService lock with a concurrent test, D-123 system-policy Signals once ontology-r1 agrees the shape, terminal status, indexing, notify, resume). Then part E (handler, DI, ArchTests, publish delta vs fresh master, CVE check, one Opus adversarial review). (3) ontology-r1 is also reviewing the D-104 range and the D-123 system-policy design (asked). (4) Queued 077 follow-up: show the F1-b "lowered to Flag" note in Rule Review |

### Ranges out for review
- ontology-r1: 3ce07e0e3 (072 F4/F2); be84b2a8f..bafa0cb9e (049 D-104, their hot path); D-123 system-policy design ask.

### Owner decisions this session
D-111 to D-120 are in the decisions log.
- D-117: an expense rule rejects only on the firm-billed code.
- D-118: client workforce users may use the partner portal.
- D-119: a bare "$" is USD.
- D-121: credit amounts may be negative (lower sprk_amount min). D-122: an automatic rejection resolves every open Signal on the invoice. D-123: D-91 / coding findings become Signals under a system billing policy (needs ontology-r1). D-124: new sprk_invoice tracking-number column.
- D-120: core-record delete. Finance history blocks the delete. A rate card scoped 1:1 to the record is deleted with it; a shared card is kept. Budgets are kept, unlinked, and never block. uac-r2 adopted it.

### Queued owner actions (live writes need the owner's OK)
- Re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1; 073 prompt change).
- The 074 live seam test.
- The IfRowVersionMatches probe (076).
- Run the schema scripts on dev before a BFF that reads them: rejection reason; D-104 sprk_pendingspend (Deploy-EbillingPendingSpendSchema.ps1; rollups 400 without it); D-121 amount min; D-124 tracking column. Then a read-only live check of the pending-spend FetchXML shape.
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
- **Open:** G-18 (lint-staged shared stash, not filed); DEF-015; ISS-009; #2010, #2011, #2045, #2077, #2087, #2090, #2100.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 071/072 (awaiting ontology-r1 re-check); 049 running (two agents) |

## Next Action

See Quick Recovery.
