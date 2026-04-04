# Production Release Procedure

> **Last Updated**: 2026-04-04
> **Owner**: Platform Operations
> **Scope**: Deploying Spaarke updates to existing production environments
>
> **Related Documents**:
> - [CUSTOMER-DEPLOYMENT-GUIDE.md](../guides/CUSTOMER-DEPLOYMENT-GUIDE.md) — Customer-side deployment
> - [CUSTOMER-ONBOARDING-RUNBOOK.md](../guides/CUSTOMER-ONBOARDING-RUNBOOK.md) — New environment provisioning
> - [PRODUCTION-DEPLOYMENT-GUIDE.md](../guides/PRODUCTION-DEPLOYMENT-GUIDE.md) — First-time platform setup
> - [INCIDENT-RESPONSE.md](../guides/INCIDENT-RESPONSE.md) — Production troubleshooting

---

## Overview

This procedure deploys a Spaarke release to one or more **existing** production environments. It covers building all client components, deploying the BFF API, importing Dataverse solutions, deploying web resources, validating the deployment, and tagging the release.

**Key Distinction**:
- `Provision-Customer.ps1` = **new environment** (creates infrastructure, Dataverse org, etc.)
- `Deploy-Release.ps1` = **update existing** (deploys code and solutions to environments that already exist)

Both share the same sub-scripts for solution import, web resource deployment, and validation.

### Release Phase Diagram

```
Phase 0: Pre-flight (git clean, CI green, auth configured)
         │
         ▼ ─── GATE ───
Phase 1: Build (shared libs → Vite solutions → code pages → PCF → external SPA)
         │
         ▼ ─── GATE ───
         ┌─────────────────────────────────────────────┐
         │  FOR EACH target environment (sequential):  │
         │                                             │
         │  Phase 2: BFF API (Deploy-BffApi.ps1)       │
         │           │                                 │
         │           ▼                                 │
         │  Phase 3: Dataverse Solutions               │
         │           (Deploy-DataverseSolutions.ps1)   │
         │           │                                 │
         │           ▼                                 │
         │  Phase 4: Web Resources                     │
         │           (Deploy-AllWebResources.ps1)      │
         │           │                                 │
         │           ▼                                 │
         │  Phase 5: Validation                        │
         │           (Validate-DeployedEnvironment.ps1)│
         │                                             │
         └─────────────────────────────────────────────┘
         │
         ▼ ─── GATE ───
Phase 6: Tag release (git tag v{major}.{minor}.{patch})
```

**Sequential per-environment**: Environments are deployed one at a time. Never deploy to multiple environments in parallel — this ensures each deployment is verified before proceeding.

---

## Phase 0: Pre-Flight Checklist

Complete **all** checks before starting a release. Any failure is a hard stop.

### 0.1 Tool Prerequisites

| Tool | Minimum Version | Verify Command |
|------|-----------------|----------------|
| Azure CLI | 2.60+ | `az --version` |
| PowerShell | 7.4+ | `pwsh --version` |
| .NET SDK | 8.0+ | `dotnet --version` |
| Node.js | 18+ | `node --version` |
| PAC CLI | Latest | `pac --version` |
| GitHub CLI | Latest | `gh --version` |

### 0.2 Authentication

```powershell
# Azure CLI — must be logged in to production subscription
az login
az account show  # Verify correct subscription

# PAC CLI — authenticate with service principal
pac auth create --environment "https://spaarke-demo.crm.dynamics.com" `
    --tenant "a221a95e-..." `
    --applicationId "..." `
    --clientSecret "..."

# Verify PAC auth
pac org who
```

### 0.3 Git Status

```powershell
# Working directory must be clean
git status  # Should show "nothing to commit, working tree clean"

# Must be on master or release branch
git branch --show-current

# All changes committed and pushed
git log origin/master..HEAD  # Should be empty if on master
```

### 0.4 CI Pipeline

```powershell
# Verify latest CI run passed
gh run list --limit 5 --branch master

