# Azure Resource Naming Convention

> **Version**: 3.0
> **Date**: 2026-03-25
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified (v3 — replaces v2)

> **Verification note (2026-04-05)**: Confirmed dev legacy resource names match root CLAUDE.md Azure Infrastructure Resources section: `spe-infrastructure-westus2`, `spe-api-dev-67e2xz`, `spaarke-spekvcert`, `spaarke-openai-dev`, `spaarke-docintel-dev`, `spaarke-search-dev`. v3 patterns are the authoritative standard for new environments.

## Overview

This document establishes the **authoritative naming convention** for all Azure resources, Azure subscriptions, Dataverse components, SharePoint Embedded (SPE) resources, code namespaces, Service Bus queues, Redis keys, and Entra ID registrations in the Spaarke deployment package.

**v3 changes from v2**:
- Replaced "shared platform" + "per-customer" model with **per-environment** model
- AI resources are **per-environment** (not shared) for data separation
- Added **Azure subscription organization** and billing structure
- Added **SPE container type** naming convention
- Removed ambiguous `-prod` suffix on environment-specific resources
- Added Dataverse solution naming for ALM (SpaarkeCore, SpaarkeFeatures)

All new resources **MUST** follow these conventions. The existing dev environment retains its legacy names; new environments (demo, prod, customer-dedicated) start clean.

---

## Naming Principles

1. **Two standard prefixes only**: `sprk_` (Dataverse/code — underscore required by platform) and `spaarke` (Azure resources — descriptive, recognizable)
2. **Descriptive names** — Every resource name should make its purpose obvious at a glance. No cryptic abbreviations, random suffixes, or legacy holdovers
3. **Consistent structure** — Same pattern for every resource of the same type
4. **Environment is the primary organizer** — The environment name (`dev`, `demo`, `prod`) identifies the deployment purpose, NOT the Dataverse environment type
5. **One resource group per environment** — Each environment gets a single resource group containing ALL its resources (BFF API, AI, storage, etc.)
6. **No legacy prefixes** — `spe-*` and `sdap-*` prefixes are **prohibited** for new resources

---

## Azure Subscription Organization

Each environment gets its own Azure subscription for clean cost allocation, quota isolation, and RBAC boundaries.

```
Billing Account: Spaarke
└── Management Group: Spaarke Environments
    ├── Subscription: spaarke-dev            ← Development (existing: "Spaarke SPE Subscription 1")
    │   └── Dev resources (legacy names, not renamed)
    │
    ├── Subscription: spaarke-demo           ← Beta/demo environment
    │   └── rg-spaarke-demo                  ← All demo resources
    │
    ├── Subscription: spaarke-prod           ← Production (future - shared infra for paying customers)
    │   ├── rg-spaarke-prod                  ← Shared prod BFF API, AI services
    │   └── rg-spaarke-prod-{customer}       ← Per-customer resources (future)
    │
    └── Subscription: spaarke-{customer}     ← Dedicated customer (future, if needed)
        └── rg-spaarke-{customer}            ← All customer resources
```

**Cost tracking**: Each subscription has independent billing. Use Azure Cost Management > Cost Analysis to see per-subscription spend. Tag all resources with `environment` and `component` tags for drill-down.

**Required tags on all resources**:

| Tag | Values | Purpose |
|-----|--------|---------|
| `environment` | `dev`, `demo`, `prod`, `{customer}` | Cost allocation |
| `component` | `bff-api`, `ai`, `storage`, `cache`, `messaging` | Component-level costs |
| `managedBy` | `bicep`, `manual`, `script` | Track how resource was created |

---

## Naming Patterns

### Azure Resources

Use `spaarke-` for resources with generous name length limits, or `sprk-` when constrained to 24 characters or fewer. The `{env}` segment is the **environment name** (dev, demo, prod) — NOT the Dataverse environment type.

```
Standard (>24 chars allowed):    spaarke-{purpose}-{env}
Short (<=24 char limit):         sprk-{env}-{purpose}
Storage accounts (no hyphens):   sprk{env}sa
Per-customer (future):           spaarke-{purpose}-{env}-{customer}
```

