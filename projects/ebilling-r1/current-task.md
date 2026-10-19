# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-19
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 48/79 complete. 071/072: the W review fixes are on the branch (4795a4209..c02200029, notes to c342f4b9a); ontology-r1's re-check of that range was requested 2026-10-19. 049 is running as two implementer agents from base c342f4b9a: impl049 (opus; pipeline: handler, processor, sink, DI; D-101 overrides the POML's "Rejected: no invoice") and impl049spend (sonnet; D-102 tests + D-104 Pending Spend in FinanceBaseValues/FinanceRollupService + a scripts/ebilling column script) |
| **Step** | Branch HEAD c342f4b9a (local; origin/work/ebilling-r1 is 33723dbdc; ask the owner before any push). Verified on c342f4b9a: build 0/0; broad filter 5349/0 + Refile/SecureChild 777/0; ArchTests 1207/0 (a first run had 9 boot-fixture timeouts under load; the rerun was clean) |
| **Status** | in-progress (parallel mode) |
| **Next Action** | (1) On ontology-r1 PASS for the W fixes: mark 071 and 072 ✅ (POML status + TASK-INDEX "✅ [done]"; check-task-status-drift.ps1). On findings: a fix agent from the tip. (2) When the 049 agents report: cherry-pick both ranges with cp.sh, run the BROAD filter on the combined tip, rewrite hashes in the 049 notes, run one Opus adversarial review of 049, then send ontology-r1 the D-104 range plus the column name for their AuthorableColumns follow-up. (3) Queued 077 follow-up: show the F1-b "consequence lowered to Flag" note in the Rule Review read model and page |

### Ranges out for review
- ontology-r1: 072 W review fixes 4795a4209..c342f4b9a (requested 2026-10-19).
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
| **Task ID** | 071/072 (awaiting ontology-r1 re-check); 049 running (two agents) |

## Next Action

See Quick Recovery.
