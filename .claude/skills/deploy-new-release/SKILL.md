---
description: Interactive production release deployment — pre-flight, environment selection, change detection, confirmation gate, script execution, and release tagging
tags: [deploy, release, production, operations]
techStack: [powershell, azure, dataverse, pac-cli]
appliesTo: ["scripts/Deploy-Release.ps1", "config/environments.json", "deploy new release", "production release", "deploy release"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-10-11
---

# Deploy New Release

> **Category**: Operations
> **Last Reviewed**: 2026-10-11 (mandatory `-CustomerId`, per-environment coverage, route master→dev to `/master-deploy`)
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A — **leave-alone**: cleanest critical-rules structure in batch; BFF URL gotcha well-surfaced)
> **Exemplar rationale**: Interactive UX patterns change frequently; the BFF URL host-only rule is the stable contract (recurring production issue prevented).
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
  [PASS] PAC CLI: connected to spaarkedev1.crm.dynamics.com
  [PASS] BFF URLs validated (1 environment, host-only)

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
```

> **Customer stamps are not targets of this skill.** The old Demo entry was retired 2026-10-14 (D35); the demo is now
> the ACME Corporation stamp. Every customer stamp (ACME included) is upgraded by re-running provisioning, which
> re-imports the published SpaarkeMaster through H6 (`docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md` §5).

#### Operator Selects Environments

Ask the operator to select one or more environments by number or name. Examples of valid responses:
- `1` or `dev` — deploy to Development only
- `1, 2` or `dev, <other>` — deploy to several (sequentially, dev first), when `config/environments.json` lists more than one
- `all` — deploy to all environments

#### Confirm Selection

After the operator selects, confirm:

```
You selected: Development
Deployment order: Development (sequential, lowest risk first)
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
  Environments: Development
  Phases:
    Phase 1: Build all client components
    Phase 2: Deploy BFF API (per environment)
    Phase 2b: Apply and verify the worklist grid configuration (per environment; only after a successful Phase 2; blocks Phase 3 if it fails)
    Phase 3: Import SpaarkeMaster — the CI-published package, typed per environment (none on dev)
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

**Phase skipping**: If the operator wants to skip phases, ask which ones. Valid skip values: `Build`, `BffApi`, `OntologyConfig`, `Solutions`, `WebResources`, `Validation`. Skipping `OntologyConfig` does not block Phase 3. A failed BFF or a failed Phase 2b always blocks Phase 3, even with `-StopOnFailure $false`.

Wait for the operator to type **"yes"** or **"proceed"**. Any other response (including "y") should prompt for clarification.

---

### Step 5: Execute Deployment

Call `Deploy-Release.ps1` with the operator's selections.

#### Build the Command

`-CustomerId` is **mandatory** (the script fails before any side effect without it). It selects the Phase 4 (web resources) target from the `sprk_dataverseenvironment` registry, independently of `-EnvironmentUrl`. Resolve it before building the command:

