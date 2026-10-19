# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 50/79 complete (071 and 072 ✅). 049 in progress. On the branch: the map, part A (foundation), part B (processor steps 1-5), spend D-102/D-104 (ontology PASS at 295aa332a), and the D-125 system policy (ontology PASS with 183c2cf47). Running: impl049c (opus; part C persistence: one ExecuteTransactionAsync, BuildCreateRequest made a list, D-126 held invoice, core-record re-check, OutcomeUnknown re-read) and impl016t (sonnet; D-127 negative totals script plus removing total-below-storable-minimum; D-128 a disallowed credit sums signed), both from base 76c1386e5 |
| **Step** | HEAD b29f85bb1 (local; origin is 33723dbdc; ask the owner before any push). Last full verify at 13094bb8c: build 0/0; broad filter 7653/0; ArchTests 1209/0 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) When C and the totals agent report: cherry-pick, run the BROAD filter (add Lifecycle, Totals, Dataverse, RuleReview, Submission), rewrite hashes in the notes. (2) Brief part D from C's result shape: Signals via the mapper (supersede on a fingerprint change, episode n+1); a Rejected submission's primary comes from 073's deciding findings; held presentation; the D-122 rejection DR resolving EVERY open Signal on the invoice under the IIdempotencyService lock, with a concurrent test; system-policy Signals read by sprk_policycode (enabled + Active, else none); terminal status, indexing, notify, resume. ALSO in part D, from ontology-r1's D-125 review: (a) BillingFindingSignalMapper gives InvoiceNumberMismatch precedence among flag-only reasons, plus a MapAll test with code_set_needs_review; (b) BillingSystemFindings.CodeRefused / FirmCodeMismatch cite an AI code only if it matches `^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$`, otherwise no value, and NotApplicableToLineType gets its own template. (3) Part E: handler (download with the container check, copied from InvoiceExtractionJobHandler), DI, ArchTests (ADR-010 ceiling is 167 after A), publish delta vs fresh master, CVE check, one Opus adversarial review. (4) Queued: the 077 F1-b note in Rule Review |

### Ranges out for review
- None open. ontology-r1 PASS: 072 (3ce07e0e3), 049 D-104 (295aa332a), D-125 (183c2cf47). Send them part D with D-125 (a)/(b).

### Owner decisions this session
D-111 to D-120 are in the decisions log.
- D-117: an expense rule rejects only on the firm-billed code.
- D-118: client workforce users may use the partner portal.
- D-119: a bare "$" is USD.
- D-121: credit amounts may be negative (lower sprk_amount min). D-122: an automatic rejection resolves every open Signal on the invoice. D-123: D-91 / coding findings become Signals under a system billing policy (needs ontology-r1). D-124: new sprk_invoice tracking-number column.
- D-125: SubmissionIntegrity rule type for the system policy; an admin may disable it. D-126: a read but uncoded submission still creates its invoice, held. D-127: negative invoice totals (credit memos). D-128: a reviewer may disallow a credit line.
- D-120: core-record delete. Finance history blocks the delete. A rate card scoped 1:1 to the record is deleted with it; a shared card is kept. Budgets are kept, unlinked, and never block. uac-r2 adopted it.

### Queued owner actions (live writes need the owner's OK)
- Re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1; 073 prompt change).
- The 074 live seam test.
- The IfRowVersionMatches probe (076).
- Run the schema scripts on dev, with the owner's OK and notices before and after to ontology-r1 and uac-r2, BEFORE a BFF that reads them: rejection reason; D-104 sprk_pendingspend; D-121 Deploy-EbillingBillingEventAmountRange.ps1; D-124 Deploy-EbillingInvoiceTrackingNumberSchema.ps1; D-125 rule-type option and Seed-BillingSystemPolicy.ps1 (fix #2107, the owning team, first); D-127 invoice totals range. The pending-spend query shapes are already verified read-only on dev.
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
| **Task ID** | 071/072 (awaiting ontology-r1 re-check); 049 running (two agents) |

## Next Action

See Quick Recovery.
