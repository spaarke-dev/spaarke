# Production Release Procedure

> **Last Updated**: 2026-04-06
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

This procedure deploys a Spaarke release to one or more **existing** production environments. A release has three deployment tracks that together constitute the full platform update.

**Key Distinction**:
- `Provision-Customer.ps1` = **new environment** (creates infrastructure, Dataverse org, etc.)
- `Deploy-Release.ps1` = **update existing** (deploys code and solutions to environments that already exist)

### Deployment Tracks

A production release deploys across three tracks:

| Track | What | Deployed Via | When |
|-------|------|-------------|------|
| **Track 1: Dataverse** | SpaarkeMaster solution (schema, web resources, PCFs, security roles, env vars, MDA app) | `pac solution export` → `pac solution import` | Every release |
| **Track 2: Azure** | BFF API (.NET 8), Office Add-ins | `Deploy-BffApi.ps1`, `Deploy-OfficeAddins.ps1` | Every release (BFF); if changed (Add-ins) |
| **Track 2.5: Reference Data** | Playbook definitions, chat context mappings, Copilot agent config | `Deploy-NotificationPlaybooks.ps1`, `Deploy-ChatContextMappings.ps1`, `Deploy-CopilotAgent.ps1` | Every release (idempotent upserts) |
| **Track 3: Infrastructure** | Azure resources (App Service, OpenAI, AI Search, Key Vault, Power BI) | `Deploy-Platform.ps1`, Bicep templates, `Deploy-ReportingReports.ps1` | Only when infra changes |

**Track 1 assumption**: SpaarkeMaster in dev is production-ready — all web resources and PCF controls contain the latest built code artifacts. No code is built or uploaded during the release itself.

### Release Phase Diagram

