# Design: Environment-Agnostic Configuration System

**Date**: 2026-03-17
**Status**: Draft — Pending Review
**Scope**: Cross-cutting infrastructure change affecting all solution layers

---

## Problem Statement

Deploying Spaarke to a new environment (customer org, staging, production) requires **manual discovery and patching** of environment-specific values scattered across:

- **9 code pages / SPAs** — BFF URL, MSAL client ID baked into JS at build time
- **3+ PCF controls** — Hardcoded client IDs, tenant IDs, redirect URIs
- **BFF API** — Hardcoded Dataverse org URL in link generation, CORS origins
- **Office Add-ins** — Hardcoded client IDs and BFF URL defaults
- **Shared libraries** — Hardcoded defaults in `@spaarke/auth` and `environmentVariables.ts`

The result: **N code pages × M environments = N×M builds**, plus manual config hunting for each deployment. Components that appear to work in dev silently fail in production because they resolve to dev-environment values.

### The Goal

**Build once, deploy anywhere.** Every environment-specific value must flow automatically from infrastructure provisioning → deployment pipeline → runtime resolution. Zero manual patching.

---

## Current State Assessment

### What's Already Environment-Agnostic (Good)

| Component | Pattern | Status |
|-----------|---------|--------|
| **appsettings.template.json** | Token substitution (`#{TOKEN}#`) + Key Vault refs | Fully parameterized |
| **Bicep infrastructure** | Per-environment `.bicepparam` files | Fully parameterized |
| **GitHub Actions secrets** | Environment-scoped (dev/staging/prod) | Properly scoped |
| **Dataverse connection string** | Reads from config + Key Vault | Fixed (TenantId added) |
| **Redis, Service Bus, AI services** | All Key Vault references | Fully parameterized |
| **SPE PowerShell scripts** | Env var fallbacks (just completed) | Fully parameterized |
| **PCF `environmentVariables.ts`** | Queries Dataverse Environment Variables at runtime | Correct pattern — but inconsistently adopted |

### What's NOT Environment-Agnostic (The Problem)

#### Layer 1: Code Pages (9 affected)

| Code Page | Build Tool | BFF URL Source | MSAL Client ID Source | Env-Agnostic? |
|-----------|------------|----------------|----------------------|---------------|
| AnalysisWorkspace | Webpack | `.env.production` (build-time) | `.env.production` (build-time) | NO |
| PlaybookBuilder | Webpack | `.env.production` (build-time) | `.env.production` (build-time) | NO |
| SprkChatPane | Webpack | `.env.production` (build-time) | `.env.production` (build-time) | NO |
| DocumentRelationshipViewer | Webpack | Hardcoded fallback | Hardcoded `170c98e1` | NO |
| SemanticSearch | Webpack | Hardcoded fallback | Hardcoded `170c98e1` | NO |
| LegalWorkspace | Vite | `.env.production` (build-time) | `.env.production` (build-time) | NO |
| DocumentUploadWizard | Webpack | `.env.production` (build-time) | Hardcoded fallback | NO |
| SpeAdminApp | Vite | `.env.production` (build-time) | N/A (uses @spaarke/auth) | NO |
| External SPA | Vite | `.env.production` (`#{TOKEN}#`) | `.env.production` (`#{TOKEN}#`) | PARTIAL (token sub only) |

**Root cause**: Code pages run as Dataverse web resources inside an iframe. They cannot use the PCF `webApi` SDK, so they can't query Dataverse Environment Variables the way PCF controls do. Instead, they bake values in at build time.

#### Layer 2: PCF Controls (3 with hardcoded auth)

| PCF Control | Issue | Files |
|-------------|-------|-------|
| UniversalQuickCreate | Hardcoded CLIENT_ID, TENANT_ID, redirect URI | `msalConfig.ts` |
| DocumentRelationshipViewer | Hardcoded CLIENT_ID, TENANT_ID, redirect URI | `msalConfig.ts`, `authInit.ts` |
| SemanticSearchControl | Hardcoded CLIENT_ID, TENANT_ID, redirect URI | `msalConfig.ts`, `authInit.ts` |