# Check specific run
gh run view <run-id>
```

### 0.5 BFF URL Validation

**CRITICAL**: The BFF API URL stored in Dataverse environment variables and used by client components must be **host-only** — no `/api` suffix.

```
✅ Correct:  https://api.spaarke.com
❌ Wrong:    https://api.spaarke.com/api
❌ Wrong:    https://api.spaarke.com/
```

The client code appends `/api/...` when making requests. If the base URL already contains `/api`, requests go to `/api/api/...` and fail silently.

**Validation**: Check `config/environments.json` — all `bffApiUrl` values must be host-only.

### 0.6 Change Detection (Optional)

If a previous release was tagged, review what changed:

```powershell
# Find last release tag
git tag --sort=-v:refname | head -5

# See changes since last release
git diff v1.0.0..HEAD --stat
git diff v1.0.0..HEAD --name-only

# Changes by component area
git diff v1.0.0..HEAD --name-only -- src/server/api/     # BFF API changes
git diff v1.0.0..HEAD --name-only -- src/client/          # Client changes
git diff v1.0.0..HEAD --name-only -- src/solutions/        # Solution changes
```

---

## Phase 1: Build All Client Components

Build all client-side components in dependency order. The build phase runs **once** regardless of how many environments will be deployed.

### Build Dependency Order

```
1. Shared Libraries (must build first — all downstream depends on these):
   ├── @spaarke/auth          (src/client/shared/Spaarke.Auth/)
   ├── @spaarke/sdap-client   (src/client/shared/Spaarke.SdapClient/)
   └── @spaarke/ui-components (src/client/shared/Spaarke.UI.Components/)

2. Vite Solutions (20 applications):
   ├── AllDocuments, CalendarSidePane, CreateEventWizard, CreateMatterWizard
   ├── CreateProjectWizard, CreateTodoWizard, CreateWorkAssignmentWizard
   ├── DailyBriefing, DocumentUploadWizard, EventDetailSidePane, EventsPage
   ├── FindSimilarCodePage, LegalWorkspace, PlaybookLibrary, Reporting
   ├── SmartTodo, SpeAdminApp, SummarizeFilesWizard, TodoDetailSidePane
   └── WorkspaceLayoutWizard

3. Webpack Code Pages (4 applications):
   ├── AnalysisWorkspace
   ├── DocumentRelationshipViewer
   ├── PlaybookBuilder
   └── SemanticSearch

4. PCF Controls (14 controls):
   ├── AIMetadataExtractor, AssociationResolver, DocumentRelationshipViewer
   ├── DrillThroughWorkspace, EmailProcessingMonitor, RelatedDocumentCount
   ├── ScopeConfigEditor, SemanticSearchControl, SpaarkeGridCustomizer
   ├── ThemeEnforcer, UniversalDatasetGrid, UniversalQuickCreate
   ├── UpdateRelatedButton, VisualHost
   └── (build from src/client/pcf/ root)

5. External SPA (1 application):
   └── src/client/external-spa/
```

### Automated Build (Recommended)

```powershell
# Build everything in order
.\scripts\Build-AllClientComponents.ps1

# Preview what would be built (no execution)
.\scripts\Build-AllClientComponents.ps1 -WhatIf

# Skip shared libs (if unchanged since last build)
.\scripts\Build-AllClientComponents.ps1 -SkipSharedLibs

# Build only specific components
.\scripts\Build-AllClientComponents.ps1 -Component "LegalWorkspace","SpeAdminApp"
```

### Manual Build (If Script Unavailable)

#### 1. Shared Libraries

```powershell
# Auth library
cd src/client/shared/Spaarke.Auth
npm ci
npm run build

# SDAP Client (depends on Auth)
cd ../Spaarke.SdapClient
npm ci
npm run build

# UI Components (depends on Auth + SdapClient)
cd ../Spaarke.UI.Components
npm ci
npm run build
```

#### 2. Vite Solutions

```powershell
# Each Vite solution builds independently (after shared libs)
$viteSolutions = Get-ChildItem -Path "src/solutions" -Directory |
    Where-Object { Test-Path "$($_.FullName)/vite.config.ts" }

