# Specification: Production Release Procedure & Tooling

**Date**: 2026-04-04
**Status**: Draft
**Scope**: Deployment automation — scripts, Claude Code skill, procedure documentation

---

## Problem Statement

Deploying a new Spaarke release to production environments requires an operator to manually:

1. **Know the correct build order** — 3 shared libraries must build before 14 Vite solutions, 5 webpack code pages, 13 PCF controls, and 1 external SPA
2. **Run 10+ deployment scripts** in the right sequence with correct parameters per environment
3. **Repeat for each customer environment** — each environment has its own Dataverse URL, App Service, and Key Vault
4. **Validate each environment** — run health checks, verify env vars, check for dev value leakage
5. **Track what was deployed** — no release versioning or change detection

This is error-prone, slow, and doesn't scale to multiple environments.

### The Goal

**One command to deploy a release.** Open a Claude Code session, say `/deploy-new-release`, and the skill orchestrates the entire process — building, deploying to one or more environments, validating, and tagging.

---

## Requirements

### FR-01: Procedure Document
Create `docs/procedures/production-release.md` as the human-readable source of truth for the release process. Must cover:
- Pre-flight checklist (auth, CI green, code ready)
- Release phases with dependency ordering
- Per-component deployment instructions with exact commands
- Multi-environment deployment flow
- Rollback procedures per component
- Emergency hotfix process
- First-time setup vs. subsequent release differences
- Release versioning strategy

### FR-02: Build Orchestrator Script
Create `scripts/Build-AllClientComponents.ps1` that builds all client-side components in correct dependency order:
1. Shared libraries (`@spaarke/auth`, `@spaarke/sdap-client`, `@spaarke/ui-components`)
2. Vite-based solutions (14 solutions in `src/solutions/`)
3. Webpack-based code pages (5 in `src/client/code-pages/`)
4. PCF controls (13 in `src/client/pcf/`)
5. External SPA (`src/client/external-spa/`)

Must support:
- `-WhatIf` to preview without building
- `-SkipSharedLibs` if libs haven't changed
- `-Component` to build only specific components
- Clear progress output with timing per component
- Exit with non-zero on any build failure

### FR-03: Web Resource Deploy Orchestrator Script
Create `scripts/Deploy-AllWebResources.ps1` that deploys all web resources to a single Dataverse environment. Calls existing individual deploy scripts:
- `Deploy-CorporateWorkspace.ps1`
- `Deploy-ExternalWorkspaceSpa.ps1`
- `Deploy-SpeAdminApp.ps1`
- `Deploy-WizardCodePages.ps1`
- `Deploy-EventsPage.ps1`
- `Deploy-PCFWebResources.ps1`
- `Deploy-RibbonIcons.ps1`

Must support:
- `-DataverseUrl` parameter (required)
- `-WhatIf` to preview
- `-SkipComponent` to exclude specific components
- Summary report of successes/failures

### FR-04: Master Release Orchestrator Script
Create `scripts/Deploy-Release.ps1` that orchestrates the full release:
1. Pre-flight checks (git status clean, CI green, auth configured)
2. Build phase (calls `Build-AllClientComponents.ps1`)
3. Per-environment deployment loop:
   - Deploy BFF API (calls `Deploy-BffApi.ps1`)
   - Deploy Dataverse solutions (calls `Deploy-DataverseSolutions.ps1`)
   - Deploy web resources (calls `Deploy-AllWebResources.ps1`)
   - Validate (calls `Validate-DeployedEnvironment.ps1`)
4. Tag release (git tag)

Must support:
- `-EnvironmentUrl` (single string or array of strings)
- `-WhatIf` to preview all phases
- `-SkipPhase` to skip specific phases (Build, BffApi, Solutions, WebResources)
- `-SkipBuild` shortcut for when builds are already done
- `-Version` for release tag (auto-suggests if omitted)
- Sequential per-environment deployment with stop-on-failure option

### FR-05: Environment Registry
Create an environment configuration that maps Dataverse URLs to their associated Azure resources:
```json
{
  "environments": {
    "demo": {
      "dataverseUrl": "https://spaarke-demo.crm.dynamics.com",
      "bffApiUrl": "https://api.spaarke.com",
      "appServiceName": "spaarke-bff-prod",
      "resourceGroup": "rg-spaarke-platform-prod",
      "keyVaultName": "sprk-platform-prod-kv"
    }
  }
}
```

