# D-13 — Task 081 conditional extension of `.claude/constraints/bff-extensions.md` § F: WARRANTED

**Date**: 2026-06-01
**Author**: r2 main session (per Track E recommendation in `baseline/phase4-track-e-anti-drift-report-2026-06-01.md` §3.1)
**Status**: **Applied**
**Cleared task**: Task 081 (Phase 5, CONDITIONAL extension of `.claude/constraints/bff-extensions.md` § F)

---

## Decision

The conditional extension of `.claude/constraints/bff-extensions.md` § F **IS warranted**, per Track E (task 044) anti-drift effectiveness report assessment.

Three new sub-sections added to § F (binding rules):

- **§ F.1 Asymmetric-Registration Tier 1.5 Anti-Pattern**: codifies the static-scan recipe from ADR-030 §10 with binding language for new service registrations in `*Module.cs` files
- **§ F.2 Fixture-Config-FIRST Inspection Protocol**: documents the workflow rule (FIRST inspect fixture config / claims / mocks BEFORE assuming production code bug) derived from tasks 025 + 037
- **§ F.3 Empirical-Reproduction-FIRST Protocol**: documents the workflow rule (hand-trace + reproduce empirically + file path-b decision record if hypothesis wrong) derived from tasks 010 + 011 + 012

Each sub-section cross-references the corresponding §18.x in `docs/procedures/testing-and-code-quality.md` (task 080) and the supporting evidence in r2's baseline/ artifacts.

## Rationale

Track E found 4 anti-drift mechanisms in the existing governance surface:
- § F Test-Update Obligation: **Effective** (100% compliance across 19 r2 commits; clean trait taxonomy)
- PR template checkbox: Partial (not yet exercised; assessable post-PR-merge)
- Procedure-doc reviewer checklist: Partial (failure path untested)
- CLAUDE.md §10 bullet 6: **Partial** — catches Tier 1 BLOCKING (4 of 13 inventory) but missed Tier 1.5 LATENT (5 of 13). r2 task 011 had to discover the 5 LATENT residuals iteratively across 4 separate Tier 1.5 commits.

The Tier 1.5 gap is the most material finding. Without explicit § F.1 binding language, the same anti-pattern is likely to recur in future BFF additions — and the recurrence will only surface as host-startup metadata-gen failures that take down the entire BFF (per the r2 task 011 evidence).

Extending § F is the right binding surface for this because:
- § F is already the binding rule for test-update obligation (the closest existing rule)
- CLAUDE.md §10 bullet 6 is too high-level (it states the "what" but not the "how")
- Procedure-doc §18.x captures the "how" with full bash recipes but is reviewer-judgment-enforced

The 3 sub-sections in § F are **binding rules** for PR reviewers; the procedure-doc sections are **reference material** for authors and reviewers.

## Implementation

Applied inline by r2 main session 2026-06-01 (per sub-agent write boundary in CLAUDE.md §3 — `.claude/` paths are main-session-only). See commit forthcoming in PR #318 chain.

## Cross-references

- ADR-030 concise — `.claude/adr/ADR-030-bff-nullobject-kill-switch.md`
- Procedure doc — `docs/procedures/testing-and-code-quality.md` §§18.1, 18.2, 18.3
- Track E report — `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-e-anti-drift-report-2026-06-01.md`
- Track C TestClock PoC report — `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-c-testclock-poc-2026-06-01.md`
- Asymmetric-registration inventory — `projects/sdap.bff.api-test-suite-repair-r2/baseline/asymmetric-registration-inventory-2026-06-01.md`
- r2 task 011 (cluster fix evidence) — POML at `projects/sdap.bff.api-test-suite-repair-r2/tasks/011-fix-rb-t028-cluster.poml`; D-09 NullObject design; D-10 security review
- r2 tasks 025 + 037 (fixture-config evidence) — RB-T028-07/08 ledger entries
- r2 tasks 010 + 011 + 012 (empirical-reproduction evidence) — D-08 (task 010 security) + E-01 (task 011 escalation) + D-07 (task 012 path-b decision)
