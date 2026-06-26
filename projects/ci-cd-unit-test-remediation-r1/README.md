# CI/CD + Unit Test Remediation (r1)

> **Portfolio**: [Project #457](https://github.com/spaarke-dev/spaarke/issues/457) · Parent Epic: [#429 DEVOPS & CODE QUALITY](https://github.com/spaarke-dev/spaarke/issues/429) · Board: [Spaarke Core](https://github.com/orgs/spaarke-dev/projects/2)
>
> **Last Updated**: 2026-06-26
>
> **Status**: In Progress (pipeline-initialized; Phase 1 not yet started)

## Overview

Combine the previously-separate `ci-cd-github-enhancement` and `test-architecture-reset-r1` efforts into one delivery across three streams: (A) tiered CI with a required-status router and `<3 min p95` blocking tier, (B) test reset that deletes ~60% wiring tests and rewrites the three places that mandate coverage-% culture, and (C) hot-path coordination (merge queue, `projects/INDEX.md`, auto-`conflict-check`) sized for 5-6 parallel worktrees.

Ships in ~2 weeks elapsed (no dev halt) with one ~4-hour cutover window.

## Quick Links

| Document | Description |
|----------|-------------|
| [spec.md](./spec.md) | AI-optimized specification (binding) |
| [design.md](./design.md) | Design rationale (3 streams, 3 phases) |
| [plan.md](./plan.md) | WBS + hot-path declaration + parallel groups |
| [current-task.md](./current-task.md) | Active task state (auto-updated) |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task registry with parallel groups |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Phase 0 (pipeline-initialized) |
| **Progress** | 0% |
| **Target Date** | 2026-07-23 (~28 elapsed days from start) |
| **Completed Date** | — |
| **Owner** | spaarke-dev (solo) |

## Problem Statement

CI/CD is slow (15-20 min p95) and untrustworthy; the ~7,900-test unit suite optimizes for coverage % rather than behavior, generating noise on every PR; and parallel projects collide on hot files (BFF `Program.cs`, SpaarkeAi widget/route registries) producing avoidable merge + deploy friction. Forcing function: `Build & Test (Release)` matrix entry temporarily disabled in `.github/workflows/sdap-ci.yml` line 86; team trust in CI at a multi-month low; today's 9-bug daily-briefing cascade caught 0 by any unit test.

## Solution Summary

Three streams shipped through three synchronized phases over ~3 weeks elapsed with no dev halt. Stream A introduces a router/Tier 1/Tier 2 architecture (with Tier 3 augmenting existing `nightly-health.yml`). Stream B rewrites `tests/CLAUDE.md`, `.claude/constraints/testing.md`, and creates standalone testing strategy ADR-038 (NOT a supersession of ADR-022, which is PCF Platform Libraries); then sliced-deletes ~60% of wiring tests; reorganizes survivors into 6 KEEP path conventions. Stream C enables GitHub merge queue (batch=1), creates `projects/INDEX.md` registry, and wires `conflict-check` + `task-execute` for hot-path auto-coordination.

## Graduation Criteria

The project is considered **complete** when:

- [ ] Tier 1 p95 < 3 min (30-day rolling)
- [ ] Tier 2 p95 < 8 min (30-day rolling)
- [ ] Tier 1 flake rate < 1%
- [ ] PR-to-merge time decreases ≥ 30%
- [ ] Master green rate ≥ 95% over 30 days post-cutover
- [ ] All 6 KEEP path conventions populated; DELETE category fully removed; Release+Debug full matrix restored and green
- [ ] `tests/CLAUDE.md` + `.claude/constraints/testing.md` no longer mandate coverage %; ADR-038 exists as standalone testing strategy
- [ ] GitHub merge queue enabled; INDEX.md lists all active worktrees with hot-path declarations
- [ ] `deploy-spaarke-ai.yml` exists with ≥1 successful CD-from-master deploy; `deploy-bff-api.yml` confirmed master-triggered
- [ ] Hot-path collision incidents drop ≥ 50% over 30 days vs prior 30 days

## Scope

### In Scope

**Stream A — CI tiering**: `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, augmentation of `nightly-health.yml` for Tier 3, retirement of `sdap-ci.yml`, `scripts/validate-markdown-links.ps1`.

**Stream B — Test reset**: Phase 1 audit (transient inventory CSV), path reorganization into 6 KEEP categories, sliced deletion in 3+ PRs, rewrite `tests/CLAUDE.md` + `.claude/constraints/testing.md`, new ADR-038 (standalone), new `docs/standards/TEST-ARCHITECTURE.md`.

**Stream C — Hot-path coordination**: GitHub merge queue (batch=1), `projects/INDEX.md`, auto-`conflict-check` on hot-path watchlist, `bff-extensions.md` Hot-Path Declaration section, root CLAUDE.md §8/§10/§17 updates, new `deploy-spaarke-ai.yml` + `deploy-bff-api.yml` audit.

### Out of Scope

- Registry architecture redesign for BFF DI and SpaarkeAi widget/route registries (candidate follow-up)
- Azure deployment workflows other than BFF + SpaarkeAi
- React/PCF Jest test architecture
- Coverage measurement elimination (stays observable, never gating)
- Team-process artifacts (surveys, retrospectives, named-owner ceremony) — N/A for solo-dev

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| ADR-038 is standalone, NOT supersession of ADR-022 | ADR-022 is PCF Platform Libraries; misattribution lives in constraints/testing.md line 25 (fixed in task `022`) | User clarification 2026-06-25 |
| INDEX.md scope = worktrees with commits in last 30 days | Spec's 5-6 active assumption matches recent-activity subset; 18 total worktrees include dormant ones | User clarification 2026-06-25 |
| Skip pipeline Step 4 (branch creation) | Already on `work/ci-cd-unit-test-remediation-r1` worktree branch | User clarification 2026-06-25 |
| Drop design.md "ship escape hatches first" task | Spec FR-A05 explicitly forbids commit-marker skip mechanism; path-aware dispatch is only relief | spec.md FR-A05 wins over design.md §6 |
| All test-modifying tasks = FULL rigor | Per spec FR-B07, overrides default STANDARD-rigor skip on Step 9.5 | spec.md FR-B07 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Real regression slips through deletion window | Med | Med | Keep all integration/auth/security/regression tests; Debug matrix still runs even with Release temporarily off |
| Tier 1 still flaky after migration | High | Med | Tier 1 contains only build + arch tests + auth smoke in shadow phase; if flake > 1%, pause cutover and re-triage |
| Sliced deletion PRs conflict with in-flight feature PRs | Med | Med | INDEX.md is source of truth; coordinate slice sequence with hot-path declarations |
| Coverage targets reintroduced by future project | Med | Med | ADR-038 explicitly bans gating on coverage; CLAUDE.md pointer reinforces |
| Hot-path declarations in INDEX.md go stale | Med | High | `project-pipeline` skill updates INDEX.md at project start; `task-execute` updates on hot-path changes |
| Stream A lands without Stream B ready (or vice versa) | High | Low | This project binds them — they ship as one project. Phase 3 cutover gated on Stream B Phase 2 deletion landing AND Tier 1 measured stable |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| `sdap-ci.yml` PR-comment dedup pattern (lines 591-619) | Internal | Stable | Copied verbatim into `ci-tier2-advisory.yml` |
| `nightly-health.yml` (Tier 3 augmentation target) | Internal | Stable | Add 4 jobs hooking into existing report-dependency pattern |
| `deploy-bff-api.yml` (pattern for `deploy-spaarke-ai.yml`) | Internal | Stable | Pattern proven; Phase 2 audits + mirrors |
| GitHub merge queue + branch-protection feature | External | GA | Spec UQ #1 spike validates required-check semantics with neutral conclusions |
| `projects/github-actions-rationalization-r1/baseline/branch-protection-2026-06-01.json` | Internal | Reference | Read-only reference for Phase 1 cross-cutting task `001` |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-25 | 0.1 | Pipeline-initialized: design.md, spec.md, README, plan, tasks scaffolded | Claude Opus 4.7 |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
