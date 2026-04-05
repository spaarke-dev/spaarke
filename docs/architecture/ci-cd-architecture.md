# CI/CD Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Describes the GitHub Actions workflow system, deployment strategies, quality gates, and promotion pipeline for the SDAP platform.

---

## Overview

The SDAP CI/CD system is implemented as 13 GitHub Actions workflows covering continuous integration, multi-environment deployment, infrastructure provisioning, quality assurance, and operational automation. The architecture uses a **slot-swap strategy** for zero-downtime BFF API deployments, **multi-stage promotion** (dev -> staging -> prod) with manual approval gates for production, and **Bicep IaC** for infrastructure changes with what-if previews.

The key design decision is separating CI (build/test/quality) from CD (deployment/promotion), with the CI pipeline producing deployment artifacts consumed by multiple downstream workflows.

## Workflow Inventory

### Continuous Integration

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **SDAP CI** | `sdap-ci.yml` | Push to master, PRs | Core CI: security scan, build/test (Debug+Release matrix), client quality (Prettier+ESLint), code quality (format, ADR tests, plugin size, dependency audit), integration readiness, ADR PR comment |
| **Claude Code Architecture Review** | `claude-code-review.yml` | PR opened/synchronized | Advisory AI code review using Claude Sonnet; checks ADR compliance, security, patterns; posts PR comment (non-blocking) |

### Deployment

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **Deploy BFF API** | `deploy-bff-api.yml` | Push to master (`src/server/api/**`), manual dispatch | Build -> test -> deploy to staging slot -> verify staging health -> swap to production (approval gate) -> verify production -> auto-rollback on failure |
| **Deploy via Slot Swap** | `deploy-slot-swap.yml` | Manual dispatch, after SDAP CI completes on master | Generic slot-swap deployment: build -> resolve config -> deploy to staging slot -> health check -> manual approval -> swap -> verify production |
| **Environment Promotion** | `deploy-promote.yml` | Manual dispatch, after SDAP CI on master | Multi-stage promotion: dev -> staging -> prod; auto-deploys to dev, manual approval for prod; downloads CI artifacts |
| **Deploy Office Add-ins** | `deploy-office-addins.yml` | Push to master (`src/client/office-addins/**`), manual dispatch | Build and deploy to Azure Static Web App |

### Infrastructure

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **Deploy Bicep Infrastructure** | `deploy-infrastructure.yml` | Push/PR on `infrastructure/bicep/**`, manual dispatch | Validate Bicep -> what-if preview (PR comment) -> deploy on approval; supports Model 1 (shared) and Model 2 (dedicated) stacks |
| **Deploy Platform Infrastructure** | `deploy-platform.yml` | Manual dispatch | Deploy shared platform via `Deploy-Platform.ps1`; what-if preview -> deploy -> verify (resource group, App Service health, Key Vault) |
| **Provision Customer** | `provision-customer.yml` | Manual dispatch | End-to-end customer provisioning via `Provision-Customer.ps1`; input validation -> provision (with approval) -> post-provisioning verification; supports dry run |

### Quality & Monitoring

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **Nightly Quality** | `nightly-quality.yml` | Cron (Mon-Fri, 6 AM UTC), manual dispatch | Full test suite with coverage -> SonarCloud analysis -> Claude Code headless AI review -> dependency audit (dotnet + npm) -> aggregate into rolling GitHub issue |
| **Weekly Quality Summary** | `weekly-quality.yml` | Cron (Friday, 10 PM UTC), manual dispatch | Aggregates nightly run artifacts into weekly trend table (coverage %, violations, TODOs, vulnerable deps); creates/updates GitHub issue |
| **ADR Architecture Audit** | `adr-audit.yml` | Weekly (Monday 9 AM UTC), manual dispatch | Runs `Spaarke.ArchTests` NetArchTest suite; groups violations by ADR; creates/updates/closes tracking issue |

### Automation

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **Auto-add to Project** | `auto-add-to-project.yml` | Issue opened/reopened | Adds issues to org-level GitHub project board |

## Slot-Swap Deployment Strategy

The BFF API uses Azure App Service deployment slots for zero-downtime deployments.

### Flow (deploy-bff-api.yml)

1. **Build**: `dotnet publish` produces deployment package
2. **Test**: Unit tests run in parallel (skippable for emergency deploys)
3. **Deploy to Staging Slot**: Zip deploy to `staging` slot; 15s warm-up wait
4. **Verify Staging**: `/healthz` health check with 12 retries at 5s intervals; `/ping` smoke test
5. **Swap to Production**: Requires manual approval via `production` GitHub environment protection; executes `az webapp deployment slot swap`
6. **Verify Production**: `/healthz` health check on production URL
7. **Auto-Rollback**: If production health check fails, automatically swaps back to previous version; if rollback also fails, logs manual intervention command

### Key Properties

- Concurrency group prevents parallel deployments (`cancel-in-progress: false`)
- OIDC federated credentials for Azure login (no stored secrets for auth)
- The staging slot warm-up uses `WEBSITE_SWAP_WARMUP_PING_PATH=/healthz` (configured in Bicep)
- After swap, staging contains the previous production version (instant rollback)

## Multi-Stage Promotion (deploy-promote.yml)

### Promotion Path

