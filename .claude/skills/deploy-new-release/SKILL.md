---
description: Interactive production release deployment — pre-flight, environment selection, change detection, confirmation gate, script execution, and release tagging
tags: [deploy, release, production, operations]
techStack: [powershell, azure, dataverse, pac-cli]
appliesTo: ["scripts/Deploy-Release.ps1", "config/environments.json", "deploy new release", "production release", "deploy release"]
alwaysApply: false
---

# Deploy New Release

> **Category**: Operations
> **Last Updated**: April 2026
> **Procedure**: [`docs/procedures/production-release.md`](../../../docs/procedures/production-release.md)

Interactive Claude Code skill for deploying a Spaarke release to one or more production environments. This skill provides the **operator UX layer** — environment selection, confirmation gates, change reporting, and progress monitoring. The actual deployment is performed by `scripts/Deploy-Release.ps1` and its sub-scripts.

---

## Quick Reference

| Item | Value |
|------|-------|
| Release Script | `scripts/Deploy-Release.ps1` |
| Environment Registry | `config/environments.json` |
| Full Procedure | `docs/procedures/production-release.md` |
| Build Script | `scripts/Build-AllClientComponents.ps1` |
| Validation Script | `scripts/Validate-DeployedEnvironment.ps1` |

---

## Critical Rules

### MUST:
- **MUST** run all pre-flight checks before presenting deployment options
- **MUST** present environment details from `config/environments.json` for operator selection
- **MUST** show change summary since last git tag before deploying
- **MUST** require explicit operator confirmation ("yes" or "proceed") before executing any deployment
- **MUST** call `Deploy-Release.ps1` for actual deployment — never reimplement deployment logic
- **MUST** validate BFF API URLs are host-only (no `/api` suffix) during pre-flight
- **MUST** report results per environment after deployment completes

### NEVER:
- **NEVER** deploy without operator confirmation — the confirmation gate is mandatory
- **NEVER** reimplement logic from `Deploy-Release.ps1` or its sub-scripts
- **NEVER** deploy to multiple environments in parallel — always sequential
- **NEVER** skip BFF URL validation — `/api` suffix causes silent failures in production
- **NEVER** create a release tag if any environment deployment failed

---

## Procedure

### Step 1: Pre-Flight Checks (Automated)

Run these checks automatically and report results to the operator. **Any failure is a hard stop.**

```bash
# 1a. Git working tree must be clean
git status --porcelain
# Expected: no output (clean)

# 1b. Check current branch
git rev-parse --abbrev-ref HEAD
# Expected: master, main, or release/*

# 1c. Azure CLI authenticated
az account show --query "{name:name, tenantId:tenantId}" -o table

# 1d. PAC CLI authenticated
pac org who
```

#### 1e. BFF URL Validation

Read `config/environments.json` and validate that **every** `bffApiUrl` value is host-only:

```
OK:    https://spe-api-dev-67e2xz.azurewebsites.net
OK:    https://api.spaarke.com
FAIL:  https://api.spaarke.com/api        <-- MUST NOT end with /api
FAIL:  https://api.spaarke.com/           <-- MUST NOT have trailing slash
```

#### Report to Operator

Present pre-flight results in a clear summary:

```
PRE-FLIGHT CHECKS
  [PASS] Git working tree is clean
  [PASS] On branch: master
  [PASS] Azure CLI: ralph@spaarke.com (tenant: a221a95e-...)
  [PASS] PAC CLI: connected to spaarke-demo.crm.dynamics.com
  [PASS] BFF URLs validated (2 environments, all host-only)

All pre-flight checks passed. Proceeding to environment selection.
```

If any check fails, report the failure clearly and **stop**. Do not proceed to Step 2.

```
PRE-FLIGHT CHECKS
  [PASS] Git working tree is clean
  [FAIL] Azure CLI not authenticated — run 'az login' first
  [PASS] PAC CLI: connected

Pre-flight failed. Fix the issues above before continuing.
```

---

### Step 2: Environment Selection (Interactive)

Read `config/environments.json` and present the available environments. Exclude the `_template` entry.

#### Present Available Environments