- A customer environment: the row's `sprk_customerid` (pattern `^[a-z][a-z0-9]{2,7}$`).
- A platform row (no `sprk_customerid`): a value equal to its `sprk_name` or `sprk_envaccountdomain` (case-insensitive). **Dev:** `-CustomerId dev` matches the row named `Dev` → `spaarkedev1` (verified 2026-10-11).
- The old `Demo 1` row (inactive since the old demo was retired 2026-10-14) cannot be resolved by id: its name has a space and its domain contains dots (#1670). Customer stamps resolve by `sprk_customerid` and are upgraded through provisioning, not this skill.

```powershell
# Single environment
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "dev" `
    -CustomerId "dev" `
    -Version "v1.1.0"

# With skipped phases
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "dev" `
    -CustomerId "dev" `
    -Version "v1.1.0" `
    -SkipPhase "Build"

# Preview mode (WhatIf) — skips the registry lookup, so it does NOT prove -CustomerId resolves
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "dev" `
    -CustomerId "dev" `
    -Version "v1.1.0" `
    -WhatIf
```

One run deploys web resources to **one** customer environment (the `-CustomerId` target), even when `-EnvironmentUrl` lists several; run once per environment.

#### What a release run covers (and what it does not)

| Phase | Dev (`solutionPackageType: none`) | Other environments |
|---|---|---|
| 1 Build | `Build-AllClientComponents.ps1` | same |
| 2 BFF API | `Deploy-BffApi.ps1` | same |
| 3 Solutions | **skipped** — dev is the authoring environment | SpaarkeMaster import (PCF controls, ribbons, forms, the packaged web-resource copies) |
| 4 Web resources | `Deploy-AllWebResources.ps1`: SpeAdmin, the `Deploy-WizardCodePages.ps1` set, EventsPage, ribbon icons — **only** | same |
| 6 Tag | creates **and pushes** a git tag | same |

So on dev a release run does **not** deploy `sprk_spaarkeai`, DailyBriefing, SmartTodo, NavigatorPane, the Invoices/KPI/Communications pages, Notepad, the conversation page, the webpack code pages (DocumentRelationshipViewer, SemanticSearch, PlaybookBuilder), classic JS web resources, or PCF controls — and it pushes a release tag. To bring **everything on master** to dev, use `/master-deploy` instead (see the Decision Tree).

#### Show the Exact Command Before Running

Always show the operator the exact PowerShell command that will be executed:

```
Executing:

  pwsh -File scripts/Deploy-Release.ps1 \
    -EnvironmentUrl "dev" \
    -CustomerId "dev" \
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
| Phase 3 (Solutions) | SpaarkeMaster is never downgraded — roll forward (fix in dev, higher version, export, CI publish, re-run). A **refused** import (type switch / downgrade) changed nothing. See `docs/procedures/production-release.md` "Dataverse Solution Rollback". |
| Phase 4 (Web Resources) | `dist/` is not committed, so rebuild from the previous tag in a separate worktree (`git worktree add --detach <path> <tag>`), then re-run the web resource scripts from there. Faster: before deploying, download each target's current content (`webresourceset?$select=name,content`) as a restore point and re-upload it. |
| Phase 5 (Validation) | Validation failure means something is misconfigured. Check the validation output for specifics. Do NOT tag the release. |

---

### Step 6: Post-Deployment (Interactive)

After `Deploy-Release.ps1` completes, report results.

#### If All Environments Succeeded

```
DEPLOYMENT COMPLETE

  v1.1.0 deployed successfully to all environments:
    Development .... OK (3m 42s)

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
  <env> .......... FAILED (Phase 3: Solution import)

  Release tag was NOT created due to failures.

  Next steps:
    1. Review the error output above
    2. Fix the issue in the failed environment
    3. Re-run deployment for that environment only:
       .\scripts\Deploy-Release.ps1 -EnvironmentUrl <env> -CustomerId <registry-id> -Version v1.1.0 -SkipBuild
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
├── Bring EVERYTHING on master to dev (after several PRs merged)?
│   └── YES → Use /master-deploy (covers every code page, JS web resource and the BFF,
│             with a clobber check; a release run on dev covers only part — see above)
└── Is this a dev iteration deploy?
    └── YES → Use the component-specific deploy script directly
```

---

## Parameters for Deploy-Release.ps1

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `-EnvironmentUrl` | string[] | (required) | Environment names or Dataverse URLs |
| `-CustomerId` | string | **(required)** | Phase 4 target via the `sprk_dataverseenvironment` registry; `^[a-z][a-z0-9]{2,7}$`. Dev = `dev`. See Step 5 |
| `-AdminEnvironmentUrl` | string | `$env:SPAARKE_ADMIN_DATAVERSE_URL`, else the registry's platform-admin entry, else the first non-template entry (dev) | Where the registry lives |
| `-Version` | string | auto-suggested | `v{major}.{minor}.{patch}` format |
| `-SkipPhase` | string[] | none | `Build`, `BffApi`, `OntologyConfig`, `Solutions`, `WebResources`, `Validation` |
| `-SkipBuild` | switch | $false | Shortcut for `-SkipPhase Build` |
| `-StopOnFailure` | bool | $true | Stop remaining environments on failure |
| `-ClientSecret` | string | (none) | **Ignored since T218f (2026-10-08)** — prints a warning. Phase 3 imports SpaarkeMaster with the operator's own az/pac sign-in; no service principal secret is used anywhere in the release. Kept so older invocations do not break. |
| `-WhatIf` | switch | $false | Preview without executing |

---

## Environment Registry Format

Environments are defined in `config/environments.json` (read it at runtime; this excerpt shows the shape and the dev values as of 2026-10-11):

```json
{
  "environments": {
    "dev": {
      "solutionPackageType": "none",
      "displayName": "Development",
      "dataverseUrl": "https://spaarkedev1.crm.dynamics.com",
      "bffApiUrl": "https://spaarke-bff-dev.azurewebsites.net",
      "appServiceName": "spaarke-bff-dev",
      "appServiceSlot": "staging",
      "resourceGroup": "rg-spaarke-dev",
      "keyVaultName": "spaarke-spekvcert",
      "tenantId": "a221a95e-..."
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
| Build fails | Missing dependencies or compilation error | Check build output; run `npm install --legacy-peer-deps --no-audit --no-fund` in the affected directory (not `npm ci` — most lock files are stale) |
| `Cannot validate argument on parameter 'CustomerId'` (pattern) / prompt or error for missing mandatory parameter `CustomerId` | `-CustomerId` omitted or fails `^[a-z][a-z0-9]{2,7}$` | Pass the registry id (dev = `dev`); see Step 5 |
| `Registry lookup found NO sprk_dataverseenvironment row` | No registry row matches the id (e.g. the inactive `Demo 1` row) | Fix the registry row (give it a valid `sprk_customerid`); never hard-code a target in the script |
| BFF deploy fails | App Service not found or auth issue | Verify `appServiceName` in environments.json matches Azure |
| SpaarkeMaster import refused or fails | Type switch / downgrade refused; no published package; no access | The message names the refusal. "no solutions.SpaarkeMaster entry" = nothing published yet (run `publish-dataverse-solutions-manifest.yml`); a refusal = fix `solutionPackageType` or publish a higher version; access = Storage Blob Data Reader on `sprkcpartifacts{env}` and pac signed in to the target (your own identity — no service principal) |
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
| `master-deploy` | Bringing everything on master to dev in one pass |
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
- **If the operator says "just deploy to demo"** — the demo is the ACME stamp: upgrade it by re-running provisioning (H6), not this skill. For any environment this skill does deploy, still run pre-flight checks.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| BFF URL configured with `/api` suffix — production breaks at runtime | Operator entered `https://...azurewebsites.net/api` instead of host-only | **Recurring production issue.** Pre-flight check MUST validate BFF URL is host-only. See `## Critical Rules` MUST/NEVER list — the #1 NEVER rule. If detected post-deploy: stop, fix the env var, redeploy. |
| Half-deployed release — BFF deployed but solution didn't import | Operator interrupted between phases, OR one phase failed silently | Each phase MUST report ✅/❌ explicitly. If a phase fails, the skill STOPS — operator decides rollback vs continue. Never half-complete silently. |
| Tag created on wrong commit | Tag step ran before all phases verified | Tag is the FINAL step. If any earlier phase failed, the tag is never created. Recovery: investigate failure, then re-run skill from beginning. |
| Parallel deployment attempted ("deploy to all 3 environments simultaneously") | Operator unfamiliar with the sequential constraint | Skill rejects parallel deployment requests. Always one environment at a time — explicit Conventions rule. |
