# sdap.bff.api-test-suite-repair-r2

> **Last Updated**: 2026-06-01
> **Status**: ✅ **COMPLETE** — merged to master at commit `7b804d35` (PR #318 admin-merge, 2026-06-01)
> **Target Date**: 2026-08-31 (closed ~3 months early)
> **Completed Date**: 2026-06-01
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
| **Phase** | ✅ Complete (all 6 phases closed) |
| **Progress** | 100% (35 of 35 active tasks complete; 1 partial closure with residual filed; 1 deferred) |
| **Target Date** | 2026-08-31 (closed early 2026-06-01) |
| **Merge Commit** | `7b804d35` (admin-merge of PR #318 to master) |
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

- [x] All 20 real-bug ledger entries closed (FR-01) — 19 `repaired` + 1 `partial-repair-residual-filed` (RB-T053-01 + RB-T053-01a LOW filed) + 1 inline closure (RB-T013-01 probabilistic flake)
- [x] 5 HIGH closed in Phase 1 incl. RB-T044-01 with security-review record (FR-02 / NFR-03) — D-08 (`dev@spaarke.com`) for task 010; D-10 (`dev@spaarke.com`) for task 011 cluster
- [x] 7 MED closed in Phase 2 (FR-03) — 6 `repaired` + 1 partial (RB-T053-01)
- [x] 8 LOW closed in Phase 3 (FR-04) — all 8 `repaired`
- [x] RB-T028-02 Insights HOLD resolved with `ai-spaarke-insights-engine-r1` (FR-05) — task 012 path-b, `GroundingVerifier.cs` production fix per D-07
- [x] r1 `priority-order.md` TBD sibling sign-off slots populated (FR-06)
- [x] `Spe.Integration.Tests` triple-run validates ≤2 flakes (FR-10) — task 038: 3 × 370 Passed / 0 Failed / 52 Skipped, **0 flakes**
- [x] Anti-drift effectiveness report published — favorable or not (FR-09 / NFR-07) — task 044 / Track E baseline doc
- [x] PCF/Code Pages audit document published with r3 recommendation (FR-11) — task 040 / Track A baseline doc; 2 r3 candidates filed (RB-CLIENT-001/002)
- [x] Mutation testing pilot report published (FR-12) — task 041 / Track B baseline doc; 89.13% mutation score on `ConversationHistorySanitizer`
- [x] TestClock/Guid PoC working in `Services/Workspace/*` (FR-13) — task 042 / Track C: `PortfolioService` PoC + 5 tests pass + ADR-010 compliant
- [x] Coverlet baseline % published per project (FR-14) — task 043 / Track D: 38.49% line / 29.98% branch debug; per-asm + per-ns breakdown
- [x] Final triple-run on both suites: `Failed: 0` (FR-15) — task 082: **18,906 cumulative executions / 0 Failed / 0 flakes** across 6 TRX (3 unit + 3 integration)
- [x] PR merged to master on or before 2026-08-31 (FR-16) — **merged 2026-06-01 (3 months early)** at commit `7b804d35`

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
| **D-06** | r3 is NOT planned — r2 is comprehensive closure (resolved 2026-06-01) | design.md §6.D-06 |

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
| `github-actions-rationalization-r1` Phase 1 | External | ✅ Complete or imminent (lands before 2026-08-04; resolved 2026-06-01) |
| `ai-spaarke-insights-engine-r1` owner | External | ✅ Contact = `dev@spaarke.com` (resolved 2026-06-01) |
| Security reviewer named | External | ✅ `dev@spaarke.com` (resolved 2026-06-01; NFR-03 unblocked) |
| Stryker.NET v3.x compatibility | External | 🟡 Validated in Phase 4 Track B |

## Team

| Role | Name | Responsibilities |
|---|---|---|
| Owner | ralph.schroeder@hotmail.com | Overall accountability; owner clarifications |
| Implementer | Claude Code (Opus, via `task-execute` skill) | Task execution at FULL rigor for production fixes |
| Security Reviewer | `dev@spaarke.com` (NFR-03) | HIGH-severity PR review (task 010 + cluster task 011) |
| Sibling Coordination Contact | `dev@spaarke.com` | FR-05 (Insights HOLD) + FR-06 (Action Engine, Insights, Communications sign-offs) |

## Changelog

| Date | Version | Change |
|---|---|---|
| 2026-06-01 | 0.1 | Design drafted ([design.md](./design.md)) |
| 2026-06-01 | 0.2 | AI spec authored ([spec.md](./spec.md)) |
| 2026-06-01 | 0.3 | Design PR #316 merged; project initialized via `/project-pipeline` |

---

*Template version 1.0 | Spaarke development lifecycle*
