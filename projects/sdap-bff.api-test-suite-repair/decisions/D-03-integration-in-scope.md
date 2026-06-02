# D-03: Integration test scope — IN SCOPE

**Status**: Locked (2026-05-30 design phase, captured 2026-05-31)
**Source**: [`design.md`](../design.md) §5.3
**Binding on**: FR-01 (integration baseline), FR-13 (P1.E classification), FR-18 (P23.I repair); Phase 0 task 002 (baseline), Phase 2+3 P23.I tasks

---

## Context

The integration test project `tests/integration/Spe.Integration.Tests/` exercises real Graph / SharePoint Embedded behavior (vs. mocked Graph in `Sprk.Bff.Api.Tests`). The initial draft scope was unit tests only; the open question was whether to include integration tests, which would add 12-20h to project total but cover the failure-mode layer that unit tests structurally cannot.

## Decision

**Option A.** Per §5.3 (verbatim):

> `tests/integration/Spe.Integration.Tests/` is in scope. Phase 0 runs the baseline; repair work is sequenced into Phase 2/3 alongside `Sprk.Bff.Api.Tests` work.

## Rationale

Per §5.3 "Why robust over easy": integration tests are the layer that catches real Graph/SPE behavior unit tests can't (mocked Graph ≠ real Graph). The 2026-05-29 BFF audit identified Graph drift as a recurring incident source. Repairing only the unit tests leaves the higher-value verification layer broken.

Per §5.3 "Effort impact": adds ~12-20h to project total. Project total revised in §10 accordingly.

## Rejected alternatives

- **Unit tests only (Option B)** — saves 12-20h but leaves the higher-value Graph-drift detection layer broken; recreates the audit-identified recurring-incident pattern.
- **Defer integration tests to a follow-up project** — defers the higher-value work indefinitely; predecessor pattern shows deferred test-repair projects don't materialize until the next forced trigger.

## Downstream Impact

- **FR-01** — Phase 0 task 002 captures integration baseline (TRX + failure counts) alongside unit baseline
- **FR-13** — Phase 1 P1.E tasks classify integration failures into the §6.2 triage taxonomy
- **FR-18** — Phase 2+3 P23.I tier repairs integration tests per the same binding rules as unit-tier
- **Dependabot coordination** — PRs touching test infrastructure (FluentAssertions #287, coverlet.collector #265, Microsoft.AspNetCore.Mvc.Testing #236) require careful merge timing around active P23.I work (project CLAUDE.md Implementation Notes)

## Reassessment trigger

If Phase 0 baseline shows integration test failures are dominated by environmental causes (Graph throttling, SPE container provisioning timeouts) rather than code-drift, re-examine whether repair-vs-quarantine balance shifts toward quarantine for the environmental subset.
