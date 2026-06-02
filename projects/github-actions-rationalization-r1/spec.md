# github-actions-rationalization-r1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-01
> **Source**: `design.md` (drafted 2026-06-01 by Claude during the BFF test-suite-repair predecessor)
> **Predecessor project**: `sdap-bff.api-test-suite-repair` (closed 2026-06-01)

---

## Executive Summary

The repository has 13 GitHub Actions workflows. At least 1 was silently broken for weeks (`sdap-ci.yml` duplicate YAML key — fixed by PR #314). At least 2 more are failing repeatedly (`deploy-promote.yml` cascade, `nightly-quality.yml` on schedule). 7+ have never been verified to work end-to-end. This project audits, fixes, deletes, or consolidates each workflow so every one that exists works AND provides value, adds CI for the CI workflows themselves (actionlint pre-merge), and sets up failure observability so failures cannot silently accumulate again. Estimated 28–42 hours / 6–9 calendar days.

---

## Scope

### In Scope

- Audit all 13 workflows in `.github/workflows/` (trigger config, last-30-day success rate, declared purpose, value statement)
- Fix workflows P2 (`deploy-promote.yml`), P3 (`deploy-infrastructure.yml`), P4 (`nightly-quality.yml`); investigate + resolve each
- Investigate post-PR-#314 SDAP CI failure on master (Build step + Prettier failure observed 2026-06-01 12:31 UTC, run id 26755019759) — this MUST be resolved in Phase 0 or Phase 1 before further work
- Delete or consolidate the 7 untested workflows per D-03 (delete-by-default) and D-04 (consolidate overlapping)
- Add `actionlint` pre-merge validation via new `.github/workflows/workflows-validate.yml`
- Add this new check to required-status-checks on master branch protection
- Author `.github/WORKFLOWS.md` (per-workflow purpose, owner, SLA, trigger model)
- Author `docs/procedures/workflow-incident-response.md` (runbook)
- Set up weekly workflow-health report (new `.github/workflows/report-workflow-health.yml`)
- Document GitHub notification routing setup (D-05; owner manually applies the routing)
- Verify gate with a deliberate-fail PR

### Out of Scope

- Modifying production code (`src/`, `power-platform/`, `infra/`, `scripts/`)
- Migrating to a different CI/CD platform (Azure DevOps, GitLab, etc.)
- Adding new test types or expanding test coverage beyond what exists
- Changing organization-level GitHub settings beyond notification routing
- Reorganizing the org or repo structure
- Fixing application-level bugs surfaced by CI (those go to product backlog; the project documents them via existing real-bug-ledger pattern if any are found)

### Affected Areas

- `.github/workflows/*.yml` — the 13 existing files; some will be modified, some deleted, 2 new ones added
- `.github/WORKFLOWS.md` — new file
- `docs/procedures/workflow-incident-response.md` — new file
- GitHub repository settings (branch protection on master; required status checks)
- GitHub organization notification routing (owner-applied; documented in this project)

---

## Requirements

### Functional Requirements

1. **FR-01**: Each of the 13 workflows is audited in Phase 0 — every one has a captured value statement, last-30-day success rate, owner, and recommended action (keep, fix, delete, consolidate). Acceptance: `projects/.../baseline/workflow-inventory-2026-06-01.md` exists with one entry per workflow.

2. **FR-02**: The current master CI failure (run 26755019759: Build step + Prettier failure) is root-caused and either fixed or documented as out-of-scope with rationale. Acceptance: a decision record in `decisions/D-06-...` OR the failure is resolved in a Phase 1 commit.

3. **FR-03**: `deploy-promote.yml` workflow_run cascade is fixed — failed `SDAP CI` runs no longer cascade into `deploy-promote.yml` failures. Acceptance: after the fix, a deliberately-failing SDAP CI run on master does NOT produce a deploy-promote failure record.

4. **FR-04**: `deploy-infrastructure.yml` no longer triggers on non-matching branches. Acceptance: after the fix, pushing a commit to a feature branch with no `infrastructure/bicep/**` changes produces zero `deploy-infrastructure.yml` runs.

5. **FR-05**: `nightly-quality.yml` either runs successfully on schedule OR is deleted with rationale. Acceptance: 3 consecutive nightly runs succeed, OR the workflow file is removed via `git rm` with a commit message explaining why.

6. **FR-06**: Each of the 7 untested workflows (`deploy-bff-api`, `deploy-platform`, `deploy-slot-swap`, `deploy-office-addins`, `provision-customer`, `weekly-quality`, `insights-eval`, `auto-add-to-project`) is either verified working OR deleted with rationale. Acceptance: per-workflow disposition recorded in the inventory; total workflow count drops to ≤8.

7. **FR-07**: A new `.github/workflows/workflows-validate.yml` runs `actionlint` on every PR that touches `.github/workflows/**`. Acceptance: a deliberate-fail PR (e.g., introducing a duplicate YAML key) is blocked from merging.

8. **FR-08**: The `actionlint` check is added to required status checks on master branch protection alongside the existing 3 (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`). Acceptance: `gh api repos/.../branches/master/protection/required_status_checks` returns 4 contexts.

9. **FR-09**: `.github/WORKFLOWS.md` exists, documenting per-workflow purpose, owner, SLA, trigger model, common failure modes. Acceptance: file exists; doc-drift audit confirms it matches the current workflow set.

10. **FR-10**: `docs/procedures/workflow-incident-response.md` exists with the runbook for "a workflow failed — what now?". Acceptance: file exists; references current workflow set; cross-linked from `.github/WORKFLOWS.md`.

11. **FR-11**: A new `.github/workflows/report-workflow-health.yml` runs weekly (e.g., Mondays 09:00 UTC), queries the last 7 days of workflow runs, computes per-workflow success rates, and creates/updates a single ongoing GitHub issue titled "CI Health Report". Acceptance: workflow runs once successfully; issue is created or updated with at least one snapshot.

12. **FR-12**: Notification routing documentation in `.github/WORKFLOWS.md` includes the exact steps to route `spaarke-dev` org notifications to `dev@spaarke.com` (D-05). Owner action is to apply the routing manually. Acceptance: documentation exists; owner confirms applied OR is captured as a follow-up.

13. **FR-13**: A deliberate-fail PR verifies the gate end-to-end after all fixes land — the PR is blocked by `Build & Test (Release)` failing AND by `actionlint` failing AND by `enforce_admins: true`. Acceptance: the verification PR is opened, blocked, then closed without merging; evidence captured in baseline.

14. **FR-14**: The rolling 7-day workflow success rate (across remaining workflows) is ≥90% by project close, measured by the report from FR-11. Acceptance: at least one weekly report shows ≥90% success rate before project close.

### Non-Functional Requirements

- **NFR-01**: No production code changes (`src/`, `power-platform/`, `infra/`, `scripts/`)
- **NFR-02**: Each workflow file edit is <50% line replacement; >50% requires escalation OR delete-and-rewrite with explicit decision record
- **NFR-03**: `enforce_admins` is not disabled outside the merge-window of a specific actionlint-fix PR; each disable is logged in `decisions/`
- **NFR-04**: All workflow deletions via `git rm` + commit; NOT comment-out-and-leave
- **NFR-05**: The new `actionlint` check is added to required-status-checks BEFORE Phase 5's deliberate-fail verification (FR-13 depends on FR-08 having landed)
- **NFR-06**: `decisions/` contains a record for every keep-vs-delete decision on the 7 untested workflows (one per workflow, minimum 1-paragraph rationale)
- **NFR-07**: Task POML metadata declares `<repair-not-rewrite>true</repair-not-rewrite>` (inherited binding pattern from predecessor)
- **NFR-08**: Project CLAUDE.md is created and loaded by every task agent (NFR-08 pattern from predecessor)

---

## Technical Constraints

### Applicable ADRs

This project's surface is primarily DevOps/CI tooling — most ADRs (which govern application architecture) are N/A. Applicable references:

- **ADR-029** (BFF Publish Hygiene): downstream of CI; deploy-bff-api workflow audit must preserve hash-verify + slot-swap rollback patterns mentioned in this ADR
- **ADR-001** (Minimal API + Workers): N/A for workflow files but referenced by deploy-bff-api workflow's deploy target — preserve it
- ADRs are otherwise N/A; this project does not touch `src/` code

### MUST Rules

- ✅ MUST use the `Test update obligation` pattern from `.claude/constraints/bff-extensions.md` § F (codified by predecessor task 080) for any workflow that runs tests
- ✅ MUST preserve the 3 currently-required status checks unless a deletion explicitly removes one
- ✅ MUST run `actionlint` locally on any workflow file BEFORE pushing
- ❌ MUST NOT push directly to master (use PR via feature branch)
- ❌ MUST NOT disable `enforce_admins` without a decisions/ entry recording the rationale and restoration timestamp
- ❌ MUST NOT delete a workflow without first checking `git log -- .github/workflows/{name}.yml` for recent contributors who may have context

### Existing Patterns

- `decisions/` per-decision record pattern (predecessor used D-01..D-06; this project starts at D-01 within its own scope)
- `ledgers/` pattern (predecessor produced 6 ledgers; this project may produce a `workflow-disposition-ledger.md` listing per-workflow keep/delete/consolidate decisions)
- `baseline/` pattern for capturing pre/post state
- Phase-gap task numbering (predecessor: 001-090 with gaps; this project: same pattern)
- POML task structure with `<repair-not-rewrite>true</repair-not-rewrite>` in metadata (predecessor NFR-09 pattern)
- Triple-run validation pattern (predecessor task 084 for `Failed: 0` stability) — applicable to workflow runs as well; weekly-report workflow effectively does this

---

## Success Criteria

1. [ ] Every workflow in `.github/workflows/` either passes a clean run on its declared triggers OR is deleted with rationale (FR-01, FR-05, FR-06) — Verify by: inventory document
2. [ ] Current master CI failure root-caused + addressed (FR-02) — Verify by: post-fix master CI run succeeds OR decision record explains why deferred
3. [ ] `actionlint` is a required status check on master AND blocks a deliberate-fail PR (FR-07, FR-08, FR-13) — Verify by: deliberate-fail PR closed with evidence in baseline
4. [ ] Workflow count is rationalized (≤8 from 13) (FR-06) — Verify by: `ls .github/workflows/ | wc -l`
5. [ ] `.github/WORKFLOWS.md` exists with per-workflow documentation (FR-09) — Verify by: doc-drift audit
6. [ ] `docs/procedures/workflow-incident-response.md` exists with runbook (FR-10) — Verify by: file exists; reviewed
7. [ ] Weekly workflow-health report runs + creates an issue (FR-11) — Verify by: at least one snapshot posted
8. [ ] Notification routing setup documented; owner confirms `dev@spaarke.com` is receiving spaarke-dev notifications (FR-12) — Verify by: owner confirmation
9. [ ] Rolling 7-day workflow success rate ≥90% (FR-14) — Verify by: at least one weekly report showing ≥90%

---

## Dependencies

### Prerequisites

- Predecessor project `sdap-bff.api-test-suite-repair` PR #314 must be merged on master (DONE 2026-06-01)
- `enforce_admins: true` restored after the predecessor merge (DONE 2026-06-01)
- The current master CI failure (run 26755019759) state is captured before any new work alters it (Phase 0 task)

### External Dependencies

- GitHub Actions service availability (no controllable; standard external risk)
- `gh` CLI available locally (used by Phase 0 inventory + Phase 4 reporting workflow)
- Owner ability to apply notification routing changes manually (D-05; owner action item)

---

## Owner Clarifications

*This project was scoped by Claude during the predecessor project's close-out. The owner approved the recommended option "Yes — author design.md and let me drive it" on 2026-06-01. No interactive interview was conducted; gaps surfaced in this section instead.*

| Topic | Question | Owner answer | Impact |
|---|---|---|---|
| Notification email destination | Should workflow failures notify `dev@spaarke.com` instead of personal addresses? | Yes, route `spaarke-dev` notifications to `dev@spaarke.com` (2026-06-01) | D-05: notification routing at GitHub account/org level (manual setup); FR-12 |
| Approach to follow-up | Run /design-to-spec + /project-pipeline now, or save assessment for later? | "Yes — author design.md and let me drive it" (2026-06-01) | Project sequence: design.md → spec.md (this file) → user review → /project-pipeline |
| Acceptable bypass for predecessor merge | Disable enforce_admins, merge PR #314, restore? | Approved (2026-06-01); brief bypass window, restored within 2 minutes | Predecessor closed; basis for NFR-03 binding rule |

---

## Assumptions

*Proceeding with these assumptions; owner can override during review:*

- **Weekly report cadence**: Assuming Mondays 09:00 UTC for the weekly health report (D-02). Affects FR-11 cron expression.
- **Workflow deletion timeframe**: Assuming "never observed running successfully in last 30 days" is the threshold for delete-by-default consideration (D-03). Affects Phase 2 disposition decisions.
- **actionlint version**: Assuming pinned to `rhysd/actionlint@v1` (latest major; auto-updates within v1). Affects FR-07 implementation.
- **`dev@spaarke.com` verification path**: Assuming the owner can either control the inbox directly for GitHub email verification OR has it routed as a forwarding alias. If neither, FR-12 owner-action step needs an interim plan (e.g., a dedicated `spaarke-dev-bot` GitHub user).
- **Notification routing scope**: Assuming "all spaarke-dev notifications" is the desired scope (not per-repo). Affects FR-12 documentation depth.
- **Issue-vs-Slack for reports**: Assuming GitHub-issue-based reporting (D-02) is acceptable; not bridging to Slack/Teams. Affects FR-11 design.

---

## Unresolved Questions

*Still need answers before or during implementation:*

- [ ] **Current master CI failure root cause** — Run 26755019759 had Build step failing in Release config + Prettier formatting issues. Is this environment-related (CI runner differences) OR an actual recent regression that the predecessor's measurements missed? Blocks: Phase 0 exit gate decision on whether to proceed with Phase 1 OR fix this first
- [ ] **Workflow consolidation specifics** — D-04 says consolidate overlapping deploy workflows. The 6 deploy-* workflows likely don't all overlap; concrete consolidation candidates need Phase 0 audit. Blocks: Phase 2 work item count
- [ ] **Deploy workflow ownership** — Are any of these workflows owned by a specific team member who must approve deletion? Predecessor noted "each project added without holistic consideration" — these may have hidden contracts. Blocks: Phase 2 delete decisions
- [ ] **Failure-report Slack/Teams bridging** — Is GitHub-issue-only sufficient (per D-02), or does failure observability also need a Slack/Teams notification? Blocks: FR-11 scope decision
- [ ] **Branch protection adjustment for self-modifying workflows** — When `workflows-validate.yml` is added to required checks, the FIRST PR that adds it can't satisfy its own check. May need a brief bypass during initial rollout. Blocks: FR-08 rollout sequence (resolvable via the same enforce_admins bypass pattern from predecessor)

---

*AI-optimized specification. Original design: `design.md`. Predecessor: `sdap-bff.api-test-suite-repair/exit-ledger.md`.*
