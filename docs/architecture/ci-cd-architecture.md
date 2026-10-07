# CI/CD Architecture

> **Last Updated**: 2026-09-30
> **Last Reviewed**: 2026-09-30
> **Reviewed By**: customer-provisioning-orchestration-r1 (doc-drift fix — workflow inventory reconciled against `.github/workflows/` after the 2026-05-26 `claude-code-review.yml` removal and the 2026-06-01 Wave C workflow-rationalization deletions)
> **Status**: Current
> **Purpose**: Describes the GitHub Actions workflow system, deployment strategies, quality gates, and promotion pipeline for the SDAP platform.

---

## Overview

The SDAP CI/CD system is implemented as a set of GitHub Actions workflows under `.github/workflows/` covering continuous integration, multi-environment deployment, infrastructure and provisioning-artifact publishing, and scheduled/operational checks. The architecture uses a **slot-swap strategy** for zero-downtime BFF API deployments, **direct-target promotion** (dev / staging / production, each deployed independently — not a sequential chain) with a manual approval gate for production, and **Bicep IaC** that CI validates (lint + compile) but never deploys — customer stamps are deployed only by the L2 control plane.

The key design decision is separating the PR gate from deployment and from provisioning: the PR gate is a single router (`ci-router.yml`) that dispatches a blocking **Tier 1** suite and an advisory **Tier 2** suite; deployment is operator-driven (`workflow_dispatch`, or auto-deploy to non-production targets only); customer provisioning is **no longer a GitHub Actions workflow** at all — it runs through the L1 handler / L2 control-plane REST API / L3 `/provision-environment` Claude Code skill architecture. This document stays at the decisions-and-rationale level; for the complete, current workflow inventory and per-workflow trigger/job detail, see [`docs/procedures/ci-cd-workflow.md`](../procedures/ci-cd-workflow.md).

## Workflow Inventory

### Continuous Integration

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **CI Router** | `ci-router.yml` | PR → master, push → master, merge_group | The single required status check (`CI / Router`): classifies the diff, dispatches a blocking Tier 1 + an advisory Tier 2, and aggregates the result (Tier 2 is excluded from adjudication by construction, so it can never redden the gate) |
| **CI Tier 1 (Blocking)** | `ci-tier1-blocking.yml` | `workflow_call` from the router | Compile (whole solution), full NetArchTest ArchTest suite, changed-surface smoke, auth smoke, golden-utterance eval gate, I1–I5 tenant-isolation invariants, Compose fidelity corpus round-trip |
| **CI Tier 2 (Advisory)** | `ci-tier2-advisory.yml` | `workflow_call` from the router | Format, lint, full unit-test suite, ADR compliance, markdown-link validation, Last-Reviewed stamp, plugin size; every job is `continue-on-error: true` |
| **SDAP CI** (legacy) | `sdap-ci.yml` | Push to master, PRs (`paths-ignore`: `docs/**`, `.claude/**`, …) | Original monolithic pipeline (security scan, build/test, client+code quality, ADR PR comment, tenant isolation, Compose fidelity). No longer the required check — superseded by `CI / Router` — and pending deletion by another project |

> **`claude-code-review.yml` removed** 2026-05-26 (commit `4edce041ef`, "remove claude-code-review workflow"). It required a missing `ANTHROPIC_API_KEY` secret (so it could never complete a review) and duplicated ADR coverage already enforced by the `code-review` / `adr-check` skills at `task-execute` Step 9.5. No GitHub Actions workflow runs AI PR review today — CodeRabbit (a separate third-party GitHub App, not an Actions workflow) still reviews every PR, advisory-only.

### Deployment

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **Deploy BFF API** | `deploy-bff-api.yml` | Manual dispatch only (the `push: master` auto-deploy trigger was removed — BFF deploys are operator-driven) | Build -> test -> deploy to staging slot -> verify staging health -> swap to production (approval gate) -> verify production -> auto-rollback on failure |
| **Environment Promotion** | `deploy-promote.yml` | Manual dispatch only | Direct-target deploy to dev / staging / production (each deploys independently, not a chain); production requires reviewer approval |
| **Deploy Office Add-ins** | `deploy-office-addins.yml` | Push to master (`src/client/office-addins/**`), manual dispatch | Build and deploy the unified Outlook+Word add-in to an Azure Static Web App |
| **Deploy SpaarkeAi** | `deploy-spaarke-ai.yml` | Push to master (SpaarkeAi / LegalWorkspace paths), manual dispatch | Build the single-file bundle, deploy as the `sprk_spaarkeai` Dataverse web resource; auto-deploys to dev, production requires reviewer approval |

