# CI/CD Workflow Guide

> **Purpose**: Developer guide for the complete CI/CD pipeline — from local commits through automated testing, pull request workflow, and deployment to staging/production.
>
> **Last Updated**: 2026-10-14
> **Last Reviewed**: 2026-10-14
> **Reviewed By**: ci-cd-unit-test-remediation-r1 task 077 — `sdap-ci.yml` retired; "How CI problems are surfaced" added (earlier: customer-provisioning-orchestration-r1, plan gap G24)
> **Status**: Current

---

## Overview

This guide explains the full CI/CD workflow for Spaarke development. The pipeline automates quality checks, testing, and deployment through GitHub Actions.

**Key Concepts**:
- **Local quality gates** run before commits (code-review, adr-check, lint)
- **`CI / Router`** (`ci-router.yml`) is the required status check on every PR to `master` — it dispatches a blocking Tier 1 and an advisory Tier 2. The ruleset also requires `Client lint (type-aware ratchet)` (`client-lint.yml`)
- Several other workflows report on PRs (actionlint, `css-reset-gate.yml`, `office-addins-tests.yml`, path-scoped gates) but are **not** in the required-check list, so a red there does not by itself block merge
- **Deployment is operator-driven**: every deploy workflow in this repo is either `workflow_dispatch`-only, or auto-deploys only to a non-production target (dev slot). Production always requires a manual trigger or a GitHub Environment reviewer approval
- The legacy monolithic pipeline (`sdap-ci.yml` + its `sdap-ci-docs-only.yml` fallback) was **retired on 2026-10-14** (ci-cd-unit-test-remediation-r1 task 077) and archived at `docs/archive/workflows/`. Its checks with no Router equivalent moved to `ci-standalone-checks.yml` (advisory); where each job went: `projects/ci-cd-unit-test-remediation-r1/notes/sdap-ci-retirement.md`
- **CI failures are never silent**: see [How CI problems are surfaced](#how-ci-problems-are-surfaced)

---

## How CI problems are surfaced

A CI problem is either **fixed** or **raised for review**. Nothing that fails is allowed to disappear into a job log.

**On a pull request**

| Check | Required? | Where a failure shows |
|---|---|---|
| `Router` (`ci-router.yml`: Tier 1 blocking + Tier 2 advisory) | **Yes** | Red required check; the PR cannot merge. Tier 2 never fails the Router; its results are in the "Tier 2 Advisory Report" PR comment |
| `Client lint (type-aware ratchet)` (`client-lint.yml`) | **Yes** | Red required check |
| `CI Standalone Checks` (`ci-standalone-checks.yml`): dependency audit, Release build + BFF publish, Compose client gate, client typecheck, PCF ESLint | No (advisory) | Red check on the PR, plus a results table in the run's `Summary` job summary |
| Other path-scoped workflows (actionlint, `office-addins-tests.yml`, `css-reset-gate.yml`, ...) | No | Red check on the PR |

Verify the required list with `gh api repos/spaarke-dev/spaarke/rules/branches/master` before relying on it.

**On master** a failure raises a **quality alert**: one GitHub issue per check, labelled `quality-alert`, **assigned to the owner (so GitHub emails them)**, edited in place while the check keeps failing, and closed automatically by the next green run. The mechanism (`.github/actions/quality-alert`) is described under [Quality alerts](#quality-alerts). Per-push checks on master:

| Alert | Raised by | `failing` when |
|---|---|---|
| `router-master` | `master-ci-alert.yml` (`workflow_run` when `CI` completes on a push to master) | the Router or any Tier 1 job is red, cancelled or timed out on its most recent master run. Master is red for every branch cut from it |
| `tier2-master/full-unit-tests` | `master-ci-alert.yml` | a Tier 2 unit-test shard reports a verdict other than `pass`, or none, on its most recent master run in which master compiled (read from the verdict artifacts; nothing is re-run) |
| `ci-standalone/dependency-audit`, `/release-build`, `/compose-client-gate`, `/client-typecheck`, `/pcf-eslint`, `/unit-tests-outside-router`, `/bff-seam-contract-tests`, `/verdict-script-tests`, `/ontology-script-tests`, `/trivy-fs` | `ci-standalone-checks.yml` job `alerts` (push to master only; skipped when a newer master run already posted) | that job is not `success` |
| `master-ci-alert/evaluation` | `master-ci-alert.yml` | the verdict script itself failed (GitHub API errors after retries), so the two alerts above were not updated |

The scheduled workflows (`nightly-health.yml`, `client-tests.yml`, `mutation-nightly.yml`) raise their own alerts the same way. The decision logic for `router-master` and `tier2-master/full-unit-tests` is `scripts/ci/master-ci-verdict.ps1`. It judges master's current state, not one run: the newest 100 completed master push runs, each job and Tier 2 shard from the most recent run that executed it. A docs-only push (Tier 1 skipped, Router green) therefore cannot close a compile-break alert, and a path-gated job keeps its last real result. It also reads the open alert issue: a job or shard the issue lists as failing that no run in the window executed is reported as still failing ("has not executed since …"), so a failing path-gated job that is not re-run cannot be dropped and "Recovered". If a newer master run is still queued or running, `ok` is held back (the alert is not closed until that run's own evaluation). Known limit: a job that failed and was then renamed or removed never runs again, so its alert stays open until closed by hand. The Pester controls are `tests/scripts/Master-Ci-Verdict.Tests.ps1`. Replay any past state: `pwsh scripts/ci/master-ci-verdict.ps1 -RunId <id> -Repo spaarke-dev/spaarke -AsOf`.

**Where to look**

- The pinned issue **`📊 Issues & Quality Dashboard`** (`issues-dashboard.yml`) lists every open quality alert with its "failing since" date, and the last result of each scheduled workflow.
- The open list `label:quality-alert is:open` ([issues](https://github.com/spaarke-dev/spaarke/issues?q=is%3Aissue+is%3Aopen+label%3Aquality-alert)) is the same information.
- **Claude Code** shows the open alerts in its session-start banner, so every session starts knowing what is red.

Closing an alert by hand does not silence the check: the next failing run reopens it.

---

## Table of Contents

1. [Pipeline Overview](#pipeline-overview)
2. [Local Development Workflow](#local-development-workflow)
3. [Commit Conventions](#commit-conventions)
4. [Pull Request Workflow](#pull-request-workflow)
5. [Pre-Merge Checklist](#pre-merge-checklist)
6. [GitHub Actions Workflows](#github-actions-workflows)
7. [Scheduled Workflows](#scheduled-workflows)
8. [Automated Code Review (AI)](#automated-code-review-ai)
9. [Deployment Pipeline](#deployment-pipeline)
10. [Monitoring and Troubleshooting](#monitoring-and-troubleshooting)
11. [Quick Reference](#quick-reference)

---

## Pipeline Overview

### End-to-End Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                      DEVELOPER WORKFLOW                              │
└─────────────────────────────────────────────────────────────────────┘
                              │
    LOCAL DEVELOPMENT         │
    ──────────────────        │
    1. Make changes           │
    2. Run quality gates:     │
       • /code-review         │
       • /adr-check           │
       • npm run lint         │
    3. Commit (conventional)  │
    4. Push to feature branch │
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│     PULL REQUEST — CI / Router (ci-router.yml)                       │
│     required status check on master (+ Client lint, client-lint.yml)│
│                                                                      │
│   classify (path-aware) ──▶ Tier 1 (blocking, ci-tier1-blocking.yml) │
│                         └─▶ Tier 2 (advisory, ci-tier2-advisory.yml) │
│                                                                      │
│   Tier 1 + Tier 2 are both SKIPPED on a docs-only diff.              │
│   router-result aggregates via alls-green; Tier 2 can NEVER fail     │
│   the gate (excluded from adjudication by construction).            │
└────────────────────────────────────────────────────────────────────┘
   Also run on PRs, REPORTING but NOT in the required-check list:
   actionlint (every PR) · css-reset-gate.yml (Code Page index.html paths) ·
   office-addins-tests.yml (office-addins + related server paths) ·
   build-provisioning-sidecar.yml (sidecar paths — build+Trivy+size only
   on PR, no push) · deploy-infrastructure.yml lint + compile only (bicep
   paths) · ci-standalone-checks.yml (advisory; paths-ignore: docs/**, **.md, .claude/**)
                              │
                              │ (merge to master)
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│         MASTER — path-triggered auto-deploy / auto-publish           │
│                                                                      │
│  deploy-spaarke-ai.yml  → builds + deploys the dev web resource      │
│  deploy-office-addins.yml → builds + deploys the Static Web App      │
│  publish-provisioning-arm-artifacts.yml → Bicep → ARM JSON artifact  │
│  build-provisioning-sidecar.yml → builds, scans, pushes sidecar image│
│  ci-standalone-checks.yml → advisory checks + quality alerts         │
│  master-ci-alert.yml      → quality alert when CI is red on master   │
└────────────────────────────────────────────────────────────────────┘
                              │
                              │ (operator runs `gh workflow run ...`)
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│      MANUAL / OPERATOR-TRIGGERED (workflow_dispatch)                 │
│                                                                      │
│  deploy-bff-api.yml        — build → staging slot → swap → verify → │
│                               (auto-rollback on prod health failure) │
│  deploy-promote.yml        — direct-target dev / staging / production│
│  deploy-infrastructure.yml — dispatch re-runs validation only (no    │
│                               deploy; customer stamps = L2 H2a)      │
│  deploy-teams-app.yml      — packages + publishes a GitHub artifact  │
│                               (admin uploads to the org catalog)     │
│  deploy-external-spa.yml   — deploy the external SPA to an SWA       │
│  publish-dataverse-solutions-manifest.yml — release-time only        │
│  publish-copilot-agent-template.yml — release-time only (T257)       │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          SCHEDULED                                    │
│  mutation-nightly.yml (dispatch only; schedule pending #1870)         │
│  issues-dashboard.yml (every 6h + issue events)                       │
│  nightly-health.yml (daily, 06:00 UTC)                                │
│  client-tests.yml (nightly, 07:00 UTC)                                │
│  pcf-build-prod-nightly.yml (daily, 08:00 UTC)                        │
│  adr-audit.yml / report-workflow-health.yml (weekly, Mon 09:00 UTC)   │
└────────────────────────────────────────────────────────────────────┘
```

### Pipeline Summary

| Stage | Trigger | Budget / Notes | Blocking |
|-------|---------|-----------------|----------|
| Pre-commit Hooks | Auto (`git commit`, Husky) | ~5-10s | Yes (local) |
| Local Quality | Manual | ~30s | Yes (recommended) |
| `CI / Router` (`ci-router.yml`) | PR → master, push → master, merge_group | Tier 1 p95 ≤ 3 min, Tier 2 p95 ≤ 8 min (spec budgets) | **Yes** (required together with `Client lint (type-aware ratchet)`) |
| actionlint (`workflows-validate.yml`) | Every PR, no path filter | ~17s | Reports only — not in the required-check list (see note below) |
| `css-reset-gate.yml` | PR/push touching Code Page `index.html` | sub-second | Reports only — not in the required-check list |
| `office-addins-tests.yml` | PR/push touching office-addins + related server/test paths | gated-jest ~16s; other jobs vary | Reports only — not in the required-check list |
| `build-provisioning-sidecar.yml` | PR/push touching the sidecar; dispatch | n/a | Blocking for its own scope (fixable HIGH/CRITICAL Trivy finding hard-fails); push leg also publishes the image |
| `deploy-bff-api.yml` | Manual dispatch | n/a | N/A (deploy) |
| `deploy-promote.yml` | Manual dispatch | n/a | N/A (deploy) |
| `deploy-infrastructure.yml` | PR/push (bicep paths), manual dispatch | n/a | Reports only — not in the required-check list (lint + compile; deploys nothing) |
| `deploy-office-addins.yml` | Push to `master`/`work/SDAP-outlook-office-add-in` (paths), dispatch | n/a | N/A (auto-deploys) |
| `deploy-spaarke-ai.yml` | Push to `master` (paths), dispatch | n/a | N/A (auto-deploys to dev; production is manual + environment approval) |
| `deploy-teams-app.yml` | Manual dispatch | n/a | N/A (builds, packages, publishes an artifact — not a live deploy) |
| `deploy-external-spa.yml` | Manual dispatch | n/a | N/A (deploy) |
| `publish-provisioning-arm-artifacts.yml` | Push to `master` (bicep paths), dispatch | n/a | N/A (publish) |
| `publish-dataverse-solutions-manifest.yml` | Manual dispatch (release-time); PR dry run on the SpaarkeMaster source | n/a | N/A (publish) |
| `publish-copilot-agent-template.yml` | Manual dispatch (release-time); PR dry run on the Copilot agent source | n/a | N/A (publish) |
| `adr-audit.yml` | Weekly (Mon 09:00 UTC), dispatch | ~5 min | No (advisory; tracking issue) |
| `nightly-health.yml` | Daily (06:00 UTC), dispatch | per-job timeouts 5-45 min | No (advisory; one quality alert per check) |
| `client-tests.yml` | Nightly (07:00 UTC), dispatch | n/a | No (advisory baseline over 40 client packages) |
| `report-workflow-health.yml` | Weekly (Mon 09:00 UTC), dispatch | n/a | No (advisory; tracking issue) |
| `ci-standalone-checks.yml` | Push to `master` / PR (`paths-ignore`: docs/**, **.md, `.claude/**`, …), dispatch | per-job timeouts 15-30 min | No (advisory; quality alerts on master) |
| `master-ci-alert.yml` | `workflow_run` when `CI` completes on master, dispatch | 10 min | No (raises/clears quality alerts) |

> **Note on actionlint**: it was once a required check (which is why it runs on every PR with no path filter — a path-filtered required check deadlocks PRs that don't touch the filtered paths). It is not required today: the `master` ruleset requires `Router` and `Client lint (type-aware ratchet)` (verified 2026-10-14 with `gh api repos/spaarke-dev/spaarke/rules/branches/master`; classic branch protection is disabled). Re-run that command before relying on this list.

### Complete Workflow Inventory

| File | Name | Classification | Purpose |
|------|------|-----------------|---------|
| `adr-audit.yml` | ADR Architecture Audit | Scheduled / advisory | Full NetArchTest ADR compliance scan; creates/updates a tracking issue |
| `build-provisioning-sidecar.yml` | Build Provisioning Sidecar | PR/push (scoped) / dispatch | Build, Trivy-scan, and push the Exchange-policy provisioning sidecar image to the platform ACR |
| `ci-router.yml` | CI (job: `Router`) | **Blocking — required check** | Single-gate router: classifies the diff, dispatches Tier 1 + Tier 2, aggregates into `CI / Router` |
| `ci-standalone-checks.yml` | CI Standalone Checks | PR/push / advisory | Checks retired from `sdap-ci.yml` with no Router equivalent: dependency audit, Release build + BFF publish (`deployment-packages` artifact for `deploy-promote.yml`), Compose client gate, client typecheck, PCF ESLint; quality alerts on master |
| `ci-tier1-blocking.yml` | CI Tier 1 (Blocking) | Blocking (reusable, called by the router) | Compile, full ArchTest suite, changed-surface smoke, auth smoke, eval gate, tenant isolation, Compose fidelity gate, Xrm capability guard; the DataGrid external-host gate is advisory until its flip |
| `ci-tier2-advisory.yml` | CI Tier 2 (Advisory) | Advisory (reusable, called by the router) | Format, lint, full unit-test suite, ADR compliance, markdown links, Last-Reviewed stamp, plugin size; posts one deduplicated PR comment |
| `client-tests.yml` | Client Tests | Scheduled / advisory | Nightly test baseline (jest, vitest, node:test) across every client package with a real `test` script (count discovered at runtime; 43 on 2026-10-12) |
| `css-reset-gate.yml` | CSS Reset Gate | PR/push (scoped); reports only | Verifies every Code Page host `index.html` carries the box-sizing reset |
| `deploy-bff-api.yml` | Deploy BFF API | Manual deploy | Build, test, publish a versioned artifact to blob storage (H9), deploy to staging slot, verify, swap to production, verify, auto-rollback |
| `deploy-external-spa.yml` | Deploy External SPA (Static Web App) | Manual deploy | Build + deploy the external Secure Project Workspace SPA to Azure Static Web Apps |
| `deploy-infrastructure.yml` | Validate Bicep Infrastructure | PR/push (scoped) / dispatch; reports only | Lint every Bicep file, compile `customer.bicep` + remaining `stacks/*.bicep`. Deploys nothing — customer stamps are deployed only by the L2 control plane (H2a) |
| `deploy-office-addins.yml` | Deploy Office Add-ins to Azure Static Web App | Auto deploy (push) | Build the Outlook + Word add-ins (plus the unified Outlook+Word app package on `master`) and deploy to an Azure Static Web App |
| `deploy-promote.yml` | Environment Promotion | Manual deploy | Direct-target deploy to dev/staging/production with smoke tests; production requires reviewer approval |
| `deploy-spaarke-ai.yml` | Deploy SpaarkeAi | Auto deploy (push) to dev; manual+approval to prod | Build the single-file HTML bundle, deploy it as the `sprk_spaarkeai` Dataverse web resource |
| `deploy-teams-app.yml` | Deploy Teams App Package | Manual | Build the external-spa Teams surface as a sanity gate, package the Teams manifest, publish it as a GitHub artifact |
| `issues-dashboard.yml` | issues-dashboard | Scheduled + issue events / advisory | Regenerates the pinned Issues & Quality Dashboard issue (see Quality alerts) |
| `master-ci-alert.yml` | Master CI Alert | `workflow_run` / advisory | Raises or clears the `router-master` and `tier2-master/full-unit-tests` quality alerts when `CI` completes on a master push |
| `mutation-nightly.yml` | mutation-nightly | Dispatch only (schedule pending #1870) / advisory | Stryker.NET mutation run over the allowlisted security/contract BFF files changed in the last 24h; raises the `mutation-nightly` quality alert |
| `nightly-health.yml` | nightly-health | Scheduled / advisory | Flake hunt, bundle-size drift, vuln scan, full integration suite, coverage observation, Trivy filesystem scan, dependency audit, Graph app-role parity; one quality alert per check + rolling tracking issue |
| `office-addins-tests.yml` | Office Add-ins Tests (Gate) | PR/push (scoped); reports only | Gated jest ratchet (56 of 56 suites), production-only TypeScript typecheck, office-scope C# server suites, ESLint |
| `publish-dataverse-solutions-manifest.yml` | Publish Dataverse Solutions Manifest | Manual publish (release-time); PR dry run | Packs SpaarkeMaster managed + unmanaged from `src/dataverse/solutions/SpaarkeMaster`, checks both, and publishes them with the `dataverse-solutions-latest.json` manifest H6 reads (T218d) |
| `publish-copilot-agent-template.yml` | Publish Copilot Agent Template | Manual publish (release-time); PR dry run | Builds the per-customer Copilot agent template from `src/solutions/CopilotAgent`, renders a sample, validates it against Microsoft's schemas, and publishes it with `copilot-agent-template-latest.json` (T257) |
| `publish-provisioning-arm-artifacts.yml` | Publish Provisioning ARM Artifacts | Auto publish (push, bicep paths) | Compiles `customer.bicep` to ARM JSON and publishes it for H2a (`model1-shared` retired by task 225a) |
| `report-workflow-health.yml` | report-workflow-health | Scheduled / advisory | Weekly rolling 7-day per-workflow success-rate report (tracking issue) |
| `workflows-validate.yml` | actionlint | PR; reports | Lints every workflow YAML file with `actionlint` |

---

## Local Development Workflow

### Pre-Commit Hooks (Automatic)

Husky pre-commit hooks run automatically on every `git commit`. They execute lint-staged, which:
- **TypeScript/TSX files**: Formats with Prettier, then lints with ESLint (from the nearest `eslint.config.mjs` directory)
- **JSON/YAML files**: Formats with Prettier
- **C# files**: Formats with `dotnet format` (scoped to staged files only)

Hooks complete in under 10 seconds. If a hook fails, the commit is blocked until the issue is fixed.

**Emergency skip** (use sparingly, document justification in commit message):
```bash
git commit --no-verify -m "fix(api): emergency hotfix — skipping hooks, will fix lint in follow-up"
```

See [Testing and Code Quality Procedures](testing-and-code-quality.md#pre-commit-hooks-husky--lint-staged) for detailed Husky documentation.

### Before Committing

Run local quality gates before every commit (in addition to automatic pre-commit hooks):

```powershell
# 1. Check for changes
git status

# 2. Run quality gates (or use /push-to-github which does this automatically)
# Option A: Individual commands
npx prettier --check "src/client/**/*.{ts,tsx}"  # TypeScript formatting
cd src/client/pcf && npx eslint . --max-warnings 0  # ESLint strict
dotnet build --warnaserror   # C# lint via Roslyn

# Option B: Use Claude Code skills
/code-review                 # Security + performance + style
/adr-check                   # ADR compliance
```

### Using /push-to-github Skill

The `push-to-github` skill automates the full workflow:

```
/push-to-github

What it does:
1. Pre-flight checks (branch, changes)
2. Run /code-review on changed files
3. Run /adr-check on changed files
4. Run lint on applicable files
5. Report issues (must fix critical/errors)
6. Stage and commit with conventional message
7. Push to remote
8. Create PR (if needed)
9. Check CI status (gh pr checks)
```

### Branch Strategy

| Branch Type | Pattern | Purpose |
|-------------|---------|---------|
| Feature | `feature/{description}` | New features |
| Fix | `fix/{description}` | Bug fixes |
| Hotfix | `hotfix/{description}` | Urgent production fixes |
| Project | `project/{project-name}` | Project-based work |
| Work | `work/{feature-name}` | Feature development |

**Protected Branches:**
- `master` - Requires PR, `CI / Router` passing, approval
- `main` - Alias for master (if used)

---

## Commit Conventions

### Format

Follow **Conventional Commits** format:

```
{type}({scope}): {description}

{body - optional}

{footer - optional}
```

### Commit Types

| Type | When to Use | Example |
|------|-------------|---------|
| `feat` | New feature | `feat(pcf): add dark mode toggle` |
| `fix` | Bug fix | `fix(api): resolve token caching issue` |
| `docs` | Documentation | `docs(readme): update setup instructions` |
| `style` | Formatting only | `style(api): fix indentation` |
| `refactor` | Code restructure | `refactor(pcf): extract shared hooks` |
| `perf` | Performance | `perf(api): add Redis caching` |
| `test` | Tests | `test(api): add auth endpoint tests` |
| `chore` | Tooling/config | `chore(deps): update packages` |

### Scopes (Spaarke-specific)

| Scope | Area |
|-------|------|
| `api` | BFF API changes |
| `pcf` | PCF control changes |
| `dataverse` | Dataverse configuration |
| `infra` | Infrastructure/Bicep |
| `docs` | Documentation |
| `deps` | Dependency updates |
| `skills` | Claude Code skills |

### Commit Rules

- **Imperative mood**: "add feature" not "added feature"
- **No period** at end of subject line
- **Subject ≤ 50 chars**, body ≤ 72 chars per line
- **Reference issues** in footer: `Closes #123` or `Refs #456`

### Examples

```bash
# Feature commit
git commit -m "feat(pcf): add summary panel refresh button"

# Bug fix with issue reference
git commit -m "fix(api): handle null SharePoint response

The API now returns 404 instead of 500 when SharePoint returns null.

Closes #234"

# Breaking change
git commit -m "feat(api)!: require authentication on all endpoints

BREAKING CHANGE: All endpoints now require valid JWT token.
Anonymous access removed per security audit."
```

---

## Pull Request Workflow

### PR Lifecycle

```
1. CREATE BRANCH
   git checkout -b feature/my-feature

2. DEVELOP
   Make changes, commit frequently

3. PUSH
   git push -u origin feature/my-feature

4. CREATE PR (Early - Draft)
   gh pr create --draft --title "feat(scope): description"

5. CI RUNS (Automatic)
   gh pr checks --watch

6. ITERATE
   Fix issues, push updates, CI re-runs

7. MARK READY
   gh pr ready

8. REQUEST REVIEW
   gh pr edit --add-reviewer @teammate

9. MERGE (When approved + CI / Router green)
   gh pr merge --squash
```

### Creating a PR

**Option A: Using GitHub CLI**

```powershell
# Create PR
gh pr create --title "feat(pcf): add dark mode support" --body "## Summary
- Added theme toggle component
- Integrated with Fluent UI v9 tokens
- Added dark mode tests

## Testing
- [ ] Unit tests pass
- [ ] UI tested in browser
- [ ] Dark mode compliance verified

## Closes
Closes #123"
```

**Option B: Using /push-to-github skill**

```
/push-to-github

# Automatically:
# - Runs quality gates
# - Creates commit
# - Pushes to remote
# - Creates PR with template
# - Reports CI status
```

### PR Template

```markdown
## Summary
{Brief description of changes - 1-3 bullet points}

## Related
- Closes #{issue number}
- Related to: {spec or design doc link}

## Changes
- {Change 1}
- {Change 2}

## Testing
- [ ] Unit tests pass
- [ ] Manual testing completed
- [ ] ADR compliance verified

## Checklist
- [ ] Code follows Spaarke conventions
- [ ] Documentation updated (if needed)
- [ ] No secrets or sensitive data committed
```

### Monitoring PR Status

```powershell
# Check all CI checks
gh pr checks

# Watch CI in real-time
gh pr checks --watch

# View PR details
gh pr view

# View specific check logs
gh run view {run-id} --log
```

### Merge Requirements

Before merging, ensure:

| Requirement | Check |
|-------------|-------|
| `CI / Router` passing | `gh pr checks` shows `CI / Router` green |
| Reviews approved | At least 1 approval |
| No conflicts | Branch is up-to-date with master |
| ADR compliant | No unaddressed violations in the Tier 2 PR comment |

**Merge command:**
```powershell
# Squash merge (recommended for feature branches)
gh pr merge --squash

# Regular merge (preserves commit history)
gh pr merge --merge
```

---

## Pre-Merge Checklist

Before merging any PR to master, verify all of the following:

### Automated Checks (Must Pass)

- [ ] **`CI / Router` green** — `gh pr checks` shows `CI / Router` passing. It is one of two required status checks (with `Client lint (type-aware ratchet)`); it is skipped entirely (and still reports pass) on a docs-only diff, i.e. one in which every changed file is documentation.
- [ ] **Tier 1 (blocking) jobs pass** — compile (whole solution, Debug), the full NetArchTest ArchTests suite, changed-surface integration smoke (conditional on changed BFF paths), auth smoke (conditional on auth-path changes), the golden-utterance eval gate, the I1–I5 tenant-isolation invariants, the Compose fidelity corpus round-trip, and the Xrm capability guard (conditional on client/solution/workflow changes)
- [ ] **Tier 2 (advisory) results reviewed** — format, lint, the full unit-test suite, ADR compliance, markdown-link validation, Last-Reviewed stamp, and plugin size are posted as one deduplicated "Tier 2 Advisory Report" PR comment; they never block, but address real failures
- [ ] **Path-scoped gates reviewed if touched** — `office-addins-tests.yml` (office-addins + related server/test paths), `css-reset-gate.yml` (Code Page `index.html`), `build-provisioning-sidecar.yml` (sidecar paths, hard-fails on a fixable HIGH/CRITICAL Trivy finding), `deploy-infrastructure.yml`'s `validate` job (bicep paths; lint + compile) — none of these are in the required-check list, but each reports a real pass/fail
- [ ] **actionlint clean** — `workflows-validate.yml` lints every workflow YAML file on every PR

### Manual Checks (Reviewer Responsibility)

- [ ] **ADR compliance** — Review any ADR violation comments posted by the Tier 2 "Tier 2 Advisory Report" PR comment; address critical findings
- [ ] **No secrets committed** — Verify no `.env`, credentials, API keys, or connection strings in changed files
- [ ] **Conventional commit messages** — All commits follow `type(scope): description` format
- [ ] **Branch up-to-date** — No merge conflicts with master
- [ ] **Documentation updated** — If behavior changed, relevant docs/guides updated
- [ ] **Breaking changes flagged** — If API contracts, DB schema, or config keys changed, commit message includes `BREAKING CHANGE:`

### Component-Specific Checks

**If API endpoints changed:**
- [ ] Endpoints return `ProblemDetails` for errors
- [ ] Auth filters applied (`.AddEndpointFilter<>().RequireAuthorization()`)
- [ ] CORS origins verified for new domains

**If PCF control changed:**
- [ ] Version bumped in all 5 locations (see `pcf-deploy` skill)
- [ ] Shared library `dist/` recompiled if shared components modified
- [ ] Bundle size verified (< 500KB with platform libraries)

**If infrastructure changed:**
- [ ] Bicep lint passes (`az bicep lint`)
- [ ] `customer.bicep` (and any remaining `stacks/*.bicep`) compiles — `deploy-infrastructure.yml`'s `validate` job; a checked-in `.json` twin is recompiled with `az bicep build --outfile`
- [ ] No CI what-if exists any more (retired by task 249, 2026-10-02): `customer.bicep` changes reach a stamp only through L2 handler H2a, which runs its own what-if drift check before deploying on upgrade runs (a new stamp has nothing to drift from)

**If the provisioning sidecar changed (`src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/**`):**
- [ ] `build-provisioning-sidecar.yml` PR run is green (build + Trivy scan + 250 MB compressed-size ceiling); it never pushes on a PR

---

## GitHub Actions Workflows

The subsections below are grouped by what each workflow does: the PR gate, standalone PR-time checks, deploy workflows, provisioning artifact publishers, and scheduled/operational workflows (the last group is detailed further in [Scheduled Workflows](#scheduled-workflows)).

### CI Gate (PR / Push / Merge Queue)

#### `ci-router.yml` — CI (job: `Router`)

**Triggers**: `pull_request` → `master`, `push` → `master`, `merge_group`

The required status check `CI / Router` (the ruleset also requires `Client lint (type-aware ratchet)`). A `classify` job (dorny/paths-filter, no `on:`-level path filter — a path filter here would re-introduce the stuck-pending trap) emits `bff` / `spaarke_ai` / `docs` / `ci_workflows` / `provisioning_prereqs` booleans and a derived `docs_only` flag, which is true only when EVERY changed file is documentation (a second `dorny/paths-filter` step with `predicate-quantifier: 'every'` detects any non-doc file; until 2026-10-06 client code plus a doc file counted as docs-only and skipped Tier 1). `tier1` and `tier2` are reusable-workflow calls gated on `docs_only != 'true'`. The final `router-result` job runs `if: always()` and aggregates via `re-actors/alls-green`, with Tier 2 **excluded from adjudication by construction** (not just `allowed-failures`) so a cancelled or red Tier 2 can never redden the gate. Tier 1 may legitimately be `skipped` (counts as pass) when no Tier-1 surface changed.

**`prereqs` job (blocking, since 2026-10-09 — customer-provisioning-orchestration-r1 task 208).** Runs when `scripts/provisioning-prereqs/**`, `.claude/skills/provision-environment/**` or `ci-router.yml` changes — **including docs-only diffs**, because `.claude/**` counts as documentation and would otherwise skip the check for a skill-only change. It validates `scripts/provisioning-prereqs/prereqs.yaml` (top-level shape, per-prereq required fields, scope enum, unique ids, the SPE `never_delete` guard on `PRQ-T-01`) with `validate.ps1`, using the **same** `powershell-yaml` parser the `/provision-environment` skill uses (never substitute another parser), and compiles `intake.schema.json` as a Draft 2020-12 JSON Schema via `ajv-cli` + `ajv-formats` (needed for the `format: uuid` checks on `tenantId`/`subscriptionId`). It is adjudicated by `router-result` (`allowed-skips: tier1,prereqs`), so a red `prereqs` fails `CI / Router`. It replaced the standalone advisory `provisioning-prereqs-validate.yml`, which is deleted.

#### `ci-tier1-blocking.yml` — CI Tier 1 (Blocking)

**Triggers**: `workflow_call` (from `ci-router.yml`), `workflow_dispatch`

| Job | Purpose |
|-----|---------|
| `classify-tier1` | Fine-grained path classification (auth, AI/semantic-search/workspace/reporting surfaces, `datagrid_gate`, `xrm_guard`) feeding `changed-surface-smoke`, `auth-smoke`, `datagrid-external-host-gate` and `xrm-capability-guard` |
| `compile` | `dotnet build` the **whole solution**, Debug, no `-warnaserror` (policy lives in `Directory.Build.props`) |
| `arch-tests` | The **full** NetArchTest suite (197 tests as of authoring) — no inclusion filter, so a renamed/new test can never silently drop out of the gate |
| `changed-surface-smoke` | `Spe.Integration.Tests` filtered to namespaces matching the changed BFF surface; no-op if nothing matches |
| `auth-smoke` | Runs only when `Infrastructure/Auth/**`, `Infrastructure/Graph/**`, claims, or an `*Authorization*`/`*AuthFilter*` file changed |
| `eval-gate` | Golden-utterance eval suite (`Category=GoldenUtteranceEval`) |
| `tenant-isolation` | The I1–I5 tenant-isolation invariant tests (`Spaarke.ArchTests.TenantIsolation.*`) |
| `compose-fidelity-gate` | Corpus round-trip harness over every `.docx` under `tests/fixtures/compose-corpus/` (requires Git-LFS checkout) |
| `datagrid-external-host-gate` | The shared DataGrid's jest folder; runs on `Spaarke.UI.Components` or workflow changes. **Advisory** (job-level `continue-on-error`) until its FLIP CONDITION; see the job |
| `xrm-capability-guard` | The `getXrm` capability guard (`xrmCapabilityUsage.guard.test.ts`, an AST scan of all of `src/`, ~70 s); runs on `src/client/**`, `src/solutions/**` or workflow changes. **Blocking**. Added 2026-10-06 as an owner-approved §6.5 path-A exception to the header's scope list (ontology task 081, PR #1309) |

Spec budget: p95 ≤ 3 min (NFR-01). `eval-gate`, `tenant-isolation`, and `compose-fidelity-gate` run on `windows-latest` and were ported verbatim from `sdap-ci.yml` because they are fast, security/fidelity-critical gates that must never be advisory.

#### `ci-tier2-advisory.yml` — CI Tier 2 (Advisory)

**Triggers**: `workflow_call` only (the standalone `pull_request` trigger was removed 2026-08-24 — it self-collided with the Router's call under the same concurrency group)

| Job | Purpose |
|-----|---------|
| `format` | `dotnet format whitespace --verify-no-changes` |
| `lint` | Prettier (`src/client/**/*.{ts,tsx}`) + ESLint (`src/client/pcf`) |
| `full-unit-tests` | The repo's one full-suite run, as four parallel shards (`full-unit-tests-shard`: `api-office`, `api-insights-integration`, `api-seam`, `rest`; `rest` is the complement of the others, so every test runs exactly once). Each shard is two-pass (all tests, then retry only pass-1 failures); the `full-unit-tests` job aggregates the shard verdicts, and a shard with no verdict counts as a failure. Debug only (`FR-B07`); 40-min timeout per shard as a runaway guard (expected ~18 min) |
| `adr-compliance` | The full NetArchTest suite again (non-blocking copy of Tier 1's `arch-tests`) |
| `markdown-link-validator` | `scripts/validate-markdown-links.ps1` (short-circuits with a notice if the script is absent) |
| `last-reviewed-stamp` | Verifies touched `.claude/skills/*/SKILL.md`, `.claude/constraints/*.md`, `docs/standards/*.md`, `docs/architecture/*.md`, `docs/procedures/*.md` carry a `Last Reviewed` stamp ≤ 365 days old |
| `plugin-size` | Builds `power-platform/plugins/Spaarke.Plugins` Release and checks it is < 1 MB (ADR-002) |
| `tier2-pr-comment` | Aggregates all seven jobs' **own reported outcome** (not the `continue-on-error`-masked job conclusion) into one deduplicated "🛈 Tier 2 Advisory Report" PR comment |

Every job carries `continue-on-error: true`. Spec budget: p95 ≤ 8 min (NFR-02).

### Standalone PR-Time Checks (not in the required-check list)

#### `workflows-validate.yml` — actionlint

**Triggers**: `pull_request`, no path filter (deliberate — a path filter here previously deadlocked PRs that never touched `.github/workflows/**`)

One job (`lint`) downloads `actionlint` and runs it with `-shellcheck=` (shellcheck integration disabled to avoid pre-existing `run:` block noise). Not a required check — see the note under [Pipeline Summary](#pipeline-summary).

#### `css-reset-gate.yml` — CSS Reset Gate

**Triggers**: `pull_request` / `push` → `master`, paths: `src/solutions/**/index.html`, `src/client/code-pages/**/index.html`, `scripts/check-html-css-reset.mjs`, or the workflow file itself

One job runs `node scripts/check-html-css-reset.mjs --all`, verifying every Code Page host `index.html` carries the universal box-sizing reset (prevents the DataGrid +24px-overflow regression class).

#### `office-addins-tests.yml` — Office Add-ins Tests (Gate)

**Triggers**: `pull_request` / `push` → `master`, scoped to `src/client/office-addins/**`, `src/client/shared/Spaarke.Auth/**`, one `provenance.ts` leaf file, several `src/server/api/Sprk.Bff.Api/{Api,Services,Workers}/Office/**` + `Services/Documents/**` server paths, and the corresponding test directories; also `workflow_dispatch`

| Job | Purpose |
|-----|---------|
| `gated-jest` | Runs an explicit, ratchet-only list of jest suites (`ci-gated-suites.txt`) — 56 of the package's 56 suites as of 2026-09-28; fails if a listed suite is missing or if jest runs a different count than the manifest declares (no vacuous green) |
| `typecheck` | Production-only TypeScript typecheck — fails on any production `error TS`; a pinned, equality-checked count of accepted test-file debt (68 lines as of the last re-baseline) must also match exactly |
| `server-tests` | A namespace-prefix-filtered slice of `Sprk.Bff.Api.Tests` (what the Office add-ins call: office, documents, record-search/visualization, rag send-to-index, communications send, file access, share-link auth; ~1,441 tests as of 2026-10-12, #1748/#1782) run as a 2-leg matrix (`api-office` / `rest`; checks `Server tests (office scope) (api-office)` and `(rest)`), each leg with its own floor assertion (`MIN_SELECTED_TESTS`) so a renamed namespace can't silently shrink coverage |
| `lint` | ESLint over `shared/`, `word/`, `outlook/` with `--max-warnings 0` |

**This workflow DOES fail its own run on a red job. It does NOT block a merge** — master's ruleset names only `Router` as required (see the note under [Pipeline Summary](#pipeline-summary)); each job's summary says so explicitly. The companion nightly baseline for the other 39 of 40 client packages is `client-tests.yml`.

#### `build-provisioning-sidecar.yml` — Build Provisioning Sidecar

**Triggers**: `push` → `master` (sidecar paths), `pull_request` (same paths), `workflow_dispatch`

Builds the Exchange-policy provisioning sidecar image, runs Trivy (`scan-type: image`, `severity: HIGH,CRITICAL`, `ignore-unfixed: true`, `exit-code: 1` — hard-fails on any **fixable** HIGH/CRITICAL finding), verifies the compressed image size stays under 250 MB, then (on `push`/`workflow_dispatch` only, never on a PR) logs into Azure via OIDC and pushes the image to the platform ACR with both a SHA tag and a date-versioned semver tag (never `latest`). **Required secrets/vars**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC), repo variable `SIDECAR_ACR_LOGIN_SERVER`. As of authoring, the push leg fails with `AADSTS700213` on every master push because the Entra federated identity credential for the `ref:refs/heads/master` subject does not yet exist — build/Trivy/size still run and pass.

### Deploy Workflows

#### `deploy-bff-api.yml` — Deploy BFF API

**Triggers**: `workflow_dispatch` only (the `push: master` auto-deploy trigger was removed — BFF deploys are operator-driven)

**Pipeline**: `build` → `test` → `deploy-staging` → `verify-staging` → `swap-production` (environment `production`, requires reviewer approval) → `verify-production` → `rollback` (auto, on production health-check failure) → `summary`. The `build` job additionally zips the publish output, runs three r3 gates (god-class ratchet, the I1–I5 ArchTests, naming-conformance), and pushes `bff-api-{buildId}.zip` + a `latest.json` manifest to the `provisioning-artifacts` blob container for the future H9 artifact-based customer-stamp deploy path (`graphAppRoleParity` is always recorded `Skipped` in the manifest — it is a per-customer-deployment property, not a property of this build). **Secrets**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC). Zero-downtime via staging-slot swap; `cancel-in-progress: false`.

#### `deploy-infrastructure.yml` — Validate Bicep Infrastructure

Listed here for its file name only — **it deploys nothing.** `customer.bicep` is the only customer-stamp
template and is deployed only by the L2 control plane's handler H2a, into each customer's own subscription
(owner decision D19, amended ADR-027).

**Triggers**: `pull_request`/`push` → `master` (paths: `infrastructure/bicep/**`), `workflow_dispatch` (no inputs)

| Job | Purpose |
|-----|---------|
| `validate` | `az bicep lint` over every `.bicep` file under `infrastructure/bicep/`, then `az bicep build --outfile` of `customer.bicep` + the remaining standalone `stacks/*.bicep` |

`permissions: contents: read`; no Azure login (no OIDC secrets), no GitHub Environment. *(The `what-if` and
`deploy` jobs, the `environment` / `stack` / `deploy` dispatch inputs and their `stacks/model2-full.bicep`
target were retired by task 249, 2026-10-02.)*

#### `deploy-office-addins.yml` — Deploy Office Add-ins to Azure Static Web App

**Triggers**: `push` → `master` or `work/SDAP-outlook-office-add-in`, paths: `src/client/office-addins/**`, `src/client/shared/Spaarke.Auth/**`, one `provenance.ts` leaf file, or the workflow file itself; also `workflow_dispatch`

One job (`build_and_deploy`) builds `@spaarke/auth` from source first (its `dist/` is gitignored, so a fresh CI checkout needs it built before the add-in install resolves the `file:` dependency), installs and builds the Office Add-ins bundle, **packages the unified Outlook+Word Microsoft 365 app manifest** via `scripts/Package-OfficeAddinUnified.ps1` and uploads it as the `spaarke-addin-unified-package` artifact (merges the two previously-separate host manifests into one package id, schema 1.30), then deploys the webpack `dist/` to the Azure Static Web App. **Secrets**: `AZURE_STATIC_WEB_APPS_API_TOKEN`.

#### `deploy-promote.yml` — Environment Promotion

**Triggers**: `workflow_dispatch` only (`target_environment`, optional `artifact_run_id`, `skip_smoke_tests`). The automatic `workflow_run` trigger (dev auto-promote after "SDAP CI" on master) was **removed** — some `if:` conditions inside the `plan` job still reference `workflow_run` defensively, but no such trigger exists in `on:` today. Its `deployment-packages` artifact comes from a master run of `ci-standalone-checks.yml` (job `release-build`; `sdap-ci.yml` until 2026-10-14). A blank `artifact_run_id` makes the download steps take the latest completed master push run of that workflow that carries the `deployment-packages` artifact (`branch: master`, `event: push`, `workflow_conclusion: completed`, `search_artifacts: true`; a run whose `release-build` passed but an unrelated standalone job was red is still accepted). No such run fails the job (`if_no_artifact_found: fail`; the previous value `error` is not valid and degraded to `ignore`).

**Pipeline**: direct-target deploy (NOT a dev→staging→prod chain — each target environment deploys independently, per the 2026-06-02 D-12 refactor) to `dev` / `staging` / `production`, each with `/ping` + `/healthz` smoke tests; `production` additionally requires `environment: production` reviewer approval. **Secrets**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC) + `DEV_APP_NAME`/`STAGING_APP_NAME`/`PROD_APP_NAME`.

#### `deploy-spaarke-ai.yml` — Deploy SpaarkeAi

**Triggers**: `push` → `master` (paths: `src/solutions/SpaarkeAi/**` + several `@spaarke/*` shared-lib paths + `src/solutions/LegalWorkspace/**`), `workflow_dispatch` (`environment`: dev/production)

**Pipeline**: `build` (vite single-file bundle; smoke-tests the bundle for `<!DOCTYPE html>`, an inlined `<script>`, the root mount element, and **no** external script/css URLs per ADR-026) → `deploy-dev` (automatic on `push`, or dispatch with `environment=dev`) → `deploy-production` (manual dispatch only, `environment: production` reviewer approval). Deploys the bundle as the `sprk_spaarkeai` Dataverse web resource via `Deploy-SpaarkeAi.ps1`; there is no slot-swap mechanism, so rollback is re-dispatching against a prior commit SHA. **Secrets**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC); production additionally needs `SPAARKE_DATAVERSE_PROD_URL`.

#### `deploy-teams-app.yml` — Deploy Teams App Package

**Triggers**: `workflow_dispatch` only

**Inputs**: `environment` = `dev` (default) | `prod`. The first step of every job rejects any other value (a REST/`gh` dispatch can pass one the UI would refuse), and a workflow-level `concurrency` group `deploy-teams-app-{environment}` (`cancel-in-progress: false`) queues a second run for the same environment instead of overlapping it.

`build_and_package` runs in the GitHub environment `external-spa-dev` / `external-spa-prod`. It builds the `external-spa` Teams-tab surface as a fail-fast sanity gate (reading the same `VITE_*` environment variables as `deploy-external-spa.yml`), stages the manifest as `appPackage/build/stage/manifest.json` — `dev` copies `appPackage/manifest.json` byte for byte; `prod` renders `appPackage/manifest.prod.json` with the id taken from the environment variable `TEAMS_APP_ID` (must be a GUID, not the all-zero placeholder) — validates the **staged** file (required fields, no `webApplicationInfo` per ADR-028 A8, no unrendered `${{` placeholder) and the icon assets, zips `manifest.json` + `color.png` + `outline.png` into `appPackage.zip`, and uploads it as a build artifact. `deploy` republishes it as the longer-retention artifact. **Artifact names**: `dev` → `teams-app-package-build` / `teams-app-package`; `prod` → `teams-app-package-prod-build` / `teams-app-package-prod`. "Deploy" here means publishing the artifact — org-catalog submission is a manual tenant-admin action; no programmatic deploy endpoint exists for a Teams org-catalog app (the prod package is built only until go-live, task 073).

**Variables** (GitHub environment variables, not secrets, set per environment): `VITE_BFF_API_URL`, `VITE_BACKENDS` (optional), `VITE_MSAL_AUTHORITY`, `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_TENANT_ID`, `VITE_MSAL_BFF_SCOPE`, `VITE_TEAMS_MSAL_CLIENT_ID`, `VITE_TEAMS_MSAL_TENANT_ID`, `VITE_TEAMS_MSAL_BFF_SCOPE`, and `TEAMS_APP_ID` (prod). The owner-supplied list with the dev values is in `projects/spaarke-SPA-external-access-platform-r3/notes/task-043-notes.md` section 3; no environment-specific value is written in the workflow.

#### `deploy-external-spa.yml` — Deploy External SPA (Static Web App)

**Triggers**: `workflow_dispatch` only (manual-only until a later phase adds a push/path trigger)

**Inputs**: `environment` = `dev` (default) | `prod`. The job runs in the GitHub environment `external-spa-dev` / `external-spa-prod` (put required reviewers on `external-spa-prod`; the prod run is a deliberate human act). The first step rejects any value other than `dev`/`prod`, and a workflow-level `concurrency` group `deploy-external-spa-{environment}` (`cancel-in-progress: false`) queues overlapping runs for the same environment.

One job validates the environment's variables (a fail-closed preflight before the build: required values present; GUID, `api://{guid}/user_impersonation`, https and JSON-list formats; the authority must be an https URL whose path is one tenant GUID segment), builds the external Secure Project Workspace SPA (Vite production build; every `VITE_*` value is read from the environment's `vars.*` — nothing environment-specific is committed), stages `staticwebapp.config.json` into the build output, and deploys to Azure Static Web Apps: `dev` → `swa-spaarke-external-spa-dev`, `prod` → `swa-spaarke-external-spa-prod`. **Secrets**: `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` (dev), `AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD` (prod, an `external-spa-prod` environment secret; no fall-back to the dev token). **Variables**: see the task-043 notes section 3 (same list as `deploy-teams-app.yml`, minus `TEAMS_APP_ID`).

### Provisioning Artifact Publishers

#### `publish-provisioning-arm-artifacts.yml` — Publish Provisioning ARM Artifacts

**Triggers**: `push` → `master` (paths: `infrastructure/bicep/customer.bicep`, `infrastructure/bicep/modules/**`, `scripts/canonical-secret-catalog/generated/**`), `workflow_dispatch`

Installs the `az bicep` CLI, compiles `customer.bicep` to flattened ARM JSON (it composes only local-relative `module` references, so a single ARM JSON file results), computes its SHA-256 + size, and uploads it plus a versioned-and-`-latest` manifest pair to the `provisioning-artifacts` blob container for the H2a Azure-stamp deploy handler. **Secrets/vars**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC), repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT`.

#### `publish-dataverse-solutions-manifest.yml` — Publish Dataverse Solutions Manifest

**Triggers**: `workflow_dispatch` (input `publish`, default false = dry run); `pull_request` on `src/dataverse/solutions/SpaarkeMaster/**`, `scripts/solution-authoring/SpaarkePackageScope.psm1` and the workflow itself (dry run only)

Packs SpaarkeMaster managed + unmanaged from the committed source `src/dataverse/solutions/SpaarkeMaster` with the pac CLI, checks both zips (name, version, managed flag, no environment-variable values) and the source (no missing dependency on `solution="Active"`), writes the manifest with `New-SpaarkeMasterManifest`, and keeps everything as a run artifact. Only a manual run on `master` with `publish: true` uploads the versioned zips, a versioned manifest, the rollback pointer `dataverse-solutions-latest.previous.json` and the new `dataverse-solutions-latest.json` that H6 reads (T218d; ADR-027 §3-§4 amended 2026-10-07). A version publishes once. Runbook: [`SPAARKE-SOLUTION-RELEASE-PROCESS.md`](SPAARKE-SOLUTION-RELEASE-PROCESS.md). **Secrets/vars**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC), repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT`.

#### `publish-copilot-agent-template.yml` — Publish Copilot Agent Template

**Triggers**: `workflow_dispatch` (input `publish`, default false = dry run); `pull_request` on `src/solutions/CopilotAgent/**`, `scripts/copilot-agent/**`, `tests/scripts/CopilotAgentPackage.Tests.ps1` and the workflow itself (dry run only)

Runs the package's Pester tests, then builds the per-customer Copilot agent template from `src/solutions/CopilotAgent` (`New-CopilotAgentTemplate` — refuses permissions, `webApplicationInfo`, a bot, a knowledge capability, hard-coded tenant/app/reference ids, or schema versions other than manifest v1.30 / DA v1.8 / plugin v2.4). It writes `copilot-agent-template-latest.json` with `New-CopilotAgentTemplateManifest`, renders a sample package and validates it against Microsoft's published JSON schemas (`scripts/copilot-agent/test_rendered_package.py`). Only a manual run on `master` with `publish: true` uploads the versioned template, a versioned manifest, the rollback pointer and the new latest manifest. A version publishes once (bump `version` in `appPackage/manifest.json`). The operator renders each customer's package from it (`scripts/copilot-agent/Render-CopilotAgentPackage.ps1`; [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §7.12). **Secrets/vars**: as above.

### Standalone checks and master alerts

#### `ci-standalone-checks.yml` — CI Standalone Checks

**Triggers**: `pull_request` / `push` → `master` (same `paths-ignore` list the legacy pipeline used: `docs/**`, `projects/_backlog/**`, `projects/x-*/**`, `**.md`, `.claude/**`, `memory/**`, `knowledge/**`, `CHANGELOG*`, `CODEOWNERS`, `LICENSE*`), `workflow_dispatch`. **Not required.**

The checks from `sdap-ci.yml` that have no Router or nightly equivalent, run in parallel on `ubuntu-latest`:

- `dependency-audit` — `dotnet list Spaarke.sln package --vulnerable --include-transitive`, failing on any package outside the accepted-risk list (empty since 2026-08-13; an entry is an ADR-029 accepted-risk decision). Also fails when the scan itself cannot run.
- `release-build` — Release build of the whole solution (spec FR-B07) and the BFF Release publish. On master pushes it uploads the `deployment-packages` artifact (`api/` layout, 30-day retention) that `deploy-promote.yml` downloads.
- `compose-client-gate` — the `@spaarke/compose-components` jest save-contract suite (builds its shared-lib `dist/` first).
- `client-typecheck` — `tsc` builds of six shared client libraries in dependency order, then `Spaarke.AI.Widgets` `tsc --noEmit`.
- `pcf-eslint` — `eslint .` in `src/client/pcf` (Tier 2 `lint` runs it only on SpaarkeAi/BFF changes and `client-lint.yml` does not cover PCF).
- `unit-tests-outside-router` — `dotnet test` on `Sprk.Provisioning.ControlPlane.Tests`, `Spaarke.Core.Tests`, `Spaarke.Scheduling.Tests`, `Sprk.Bff.Api.IntegrationTests` and `Spe.Integration.Tests`. The legacy `build-test` ran the whole solution; Tier 2 runs only when the Router's `bff` path filter matches, and that filter lists none of these. (The cleaner fix is widening the Router `bff` filter with `src/server/services/**`, `tests/unit/**` and `tests/integration/**`; it needs the no-edit rule on `ci-router.yml` released.) Live seam tests skip themselves when their environment is absent, so no category filter is needed.
- `bff-seam-contract-tests` — tests in `tests/integration/{seam,contract,tenant,Shared}` and `tests/unit/domain` compile into `Sprk.Bff.Api.Tests` but are outside the `bff` filter. When such a `.cs` file changes, the job runs the test classes it declares (namespace + class name filter; the whole assembly is ~14,000 tests). A shared helper that declares no test class is compiled but runs nothing; production-code changes remain Tier 2's.
- `verdict-script-tests` — Pester must-fire controls for `scripts/ci/master-ci-verdict.ps1` (fake `gh`, no network).
- `ontology-script-tests` — Pester controls for `scripts/Apply-DoLanePolicies.ps1` (policy lint, apply order, drift) and `scripts/Invoke-OntologyStampBackfill.ps1`; no Dataverse.
- `trivy-fs` — the legacy per-change repository scan: Trivy `fs`, SARIF to the Security tab under the legacy category (`.github/workflows/sdap-ci.yml:security-scan`, so existing code-scanning alerts continue as one series). Reports only; it never fails on findings (the backlog is known), and raises `ci-standalone/trivy-fs` only if the scan itself errors. The upload step warns instead of failing on fork and Dependabot PRs, which cannot write `security-events`.

`Summary` writes one results table to the run's summary. On a push to master, `alerts` raises or clears one quality alert per job (`ci-standalone/<job>`). No job uses `continue-on-error`: a failure must show as a failure, and a `continue-on-error` job reads as `success` to the alerts job.

#### `master-ci-alert.yml` — Master CI Alert

**Triggers**: `workflow_run` (workflow `CI`, completed, branch `master`; push events only), `workflow_dispatch` (inputs `run_id`, `dry_run` default true).

Runs `scripts/ci/master-ci-verdict.ps1` over the newest 100 completed master runs of `CI` (each job judged from its most recent execution; a failing job absent from the window stays failing) and raises or clears `router-master` and `tier2-master/full-unit-tests`, plus `master-ci-alert/evaluation` when the script itself fails (see [How CI problems are surfaced](#how-ci-problems-are-surfaced)). It reads the run, its jobs and the Tier 2 shard verdict artifacts; it re-runs nothing and does not touch the tier workflows. `workflow_run` workflows run from the default branch's copy only.

#### Retired: `sdap-ci.yml` and `sdap-ci-docs-only.yml`

Retired 2026-10-14 (task 077) after the Router cutover (2026-09-29) and the 7-day soak. Both files are kept, not executed, at `docs/archive/workflows/`. The job-by-job mapping (what moved to Tier 1, Tier 2, a nightly, or `ci-standalone-checks.yml`, and what was dropped with the reason) is in `projects/ci-cd-unit-test-remediation-r1/notes/sdap-ci-retirement.md`. `scripts/ci/shadow-window-status.ps1` now reports the shadow window as closed (`-Historical` re-runs the old comparison).

### Weekly ADR Audit

#### `adr-audit.yml` — ADR Architecture Audit

**Triggers**: `schedule` (`0 9 * * 1`, Monday 09:00 UTC), `workflow_dispatch`

Runs the full NetArchTest suite (`continue-on-error: true` on the test step itself) and parses the TRX into a per-ADR-grouped report, creating or updating a single open tracking issue titled "ADR Architecture Compliance Report - {date}" (labels `architecture`, `technical-debt`, `adr-audit`); auto-closes the issue when a run finds zero violations.

---

## Scheduled Workflows

Six workflows run on a `schedule:` trigger (`mutation-nightly.yml` is dispatch-only until #1870 proves a complete run). Two of them (`nightly-health.yml`, `client-tests.yml`), plus `mutation-nightly.yml`, also raise [quality alerts](#quality-alerts). There is no longer a single "nightly quality" or "weekly quality" workflow — those were deleted (see [`.claude/CHANGELOG.md`](../../.claude/CHANGELOG.md) history and the `github-actions-rationalization-r1` project) and their responsibilities now live across the workflows below.

| Workflow | Schedule | What it reports | Blocking |
|---|---|---|---|
| `issues-dashboard.yml` | Every 6 hours (also on issue open/close/label events) | Regenerates the pinned `📊 Issues & Quality Dashboard` issue: open quality alerts, last result of each nightly, new / stale issues, totals | No — edits one issue body in place, never comments |
| `mutation-nightly.yml` | **Dispatch only** (daily 05:00 UTC once #1870 is closed) | Stryker.NET mutation run over the allowlisted security/contract BFF files that changed in the last 24h (since-mode: changed lines only) | No — quality alert `mutation-nightly` + `mutation-report` artifact |
| `nightly-health.yml` | Daily, 06:00 UTC | Flake hunt, bundle-size drift, vuln scan, full integration suite, coverage observation, Trivy fs scan, dependency audit, Graph app-role parity | No — one quality alert per check + rolling tracking issue |
| `client-tests.yml` | Nightly, 07:00 UTC | test baseline (jest, vitest, node:test) across the client packages (pass/fail/install-failed/deps-build-failed table) | No — quality alert `client-tests` + job summary + `client-test-baseline` artifact |
| `adr-audit.yml` | Weekly, Monday 09:00 UTC | Full ADR NetArchTest compliance | No — tracking issue (see [above](#weekly-adr-audit)) |
| `report-workflow-health.yml` | Weekly, Monday 09:00 UTC | Rolling 7-day per-workflow success rate across every `.github/workflows/*.yml` | No — tracking issue |
| `pcf-build-prod-nightly.yml` | Daily, 08:00 UTC | `npm run build:prod` for every PCF control, judged by the build output (pcf-scripts exits 0 on a failed build) | No — advisory; job summary + artifact |

`redis-key-rotation.yml` was removed on 2026-10-05 (task 242b): every Spaarke Redis is Azure Managed Redis with access keys disabled, so there is no key to rotate, and every scheduled run had failed.

### `nightly-health.yml`

| Job | Purpose | Timeout |
|-----|---------|---------|
| `flake-hunt-run` + `flake-hunt` | Runs `tests/unit/Sprk.Bff.Api.Tests/` 3× (excluding `Category=Flaky`) as a matrix of 3 runs × 4 namespace shards (the shards `ci-tier2-advisory.yml` uses; measured 7-13 min per leg); `flake-hunt` merges the TRX per run, flags any test that passed in some runs and failed in others, and reports an incomplete hunt (missing legs) as such | 30 min per leg, 10 min analysis |
| `bundle-size` | Builds `SemanticSearch`, `CreateMatterWizard`, and `LegalWorkspace` client artifacts; flags >10% drift vs `.github/baseline-bundle-sizes.json` | 30 min |
| `vuln-scan` | `dotnet list package --vulnerable --include-transitive` + `npm audit` on 3 key client packages | 20 min |
| `full-integration` | Full, unfiltered `Spe.Integration.Tests` run (Tier 3 — never blocks a PR) | 45 min |
| `coverage-shard` + `coverage-observation` | Coverage over the whole suite in 5 shards (the 4 namespace shards plus the other test projects of `Spaarke.sln`; measured 4-20 min), merged per source line — **observation only, never a gate** (ADR-038) | 45 min per shard, 10 min merge |
| `trivy-fs` | Full filesystem Trivy scan, `exit-code: 0` (never fails the job; counts are reported) | 20 min |
| `dep-audit` | Full transitive `dotnet`/`npm` vulnerability audit across every `package-lock.json` root | 20 min |
| `graph-app-role-parity` | Compares a customer UAMI service principal's Graph app roles against `GraphAppRoles.cs`; its steps (not the job) are `continue-on-error`, and a `verdict` step exposes the job output `failed` that the alerts leg reads; **self-skips today** because the four `NIGHTLY_*` secrets it needs (`NIGHTLY_GRAPH_READER_CLIENT_ID`, `NIGHTLY_AZURE_TENANT_ID`, `NIGHTLY_SUBSCRIPTION_ID`, `NIGHTLY_UAMI_SP_OBJECT_ID`) have never been populated | n/a |
| `report` | Legacy log: appends a comment to the rolling issue "Nightly Health — {date}". **Off by default** (the alerts and the [dashboard](#quality-alerts) replaced it; rolling issue #408 is to be closed with a pointer to the dashboard right after this change merges; closing it earlier would let the next master run open a fresh one). Restore with `-f post_tracking_comment=true` | 10 min |
| `alerts` | One [quality alert](#quality-alerts) per check above (matrix); a job that is cancelled, timed out or skipped is a failing check | 5 min |

**Manual dispatch**: `gh workflow run nightly-health.yml -f update_baseline=true` (commits the current bundle sizes as the new baseline). Other inputs: `dry_run_alerts=true` (print the alert decisions instead of posting them) and `post_tracking_comment=true` (restore the legacy per-night comment).

Both unit-test jobs check out with `lfs: true`: `tests/fixtures/compose-corpus/*.docx` are Git-LFS pointers, and without the smudge ~1,084 Compose seam tests failed every night on 129-byte fixtures (the PR run has had `lfs: true` since 2026-08-13).

### `client-tests.yml`

Discovers every `package.json` with a real `test` script (filters out the npm-init default), matrix-runs `npm test -- --ci` per package (jest; vitest packages such as `external-spa` and node:test packages such as `EventCommands` are covered by the same `npm test` script) (`max-parallel: 8`, `continue-on-error: true` per leg; vitest packages get no `--ci`/`--reporters`, which vitest rejects), and writes a combined pass/fail/install-failed/deps-build-failed table to the job summary plus a `client-test-baseline` artifact. Each leg is `npm install` → **install + build the shared libraries the package depends on** → tests. The library set is derived from the package, never listed: `file:` dependencies (transitively; built when `main`/`types` point at `dist/`), siblings that any `../**/dist` path string in a `tsconfig*.json` file points into (not only `paths` entries), and siblings whose source the package imports by relative path without declaring them; any package with a `refreshTypes` script (PCF controls) also runs `npm run refreshTypes` for the gitignored `generated/ManifestTypes`. A library that will not install/build, or a failing `refreshTypes`, is reported as `deps-build-failed`, not `fail` — the package's tests never ran. Jest `moduleNameMapper` is deliberately not used to derive the set (installing a source-mapped library gives it a second copy of react). Deliberately **not** wired into `ci-tier1-blocking.yml`/`ci-tier2-advisory.yml` — see the file's own header for the promotion criteria. `office-addins-tests.yml`'s `gated-jest` job is the one promoted subset that *does* gate PRs today.

**Manual dispatch**: `gh workflow run client-tests.yml -f package_filter=<substring>` to scope to matching package paths.

### `report-workflow-health.yml`

Iterates every file in `.github/workflows/*.yml`, queries `gh run list --workflow=<file> --created=">7 days ago"`, computes a per-workflow success rate, and appends a weekly snapshot to a single rolling "CI Health Report" issue.

### `mutation-nightly.yml`

Stryker.NET 5.0.0 (installed with `dotnet tool install --tool-path`, pinned) over the security/contract-critical files that changed on the branch in the last 24 hours. Line coverage says a filter was executed; a mutation run says whether any test would notice if it were broken. A **surviving** mutant (the tests still pass with the line deliberately broken) or a **no-coverage** mutant on a changed line is the signal.

| Item | Value |
|---|---|
| Allowlist | `src/server/api/Sprk.Bff.Api/{Api/Filters, Infrastructure/Errors, Infrastructure/Authentication, Services/Access}/**` and `src/server/shared/Spaarke.Dataverse/**AccessRights*` (the latter is a second Stryker run, `project=Spaarke.Dataverse.csproj`). Edit the pathspecs in the `Resolve scope` step to widen it. |
| Window | Commits from the last 24h, or `-f since=<git ref>` on dispatch. Stryker `since` mode then mutates only the changed lines. No allowlisted file changed: the job succeeds in about a minute and the alert is `ok`. |
| Tests that may kill a mutant | Derived per run by `scripts/ci/run-stryker.sh` from the files being mutated: the dedicated suites `Filters.`, `Services.Access.`, `Infrastructure.Authentication.`, `Infrastructure.Errors.` plus every test class in `tests/**` that names a type declared in a mutated file (worst case, all 52 files: 4,661 tests; 14 files: 2,746). Compose tests are excluded (Git-LFS fixtures). The broad ADR-038 KEEP set (9,667 tests, with Api.Office / Api.Insights / Api.Documents already excluded) took 71 min for the initial run alone and was dropped. A test that reaches a mutated type without naming it is not selected. |
| Output | `mutation-report` artifact (HTML + JSON), a per-file table (killed / survived / no-coverage / score) and every surviving mutant with `file:line` in the job summary (`scripts/ci/summarize-mutation-report.py`). |
| Alert | `mutation-nightly`: `failing` when a changed file has surviving or no-coverage mutants, or the run errors or hits its cap; `ok` otherwise. |
| Timeout / duration | 190 min (~1.5x the one complete run). **One run has completed end to end** (run 38601727169, 2 changed files, 1,390 mutants, 1,259 tests: 124 min = ~21 min build and instrumentation of the 165,676-mutant assembly + ~79 min at ~3.4 s per mutant). A 14-file window (3,448 mutants) did not finish in 240 min, so a heavy night cannot finish. That is why the schedule is off until #1870 decides how to bound a night. |

**Manual dispatch**: `gh workflow run mutation-nightly.yml -f since=<ref>` (optionally `-f dry_run_alert=true` to print the alert decision instead of posting it). Use a `since` ref that predates the file you want mutated, otherwise the window is empty.

### Quality alerts

A quality alert is a GitHub issue that says "this scheduled check is failing", built so it cannot be buried. Nightly results used to land only in job summaries and in a rolling tracking issue that collected one comment per night (`nightly-health`'s #408 had ~100 that nobody read), and a job cancelled at its timeout read as nothing at all.

**The mechanism** is the composite action `.github/actions/quality-alert` (inputs `check`, `status` = `ok`\|`failing`, `summary`, `run-url`). Per check there is exactly one issue titled `🔴 Quality alert: <check>`, labelled `quality-alert` (created on first use) and **assigned to `spaarke-dev`**, which is what makes GitHub email the owner.

| Check says | An open alert exists | Closed alert exists | None exists |
|---|---|---|---|
| `failing` | Body **edited in place** (current summary, run link, "failing since" date kept from the original). No new comment. | Reopened, body rewritten, one comment "Failing again" | Created, with one comment |
| `ok` | Comment "Recovered in <run>" and **closed** | nothing | nothing |

The action never fails the calling job: if it cannot post it prints a `::warning::` and exits 0. Callers need `issues: write` and a checkout of `.github/actions`. `dry-run: true` prints the decision instead of writing. The nightly workflows expose it as a dispatch input and force it on whenever the run is not on the default branch, so a branch or proof run can never open or close a real alert.

**What raises one today**

| Check | Raised by | `failing` when |
|---|---|---|
| `nightly-health/flake-hunt`, `/bundle-size`, `/vuln-scan`, `/full-integration`, `/coverage-observation`, `/trivy-fs`, `/dep-audit`, `/graph-app-role-parity` | `nightly-health.yml` job `alerts` | the job is **not `success`** (failure, cancelled, which is how a job that hits its `timeout-minutes` reports, or skipped). Additionally: `flake-hunt` when a flake is found or a run/shard result is missing; `full-integration` when any test failed or when 0 tests passed (it did not run; its job stays green by design); `coverage-observation` when no shard produced a coverage file; `graph-app-role-parity` when a configured step failed (steps are `continue-on-error`, so the job reports success; the alert reads the job's `failed` output). Vulnerability and Trivy *counts* are not alerted: they are a standing backlog, and an alert that can never close gets ignored. So `vuln-scan`, `trivy-fs` and `dep-audit` raise their alert only if the job itself fails (crash, timeout, skipped), which they are built not to do on findings. |
| `client-tests` | `client-tests.yml` job `alert` | any package is `fail`, `install-failed` or `deps-build-failed`, or the baseline is incomplete. The alert lists the packages and links the known issues (#1788-#1793). |
| `mutation-nightly` | `mutation-nightly.yml` job `alert` | surviving or no-coverage mutants on changed lines, or the run errors, is cancelled or times out. |

**Where to look.** The open list `label:quality-alert is:open` ([issues](https://github.com/spaarke-dev/spaarke/issues?q=is%3Aissue+is%3Aopen+label%3Aquality-alert)) is the dashboard: if it is empty, every alerting nightly passed its last run. Every alert is assigned to the owner. Closing an alert by hand does not silence the check; the next failing run reopens it.

**Adding a check.** Compute `ok`/`failing` in the workflow (treat a non-`success` job result as `failing`), then call `./.github/actions/quality-alert` with a stable `check` name (`<workflow>/<check>`, never a date or run number) from a job with `if: always()` and `permissions: issues: write`. Alerts are opened and closed with the default `GITHUB_TOKEN`, which does not start other workflows; the dashboard below picks changes up on its schedule.

**Dashboard.** `issues-dashboard.yml` maintains ONE pinned issue, `📊 Issues & Quality Dashboard` (label `dashboard`), whose body is regenerated every 6 hours, on demand and on issue open/close/label events: open quality alerts with "failing since", the last result of each nightly workflow (a cancelled or missing run shows ⚠️, never ✅), issues opened in the last 7 days, open issues idle for 30+ days (count and the 10 oldest) and totals (open issues, open PRs, unassigned). It edits the body in place and never comments, so refreshing it notifies nobody. Dry run: `gh workflow run issues-dashboard.yml -f dry_run=true`. If the token cannot pin the issue the run prints a `::warning::`; pin it by hand once (Issue page, Pin issue). The Claude Code session-start banner shows the open alerts in every session.

---

## Automated Code Review (AI)

### CodeRabbit

CodeRabbit is an AI code review tool that reviews every PR automatically.

**What it does**: Provides line-by-line code review comments with architecture-aware feedback based on Spaarke ADR constraints.

**When it runs**: On every PR targeting `master` or `feature/**` branches (configured in `.coderabbit.yaml`, which exists at the repository root — confirmed present).

**Configuration**: `.coderabbit.yaml` at repository root. Key settings:
- Auto-review enabled for non-draft PRs
- Custom review instructions reference Spaarke ADRs (001, 008, 010, 021, 022)
- Path-specific instructions for `src/client/pcf/`, `src/client/code-pages/`, `src/server/api/`, and `scripts/`
- Advisory profile ("assertive" but non-blocking)
- Auto-resolve disabled — human manages conversations

**Advisory status**: CodeRabbit reviews are **informational only**. They do not block PR merges — it is not invoked by any GitHub Actions workflow in `.github/workflows/`; it runs as a separate third-party GitHub App integration. Review comments for useful insights, but use your judgment on whether to address them.

**Per-PR configuration**: You can adjust CodeRabbit behavior on a specific PR by adding a comment:
```
@coderabbitai ignore this PR
@coderabbitai review this PR
@coderabbitai configuration
```

### Claude Code Action — removed

There is **no** GitHub Actions workflow running an AI PR-review action in this repo today. `claude-code-review.yml` was deleted (commit `4edce041ef`, "remove claude-code-review workflow"): it required a missing `ANTHROPIC_API_KEY` secret (so it could never complete a review), and it duplicated coverage already provided by:
- the `code-review` skill — mandatory at `task-execute` Step 9.5 per root `CLAUDE.md` §4
- the `adr-check` skill — direct ADR validation via `/adr-check`
- `task-execute` rigor levels — explicit ADR loading per task

If PR-time AI review is reintroduced, author a new workflow rather than resurrecting the deleted one — it was not merely broken, it was judged redundant with the skill-level gates above.

### Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| CodeRabbit not reviewing | PR is a draft, or target branch not in `base_branches` | Mark PR as ready, or check `.coderabbit.yaml` `base_branches` list |
| CodeRabbit review is too noisy | Reviewing generated/vendor files | Add paths to CodeRabbit ignore patterns in `.coderabbit.yaml` |

---

## Deployment Pipeline

### Dev/Staging Deployment (Automatic, push-triggered)

Two workflows auto-deploy on a `master` push to a non-production target:

```
deploy-spaarke-ai.yml   — push to master (SpaarkeAi/shared-lib paths) → build → deploy-dev (Dataverse web resource sprk_spaarkeai)
deploy-office-addins.yml — push to master (office-addins paths) → build → deploy to Azure Static Web App
```

(Plugin deployment step removed 2026-09-25 — Spaarke ships no plugins, ADR-002.)

**Monitor an auto-deploy:**
```powershell
# View recent runs
gh run list --workflow=deploy-spaarke-ai.yml
gh run list --workflow=deploy-office-addins.yml

# Watch live
gh run watch

# Check dev health (SpaarkeAi is a Dataverse web resource, not an App Service — verify via the Dataverse Web API, not a ping endpoint)
```

### Production Deployment (Manual)

**BFF API** (`deploy-bff-api.yml`) — staging-slot swap with auto-rollback:
```powershell
gh workflow run deploy-bff-api.yml -f environment=production
gh run watch
```

**SpaarkeAi** (`deploy-spaarke-ai.yml`) — production requires `environment: production` reviewer approval:
```powershell
gh workflow run deploy-spaarke-ai.yml -f environment=production
```

**Direct environment promotion** (`deploy-promote.yml`) — direct-target, not a dev→staging→prod chain:
```powershell
gh workflow run deploy-promote.yml -f target_environment=production
```

**When to deploy to production:**
- Staging/dev verified healthy
- Feature testing complete
- Stakeholder approval (if required) — enforced mechanically for `deploy-bff-api.yml` swap, `deploy-promote.yml` production, and `deploy-spaarke-ai.yml` production via GitHub Environment reviewer rules

### Deployment Rollback

```powershell
# BFF API: automatic — the `rollback` job in deploy-bff-api.yml swaps back on a failed
# production /healthz check. To force a manual swap-back:
az webapp deployment slot swap --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --slot staging --target-slot production

# SpaarkeAi: no slot-swap mechanism — re-dispatch against the last-known-good commit
gh workflow run deploy-spaarke-ai.yml -f environment=production --ref <last-good-sha>

# View recent deployment runs
gh run list --workflow=deploy-bff-api.yml --limit 10

# Re-run a previous successful deployment
gh run rerun {previous-run-id}
```

---

## Monitoring and Troubleshooting

### Common CI Failures

| Failure | Where | Cause | Fix |
|---------|-------|-------|-----|
| `compile` (Tier 1) | `ci-tier1-blocking.yml` | Code error anywhere in the solution (whole-solution build, not just the BFF csproj) | Fix locally, push update |
| `arch-tests` / `adr-compliance` | Tier 1 (blocking) / Tier 2 (advisory) | Architecture violation | Run `/adr-check`, fix violations |
| `changed-surface-smoke` / `auth-smoke` | Tier 1 | Integration regression in the changed BFF surface | Check the uploaded TRX artifact; fix locally |
| `eval-gate` | Tier 1 | A `Category=GoldenUtteranceEval` test regressed | Run `dotnet test ... --filter "Category=GoldenUtteranceEval"` locally |
| `tenant-isolation` | Tier 1 | An I1–I5 invariant broke (treat as a security incident, not a flake) | Run `dotnet test ... --filter "FullyQualifiedName~Spaarke.ArchTests.TenantIsolation"` locally |
| `compose-fidelity-gate` | Tier 1 | A Compose corpus document round-trip lost fidelity | Download the `fidelity-gate-result` artifact for the per-document breakdown |
| `xrm-capability-guard` | Tier 1 | A `getXrm(...)` value is used for a member its requested capability does not cover, or the analyzer hit a shape it cannot follow (blind spot) | The log names file:line and the site; request the capability the code calls, or rewrite the shape. Run `npx jest src/utils/__tests__/xrmCapabilityUsage.guard.test.ts` in `Spaarke.UI.Components` |
| `format` / `lint` (Tier 2) | `ci-tier2-advisory.yml` | Formatting/lint violation | `dotnet format whitespace`; `npx prettier --write "src/client/**/*.{ts,tsx}"`; `cd src/client/pcf && npx eslint . --fix` |
| `full-unit-tests` (Tier 2) | `ci-tier2-advisory.yml` | A unit test failed on both pass 1 and the pass-2 retry | Download the `tier2-unit-test-results-Debug-<shard>` artifact of the shard that failed |
| `dependency-audit` | `ci-standalone-checks.yml` | A package has a known vulnerability (or the scan could not run) | The job summary lists the packages; upgrade, or record an ADR-029 accepted-risk entry |
| `release-build` / `compose-client-gate` / `client-typecheck` / `pcf-eslint` / `unit-tests-outside-router` / `bff-seam-contract-tests` | `ci-standalone-checks.yml` | Release-only compile error; a Compose save-contract test failed; a shared-lib type error; a PCF ESLint error; a ControlPlane/Core/Scheduling/integration test failed; a changed seam/contract test class failed | The failing step's log; on master the matching `ci-standalone/<job>` quality alert links the run |

### Viewing CI Logs

```powershell
# List recent runs
gh run list

# View specific run
gh run view {run-id}

# View with logs
gh run view {run-id} --log

# View specific job logs
gh run view {run-id} --log --job={job-id}

# Download logs
gh run view {run-id} --log > ci-logs.txt

# Download artifacts
gh run download {run-id}
```

### Re-running Failed Jobs

```powershell
# Re-run only failed jobs
gh run rerun {run-id} --failed

# Re-run entire workflow
gh run rerun {run-id}
```

### ADR Violations in CI

ADR violations appear in two places (Tier 1 `arch-tests` also blocks the merge on any ArchTest failure):

1. **Tier 2 PR Comment**: `ci-tier2-advisory.yml`'s `tier2-pr-comment` job posts one deduplicated "🛈 Tier 2 Advisory Report" comment per PR (advisory — never blocks)
2. **Weekly Issue**: `adr-audit.yml` creates/updates a tracking issue

**To fix:**
```powershell
# Run local ADR check
/adr-check

# View the Tier 1 full ArchTests run
gh run view {run-id} --log --job="Arch Tests (full suite, blocking)"

# Download the Tier 2 (advisory) ADR results artifact
gh run download {run-id} --name tier2-adr-results

```

### Health Check Endpoints

| Environment | Endpoint |
|-------------|----------|
| BFF staging slot | `https://spaarke-bff-prod-staging.azurewebsites.net/ping` + `/healthz` |
| BFF production | `https://api.spaarke.com/ping` + `/healthz` |

Expected responses:
```json
// /ping
{ "service": "Spe.Bff.Api", "status": "ok", "timestamp": "..." }

// /healthz
"Healthy"
```

SpaarkeAi (`sprk_spaarkeai`) is a Dataverse web resource, not an App Service — there is no `/ping`/`/healthz` endpoint; `deploy-spaarke-ai.yml` verifies success via the Dataverse Web API (`webresourceset` `modifiedon`).

---

## Quick Reference

### Daily Commands

```powershell
# Start work
git pull origin master
git checkout -b feature/my-feature

# During development
git add .
git commit -m "feat(scope): description"

# Push and create PR
/push-to-github  # Or manually:
git push origin HEAD
gh pr create

# Check CI
gh pr checks
gh pr checks --watch

# Merge
gh pr merge --squash
```

### CI/CD Commands

```powershell
# Workflow status
gh run list                              # Recent runs
gh run view {id}                         # Run details
gh run view {id} --log                   # Full logs
gh run watch                             # Live status

# Workflow triggers
gh workflow run deploy-bff-api.yml -f environment=production   # Trigger BFF prod deploy
gh workflow run deploy-promote.yml -f target_environment=production  # Direct-target promote
gh workflow run adr-audit.yml            # Trigger ADR audit
gh workflow run nightly-health.yml       # Trigger nightly health on-demand

# Artifacts
gh run download {id}                     # Download all
gh run download {id} --name {name}       # Specific artifact
```

### PR Commands

```powershell
# Create
gh pr create --draft                     # Draft PR
gh pr create                             # Ready PR

# Update
gh pr ready                              # Mark ready
gh pr edit --add-reviewer @user          # Add reviewer

# Status
gh pr checks                             # CI status
gh pr view                               # PR details

# Merge
gh pr merge --squash                     # Squash merge
gh pr merge --merge                      # Regular merge
```

### Skill Commands

```bash
# Local quality gates
/code-review              # Security + style review
/adr-check               # ADR compliance

# Git workflow
/push-to-github          # Full commit + PR workflow
/pull-from-github        # Sync from remote

# CI/CD
/ci-cd                   # Pipeline status and help
```

---

## Integration with Quality Gates

This CI/CD workflow integrates with the [Testing and Code Quality Procedures](testing-and-code-quality.md). For detailed tool documentation (configuration, run-locally commands, troubleshooting), see the tool-specific sections in that guide.

| Stage | Local | `CI / Router` (Tier 1 / Tier 2) | Scheduled |
|-------|-------|----------------------------------|-----------|
| Prettier | Pre-commit hook | Tier 2 `lint` job | — |
| ESLint | Pre-commit hook | Tier 2 `lint` job | `client-tests.yml` (full suite, nightly) |
| dotnet format | Pre-commit hook | Tier 2 `format` job | — |
| Tests (compile + smoke) | During implementation | Tier 1 `compile` / `changed-surface-smoke` / `auth-smoke` | `nightly-health.yml` `full-integration` |
| Full unit-test suite | — | Tier 2 `full-unit-tests` (advisory) | — |
| ADR Tests | Step 9.5: `/adr-check` | Tier 1 `arch-tests` (blocking) + Tier 2 `adr-compliance` (advisory copy) | `adr-audit.yml` (weekly) |
| Eval gate / tenant isolation / Compose fidelity | — | Tier 1 (blocking) | — |
| Coverage | — | — | `nightly-health.yml` `coverage-observation` (**observation only — never a gate, per ADR-038**) |
| Dependency / vulnerability audit | — | — | `nightly-health.yml` `vuln-scan` + `dep-audit` + `trivy-fs` |
| AI Review | Step 9.5: `/code-review` | CodeRabbit (third-party GitHub App, not a workflow) | — |

**Key principle:** Quality checks run at three levels — pre-commit hooks catch formatting instantly, `CI / Router` enforces (Tier 1) or reports (Tier 2) on every PR, and the scheduled workflows provide deep, non-blocking analysis. Run quality gates locally first (Step 9.5) to catch issues before pushing.

---

## Required Secrets

### CI Gate (`ci-router.yml` / Tier 1 / Tier 2 / `ci-standalone-checks.yml` / `master-ci-alert.yml`)

No secrets required — these run against the repo's own code with the default `GITHUB_TOKEN` (the alert jobs use it with `issues: write`).

### BFF API Deploy (`deploy-bff-api.yml`)

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC federated-identity client id |
| `AZURE_TENANT_ID` | Azure AD tenant |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |

(Resource group, App Service name, and slot name are hardcoded `env:` values in the workflow, not secrets.)

### Environment Promotion (`deploy-promote.yml`)

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | OIDC |
| `DEV_APP_NAME` / `STAGING_APP_NAME` / `PROD_APP_NAME` | Target App Service names |

### SpaarkeAi Deploy (`deploy-spaarke-ai.yml`)

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | OIDC |
| `SPAARKE_DATAVERSE_PROD_URL` | Production Dataverse org URL (production deploy only) |

### Bicep Infrastructure (`deploy-infrastructure.yml`)

**None.** The workflow ("Validate Bicep Infrastructure") only lints and compiles Bicep and never
authenticates to Azure (`permissions: contents: read`). *(It used the OIDC trio for its what-if/deploy
stages until task 249, 2026-10-02 retired them.)*

### Static Web App Deploys

| Workflow | Secret |
|---|---|
| `deploy-office-addins.yml` | `AZURE_STATIC_WEB_APPS_API_TOKEN` |
| `deploy-external-spa.yml` | `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` (dev), `AZURE_SWA_TOKEN_EXTERNAL_SPA_PROD` (prod) |

### Provisioning Artifact Publishers

| Workflow | Secret / Variable |
|---|---|
| `build-provisioning-sidecar.yml` | `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC); repo variable `SIDECAR_ACR_LOGIN_SERVER` |
| `publish-provisioning-arm-artifacts.yml`, `publish-dataverse-solutions-manifest.yml`, `publish-copilot-agent-template.yml` | `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC); repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` |

### Nightly Health — Graph App-Role Parity (currently unconfigured)

| Secret | Purpose |
|--------|---------|
| `NIGHTLY_GRAPH_READER_CLIENT_ID` / `NIGHTLY_AZURE_TENANT_ID` / `NIGHTLY_SUBSCRIPTION_ID` / `NIGHTLY_UAMI_SP_OBJECT_ID` | OIDC + target UAMI identity. None are populated today; the job self-skips with a notice until all four exist. |

---

## Related Documentation

- [Testing and Code Quality Procedures](testing-and-code-quality.md) - Quality gates, tool details (Prettier, ESLint, Husky, PSScriptAnalyzer)
- [Parallel Claude Code Sessions](parallel-claude-sessions.md) - Multi-session workflow
- [ci-cd Skill](../../.claude/skills/ci-cd/SKILL.md) - Full skill documentation
- [push-to-github Skill](../../.claude/skills/push-to-github/SKILL.md) - Git workflow skill
- [adr-check Skill](../../.claude/skills/adr-check/SKILL.md) - ADR validation

---

*Last updated: 2026-09-30*
