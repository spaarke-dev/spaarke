# RED-3 — BFF Unit-Test-Suite Reduction (10,415 → ADR-038 target ≤3,500)

> **Type**: remediation-project seed · **Origin**: r3 post-program review (2026-08-15)
> **Surface**: `tests/unit/Sprk.Bff.Api.Tests` · **Effort**: L · **Value**: Med-High (ADR-038 alignment, CI speed, honesty)
> **Owner note**: nominally `ci-cd-unit-test-remediation-r1` (CICD-083..085) — **not achieved** (~7,000-test gap).

## Summary

The BFF unit test project holds **~10,415 tests** against an ADR-038 target of **≤3,500** with an
integration-heavy (~70/30) shape. The gap is ~7,000 tests, dominated by mock-scaffolding (build-class
tests that drove design or lifted coverage, not regression-protectors). ADR-038 §7's 17-ban classifier
(B1–B17) exists precisely to retire these; the `/test-diet` at each project close is conservative
(project-delta only), so the historical backlog persists.

## Evidence

- **332 test files in `tests/unit` use `Mock<`**; ~1,922 `Mock<` usages total in unit vs ~603 in
  integration (inverted from the ADR-038 ideal).
- Verified-clean (do NOT chase): `Mock<HttpMessageHandler>` (B1 ban) = **0** real usages — all hits are
  compliance comments. CS1998 async-without-await = 0. So the debt is B7/B9/B15-class (all-mocks-trivial,
  pass-through, high setup-to-assertion), not the B1 transport-mock class.

## Why it matters

1. **CI wall-clock**: 10,400 unit tests run on every PR (Debug + Release) — a large, slow tax with low
   marginal regression protection.
2. **Honesty / maintenance**: build-class tests break on refactors without protecting behavior — they
   punish good refactoring (the God-class decompositions in RED-1/RED-2 will thrash hundreds of them).
3. ADR-038 §7 is binding; the suite shape contradicts the standard the repo publishes.

## Proposed approach (staged, conservative, reversible)

1. **Classify, don't bulk-delete.** Run `/test-diet` in **whole-suite mode** (not project-delta) against
   the B7/B9/B15 heuristics; emit `git rm` candidates + AMBIGUOUS bucket. Reviewer confirms.
2. **Delete in waves by ban-class**, each wave its own PR with a full `dotnet test` after:
   - Wave A: B9 pass-through/`Verify.Once` single-delegation tests.
   - Wave B: B7 all-mocks-trivial (≥3 mocks, ≤2 assertions).
   - Wave C: B15 setup-to-assertion >10:1.
   - Wave D: B6/B16 mirror + getter/setter.
3. **Backfill where a real gap is exposed**: if deleting a mock-test removes the only coverage of a real
   branch, author ONE integration/contract test at the proper KEEP path (net reduction, higher fidelity).
4. Track the count down toward ≤3,500; coverage is observation, never a gate (ADR-038).

## Risks & mitigations

- **Risk**: deleting a "scaffolding" test that was actually load-bearing (shared helper, or the only
  branch coverage). **Mitigation**: `/test-diet` PATH-VIOLATION-PROTECTED guard + per-wave full-suite run;
  reviewer confirms every `git rm`; AMBIGUOUS bucket is human-judged, never auto-deleted.
- **Risk**: cross-worktree churn (many worktrees touch these tests). **Mitigation**: coordinate with
  `ci-cd-unit-test-remediation-r1` (owns this) — this seed HANDS the work to that track with the current
  evidence, rather than a parallel effort.

## Acceptance criteria

- BFF unit test count trending to ≤3,500; suite green after each wave; every deletion cites its B-ban;
  no net loss of real-branch coverage (backfilled at KEEP paths where needed).

## Dependencies / coordination

Owned by / coordinated with `ci-cd-unit-test-remediation-r1`. Sequence the God-class decompositions
(RED-1/RED-2) to land AFTER or alongside so their test churn is absorbed once.
