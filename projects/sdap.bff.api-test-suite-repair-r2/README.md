# sdap.bff.api-test-suite-repair-r2

> **Last Updated**: 2026-06-01
> **Status**: In Progress — Phase 0 (Project Setup)
> **Target Date**: 2026-08-31
> **Predecessor**: [`sdap-bff.api-test-suite-repair`](../sdap-bff.api-test-suite-repair/) (r1, closed 2026-06-01)

## Overview

r2 is the closure project that converts the 20 real-bug ledger entries surfaced by the r1 test-suite-repair predecessor into production-code fixes. It also validates `Spe.Integration.Tests` stability, measures anti-drift effectiveness, completes sibling-project sign-offs, and pilots four forward-looking quality improvements (PCF audit, mutation testing, TestClock/Guid PoC, Coverlet baseline). All work ships by **2026-08-31** to beat the September 2026 fix-by cliff.

## Quick Links

| Document | Description |
|---|---|
| [plan.md](./plan.md) | Comprehensive implementation plan with phase breakdown + discovered resources |
| [design.md](./design.md) | Full design document (402 lines) |
| [spec.md](./spec.md) | AI-optimized specification (225 lines; 16 FRs / 11 NFRs / 14 SC) |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task tracker (created by `/task-create`) |
| [current-task.md](./current-task.md) | Active task state (for context recovery) |
| [r1 real-bug-ledger](../sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md) | The 20 entries r2 closes |
| [r1 lessons-learned](../sdap-bff.api-test-suite-repair/notes/lessons-learned.md) | Calibration input (load at Phase 0) |

## Current Status

| Metric | Value |
|---|---|
| **Phase** | Phase 0 — Project Setup |
| **Progress** | 0% (initialization complete; tasks not yet started) |
| **Target Date** | 2026-08-31 |
| **Owner** | ralph.schroeder@hotmail.com |

## Problem Statement

