# D-08 — Security review approval for RB-T044-01 (task 010)

**Date**: 2026-06-01
**Reviewer**: `dev@spaarke.com` (per NFR-03 binding)
**Status**: **Approved**
**Cleared NFR**: NFR-03 for task 010 / RB-T044-01

---

## Decision

The task 010 production fix (two-mode semantic in `ConversationHistorySanitizer.StripRetrievedContent`) is approved for the eventual master merge (Phase 5 task 083 / FR-16).

## Reviewer findings (verbatim)

1. **The two-mode semantic is correct.** Matter-pivot mode vs legacy mode, keyed on whether `fromTurnIndex` points to a System-role matter marker, is the right abstraction for this code's two call patterns (production `MatterContextDetector.DetectChange` vs synthetic direct-test invocation). The deviation from the r1 ledger's one-line inversion recommendation is justified — the simple flip would have broken `Sanitizer_StripsRetrievalBlocks_PreservesConclusions`.

2. **No additional cross-matter scenarios are known to be uncovered.** The 5 `PrivilegeLeakageTests` cover the documented 2-matter pivot patterns; the new `MatterPivot_ThreeMatters_StripsOnlyImmediatelyPreviousMatterContent` regression covers the 3-matter case. If a 4+-matter or mid-message-pivot scenario emerges in production, that's a new ledger entry — not a gap in this fix.

## Implications

- **Merge gate**: NFR-03 cleared for task 010. The eventual master merge (task 083, FR-16) is unblocked for this specific work item.
- **Task 011** (Phase 1 P1-S2 RB-T028-03/04/05/06 cluster fix) is unblocked and may dispatch.
- **Audit trail**: PR #318 comment `4594687188` (security-review request) + `4594694xxx` (approval reply) capture the full review history.
- **Pattern set**: this is the first HIGH-severity security review under r2. The same approval format should be used for task 011 (cluster fix includes RB-T028-06 Auth, which D-03 implies also requires security review).

## Reference

- Production fix commit: `8b7a905d` — `feat(sdap-bff-test-r2): task 010 complete — RB-T044-01 cross-matter privilege leak fixed`
- Ledger transition commit: `41884235` — RB-T044-01: `real-bug-pending-fix` → `repaired`
- Task POML: `projects/sdap.bff.api-test-suite-repair-r2/tasks/010-fix-rb-t044-01.poml`
- Triple-run report: `projects/sdap.bff.api-test-suite-repair-r2/baseline/per-fix-triple-run-rb-t044-01-2026-06-01.md`
- PR #318: https://github.com/spaarke-dev/spaarke/pull/318