> **`deploy-slot-swap.yml` removed** 2026-06-01 (commit `902bebc49c`, D-06 — zero-merge consolidation): `deploy-bff-api.yml` was already a functional superset for the in-use production-only slot-swap path.

### Infrastructure

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **Validate Bicep Infrastructure** | `deploy-infrastructure.yml` | Push/PR on `infrastructure/bicep/**`, manual dispatch (no inputs) | Lint every Bicep file and compile `customer.bicep` + the remaining standalone `stacks/*.bicep`. **Deploys nothing** — no what-if, no Azure login, `contents: read`. `customer.bicep` is the only customer-stamp template, deployed only by the L2 control plane's handler H2a into each customer's own subscription (owner decision D19, amended ADR-027). *(Its former what-if + deploy stages, which targeted `stacks/model2-full.bicep`, were retired by task 249, 2026-10-02.)* |
| **Publish Provisioning ARM Artifacts** | `publish-provisioning-arm-artifacts.yml` | Push to master (bicep paths), manual dispatch | Compiles `customer.bicep` to ARM JSON and publishes it for the H2a Azure-stamp deploy handler (`model1-shared` retired by task 225a) |

> **`deploy-platform.yml` removed** 2026-06-01 (commit `902bebc49c`, D-05). Spaarke's own non-customer-serving platform infrastructure is now deployed by running `scripts/Deploy-Platform.ps1` directly from an operator shell (`az`/`pwsh`) — it is no longer wrapped in a GitHub Actions workflow; see the script's own header comment for current usage.
>
> **`provision-customer.yml` removed** 2026-06-01 (commit `902bebc49c`, D-08). Customer provisioning is **not** a GitHub Actions workflow at all today — it runs through the L1 handler / L2 control-plane REST API / L3 `/provision-environment` Claude Code skill architecture (`.claude/skills/provision-environment/SKILL.md`); see [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) for the full operator path.

