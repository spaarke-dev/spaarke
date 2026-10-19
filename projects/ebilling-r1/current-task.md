# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 48/79 complete (073 and 074 newly ✅). 072 and 071 wait on two reviews of part W: our Opus adversarial pass on E+W (running), and ontology-r1's re-check of E (46c45e4bc..2b7c6a228) plus W (ffe12af4b..97b51f619). #2095 is done (uac-r2 PASS on a69e703fc..5abdaf7fb and c7c53ec8b) |
| **Step** | Branch HEAD ba0f697f0+ (local; origin/work/ebilling-r1 is 33723dbdc; ask the owner before pushing). origin/master 1473b34c6 merged (11d3b7103, then bf5ca1305). Verified on the combined tip: build 0/0; broad billing filter 6062/0; ArchTests 1207/0. The full unit suite's only failures are 3-minute seam/contract timeouts under load (they pass when rerun on a quiet machine) |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) On W review findings: brief a fix agent (base = branch tip, in its own worktree), cherry-pick, run the BROAD filter (memory: verify-after-parallel-picks), send to ontology-r1. When both reviews are clean, mark 071 and 072 ✅ (POML status + TASK-INDEX "✅ [done]"; run check-task-status-drift.ps1). (2) Then start 049 (opus, FULL; read all its constraints, which have grown a lot). (3) Queued small 077 follow-up: show the F1-b "consequence lowered to Flag" note (stored in sprk_proposedaction) in the Rule Review read model and page. (4) Comment on #2095 that the fix is on work/ebilling-r1 and closes on merge |

### Ranges out for review
- ontology-r1: 072 E+W, as above.
- Their earlier re-checks were clean: 073 fix round (plus our K1/K2 fix 9bbe8c0f4) and the 074 follow-up (plus our hash fix 18b6390dc).

### Owner decisions this session
D-111 to D-120 are in the decisions log.
- D-117: an expense rule rejects only on the firm-billed code.
- D-118: client workforce users may use the partner portal.
- D-119: a bare "$" is USD.
- D-120: core-record delete. Finance history blocks the delete. A rate card scoped 1:1 to the record is deleted with it; a shared card is kept. Budgets are kept, unlinked, and never block. uac-r2 adopted it.

### Queued owner actions (live writes need the owner's OK)
- Re-sync the drafting Action row (scripts/ebilling/Ensure-ActionRow.ps1; 073 prompt change).
- The 074 live seam test.
- The IfRowVersionMatches probe (076).
- Run the schema scripts (rejection reason; D-104 Pending Spend) on dev before a BFF that reads them.
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
| **Task ID** | 071/072 (awaiting W reviews); next 049 |

## Next Action

See Quick Recovery.