foreach ($solution in $viteSolutions) {
    Write-Host "Building $($solution.Name)..." -ForegroundColor Cyan
    Push-Location $solution.FullName
    npm ci
    npm run build
    Pop-Location
}
```

#### 3. Webpack Code Pages

```powershell
$codePages = @("AnalysisWorkspace", "DocumentRelationshipViewer", "PlaybookBuilder", "SemanticSearch")

foreach ($page in $codePages) {
    Write-Host "Building $page..." -ForegroundColor Cyan
    Push-Location "src/client/code-pages/$page"
    npm ci
    npm run build
    Pop-Location
}
```

#### 4. PCF Controls

```powershell
cd src/client/pcf
npm ci
npm run build
```

#### 5. External SPA

```powershell
cd src/client/external-spa
npm ci
npm run build
```

### Build Verification

After building, verify all build artifacts exist:

```powershell
# Spot-check key artifacts
Test-Path "src/solutions/LegalWorkspace/dist/corporateworkspace.html"     # Corporate workspace
Test-Path "src/solutions/SpeAdminApp/dist/speadmin.html"                   # SPE Admin
Test-Path "src/solutions/EventsPage/dist/index.html"                       # Events page
Test-Path "src/client/external-spa/dist/index.html"                        # External SPA
Test-Path "src/client/pcf/out/controls/UniversalQuickCreate/bundle.js"     # PCF bundle
```

**GATE**: All builds must succeed. Any build failure is a hard stop — fix the issue before proceeding to deployment.

---

## Phase 2: Deploy BFF API

Deploy the .NET 8 BFF API to Azure App Service. For production environments, **always use staging slot deployment** with zero-downtime swap.

### Production Deployment (Recommended)

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy
```

**What this does**:
1. Builds the API in Release mode (`dotnet publish`)
2. Packages to `deploy/api-publish.zip`
3. Deploys to **staging slot** (not production)
4. Runs health check against staging: `GET /healthz`
5. If healthy → **swaps staging to production** (zero-downtime)
6. Runs health check against production
7. If production health fails and `-RollbackOnFailure` is true → **auto-swaps back**

### Skip Build (If Already Built)

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy `
    -SkipBuild
```

### Dev Environment (Direct Deploy)

```powershell
# Dev uses direct deployment (no slot)
.\scripts\Deploy-BffApi.ps1
# Defaults: -Environment dev, -AppServiceName spe-api-dev-67e2xz
```

### Verification

```powershell
# Health check
curl https://api.spaarke.com/healthz    # Should return 200
curl https://api.spaarke.com/ping        # Should return 200
```

### Parameters Reference

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `-Environment` | string | dev | `dev`, `staging`, `production` |
| `-ResourceGroupName` | string | spe-infrastructure-westus2 | Azure resource group |
| `-AppServiceName` | string | spe-api-dev-67e2xz | Azure App Service name |
| `-UseSlotDeploy` | switch | $false | **Required for production** — staging slot swap |
| `-SkipBuild` | switch | $false | Skip dotnet publish, use existing artifacts |
| `-RollbackOnFailure` | bool | $true | Auto-rollback if post-swap health fails |
| `-MaxHealthCheckRetries` | int | 12 | Retry count for health checks |
| `-HealthCheckIntervalSeconds` | int | 5 | Seconds between retries |

**GATE**: BFF API health check must pass before proceeding to Dataverse deployment.

---

## Phase 3: Deploy Dataverse Solutions

Import all managed solutions to the target Dataverse environment in dependency order.

### Deployment

```powershell
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -TenantId "a221a95e-..." `
    -ClientId "..." `
    -ClientSecret "..."
```

### Import Order (Dependency Tiers)

Solutions are imported in strict dependency order:

| Tier | Solution | Dependencies |
|------|----------|-------------|
| 1 | **SpaarkeCore** | None (base entities, option sets, security roles) |
| 2 | **webresources** | SpaarkeCore (JS files for forms/ribbons) |
| 3 | CalendarSidePane | SpaarkeCore |
| 3 | DocumentUploadWizard | SpaarkeCore |
| 3 | EventCommands | SpaarkeCore |
| 3 | EventDetailSidePane | SpaarkeCore |
| 3 | EventsPage | SpaarkeCore |
| 3 | LegalWorkspace | SpaarkeCore |
| 3 | TodoDetailSidePane | SpaarkeCore |

