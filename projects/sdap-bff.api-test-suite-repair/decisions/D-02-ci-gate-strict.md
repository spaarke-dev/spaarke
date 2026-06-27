# D-02: CI gate — full enforce_admins + documented incident-response emergency path

**Status**: Locked (2026-05-30 design phase, captured 2026-05-31)
**Source**: [`design.md`](../design.md) §5.2
**Binding on**: FR-09, FR-10, FR-11, FR-12; Phase 1 task 020 (enforce_admins flip), Phase 4 verification task 086

---

## Context

The CI gate (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality` status checks on master branch protection) is currently bypassable via admin merges and via a `skip-tests` workflow_dispatch input on [`deploy-bff-api.yml`](../../../.github/workflows/deploy-bff-api.yml). This casual-bypass pattern is the structural mechanism that allowed the test-suite to drift unchecked across multiple projects through 2026, producing the 5,215-test / 4,844-failing baseline this project exists to repair. Re-locking the gate is FR-09's mandate; the open question was whether to keep "easy bypass for emergencies" or remove it entirely.

## Decision

**Option C with strict criteria.** Per §5.2 (verbatim):

> `enforce_admins: true` for `Build & Test (Debug)`, `Build & Test (Release)`, and `Code Quality` status checks. The `skip-tests` workflow_dispatch input is REMOVED from [`deploy-bff-api.yml`](../../.github/workflows/deploy-bff-api.yml). Emergency deploy is a documented procedure requiring (a) a filed incident, (b) named approver from a documented allowlist, and (c) auto-creation of a follow-up issue to fix the underlying cause within 5 business days.

## Rationale

Per §5.2 "Why robust over easy": the current admin-bypass merging pattern is the entire reason this project exists. Anything less than full `enforce_admins` recreates the rot mechanism. The documented emergency path acknowledges that real emergencies happen without giving everyday merges a casual escape hatch.

Per §5.2 "Why this is not over-engineering": incident-response procedure is a one-page document; `enforce_admins` is one API call. Total operational cost is minutes, not hours per month. Cost of NOT doing this is the next test-suite-repair project in 2027.

## Rejected alternatives

- **Keep `skip-tests` workflow_dispatch input** — recreates casual-bypass pattern.
- **Partial `enforce_admins` (some checks only)** — leaves the rot mechanism partially open; the next project drifts via whichever checks remain bypassable.
- **No documented emergency procedure** — real production emergencies still happen; agents/owners need a sanctioned path other than disabling the gate.

## Downstream Impact

- **FR-09** — `enforce_admins: true` flip on the 3 named status checks (Phase 1)
- **FR-10** — `skip-tests` workflow_dispatch input removed from `deploy-bff-api.yml` (Phase 1)
- **FR-11** — Emergency procedure documented (owner-only approver allowlist + 5-business-day follow-up clause) (Phase 4)
- **FR-12** — Operational verification: gate behaves as specified post-flip (Phase 4 task 086)

## Reassessment trigger

If a real incident requires the emergency procedure 3+ times in a single quarter, the procedure burden indicates a different failure mode (e.g., flaky tests, infra fragility) and the gate criteria should be re-examined — NOT relaxed, but the upstream cause investigated.