```
AVAILABLE ENVIRONMENTS (from config/environments.json)

  [1] Development
      Dataverse: https://spaarkedev1.crm.dynamics.com
      BFF API:   https://spe-api-dev-67e2xz.azurewebsites.net
      App Svc:   spe-api-dev-67e2xz (spe-infrastructure-westus2)

  [2] Demo
      Dataverse: https://spaarke-demo.crm.dynamics.com
      BFF API:   https://api.spaarke.com
      App Svc:   spaarke-bff-prod (rg-spaarke-platform-prod)
```

#### Operator Selects Environments

Ask the operator to select one or more environments by number or name. Examples of valid responses:
- `1` or `dev` — deploy to Development only
- `2` or `demo` — deploy to Demo only
- `1, 2` or `dev, demo` — deploy to both (sequentially, dev first)
- `all` — deploy to all environments

#### Confirm Selection

After the operator selects, confirm:

```
You selected: Development, Demo
Deployment order: Development -> Demo (sequential, lowest risk first)
Is this correct? (yes/no)
```

Wait for explicit confirmation before proceeding.

---

### Step 3: Change Detection (Automated)

Detect what changed since the last release tag and present a summary.

```bash
# Find the last release tag
git tag --sort=-v:refname | head -1

# If a tag exists, show changes since that tag
git diff <last-tag>..HEAD --stat

# Categorize changes by component area
git diff <last-tag>..HEAD --name-only -- src/server/api/        # BFF API
git diff <last-tag>..HEAD --name-only -- src/client/            # Client (PCF, code pages, shared)
git diff <last-tag>..HEAD --name-only -- src/solutions/         # Vite solutions
git diff <last-tag>..HEAD --name-only -- src/client/external-spa/  # External SPA
git diff <last-tag>..HEAD --name-only -- scripts/               # Deployment scripts
git diff <last-tag>..HEAD --name-only -- docs/                  # Documentation
git diff <last-tag>..HEAD --name-only -- config/                # Configuration
```

If no tags exist, note this is the first release and all files are new.

#### Present Change Summary

```
CHANGES SINCE v1.0.0 (last release)

  BFF API (src/server/api/):     12 files changed
  Client Components:             34 files changed
    - PCF Controls:               8 files
    - Code Pages:                14 files
    - Shared Libraries:          12 files
  Vite Solutions (src/solutions/): 18 files changed
  External SPA:                   3 files changed
  Scripts:                        5 files changed
  Documentation:                  7 files changed

  Total: 79 files changed, 2,431 insertions, 847 deletions

Components that will be deployed:
  - BFF API ............. YES (12 files changed)
  - Dataverse Solutions . YES (solution files changed)
  - Web Resources ....... YES (client + solutions changed)
```

---

### Step 4: Deployment Plan Confirmation (GATE)

Present the full deployment plan and require explicit approval. **Nothing deploys until the operator confirms.**

```
DEPLOYMENT PLAN

  Version:      v1.1.0 (auto-suggested from last tag v1.0.0)
  Environments: Development -> Demo (sequential)
  Phases:
    Phase 1: Build all client components
    Phase 2: Deploy BFF API (per environment)
    Phase 3: Import Dataverse solutions (per environment)
    Phase 4: Deploy web resources (per environment)
    Phase 5: Validate deployment (per environment)
    Phase 6: Tag release v1.1.0

  Estimated time: ~10-15 minutes per environment

  Proceed with deployment? (yes/no)
```

**Version suggestion logic**:
- If the last tag is `v1.0.0`, suggest `v1.0.1` (patch increment)
- The operator can accept or provide a different version
- Version must match `v{major}.{minor}.{patch}` format

**Phase skipping**: If the operator wants to skip phases, ask which ones. Valid skip values: `Build`, `BffApi`, `Solutions`, `WebResources`, `Validation`.

Wait for the operator to type **"yes"** or **"proceed"**. Any other response (including "y") should prompt for clarification.

---

### Step 5: Execute Deployment

Call `Deploy-Release.ps1` with the operator's selections.

#### Build the Command

```powershell
# Single environment
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "dev" `
    -Version "v1.1.0"