**SpaarkeCore must import first** — all other solutions depend on it. The script handles this automatically.

### Preview Without Deploying

```powershell
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..." `
    -WhatIf
```

### Import Subset

```powershell
# Only import specific solutions (e.g., after targeted changes)
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..." `
    -SolutionsToImport @("SpaarkeCore", "LegalWorkspace")
```

### Parameters Reference

| Parameter | Type | Required | Notes |
|-----------|------|----------|-------|
| `-EnvironmentUrl` | string | Yes | Dataverse URL (e.g., `https://spaarke-demo.crm.dynamics.com`) |
| `-TenantId` | string | Yes | Azure AD tenant ID |
| `-ClientId` | string | Yes | Service principal client ID |
| `-ClientSecret` | string | Yes* | Mutually exclusive with `-CertificateThumbprint` |
| `-CertificateThumbprint` | string | Yes* | Mutually exclusive with `-ClientSecret` |
| `-SolutionsToImport` | string[] | No | Subset of solutions to import |
| `-SkipVerification` | switch | No | Skip post-import verification |

**GATE**: All solution imports must succeed and verify before proceeding to web resources.

---

## Phase 4: Deploy Web Resources

Deploy all web resources (HTML pages, JavaScript bundles, CSS, SVG icons) to the target Dataverse environment.

### Automated Deployment (Recommended)

```powershell
.\scripts\Deploy-AllWebResources.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"
```

### What Gets Deployed

The orchestrator calls these scripts in order:

| # | Script | Web Resources | Count |
|---|--------|--------------|-------|
| 1 | `Deploy-CorporateWorkspace.ps1` | sprk_corporateworkspace (HTML) | 1 |
| 2 | `Deploy-ExternalWorkspaceSpa.ps1` | sprk_externalworkspace (HTML + inline JS) | 1 |
| 3 | `Deploy-SpeAdminApp.ps1` | sprk_speadmin (HTML) | 1 |
| 4 | `Deploy-WizardCodePages.ps1` | 12 wizard/code page web resources | 12 |
| 5 | `Deploy-EventsPage.ps1` | sprk_eventspage.html | 1 |
| 6 | `Deploy-PCFWebResources.ps1` | PCF bundle.js + CSS | 2 |
| 7 | `Deploy-RibbonIcons.ps1` | 3 SVG ribbon icons | 3 |

**Total**: ~21 web resources per environment.

### Individual Script Invocations

If you need to deploy a single component:

```powershell
# Corporate Workspace
.\scripts\Deploy-CorporateWorkspace.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# External SPA (Vite build with inline JS)
.\scripts\Deploy-ExternalWorkspaceSpa.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# SPE Admin App
.\scripts\Deploy-SpeAdminApp.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# All Wizard Code Pages (12 web resources)
.\scripts\Deploy-WizardCodePages.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# Events Page
.\scripts\Deploy-EventsPage.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# PCF Control Bundles
.\scripts\Deploy-PCFWebResources.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# Ribbon Icons
.\scripts\Deploy-RibbonIcons.ps1
```

### Additional Web Resources

These are not included in the standard orchestrator but may be needed:

```powershell
# Smart Todo (if changed)
.\scripts\Deploy-SmartTodo.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# Theme Icons (if changed)
.\scripts\Deploy-ThemeIcons.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# Theme Menu JS (if changed)
.\scripts\Deploy-ThemeMenuJs.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"
```

### Authentication Notes

- **Azure CLI scripts** (Deploy-CorporateWorkspace, Deploy-ExternalWorkspaceSpa, Deploy-SpeAdminApp, Deploy-WizardCodePages, Deploy-EventsPage): Use `az account get-access-token` — requires `az login`.
- **PAC CLI scripts** (Deploy-PCFWebResources, Deploy-RibbonIcons): Use authenticated PAC CLI session — requires `pac auth create`.

### Skip Specific Components

```powershell
.\scripts\Deploy-AllWebResources.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com" `
    -SkipComponent "RibbonIcons","PCFWebResources"
