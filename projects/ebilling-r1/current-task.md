# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-17
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 41/79 complete (070 included). On the branch, awaiting review: 015, 051, 071, 076, and 074 part 1. Agents running from base 62a1ec56b+: 074 (finishing the D-100 writer entry), adversarial review of 071 (1 pass), adversarial review of 076 (pass 1 of 2, auth), 075 (rule drafting Action) |
| **Step** | Cherry-picked 015 (e0df77339..d4e555e38), 071 (6b99e980f..d22c8e26d), 051 (bdcc8ee35..d35563abd), 076 (194aada77..351704736) and 074 part 1 (0c2f8590f, 5455d3977). ADR-010 ceiling 166; route ledger 137 (076). Build, ArchTests and targeted tests are running on 62a1ec56b. external-spa uses vitest, not jest |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent reports: 074 → cherry-pick, then send the range to ontology-r1 (7-point plan + re-check). 071 / 076 review findings → fix agent; then 076 pass 2. 075 → review, cherry-pick. Wait for: uac-r2 (026 fr2 re-check → mark 026 ✅, close #2056; 015 shared files; 051 /me), SPA-r3 (015 / 051 notices), ontology-r1 (015 files). Mark 015 / 051 / 071 / 076 ✅ after their reviews. Next launches: 072 (needs 071 ✅), 077 (needs 076 ✅), then 049 (needs 072, 073) |

### Files Modified This Session
- All committed (last: 049 two-path constraint e5a0e42c9, plus this checkpoint).

### Critical Context
- Decisions this session:
  - D-96: invoice re-file line check runs as the app identity.
  - D-97: 015 approved, including the delete.
  - D-98: counted spend = In Review, Approved, Sent to AP, Paid.
  - D-99: firm step for multi-firm callers.
  - D-100: path B, `WriteBillingDecisionAsync`.
  - D-101: every submission that is read creates its invoice. Rejected → created Rejected; blocked → In Review, held; no submission lookup on DR.
  - D-102: held invoices are excluded from spend on BOTH sum paths (FinanceRollupService and FinanceBaseValues.BuildGroupedSumFetchXml). One predicate, in FinanceBaseValues. Ships with 049.
- ontology-r1 conditions:
  - 074's 7-point plan is in the 074 agent prompt and D-100.
  - **PR #1911 must not merge to master until ontology's owner confirms Q24 (CountedInvoiceStatuses).**
  - 049 goes to ontology-r1 for an adversarial pass.
- 015 live delete sequence: deploy the BFF containing e0df77339 → `Remove-EbillingInvoiceStatus.ps1 -GridConfig` (WhatIf first) → `-Delete -OwnerApproval 'D-97 2026-10-17 owner'`. Blocked by grid config 3ff4102c and live aiskillconfig a32ae5a7. Runs at the 053 / 079 deploy. SpaarkeMaster source is ahead of dev: no full import before the delete.
- Filed #2077: WorkspaceAiService and ProjectLiveFactResolver read columns that don't exist (pre-existing, outside scope).
- Standing:
  - no line (sprk_billingevent) upload or storage route before uac-r2 #2047;
  - 026 must not edit `Roles/Spaarke Ontology Service.xml` (#2048);
  - remove worktrees with `scripts/worktree/Remove-WorktreeSafe.ps1`;
  - the full Sprk.Bff.Api.Tests run takes ~50 min; agents run filtered sets only.
- Owner awareness:
  - 071 judgment calls 1–4 were accepted as recommended.
  - 076 billing admin = Write on sprk_ruleset, which no role carries yet (owner decision before 079).
  - The owner must add the 7 new tables to the Matter Management and Spaarke Administration apps.
- Open: lint-staged shared-stash risk (G-18), not yet filed; DEF-015; ISS-009; #2010, #2011, #2045.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 074, 075 (implementers); 071, 076 (reviews) |

## Next Action

See Quick Recovery.