**Root cause**: These controls predate the `@spaarke/auth` shared library and `environmentVariables.ts` pattern. They were written with dev values and never migrated.

#### Layer 3: BFF API (3 hardcoded references)

| Location | Hardcoded Value | Impact |
|----------|----------------|--------|
| `OfficeDocumentPersistence.cs:225` | `https://spaarkedev1.crm.dynamics.com` + app ID `729afe6d` | Document record links always point to dev |
| `OfficeService.cs:1112` | `https://spaarkedev1.crm.dynamics.com` | Quick-create record links point to dev |
| `OfficeService.cs:983` | `https://spaarke.app/doc` | Share links use fixed domain |
| `appsettings.Production.json:38-39` | `spaarkedev1` in CORS origins | Production CORS allows wrong origin |

#### Layer 4: Shared Libraries (defaults leak through)

| Library | Hardcoded Default | File |
|---------|------------------|------|
| `@spaarke/auth` | CLIENT_ID `170c98e1`, BFF scope `api://1e40baad.../user_impersonation` | `config.ts` |
| `environmentVariables.ts` | BFF URL `spe-api-dev-67e2xz`, OpenAI endpoint `spaarke-openai-dev` | `environmentVariables.ts` |

#### Layer 5: Office Add-ins

| Location | Hardcoded Default | File |
|----------|------------------|------|
| `authConfig.ts` | CLIENT_ID `c1258e2d`, TENANT_ID `a221a95e`, BFF URL `spe-api-dev-67e2xz` | `shared/auth/authConfig.ts` |

#### Layer 6: Inconsistencies

| Issue | Details |
|-------|---------|
| Window global naming | `__SPAARKE_BFF_BASE_URL__` vs `__SPAARKE_BFF_URL__` (SpeAdminApp) |
| BFF scope format | `user_impersonation` vs `SDAP.Access` (DocumentRelationshipViewer PCF) |
| `.env.production` values | Some point to dev, some to prod, some use tokens — no consistency |

---

## Proposed Solution: Environment Configuration Pipeline

### Architecture: Single Source of Truth → Automatic Distribution

```
┌─────────────────────────────────────────────────────────────────────┐
│                    INFRASTRUCTURE PROVISIONING                       │
│  (Provision-Customer.ps1 / platform.bicep / customer.bicep)         │
│                                                                     │
│  Creates:                                                           │
│  ├── Azure Resources (App Service, Key Vault, OpenAI, etc.)        │
│  ├── App Service Settings (TENANT_ID, API_APP_ID, etc.)            │
│  ├── Key Vault Secrets (connection strings, API keys)               │
│  └── Dataverse Environment Variables (sprk_BffApiBaseUrl, etc.)     │
│                                                                     │
│  OUTPUT: environment-config.json (canonical list of all values)     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                    DEPLOYMENT PIPELINE
                             │
         ┌───────────────────┼───────────────────┐
         ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│   BFF API       │ │  Code Pages     │ │  Dataverse      │
│                 │ │  (Build Once)   │ │  Solutions      │
│ Token sub in    │ │                 │ │                 │
│ appsettings     │ │ Runtime resolve │ │ PAC CLI import  │
│ from env-config │ │ from Dataverse  │ │ + env var set   │
│                 │ │ Environment Vars│ │ from env-config │
└─────────────────┘ └─────────────────┘ └─────────────────┘
```

### The 5 Canonical Environment Values

Every environment is defined by exactly these values. Everything else derives from them.

| Canonical Value | Source | Token | Dataverse Env Var | App Setting |
|----------------|--------|-------|-------------------|-------------|
| **Tenant ID** | Azure AD | `#{TENANT_ID}#` | — | `TENANT_ID` |
| **BFF API URL** | App Service hostname | `#{BFF_API_URL}#` | `sprk_BffApiBaseUrl` | (self) |
| **BFF App ID** (API registration) | Azure AD App Registration | `#{API_APP_ID}#` | `sprk_BffApiAppId` | `API_APP_ID` |
| **MSAL Client ID** (SPA registration) | Azure AD App Registration | `#{MSAL_CLIENT_ID}#` | `sprk_MsalClientId` | — |
| **Dataverse Org URL** | Dataverse environment | `#{DATAVERSE_URL}#` | — (self-referencing) | `Dataverse:ServiceUrl` |