### Dataverse Components

All Dataverse components use the `sprk_` publisher prefix (underscore required by platform).

```
Tables:            sprk_{entityname}           e.g., sprk_documentprofile
Columns:           sprk_{columnname}           e.g., sprk_containerid
Security Roles:    Spaarke - {Role Name}       e.g., Spaarke - Standard User
Solutions:         Spaarke{Feature}             e.g., SpaarkeCore, SpaarkeFeatures
Environment Vars:  sprk_{VariableName}         e.g., sprk_BffApiBaseUrl
Web Resources:     sprk_{resourcename}         e.g., sprk_documentviewer.html
PCF Controls:      sprk_{ControlName}          e.g., sprk_AiToolAgent
```

### Dataverse Solutions (ALM)

| Solution | Purpose | Update Strategy |
|----------|---------|-----------------|
| `SpaarkeCore` | Schema: entities, fields, option sets, security roles, sitemaps, model-driven apps, environment variable definitions, business rules, dashboards, connection roles, modifications to standard entities | Full re-export + re-import (unmanaged during beta, managed for prod customers) |
| `SpaarkeFeatures` | UI: web resources, PCF controls, code pages, ribbon customizations, icons | Full re-export + re-import per release |

### SharePoint Embedded (SPE) Resources

```
Container Type Name:   Spaarke {Environment} Documents    e.g., "Spaarke Demo Documents"
Container Type Owner:  BFF API app registration for that environment
Container Display:     {BusinessUnit} Documents           e.g., "Root BU Documents"
```

| SPE Resource | Pattern | Dev Example | Demo Example |
|-------------|---------|-------------|--------------|
| Container Type Name | `Spaarke {Env} Documents` | `Spaarke PAYGO 1` (legacy) | `Spaarke Demo Documents` |
| Container Type Owner | BFF API app for that env | `170c98e1-...` (legacy PCF app) | `{demo-bff-app-id}` |
| Default Container | `{env} Root Documents` | _(existing)_ | `Demo Root Documents` |

### Code Components

```
.NET Namespaces:   Sprk.{Area}.{Component}     e.g., Sprk.Bff.Api
npm Packages:      @spaarke/{package}           e.g., @spaarke/ui-components
PCF Projects:      Sprk{ControlName}            e.g., SprkDocumentProfile
Solution Projects: Sprk.{Purpose}               e.g., Sprk.Plugins.Validation
```

### Service Bus Queues

Queue names are descriptive and do not need a prefix — they are scoped by the Service Bus namespace.

```
document-processing          (replaces legacy: sdap-jobs)
document-indexing
ai-indexing
sdap-communication
sdap-jobs
```

### Redis Key Prefixes

```
sprk-{env}:{area}:{key}     e.g., sprk-demo:graph:token:{hash}
```

### Entra ID App Registrations

Each environment gets its **own app registrations** to ensure credential and scope isolation.

| Registration | Pattern | Dev | Demo |
|-------------|---------|-----|------|
| BFF API | `Spaarke BFF API - {Env}` | `spe-bff-api` (legacy) | `Spaarke BFF API - Demo` |
| Dataverse S2S | `Spaarke Dataverse S2S - {Env}` | _(shared)_ | `Spaarke Dataverse S2S - Demo` |
| UI SPA (MSAL) | `Spaarke UI - {Env}` | _(existing)_ | `Spaarke UI - Demo` |
| API Scope URI | `api://{appId}/user_impersonation` | `api://spe-bff-api/...` (legacy) | `api://{guid}/user_impersonation` |

---

## Complete Resource Naming Matrix

### Per-Environment Resources

Each environment gets **one resource group** containing ALL its resources. No shared platform resources across environments — each environment is fully self-contained for data separation.