```

**GATE**: Web resource deployment summary must show all components succeeded.

---

## Phase 5: Validation

Run comprehensive post-deployment validation to verify the environment is correctly configured.

### Automated Validation

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"
```

### What Gets Validated

| Category | Checks | Pass Criteria |
|----------|--------|---------------|
| **Dataverse Environment Variables** | 7 canonical variables exist with non-empty values | All 7 present |
| **BFF API Health** | `GET /healthz` and `GET /ping` | Both return HTTP 200 |
| **CORS Configuration** | BFF API CORS includes Dataverse org URL | Dataverse URL in allowed origins |
| **Dev Value Leakage** | Scans for dev identifiers (`spaarkedev1`, `spe-api-dev`, `67e2xz`) | Zero dev values detected |

### 7 Required Dataverse Environment Variables

| Variable | Schema Name | Expected Value (Demo) |
|----------|-------------|----------------------|
| BFF API Base URL | `sprk_BffApiBaseUrl` | `https://api.spaarke.com` |
| BFF API App ID | `sprk_BffApiAppId` | `api://bff-api-prod-app-id` |
| MSAL Client ID | `sprk_MsalClientId` | `{production MSAL client ID}` |
| Tenant ID | `sprk_TenantId` | `a221a95e-...` |
| Azure OpenAI Endpoint | `sprk_AzureOpenAiEndpoint` | `https://spaarke-openai-prod.openai.azure.com/` |
| Share Link Base URL | `sprk_ShareLinkBaseUrl` | `https://app.spaarke.com/share` |
| SPE Container ID | `sprk_SharePointEmbeddedContainerId` | `{production container ID}` |

### Manual Spot-Checks

After automated validation passes, verify key user flows:

- [ ] Open a matter form — corporate workspace loads without console errors
- [ ] Open document upload wizard — wizard renders and connects to BFF
- [ ] Run an AI analysis — playbook executes and returns results
- [ ] Check SPE Admin app — admin dashboard loads
- [ ] Open events page — events grid populates

**GATE**: `Validate-DeployedEnvironment.ps1` must output `VERDICT: PASSED`. Any failure must be resolved before tagging.

---

## Phase 6: Tag Release

After all environments are deployed and validated, create a git tag to mark the release.

### Versioning Strategy

Tags use semantic versioning: `v{major}.{minor}.{patch}`

| Component | Increment When |
|-----------|---------------|
| **Major** | Breaking API changes, schema migrations, incompatible solution upgrades |
| **Minor** | New features, new scripts, new skills, non-breaking enhancements |
| **Patch** | Bug fixes, hotfixes, documentation updates |

### Create Tag

```powershell
# Determine version
git tag --sort=-v:refname | head -5  # See existing tags

# Create annotated tag
git tag -a v1.1.0 -m "Release v1.1.0 - Production release procedure tooling"

# Push tag to remote
git push origin v1.1.0
```

### Tag Enables Change Detection

Future releases can detect what changed:

```powershell
git diff v1.0.0..v1.1.0 --stat
git diff v1.0.0..v1.1.0 --name-only
```

---

## Multi-Environment Deployment

When deploying to multiple environments, repeat Phases 2-5 for each environment **sequentially**.

### Deployment Order

Deploy to environments in order of risk (lowest first):

1. **Dev** (`https://spaarkedev1.crm.dynamics.com`) — verify in safe environment
2. **Demo** (`https://spaarke-demo.crm.dynamics.com`) — customer-facing demo
3. **Production customers** — in order of priority

### Per-Environment Loop

```
FOR EACH environment in deployment list:
    Phase 2: Deploy BFF API (with environment-specific App Service)
    Phase 3: Deploy Dataverse Solutions (with environment-specific URL)
    Phase 4: Deploy Web Resources (with environment-specific URL)
    Phase 5: Validate (with environment-specific URL)

    IF validation fails → STOP (do not proceed to next environment)
```

### Using Deploy-Release.ps1

```powershell
# Single environment
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -Version "v1.1.0"

# Multiple environments (sequential)
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl @("https://spaarkedev1.crm.dynamics.com", "https://spaarke-demo.crm.dynamics.com") `
    -Version "v1.1.0"

# Preview without deploying
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -WhatIf