**Derived values** (computed, not configured):

| Derived Value | Formula |
|---------------|---------|
| BFF OAuth Scope | `api://${API_APP_ID}/user_impersonation` |
| MSAL Authority | `https://login.microsoftonline.com/${TENANT_ID}` (single-tenant) or `/organizations` (multi-tenant) |
| MSAL Redirect URI | `${DATAVERSE_ORG_URL}` (PCF/code pages) or `brk-multihub://localhost` (Office add-ins) |
| Dataverse Admin URL | Derived from org URL pattern |
| CORS Origins | `${DATAVERSE_ORG_URL}`, `${DATAVERSE_ORG_URL.replace('.crm.', '.api.crm.')}` |

### What Changes — By Layer

#### A. Infrastructure: Provisioning Creates Environment Config

**New**: `Provision-Customer.ps1` outputs a canonical `environment-config.json` after resource creation:

```json
{
  "environment": "production",
  "tenantId": "a221a95e-...",
  "bffApiUrl": "https://spe-api-prod-abc123.azurewebsites.net",
  "bffApiAppId": "1e40baad-...",
  "msalClientId": "170c98e1-...",
  "dataverseUrl": "https://contoso.crm.dynamics.com",
  "keyVaultUrl": "https://sprk-contoso-prod-kv.vault.azure.net/",
  "derived": {
    "bffOAuthScope": "api://1e40baad-.../user_impersonation",
    "corsOrigins": [
      "https://contoso.crm.dynamics.com",
      "https://contoso.api.crm.dynamics.com"
    ]
  }
}
```

**New**: Provisioning step explicitly creates Dataverse Environment Variables:

```powershell
# In Provision-Customer.ps1 — new step or added to existing step
pac env var set --name sprk_BffApiBaseUrl    --value "$BffApiUrl/api"
pac env var set --name sprk_BffApiAppId      --value "$BffApiAppId"
pac env var set --name sprk_MsalClientId     --value "$MsalClientId"
pac env var set --name sprk_TenantId         --value "$TenantId"
```

**New**: `Validate-EnvironmentConfig.ps1` — post-deployment validation script:

```powershell
# Validates that:
# 1. Dataverse Environment Variables exist and are populated
# 2. BFF API /healthz responds from the configured URL
# 3. CORS origins match the Dataverse org URL
# 4. MSAL client ID resolves in Azure AD
# 5. Key Vault is reachable
# 6. Code pages can authenticate (headless MSAL token test)
```

#### B. BFF API: Fix Hardcoded Values

| File | Change |
|------|--------|
| `OfficeDocumentPersistence.cs:225` | Inject `IOptions<DataverseOptions>` → use `options.Value.EnvironmentUrl` |
| `OfficeService.cs:1112` | Same — use `DataverseOptions.EnvironmentUrl` |
| `OfficeService.cs:983` | Add `ShareLinkBaseUrl` to appsettings, inject via `IOptions` |
| `appsettings.Production.json:38-39` | Replace hardcoded CORS with `#{DATAVERSE_ORG_NAME}#` token |
| `appsettings.template.json` | Add tokens: `#{SHARE_LINK_BASE_URL}#`, `#{DATAVERSE_APP_ID}#` |

#### C. Code Pages: Runtime Resolution (The Big Change)

**Strategy**: Code pages resolve BFF URL from Dataverse Environment Variables at runtime, using the same pattern as PCF controls — but via `fetch()` + bearer token instead of `webApi`.

**Implementation in `@spaarke/auth`** (shared library, already used by most code pages):