# Multiple environments
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "dev", "demo" `
    -Version "v1.1.0"

# With skipped phases
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "demo" `
    -Version "v1.1.0" `
    -SkipPhase "Build"

# Preview mode (WhatIf)
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "demo" `
    -Version "v1.1.0" `
    -WhatIf
```

#### Show the Exact Command Before Running

Always show the operator the exact PowerShell command that will be executed:

```
Executing:

  pwsh -File scripts/Deploy-Release.ps1 \
    -EnvironmentUrl "dev","demo" \
    -Version "v1.1.0"

Starting deployment...
```

#### Monitor and Report

- Stream script output to the operator
- After each phase completes, briefly note the result
- If a phase fails, report the error and the script's built-in behavior (StopOnFailure)

#### Error Handling

If `Deploy-Release.ps1` reports a failure:

1. **Show the error output** clearly
2. **Report which phase and environment failed**
3. **Provide rollback guidance** based on the phase that failed:

| Failed Phase | Rollback Guidance |
|-------------|-------------------|
| Phase 1 (Build) | No rollback needed — nothing was deployed. Fix the build error and re-run. |
| Phase 2 (BFF API) | If `-UseSlotDeploy` was used, the script auto-rolls back. Otherwise: `az webapp deployment slot swap` to swap back. |
| Phase 3 (Solutions) | Re-import previous solution version from last git tag. See `docs/procedures/production-release.md` Rollback section. |
| Phase 4 (Web Resources) | Re-deploy from previous tag: `git checkout <tag> -- src/solutions/*/dist/` then re-run web resource scripts. |
| Phase 5 (Validation) | Validation failure means something is misconfigured. Check the validation output for specifics. Do NOT tag the release. |

---

### Step 6: Post-Deployment (Interactive)

After `Deploy-Release.ps1` completes, report results.

#### If All Environments Succeeded

```
DEPLOYMENT COMPLETE

  v1.1.0 deployed successfully to all environments:
    Development .... OK (3m 42s)
    Demo ........... OK (4m 18s)

  Release tag v1.1.0 has been created and pushed to origin.

  Post-deployment checklist:
    [ ] Verify corporate workspace loads in each environment
    [ ] Verify document upload wizard connects to BFF
    [ ] Run a test AI analysis
    [ ] Check SPE Admin dashboard
    [ ] Notify stakeholders of the release
```

The script creates and pushes the git tag automatically (Phase 6). Report that the tag was created.

#### If Any Environment Failed

```
DEPLOYMENT COMPLETED WITH FAILURES

  Development .... OK (3m 42s)
  Demo ........... FAILED (Phase 3: Solution import)

  Release tag was NOT created due to failures.

  Next steps:
    1. Review the error output above
    2. Fix the issue in the Demo environment
    3. Re-run deployment for Demo only:
       .\scripts\Deploy-Release.ps1 -EnvironmentUrl demo -Version v1.1.0 -SkipBuild
    4. Once all environments pass, manually tag:
       git tag -a v1.1.0 -m "Release v1.1.0"
       git push origin v1.1.0
```

---

## Decision Tree: When to Use This Skill

```
Is this a full production release to existing environments?
├── YES → Use this skill (/deploy-new-release)
├── Is this a new customer environment setup?
│   └── YES → Use Provision-Customer.ps1 (see CUSTOMER-ONBOARDING-RUNBOOK.md)
├── Is this an emergency hotfix?
│   └── YES → See "Emergency Hotfix Procedure" in production-release.md
├── Is this a single-component deploy (just API, just solutions)?
│   └── YES → Use the component-specific skill (bff-deploy, dataverse-deploy, etc.)
└── Is this a dev iteration deploy?
    └── YES → Use the component-specific deploy script directly
```

---

