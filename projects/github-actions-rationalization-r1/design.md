# GitHub Actions Rationalization — Design

> **Project**: `github-actions-rationalization-r1`
> **Status**: 🟡 Design (drafted 2026-06-01)
> **Owner**: ralph.schroeder@hotmail.com
> **Predecessor**: `sdap-bff.api-test-suite-repair` (closed 2026-06-01) — that project surfaced and partially addressed the symptoms; this project addresses root causes.

---

## 1. Motivation

The 2026-06-01 GitHub Actions assessment (during the BFF test-suite-repair project) surfaced a compounding problem set:

- `sdap-ci.yml` had a 1-character bug (duplicate YAML mapping key) that silently broke the CI workflow for **weeks**. Nobody noticed because failures had become noise.
- Multiple workflows (`deploy-promote.yml`, `deploy-infrastructure.yml`, `nightly-quality.yml`) are failing on every push/schedule. Each failure generates a notification; the notifications are being filtered or ignored.
- 13 workflows exist in `.github/workflows/`. At least 7 have never been verified to work end-to-end. Many appear to be scaffolds from prior project phases.
- The branch-protection `enforce_admins: true` configuration was masking the workflow brokenness — the gate required passing checks that never posted, so all PRs were blocked. Owner had to bypass protection 3+ times in recent history to land any work, which trains everyone that "the gate is theater" — exactly the failure mode the gate was set up to prevent.

**The underlying issue**: when CI/CD fails consistently, it gets ignored. When it gets ignored, real regressions hide in the noise. This project breaks that cycle by ensuring every workflow that exists works AND provides value.

The owner's stated principle (2026-06-01):
> *"BUT we need to do a proper review of the github actions etc. that they are working correctly and providing value otherwise we always just ignore and that's not useful."*

This project operationalizes that principle.

---

## 2. Problem Statement

### 2.1 What's broken now (measured)

