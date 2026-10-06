# CI/CD Workflow Guide

> **Purpose**: Developer guide for the complete CI/CD pipeline — from local commits through automated testing, pull request workflow, and deployment to staging/production.
>
> **Last Updated**: 2026-09-30
> **Last Reviewed**: 2026-09-30
> **Reviewed By**: customer-provisioning-orchestration-r1 (plan gap G24) — workflow sections rewritten against `.github/workflows`
> **Status**: Current

---

## Overview

This guide explains the full CI/CD workflow for Spaarke development. The pipeline automates quality checks, testing, and deployment through GitHub Actions.

**Key Concepts**:
- **Local quality gates** run before commits (code-review, adr-check, lint)
- **`CI / Router`** (`ci-router.yml`) is the single required status check on every PR to `master` — it dispatches a blocking Tier 1 and an advisory Tier 2
- Several other workflows report on PRs (actionlint, `css-reset-gate.yml`, `office-addins-tests.yml`, `provisioning-prereqs-validate.yml`, path-scoped gates) but are **not** in the required-check list, so a red there does not by itself block merge
- **Deployment is operator-driven**: every deploy workflow in this repo is either `workflow_dispatch`-only, or auto-deploys only to a non-production target (dev slot). Production always requires a manual trigger or a GitHub Environment reviewer approval
- A legacy monolithic pipeline (`sdap-ci.yml` + its `sdap-ci-docs-only.yml` fallback) still runs but is **no longer the required branch-protection check** — it has been superseded by `CI / Router` and is pending deletion by another project

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
│     THE single required status check on master                      │
│                                                                      │
│   classify (path-aware) ──▶ Tier 1 (blocking, ci-tier1-blocking.yml) │
│                         └─▶ Tier 2 (advisory, ci-tier2-advisory.yml) │
│                                                                      │
│   Tier 1 + Tier 2 are both SKIPPED on a docs-only diff.              │
│   router-result aggregates via alls-green; Tier 2 can NEVER fail     │
│   the gate (excluded from adjudication by construction).            │
└────────────────────────────────────────────────────────────────────┘
   Also run on PRs, REPORTING but NOT in the required-check list:
   actionlint (every PR) · provisioning-prereqs-validate.yml (every PR,
   advisory) · css-reset-gate.yml (Code Page index.html paths) ·
   office-addins-tests.yml (office-addins + related server paths) ·
   build-provisioning-sidecar.yml (sidecar paths — build+Trivy+size only
   on PR, no push) · deploy-infrastructure.yml lint + compile only (bicep
   paths) · legacy sdap-ci.yml (paths-ignore: docs/**, **.md, .claude/**)
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
│  legacy sdap-ci.yml / sdap-ci-docs-only.yml still run (not required) │
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
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          SCHEDULED                                    │
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
| `CI / Router` (`ci-router.yml`) | PR → master, push → master, merge_group | Tier 1 p95 ≤ 3 min, Tier 2 p95 ≤ 8 min (spec budgets) | **Yes — the only required status check** |
| actionlint (`workflows-validate.yml`) | Every PR, no path filter | ~17s | Reports only — not in the required-check list (see note below) |
| `provisioning-prereqs-validate.yml` | PR, push → master, merge_group | ~30s cold | Advisory |
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
| `publish-dataverse-solutions-manifest.yml` | Manual dispatch (release-time only) | n/a | N/A (publish) |
| `adr-audit.yml` | Weekly (Mon 09:00 UTC), dispatch | ~5 min | No (advisory; tracking issue) |
| `nightly-health.yml` | Daily (06:00 UTC), dispatch | per-job timeouts 20-60 min | No (advisory; rolling tracking issue) |
| `client-tests.yml` | Nightly (07:00 UTC), dispatch | n/a | No (advisory baseline over 40 client packages) |
| `report-workflow-health.yml` | Weekly (Mon 09:00 UTC), dispatch | n/a | No (advisory; tracking issue) |
| `sdap-ci.yml` (legacy) | Push to `master` / PR (`paths-ignore`: docs/**, **.md, `.claude/**`, …) | n/a | No — superseded by `CI / Router`; pending deletion |
| `sdap-ci-docs-only.yml` (legacy) | PR touching only `sdap-ci.yml`'s ignored paths | n/a | No — paired no-op fallback for `sdap-ci.yml` |

> **Note on actionlint**: it was once a required check (which is why it runs on every PR with no path filter — a path-filtered required check deadlocks PRs that don't touch the filtered paths). It is not required today: the `master` ruleset requires only `Router` (verified 2026-09-30 with `gh api repos/spaarke-dev/spaarke/rules/branches/master`; classic branch protection is disabled). Re-run that command before relying on this list.

### Complete Workflow Inventory

| File | Name | Classification | Purpose |
|------|------|-----------------|---------|
| `adr-audit.yml` | ADR Architecture Audit | Scheduled / advisory | Full NetArchTest ADR compliance scan; creates/updates a tracking issue |
| `build-provisioning-sidecar.yml` | Build Provisioning Sidecar | PR/push (scoped) / dispatch | Build, Trivy-scan, and push the Exchange-policy provisioning sidecar image to the platform ACR |
| `ci-router.yml` | CI (job: `Router`) | **Blocking — required check** | Single-gate router: classifies the diff, dispatches Tier 1 + Tier 2, aggregates into `CI / Router` |
| `ci-tier1-blocking.yml` | CI Tier 1 (Blocking) | Blocking (reusable, called by the router) | Compile, full ArchTest suite, changed-surface smoke, auth smoke, eval gate, tenant isolation, Compose fidelity gate |
| `ci-tier2-advisory.yml` | CI Tier 2 (Advisory) | Advisory (reusable, called by the router) | Format, lint, full unit-test suite, ADR compliance, markdown links, Last-Reviewed stamp, plugin size; posts one deduplicated PR comment |
| `client-tests.yml` | Client Tests (Jest) | Scheduled / advisory | Nightly jest baseline across every client package with a real `test` script (40 packages as of authoring) |
| `css-reset-gate.yml` | CSS Reset Gate | PR/push (scoped); reports only | Verifies every Code Page host `index.html` carries the box-sizing reset |
| `deploy-bff-api.yml` | Deploy BFF API | Manual deploy | Build, test, publish a versioned artifact to blob storage (H9), deploy to staging slot, verify, swap to production, verify, auto-rollback |
| `deploy-external-spa.yml` | Deploy External SPA (Static Web App) | Manual deploy | Build + deploy the external Secure Project Workspace SPA to Azure Static Web Apps |
| `deploy-infrastructure.yml` | Validate Bicep Infrastructure | PR/push (scoped) / dispatch; reports only | Lint every Bicep file, compile `customer.bicep` + remaining `stacks/*.bicep`. Deploys nothing — customer stamps are deployed only by the L2 control plane (H2a) |
| `deploy-office-addins.yml` | Deploy Office Add-ins to Azure Static Web App | Auto deploy (push) | Build the Outlook + Word add-ins (plus the unified Outlook+Word app package on `master`) and deploy to an Azure Static Web App |
| `deploy-promote.yml` | Environment Promotion | Manual deploy | Direct-target deploy to dev/staging/production with smoke tests; production requires reviewer approval |
| `deploy-spaarke-ai.yml` | Deploy SpaarkeAi | Auto deploy (push) to dev; manual+approval to prod | Build the single-file HTML bundle, deploy it as the `sprk_spaarkeai` Dataverse web resource |
| `deploy-teams-app.yml` | Deploy Teams App Package | Manual | Build the external-spa Teams surface as a sanity gate, package the Teams manifest, publish it as a GitHub artifact |
| `nightly-health.yml` | nightly-health | Scheduled / advisory | Flake hunt, bundle-size drift, vuln scan, full integration suite, coverage observation, Trivy filesystem scan, dependency audit, Graph app-role parity; rolling tracking issue |
| `office-addins-tests.yml` | Office Add-ins Tests (Gate) | PR/push (scoped); reports only | Gated jest ratchet (56 of 56 suites), production-only TypeScript typecheck, office-scope C# server suites, ESLint |
| `provisioning-prereqs-validate.yml` | provisioning-prereqs-validate | PR/push/merge_group; advisory | Validates `prereqs.yaml` + `intake.schema.json` shape and parser parity with the `/provision-environment` skill |
| `publish-dataverse-solutions-manifest.yml` | Publish Dataverse Solutions Manifest | Manual publish (release-time) | Locates the 8 canonical pre-built managed-solution ZIPs, uploads them, and publishes the manifest H6 reads |
| `publish-provisioning-arm-artifacts.yml` | Publish Provisioning ARM Artifacts | Auto publish (push, bicep paths) | Compiles `customer.bicep` to ARM JSON and publishes it for H2a (`model1-shared` retired by task 225a) |
| `report-workflow-health.yml` | report-workflow-health | Scheduled / advisory | Weekly rolling 7-day per-workflow success-rate report (tracking issue) |
| `sdap-ci-docs-only.yml` | SDAP CI - Docs-Only Fallback | Legacy, PR-scoped | No-op success check pairing with `sdap-ci.yml`'s `paths-ignore` gap |
| `sdap-ci.yml` | SDAP CI | Legacy, no longer required | Original monolithic pipeline (security scan, build/test, eval gate, client/code quality, tenant isolation, integration readiness, Compose fidelity + client gates); superseded by `CI / Router`, pending deletion |
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

- [ ] **`CI / Router` green** — `gh pr checks` shows `CI / Router` passing. This is the only required status check; it is skipped entirely (and still reports pass) on a docs-only diff.
- [ ] **Tier 1 (blocking) jobs pass** — compile (whole solution, Debug), the full NetArchTest ArchTests suite, changed-surface integration smoke (conditional on changed BFF paths), auth smoke (conditional on auth-path changes), the golden-utterance eval gate, the I1–I5 tenant-isolation invariants, and the Compose fidelity corpus round-trip
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

The single required status check (`CI / Router`). A `classify` job (dorny/paths-filter, no `on:`-level path filter — a path filter here would re-introduce the stuck-pending trap) emits `bff` / `spaarke_ai` / `docs` / `ci_workflows` booleans and a derived `docs_only` flag. `tier1` and `tier2` are reusable-workflow calls gated on `docs_only != 'true'`. The final `router-result` job runs `if: always()` and aggregates via `re-actors/alls-green`, with Tier 2 **excluded from adjudication by construction** (not just `allowed-failures`) so a cancelled or red Tier 2 can never redden the gate. Tier 1 may legitimately be `skipped` (counts as pass) when no Tier-1 surface changed.

#### `ci-tier1-blocking.yml` — CI Tier 1 (Blocking)

**Triggers**: `workflow_call` (from `ci-router.yml`), `workflow_dispatch`

| Job | Purpose |
|-----|---------|
| `classify-tier1` | Fine-grained path classification (auth, AI/semantic-search/workspace/reporting surfaces) feeding `changed-surface-smoke` and `auth-smoke` |
| `compile` | `dotnet build` the **whole solution**, Debug, no `-warnaserror` (policy lives in `Directory.Build.props`) |
| `arch-tests` | The **full** NetArchTest suite (197 tests as of authoring) — no inclusion filter, so a renamed/new test can never silently drop out of the gate |
| `changed-surface-smoke` | `Spe.Integration.Tests` filtered to namespaces matching the changed BFF surface; no-op if nothing matches |
| `auth-smoke` | Runs only when `Infrastructure/Auth/**`, `Infrastructure/Graph/**`, claims, or an `*Authorization*`/`*AuthFilter*` file changed |
| `eval-gate` | Golden-utterance eval suite (`Category=GoldenUtteranceEval`) |
| `tenant-isolation` | The I1–I5 tenant-isolation invariant tests (`Spaarke.ArchTests.TenantIsolation.*`) |
| `compose-fidelity-gate` | Corpus round-trip harness over every `.docx` under `tests/fixtures/compose-corpus/` (requires Git-LFS checkout) |

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

#### `provisioning-prereqs-validate.yml`

**Triggers**: `pull_request`, `push` → `master`, `merge_group`

Validates `scripts/provisioning-prereqs/prereqs.yaml` (top-level shape, per-prereq required fields, scope enum, unique ids, the SPE `never_delete` guard on `PRQ-T-01`) using the **same** `powershell-yaml` parser the `/provision-environment` skill uses, plus validates `intake.schema.json` as a Draft 2020-12 JSON Schema via `ajv-cli` + `ajv-formats` (needed for the `format: uuid` checks on `tenantId`/`subscriptionId`). Advisory; a follow-on task is filed to route it through the router's `classify` job.

#### `css-reset-gate.yml` — CSS Reset Gate

**Triggers**: `pull_request` / `push` → `master`, paths: `src/solutions/**/index.html`, `src/client/code-pages/**/index.html`, `scripts/check-html-css-reset.mjs`, or the workflow file itself

One job runs `node scripts/check-html-css-reset.mjs --all`, verifying every Code Page host `index.html` carries the universal box-sizing reset (prevents the DataGrid +24px-overflow regression class).

#### `office-addins-tests.yml` — Office Add-ins Tests (Gate)

**Triggers**: `pull_request` / `push` → `master`, scoped to `src/client/office-addins/**`, `src/client/shared/Spaarke.Auth/**`, one `provenance.ts` leaf file, several `src/server/api/Sprk.Bff.Api/{Api,Services,Workers}/Office/**` + `Services/Documents/**` server paths, and the corresponding test directories; also `workflow_dispatch`

| Job | Purpose |
|-----|---------|
| `gated-jest` | Runs an explicit, ratchet-only list of jest suites (`ci-gated-suites.txt`) — 56 of the package's 56 suites as of 2026-09-28; fails if a listed suite is missing or if jest runs a different count than the manifest declares (no vacuous green) |
| `typecheck` | Production-only TypeScript typecheck — fails on any production `error TS`; a pinned, equality-checked count of accepted test-file debt (68 lines as of the last re-baseline) must also match exactly |
| `server-tests` | A namespace-prefix-filtered slice of `Sprk.Bff.Api.Tests` (office/AI/documents/domain/seam/filters/auth namespaces) with a floor assertion (`MIN_SELECTED_TESTS`) so a renamed namespace can't silently shrink coverage |
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

**Triggers**: `workflow_dispatch` only (`target_environment`, optional `artifact_run_id`, `skip_smoke_tests`). The automatic `workflow_run` trigger (dev auto-promote after "SDAP CI" on master) was **removed** — some `if:` conditions inside the `plan` job still reference `workflow_run` defensively, but no such trigger exists in `on:` today.

**Pipeline**: direct-target deploy (NOT a dev→staging→prod chain — each target environment deploys independently, per the 2026-06-02 D-12 refactor) to `dev` / `staging` / `production`, each with `/ping` + `/healthz` smoke tests; `production` additionally requires `environment: production` reviewer approval. **Secrets**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC) + `DEV_APP_NAME`/`STAGING_APP_NAME`/`PROD_APP_NAME`.

#### `deploy-spaarke-ai.yml` — Deploy SpaarkeAi

**Triggers**: `push` → `master` (paths: `src/solutions/SpaarkeAi/**` + several `@spaarke/*` shared-lib paths + `src/solutions/LegalWorkspace/**`), `workflow_dispatch` (`environment`: dev/production)

**Pipeline**: `build` (vite single-file bundle; smoke-tests the bundle for `<!DOCTYPE html>`, an inlined `<script>`, the root mount element, and **no** external script/css URLs per ADR-026) → `deploy-dev` (automatic on `push`, or dispatch with `environment=dev`) → `deploy-production` (manual dispatch only, `environment: production` reviewer approval). Deploys the bundle as the `sprk_spaarkeai` Dataverse web resource via `Deploy-SpaarkeAi.ps1`; there is no slot-swap mechanism, so rollback is re-dispatching against a prior commit SHA. **Secrets**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC); production additionally needs `SPAARKE_DATAVERSE_PROD_URL`.

#### `deploy-teams-app.yml` — Deploy Teams App Package

**Triggers**: `workflow_dispatch` only

`build_and_package` builds the `external-spa` Teams-tab surface as a fail-fast sanity gate (the manifest's `contentUrl` points at that already-deployed bundle), validates `appPackage/manifest.json` + icon assets, zips them into `appPackage.zip`, and uploads it as a build artifact; `deploy` republishes it as the longer-retention `teams-app-package` artifact. "Deploy" here means publishing the artifact — org-catalog submission is a manual tenant-admin action; no programmatic deploy endpoint exists for a Teams org-catalog app.

#### `deploy-external-spa.yml` — Deploy External SPA (Static Web App)

**Triggers**: `workflow_dispatch` only (manual-only until a later phase adds a push/path trigger)

One job builds the external Secure Project Workspace SPA (Vite production build with hardcoded non-secret CIAM/MSAL identifiers as build-time env vars), stages `staticwebapp.config.json` into the build output, and deploys to Azure Static Web Apps. **Secrets**: `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV`.

### Provisioning Artifact Publishers

#### `publish-provisioning-arm-artifacts.yml` — Publish Provisioning ARM Artifacts

**Triggers**: `push` → `master` (paths: `infrastructure/bicep/customer.bicep`, `infrastructure/bicep/modules/**`, `scripts/canonical-secret-catalog/generated/**`), `workflow_dispatch`

Installs the `az bicep` CLI, compiles `customer.bicep` to flattened ARM JSON (it composes only local-relative `module` references, so a single ARM JSON file results), computes its SHA-256 + size, and uploads it plus a versioned-and-`-latest` manifest pair to the `provisioning-artifacts` blob container for the H2a Azure-stamp deploy handler. **Secrets/vars**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC), repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT`.

#### `publish-dataverse-solutions-manifest.yml` — Publish Dataverse Solutions Manifest

**Triggers**: `workflow_dispatch` only (release-time; deliberately **no** `push` trigger — a push runner has never built the solution ZIPs, so an earlier `push: master` trigger failed on every solution change)

Locates all 8 canonical managed-solution ZIPs under `src/solutions/<Folder>/{bin/Release,bin/Debug,.,out}/*.zip` (fails if any of the 8 is missing — it refuses to publish a partial manifest), reads each ZIP's `solution.xml` version, uploads the ZIPs, and publishes `dataverse-solutions-latest.json` (the exact blob name `SolutionArtifactManifestOptions` resolves) plus a versioned copy. **Secrets/vars**: `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC), repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT`. This workflow does **not** build/pack the ZIPs — that is a separate, larger follow-on item for 6 of the 8 solutions (Vite/React Code Page source trees with no unpacked-solution scaffolding yet).

### Legacy Pipeline (superseded, pending deletion)

#### `sdap-ci.yml` — SDAP CI

**Triggers**: `pull_request` / `push` → `master`, both with `paths-ignore` (`docs/**`, `projects/_backlog/**`, `projects/x-*/**`, `**.md`, `.claude/**`, `memory/**`, `knowledge/**`, `CHANGELOG*`, `CODEOWNERS`, `LICENSE*`)

The original monolithic pipeline: `security-scan` (Trivy fs, advisory), `build-test` (two-pass Debug build+test with coverage, no longer `continue-on-error` at the job level), `eval-gate` (golden-utterance, blocking), `client-quality` (Prettier/ESLint/tsc across the shared-lib chain, advisory), `code-quality` (needs `build-test`; `dotnet format`, the full NetArchTest suite, naming-conformance, plugin size, dependency audit — all advisory via `continue-on-error`), `tenant-isolation` (I1–I5, blocking), `integration-readiness` (publish + package artifact, advisory), `adr-pr-comment` (PR comment), `summary`, `compose-fidelity-gate` (blocking), and `compose-client-gate` (jest save-contract suite, advisory pending 3 green runs on `ubuntu-latest`). **It is no longer the branch's required status check** — `ci-router.yml` is — and multiple comments across the newer tier workflows note it is slated for deletion by another project's task 077, not yet executed as of 2026-09-28.

#### `sdap-ci-docs-only.yml` — SDAP CI - Docs-Only Fallback

**Triggers**: `pull_request` matching exactly `sdap-ci.yml`'s `paths-ignore` list

Emits no-op "Build & Test (Debug)"/"Build & Test (Release)" success checks for PRs that touch nothing `sdap-ci.yml` would build, so a docs-only PR is not blocked waiting for a check name that `sdap-ci.yml` never reports. Paired 1:1 with `sdap-ci.yml`'s own trigger paths — if that workflow is deleted, this one's purpose goes with it.

### Weekly ADR Audit

#### `adr-audit.yml` — ADR Architecture Audit

**Triggers**: `schedule` (`0 9 * * 1`, Monday 09:00 UTC), `workflow_dispatch`

Runs the full NetArchTest suite (`continue-on-error: true` on the test step itself) and parses the TRX into a per-ADR-grouped report, creating or updating a single open tracking issue titled "ADR Architecture Compliance Report - {date}" (labels `architecture`, `technical-debt`, `adr-audit`); auto-closes the issue when a run finds zero violations.

---

## Scheduled Workflows

Five workflows run on a `schedule:` trigger. There is no longer a single "nightly quality" or "weekly quality" workflow — those were deleted (see [`.claude/CHANGELOG.md`](../../.claude/CHANGELOG.md) history and the `github-actions-rationalization-r1` project) and their responsibilities now live across the workflows below.

| Workflow | Schedule | What it reports | Blocking |
|---|---|---|---|
| `nightly-health.yml` | Daily, 06:00 UTC | Flake hunt, bundle-size drift, vuln scan, full integration suite, coverage observation, Trivy fs scan, dependency audit, Graph app-role parity | No — rolling tracking issue |
| `client-tests.yml` | Nightly, 07:00 UTC | jest baseline across 40 client packages (pass/fail/install-failed table) | No — job summary + `client-test-baseline` artifact |
| `adr-audit.yml` | Weekly, Monday 09:00 UTC | Full ADR NetArchTest compliance | No — tracking issue (see [above](#weekly-adr-audit)) |
| `report-workflow-health.yml` | Weekly, Monday 09:00 UTC | Rolling 7-day per-workflow success rate across every `.github/workflows/*.yml` | No — tracking issue |
| `pcf-build-prod-nightly.yml` | Daily, 08:00 UTC | `npm run build:prod` for every PCF control, judged by the build output (pcf-scripts exits 0 on a failed build) | No — advisory; job summary + artifact |

`redis-key-rotation.yml` was removed on 2026-10-05 (task 242b): every Spaarke Redis is Azure Managed Redis with access keys disabled, so there is no key to rotate, and every scheduled run had failed.

### `nightly-health.yml`

| Job | Purpose | Timeout |
|-----|---------|---------|
| `flake-hunt` | Runs `tests/unit/Sprk.Bff.Api.Tests/` 3× (excluding `Category=Flaky`); flags any test that passed in some runs and failed in others | 60 min |
| `bundle-size` | Builds `SemanticSearch`, `CreateMatterWizard`, and `LegalWorkspace` client artifacts; flags >10% drift vs `.github/baseline-bundle-sizes.json` | 30 min |
| `vuln-scan` | `dotnet list package --vulnerable --include-transitive` + `npm audit` on 3 key client packages | 20 min |
| `full-integration` | Full, unfiltered `Spe.Integration.Tests` run (Tier 3 — never blocks a PR) | 45 min |
| `coverage-observation` | Aggregates Cobertura coverage across the whole suite — **observation only, never a gate** (ADR-038) | 30 min |
| `trivy-fs` | Full filesystem Trivy scan, `exit-code: 0` (never fails the job; counts are reported) | 20 min |
| `dep-audit` | Full transitive `dotnet`/`npm` vulnerability audit across every `package-lock.json` root | 20 min |
| `graph-app-role-parity` | Compares a customer UAMI service principal's Graph app roles against `GraphAppRoles.cs`; `continue-on-error: true`; **self-skips today** because the four `NIGHTLY_*` secrets it needs (`NIGHTLY_GRAPH_READER_CLIENT_ID`, `NIGHTLY_AZURE_TENANT_ID`, `NIGHTLY_SUBSCRIPTION_ID`, `NIGHTLY_UAMI_SP_OBJECT_ID`) have never been populated | n/a |
| `report` | Creates/updates a single rolling issue titled "Nightly Health — {date}" (closes it automatically on a clean night) | 10 min |

**Manual dispatch**: `gh workflow run nightly-health.yml -f update_baseline=true` (commits the current bundle sizes as the new baseline — only input it accepts).

### `client-tests.yml`

Discovers every `package.json` with a real `test` script (filters out the npm-init default), matrix-runs `npm test -- --ci` per package (`max-parallel: 8`, `continue-on-error: true` per leg), and writes a combined pass/fail/install-failed table to the job summary plus a `client-test-baseline` artifact. Deliberately **not** wired into `ci-tier1-blocking.yml`/`ci-tier2-advisory.yml` — see the file's own header for the promotion criteria. `office-addins-tests.yml`'s `gated-jest` job is the one promoted subset that *does* gate PRs today.

**Manual dispatch**: `gh workflow run client-tests.yml -f package_filter=<substring>` to scope to matching package paths.

### `report-workflow-health.yml`

Iterates every file in `.github/workflows/*.yml`, queries `gh run list --workflow=<file> --created=">7 days ago"`, computes a per-workflow success rate, and appends a weekly snapshot to a single rolling "CI Health Report" issue.

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
| `format` / `lint` (Tier 2) | `ci-tier2-advisory.yml` | Formatting/lint violation | `dotnet format whitespace`; `npx prettier --write "src/client/**/*.{ts,tsx}"`; `cd src/client/pcf && npx eslint . --fix` |
| `full-unit-tests` (Tier 2) | `ci-tier2-advisory.yml` | A unit test failed on both pass 1 and the pass-2 retry | Download the `tier2-unit-test-results-Debug-<shard>` artifact of the shard that failed |
| legacy `security-scan`/`build-test`/`client-quality`/`code-quality` | `sdap-ci.yml` | Same classes as above, legacy job names | These no longer gate the merge (superseded by `CI / Router`), but a real red is still worth fixing |

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

ADR violations appear in three places:

1. **Tier 2 PR Comment**: `ci-tier2-advisory.yml`'s `tier2-pr-comment` job posts one deduplicated "🛈 Tier 2 Advisory Report" comment per PR (advisory — never blocks)
2. **Legacy PR Comment**: `sdap-ci.yml`'s `adr-pr-comment` job still posts a separate "🏛️ ADR Architecture Validation Report" comment (legacy, same non-blocking status)
3. **Weekly Issue**: `adr-audit.yml` creates/updates a tracking issue

**To fix:**
```powershell
# Run local ADR check
/adr-check

# View the Tier 1 full ArchTests run
gh run view {run-id} --log --job="Arch Tests (full suite, blocking)"

# Download the Tier 2 (advisory) ADR results artifact
gh run download {run-id} --name tier2-adr-results

# Download the legacy sdap-ci.yml ADR results artifact
gh run download {run-id} --name adr-test-results
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

### CI Gate (`ci-router.yml` / Tier 1 / Tier 2 / legacy `sdap-ci.yml`)

No secrets required — these run read-only against the repo's own code.

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
| `deploy-external-spa.yml` | `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` |

### Provisioning Artifact Publishers

| Workflow | Secret / Variable |
|---|---|
| `build-provisioning-sidecar.yml` | `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC); repo variable `SIDECAR_ACR_LOGIN_SERVER` |
| `publish-provisioning-arm-artifacts.yml`, `publish-dataverse-solutions-manifest.yml` | `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` (OIDC); repo variable `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` |

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