## Parameters for Deploy-Release.ps1

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `-EnvironmentUrl` | string[] | (required) | Environment names or Dataverse URLs |
| `-Version` | string | auto-suggested | `v{major}.{minor}.{patch}` format |
| `-SkipPhase` | string[] | none | `Build`, `BffApi`, `Solutions`, `WebResources`, `Validation` |
| `-SkipBuild` | switch | $false | Shortcut for `-SkipPhase Build` |
| `-StopOnFailure` | bool | $true | Stop remaining environments on failure |
| `-ClientSecret` | string | from env var | Service principal secret for Dataverse |
| `-WhatIf` | switch | $false | Preview without executing |

---

## Environment Registry Format

Environments are defined in `config/environments.json`:

```json
{
  "environments": {
    "dev": {
      "displayName": "Development",
      "dataverseUrl": "https://spaarkedev1.crm.dynamics.com",
      "bffApiUrl": "https://spe-api-dev-67e2xz.azurewebsites.net",
      "appServiceName": "spe-api-dev-67e2xz",
      "appServiceSlot": "staging",
      "resourceGroup": "spe-infrastructure-westus2",
      "keyVaultName": "spaarke-spekvcert",
      "tenantId": "a221a95e-...",
      "servicePrincipal": { "clientId": "..." }
    }
  }
}
```

**Key rule**: `bffApiUrl` must be **host-only** — no `/api` suffix, no trailing slash. The client code appends `/api/...` at runtime. See `docs/procedures/production-release.md` Phase 0.5 for details.

---

## Troubleshooting

| Issue | Cause | Resolution |
|-------|-------|------------|
| Pre-flight: Azure CLI not authenticated | Session expired | Run `az login` and retry |
| Pre-flight: PAC CLI not authenticated | No auth profile | Run `pac auth create --environment <url>` |
| Pre-flight: BFF URL ends with `/api` | Misconfigured registry | Edit `config/environments.json` to remove `/api` suffix |
| Build fails | Missing dependencies or compilation error | Check build output; run `npm ci` in affected directory |
| BFF deploy fails | App Service not found or auth issue | Verify `appServiceName` in environments.json matches Azure |
| Solution import fails | Missing dependency or auth | Check import order; verify service principal has permissions |
| Web resource deploy fails | Auth token expired or wrong URL | Re-run `az login`; verify `dataverseUrl` |
| Validation fails | Environment variables misconfigured | Check 7 required Dataverse env vars (see production-release.md Phase 5) |
| Tag creation fails | Tag already exists | Delete existing tag: `git tag -d <tag>; git push origin :refs/tags/<tag>` |
| `Deploy-Release.ps1` not found | Wrong working directory | Run from repository root |

---

## Related Skills

| Skill | When to Use Instead |
|-------|---------------------|
| `bff-deploy` | Deploying only the BFF API (no solutions or web resources) |
| `dataverse-deploy` | Deploying only Dataverse solutions or PCF controls |
| `azure-deploy` | Azure infrastructure changes (Bicep), App Settings, Key Vault |
| `code-page-deploy` | Deploying a single code page web resource |
| `power-page-deploy` | Deploying the external SPA web resource |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| `docs/procedures/production-release.md` | Full procedure this skill wraps |
| `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` | Customer-side deployment reference |
| `docs/guides/CUSTOMER-ONBOARDING-RUNBOOK.md` | New environment provisioning |
| `docs/guides/INCIDENT-RESPONSE.md` | Production troubleshooting |

---

## Tips for AI

- **This skill is interactive** — every step requires either automated checks or operator input. Never skip the confirmation gate (Step 4).
- **Read `config/environments.json` at runtime** — do not hard-code environment details. The registry is the source of truth.
- **Show the exact command** before executing `Deploy-Release.ps1`. The operator must see what will run.
- **BFF URL validation is critical** — this is a recurring production issue. Always check during pre-flight.
- **Change detection helps the operator decide** — summarize changes by component area so the operator knows what is being deployed and can decide if phases should be skipped.
- **Sequential deployment** — environments are always deployed one at a time. Never attempt parallel deployment.
- **The script handles rollback** for BFF API (slot swap). For solutions and web resources, provide manual rollback guidance referencing the procedure document.
- **Version suggestion** — auto-suggest patch increment from the last tag. Let the operator override if they want minor or major.
- **If the operator says "just deploy to demo"** — still run pre-flight checks. The checks protect against silent failures.
