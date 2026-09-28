---
description: Document and interact with GitHub Actions CI/CD workflows — checking build status, understanding pipeline stages, and integrating with deployment workflows
tags: [ci-cd, github-actions, deployment, workflows, automation]
techStack: [github-actions, dotnet, azure, dataverse]
appliesTo: [".github/workflows/", "ci-cd", "build status", "pipeline"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-09-25
---

# CI/CD Pipeline Skill

> **Category**: Operations
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: Workflows evolve quarterly; no single canonical pipeline holds steady. Inventory + status checks live in `notes/inventory/workflows.md` (Phase 0 task 003).
> **Inventory anomaly #1 RESOLVED**: This skill had zero frontmatter before 2026-05-16. Frontmatter block now in place.

---

## Purpose

Document and interact with GitHub Actions CI/CD workflows. Provides guidance on checking build status, understanding pipeline stages, and integrating with deployment workflows.

---

## Key Terminology

Understanding the distinction between CI, deployment, and sync:

| Term | What It Means | Workflow |
|------|---------------|----------|
| **CI (Continuous Integration)** | Build, test, code quality checks | `ci-router.yml` → `ci-tier1-blocking.yml` (required check **`Router`**) + `ci-tier2-advisory.yml`; legacy `sdap-ci.yml` runs in shadow until cutover (ci-cd-unit-test-remediation-r1) |
| **Deployment** | Deploy a component to an environment | Per-component `deploy-*.yml` (see [Deployment Workflows](#deployment-workflows)) — mostly `workflow_dispatch` / operator-driven |
| **Merge to Master** | Push changes to origin/master | Git operation (not a workflow) |
| **Sync Main Repo** | Pull origin/master to local main repo | Git operation (needed for worktrees) |

### Important Distinctions

1. **CI ≠ Deployment**: CI validates code quality. There is **no staging environment and no automatic "deploy after CI" chain**. (`deploy-staging.yml` / `deploy-to-azure.yml` were documented here previously; neither exists — removed from this skill 2026-09-25.)

2. **"Merge to master" updates origin/master** but does NOT:
   - Deploy the BFF — BFF deploys are **operator-driven** (`/bff-deploy`, or `deploy-bff-api.yml` via `workflow_dispatch`); CI never auto-deploys the BFF on merge
   - Sync the main repo's local master (must be done explicitly when using worktrees)

3. **A few deploy workflows are path-triggered on push to master** (`deploy-spaarke-ai.yml`, `deploy-office-addins.yml`, `deploy-infrastructure.yml`) — they can fail independently of CI.

---

## Applies When

- Checking CI status after pushing code
- Waiting for build/test results before merging
- Troubleshooting failed workflows
- Understanding deployment pipeline
- **Trigger phrases**: "check CI", "build status", "workflow failed", "deployment status", "CI/CD"

---

## Quick Reference

### Check CI Status

```powershell
# Check all checks for current PR
gh pr checks

# Check specific PR
gh pr checks 123

# View workflow run details
gh run view

# List recent workflow runs
gh run list --limit 5

# Watch a running workflow
gh run watch
```

### Common Actions

| Action | Command |
|--------|---------|
| View PR checks | `gh pr checks` |
| View run details | `gh run view {run-id}` |
| Download artifacts | `gh run download {run-id}` |
| Re-run failed jobs | `gh run rerun {run-id} --failed` |
| Cancel a run | `gh run cancel {run-id}` |
| Trigger workflow manually | `gh workflow run {workflow-name}` |

---

## GitHub Workflows Overview

### Primary CI Pipeline: `sdap-ci.yml`

**Triggers**: Push to `main`/`master`, Pull requests

| Job | Purpose | Blocking? |
|-----|---------|-----------|
| `security-scan` | Trivy vulnerability scanner | Yes |
| `build-test` | Build + test (Debug & Release) | Yes |
| `code-quality` | Format check, ADR tests, dependencies | Yes |
| `integration-readiness` | Package artifacts for deployment | Yes |
| `adr-pr-comment` | Post ADR violations to PR (non-blocking) | No |
| `summary` | Pipeline summary report | No |

**Key Checks**:
- Trivy security scan uploads to GitHub Security tab
- `dotnet format --verify-no-changes` for code style
- NetArchTest ADR validation (`Spaarke.ArchTests`)
- ADR-002 zero-plugin guard (`ADR002_PluginTests` — no plugin code/projects anywhere in `src/`)
- Vulnerable package detection

### Deployment Workflows

Verified against `.github/workflows/` on 2026-09-25. There is no staging environment and no plugin deployment (Spaarke ships no Dataverse plugins — ADR-002).

| Workflow | Deploys | Trigger |
|----------|---------|---------|
| `deploy-bff-api.yml` | BFF API (App Service) | `workflow_dispatch` only — BFF deploys are operator-driven; prefer the `/bff-deploy` skill |
| `deploy-infrastructure.yml` | Bicep infrastructure | PR (what-if) + push to master on `infrastructure/bicep/**` |
| `deploy-spaarke-ai.yml` | SpaarkeAi code page | Push to master on `src/solutions/SpaarkeAi/**` / shared UI lib |
| `deploy-office-addins.yml` | Office add-ins (Static Web App) | Push to master on `src/client/office-addins/**` |
| `deploy-external-spa.yml` | External SPA (Static Web App) | `workflow_dispatch` |
| `deploy-teams-app.yml` | Teams app package | `workflow_dispatch` |
| `deploy-promote.yml` | Environment promotion | `workflow_dispatch` (target environment input) |

Read each workflow's `on:` block before relying on a trigger — they change.

### ADR Compliance Audit: `adr-audit.yml`

**Triggers**: Manual, Weekly (Monday 9 AM UTC)

| Job | Purpose |
|-----|---------|
| `audit` | Run NetArchTest ADR validations |
| | Parse results and create/update tracking issue |

**Behavior**:
- Creates GitHub issue with `architecture`, `technical-debt`, `adr-audit` labels
- Updates existing issue if open
- Closes issue automatically when all violations resolved
- Groups violations by ADR number

### Supporting Workflows

| Workflow | Purpose | Triggers |
|----------|---------|----------|
| `build-only.yml` | Simple build + artifact upload | Push to main, manual |
| `dotnet.yml` | .NET build validation | (Legacy) |
| `test.yml` | Test runner | (Legacy) |
| `auto-add-to-project.yml` | Auto-add issues/PRs to GitHub Project | Issue/PR events |

---

## Workflow Integration Points

### Before Merging a PR

```
1. Push changes (triggers sdap-ci.yml)
2. Wait for all checks to pass:
   gh pr checks --watch
3. Review any ADR violation comments
4. Merge when all checks green
```

### After Merge to Master

```
1. CI (Router) runs on master
2. Path-triggered deploy workflows run if their paths changed
   (deploy-spaarke-ai / deploy-office-addins / deploy-infrastructure):
   gh run list --limit 10
3. Nothing else deploys automatically — the BFF is deployed by an operator
```

### Deploying the BFF

```
Use the /bff-deploy skill (operator-driven). Or, on demand:
   gh workflow run deploy-bff-api.yml -f environment=<env>
Monitor: gh run watch
Verify:  curl https://{app}.azurewebsites.net/ping
```

---

## CI/CD Workflow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        Developer Workflow                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  /push-to-github                                                 │
│  - Code review, ADR check (local)                               │
│  - Commit and push                                               │
│  - Create PR                                                     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  sdap-ci.yml (Automatic on PR/Push)                             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ security-scan│  │ build-test   │  │ code-quality │          │
│  │ (Trivy)      │  │ (Debug/Rel)  │  │ (ADR, format)│          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
│         │                  │                  │                  │
│         └──────────────────┼──────────────────┘                  │
│                            ▼                                     │
│                ┌──────────────────────┐                         │
│                │ integration-readiness │                         │
│                │ (Package artifacts)   │                         │
│                └──────────────────────┘                         │
│                            │                                     │
│         ┌──────────────────┼──────────────────┐                 │
│         ▼                  ▼                  ▼                  │
│  ┌────────────┐    ┌────────────┐    ┌────────────┐            │
│  │adr-pr-comm │    │  summary   │    │ artifacts  │            │
│  │(PR comment)│    │  (report)  │    │ (30 days)  │            │
│  └────────────┘    └────────────┘    └────────────┘            │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (on master merge)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Path-triggered deploy-*.yml only (SpaarkeAi, Office add-ins,   │
│  Bicep). BFF + everything else: operator-driven /              │
│  workflow_dispatch — see "Deployment Workflows".                │
└─────────────────────────────────────────────────────────────────┘
```
(The diagram's CI box shows the legacy `sdap-ci.yml` job layout; the required check is now `Router` → Tier 1/Tier 2 — see Key Terminology.)

---

## Troubleshooting

### Common CI Failures

| Failure | Cause | Fix |
|---------|-------|-----|
| `security-scan` | Vulnerable dependency | Update package or add to allowlist |
| `build-test` | Compilation error | Fix code errors locally |
| `code-quality` format | Code style violation | Run `dotnet format` locally |
| `code-quality` ADR | Architecture violation | Run `/adr-check` locally, fix violations |
| `code-quality` vulnerable | Package vulnerability | Update or replace vulnerable package |

### View Detailed Logs

```powershell
# Get run ID from list
gh run list --workflow=sdap-ci.yml

# View full logs
gh run view {run-id} --log

# View specific job logs
gh run view {run-id} --log --job={job-id}

# Download logs
gh run view {run-id} --log > ci-logs.txt
```

### Re-run Failed Jobs

```powershell
# Re-run only failed jobs
gh run rerun {run-id} --failed

# Re-run entire workflow
gh run rerun {run-id}
```

---

## Required Secrets

### CI Pipeline (sdap-ci.yml)
No secrets required - runs in read-only mode.

### Deployment Workflows
Each `deploy-*.yml` declares its own secrets / OIDC federation — read the workflow's `env:` and `secrets.*` references rather than relying on a list here.

---

## Integration with Skills

| Skill | CI/CD Integration |
|-------|-------------------|
| `push-to-github` | After push, check `gh pr checks` before merge |
| `adr-check` | Local validation mirrors `code-quality` ADR tests |
| `azure-deploy` | Bicep / Key Vault deployment (automated counterpart: `deploy-infrastructure.yml`) |
| `bff-deploy` | BFF deployment (automated counterpart: `deploy-bff-api.yml`, `workflow_dispatch` only) |
| `dataverse-deploy` | Solution / PCF / web resource deployment (no plugins — ADR-002) |
| `code-review` | Quality gates run same checks as `code-quality` job |

---

## Related Skills

- `push-to-github` - Push code and create PRs
- `pull-from-github` - Pull latest changes
- `azure-deploy` - Manual Azure deployments
- `dataverse-deploy` - Manual Dataverse deployments
- `adr-check` - Local ADR validation

---

## Operator Notes

- Always check `gh pr checks` before suggesting merge
- If CI fails, read logs with `gh run view {id} --log` before suggesting fixes
- ADR violations in CI mirror local `/adr-check` - use same fix guidance
- There is no staging environment; the BFF is never auto-deployed on merge (operator-driven via `/bff-deploy`)
- Never auto-deploy to prod
- Use `gh run watch` to monitor long-running deployments

### Worktree Considerations

- After merging to master from a worktree, **always sync the main repo**
- CI passing does NOT mean the main repo is synced - these are separate concerns
- When user asks to "merge to master and sync", ensure BOTH operations complete:
  1. Push to origin/master (triggers CI + any path-triggered deploy workflows)
  2. Pull origin/master to main repo's local master
- Report full status: CI status, any triggered deploy workflow status, AND main repo sync status

### Complete Merge Flow (Worktree)

```
1. Push branch:master → updates origin/master
2. CI runs on master → monitor with gh run watch
3. Path-triggered deploy workflows (if any) run → check gh run list
4. Sync main repo → cd {main-repo} && git pull origin master
5. Report all statuses to user
```

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Workflow fails in 0-2 seconds with "failed" status | Workflow startup failure — action version doesn't exist in registry (e.g., `actions/checkout@v6` when current major is v4). See [`FAILURE-MODES.md#G-3`](../../FAILURE-MODES.md#g-3-zero-second-github-actions-workflow-failures-are-startup-failures-not-test-failures) | Look at action version pins FIRST before debugging test logic. `actionlint` (Phase 4b) catches this pre-merge. |
| Merge landed but an expected deploy didn't run | Deploy workflows are path-filtered or `workflow_dispatch`-only; the change didn't touch the filtered paths, or a `workflow_run` chain references a renamed workflow | Inspect the deploy workflow's `on:` block (paths / `workflow_run: workflows: [<name>]`). After renaming any workflow, update every workflow that depends on it. |
| Required-status check failing but not gating the PR | Branch protection allows merge despite failing status (admin-bypass enabled OR check not marked required) | Re-audit required status checks in repo settings. Branch protection bypass during ai-procedure-quality-r1 is acceptable; re-audit at project wrap. |
| Action version is pinned to a major tag (`@v4`) instead of SHA | SHA pinning not enforced in any current workflow (0 of 115 actions are SHA-pinned per Phase 0 inventory) | Phase 4b task 070 introduces SHA pinning; until then, treat major-tag pins as a known security/reproducibility gap. |