r1 closed at `Failed: 0` across both BFF test suites but explicitly deferred production-code changes (r1's NFR-01). r1's diagnostic playbook surfaced **20 real production bugs** (5 HIGH, 7 MED, 8 LOW) currently held in `[Skip] + [Trait("status","real-bug-pending-fix")]` state. The latest fix-by date is 2026-09-30; without action, the cliff hits and the bug backlog calcifies. r1 also left integration-suite stability unvalidated, anti-drift mechanisms unmeasured, and PCF/Code Pages test rot unaudited.

## Solution Summary

A single 3-month closure project (2026-06-01 → 2026-08-31) executing 6 phases:

1. **Phase 0** (1 week): Reproduce 20 entries; sibling outreach; project artifacts
2. **Phase 1** (3 weeks): Fix 5 HIGH-severity entries with security review (cross-matter privilege leak + RB-T028 cluster shared-root-cause fix per ADR-010/ADR-018)
3. **Phase 2** (3 weeks): Fix 7 MED-severity entries
4. **Phase 3** (2 weeks): Fix 8 LOW-severity entries + `Spe.Integration.Tests` triple-run validation
5. **Phase 4** (3 weeks, 5 parallel tracks): PCF audit, mutation testing pilot, TestClock/Guid PoC, Coverlet baseline, anti-drift effectiveness report
6. **Phase 5** (1 week): Governance updates, final triple-run, PR + admin-merge, lessons-learned

## Graduation Criteria

The project is **complete** when all 14 criteria pass:

- [ ] All 20 real-bug ledger entries closed (FR-01 — `repaired` / `transferred-to-sibling` / `archived-as-dead-target`)
- [ ] 5 HIGH closed in Phase 1 incl. RB-T044-01 with security-review record (FR-02 / NFR-03)
- [ ] 7 MED closed in Phase 2 (FR-03)
- [ ] 8 LOW closed in Phase 3 (FR-04)
- [ ] RB-T028-02 Insights HOLD resolved with `ai-spaarke-insights-engine-r1` (FR-05)
- [ ] r1 `priority-order.md` TBD sibling sign-off slots populated (FR-06)
- [ ] `Spe.Integration.Tests` triple-run validates ≤2 flakes (FR-10)
- [ ] Anti-drift effectiveness report published — favorable or not (FR-09 / NFR-07)
- [ ] PCF/Code Pages audit document published with r3 recommendation (FR-11)
- [ ] Mutation testing pilot report published (FR-12)
- [ ] TestClock/Guid PoC working in `Services/Workspace/*` (FR-13)
- [ ] Coverlet baseline % published per project (FR-14)
- [ ] Final triple-run on both suites: `Failed: 0` (FR-15)
- [ ] PR merged to master on or before 2026-08-31 (FR-16)

## Scope

### In Scope

- 20 real-bug production fixes per r1's `real-bug-ledger.md`
- Insights Layer 2 HOLD resolution (RB-T028-02; sibling coordination)
- `Spe.Integration.Tests` runtime stability via triple-run + flake-quarantine
- Sibling-project sign-off completion (Action Engine, Insights, Communications)
- Anti-drift effectiveness measurement (BFF-touching PRs 2026-06-01 → 2026-08-15)
- PCF/Code Pages test rot audit (read-only)
- Mutation testing pilot — Stryker.NET against `Services/Ai/Safety/*`
- TestClock + seeded-Guid PoC in `Services/Workspace/*`
- Coverlet baseline measurement (no threshold enforcement)
- Ledger lifecycle documentation in `docs/procedures/testing-and-code-quality.md`
- Conditional `.claude/constraints/bff-extensions.md` § F extension

### Out of Scope

- Full PCF/Code Pages test suite repair (r2 audits + recommends only; full = r3)
- Full mutation testing remediation (r2 pilots one area; r3 expands)
- Full deterministic test data migration (r2 proves in `Services/Workspace/*`; r3 generalizes)
- Coverage gate as required-status-check (waits for `github-actions-rationalization-r1`)
- New test types (property-based, fuzzing)
- Feature work / refactors unrelated to ledger entries

## Key Decisions

| Decision | Rationale | Source |
|---|---|---|
| **D-01** | r1 NFR-01 (no `src/`) RELAXED — production IS in scope | design.md §6.D-01 |
| **D-02** | One production fix = one ledger entry closed; cluster exception for shared root cause | design.md §6.D-02 |
| **D-03** | HIGH gets security review; MED + LOW get FULL-rigor code-review + adr-check | design.md §6.D-03 |
| **D-04** | Phase 4 tracks are pilot-grade; full execution scoped to r3 | design.md §6.D-04 |
| **D-05** | Real-bug ledger is the source of truth for state transitions | design.md §6.D-05 |
| **D-06** | r3 is NOT pre-committed at r2 start | design.md §6.D-06 |

## Risks & Mitigations (top 3)

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| RB-T044-01 fix introduces new bug (privilege-leak cascade) | HIGH | LOW | Mandatory security review (NFR-03); regression-test additions; staged dev-env scenario test before merge |
| RB-T028-03/04/05/06 root-cause fix breaks other endpoint registrations | HIGH | MED | Per-fix triple-run; staged rollout one feature flag at a time; automated revert PR if post-merge regression |
| 2026-08-31 deadline slips | HIGH | MED | Each phase has hard end date; 1-week slip → next-phase descope by equal amount; Phase 4 tracks independent and droppable |

## Dependencies

| Dependency | Type | Status |
|---|---|---|
| r1 predecessor merged to master | Internal | ✅ DONE 2026-06-01 (PR #314) |
| r1 `real-bug-ledger.md` accessible | Internal | ✅ Available |
| `enforce_admins: true` restored | Internal | ✅ DONE 2026-06-01 |
| `github-actions-rationalization-r1` Phase 1 | External | 🟡 In flight (affects Phase 4 Track D Coverlet) |
| `ai-spaarke-insights-engine-r1` owner | External | 🟡 Contact pending (affects FR-05 RB-T028-02) |
| Security reviewer named | External | 🟡 Pending (blocks Phase 1 merge gates per NFR-03) |
| Stryker.NET v3.x compatibility | External | 🟡 Validated in Phase 4 Track B |

## Team

| Role | Name | Responsibilities |
|---|---|---|
| Owner | ralph.schroeder@hotmail.com | Overall accountability; owner clarifications; security-reviewer assignment |
| Implementer | Claude Code (Opus, via `task-execute` skill) | Task execution at FULL rigor for production fixes |
| Security Reviewer | TBD (per NFR-03) | HIGH-severity PR review |
| Sibling Owners | Action Engine / Insights / Communications | FR-06 sign-offs |

## Changelog

| Date | Version | Change |
|---|---|---|
| 2026-06-01 | 0.1 | Design drafted ([design.md](./design.md)) |
| 2026-06-01 | 0.2 | AI spec authored ([spec.md](./spec.md)) |
| 2026-06-01 | 0.3 | Design PR #316 merged; project initialized via `/project-pipeline` |

---

*Template version 1.0 | Spaarke development lifecycle*
