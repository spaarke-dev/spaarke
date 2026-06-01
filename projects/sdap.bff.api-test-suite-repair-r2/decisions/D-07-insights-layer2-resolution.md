# D-07 — Insights Layer 2 HOLD Resolution Path (RB-T028-02)

> **Status**: **pending Phase 1 task 012 execution**
> **Created**: 2026-06-01 (r2 Phase 0 task 002)
> **Resolved at**: Phase 1 task 012 (Resolve RB-T028-02 Insights Layer 2 HOLD)
> **Binding requirement**: FR-05

---

## Decision (placeholder — finalized at task 012)

`RB-T028-02` (Insights Layer 2 outcome-extraction fixture drift, 3 tests in `Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests`) takes one of the following paths at Phase 1 task 012:

| Path | Outcome | When chosen |
|---|---|---|
| **(a)** sibling-takes-bug | 3 tests transferred to `ai-spaarke-insights-engine-r1` backlog; T028-02 closed in r2 with cross-reference | If `dev@spaarke.com` (owner) determines the bug is properly addressed in the sibling project |
| **(b)** r2-takes-bug | Production fix in r2 Phase 1 task 012 + Phase 2 task 026 (fallback); 3 tests flip Skip → Pass | If `dev@spaarke.com` determines r2 is the appropriate locus |
| ~~(c)~~ archived-pending-sibling-engagement | ~~1-week timeout reached; ledger entry archived~~ | **N/A — see below** |

---

## Path (c) is OFF the table

Per the 2026-06-01 owner clarification: `dev@spaarke.com` is the consolidated contact for all sibling coordination (Action Engine, Insights, Communications). Because the Insights sibling-project owner IS the r2 owner, there is no separate outreach thread with a response window to time out. The `archived-pending-sibling-engagement` path-c from the original FR-05 framing is therefore inapplicable.

See [`owner-responses/consolidated-sibling-contact-2026-06-01.md`](owner-responses/consolidated-sibling-contact-2026-06-01.md) for the full rationale.

---

## Decision criteria (applied at Phase 1 task 012)

At Phase 1 task 012, `dev@spaarke.com` reviews:
1. The RB-T028-02 entry detail in [r1 real-bug-ledger.md](../../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md)
2. The 3 affected test cases in `Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests`
3. The production code path under question (Layer 2 outcome-extraction in `ai-spaarke-insights-engine-r1` vs. in `Sprk.Bff.Api`)
4. Active scope of `ai-spaarke-insights-engine-r1` at that point (is the sibling project staffed for the fix?)

Owner selects (a) or (b). Decision is recorded in the FINAL VERSION of this file (overwrites this placeholder section) + in the task 012 POML's `<notes>` section + a commit message citing RB-T028-02 + FR-05.

---

## Downstream task scoping (already correct in TASK-INDEX)

The r2 task index already covers both paths:

- **Task 012** (Phase 1, P1-W1): "Resolve RB-T028-02 Insights Layer 2 HOLD (FR-05)" — produces the (a)/(b) decision
- **Task 026** (Phase 2, P2-W1, CONDITIONAL): "Fix RB-T028-02 fallback (conditional — only if 012 outcome = 'we-take-bug')" — executes the production fix if path (b) chosen; otherwise skipped

No re-scoping of TASK-INDEX is required by the path-c removal because path-c never had a task allocated (it would have been a ledger-state-transition-only outcome).

---

## Intentionally minimal at Phase 0

This decision record is intentionally minimal at Phase 0 task 002. The substance of the decision (which of paths (a) or (b)) is owner judgment at Phase 1 task 012 and cannot be determined at Phase 0 because:
- The production code path may have changed by Phase 1 (sibling project R1 ships independently)
- The sibling project's active staffing may shift
- The Layer 2 outcome-extraction subsystem may have been refactored in the meantime

This file is finalized (overwriting the "Decision (placeholder)" section with the actual chosen path + rationale) when Phase 1 task 012 executes.

---

## Cross-references

| Reference | Purpose |
|---|---|
| [FR-05 in spec.md](../spec.md) | Binding source for the 3-path resolution requirement |
| [Consolidated sibling contact record](owner-responses/consolidated-sibling-contact-2026-06-01.md) | Why path (c) is N/A |
| [r1 real-bug-ledger.md RB-T028-02 entry](../../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md) | The bug being resolved |
| [Task 012 POML](../tasks/012-resolve-rb-t028-02-insights-hold.poml) | Will finalize this record |
| [Task 026 POML](../tasks/026-fix-rb-t028-02-fallback.poml) | Executes path (b) if chosen |

---

*Placeholder created 2026-06-01 by r2 task 002. Will be finalized at Phase 1 task 012 by the owner reviewing the RB-T028-02 evidence.*