```typescript
// New: resolveRuntimeConfig() in @spaarke/auth
export async function resolveRuntimeConfig(): Promise<SpaarkeRuntimeConfig> {
  // 1. Get org URL from Xrm context (available in all Dataverse web resources)
  const orgUrl = getOrgUrl(); // Xrm.Utility.getGlobalContext().getClientUrl()

  // 2. Acquire Dataverse token (MSAL already initialized)
  const token = await acquireToken([`${orgUrl}/.default`]);

  // 3. Query Dataverse Environment Variables
  const envVars = await fetchEnvironmentVariables(token, orgUrl, [
    'sprk_BffApiBaseUrl',
    'sprk_BffApiAppId',
    'sprk_MsalClientId',
  ]);

  // 4. Cache for 5 minutes (same as PCF pattern)
  return {
    bffBaseUrl: envVars['sprk_BffApiBaseUrl'],
    bffOAuthScope: `api://${envVars['sprk_BffApiAppId']}/user_impersonation`,
    msalClientId: envVars['sprk_MsalClientId'],
  };
}
```

**Bootstrap sequence change**:

```
BEFORE (build-time):
  1. Read MSAL client ID from import.meta.env.VITE_MSAL_CLIENT_ID
  2. Initialize MSAL
  3. Read BFF URL from import.meta.env.VITE_BFF_BASE_URL
  4. Render app

AFTER (runtime):
  1. Read MSAL client ID from Xrm context OR window global OR build-time fallback
  2. Initialize MSAL with org-scoped redirect
  3. Call resolveRuntimeConfig() → queries Dataverse Environment Variables
  4. Cache config (5 min)
  5. Render app with resolved config
```

**MSAL Client ID chicken-and-egg**: The MSAL client ID is needed before auth (to start the auth flow). Options:

| Option | Feasibility | Recommendation |
|--------|-------------|----------------|
| Read from `Xrm.Utility.getGlobalContext()` | Xrm is available before auth in web resources | **Preferred** — investigate if clientId is exposed |
| Embed in HTML web resource as data attribute | Set during solution import | Viable fallback |
| Keep as build-time env var | Same app registration across environments | **Acceptable** if client ID is truly shared |
| Window global set by host page | Requires Dataverse form script | Complex, not recommended |

#### D. PCF Controls: Migrate to Shared Patterns

| Control | Change |
|---------|--------|
| UniversalQuickCreate | Replace hardcoded `msalConfig.ts` → use `@spaarke/auth` + `environmentVariables.ts` |
| DocumentRelationshipViewer | Same migration |
| SemanticSearchControl | Same migration |

**Pattern**: All PCF controls should get auth config from Dataverse Environment Variables via `environmentVariables.ts`, not hardcoded constants.

#### E. Office Add-ins: Environment Variable Injection

| Change | Details |
|--------|---------|
| `authConfig.ts` defaults | Replace hardcoded IDs with webpack `process.env` injection (already partially done) |
| Webpack configs | Ensure `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, `BFF_API_BASE_URL` are injected from CI/CD |
| Manifest | Parameterize URLs in add-in manifest for per-environment deployment |

#### F. Shared Libraries: Remove Dev Defaults

| Library | Change |
|---------|--------|
| `@spaarke/auth/config.ts` | Remove `DEFAULT_CLIENT_ID = '170c98e1'` — require explicit config or runtime resolution |
| `environmentVariables.ts` | Remove hardcoded dev fallbacks for `sprk_BffApiBaseUrl` etc. — throw if not found |

**Rationale**: Dev defaults mask configuration failures. If Dataverse Environment Variables aren't set, the component should fail loudly with a clear error message — not silently connect to the dev environment.

#### G. Deployment Validation Script

**New**: `Validate-DeployedEnvironment.ps1` — runs after every deployment:

