# CI/CD Pipeline Skill

> **Category**: Operations
> **Last Updated**: January 2026

---

## Purpose

Document and interact with GitHub Actions CI/CD workflows. Provides guidance on checking build status, understanding pipeline stages, and integrating with deployment workflows.

---

## Key Terminology

Understanding the distinction between CI, deployment, and sync:

| Term | What It Means | Workflow |
|------|---------------|----------|
| **CI (Continuous Integration)** | Build, test, code quality checks | `sdap-ci.yml` |
| **Staging Deployment** | Deploy to staging environment | `deploy-staging.yml` (separate workflow) |
| **Production Deployment** | Deploy to production | `deploy-to-azure.yml` (manual trigger) |
| **Merge to Master** | Push changes to origin/master | Git operation (not a workflow) |
| **Sync Main Repo** | Pull origin/master to local main repo | Git operation (needed for worktrees) |

### Important Distinctions

1. **CI ≠ Staging Deployment**: CI validates code quality. Staging deployment is a *separate* workflow that runs *after* CI passes on master.

2. **"Merge to master" updates origin/master** but does NOT:
   - Trigger staging deployment immediately (CI runs first)
   - Sync the main repo's local master (must be done explicitly when using worktrees)

3. **Staging deployment triggers automatically** after CI passes on master, but may fail independently of CI.

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
| `code-quality` | Format check, ADR tests, plugin size, dependencies | Yes |
| `integration-readiness` | Package artifacts for deployment | Yes |
| `adr-pr-comment` | Post ADR violations to PR (non-blocking) | No |
| `summary` | Pipeline summary report | No |

**Key Checks**:
- Trivy security scan uploads to GitHub Security tab
- `dotnet format --verify-no-changes` for code style
- NetArchTest ADR validation (`Spaarke.ArchTests`)
- Plugin assembly size limit (1MB per ADR-002)
- Vulnerable package detection

### Deployment: `deploy-to-azure.yml`

**Triggers**: Manual (`workflow_dispatch`), After `sdap-ci.yml` succeeds on master

| Job | Purpose |
|-----|---------|
| `deploy-infrastructure` | Deploy Bicep templates to Azure |
| `deploy-api` | Deploy BFF API to App Service |
| `smoke-test` | Verify `/ping` endpoint responds |
| `notify` | Deployment summary |

**Prerequisites**:
- Azure OIDC authentication configured
- `AZURE_CLIENT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID` secrets set
- Environment: `production`

### Staging Deployment: `deploy-staging.yml`

**Triggers**: Manual, After `sdap-ci.yml` succeeds on master

| Job | Purpose |
|-----|---------|
| `deploy-api` | Deploy API to staging App Service |
| `deploy-plugins` | Deploy Dataverse plugins via PAC CLI |
| `integration-tests` | Run integration tests against staging |
| `notify` | Deployment summary |

**Prerequisites**:
- `STAGING_APP_NAME` secret configured
- `POWER_PLATFORM_*` secrets for plugin deployment
- Environment: `staging`

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
1. sdap-ci.yml runs on master
2. If successful, deploy-staging.yml triggers automatically
3. Monitor staging deployment:
   gh run list --workflow=deploy-staging.yml
4. Verify staging health:
   curl https://{staging-app}.azurewebsites.net/ping
```

### Manual Production Deployment

```
1. Verify staging is healthy
2. Trigger production deployment:
   gh workflow run deploy-to-azure.yml
3. Monitor deployment:
   gh run watch
4. Verify production health:
   curl https://{prod-app}.azurewebsites.net/ping
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
│  deploy-staging.yml (Automatic after CI success)                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ deploy-api   │─▶│deploy-plugins│─▶│ integration  │          │
│  │ (App Service)│  │ (PAC CLI)    │  │ tests        │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (manual trigger)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  deploy-to-azure.yml (Manual production deployment)             │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │ deploy-infra │─▶│ deploy-api   │─▶│ smoke-test   │          │
│  │ (Bicep)      │  │ (App Service)│  │ (/ping)      │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
└─────────────────────────────────────────────────────────────────┘
```

---

## Troubleshooting

### Common CI Failures

| Failure | Cause | Fix |
|---------|-------|-----|
| `security-scan` | Vulnerable dependency | Update package or add to allowlist |
| `build-test` | Compilation error | Fix code errors locally |
| `code-quality` format | Code style violation | Run `dotnet format` locally |
| `code-quality` ADR | Architecture violation | Run `/adr-check` locally, fix violations |
| `code-quality` plugin size | Plugin >1MB | Reduce dependencies per ADR-002 |
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
| `STAGING_TEST_CLIENT_ID` | Integration test credentials |
| `STAGING_TEST_CLIENT_SECRET` | Integration test credentials |

### Production Deployment
| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Azure OIDC app ID |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_TENANT_ID` | Azure AD tenant |
| `AZURE_RESOURCE_GROUP` | Target resource group |
| `AZURE_APP_SERVICE_NAME` | App Service name |

---

## Integration with Skills

| Skill | CI/CD Integration |
|-------|-------------------|
| `push-to-github` | After push, check `gh pr checks` before merge |
| `adr-check` | Local validation mirrors `code-quality` ADR tests |
| `azure-deploy` | Manual deployment uses same process as `deploy-to-azure.yml` |
| `dataverse-deploy` | Plugin deployment mirrors `deploy-staging.yml` |
| `code-review` | Quality gates run same checks as `code-quality` job |

---

## Related Skills

- `push-to-github` - Push code and create PRs
- `pull-from-github` - Pull latest changes
- `azure-deploy` - Manual Azure deployments
- `dataverse-deploy` - Manual Dataverse deployments
- `adr-check` - Local ADR validation

---

## Tips for AI

- Always check `gh pr checks` before suggesting merge
- If CI fails, read logs with `gh run view {id} --log` before suggesting fixes
- ADR violations in CI mirror local `/adr-check` - use same fix guidance
- Staging deploys automatically after master merge - no manual trigger needed
- Production deployment requires manual trigger - never auto-deploy to prod
- Use `gh run watch` to monitor long-running deployments

### Worktree Considerations

- After merging to master from a worktree, **always sync the main repo**
- CI passing does NOT mean the main repo is synced - these are separate concerns
- When user asks to "merge to master and sync", ensure BOTH operations complete:
  1. Push to origin/master (triggers CI → staging)
  2. Pull origin/master to main repo's local master
- Report full status: CI status, staging deployment status, AND main repo sync status

### Complete Merge Flow (Worktree)

```
1. Push branch:master → updates origin/master
2. CI runs on master → monitor with gh run watch
3. If CI passes → staging deployment triggers automatically
4. Sync main repo → cd {main-repo} && git pull origin master
5. Report all statuses to user
```