# Skip build (already built)
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -SkipBuild

# Skip specific phases
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -SkipPhase "BffApi"  # Only deploy solutions + web resources
```

---

## Rollback Procedures

### BFF API Rollback

**Automatic** (if `-UseSlotDeploy` and `-RollbackOnFailure $true`):
The script auto-swaps staging back to production if the post-swap health check fails.

**Manual** (after a completed swap):
```powershell
# Swap staging back to production
az webapp deployment slot swap `
    -g "rg-spaarke-platform-prod" `
    -n "spaarke-bff-prod" `
    --slot staging `
    --target-slot production

# Verify health
curl https://api.spaarke.com/healthz
```

### Dataverse Solution Rollback

Managed solutions support version rollback:

1. **Power Platform Admin Center** → Environment → Solutions → Solution history
2. Import the **previous version** of the managed solution ZIP

Or re-import from a previous git commit:

```powershell
# Checkout solution ZIPs from previous tag
git checkout v1.0.0 -- src/solutions/

# Re-import
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..."
```

### Web Resource Rollback

Re-deploy from a previous git commit:

```powershell
# Checkout previous build artifacts
git checkout v1.0.0 -- src/solutions/*/dist/ src/client/external-spa/dist/

# Re-deploy web resources
.\scripts\Deploy-AllWebResources.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"
```

### Full Rollback

To rollback an entire release:

```powershell
# 1. Rollback BFF API (slot swap)
az webapp deployment slot swap `
    -g "rg-spaarke-platform-prod" `
    -n "spaarke-bff-prod" `
    --slot staging --target-slot production

# 2. Checkout previous release
git checkout v1.0.0

# 3. Re-import solutions
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..."

# 4. Re-deploy web resources
.\scripts\Deploy-AllWebResources.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# 5. Validate
.\scripts\Validate-DeployedEnvironment.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"
```

---

## Emergency Hotfix Procedure

For critical production issues requiring immediate deployment. This is an **abbreviated release** targeting only the affected component.

### When to Use

- SEV-1 or SEV-2 incident requiring code change
- Cannot wait for full release cycle
- Fix is scoped to a single component (API, single solution, or single web resource)

### Abbreviated Process

```
1. Create hotfix branch from latest tag
   git checkout -b hotfix/v1.0.1 v1.0.0

2. Apply fix (minimal, targeted change)

3. Test locally

4. Deploy ONLY the affected component:

   BFF API fix:
     .\scripts\Deploy-BffApi.ps1 -Environment production `
         -ResourceGroupName "rg-spaarke-platform-prod" `
         -AppServiceName "spaarke-bff-prod" -UseSlotDeploy

   Solution fix:
     .\scripts\Deploy-DataverseSolutions.ps1 `
         -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
         -TenantId "..." -ClientId "..." -ClientSecret "..." `
         -SolutionsToImport @("AffectedSolution")

   Web resource fix:
     .\scripts\Deploy-CorporateWorkspace.ps1 `  # or whichever script
         -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

5. Validate
   .\scripts\Validate-DeployedEnvironment.ps1 `
       -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

6. Tag hotfix
   git tag -a v1.0.1 -m "Hotfix: {description}"
   git push origin v1.0.1

7. Merge hotfix back to master
   git checkout master
   git merge hotfix/v1.0.1
   git push origin master
```

---

## First-Time vs. Subsequent Release

| Step | First-Time Setup | Subsequent Releases |
|------|-----------------|---------------------|
| Platform infrastructure | Deploy Bicep (see PRODUCTION-DEPLOYMENT-GUIDE) | Skip — already exists |
| Entra ID app registrations | Create apps + secrets (see PRODUCTION-DEPLOYMENT-GUIDE) | Skip — already exists |
| Custom domain + SSL | Configure DNS + SSL (see PRODUCTION-DEPLOYMENT-GUIDE) | Skip — already exists |
| Key Vault secrets | Seed with Configure-ProductionAppSettings.ps1 | Only if new secrets added |
| Dataverse environment | Created by Provision-Customer.ps1 | Skip — already exists |
| Dataverse environment variables | Set 7 variables (Provision-Customer.ps1 Step 8) | Only if values changed |
| **Phase 0: Pre-flight** | ✅ Required | ✅ Required |
| **Phase 1: Build** | ✅ Required | ✅ Required |
| **Phase 2: BFF API** | ✅ Required | ✅ Required |
| **Phase 3: Solutions** | ✅ Required (fresh import) | ✅ Required (upgrade) |
| **Phase 4: Web Resources** | ✅ Required | ✅ Required |
| **Phase 5: Validation** | ✅ Required | ✅ Required |
| **Phase 6: Tag** | ✅ First tag (v1.0.0) | ✅ Increment version |

**Key difference**: First-time requires running `Provision-Customer.ps1` first (see [CUSTOMER-ONBOARDING-RUNBOOK.md](../guides/CUSTOMER-ONBOARDING-RUNBOOK.md)). Subsequent releases only need Phases 0-6 from this document.

---

## Environment Registry

Environment details are stored in `config/environments.json`. This file maps environment names to their Azure resource details and is consumed by `Deploy-Release.ps1` and the `/deploy-new-release` skill.

```json
{
  "environments": {
    "dev": {
      "dataverseUrl": "https://spaarkedev1.crm.dynamics.com",
      "bffApiUrl": "https://spe-api-dev-67e2xz.azurewebsites.net",
      "appServiceName": "spe-api-dev-67e2xz",
      "resourceGroup": "spe-infrastructure-westus2",
      "keyVaultName": "spaarke-spekvcert"
    },
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

---

## Claude Code Skill: `/deploy-new-release`

For interactive, assisted deployments, use the Claude Code skill:

```
/deploy-new-release
```

The skill guides you through:
1. Pre-flight checks (automated)
2. Environment selection from registry
3. Change detection since last tag
4. Deployment plan confirmation
5. Script execution with progress reporting
6. Validation and release tagging

See `.claude/skills/deploy-new-release/SKILL.md` for full documentation.

---

## Quick Reference: Script Invocations

| Phase | Script | Key Parameters |
|-------|--------|---------------|
| 1 | `Build-AllClientComponents.ps1` | `-WhatIf`, `-SkipSharedLibs`, `-Component` |
| 2 | `Deploy-BffApi.ps1` | `-Environment`, `-AppServiceName`, `-UseSlotDeploy`, `-SkipBuild` |
| 3 | `Deploy-DataverseSolutions.ps1` | `-EnvironmentUrl`, `-TenantId`, `-ClientId`, `-ClientSecret` |
| 4 | `Deploy-AllWebResources.ps1` | `-DataverseUrl`, `-WhatIf`, `-SkipComponent` |
| 5 | `Validate-DeployedEnvironment.ps1` | `-DataverseUrl`, `-BffApiUrl` |
| — | `Deploy-Release.ps1` | `-EnvironmentUrl`, `-Version`, `-WhatIf`, `-SkipPhase`, `-SkipBuild` |

---

## Quick Reference: Common Scenarios

| Scenario | Command |
|----------|---------|
| Full release to demo | `.\scripts\Deploy-Release.ps1 -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" -Version "v1.1.0"` |
| Preview release plan | `.\scripts\Deploy-Release.ps1 -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" -WhatIf` |
| Deploy only web resources | `.\scripts\Deploy-AllWebResources.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"` |
| Deploy only BFF API (prod) | `.\scripts\Deploy-BffApi.ps1 -Environment production -AppServiceName spaarke-bff-prod -UseSlotDeploy` |
| Deploy only solutions | `.\scripts\Deploy-DataverseSolutions.ps1 -EnvironmentUrl "..." -TenantId "..." -ClientId "..." -ClientSecret "..."` |
| Validate after deploy | `.\scripts\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://spaarke-demo.crm.dynamics.com"` |
| Rollback BFF API | `az webapp deployment slot swap -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging --target-slot production` |
| Emergency hotfix | See [Emergency Hotfix Procedure](#emergency-hotfix-procedure) |

---

*This procedure is the source of truth for production releases. Scripts in `scripts/` implement what this document describes. The `/deploy-new-release` skill wraps the scripts for interactive use.*
