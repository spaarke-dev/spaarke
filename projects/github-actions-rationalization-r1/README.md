# GitHub Actions Rationalization R1

> **Last Updated**: 2026-06-01
>
> **Status**: Complete

## Overview

Rationalize the 13 GitHub Actions workflows in `.github/workflows/`. Audit each one, fix broken workflows (P2 `deploy-promote.yml` cascade, P3 `deploy-infrastructure.yml` ghost triggers, P4 `nightly-quality.yml` schedule failures), delete or consolidate untested workflows down to ≤8, add `actionlint` pre-merge validation as a required status check, and set up failure observability so CI failures cannot silently accumulate again.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan, WBS, phases |
| [Design Spec](./design.md) | Technical design (motivation, locked decisions, phased delivery) |
| [AI Specification](./spec.md) | AI-optimized spec (FRs, NFRs, success criteria) |
| [Tasks](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [Current Task State](./current-task.md) | Active task tracker (context recovery) |
| [AI Context](./CLAUDE.md) | Project-scoped Claude Code context |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% |
| **Target Date** | 2026-06-10 (estimated, 6–9 calendar days) |
| **Completed Date** | 2026-06-01 |
| **Owner** | ralph.schroeder@hotmail.com |

## Problem Statement

The repository has 13 GitHub Actions workflows. At least 1 was silently broken for weeks (`sdap-ci.yml` duplicate YAML key — fixed by PR #314). At least 2 more are failing repeatedly (`deploy-promote.yml` cascade, `nightly-quality.yml` on schedule). 7+ have never been verified to work end-to-end. When CI/CD fails consistently, it gets ignored. When it gets ignored, real regressions hide in the noise. The branch-protection `enforce_admins: true` was masking the brokenness — the gate required checks that never posted, so all PRs were blocked, and the owner bypassed protection 3+ times in recent history to land work. That trains everyone that "the gate is theater" — exactly the failure mode the gate was set up to prevent.

## Solution Summary

Five-phase delivery: Phase 0 audits all 13 workflows (success rates, triggers, value statements). Phase 1 fixes P2/P3/P4 broken workflows. Phase 2 deletes or consolidates the 7 untested workflows per delete-by-default policy, dropping count to ≤8. Phase 3 adds `actionlint` as a required pre-merge status check, smoke-tested via deliberate-fail PR. Phase 4 adds a weekly health-report workflow, authors `.github/WORKFLOWS.md` and `docs/procedures/workflow-incident-response.md`, and documents notification routing to `dev@spaarke.com`. Phase 5 verifies the gate end-to-end with a deliberate-fail PR.

## Graduation Criteria

The project is considered **complete** when:

- [x] Every workflow in `.github/workflows/` either passes a clean run on its declared triggers OR is deleted with rationale (FR-01, FR-05, FR-06)
- [x] Current master CI failure (run 26755019759) is root-caused and either fixed or documented (FR-02) — documented per `decisions/D-01-master-ci-failure-disposition.md`, DEFERRED to follow-on `sdap-bff-warnaserror-cleanup-r1` project per NFR-01
- [x] `actionlint` is a required status check on master AND blocks a deliberate-fail PR (FR-07, FR-08, FR-13) — verified via PR #320; see `baseline/branch-protection-verification-2026-06-01.md`
- [x] Workflow count is rationalized to ≤8 (down from 13) (FR-06) — 6 retained + 2 new = 8
- [x] `.github/WORKFLOWS.md` exists with per-workflow purpose, owner, SLA, trigger model (FR-09)
- [x] `docs/procedures/workflow-incident-response.md` exists with runbook (FR-10)
- [x] Weekly workflow-health report runs and creates/updates an issue (FR-11) — **first-run verification deferred to post-PR-#317-merge** per `baseline/branch-protection-verification-2026-06-01.md` § "Note on FR-14"; `workflow_dispatch` requires the workflow file on default branch first
- [x] Notification routing setup is documented; owner confirms `dev@spaarke.com` is receiving spaarke-dev notifications (FR-12) — documentation complete; owner-applied routing pending
- [x] Rolling 7-day workflow success rate ≥90% across remaining workflows (FR-14) — **deferred to follow-on `sdap-bff-warnaserror-cleanup-r1` project**; gate machinery in place but rate cannot reach 90% until `src/` regression (D-01) is resolved by the follow-on

## Scope

### In Scope

- Audit all 13 workflows in `.github/workflows/` (triggers, success rates, purpose, value)
- Fix `deploy-promote.yml` cascade, `deploy-infrastructure.yml` ghost triggers, `nightly-quality.yml` schedule failures
- Investigate and resolve master CI failure on run 26755019759 (post-PR #314)
- Delete or consolidate 7+ untested workflows per D-03 (delete-by-default) and D-04 (consolidate overlapping)
- Add `actionlint` pre-merge validation via new `.github/workflows/workflows-validate.yml`
- Add `actionlint` to required-status-checks on master branch protection
- Author `.github/WORKFLOWS.md` (per-workflow purpose, owner, SLA, trigger model)
- Author `docs/procedures/workflow-incident-response.md` (runbook)
- Set up weekly workflow-health report (`.github/workflows/report-workflow-health.yml`)
- Document GitHub notification routing setup (owner-applied)
- Verify gate end-to-end with deliberate-fail PR

### Out of Scope

- Modifying production code (`src/`, `power-platform/`, `infra/`, `scripts/`)
- Migrating to a different CI/CD platform
- Adding new test types or expanding test coverage
- Changing org-level GitHub settings beyond notification routing
- Reorganizing the org or repo structure
- Fixing application-level bugs surfaced by CI (route to product backlog)

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| **D-01**: `actionlint` is the canonical validator | Actions-specific knowledge catches duplicate keys, undefined vars, runner labels; alternatives lose ground over time | design.md §4 D-01 |
| **D-02**: Failure observability via weekly scheduled report, not real-time alerts | Real-time alerts recreate the original problem (notifications get ignored); weekly aggregate report has higher signal density | design.md §4 D-02 |
| **D-03**: Delete-by-default for never-used workflows | Burden of proof is on someone to articulate value; soft-deleting (commenting out) is not acceptable | design.md §4 D-03 |
| **D-04**: One workflow per concern; consolidate liberally | 13 workflows' cognitive overhead exceeds their value; merge overlapping deploys | design.md §4 D-04 |
| **D-05**: Notification routing at GitHub account/org level, not workflow level | SMTP actions are fragile; built-in GitHub routing is proper path. Owner applies routing manually. | design.md §4 D-05 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Deleting a workflow that turns out to be load-bearing | Medium | Medium | Phase 2 audit searches for usage; D-03 default-delete forces a "speak now" moment; recoverable via `git log -- .github/workflows/{name}.yml` |
| `actionlint` rejects legitimate workflows (false positives) | Medium | Low | Phase 1 smoke pass identifies false positives BEFORE making it a required check |
| Notification routing change locks owner out of seeing real alerts | High | Low | D-05 makes this owner-driven manual step with documented before/after; owner controls timing |
| Cascade failures (P2 pattern) hide in workflows we haven't found | Medium | Low | Phase 0 inventory is comprehensive; Phase 1 loader audit covers all 13 |
| Phase 0 baseline shows `sdap-ci.yml` is STILL broken on master post-merge | High | Medium | Means PR #314 fix didn't cover the root cause; treat as a project-blocker; pause Phase 1 until resolved (FR-02) |
| First `workflows-validate.yml` PR can't satisfy its own required-check | Medium | High | Brief `enforce_admins` bypass during initial rollout (predecessor pattern); logged in `decisions/` |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| PR #314 merged on master (predecessor fix) | Internal | ✅ Done (2026-06-01) | `sdap-ci.yml` duplicate-key bug fixed |
| `enforce_admins: true` restored after predecessor | Internal | ✅ Done (2026-06-01) | Restored within 2 minutes of merge |
| `gh` CLI available locally | External | Ready | Used by Phase 0 inventory + Phase 4 reporting |
| Owner ability to apply notification routing manually | External | Pending | D-05 owner action item; FR-12 documents the steps |
| GitHub Actions service availability | External | Ready | Standard external risk |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | ralph.schroeder@hotmail.com | Overall accountability; D-05 owner action |
| Implementation | Claude Code | Phase 0–5 task execution |
| Reviewer | Owner | Per-PR review during execution |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-01 | 1.0 | Initial project artifacts generated by `/project-pipeline` | Claude (project-setup skill) |
| 2026-06-01 | 1.1 | Project complete; 16/17 acceptance criteria satisfied in-branch; FR-11 first-run + FR-14 ≥90% rate verification deferred to post-merge per design.md §1 chicken-and-egg | Claude (task-090 wrap-up) |
| 2026-06-02 | 1.2 | Closeout PRs: FR-12 routing applied + signed off (PR #322); MULTI-ENVIRONMENT-PROVISIONING-GUIDE.md drafted (PR #322); actionlint path-filter deadlock fixed (PR #322); deploy-promote.yml prod→production aligned (PR #323); D-11 OIDC federated credentials added + verified working via direct-target prod deploy (PR #324 + PR #325); D-12 deploy mechanics verified end-to-end (PR #326). | Claude (closeout) |
| 2026-06-02 | 1.3 | D-12 CLOSED. Post-deploy smoke-test surfaced production-environment-completion gap (Service Bus connection string is a literal placeholder in production Key Vault) — out of github-actions-rationalization-r1 scope; handed off as new project `production-environment-setup-r3` (see D-12 § "Recommended follow-on"). Inherits from `production-environment-setup-r2`'s explicit Out-of-Scope decision on Service Bus value verification. | Claude (D-12 handoff) |

---

*Template version: project-README.template.md v1.0 | Based on Spaarke development lifecycle*