```
INFRASTRUCTURE VALIDATION:
  [ ] Azure App Service responds at configured URL
  [ ] BFF API /healthz responds 200
  [ ] Key Vault is accessible from App Service (test secret read)
  [ ] AI services reachable (OpenAI, Doc Intel, AI Search — test endpoint ping)
  [ ] Redis cache connection succeeds
  [ ] Service Bus connection succeeds

DATAVERSE VALIDATION:
  [ ] Dataverse Environment Variables exist and are populated:
      - sprk_BffApiBaseUrl
      - sprk_BffApiAppId
      - sprk_MsalClientId
      - sprk_TenantId
      - sprk_AzureOpenAiEndpoint
      - sprk_ShareLinkBaseUrl
      - sprk_SharePointEmbeddedContainerId
  [ ] BFF API CORS origins match Dataverse org URL (not dev)
  [ ] Dataverse ServiceClient can connect (test query)
  [ ] SpaarkeCore solution is imported (check solution version)
  [ ] Code page web resources exist in Dataverse
  [ ] PCF controls are registered

SPE VALIDATION:
  [ ] Container Type ID is valid (Key Vault secret exists, GUID format)
  [ ] Default container exists and is accessible via Graph API
  [ ] Root business unit has sprk_containerid populated
  [ ] BFF API can create/read containers (test Graph API call)

AUTHENTICATION VALIDATION:
  [ ] MSAL client ID resolves in Azure AD (app registration exists)
  [ ] BFF App ID resolves in Azure AD
  [ ] Token acquisition succeeds (client credentials flow)
  [ ] OBO token exchange works (BFF → Graph)

SECRET LIFECYCLE:
  [ ] All Key Vault secrets exist and are non-empty
  [ ] App registration secret expiry > 90 days (warn if < 90)
  [ ] Certificate expiry check (if certs in use)

NO DEV LEAKAGE:
  [ ] No "spaarkedev1" in resolved CORS origins
  [ ] No "spe-api-dev" in resolved BFF URL
  [ ] No dev tenant ID in resolved config
  [ ] No dev client IDs in resolved config
```

---

## Additional Findings: Keys/Certs, Dataverse, SPE

### Keys, Certificates & Secrets — Status: Well-Parameterized

All 16+ secrets are stored in Azure Key Vault with `@Microsoft.KeyVault()` references. No hardcoded secrets found in code. Pattern:

```json
"OpenAiKey": "@Microsoft.KeyVault(SecretUri=#{KEY_VAULT_URL}#secrets/ai-openai-key)"
```

**Key Vault Secrets Inventory** (all environment-specific, all properly managed):

| Secret | Purpose | Set By |
|--------|---------|--------|
| `BFF-API-ClientSecret` | BFF API app registration | Azure AD |
| `DSM-SPE-Dev-2-ClientSecret` | SPA app registration | Azure AD |
| `Dataverse-ServiceUrl` | Dataverse connection | Provisioning |
| `ServiceBus-ConnectionString` | Messaging | Bicep output |
| `Redis-ConnectionString` | Caching | Bicep output |
| `Storage-ConnectionString` | Blob storage | Bicep output |
| `ai-openai-endpoint` / `ai-openai-key` | Azure OpenAI | Bicep output |
| `ai-docintel-endpoint` / `ai-docintel-key` | Document Intelligence | Bicep output |
| `ai-search-endpoint` / `ai-search-key` | AI Search | Bicep output |
| `SPE-ContainerTypeId` | SPE container type | Manual / provisioning |
| `SPE-DefaultContainerId` | Email default container | Manual / provisioning |
| `SPE-CommunicationArchiveContainerId` | Communication archive | Manual / provisioning |
| `PromptFlow-Endpoint` / `PromptFlow-Key` | AI Foundry | Bicep output |
| `Email-WebhookSecret` | Webhook validation | Manual |
| `AppInsights-ConnectionString` | Telemetry | Bicep output |

**Gap — Secret Expiry Tracking**:
- BFF API secret expires **2027-12-18**
- DSM-SPE Dev 2 secret expires **2027-09-22**
- No automated rotation or alerting exists
- **Recommendation**: Add Key Vault secret expiry monitoring to `Validate-DeployedEnvironment.ps1`

**Certificates**: Not used in production (client secret approach preferred). Pattern documented in `.claude/patterns/auth/service-principal.md` but not active.

### Dataverse-Specific Configuration

**Environment-Agnostic (No Action Needed)**:

| Component | Why It's Safe |
|-----------|---------------|
| Entity logical names (`sprk_matter`, etc.) | Standardized publisher prefix `sprk_` — same in all environments |
| Solution GUIDs (forms, views, saved queries) | Dataverse remaps automatically on solution import |
| Security roles and teams | Defined in solution XML, no hardcoded GUIDs in code |
| Plugin registration | Solution-import-driven, entity/message names are standard |
| Publisher prefix (`sprk_`) | Consistent across all components |
| Web resource names (`sprk_*`) | Fixed names, same in all environments |