| Resource Type | Max Length | Pattern | Demo Example | Prod Example |
|--------------|------------|---------|--------------|--------------|
| Resource Group | 90 | `rg-spaarke-{env}` | `rg-spaarke-demo` | `rg-spaarke-prod` |
| Key Vault | 24 | `sprk-{env}-kv` | `sprk-demo-kv` | `sprk-prod-kv` |
| App Service Plan | 40 | `spaarke-{env}-plan` | `spaarke-demo-plan` | `spaarke-prod-plan` |
| App Service (BFF API) | 60 | `spaarke-bff-{env}` | `spaarke-bff-demo` | `spaarke-bff-prod` |
| App Service Slot | — | `{app-service}/staging` | `spaarke-bff-demo/staging` | `spaarke-bff-prod/staging` |
| Azure OpenAI | 64 | `spaarke-openai-{env}` | `spaarke-openai-demo` | `spaarke-openai-prod` |
| AI Search | 60 | `spaarke-search-{env}` | `spaarke-search-demo` | `spaarke-search-prod` |
| Document Intelligence | 64 | `spaarke-docintel-{env}` | `spaarke-docintel-demo` | `spaarke-docintel-prod` |
| Service Bus Namespace | 50 | `spaarke-{env}-sbus` | `spaarke-demo-sbus` | `spaarke-prod-sbus` |
| Service Bus Queue | 260 | `{purpose}` | `document-processing` | `document-processing` |
| Redis Cache | 63 | `spaarke-{env}-cache` | `spaarke-demo-cache` | `spaarke-prod-cache` |
| Redis Key Prefix | N/A | `sprk-{env}:` | `sprk-demo:` | `sprk-prod:` |
| Storage Account | 24 | `sprk{env}sa` | `sprkdemosa` | `sprkprodsa` |
| Application Insights | 255 | `spaarke-{env}-insights` | `spaarke-demo-insights` | `spaarke-prod-insights` |
| Log Analytics | 63 | `spaarke-{env}-logs` | `spaarke-demo-logs` | `spaarke-prod-logs` |
| AI Foundry Hub | 63 | `sprk-{env}-aif-hub` | `sprk-demo-aif-hub` | `sprk-prod-aif-hub` |
| AI Foundry Project | 63 | `sprk-{env}-aif-proj` | `sprk-demo-aif-proj` | `sprk-prod-aif-proj` |
| AI Foundry Storage | 24 | `sprk{env}aifsa` | `sprkdemoaifsa` | `sprkprodaifsa` |
| App Registration (BFF) | 120 | `Spaarke BFF API - {Env}` | `Spaarke BFF API - Demo` | `Spaarke BFF API - Production` |
| App Registration (DV) | 120 | `Spaarke Dataverse S2S - {Env}` | `Spaarke Dataverse S2S - Demo` | `Spaarke Dataverse S2S - Production` |
| App Registration (UI) | 120 | `Spaarke UI - {Env}` | `Spaarke UI - Demo` | `Spaarke UI - Production` |
| API Scope URI | N/A | `api://{appId}/user_impersonation` | Use Application ID GUID | Use Application ID GUID |

**API Scope URI**: Always use the Application ID GUID, not a friendly name. This avoids coupling scope URIs to human-readable names.

---

### Demo Environment Full Resource Inventory (`rg-spaarke-demo`)

| Resource Type | Name | Purpose |
|--------------|------|---------|
| Resource Group | `rg-spaarke-demo` | All demo environment resources |
| App Service Plan | `spaarke-demo-plan` | Compute (B1 or P1v3) |
| App Service (BFF API) | `spaarke-bff-demo` | BFF API for demo |
| App Service Slot | `spaarke-bff-demo/staging` | Zero-downtime deploy |
| Key Vault | `sprk-demo-kv` | Demo secrets |
| Azure OpenAI | `spaarke-openai-demo` | Demo AI models |
| AI Search | `spaarke-search-demo` | Demo search indexes |
| Document Intelligence | `spaarke-docintel-demo` | Demo document processing |
| Service Bus | `spaarke-demo-sbus` | Demo async messaging |
| Redis Cache | `spaarke-demo-cache` | Demo caching |
| Storage Account | `sprkdemosa` | Demo file storage |
| App Insights | `spaarke-demo-insights` | Demo monitoring |
| Log Analytics | `spaarke-demo-logs` | Demo log aggregation |