> 🟡 **Deployment-model note (2026-09-28, owner decision [D-12](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md))**
> — this table previously said the Bicep workflow *"supports Model 1 (shared) and Model 2 (dedicated)
> stacks"*. **There is no shared Model 1 stack.** Spaarke has **two deployment models that differ in one
> axis only: which Azure tenant owns the customer's subscription** (Model 1 = Spaarke's; Model 2 = the
> customer's). **Every Azure resource is dedicated per customer in both**, in that customer's own Azure
> subscription and resource group, so infrastructure CI deploys **one full stack per customer** — there is
> no shared-platform step to run first and no per-customer overlay on top of it. The `model1-shared.bicep`
> and `model1-customer.bicep` stacks were **deleted by `customer-provisioning-orchestration-r1` task 225a
> (2026-10-01)** together with their parameter files, the `bicep-e2e-dry-run.ps1` assertion, the provisioning
> ARM manifest's `model1-shared` entry and the publish / deploy-infrastructure workflow steps. The only model-dependent CI behaviour is that
> **Model 2 additionally requires H0.5 admin consent and Azure Lighthouse delegation**; Model 1 requires
> neither.
>
> *(Task 249, 2026-10-02: infrastructure CI no longer deploys anything — the per-customer stack is deployed
> only by the L2 control plane's handler H2a; `deploy-infrastructure.yml` now only validates.)*

### Quality & Monitoring

| Workflow | File | Trigger | Purpose |
|----------|------|---------|---------|
| **nightly-health** | `nightly-health.yml` | Daily (06:00 UTC), manual dispatch | Flake hunt, bundle-size drift, vuln scan, full integration suite, coverage observation (ADR-038: observation only, never a gate), Trivy filesystem scan, dependency audit, Graph app-role parity; aggregates into a rolling GitHub issue |
| **report-workflow-health** | `report-workflow-health.yml` | Weekly (Monday 9 AM UTC), manual dispatch | Rolling 7-day per-workflow success-rate report across every `.github/workflows/*.yml`; creates/updates a tracking issue |
| **ADR Architecture Audit** | `adr-audit.yml` | Weekly (Monday 9 AM UTC), manual dispatch | Runs `Spaarke.ArchTests` NetArchTest suite; groups violations by ADR; creates/updates/closes tracking issue |

> **`nightly-quality.yml` and `weekly-quality.yml` removed** 2026-06-01 (commit `902bebc49c`, D-03) — `nightly-quality.yml` was a `src/` regression blocker (NFR-01 forbade fixing it in place); `weekly-quality.yml` depended on its artifacts and was also broken. Neither has a 1:1 successor: `nightly-health.yml` covers different ground (no SonarCloud analysis, no AI review), and `report-workflow-health.yml` reports per-workflow success rate, not a code-quality trend table. SonarCloud and the nightly/weekly Claude Code AI review are **not run anywhere** in this repo's CI today.

### Automation

> **`auto-add-to-project.yml` removed** 2026-06-01 (commit `902bebc49c`, D-10 — broken 8+ months, 29/29 runs failed in the preceding 30 days). No GitHub Actions workflow auto-adds issues to the project board today; GitHub Project tracking now runs through the `/devops-*` skill suite (see root `CLAUDE.md` §17, "Portfolio tracking + DevOps procedures").

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

`deploy-promote.yml` is **direct-target** (2026-06-02, D-12 refactor) and **`workflow_dispatch`-only** — the earlier automatic `workflow_run` trigger (dev auto-promote after "SDAP CI" on master) was removed 2026-08-11 (dotnet-10-upgrade-r1). There is no sequential dev → staging → prod chain; each dispatch deploys to exactly the `target_environment` requested:

```
dispatch(target_environment=dev)        ──> Dev        (smoke tests: /ping, /healthz)
dispatch(target_environment=staging)    ──> Staging    (smoke tests + CORS integration check)
dispatch(target_environment=production) ──> Production (requires `production` GitHub Environment reviewer approval, then smoke tests)
```

1. **Plan**: Resolves the dispatched `target_environment` into a single deploy flag (`deploy_dev` / `deploy_staging` / `deploy_prod`) and the artifact run ID
2. **Dev / Staging / Production**: Only the flagged environment's job runs; `production` additionally gates on `environment: production` reviewer approval

Each target is an operationally independent App Service in its own resource group — the gate that matters is the GitHub Environment protection rule on `production`, not workflow-internal chaining.

## Quality Gates

### PR Gate — CI Router (ci-router.yml dispatching ci-tier1-blocking.yml + ci-tier2-advisory.yml)

| Gate | Blocking? | Details |
|------|-----------|---------|
| Compile (whole solution, Debug) | Yes (Tier 1) | `dotnet build`, no `-warnaserror` (policy lives in `Directory.Build.props`) |
| Full NetArchTest ArchTest suite | Yes (Tier 1) | No inclusion filter — a renamed/new test can't silently drop out |
| Changed-surface smoke / auth smoke | Yes (Tier 1) | Conditional on changed BFF / auth paths |
| Golden-utterance eval gate | Yes (Tier 1) | `Category=GoldenUtteranceEval` |
| I1–I5 tenant-isolation invariants | Yes (Tier 1) | Treated as a security incident if it breaks, not a flake |
| Compose fidelity corpus round-trip | Yes (Tier 1) | Over every `.docx` under `tests/fixtures/compose-corpus/` |
| Format, lint, full unit-test suite, ADR compliance, markdown links, Last-Reviewed stamp, plugin size | No (Tier 2, advisory) | Every job is `continue-on-error: true`; posted as one deduplicated PR comment |

`CI / Router` is the **only** required status check on `master` (verified 2026-09-30). The legacy `sdap-ci.yml` (Trivy scan, Debug+Release build/test matrix, Prettier/ESLint, `dotnet format`, ADR NetArchTest suite, dependency audit) still runs on PRs but is no longer required and is pending deletion by another project. `claude-code-review.yml` (the one AI-review gate this table used to list) was removed 2026-05-26 — see the Workflow Inventory note above.

### Nightly (nightly-health.yml)

| Gate | Budget | Details |
|------|--------|---------|
| flake-hunt | 60 min | Runs the unit suite 3× sequentially; flags tests that didn't pass all 3 runs |
| bundle-size | 30 min | Flags >10% drift vs `.github/baseline-bundle-sizes.json` for 3 key client artifacts |
| vuln-scan | 20 min | `dotnet list package --vulnerable --include-transitive` + `npm audit` on 3 key client packages |
| full-integration | 45 min | Full, unfiltered `Spe.Integration.Tests` run |
| coverage-observation | 30 min | Aggregated Cobertura coverage — observation only, never a gate (ADR-038) |
| trivy-fs | 20 min | Filesystem Trivy scan; never fails the job |
| dep-audit | 20 min | Full transitive `dotnet`/`npm` vulnerability audit |
| graph-app-role-parity | n/a | Self-skips today — the four `NIGHTLY_*` secrets it needs were never populated |

Results aggregated into a single rolling GitHub issue, "Nightly Health — {date}".

### Weekly (report-workflow-health.yml)

Iterates every `.github/workflows/*.yml`, queries each workflow's 7-day run history, and appends a per-workflow success-rate snapshot to a single rolling "CI Health Report" tracking issue. This is **not** a code-quality trend table (coverage %, violation count, TODOs) — that responsibility had no direct successor when `weekly-quality.yml` was removed (see the Workflow Inventory note above).

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Azure App Service | Deployment slots, slot swap | Zero-downtime deployment |
| Depends on | Azure OIDC | Federated credentials | `id-token: write` permission |
| Depends on | GitHub Environments | Protection rules, required reviewers | Manual approval gates |
| Consumed by | Developers | PR checks, deployment summaries | Quality feedback loop |
| Consumed by | Operations | Issue-based reporting | Nightly/weekly tracking issues |

> SonarCloud and the Anthropic API (`ANTHROPIC_API_KEY`) are **no longer dependencies** of any workflow in this repo — both were used only by the now-deleted `nightly-quality.yml` and `claude-code-review.yml` (see the Workflow Inventory notes above).

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Slot-swap for API deployment | Staging slot + swap | Zero-downtime; instant rollback by re-swapping | -- |
| OIDC for Azure auth | Federated credentials, no stored secrets | More secure; no secret rotation needed | -- |
| ADR tests non-blocking | PR comment, not merge blocker | Avoid blocking velocity while building awareness | -- |
| Nightly health checks | Separate from CI; `nightly-health.yml` runs daily 06:00 UTC | Deeper analysis without slowing PRs | -- |
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
- **Artifact cross-workflow downloads**: `deploy-promote.yml` uses `dawidd6/action-download-artifact` to pull artifacts from `sdap-ci.yml` runs; this requires `actions: read` permission and correct artifact naming
- **ADR PR comment deduplication — two independent bots now**: `sdap-ci.yml`'s legacy comment searches for `"ADR Architecture Validation Report"`; `ci-tier2-advisory.yml`'s `tier2-pr-comment` job searches for `"Tier 2 Advisory Report"` instead (deliberately different marker text so the two never collide). Changing either string breaks that workflow's update-in-place behavior
- **`config/coverlet-nightly.runsettings` is currently orphaned**: it was written for the deleted `nightly-quality.yml` (SonarCloud opencover output); no workflow references it today

## Related

- [CI/CD Workflow Procedure](../procedures/ci-cd-workflow.md) -- Operational procedures for the CI/CD system
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) -- Minimal API + BackgroundService (health check endpoint)
- [ADR-002](../../.claude/adr/ADR-002-thin-plugins.md) -- No Dataverse plugins; enforced by the `ADR002_PluginTests` zero-plugin arch test (plugin size validation is dead — no plugin exists; job removal deferred to post-cutover, see ci-cd-unit-test-remediation-r1 notes)