**NOT Environment-Agnostic (Action Required)**:

| Issue | File | Severity |
|-------|------|----------|
| **Environment Variable definitions missing from solution XML** | SpaarkeCore solution | CRITICAL — `sprk_BffApiBaseUrl` etc. must be manually created per environment instead of auto-created on import |
| **`sprk_subgrid_parent_rollup.js`** hardcodes org-to-BFF URL mapping | `src/solutions/webresources/sprk_subgrid_parent_rollup.js:59-66` | CRITICAL — `if (org === "spaarkedev1") url = "..."` — new customer orgs fall back to dev |
| **Dataverse App ID `729afe6d`** hardcoded in record link generation | `OfficeDocumentPersistence.cs:225` | CRITICAL — model-driven app GUID is environment-specific |
| **`update-theme-icons.ps1`** hardcodes dev org URL | `infrastructure/scripts/update-theme-icons.ps1:7,13` | HIGH |
| **`Deploy-EventsSitemap.ps1`** defaults to dev org | `infrastructure/dataverse/solutions/EventsSitemap/Deploy-EventsSitemap.ps1:34` | HIGH |

### SharePoint Embedded (SPE) — Status: 95% Parameterized

SPE is the **best-parameterized layer** in the solution:

| SPE Component | Configuration Source | Status |
|---------------|---------------------|--------|
| Container Type ID | Key Vault `SPE-ContainerTypeId` → appsettings `#{DEFAULT_CT_ID}#` | **Parameterized** |
| Email Default Container | Key Vault `SPE-DefaultContainerId` → appsettings `#{SPE_DEFAULT_CONTAINER_ID}#` | **Parameterized** |
| Communication Archive Container | Key Vault `SPE-CommunicationArchiveContainerId` | **Parameterized** |
| Business Unit Containers | Dataverse `sprk_containerid` on BU records, set by `New-BusinessUnitContainer.ps1` | **Runtime (script-driven)** |
| SPE API calls | Microsoft.Graph SDK via `SpeFileStore` facade — no hardcoded endpoints | **Abstracted** |
| Owning App credentials | Key Vault (`BFF-API-ClientId`, `BFF-API-ClientSecret`) | **Parameterized** |
| SPE roles/permissions | Dynamic via Graph API | **Not hardcoded** |
| Bicep parameters | `containerTypeId`, `communicationArchiveContainerId` parameterized | **Parameterized** |
| SharePoint domain in scripts | Env var `$env:SHAREPOINT_DOMAIN` (completed this project) | **Parameterized** |
| SharePoint domain in C# | Graph SDK abstraction — no direct domain references | **Abstracted** |

**One SPE Gap**: `sprk_SharePointEmbeddedContainerId` is referenced in `environmentVariables.ts` defaults but:
- Not created by provisioning scripts
- Not defined in SpaarkeCore solution XML
- PCF controls that need the default container ID have no reliable way to get it in a new environment

**Recommendation**: Add `sprk_SharePointEmbeddedContainerId` to the Dataverse Environment Variable definitions and set it during provisioning Step 8 (container creation).

---

## Complete Environment-Specific Value Inventory

### Values That Flow from Infrastructure → All Components

