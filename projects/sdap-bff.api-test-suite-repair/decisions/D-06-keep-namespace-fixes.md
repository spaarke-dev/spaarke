# D-06: KEEP the 3 in-progress namespace fixes

**Status**: Locked (2026-05-30 design phase, captured 2026-05-31)
**Source**: [`design.md`](../design.md) §5.6
**Binding on**: Phase 0 task 007 (commits the fixes as the project's first commit)

---

## Context

At project start, 3 in-progress namespace fix edits exist in the working tree from prior unsanctioned work (per spec.md Owner Clarifications). The open question was whether to (a) revert them to establish a "clean starting state" before Phase 0 baseline, or (b) keep them and commit them as the project's first commit. Reverting would discard correct work in service of a procedural symmetry; keeping them captures legitimate drift fixes and saves Phase 1 re-discovery cost.

## Decision

**Option A.** Per §5.6 (verbatim):

> The three in-progress namespace fixes are kept in working tree and become the project's first commit.

## Rationale

Per §5.6 "Why": They are legitimate fixes for real drift. Reverting them would discard correct work to "preserve a clean starting state" — that's process theater, not engineering value. The fixes are exactly what Phase 1 would re-do; doing them now saves the re-discovery cost.

## Rejected alternatives

- **Revert to clean baseline before Phase 0** — discards correct work; Phase 1 would re-do the same fixes; net cost = re-discovery time + risk of getting them subtly wrong the second time.
- **Stash and re-apply in Phase 1** — same as revert from a baseline-purity standpoint but with extra git complexity and the same re-discovery risk if re-application is incomplete.
- **Commit them but pre-Phase-0 (before baseline capture)** — pollutes the baseline measurement (task 002 would record results post-fix, not pre-fix); the baseline must represent the as-found state.

## Downstream Impact

- **Phase 0 task 007** — commits the 3 namespace fixes as the project's first commit (sequenced AFTER baseline capture in task 002 so baseline records the pre-fix state per §5.6 sequencing)
- **Phase 1 P1.* tasks** — namespace-fix territory is partially covered already; Phase 1 inventory in task 011 reconciles which other namespace drift remains
- **Operational N/A clause** — if no in-progress fixes exist at task 007 execution time (e.g., they were committed by another stream), this decision is documented but operationally a no-op; task 007 verifies state and proceeds accordingly

## Reassessment trigger

If task 007 finds that the 3 in-progress fixes have been corrupted (partial application, conflicts) or expanded beyond their original scope (touch production code, contravening NFR-01), escalate per §4.8 BEFORE committing — the keep-as-first-commit decision assumes the fixes are still clean drift fixes.
