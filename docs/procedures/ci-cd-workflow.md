# CI/CD Workflow Guide

> **Purpose**: Developer guide for the complete CI/CD pipeline — from local commits through automated testing, pull request workflow, and deployment to staging/production.
>
> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current

---

## Overview

This guide explains the full CI/CD workflow for Spaarke development. The pipeline automates quality checks, testing, and deployment through GitHub Actions.

**Key Concepts**:
- **Local quality gates** run before commits (code-review, adr-check, lint)
- **CI pipeline** runs automatically on every push and PR
- **Staging deployment** triggers automatically after merge to master
- **Production deployment** requires manual trigger
- **PR process** ensures all checks pass before merge

---

## Table of Contents

1. [Pipeline Overview](#pipeline-overview)
2. [Local Development Workflow](#local-development-workflow)
3. [Commit Conventions](#commit-conventions)
4. [Pull Request Workflow](#pull-request-workflow)
5. [Pre-Merge Checklist](#pre-merge-checklist)
6. [GitHub Actions Workflows](#github-actions-workflows)
7. [Nightly Quality Workflow](#nightly-quality-workflow)
8. [Automated Code Review (AI)](#automated-code-review-ai)
9. [Weekly Quality Summary](#weekly-quality-summary)
10. [Deployment Pipeline](#deployment-pipeline)
11. [Monitoring and Troubleshooting](#monitoring-and-troubleshooting)
12. [Quick Reference](#quick-reference)

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
│              CI PIPELINE (sdap-ci.yml) - Automatic                  │
│  ┌────────────┐   ┌────────────┐   ┌────────────┐  ┌────────────┐ │
│  │  Security  │   │   Build    │   │   Code     │  │  Client    │ │
│  │    Scan    │   │  & Test    │   │  Quality   │  │  Quality   │ │
│  │  (Trivy)   │   │  (D+R)     │   │  (ADR)     │  │(Prettier/  │ │
│  │            │   │            │   │            │  │  ESLint)   │ │
│  └────────────┘   └────────────┘   └────────────┘  └────────────┘ │
│        │                │                │               │         │
│        └────────────────┼────────────────┼───────────────┘         │
│                         ▼                                          │
│              ┌─────────────────────┐                               │
│              │    Integration      │                               │
│              │    Readiness        │                               │
│              │  (Package Build)    │                               │
│              └─────────────────────┘                               │
└────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      PULL REQUEST                                    │
│                                                                      │
│  1. Create PR (draft or ready)                                      │
│  2. CI runs automatically                                           │
│  3. ADR violations posted as PR comments                            │
│  4. Review and address feedback                                     │
│  5. Request reviews when CI passes                                  │
│  6. Merge when approved                                             │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ (merge to master)
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│            STAGING DEPLOYMENT (deploy-staging.yml)                  │
│                     - Automatic -                                    │
│                                                                      │
│  1. Download artifacts from CI                                      │
│  2. Deploy API to staging App Service                               │
│  3. Deploy plugins via PAC CLI (if enabled)                         │
│  4. Run integration tests                                           │
│  5. Health check verification                                       │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ (manual trigger)
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│          PRODUCTION DEPLOYMENT (deploy-to-azure.yml)                │
│                     - Manual -                                       │
│                                                                      │
│  1. Deploy infrastructure (Bicep) - optional                        │
│  2. Deploy API to production App Service                            │
│  3. Smoke test verification                                         │
│  4. Notification                                                    │
└─────────────────────────────────────────────────────────────────────┘
```

### Pipeline Summary

| Stage | Trigger | Duration | Blocking |
|-------|---------|----------|----------|
| Pre-commit Hooks | Auto (git commit) | ~5-10s | Yes (local) |
| Local Quality | Manual | ~30s | Yes (recommended) |
| CI Pipeline (`sdap-ci.yml`) | Auto (push/PR) | ~5-8 min | Yes |
| Claude Code Review (`claude-code-review.yml`) | Auto (PR events) | ~3 min | No (advisory) |
| Nightly Quality (`nightly-quality.yml`) | Scheduled (weeknights 6 AM UTC) | ~10-15 min | No (advisory) |
| Weekly Summary (`weekly-quality.yml`) | Scheduled (Friday 10 PM UTC) | ~10 min | No (advisory) |
| ADR Audit (`adr-audit.yml`) | Scheduled (Monday 9 AM UTC) | ~5 min | No (advisory) |
| BFF API Deploy (`deploy-bff-api.yml`) | Auto (master push + API changes) or manual | ~5-10 min | N/A |
| Environment Promotion (`deploy-promote.yml`) | Auto (after CI on master) or manual | ~10-15 min | N/A |
| Slot Swap (`deploy-slot-swap.yml`) | Auto (after CI on master) or manual | ~10-15 min | N/A |
| Bicep Infrastructure (`deploy-infrastructure.yml`) | PR/push with Bicep changes or manual | ~5-10 min | N/A |
| Platform Infrastructure (`deploy-platform.yml`) | Manual only | ~5-10 min | N/A |
| Office Add-ins (`deploy-office-addins.yml`) | Auto (master push + add-in changes) or manual | ~2-3 min | N/A |
| Customer Provisioning (`provision-customer.yml`) | Manual only | ~5-15 min | N/A |

### Complete Workflow Inventory

| File | Name | Purpose |
|------|------|---------|
| `sdap-ci.yml` | SDAP CI | Build, test, quality, security scan |
| `claude-code-review.yml` | Claude Code Architecture Review | AI-powered advisory PR review |
| `nightly-quality.yml` | Nightly Quality | Deep analysis: coverage, SonarCloud, AI review, dependency audit |
| `weekly-quality.yml` | Weekly Quality Summary | Aggregate weekly trends from nightly runs |
| `adr-audit.yml` | ADR Architecture Audit | Full ADR compliance scan with tracking issue |
| `deploy-bff-api.yml` | Deploy BFF API | Staging slot swap deployment with auto-rollback |
| `deploy-infrastructure.yml` | Deploy Bicep Infrastructure | Validate, what-if, deploy Bicep templates |
| `deploy-platform.yml` | Deploy Platform Infrastructure | Shared platform resources via `Deploy-Platform.ps1` |
| `deploy-promote.yml` | Environment Promotion | Cascade deployment: dev -> staging -> prod |
| `deploy-slot-swap.yml` | Deploy via Slot Swap | Zero-downtime slot swap deployment |
| `deploy-office-addins.yml` | Deploy Office Add-ins | Build and deploy to Azure Static Web App |
| `provision-customer.yml` | Provision Customer | End-to-end customer environment provisioning |
| `auto-add-to-project.yml` | Auto-add to Project | Add issues to Spaarke Core project board |

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

See [Testing and Code Quality Procedures](testing-and-code-quality.md#husky--lint-staged-pre-commit-hooks) for detailed Husky documentation.

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
- `master` - Requires PR, CI passing, approval
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
| `plugin` | Dataverse plugin changes |
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

9. MERGE (When approved + CI green)
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
| CI passing | `gh pr checks` shows all green |
| Reviews approved | At least 1 approval |
| No conflicts | Branch is up-to-date with master |
| ADR compliant | No violations in PR comments |

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

- [ ] **CI pipeline green** — `gh pr checks` shows all jobs passing
- [ ] **Security scan** — No new critical/high vulnerabilities from Trivy
- [ ] **Build succeeds** — Both Debug and Release configurations compile without warnings (`-warnaserror`)
- [ ] **Tests pass** — All unit tests pass with coverage collected
- [ ] **Client quality** — Prettier format check and ESLint strict (`--max-warnings 0`) pass
- [ ] **Code quality** — `dotnet format --verify-no-changes` passes; ADR architecture tests pass; plugin size under 1MB

### Manual Checks (Reviewer Responsibility)

- [ ] **ADR compliance** — Review any ADR violation comments posted by `adr-pr-comment` job or Claude Code review; address critical findings
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
- [ ] Parameter files exist for all target environments
- [ ] What-if preview reviewed (posted as PR comment by `deploy-infrastructure.yml`)

---

## GitHub Actions Workflows

### CI Pipeline: `sdap-ci.yml`

**Triggers**: Push to `main`/`master`, Pull requests

| Job | Purpose | Duration | Blocking |
|-----|---------|----------|----------|
| `security-scan` | Trivy vulnerability scan | ~1 min | Yes |
| `build-test` | Build + run tests (Debug & Release) | ~3-4 min | Yes |
| `client-quality` | Prettier format check + ESLint strict | ~1-2 min | Yes |
| `code-quality` | Format check, ADR tests, plugin size | ~2 min | Yes |
| `integration-readiness` | Package artifacts | ~1 min | Yes |
| `adr-pr-comment` | Post ADR violations to PR | ~30s | No |
| `summary` | Generate summary report | ~10s | No |

**What Each Job Checks:**

```
security-scan:
  • Trivy filesystem scan
  • Uploads results to GitHub Security tab

build-test:
  • dotnet restore
  • dotnet build (Debug + Release)
  • dotnet test with coverage
  • Upload test results artifact

client-quality:
  • npm ci (root) → Prettier check on src/client/**/*.{ts,tsx}
  • npm ci (src/client/pcf/) → ESLint strict (--max-warnings 0)
  • Runs on ubuntu-latest (parallel with build-test)

code-quality:
  • dotnet format --verify-no-changes
  • ADR architecture tests (NetArchTest) — blocking
  • ADR policy check (Legacy PowerShell) — advisory (continue-on-error)
  • Plugin assembly size (<1MB per ADR-002)
  • Vulnerable package detection

integration-readiness:
  • dotnet publish (API)
  • Package deployment artifacts
  • 30-day retention
  • Depends on: security-scan, build-test, code-quality, client-quality
```

### BFF API Deployment: `deploy-bff-api.yml`

**Triggers**: Push to `master` when `src/server/api/**` changes, Manual dispatch

**Pipeline**: build -> test -> deploy staging slot -> verify staging -> swap to production -> verify production -> (rollback on failure)

| Job | Purpose |
|-----|---------|
| `build` | Build and publish API (`dotnet publish`) |
| `test` | Run unit tests (`tests/unit/Sprk.Bff.Api.Tests/`); skippable for emergency deploys |
| `deploy-staging` | Deploy zip to staging slot (`spaarke-bff-prod-staging`) |
| `verify-staging` | Health check `/healthz` + smoke test `/ping` on staging slot |
| `swap-production` | Swap staging -> production (requires `production` environment approval) |
| `verify-production` | Health check `/healthz` + smoke test `/ping` on production (`https://api.spaarke.com`) |
| `rollback` | Auto swap-back if production health check fails |
| `summary` | Deployment summary with job results |

**Key features**: Zero-downtime via staging slot swap (NFR-02). Automatic rollback on production health check failure (NFR-06). Concurrency group prevents overlapping deploys.

### Bicep Infrastructure Deployment: `deploy-infrastructure.yml`

**Triggers**: PR or push to `master` when `infrastructure/bicep/**` changes, Manual dispatch

| Job | Purpose |
|-----|---------|
| `validate` | Lint and build all Bicep templates, verify parameter files |
| `what-if` | What-if preview (PR: posted as comment; Manual: step summary) |
| `deploy` | Deploy infrastructure (manual dispatch only with `deploy: true`) |
| `summary` | Pipeline summary |

**Supports**: Model 1 (shared multi-tenant) and Model 2 (customer-dedicated) stacks. PR triggers only validate (no deployment).

### Platform Infrastructure Deployment: `deploy-platform.yml`

**Triggers**: Manual dispatch only

| Job | Purpose |
|-----|---------|
| `what-if` | What-if preview via `Deploy-Platform.ps1 -WhatIf` |
| `deploy` | Deploy platform infrastructure (requires environment approval) |
| `verify` | Verify resource group, App Service health, Key Vault access |

### Environment Promotion: `deploy-promote.yml`

**Triggers**: Automatic after successful SDAP CI on master, Manual dispatch

**Pipeline**: dev -> staging -> prod (with manual approval gate for production)

| Job | Purpose |
|-----|---------|
| `plan` | Determine promotion path and artifact source |
| `deploy-dev` | Deploy to dev + smoke tests (`/ping`, `/healthz`) |
| `deploy-staging` | Deploy to staging + smoke tests + CORS integration check |
| `deploy-prod` | Deploy to production (requires `prod` environment reviewer approval) |
| `summary` | Promotion summary with path and per-environment results |

**Key features**: Automatic trigger after CI; cascading deployment (dev must pass before staging); production gate via GitHub environment protection rules.

### Slot Swap Deployment: `deploy-slot-swap.yml`

**Triggers**: Automatic after successful SDAP CI on master, Manual dispatch

| Job | Purpose |
|-----|---------|
| `build` | Build, test, and publish API |
| `resolve-config` | Map environment to App Service name, resource group, URLs |
| `deploy-to-slot` | Deploy to staging slot + warm-up wait |
| `health-check` | `/healthz` and `/ping` checks on staging slot |
| `swap-to-production` | Swap staging -> production (requires `production` environment approval) |
| `summary` | Pipeline summary with all job statuses |

### Office Add-ins Deployment: `deploy-office-addins.yml`

**Triggers**: Push to `master` or `work/SDAP-outlook-office-add-in` when `src/client/office-addins/**` changes, Manual dispatch

| Job | Purpose |
|-----|---------|
| `build_and_deploy` | Build webpack bundle + deploy to Azure Static Web App |

### Customer Provisioning: `provision-customer.yml`

**Triggers**: Manual dispatch only

| Job | Purpose |
|-----|---------|
| `validate-inputs` | Validate customer ID format (lowercase alphanumeric, 3-10 chars) |
| `provision` | Run `Provision-Customer.ps1` with Azure OIDC auth; supports dry run |
| `verify` | Post-provisioning verification via `Test-Deployment.ps1` |
| `summary` | Provisioning summary with resource names |

**Key features**: Requires `production` environment approval. Logs uploaded as artifacts (90-day retention) for audit trail.

### Claude Code Architecture Review: `claude-code-review.yml`

**Triggers**: Pull request events (opened, synchronize, reopened)

| Job | Purpose |
|-----|---------|
| `architecture-review` | Advisory architecture review using Claude Code Action (Sonnet 4.5); checks ADR compliance |

**Advisory only**: Never blocks PR merges (`continue-on-error: true`). Skips draft PRs and Dependabot PRs.

### Auto-Add to Project: `auto-add-to-project.yml`

**Triggers**: Issues opened or reopened

Automatically adds new issues to the Spaarke Core GitHub project board.

### ADR Audit: `adr-audit.yml`

**Triggers**: Weekly (Monday 9 AM UTC), Manual

| Action | Purpose |
|--------|---------|
| Run NetArchTest | Full ADR compliance scan |
| Create/Update Issue | Track violations with `architecture` and `adr-audit` labels |
| Auto-close | When all violations resolved |

---

## Nightly Quality Workflow

The nightly quality pipeline (`nightly-quality.yml`) runs a comprehensive quality sweep every weeknight to catch issues that are too expensive to run on every PR.

**Trigger**: Weeknights (Mon-Fri), 6 AM UTC / midnight MST. Also supports manual dispatch via `workflow_dispatch`.

### Jobs

| Job | Purpose | Timeout | Dependencies |
|-----|---------|---------|--------------|
| `test-and-coverage` | Full test suite with Coverlet coverage collection | 10 min | None |
| `sonarcloud-analysis` | SonarCloud deep analysis with coverage data | 8 min | test-and-coverage |
| `ai-code-review` | Claude Code headless review using `scripts/quality/nightly-review-prompt.md` | 5 min | None (parallel) |
| `dependency-audit` | `dotnet list --vulnerable` + `npm audit` across all client projects | 5 min | None (parallel) |
| `report-results` | Aggregates findings into a rolling GitHub issue (label: `nightly-quality`) | 5 min | All four jobs |

### Job Dependency Graph

```
test-and-coverage ──────┐
                        ├──→ sonarcloud-analysis
ai-code-review ─────────┤
                        ├──→ report-results (always, needs all 4)
dependency-audit ───────┘
```

### What Happens When It Fails

1. The `report-results` job creates or updates a rolling GitHub issue titled "Nightly Quality Report -- {date}" with the `nightly-quality` label.
2. The issue contains a job summary table showing pass/fail/skip status for each job.
3. Dependency audit results and AI review findings are included in the issue body.
4. A findings hash is computed to detect when content changes between runs.

### How to Interpret the Nightly Issue

| Section | What to Look For |
|---------|-----------------|
| Job Summary | Any "failed" status indicates a problem that needs attention |
| Dependency Audit | Lists vulnerable .NET and npm packages with severity |
| AI Code Review | Architecture and code quality observations from Claude Code |

### Manual Dispatch

You can run the nightly workflow on-demand with optional job toggles:

```bash
# Run all jobs
gh workflow run nightly-quality.yml

# Run without SonarCloud
gh workflow run nightly-quality.yml -f run_sonarcloud=false

# Run without AI review
gh workflow run nightly-quality.yml -f run_ai_review=false
```

### Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| SonarCloud job fails with "No coverage data" | Coverage artifacts not uploaded by test-and-coverage | Check test-and-coverage logs for test failures; coverage upload uses `continue-on-error: true` |
| AI Code Review shows "skipped" | `scripts/quality/nightly-review-prompt.md` file missing | Create the prompt file (task 021) or check it was not accidentally deleted |
| Report issue not created | `GITHUB_TOKEN` lacks `issues: write` permission | Verify workflow `permissions` block includes `issues: write` |
| Dependency audit shows false positives | Transitive dependency vulnerability | Evaluate with `dotnet list package --vulnerable --include-transitive`; if no direct fix, document in a tracking issue |

---

## Automated Code Review (AI)

Two AI-powered review tools provide automated code review feedback on pull requests.

### CodeRabbit

CodeRabbit is an AI code review tool that reviews every PR automatically.

**What it does**: Provides line-by-line code review comments with architecture-aware feedback based on Spaarke ADR constraints.

**When it runs**: On every PR targeting `master` or `feature/**` branches (configured in `.coderabbit.yaml`).

**Configuration**: `.coderabbit.yaml` at repository root. Key settings:
- Auto-review enabled for non-draft PRs
- Custom review instructions reference Spaarke ADRs (001, 008, 010, 021, 022)
- Path-specific instructions for `src/client/pcf/`, `src/client/code-pages/`, `src/server/api/`, and `scripts/`
- Advisory profile ("assertive" but non-blocking)
- Auto-resolve disabled -- human manages conversations

**Advisory status**: CodeRabbit reviews are **informational only**. They do not block PR merges. Review comments for useful insights, but use your judgment on whether to address them.

**Per-PR configuration**: You can adjust CodeRabbit behavior on a specific PR by adding a comment:
```
@coderabbitai ignore this PR
@coderabbitai review this PR
@coderabbitai configuration
```

### Claude Code Action

Claude Code Action provides AI review via GitHub Actions on PRs.

**What it does**: Reviews PR changes using Claude Code in headless mode, focusing on architecture patterns, security, and performance.

**When it runs**: Triggered on PR events (configured in the CI workflow or as a separate action).

**Advisory status**: Claude Code Action reviews are **advisory only**. Comments are posted to the PR but do not block merges.

**How to interpret PR comments**: AI review comments appear as bot comments on the PR. They highlight potential issues but may include false positives. Treat them as a second pair of eyes -- address critical findings, evaluate warnings, and ignore suggestions that don't apply.

### Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| CodeRabbit not reviewing | PR is a draft, or target branch not in `base_branches` | Mark PR as ready, or check `.coderabbit.yaml` `base_branches` list |
| CodeRabbit review is too noisy | Reviewing generated/vendor files | Add paths to CodeRabbit ignore patterns in `.coderabbit.yaml` |
| Claude Code Action comment is a false positive | AI misinterpreted code pattern | Dismiss the comment; no action required for advisory reviews |

---

## Weekly Quality Summary

A weekly summary workflow aggregates quality trends and posts them as a GitHub issue.

**When it runs**: Weekly (Monday mornings, 8 AM UTC).

**What it shows**:
- Trend table comparing nightly quality results over the past week
- Test pass/fail rates
- SonarCloud quality gate status
- Dependency vulnerability counts
- Open nightly-quality issues

**How to read the trend table**: Look for degradation patterns -- if test failures or vulnerability counts are trending upward, prioritize remediation. Consistent green results indicate stable code quality.

**Trigger manually**:
```bash
gh workflow run weekly-quality-summary.yml
```

---

## Deployment Pipeline

### Staging Deployment (Automatic)

After merging to master:

```
1. CI Pipeline runs on master
2. If successful, deploy-staging.yml triggers
3. API deployed to staging App Service
4. Plugins deployed (if enabled)
5. Integration tests run
6. Health check verifies deployment
```

**Monitor staging deployment:**
```powershell
# View deployment status
gh run list --workflow=deploy-staging.yml

# Watch live
gh run watch

# Check staging health
curl https://{staging-app}.azurewebsites.net/ping
```

### Production Deployment (Manual)

**Trigger production deployment:**
```powershell
# Verify staging is healthy first
curl https://{staging-app}.azurewebsites.net/ping

# Trigger production deployment
gh workflow run deploy-to-azure.yml

# Monitor
gh run watch
```

**When to deploy to production:**
- Staging verified healthy
- Feature testing complete
- Stakeholder approval (if required)

### Deployment Rollback

If issues occur after deployment:

```powershell
# View recent deployments
gh run list --workflow=deploy-to-azure.yml --limit 10

# Re-run previous successful deployment
gh run rerun {previous-run-id}

# Or deploy specific artifact
gh workflow run deploy-to-azure.yml -f artifact_id={id}
```

---

## Monitoring and Troubleshooting

### Common CI Failures

| Failure | Cause | Fix |
|---------|-------|-----|
| `security-scan` | Vulnerable dependency | Update package or add to allowlist |
| `build-test` compile | Code error | Fix locally, push update |
| `build-test` test | Test failure | Fix test or code |
| `client-quality` Prettier | TypeScript formatting violation | Run `npx prettier --write "src/client/**/*.{ts,tsx}"` |
| `client-quality` ESLint | ESLint rule violation | Run `cd src/client/pcf && npx eslint . --fix` |
| `code-quality` format | C# style violation | Run `dotnet format` |
| `code-quality` ADR | Architecture violation | Run `/adr-check`, fix violations |
| `code-quality` size | Plugin >1MB | Reduce dependencies per ADR-002 |

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

ADR violations appear in two places:

1. **PR Comments**: The `adr-pr-comment` job posts violations directly to the PR
2. **Weekly Issue**: `adr-audit.yml` creates a tracking issue

**To fix:**
```powershell
# Run local ADR check
/adr-check

# View CI ADR results
gh run view {run-id} --log --job=code-quality

# Download test results
gh run download {run-id} --name adr-test-results
```

### Health Check Endpoints

| Environment | Endpoint |
|-------------|----------|
| Staging | `https://{staging-app}.azurewebsites.net/ping` |
| Production | `https://{prod-app}.azurewebsites.net/ping` |
| Health | `/healthz` |
| Ready | `/ready` |

Expected responses:
```json
// /ping
{ "service": "Spe.Bff.Api", "status": "ok", "timestamp": "..." }

// /healthz
"Healthy"
```

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
gh workflow run deploy-to-azure.yml      # Trigger prod deploy
gh workflow run adr-audit.yml            # Trigger ADR audit

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

| Stage | Local | CI (sdap-ci.yml) | Nightly |
|-------|-------|-------------------|---------|
| Prettier | Pre-commit hook | client-quality job | — |
| ESLint | Pre-commit hook | client-quality job | — |
| dotnet format | Pre-commit hook | code-quality job | — |
| Tests | During implementation | build-test job | test-and-coverage |
| ADR Tests | Step 9.5: /adr-check | code-quality job | — |
| SonarCloud | — | — | sonarcloud-analysis |
| AI Review | Step 9.5: /code-review | — | ai-code-review |
| Dependency Audit | — | code-quality job | dependency-audit |

**Key principle:** Quality checks run at three levels -- pre-commit hooks catch formatting instantly, CI enforces on every PR, and nightly sweeps provide deep analysis. Run quality gates locally first (Step 9.5) to catch issues before pushing.

---

## Required Secrets

### CI Pipeline
No secrets required - runs in read-only mode.

### Staging Deployment

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Azure OIDC app ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_TENANT_ID` | Azure AD tenant |
| `STAGING_APP_NAME` | App Service name |
| `POWER_PLATFORM_URL` | Dataverse environment URL |
| `POWER_PLATFORM_CLIENT_ID` | Dataverse app ID |
| `POWER_PLATFORM_CLIENT_SECRET` | Dataverse app secret |

### Production Deployment

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Azure OIDC app ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_TENANT_ID` | Azure AD tenant |
| `AZURE_RESOURCE_GROUP` | Target resource group |
| `AZURE_APP_SERVICE_NAME` | App Service name |

---

## Related Documentation

- [Testing and Code Quality Procedures](testing-and-code-quality.md) - Quality gates, tool details (Prettier, ESLint, Husky, PSScriptAnalyzer, SonarCloud)
- [Parallel Claude Code Sessions](parallel-claude-sessions.md) - Multi-session workflow
- [ci-cd Skill](../../.claude/skills/ci-cd/SKILL.md) - Full skill documentation
- [push-to-github Skill](../../.claude/skills/push-to-github/SKILL.md) - Git workflow skill
- [adr-check Skill](../../.claude/skills/adr-check/SKILL.md) - ADR validation

---

*Last updated: April 5, 2026*