### Per-Customer Resources (Future — Production Multi-Tenant)

When production supports multiple paying customers, each gets isolated data resources within the prod subscription:

| Resource Type | Pattern | Example (Acme) |
|--------------|---------|----------------|
| Resource Group | `rg-spaarke-prod-{customer}` | `rg-spaarke-prod-acme` |
| Storage Account | `sprk{customer}sa` | `sprkacmesa` |
| Key Vault | `sprk-{customer}-kv` | `sprk-acme-kv` |
| Service Bus Namespace | `spaarke-{customer}-sbus` | `spaarke-acme-sbus` |
| Redis Cache | `spaarke-{customer}-cache` | `spaarke-acme-cache` |

> These customer resources share the environment-level AI services and BFF API from `rg-spaarke-prod`.

---

### SharePoint Embedded (SPE) Resources

| SPE Resource | Pattern | Dev (Legacy) | Demo |
|-------------|---------|--------------|------|
| Container Type Name | `Spaarke {Env} Documents` | `Spaarke PAYGO 1` | `Spaarke Demo Documents` |
| Container Type Owner | BFF API app for env | `170c98e1-...` (legacy) | Demo BFF API app ID |
| Default Container | `{Env} Root Documents` | _(existing)_ | `Demo Root Documents` |

Each environment gets its **own Container Type** owned by that environment's BFF API app registration. This ensures:
- Data separation between environments
- Independent permission management
- No cross-environment container access

---

### Dataverse Environments

| Environment | URL Pattern | Dataverse Type | Purpose |
|-------------|-------------|----------------|---------|
| Dev | `spaarkedev1.crm.dynamics.com` | Sandbox | Development/testing (legacy name) |
| Demo | `spaarke-demo.crm.dynamics.com` | Production | Beta testers, demos |
| Prod | `spaarke-prod.crm.dynamics.com` | Production | Paying customers (future) |
| Customer | `spaarke-{customer}.crm.dynamics.com` | Production | Dedicated customer (future) |

---

## Current State & Migration Plan

### Existing Resources (R1 Deployment — March 2026)

R1 created resources using v2 naming with `-prod` suffix. These need to be migrated to v3 naming.

#### Resources to Rename/Recreate for Demo

