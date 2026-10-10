---
description: Document and interact with GitHub Actions CI/CD workflows — checking build status, understanding pipeline stages, and integrating with deployment workflows
tags: [ci-cd, github-actions, deployment, workflows, automation]
techStack: [github-actions, dotnet, azure, dataverse]
appliesTo: [".github/workflows/", "ci-cd", "build status", "pipeline"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-09-30
---

# CI/CD Pipeline Skill

> **Category**: Operations
> **Last Reviewed**: 2026-09-30
> **Reviewed By**: customer-provisioning-orchestration-r1 (plan G24 — workflow sections corrected against `.github/workflows`; full per-workflow reference: [`docs/procedures/ci-cd-workflow.md`](../../../docs/procedures/ci-cd-workflow.md))
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
| **CI (Continuous Integration)** | Build, test, code quality checks | `ci-router.yml` → `ci-tier1-blocking.yml` (blocking) + `ci-tier2-advisory.yml` (advisory). **`Router` is the only required status check** on master (verified 2026-09-30). Legacy `sdap-ci.yml` still runs but is not required (pending deletion) |
| **Deployment** | Deploy a component to an environment | Per-component `deploy-*.yml` (see [Deployment Workflows](#deployment-workflows)) — mostly `workflow_dispatch` / operator-driven |
| **Merge to Master** | Push changes to origin/master | Git operation (not a workflow) |
| **Sync Main Repo** | Pull origin/master to local main repo | Git operation (needed for worktrees) |

### Important Distinctions

1. **CI ≠ Deployment**: CI validates code quality. There is **no staging environment and no automatic "deploy after CI" chain**. (`deploy-staging.yml` / `deploy-to-azure.yml` were documented here previously; neither exists — removed from this skill 2026-09-25.)

2. **"Merge to master" updates origin/master** but does NOT:
   - Deploy the BFF — BFF deploys are **operator-driven** (`/bff-deploy`, or `deploy-bff-api.yml` via `workflow_dispatch`); CI never auto-deploys the BFF on merge
   - Sync the main repo's local master (must be done explicitly when using worktrees)

3. **Two deploy workflows are path-triggered on push to master** (`deploy-spaarke-ai.yml` → dev, `deploy-office-addins.yml`) — they can fail independently of CI. `deploy-infrastructure.yml` ("Validate Bicep Infrastructure") is path-triggered too, but it only lints and compiles Bicep — it has no deploy job since task 249 (customer stamps are deployed by the L2 control plane).

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

### Primary CI: `ci-router.yml` (workflow `CI`, required check `Router`)

**Triggers**: every pull request, push to `master`, `merge_group` — **no `paths:` filter** (a path-filtered required check gets stuck pending). The `classify` job decides which tiers run; both tiers are skipped on a docs-only diff, and a skipped job reports success.

| Job | Purpose | Blocking? |
|-----|---------|-----------|
| `classify` (Classify Diff) | Path-aware diff classification | — |
| Tier 1 → `ci-tier1-blocking.yml` | Compile (Debug) · Arch Tests (full suite) · Changed-Surface Integration Smoke · Auth Smoke · Eval Gate (Golden Utterances) · Tenant Isolation (I1–I5) · Compose Fidelity Gate · Xrm Capability Guard (getXrm AST scan; on client/solution/workflow changes) · DataGrid External-Host Gate (advisory until its flip) | **Yes** (DataGrid gate: no, until flipped) |
| Tier 2 → `ci-tier2-advisory.yml` | Format (`dotnet format`) · Lint (ESLint + Prettier) · Full Unit Tests · ADR Compliance (NetArchTest) · Markdown Link Validator · Last Reviewed Stamp · Plugin Size (ADR-002) · one deduplicated advisory PR comment | No — excluded from the gate by construction |
| `router-result` (**Router**) | `if: always()` + `re-actors/alls-green` over the tiers — its result IS the required check | **Yes** |

The legacy monolithic `sdap-ci.yml` (and its `sdap-ci-docs-only.yml` fallback) still runs on PRs/pushes but is **not** required.

### Deployment Workflows

Verified against `.github/workflows/` on 2026-09-25. There is no staging environment and no plugin deployment (Spaarke ships no Dataverse plugins — ADR-002).

| Workflow | Deploys | Trigger |
|----------|---------|---------|
| `deploy-bff-api.yml` | BFF API (App Service) | `workflow_dispatch` only — BFF deploys are operator-driven; prefer the `/bff-deploy` skill |
| `deploy-infrastructure.yml` ("Validate Bicep Infrastructure") | Bicep infrastructure | PR + push to master on `infrastructure/bicep/**`, or `workflow_dispatch` → lint every Bicep file + compile `customer.bicep` and the remaining stacks; **deploys nothing** (task 249, D19) |
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

None of these is a required check. Full per-workflow detail: [`docs/procedures/ci-cd-workflow.md`](../../../docs/procedures/ci-cd-workflow.md).

| Workflow | Purpose | Triggers |
|----------|---------|----------|
| `workflows-validate.yml` (actionlint) | Lints every workflow YAML file | Every PR |
| `ci-router.yml` job `prereqs` (was `provisioning-prereqs-validate.yml`, retired 2026-10-09) | `prereqs.yaml` + `intake.schema.json` shape and parser parity — BLOCKING via `CI / Router` | PR, push to master, merge_group — when `scripts/provisioning-prereqs/**`, `.claude/skills/provision-environment/**` or `ci-router.yml` changes (docs-only diffs included) |
| `css-reset-gate.yml` | Code Page `index.html` box-sizing reset | PR/push on Code Page `index.html` paths |
| `office-addins-tests.yml` | Office add-in jest ratchet, typecheck, server suites, ESLint | PR/push on office-addins + related paths |
| `build-provisioning-sidecar.yml` | Build + Trivy-scan the provisioning sidecar image (push leg publishes it) | PR/push on sidecar paths, manual |
| `publish-provisioning-arm-artifacts.yml` | Compile the customer Bicep to ARM JSON for H2a | Push to master on Bicep paths, manual |
| `publish-dataverse-solutions-manifest.yml` | Publish the managed-solution manifest H6 reads | Manual (release-time); PR dry run on the SpaarkeMaster source |
| `publish-copilot-agent-template.yml` | Build + publish the per-customer Copilot agent template (sample render validated against Microsoft's schemas) | Manual (release-time); PR dry run on the agent source |
| `nightly-health.yml` | Flake hunt, bundle-size drift, vuln + Trivy scans, integration suite, coverage observation | Daily 06:00 UTC, manual |
| `client-tests.yml` | Nightly jest baseline across client packages | Nightly 07:00 UTC, manual |
| `report-workflow-health.yml` | Weekly per-workflow success-rate report | Weekly, manual |

---

## Workflow Integration Points

### Before Merging a PR

```
1. Push changes (triggers CI / Router, plus any path-scoped reporting workflows)
2. Wait for the required check:
   gh pr checks --watch
3. Review the Tier 2 advisory PR comment (format, lint, unit tests, ADR compliance)
4. Merge when Router is green (and look at any red reporting check before merging,
   even though it does not block)
```

### After Merge to Master

```
1. CI (Router) runs on master
2. Path-triggered workflows run if their paths changed — deploys:
   deploy-spaarke-ai (dev) / deploy-office-addins; publish-only:
   publish-provisioning-arm-artifacts / build-provisioning-sidecar;
   validate only: deploy-infrastructure:
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
│  ci-router.yml — CI / Router (automatic on PR / push / queue)   │
│                                                                  │
│   classify (path-aware) ──▶ Tier 1 (blocking)                    │
│                         └─▶ Tier 2 (advisory, PR comment)        │
│                                    │                             │
│                                    ▼                             │
│              router-result = "Router" (THE required check)       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ (on master merge)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Path-triggered: deploy-spaarke-ai (dev) + deploy-office-addins │
│  deploy; Bicep paths → validate/what-if + ARM artifact publish. │
│  BFF + everything else: operator-driven / workflow_dispatch —   │
│  see "Deployment Workflows".                                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## Troubleshooting

### Common CI Failures

| Failure | Cause | Fix |
|---------|-------|-----|
| Tier 1 `Compile (Debug)` | Compilation error | Fix code errors locally (`dotnet build`) |
| Tier 1 `Arch Tests (full suite, blocking)` | Architecture rule violation | Run `dotnet test tests/Spaarke.ArchTests/` + `/adr-check` locally |
| Tier 1 `Tenant Isolation (I1–I5 invariants)` | A tenant-isolation ArchTest failed | Read the failing invariant test; never weaken it |
| Tier 1 `Xrm Capability Guard (getXrm AST scan)` | A `getXrm(...)` value is used for a member its requested capability does not cover, or the analyzer reported a blind spot | Read file:line in the log; request the capability the code calls (or rewrite the shape); run `npx jest src/utils/__tests__/xrmCapabilityUsage.guard.test.ts` in `Spaarke.UI.Components` |
| Tier 1 smoke / eval / Compose gates | Changed surface broke an integration path | Read the job log; reproduce with the named test project |
| Tier 2 (any) | Format, lint, unit test, ADR compliance, links, stamps | Advisory — does not block, but fix what the PR comment lists |

### View Detailed Logs

```powershell
# Get run ID from list
gh run list --workflow=ci-router.yml

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

### CI (`ci-router.yml` + both tiers)
No secrets referenced (verified 2026-09-30).

### Deployment Workflows
Each `deploy-*.yml` declares its own secrets / OIDC federation — read the workflow's `env:` and `secrets.*` references rather than relying on a list here.

---

## Integration with Skills

| Skill | CI/CD Integration |
|-------|-------------------|
| `push-to-github` | After push, check `gh pr checks` before merge |
| `adr-check` | Local validation mirrors Tier 1 Arch Tests + Tier 2 ADR Compliance |
| `azure-deploy` | Bicep / Key Vault deployment (manual; `deploy-infrastructure.yml` only validates Bicep — customer stamps deploy via the L2 control plane) |
| `bff-deploy` | BFF deployment (automated counterpart: `deploy-bff-api.yml`, `workflow_dispatch` only) |
| `dataverse-deploy` | Solution / PCF / web resource deployment (no plugins — ADR-002) |
| `code-review` | Local quality gate; CI's closest counterparts are Tier 2 format/lint/unit tests |

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
| Workflow fails in 0-2 seconds with "failed" status | Workflow startup failure — action version doesn't exist in registry (e.g. a major tag one ahead of the action's latest release). See [`FAILURE-MODES.md#G-3`](../../FAILURE-MODES.md#g-3-zero-second-github-actions-workflow-failures-are-startup-failures-not-test-failures) | Look at action version pins FIRST before debugging test logic. `actionlint` (Phase 4b) catches this pre-merge. |
| Merge landed but an expected deploy didn't run | Deploy workflows are path-filtered or `workflow_dispatch`-only; the change didn't touch the filtered paths, or a `workflow_run` chain references a renamed workflow | Inspect the deploy workflow's `on:` block (paths / `workflow_run: workflows: [<name>]`). After renaming any workflow, update every workflow that depends on it. |
| Required-status check failing but not gating the PR | Branch protection allows merge despite failing status (admin-bypass enabled OR check not marked required) | Re-audit required status checks in repo settings. Branch protection bypass during ai-procedure-quality-r1 is acceptable; re-audit at project wrap. |
| Action version is pinned to a major tag (`@v4`) instead of SHA | SHA pinning not enforced in any current workflow (0 of 115 actions are SHA-pinned per Phase 0 inventory) | Phase 4b task 070 introduces SHA pinning; until then, treat major-tag pins as a known security/reproducibility gap. |