```
Phase 0: Pre-flight (git clean, CI green, auth configured)
         │
         ▼ ─── GATE ───
Phase 1: Export SpaarkeMaster from dev
         │  (pac solution export --name SpaarkeMaster)
         │
         ▼ ─── GATE ───
         ┌─────────────────────────────────────────────┐
         │  FOR EACH target environment (sequential):  │
         │                                             │
         │  Phase 2: BFF API (Deploy-BffApi.ps1)       │
         │           + Office Add-ins (if changed)     │
         │           │                                 │
         │           ▼                                 │
         │  Phase 3: Import SpaarkeMaster              │
         │           (pac solution import)             │
         │           │                                 │
         │           ▼                                 │
         │  Phase 4: Reference Data + Publish          │
         │           (playbooks, chat context, publish) │
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

## Deployment Package: SpaarkeMaster Solution

The Dataverse deployment is based on a single comprehensive solution — **SpaarkeMaster** — that contains all Spaarke platform components. This solution is the authoritative package for what gets exported from dev and imported to target environments.

### Source and Target Model

```
Source: Dev Dataverse (https://spaarkedev1.crm.dynamics.com)
  │
  ├── SpaarkeMaster solution (unmanaged)
  │   Contains: all sprk_ entities, web resources, PCFs, security roles, etc.
  │
  └── Export as unmanaged ZIP
        │
        ▼
Target: Production Dataverse (one or more environments)
  └── Import SpaarkeMaster ZIP (unmanaged)
```

**Key principles**:
- **No build step during deployment** — all solution components (including web resources built from code) must be ready in the dev environment before export
- **Full solution import** (V1) — every release exports and imports the complete SpaarkeMaster solution. Incremental (diff-only) deployment is a V2 enhancement.
- **Dev is the source of truth** — entities, forms, views, security roles, and web resources are developed in dev, then promoted via solution export/import

### Component Inventory

SpaarkeMaster contains **383 confirmed components** across these categories:

#### Entities (91)

**87 custom entities** (full — includes all forms, views, columns, relationships):

| Category | Entities |
|----------|----------|
| **Core** | sprk_matter, sprk_project, sprk_event, sprk_document, sprk_organization, sprk_communication |
| **Financial** | sprk_invoice, sprk_invoicelineitem, sprk_billingevent, sprk_budget, sprk_budgetbucket, sprk_spendsignal, sprk_spendsnapshot |
| **Events/Tasks** | sprk_event, sprk_eventset, sprk_eventtodo, sprk_eventlog, sprk_workassignment |
| **AI/Analysis** | sprk_analysis, sprk_analysisaction, sprk_analysisplaybook, sprk_analysischatmessage, sprk_analysisoutput, sprk_analysisworkingversion, sprk_playbooknode, sprk_playbooknoderun, sprk_playbookrun |
| **AI Configuration** | sprk_aiactiontype, sprk_aichatcontextmap, sprk_aichatmessage, sprk_aichatsummary, sprk_aiknowledgedeployment, sprk_aiknowledgesource, sprk_aiknowledgetype, sprk_aimodeldeployment, sprk_aioutputtype, sprk_airetrievalmode, sprk_aiskilltype, sprk_aitooltype, sprk_analysisknowledge, sprk_analysisskill, sprk_analysistool |
| **Document Management** | sprk_container, sprk_documenttype, sprk_fileversion, sprk_uploadcontext, sprk_attachmentartifact, sprk_emailartifact |
| **Communication** | sprk_communicationaccount, sprk_communicationattachment, sprk_emailsaverule, sprk_analysisemailmetadata |
| **Configuration** | sprk_chartdefinition, sprk_charttype, sprk_deliverytemplate, sprk_externalrecordaccess, sprk_externalserviceconfig, sprk_fieldmappingprofile, sprk_fieldmappingrule, sprk_gridconfiguration, sprk_speauditlog, sprk_specontainertypeconfig, sprk_speenvironment, sprk_userpreferences, sprk_workspacelayout |
| **Reference/Lookup** | sprk_accounttype_ref, sprk_contacttype_ref, sprk_countryregion_ref, sprk_eventtype_ref, sprk_mattersubtype_ref, sprk_mattertype_ref, sprk_organizationtype_ref, sprk_practicearea_ref, sprk_projecttype_ref, sprk_recordtype_ref, sprk_usertype_ref |
| **Reporting** | sprk_report, sprk_reportcard, sprk_reportingentity, sprk_reportingview, sprk_kpiassessment |
| **Other** | sprk_memo, sprk_processingjob, sprk_registrationrequest, sprk_timekeeper, sprk_outputtypes, sprk_analysisdeliverytype, sprk_analysisactiontype |

**4 standard Microsoft entities** (metadata-only — only sprk_ customizations, not the full entity):

| Entity | Custom Columns |
|--------|---------------|
| account | sprk_containerid |
| contact | sprk_containerid, sprk_invoice, sprk_organization, sprk_systemuser |
| systemuser | sprk_containerid, sprk_usertype |
| businessunit | sprk_containerid |

**Not included**: `email` entity (not using sprk_ customizations on email).

#### Web Resources (195)

All `sprk_*` prefixed web resources including:
- HTML code pages (wizards, workspaces, admin apps)
- JavaScript bundles (form scripts, ribbon commands)
- CSS stylesheets
- SVG icons (ribbon icons, theme icons)

These are built from source code and uploaded to dev before export.

#### PCF Custom Controls (10 confirmed in-use)

| Control | Used On |
|---------|---------|
| DocumentRelationshipViewer | sprk_document main form |
| EventFormController | sprk_event (3 forms) |
| RelatedDocumentCount | sprk_document main form |
| SpeDocumentViewer | sprk_document main form |
| VisualHost | sprk_matter, sprk_project, sprk_workassignment main forms |
| SemanticSearchControl | sprk_matter, sprk_project, sprk_invoice, sprk_workassignment main forms |
| UpdateRelatedButton | Views |
| EmailProcessingMonitor | Forms |
| ThemeEnforcer | Forms |
| RegardingLink | sprk_event views (dataset binding) |

**Excluded PCFs**:
AssociationResolver, EventAutoAssociate, UniversalDocumentUpload, ScopeConfigEditor, AnalysisBuilder, AnalysisWorkspace, DueDatesWidget, EventCalendarFilter, FieldMappingAdmin, PlaybookBuilderHost, SpaarkeGridCustomizer, LegalWorkspace (PCF), UniversalDatasetGrid (broken styles.css web resource reference).

#### Other Components

| Type | Count | Notes |
|------|-------|-------|
| Global Option Sets | 24 | All `sprk_*` global choices; entity-level choices come with entities |
| MDA App Module Components | 14 | Navigation components for the Corporate Counsel app |
| Security Roles | 7 | Spaarke Basic User, Office Add In User, Reporting (Author/Viewer/Admin), AI Analysis (User/Admin) |
| Environment Variable Definitions | 21 | All `sprk_*` env vars (BFF URL, tenant ID, AI endpoints, feature flags, etc.) |
| Environment Variable Values | 9 | Current values (environment-specific — overridden per target) |
| Entity Relationships | 4 | M2M junction tables (included as relationship components) |
| MDA App | 1 | `sprk_MatterManagement` (Corporate Counsel) — the main model-driven app |
| Site Map | 1 | `sprk_MatterManagement` app navigation (3 legacy sitemaps excluded as tech debt) |

### Building the SpaarkeMaster Solution

The SpaarkeMaster solution in dev is built programmatically using the Dataverse Web API `AddSolutionComponent` action. The identification logic:

1. **Custom entities**: All entities with `sprk_` prefix (excluding M2M intersection tables which come as relationship subcomponents)
2. **Standard entities**: `account`, `contact`, `systemuser`, `businessunit` — added with `DoNotIncludeSubcomponents=true`, then `sprk_` columns added individually as Attribute components
3. **Web resources**: All records in `webresourceset` where name starts with `sprk_`
4. **Global option sets**: All records in `GlobalOptionSetDefinitions` where name starts with `sprk_`
5. **PCF controls**: 11 specific controls from the confirmed in-use list (by customcontrolid)
6. **Security roles**: Root business unit roles containing "Spaarke" in the name
7. **Environment variables**: All definitions where schemaname starts with `sprk_` (component type 380 + values type 381)

**What's NOT included**:
- Canvas apps, PowerApps settings, PowerApps components (not migrating SPAs via solution)
- Managed solutions (Creator Kit, Dataverse Accelerator — installed separately)
- The `email` standard entity (not using sprk_ customizations on email)
- 12 orphaned PCF controls (registered but not bound to any active form/view)
- 3 legacy sitemaps (sprk_DocumentManagement, sprk_LawFirmCaseManagement, sprk_CorporateMatterManagement — tech debt from removed MDA apps)
- Reference data (playbook definitions, chat context mappings — deployed separately via Track 2.5)

### Pre-Release Assumption

SpaarkeMaster in dev is assumed to be **production-ready** at the time of release. All web resources contain the latest built code, all PCF controls are at the correct version, and all schema changes are complete. This means:

- Development workflow (build code → upload to dev → iterate) happens **before** the release process starts
- The release process starts at **export** — not at build
- No code is compiled, bundled, or uploaded during the release itself

**Pre-release verification**:
```powershell
# Verify SpaarkeMaster component count (expected: 386)
pac solution list  # Should show SpaarkeMaster v1.0.0.0

# Publish all customizations in dev before export
pac org publish
```

### Build-SpaarkeMaster.ps1

The `Build-SpaarkeMaster.ps1` script automates solution composition using independent discovery:
- Creates/recreates the SpaarkeMaster solution in dev
- Adds all components programmatically using the identification logic above
- Verifies component count matches expected (386)

Run this when: new entities/PCFs/web resources are added, or to rebuild the solution from scratch.

See `scripts/Build-SpaarkeMaster.ps1` for implementation.

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

## Phase 1: Export SpaarkeMaster from Dev

Export the production-ready SpaarkeMaster solution from the dev environment. No code is built during this phase — the assumption is that all web resources and PCF controls in dev already contain the latest built artifacts.

### Export

```powershell
# Ensure PAC CLI is connected to dev
pac auth select --environment "https://spaarkedev1.crm.dynamics.com"

# Publish all customizations before export
pac org publish

# Export SpaarkeMaster
pac solution export --name SpaarkeMaster --path ./deploy/SpaarkeMaster.zip --overwrite
```

### Verify Export

```powershell
# Check ZIP was created
Test-Path ./deploy/SpaarkeMaster.zip

# Check file size is reasonable (should be several MB)
(Get-Item ./deploy/SpaarkeMaster.zip).Length / 1MB
```

**GATE**: Export must succeed and produce a valid ZIP. If SpaarkeMaster solution doesn't exist in dev, run `Build-SpaarkeMaster.ps1` first.

---

## Phase 2: Deploy BFF API

Deploy the .NET 8 BFF API to Azure App Service. For production environments, **always use staging slot deployment** with zero-downtime swap.

**CRITICAL**: The BFF API must be built and deployed from the **master branch** in the main repository (not a worktree or feature branch). This ensures the deployed API matches the released code.

```powershell
# Verify you are on master in the main repo
git branch --show-current  # Must show: master
git log --oneline -1       # Must match the release commit
```

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

## Phase 3: Deploy Dataverse Solution (SpaarkeMaster)

Import the SpaarkeMaster solution to the target Dataverse environment. This is a single unmanaged solution containing all Spaarke platform components (see [Deployment Package](#deployment-package-spaarkemaster-solution) above).

### Export from Dev

Before deploying to any target, export SpaarkeMaster from the dev environment:

```powershell
# Ensure PAC CLI is connected to dev
pac auth select --environment "https://spaarkedev1.crm.dynamics.com"

# Export SpaarkeMaster
pac solution export --name SpaarkeMaster --path ./deploy/SpaarkeMaster.zip --overwrite
```

### Import to Target

```powershell
# Connect PAC CLI to target environment
pac auth select --environment "https://spaarke-demo.crm.dynamics.com"

# Import SpaarkeMaster
pac solution import --path ./deploy/SpaarkeMaster.zip --publish-changes

# Or via Deploy-Release.ps1 which handles this automatically
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -Version "v1.1.0"
```

### What Gets Imported

A single import of SpaarkeMaster deploys:
- 87 custom entities (full schema, forms, views, relationships)
- 4 standard entity customizations (sprk_ columns on account, contact, systemuser, businessunit)
- 195 web resources (HTML pages, JS bundles, CSS, SVG icons)
- 11 PCF custom controls
- 24 global option sets
- 7 security roles
- 21 environment variable definitions + values
- 4 sitemaps, entity relationships, plugin registrations

**Note**: Environment variable **values** will be imported from dev. After first import to a new target, update environment-specific values (BFF URL, tenant ID, etc.) — see Phase 5 validation.

### Preview Without Deploying

```powershell
.\scripts\Deploy-Release.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -WhatIf
```

**GATE**: Solution import must succeed before proceeding. Check for import errors in the Power Platform Admin Center if the import fails.

---

## Phase 4: Reference Data + Publish

With SpaarkeMaster imported, all schema, web resources, and PCF controls are in the target environment. This phase deploys **reference data** (records that the platform requires to function) and publishes all customizations.

### 4.1 Deploy Reference Data

Reference data scripts are idempotent (safe to re-run every release):

```powershell
# Playbook definitions (7 notification playbooks with nodes and relationships)
.\scripts\Deploy-NotificationPlaybooks.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# Chat context mappings (AI chat configuration seed data)
.\scripts\Deploy-ChatContextMappings.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"

# Copilot agent configuration (entity descriptions + glossary)
.\scripts\Deploy-CopilotAgent.ps1 `
    -DataverseUrl "https://spaarke-demo.crm.dynamics.com"
```

### 4.2 Publish All Customizations

```powershell
# Publish all customizations in the target environment
pac org publish --async
```

### 4.3 Verify Import

Spot-check that key components were imported correctly:

```powershell
$token = az account get-access-token --resource "https://spaarke-demo.crm.dynamics.com" --query accessToken -o tsv
$headers = @{ "Authorization" = "Bearer $token"; "OData-Version" = "4.0" }

# Check corporate workspace web resource
Invoke-RestMethod -Uri "https://spaarke-demo.crm.dynamics.com/api/data/v9.2/webresourceset?`$filter=name eq 'sprk_corporateworkspace'&`$select=name,displayname" -Headers $headers

# Check a PCF control
Invoke-RestMethod -Uri "https://spaarke-demo.crm.dynamics.com/api/data/v9.2/customcontrols?`$filter=contains(name,'SemanticSearchControl')&`$select=name,version" -Headers $headers
```

### Dev-Only: Updating Web Resources Without Full Solution Import

During development, individual web resources can be updated without re-exporting and re-importing SpaarkeMaster. These scripts upload directly to the dev environment:

| Script | Purpose |
|--------|---------|
| `Deploy-CorporateWorkspace.ps1` | Upload sprk_corporateworkspace HTML |
| `Deploy-WizardCodePages.ps1` | Upload 12 wizard/code page web resources |
| `Deploy-EventsPage.ps1` | Upload sprk_eventspage HTML |
| `Deploy-SpeAdminApp.ps1` | Upload sprk_speadmin HTML |
| `Deploy-PCFWebResources.ps1` | Upload PCF bundle.js + CSS |
| `Deploy-RibbonIcons.ps1` | Upload SVG ribbon icons |

These are **development iteration tools**, not part of the production release flow. For production releases, all web resources are included in the SpaarkeMaster solution import.

**GATE**: All reference data scripts and `pac org publish` must complete without errors.

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