```
SDAP CI (master) ──auto──> Dev ──auto──> Staging ──approval──> Prod
```

1. **Plan**: Determines promotion path based on target environment; resolves artifact run ID
2. **Dev**: Auto-deploys CI artifacts; runs smoke tests (`/ping`, `/healthz`)
3. **Staging**: Auto-deploys after dev succeeds; runs smoke tests + CORS integration check
4. **Prod**: Requires manual approval via `prod` environment protection; runs smoke tests

For automatic triggers (workflow_run from SDAP CI), only dev is deployed. Manual dispatch can target any environment.

## Quality Gates

### Per-PR (sdap-ci.yml)

| Gate | Blocking? | Details |
|------|-----------|---------|
| Trivy security scan | Yes | Filesystem vulnerability scan, SARIF uploaded to GitHub Security |
| Build (Debug + Release) | Yes | Matrix build with `-warnaserror` |
| Unit tests with coverage | Yes | Coverlet coverage collection |
| Prettier format check | Yes | Client TypeScript/TSX files |
| ESLint strict check | Yes | `--max-warnings 0` on PCF code |
| `dotnet format` verification | Yes | No formatting changes allowed |
| ADR NetArchTest suite | No (warning) | Violations posted as PR comment |
| Plugin size validation | Yes | Must be under 1MB (ADR-002) |
| Dependency vulnerability audit | Yes | Fails on vulnerable packages |
| Claude Code AI review | No (advisory) | Architecture review posted as PR comment |

### Nightly (nightly-quality.yml)

| Gate | Budget | Details |
|------|--------|---------|
| Full test suite + coverage | 10 min | Coverlet with nightly runsettings |
| SonarCloud analysis | 8 min | Uses coverage artifacts from test job |
| Claude Code AI review | 5 min | Headless mode with `nightly-review-prompt.md` |
| Dependency audit | 5 min | `dotnet list --vulnerable` + `npm audit` per package |

Results aggregated into rolling GitHub issue (label: `nightly-quality`).

### Weekly Trend (weekly-quality.yml)

Collects artifacts from 5 nightly runs (Mon-Fri) and builds a trend table tracking: coverage %, new violations, TODO count, vulnerable dependencies. Creates/updates GitHub issue (label: `weekly-quality-summary`).

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Azure App Service | Deployment slots, slot swap | Zero-downtime deployment |
| Depends on | Azure OIDC | Federated credentials | `id-token: write` permission |
| Depends on | GitHub Environments | Protection rules, required reviewers | Manual approval gates |
| Depends on | SonarCloud | `SONAR_TOKEN` secret | Nightly code analysis |
| Depends on | Anthropic API | `ANTHROPIC_API_KEY` secret | AI code review (nightly + PR) |
| Consumed by | Developers | PR checks, deployment summaries | Quality feedback loop |
| Consumed by | Operations | Issue-based reporting | Nightly/weekly quality tracking |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Slot-swap for API deployment | Staging slot + swap | Zero-downtime; instant rollback by re-swapping | -- |
| OIDC for Azure auth | Federated credentials, no stored secrets | More secure; no secret rotation needed | -- |
| ADR tests non-blocking | PR comment, not merge blocker | Avoid blocking velocity while building awareness | -- |
| Nightly quality pipeline | Separate from CI; runs Mon-Fri 6 AM UTC | Deeper analysis without slowing PRs; 15-min budget | -- |
| Rolling GitHub issues | Single issue updated nightly, separate weekly | Prevents issue sprawl; trend visibility | -- |
| Matrix build (Debug + Release) | Both configs tested in CI | Catches config-specific issues (e.g., conditional compilation) | -- |

## Constraints

- **MUST**: Never `cancel-in-progress` on deployment workflows
- **MUST**: Require manual approval (GitHub environment protection) before production deployments
- **MUST**: Auto-rollback if production health check fails after swap
- **MUST**: Use OIDC federated credentials for Azure login (no stored client secrets)
- **MUST NOT**: Skip tests in CI for regular deployments (only `workflow_dispatch` with `skip-tests` for emergencies)
- **MUST**: Upload test results and coverage as artifacts for audit trail

## Known Pitfalls

- **Slot warm-up timing**: The 15-30s sleep after staging deployment is a heuristic; .NET DI container initialization on first request may take longer under cold start
- **Artifact cross-workflow downloads**: `deploy-promote.yml` uses `dawidd6/action-download-artifact` to pull artifacts from SDAP CI runs; this requires `actions: read` permission and correct artifact naming
- **SonarCloud coverage path**: Coverage files must match the `sonar.cs.opencover.reportsPaths` pattern; nightly uses a separate `coverlet-nightly.runsettings` file
- **ADR PR comment deduplication**: The bot comment search looks for `"ADR Architecture Validation Report"` in the body; changing this string breaks update-in-place behavior
- **Weekly artifact collection**: Downloads artifacts from up to 5 most recent nightly runs; if nightly runs are disabled, weekly summary shows empty state

## Related

- [CI/CD Workflow Procedure](../procedures/ci-cd-workflow.md) -- Operational procedures for the CI/CD system
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) -- Minimal API + BackgroundService (health check endpoint)
- [ADR-002](../../.claude/adr/ADR-002-thin-plugins.md) -- Thin plugins (size validation in CI)