| # | Value | Infrastructure Source | BFF API | Code Pages | PCF Controls | Office Add-ins | Scripts |
|---|-------|----------------------|---------|------------|-------------|----------------|---------|
| 1 | **Tenant ID** | Azure AD | appsettings `#{TENANT_ID}#` | Xrm context or env var `sprk_TenantId` | Xrm context or env var | webpack inject | `$env:TENANT_ID` |
| 2 | **BFF API URL** | App Service hostname | (self) | env var `sprk_BffApiBaseUrl` | env var `sprk_BffApiBaseUrl` | webpack inject | `$env:BFF_API_URL` |
| 3 | **BFF App ID** | Azure AD App Reg | appsettings `#{API_APP_ID}#` | env var `sprk_BffApiAppId` (for scope) | env var `sprk_BffApiAppId` | webpack inject | `$env:API_APP_ID` |
| 4 | **MSAL Client ID** | Azure AD App Reg | N/A | env var `sprk_MsalClientId` or Xrm | env var `sprk_MsalClientId` | webpack inject | N/A |
| 5 | **Dataverse Org URL** | Dataverse environment | Key Vault `Dataverse-ServiceUrl` | `Xrm.getClientUrl()` | `Xrm.getClientUrl()` | N/A | `$env:DATAVERSE_URL` |
| 6 | **Key Vault URL** | Bicep output | appsettings `#{KEY_VAULT_URL}#` | N/A | N/A | N/A | `$env:KEY_VAULT_URL` |
| 7 | **SPE Container Type ID** | Key Vault `SPE-ContainerTypeId` | appsettings `#{DEFAULT_CT_ID}#` | N/A | N/A | N/A | `$env:SPE_CONTAINER_TYPE_ID` |
| 8 | **SharePoint Domain** | Tenant config | Graph SDK (auto) | N/A | N/A | N/A | `$env:SHAREPOINT_DOMAIN` |
| 9 | **Share Link Base URL** | Business decision | appsettings (new) | N/A | N/A | N/A | N/A |
| 10 | **Dataverse App GUID** | Model-driven app | appsettings (new `#{DATAVERSE_APP_ID}#`) | N/A | N/A | N/A | N/A |
| 11 | **CORS Origins** | Derived from #5 | appsettings `#{DATAVERSE_ORG_NAME}#` | N/A | N/A | N/A | N/A |
| 12 | **AI Service Endpoints** | Key Vault | appsettings (KV refs) | N/A | env var `sprk_AzureOpenAiEndpoint` | N/A | N/A |

---

## New Dataverse Environment Variables Required

The provisioning process must create these in every target environment:

| Schema Name | Display Name | Default Value (Dev) | Set By |
|-------------|-------------|--------------------|---------|
| `sprk_BffApiBaseUrl` | BFF API Base URL | `https://spe-api-dev-67e2xz.azurewebsites.net/api` | Provision-Customer.ps1 |
| `sprk_BffApiAppId` | BFF API App ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | Provision-Customer.ps1 |
| `sprk_MsalClientId` | MSAL Client ID | `170c98e1-d486-4355-bcbe-170454e0207c` | Provision-Customer.ps1 |
| `sprk_TenantId` | Tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Provision-Customer.ps1 |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI Endpoint | `https://spaarke-openai-dev.openai.azure.com/` | Provision-Customer.ps1 |
| `sprk_ShareLinkBaseUrl` | Share Link Base URL | `https://spaarke.app/doc` | Provision-Customer.ps1 |
| `sprk_SharePointEmbeddedContainerId` | Default SPE Container ID | (per-environment GUID) | Provision-Customer.ps1 Step 8 |

**These definitions should be included in the SpaarkeCore Dataverse solution** so they're created automatically on solution import. Override values are set per-environment by the provisioning script.

---

## Implementation Phases

### Phase 1: Infrastructure Foundation
- [ ] Add new tokens to `appsettings.template.json` (`#{SHARE_LINK_BASE_URL}#`, `#{DATAVERSE_APP_ID}#`)
- [ ] Fix BFF API hardcoded values (3 files, 4 locations — OfficeDocumentPersistence, OfficeService ×2, CORS)
- [ ] Fix `appsettings.Production.json` CORS — replace `spaarkedev1` with `#{DATAVERSE_ORG_NAME}#`
- [ ] Add Dataverse Environment Variable definitions to SpaarkeCore solution XML
- [ ] Add Environment Variable creation step to `Provision-Customer.ps1` (`pac env var set`)
- [ ] Create `environment-config.json` canonical output from provisioning
- [ ] Create `Validate-DeployedEnvironment.ps1` (see validation checklist below)
- [ ] Add `sprk_SharePointEmbeddedContainerId` to env var definitions + set in provisioning Step 8
- [ ] Parameterize `sprk_subgrid_parent_rollup.js` — replace org-to-BFF URL map with Dataverse Environment Variable query
- [ ] Parameterize `update-theme-icons.ps1` and `Deploy-EventsSitemap.ps1` — add `$DataverseUrl` param with env var fallback
- [ ] Add secret expiry monitoring to validation script