| Current Name (v2) | New Name (v3) | Action |
|-------------------|---------------|--------|
| `rg-spaarke-demo-prod` | `rg-spaarke-demo` | Rename or recreate |
| `sprk-demo-prod-kv` | `sprk-demo-kv` | Recreate (Key Vaults can't be renamed) |
| `spaarke-demo-prod-cache` | `spaarke-demo-cache` | Recreate |
| `sprkdemoprodsa` | `sprkdemosa` | Recreate |
| `spaarke-demo-prod-sbus` | `spaarke-demo-sbus` | Recreate |

#### Resources to Create (New for Demo)

| Resource | Name | Notes |
|----------|------|-------|
| App Service Plan | `spaarke-demo-plan` | New |
| App Service (BFF API) | `spaarke-bff-demo` | New — each env gets its own BFF API |
| Azure OpenAI | `spaarke-openai-demo` | New — per-env for data separation |
| AI Search | `spaarke-search-demo` | New — per-env |
| Document Intelligence | `spaarke-docintel-demo` | New — per-env |
| App Insights | `spaarke-demo-insights` | New |
| Log Analytics | `spaarke-demo-logs` | New |

#### Existing "Platform" Resources (R1) — Disposition

| Current Name | Decision | Reason |
|-------------|----------|--------|
| `rg-spaarke-platform-prod` | **Rename to `rg-spaarke-legacy-r1`** or delete | No longer fits the per-env model |
| `spaarke-bff-prod` | **Keep temporarily** — currently serves dev Dataverse | Reconfigure or replace per env |
| `sprk-platform-prod-kv` | **Keep as dev secrets** until dev gets proper Key Vault | Contains dev-pointing secrets |
| `spaarke-openai-prod` | **Rename or keep as dev AI** | Currently used by dev BFF API |
| `spaarke-search-prod` | **Rename or keep as dev AI** | Same |
| `spaarke-docintel-prod` | **Rename or keep as dev AI** | Same |

### Dev Environment (DO NOT RENAME)

The dev environment retains its legacy names. Documented for reference only.

| Resource Type | Current Dev Name | Issue |
|--------------|-----------------|-------|
| Resource Group | `spe-infrastructure-westus2` | Uses legacy `spe` prefix |
| App Service | `spe-api-dev-67e2xz` | Uses legacy `spe` prefix + random suffix |
| Key Vault | `spaarke-spekvcert` | Mixed `spaarke` + `spe` |
| App Registration | `spe-bff-api` | Uses legacy `spe` prefix |
| API Scope | `api://spe-bff-api/user_impersonation` | Uses legacy `spe` prefix |
| Service Bus Namespace | `spaarke-servicebus-dev` | `spaarke` prefix (acceptable) |
| Service Bus Queue | `sdap-jobs` | Uses legacy `sdap` prefix |

---

## Configuration Strategy

Each environment uses **Key Vault references** in App Service settings. The Key Vault name is the only environment-specific value in `appsettings.{Environment}.json`:

```json
// appsettings.Demo.json
{
  "KeyVault": {
    "VaultName": "sprk-demo-kv"
  }
}
```

All other secrets resolve at runtime via Key Vault references:

```bash
# App Service Configuration (Key Vault references — same secret names across all environments)
Dataverse__ServiceUrl = @Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=Dataverse-ServiceUrl)
AzureOpenAI__Endpoint = @Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=AzureOpenAI-Endpoint)
# etc.
```

This means the **same BFF API code artifact** deploys to any environment — only the Key Vault name and its secrets differ.

---

## Automation Auth Requirements

For fully automated provisioning (CI/CD, `Provision-Customer.ps1`), these service principal permissions are required:

| Service Principal | Scope | Required Roles/Permissions |
|-------------------|-------|----------------------------|
| `Spaarke Provisioning SP` | Azure Subscription | Contributor, Key Vault Secrets Officer |
| `Spaarke Provisioning SP` | Entra ID | Application Administrator (app registrations) |
| `Spaarke BFF API - {Env}` | Microsoft Graph | `FileStorageContainer.Selected` (Application) |
| `Spaarke BFF API - {Env}` | SharePoint | `Container.Selected` (Application) |
| `Spaarke Dataverse S2S - {Env}` | Dataverse | System Administrator security role |
| PAC CLI auth | Dataverse | System Administrator (for solution import) |

**For manual provisioning** (current approach): Azure CLI interactive login (`az login`) + PAC CLI interactive login (`pac auth create`) are sufficient.

---

## Reference: Azure Naming Limits

| Resource | Max Length | Valid Characters |
|----------|-----------|------------------|
| Resource Group | 90 | Alphanumerics, underscores, hyphens, periods, parentheses |
| Key Vault | 24 | Alphanumerics and hyphens (start with letter) |
| Storage Account | 24 | Lowercase letters and numbers only |
| App Service | 60 | Alphanumerics and hyphens |
| Service Bus | 50 | Alphanumerics and hyphens (start with letter) |
| Redis | 63 | Alphanumerics and hyphens |
| AI Foundry Hub/Project | 63 | Alphanumerics and hyphens (start with letter) |
| Azure OpenAI | 64 | Alphanumerics and hyphens (start with letter) |
| Azure AI Search | 60 | Lowercase alphanumerics and hyphens (start with letter) |
| App Insights | 255 | Alphanumerics, hyphens, underscores, periods |
| Log Analytics | 63 | Alphanumerics and hyphens |
| Document Intelligence | 64 | Alphanumerics and hyphens (start with letter) |

---

*v3 Adopted: March 25, 2026. Supersedes v2 (March 13, 2026). This is the authoritative naming reference for all Spaarke resources.*