### FR-06: Claude Code Skill
Create `.claude/skills/deploy-new-release/SKILL.md` invocable via `/deploy-new-release`. The skill:
1. Runs pre-flight checks interactively
2. Prompts for target environment(s) from registry
3. Detects changes since last release tag
4. Confirms deployment plan with operator
5. Executes `Deploy-Release.ps1` with selected parameters
6. Reports results and prompts for release tag

### FR-07: Reuse Existing Scripts
All new orchestrator scripts MUST call existing deployment scripts rather than reimplementing their logic. The existing scripts are proven and handle edge cases (encoding, auth, retries, etc.).

### FR-08: Alignment with Customer Onboarding
- `Deploy-Release.ps1` shares the same sub-scripts as `Provision-Customer.ps1`
- Clear distinction: `Provision-Customer.ps1` = new environment (creates infra), `Deploy-Release.ps1` = update existing (skips infra)
- Procedure doc cross-references `CUSTOMER-DEPLOYMENT-GUIDE.md` and `CUSTOMER-ONBOARDING-RUNBOOK.md`

### FR-09: Release Versioning
- Git tags in `v{major}.{minor}.{patch}` format
- Tags created after successful deployment and validation
- Tags enable change detection: `git diff <last-tag>..HEAD --name-only`
- Procedure includes versioning strategy section

---

## Non-Requirements (Explicit Exclusions)

- **No Azure DevOps pipelines** — stay with GitHub Actions + PowerShell
- **No parallel environment deployment** — sequential for safety
- **No infrastructure provisioning** — that's `Provision-Customer.ps1`'s job
- **No new CI/CD workflows** — leverage existing GitHub Actions where possible

---

## Success Criteria

1. Operator can deploy a release to `https://spaarke-demo.crm.dynamics.com` by running `/deploy-new-release` in Claude Code
2. All existing deployment scripts are reused (not reimplemented)
3. `Validate-DeployedEnvironment.ps1` passes after deployment
4. Multi-environment deployment works (deploy to 2+ environments sequentially)
5. Rollback procedures are documented and tested for each component
6. Procedure document is complete and cross-references existing customer docs

---

## Technical Approach

### Release Phase Diagram
```
Phase 0: Pre-flight (git clean, CI green, auth)     --- GATE --->
Phase 1: Build (shared libs -> all client components) --- GATE --->
Phase 2A: BFF API (Deploy-BffApi.ps1)        |
Phase 2B: Dataverse Solutions (Deploy-DS.ps1) |--- PARALLEL --- GATE --->
Phase 3:  Web Resources (Deploy-AllWR.ps1)    |
Phase 4:  Validation (Validate-DE.ps1)                --- GATE --->
Phase 5:  Tag release (git tag)                        --- COMPLETE
```

### Existing Scripts to Reuse
| Script | Called By | Purpose |
|--------|----------|---------|
| `Deploy-BffApi.ps1` | `Deploy-Release.ps1` | BFF API to App Service |
| `Deploy-DataverseSolutions.ps1` | `Deploy-Release.ps1` | 10 solutions in dependency order |
| `Deploy-CorporateWorkspace.ps1` | `Deploy-AllWebResources.ps1` | Corporate workspace HTML |
| `Deploy-ExternalWorkspaceSpa.ps1` | `Deploy-AllWebResources.ps1` | External SPA |
| `Deploy-SpeAdminApp.ps1` | `Deploy-AllWebResources.ps1` | SPE Admin app |
| `Deploy-WizardCodePages.ps1` | `Deploy-AllWebResources.ps1` | Batch wizard deploy |
| `Deploy-EventsPage.ps1` | `Deploy-AllWebResources.ps1` | Events page |
| `Deploy-PCFWebResources.ps1` | `Deploy-AllWebResources.ps1` | PCF control bundles |
| `Deploy-RibbonIcons.ps1` | `Deploy-AllWebResources.ps1` | Ribbon icons |
| `Configure-ProductionAppSettings.ps1` | `Deploy-Release.ps1` (if needed) | Key Vault references |
| `Validate-DeployedEnvironment.ps1` | `Deploy-Release.ps1` | Post-deploy validation |