### Phase 2: Runtime Resolution Library
- [ ] Add `resolveRuntimeConfig()` to `@spaarke/auth` — Dataverse Environment Variable query via REST
- [ ] Investigate `Xrm.Utility.getGlobalContext()` for MSAL client ID + tenant ID (avoids chicken-and-egg)
- [ ] Remove hardcoded dev defaults from `@spaarke/auth/config.ts` (DEFAULT_CLIENT_ID, DEFAULT_BFF_SCOPE)
- [ ] Remove hardcoded dev defaults from `environmentVariables.ts` (fail loudly instead)
- [ ] Standardize window global names (`__SPAARKE_BFF_BASE_URL__` everywhere, fix SpeAdminApp variant)
- [ ] Standardize BFF scope format (`user_impersonation` everywhere, fix `SDAP.Access` variant)

### Phase 3: Code Page Migration (Incremental)
- [ ] Migrate AnalysisWorkspace (pilot) — verify runtime resolution pattern
- [ ] Migrate remaining code pages (batch): PlaybookBuilder, SprkChatPane, LegalWorkspace, DocumentUploadWizard, SpeAdminApp
- [ ] Migrate deprecated code pages: DocumentRelationshipViewer, SemanticSearch
- [ ] Remove `.env.production` BFF URL from all migrated code pages
- [ ] Remove DefinePlugin shim from webpack configs (no longer needed for BFF URL)

### Phase 4: PCF Control Migration
- [ ] Migrate UniversalQuickCreate → `@spaarke/auth` + `environmentVariables.ts`
- [ ] Migrate DocumentRelationshipViewer PCF → same
- [ ] Migrate SemanticSearchControl PCF → same
- [ ] Remove all hardcoded client IDs, tenant IDs, redirect URIs from PCF controls

### Phase 5: Office Add-ins & External SPA
- [ ] Parameterize Office Add-in manifest URLs per environment
- [ ] Ensure webpack injects env vars from CI/CD for add-in builds
- [ ] Remove hardcoded dev fallbacks from `authConfig.ts`
- [ ] Verify External SPA token substitution pattern works end-to-end

### Phase 6: Validation & Cleanup
- [ ] Run `Validate-DeployedEnvironment.ps1` in dev — confirm all checks pass
- [ ] Deploy to staging — verify all components resolve correctly with zero manual patching
- [ ] Remove deprecated `msalConfig.ts` files (after migration)
- [ ] Remove deprecated `bffConfig.ts` files that used build-time resolution
- [ ] Update deployment guides (HOW-TO, PRODUCTION-DEPLOYMENT-GUIDE) with new process
- [ ] Remove `.env.production` files that are no longer needed
- [ ] Document secret rotation procedure and expiry dates

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| MSAL client ID chicken-and-egg (needed before auth) | Investigate Xrm context; keep build-time fallback as safety net |
| Runtime Dataverse query adds ~100ms to page load | Cache for 5 min (same as PCF pattern); query is tiny |
| Removing dev defaults breaks local development | `.env.development` files remain for local dev; only `.env.production` changes |
| Migration breaks existing code pages | Incremental rollout — one code page at a time, verify, then batch |
| Provisioning script doesn't set env vars | `Validate-DeployedEnvironment.ps1` catches missing vars immediately |

---

## Success Criteria

1. **Build once, deploy anywhere**: Code pages and PCF controls work in any environment without rebuild
2. **Single source of truth**: `Provision-Customer.ps1` creates all environment-specific config
3. **Automatic validation**: `Validate-DeployedEnvironment.ps1` confirms all components can resolve their config
4. **Zero hardcoded dev values** in production-facing code (only in `.env.development` for local dev)
5. **Fail loudly**: Missing configuration produces clear error messages, never silently falls back to dev
