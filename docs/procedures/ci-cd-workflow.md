# CI/CD Workflow Guide

> **Purpose**: Developer guide for the complete CI/CD pipeline — from local commits through automated testing, pull request workflow, and deployment to staging/production.
>
> **Last Updated**: January 6, 2026

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
5. [GitHub Actions Workflows](#github-actions-workflows)
6. [Deployment Pipeline](#deployment-pipeline)
7. [Monitoring and Troubleshooting](#monitoring-and-troubleshooting)
8. [Quick Reference](#quick-reference)

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
│  ┌────────────┐   ┌────────────┐   ┌────────────┐                  │
│  │  Security  │   │   Build    │   │   Code     │                  │
│  │    Scan    │   │  & Test    │   │  Quality   │                  │
│  │  (Trivy)   │   │  (D+R)     │   │  (ADR)     │                  │
│  └────────────┘   └────────────┘   └────────────┘                  │
│        │                │                │                          │
│        └────────────────┼────────────────┘                          │
│                         ▼                                           │
│              ┌─────────────────────┐                                │
│              │    Integration      │                                │
│              │    Readiness        │                                │
│              │  (Package Build)    │                                │
│              └─────────────────────┘                                │
└─────────────────────────────────────────────────────────────────────┘
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
| Local Quality | Manual | ~30s | Yes (recommended) |
| CI Pipeline | Auto (push/PR) | ~5-8 min | Yes |
| Staging Deploy | Auto (master merge) | ~3-5 min | No |
| Production Deploy | Manual | ~5-10 min | N/A |

---

## Local Development Workflow

### Before Committing

Run local quality gates before every commit:

```powershell
# 1. Check for changes
git status

# 2. Run quality gates (or use /push-to-github which does this automatically)
# Option A: Individual commands
npm run lint                 # TypeScript lint
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

## GitHub Actions Workflows

### CI Pipeline: `sdap-ci.yml`

**Triggers**: Push to `main`/`master`, Pull requests

| Job | Purpose | Duration | Blocking |
|-----|---------|----------|----------|
| `security-scan` | Trivy vulnerability scan | ~1 min | Yes |
| `build-test` | Build + run tests (Debug & Release) | ~3-4 min | Yes |
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

code-quality:
  • dotnet format --verify-no-changes
  • ADR architecture tests (NetArchTest)
  • Plugin assembly size (<1MB per ADR-002)
  • Vulnerable package detection

integration-readiness:
  • dotnet publish (API)
  • Package deployment artifacts
  • 30-day retention
```

### Staging Deployment: `deploy-staging.yml`

**Triggers**: Auto after CI succeeds on master, Manual

| Job | Purpose |
|-----|---------|
| `deploy-api` | Deploy API to staging App Service |
| `deploy-plugins` | Deploy Dataverse plugins via PAC CLI |
| `integration-tests` | Run integration tests against staging |
| `notify` | Deployment summary |

### Production Deployment: `deploy-to-azure.yml`

**Triggers**: Manual only

| Job | Purpose |
|-----|---------|
| `deploy-infrastructure` | Deploy Bicep templates |
| `deploy-api` | Deploy API to production App Service |
| `smoke-test` | Verify `/ping` endpoint |
| `notify` | Deployment summary |

### ADR Audit: `adr-audit.yml`

**Triggers**: Weekly (Monday 9 AM UTC), Manual

| Action | Purpose |
|--------|---------|
| Run NetArchTest | Full ADR compliance scan |
| Create/Update Issue | Track violations with `architecture` label |
| Auto-close | When all violations resolved |

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
| `code-quality` format | Style violation | Run `dotnet format` |
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

This CI/CD workflow integrates with the [Testing and Code Quality Procedures](testing-and-code-quality.md):

| Stage | Local (task-execute) | CI (GitHub Actions) |
|-------|----------------------|---------------------|
| Code Review | Step 9.5: /code-review | build-test job |
| ADR Check | Step 9.5: /adr-check | code-quality job |
| Linting | Step 9.5: npm/dotnet | code-quality job |
| Tests | During implementation | build-test job |

**Key principle:** Run quality gates locally first (Step 9.5) to catch issues before pushing. CI runs the same checks to enforce standards.

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

- [Testing and Code Quality Procedures](testing-and-code-quality.md) - Quality gates and testing
- [Parallel Claude Code Sessions](parallel-claude-sessions.md) - Multi-session workflow
- [ci-cd Skill](../../.claude/skills/ci-cd/SKILL.md) - Full skill documentation
- [push-to-github Skill](../../.claude/skills/push-to-github/SKILL.md) - Git workflow skill
- [adr-check Skill](../../.claude/skills/adr-check/SKILL.md) - ADR validation

---

*Last updated: January 6, 2026*