| Problem | Evidence | Severity |
|---|---|---|
| **P1 — `sdap-ci.yml` duplicate-key** | Duplicate `if-no-files-found: warn` at lines 184+186 of pre-fix file; every run completed 0s with `conclusion: failure`, `workflow_run_id: 0`, `jobs:[]` | RESOLVED 2026-06-01 (commit `c9863276` shipped via PR #314) |
| **P2 — `deploy-promote.yml` cascade** | `workflow_run` trigger fires on `SDAP CI` completion regardless of conclusion; job-level `if:` filters out non-success but the workflow itself still reports `failure`. Doubles every P1 failure into 2 failure records. | HIGH |
| **P3 — `deploy-infrastructure.yml` ghost triggers** | Workflow declares triggers limited to `pull_request` + `push to master` + `workflow_dispatch` with path filters, but runs appear in history on every non-master push with `event: "push"` and `jobs:[]` — same loader-failure pattern as P1 | HIGH (mechanism unclear; needs investigation) |
| **P4 — `nightly-quality.yml` failing on schedule** | Most recent scheduled run (2026-06-01 07:37) = failure; pattern likely recurring nightly | MEDIUM |
| **P5 — 7+ untested workflows** | `deploy-bff-api.yml`, `deploy-platform.yml`, `deploy-slot-swap.yml`, `deploy-office-addins.yml`, `provision-customer.yml`, `weekly-quality.yml`, `insights-eval.yml`, `auto-add-to-project.yml` — never observed running successfully in recent history | UNKNOWN (need audit) |
| **P6 — No CI for CI** | Workflow files ship without validation. P1 was a duplicate-key bug — `actionlint` catches this in <1 second. No pre-merge check is currently configured. | HIGH |
| **P7 — Failure observability** | No alerting; failures are filtered to email; email is ignored. No dashboard, no on-call rotation, no SLA. | MEDIUM (process, not config) |

### 2.2 Scope of failure

Last 30 days of `gh run list` shows the pattern. Roughly half of all workflow runs are failures. Many of those are non-actionable (the workflow itself is the bug, not the underlying code). The signal-to-noise ratio is too low for failures to drive action.

---

## 3. Scope

### 3.1 In scope

- Audit all 13 workflows in `.github/workflows/`: trigger config, last-30-day success rate, declared purpose, value-statement
- Fix or delete each broken workflow (P2, P3, P4 from the table above)
- Verify each non-broken workflow still serves a purpose; delete or consolidate where it doesn't
- Add `actionlint` pre-merge validation (`.github/workflows/workflows-validate.yml`)
- Author `.github/WORKFLOWS.md` documenting each remaining workflow's purpose, trigger model, ownership, and SLA
- Configure failure observability: at minimum, scheduled report of workflow failure rates over rolling 7-day window, posted as an issue or to a Slack/email destination
- Update notification routing so workflow failures reach `dev@spaarke.com` (shared team inbox), not personal individual addresses
- Re-verify the BFF branch protection gate works end-to-end (FR-30 from the predecessor project achieved "operational PASS WITH CONTEXT"; this project achieves full PASS)

### 3.2 Out of scope

- Modifying Azure infrastructure or production code under `src/` (NFR — same as predecessor)
- Migrating to a different CI/CD platform (the question is "make Actions work," not "replace Actions")
- Adding new test types or coverage (the existing suites are in good shape post-predecessor; this project doesn't add work to them)
- Changing organization-level GitHub settings beyond notification routing
- Reorganizing the org structure or repo layout

### 3.3 Outcomes (must ship together)

| Outcome | Definition |
|---|---|
| **A. All workflows verified** | Every workflow in `.github/workflows/` either passes a clean run OR is deleted with rationale |
| **B. Pre-merge validation** | `actionlint` runs on every PR touching `.github/workflows/**`; P1-class bugs fail at PR time |
| **C. Observability** | Failure rate is measured + reported; failures cannot silently accumulate |
| **D. Documentation** | `.github/WORKFLOWS.md` exists with per-workflow purpose, owner, SLA |

---

## 4. Locked Decisions

### D-01: actionlint is the canonical validator
`actionlint` is the de facto standard for GitHub Actions linting. It catches duplicate keys, undefined variables, expression syntax errors, runner labels that don't exist, missing inputs, and many more. Alternatives (custom Python YAML validation, etc.) lose ground over time as Actions adds features. **Decision**: use actionlint via `rhysd/actionlint@v1` GitHub Action OR via Docker container, run on every PR.

### D-02: Failure observability via scheduled report, not real-time alerts
Real-time alerts on every workflow failure recreate the original problem (notifications get ignored). A daily/weekly **report** has a higher signal density and forces aggregate triage. **Decision**: a `report-workflow-health.yml` workflow that runs weekly, queries `gh run list`, computes per-workflow success rates over 7 days, and creates/updates an open issue titled "CI Health Report — week of YYYY-MM-DD". The issue gets one comment per week with the latest snapshot. People can subscribe to the issue if they want notifications.

### D-03: Delete-by-default for never-used workflows
For each workflow in P5 (never observed running successfully): the default is **delete**. Burden of proof is on someone to articulate value. Soft-deleting (commenting out trigger blocks) is NOT acceptable — that's how dead code accumulates. Hard delete; recreate from git history if ever needed.

### D-04: One workflow per concern; consolidate liberally
Multiple deploy workflows (`deploy-bff-api`, `deploy-platform`, `deploy-promote`, `deploy-slot-swap`, `deploy-infrastructure`, `deploy-office-addins`) likely overlap. **Decision**: if 2+ workflows do similar things, consolidate into 1 with conditionals. The cognitive overhead of 13 workflows exceeds the value most provide.

### D-05: Notification routing handled at GitHub account/org level, not workflow level
Workflow-level email sends (via SMTP actions) have been explored historically and are fragile. The proper path is GitHub's built-in notification routing: configure org-level email routing OR per-user notification preferences. **Decision**: out of workflow code; document the steps in `.github/WORKFLOWS.md` and `docs/procedures/`. The owner action item (2026-06-01) is to add `dev@spaarke.com` and route `spaarke-dev` notifications to it.

---

## 5. Phased Delivery

### Phase 0: Inventory + baseline (~4 hours, 1 day wall)
- For each of 13 workflows: capture last-30-day success rate via `gh run list`, declared triggers, file size, last meaningful commit
- Produce `projects/.../baseline/workflow-inventory-2026-06-01.md`
- Capture current branch protection state to `baseline/branch-protection-2026-06-01.json` (snapshot)
- Confirm the post-P1-merge state: does `sdap-ci.yml` now succeed on master pushes? (Should answer YES after PR #314 lands; if NO, there's a deeper issue we need to address before Phase 1)

### Phase 1: Loader audit + fix broken workflows (~8-12 hours, 2-3 days wall)
- **P2 fix**: `deploy-promote.yml` — change `workflow_run.types: [completed]` to `[completed]` + add workflow-level `if:` filter for success only, OR remove the `workflow_run` trigger entirely if deploy-promote is supposed to be manual-only (read the file end-to-end first to determine intent)
- **P3 investigation**: `deploy-infrastructure.yml` — read end-to-end, find the trigger anomaly. Hypothesis: same loader-failure pattern as P1; needs strict YAML review
- **P4 fix**: `nightly-quality.yml` — read the failed run logs; determine if it's loader-broken (P1/P3 pattern) or a real test/coverage failure being reported; address accordingly
- After each fix: push to a test branch (NOT master), confirm the workflow runs cleanly, then PR + merge
- Exit gate: each of P2/P3/P4 either runs to a real conclusion (not 0s loader failure) or is deleted with rationale in commit message

### Phase 2: Rationalization (~6-8 hours, 1-2 days wall)
- For each P5 workflow: audit declared purpose, search recent project history for "did this run for X reason in last 90 days?", make keep/delete decision per D-03
- For workflows kept: consolidate per D-04 where overlap is obvious (e.g., merge `weekly-quality.yml` + `nightly-quality.yml` into one workflow with conditional cadence)
- Each delete = 1 commit with rationale
- Exit gate: workflow count ≤8 (down from 13), each with documented purpose

### Phase 3: Prevention (~4-6 hours, 1 day wall)
- Author `.github/workflows/workflows-validate.yml`: triggers on PR touching `.github/workflows/**`; runs `actionlint`; fails the PR on any error
- Add to required-status-checks for master branch protection (alongside existing `Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`)
- Smoke-test by intentionally introducing a duplicate-key error on a test branch — confirm it blocks merge
- Exit gate: actionlint check is required + verified blocking on a deliberate-fail PR

### Phase 4: Observability + docs (~6-8 hours, 1-2 days wall)
- Author `.github/workflows/report-workflow-health.yml`: weekly schedule; queries `gh run list`; computes per-workflow success rates; creates/updates issue
- Author `.github/WORKFLOWS.md`: per-workflow purpose, owner, SLA, trigger model, common-failure-modes
- Author `docs/procedures/workflow-incident-response.md`: when a workflow fails, what's the runbook?
- Configure GitHub notification routing steps in `.github/WORKFLOWS.md` for the owner to apply (D-05; out of workflow code)
- Exit gate: weekly report runs once successfully; issue exists; docs reviewed

### Phase 5: Validate the gate
- Open a deliberate-fail PR (similar to predecessor's task 023): branch from current master, add a deliberate-fail test, confirm branch protection blocks merge AND `actionlint` blocks workflow changes
- Close the verify PR; document evidence
- Exit gate: branch protection + actionlint both verified blocking

**Total effort**: 28–42 hours / 6–9 calendar days

---

## 6. Binding Rules (NFRs)

- **NFR-01**: No production code changes (`src/`, `power-platform/`, `infra/`, `scripts/`). Workflow files and `.github/` only.
- **NFR-02**: Each workflow file edit <50% replacement; >50% requires escalation OR delete-and-rewrite with explicit decision record.
- **NFR-03**: Don't disable `enforce_admins` outside the merge-window of an actionlint-fix PR. Each disable is logged in `decisions/`.
- **NFR-04**: All deletions of workflows go via `git rm` + commit with rationale, NOT comment-out-and-leave.
- **NFR-05**: Pre-merge actionlint check is required-status-check on master before Phase 5 starts.

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| Deleting a workflow that turns out to be load-bearing for a process nobody documented | Phase 2 audit explicitly searches for usage; D-03 default-delete forces a "speak now or it goes" moment; recovery via `git log -- .github/workflows/{name}.yml` |
| `actionlint` is opinionated; may reject legitimate workflows | Phase 1 includes a smoke pass to identify any false-positives BEFORE making it a required check |
| Notification routing change locks the owner out of seeing real alerts | D-05 makes this an owner-driven manual step with documented before/after; owner controls timing |
| Cascade failures (P2 pattern) hide in other workflows we haven't found | Phase 0 inventory is comprehensive; Phase 1's loader audit covers all 13 |
| Phase 0 baseline shows `sdap-ci.yml` is STILL broken on master post-merge | Means the PR #314 fix didn't cover the root cause; treat as a project-blocker; pause Phase 1 until resolved |

---

## 8. Success Criteria (preview — formalized in spec.md)

1. ✅ Every workflow either passes a clean run on its declared triggers OR is deleted with rationale
2. ✅ `actionlint` is a required status check on master; a deliberate-fail PR is blocked
3. ✅ Workflow count is rationalized (≤8 from 13)
4. ✅ `.github/WORKFLOWS.md` exists with per-workflow documentation
5. ✅ `docs/procedures/workflow-incident-response.md` exists with runbook
6. ✅ Weekly workflow-health report runs + creates an issue
7. ✅ `dev@spaarke.com` is the notification routing target for `spaarke-dev` org notifications (owner action; documented)
8. ✅ Branch protection gate verified blocking via deliberate-fail PR
9. ✅ Rolling 7-day workflow success rate ≥90% across remaining workflows

---

## 9. References

- Predecessor project: `projects/sdap-bff.api-test-suite-repair/` — surfaced the symptoms; this project addresses root causes
- Assessment performed 2026-06-01: see `notes/` for the original assessment output if archived; key findings reproduced in §2.1
- Tools considered: `actionlint`, `yamllint`, custom Python validation. D-01 chose actionlint for its Actions-specific knowledge
- GitHub Actions docs: https://docs.github.com/en/actions

---

*Authored 2026-06-01. Status will move 🟡 → 🟢 → ✅ as the pipeline advances.*
