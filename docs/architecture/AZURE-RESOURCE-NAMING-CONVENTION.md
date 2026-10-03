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
Solution Projects: Sprk.{Purpose}               e.g., Sprk.Bff.Api   (no plugin projects — ADR-002)
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
| App Registration (UI) | 120 | `Spaarke UI - {Env}` | `Spaarke UI - Demo` | `Spaarke UI - Production` |
| API Scope URI | N/A | `api://{appId}/user_impersonation` | Use Application ID GUID | Use Application ID GUID |
| Managed Identity (User-Assigned) | 128 | `mi-{purpose}-{env}` | `mi-bff-api-demo` | `mi-bff-api-prod` |
| Cosmos DB account (Serverless, SQL API) | 44 | `spaarke-cosmos-{purpose}-{env}` | `spaarke-cosmos-demo-ai` | `spaarke-cosmos-prod-ai` |

**API Scope URI**: Always use the Application ID GUID, not a friendly name. This avoids coupling scope URIs to human-readable names.

**Managed Identity + Cosmos DB**: User-Assigned Managed Identities (UAMI) follow `mi-{purpose}-{env}` (e.g., `mi-bff-api-dev`, `mi-bff-api-demo`). Cosmos DB accounts use Serverless + SQL API; the database is named `spaarke-ai` with 5 containers (`sessions`, `prompts`, `audit`, `memory`, `feedback`) partitioned on `/tenantId`. See [`projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md`](../../projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md) Phase 5 task 060 (demo prep) for the canonical step-by-step provisioning of a new environment, including Graph app-role grants, Cosmos data-plane RBAC, and the full App Settings configuration block.

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

### Per-Customer Resources

Each customer gets its own resource group — and per [ADR-027](../adr/ADR-027-azure-subscription-topology.md)
as amended, its own **subscription**. Patterns below are taken from the deploying Bicep, not from intent.
`infrastructure/bicep/customer.bicep` is the **only** customer-stamp template (owner decision D19,
2026-10-02), deployed by the L2 control plane's handler H2a; the "Source" column names its variable/param:

| Resource Type | Pattern | Example (`acme`, prod) | Source (`customer.bicep`) |
|---|---|---|---|
| Resource Group | `rg-spaarke-{customerId}-{env}` | `rg-spaarke-acme-prod` | `var resourceGroupName` |
| UAMI | `mi-spaarke-{customerId}-{env}` | `mi-spaarke-acme-prod` | `module uami` → `name` |
| Key Vault | `take('sprk-{customerId}-{env}-kv', 24)` | `sprk-acme-prod-kv` | `param keyVaultName` (default; overridable only for a registered naming exception) |
| Storage Account | `sprk{customerId}{env}sa` | `sprkacmeprodsa` | `var storageAccountName` (hyphens stripped, lowercased, capped 24) |
| Service Bus | `spaarke-{customerId}-{env}-sbus` | `spaarke-acme-prod-sbus` | `var serviceBusName` (`-sb` is reserved by Azure) |
| Cosmos DB | `spaarke-{customerId}-{env}-cosmos` | `spaarke-acme-prod-cosmos` | `var cosmosAccountName` (capped 44) |
| Azure OpenAI · AI Search · Document Intelligence · Redis · App Insights · Log Analytics | `sprk-{customerId}-{env}-{openai\|search\|docintel\|redis\|insights\|logs}` | `sprk-acme-prod-openai` | `var openAiName` / `searchServiceName` / `docIntelligenceName` / `redisCacheName` / `appInsightsName` / `logAnalyticsName` |
| App Service Plan · BFF App Service | `sprk-{customerId}-{env}-{plan\|api}` | `sprk-acme-prod-api` | `module appServicePlan` → `planName`, `module bffApi` → `appServiceName` |
| SignalR · ACS (only when enabled) | `sprk-{customerId}-{env}-{signalr\|acs}` | `sprk-acme-prod-signalr` | `var signalrName` / `acsResourceName` |

> ⚠️ **Corrected 2026-09-29 (task 124).** This table previously gave `rg-spaarke-prod-{customer}` — env
> BEFORE customer — which is the reverse of what the Bicep deploys, and omitted `{env}` from the other
> patterns. It also closed with *"These customer resources share the environment-level AI services and BFF
> API from `rg-spaarke-prod`"*, which **D-12 retired**: there is no shared tier. Each customer's stamp
> includes its own BFF, Redis, OpenAI and Search.
>
> **One Key Vault form.** `customer.bicep` composes `take('sprk-{customerId}-{env}-kv', 24)`, which is what
> sets the length limit below. The separator-less `sprk{customerId}{env}-kv` form and the
> `sprk-{env}-{customerId}-uami` identity name belonged to `stacks/model2-full.bicep`, which deployed no live
> environment *(retired by task 249, 2026-10-02 — file deleted; git history keeps it)*.

---

### The `customerId` standard

**`customerId` is 3–8 characters, lowercase letters and digits, starting with a letter.**

It is the identifier of record for a customer and the root of every per-customer resource name. Assigned at
provisioning intake and stored on `sprk_dataverseenvironment.sprk_customerid`; Bicep **consumes** it and
never mints one.

#### Why 8 — the derivation, not a preference

`customer.bicep` composes its Key Vault name as `take(format('sprk-{0}-{1}-kv', customerId, environmentName), 24)`.
Azure Key Vault names are 3–24 characters and **may not begin or end with a hyphen**. The binding case is
`environmentName = 'staging'`, the longest allowed value:

| `customerId` length | Resulting name | Outcome |
|---|---|---|
| 8 | `sprk-xxxxxxxx-staging-kv` | ✅ complete, exactly 24 |
| 9 | `sprk-xxxxxxxxx-staging-k` | ⚠️ truncated — loses the `v` |
| **10** | `sprk-xxxxxxxxxx-staging-` | 🔴 **INVALID — trailing hyphen; Azure rejects it and the deployment fails** |

The parameter previously allowed 10, so it admitted a value that cannot deploy. `take()` concealed it: the
name was silently shortened rather than the template refusing, so the failure surfaced from Azure rather
than from the template.

#### Why lowercase letters and digits only

The storage-account name is `take(toLower(replace('sprk{customerId}{env}sa', '-', '')), 24)` — it **strips
hyphens**. So `acme-x` and `acmex` resolve to the *same* storage account name, and nothing detects the
collision.

🔴 **This rule cannot be enforced in Bicep.** ARM has no regex constraint on parameters — there is no
`@pattern` decorator, and this repo has no `bicepconfig.json` enabling the experimental assertions feature.
Only the **length** is enforceable in the template, and it is. The character rule is therefore enforced
**where the value is assigned** — at provisioning intake — and the Bicep parameter documents it so the two
cannot drift apart unnoticed.

The leading-letter rule is a **readability convention, not an Azure requirement**: every composed name
already begins with the `sprk` prefix, which satisfies the platform's start-character rules on its own.

#### Which rules are HARD, and where each can be enforced

🔴 **These three rules are not equally binding, and only one of them can be enforced by a Dataverse column.**
Treating them as one rule is how a late, opaque deploy-time failure gets built in.

| Rule | Hard? | Dataverse column | Bicep/ARM | Consequence if violated |
|---|---|---|---|---|
| **max 8** | ✅ Azure-derived | ✅ `MaxLength = 8` | ✅ `@maxLength(8)` | 🔴 deployment **FAILS** — Key Vault name ends in a hyphen |
| **lowercase letters + digits** | ✅ collision risk | ❌ no regex on text columns | ❌ no `@pattern` | 🔴 **SILENT** — `acme-x` and `acmex` share one storage account |
| **min 3** | ❌ **convention only** | ❌ Dataverse has no minimum | ✅ `@minLength(3)` | nothing breaks — every composed name is ≥10 chars even at length 1, because of the `sprk` / `rg-spaarke-` prefixes |

**Why this matters for where the value is created.** A Dataverse text column enforces a maximum length but
has **no minimum and no regex**. If `customerId` is first typed into Dataverse, the column can catch the one
rule that would break a deployment (set `MaxLength = 8`) and cannot catch the other two.

That is acceptable **only because of how the three rules fall**:

- the rule Dataverse CAN enforce is the one that would otherwise fail a deployment;
- the rule it cannot enforce and that MATTERS (character set) needs code-level validation at intake anyway,
  because ARM cannot enforce it either;
- the rule it cannot enforce and that it would be brittle to depend on (min 3) **has no technical
  consequence** — it is a readability convention. A 2-character id deploys perfectly well.

**So: set `MaxLength = 8` on the column, validate the character set in the intake code path, and treat
min-3 as advisory.** Do not let min-3 become a late failure: `@minLength(3)` stays in the template as a
backstop, but intake should catch it first, and if it is ever hit in practice the correct response is to
relax it rather than to reject the customer.

#### Recommended form

```
^[a-z][a-z0-9]{2,7}$        acme · contoso · fabrikam · nwind
```

Names longer than 8 characters are abbreviated at intake — `northwind` → `nwind`. The abbreviation is a
decision made once, at onboarding, and recorded on the registry row; it is not re-derived anywhere.

**Reserved: `platform`, `shared`, `byok`.** They match the pattern but already occupy the customerId position in
non-customer resource-group names — `rg-spaarke-platform-{env}` (the BFF and the L2 control plane),
`rg-spaarke-shared-{env}` (Spaarke's shared production resources — the Model 1 SPE billing account and, from its first prod deployment, the L2 control plane; formerly the retired Model 1 tier, D23 2026-10-03), `rg-spaarke-byok-prod`. A customer with one of these ids would
deploy into that group. Intake refuses them (`CustomerIdStandard.ReservedIds`), and the BFF refuses to derive them
at runtime (`CustomerIdResolver`).

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
| `Spaarke BFF API - {Env}` | Dataverse | System Administrator security role (single Dataverse Application User; the separate Dataverse S2S app was removed 2026-08-14 — task 060) |
| PAC CLI auth | Dataverse | System Administrator (for solution import) |

**For manual provisioning** (current approach): Azure CLI interactive login (`az login`) + PAC CLI interactive login (`pac auth create`) are sufficient.

---

## KV-Secret & Resource Naming Standard (Conformance-Gated)

> **Added by** `code-quality-and-assurance-r3` task 063 (owner productization directive, 2026-08-13).
> **Enforced by** `scripts/naming-conformance-check.ps1` (r3 owns the standard + the gate;
> `customer-provisioning-orchestration-r1` owns applying canonical names at provisioning time +
> remediating live-environment drift). Grounded in the FR-29 naming-drift census —
> `projects/code-quality-and-assurance-r3/workstreams/config-deployment/design.md` (task 017).

This section makes the env-agnostic principle stated above **explicit and enforceable**. It exists
because the convention was doc-only and had drifted (four vault-naming conventions, 3 casing styles for
one AI-Search key, env-token-baked names, orphan secrets — see the 017 census).

### The load-bearing rule — env-agnostic replicated names

A name that is **replicated across environments MUST be env-agnostic**: the environment lives in the
**value** (and in the vault name, which is legitimately per-env), never baked into the **secret or
resource name**.

| ✅ Conformant | ❌ Violation | Why |
|---|---|---|
| `Dataverse-ServiceUrl` | `SPRK-DEV-DATAVERSE-URL` | env token (`DEV`) baked into a replicated secret name |
| ~~`BFF-API-ClientSecret` (one casing everywhere)~~ | ~~`BFF-API-ClientSecret` + `bff-api-client-secret`~~ | **RESOLVED 2026-08-24** — both deleted (auth-v4 task 033); the BFF identity is secret-free, so the casing-drift hazard is gone rather than reconciled. The rule itself still applies to every other secret |
| vault `sprk-demo-kv` / `sprk-prod-kv` | vault `sprk-platform-prod-kv`, `kv-sdap-dev`, `spaarke-kv-dev` | non-canonical vault form (extra qualifier / wrong scheme) |

### The four standard rules

1. **Env-agnostic secret names (R1)** — no `DEV/DEMO/PROD/UAT/TEST/STAGING/SANDBOX/QA` token as a
   delimited segment of a KV-secret name. The per-environment difference is the secret **value** and the
   **vault** it lives in, never the secret name.
2. **One canonical casing per logical secret (R2)** — a logical secret has exactly ONE spelling across
   template, seeder, IaC, and tokens doc. Canonical casing for **new** KV secrets is **kebab-case**
   (`communication-webhook-signing-key`); existing PascalCase live secrets (`BFF-API-ClientSecret`) are
   grandfathered but MUST NOT gain a second casing. Never two casings for one value simultaneously.
3. **Canonical vault name (R3)** — `sprk-{env}-kv` (e.g. `sprk-demo-kv`). **Codified legacy exception
   (DO-NOT-RENAME): `spaarke-spekvcert`** — the only live dev vault; bicep accepts the vault name as a
   parameter rather than hardcoding a divergent form. `kv-sdap-{env}`, `spaarke-kv-dev`,
   `sprkshareddev-kv`, and `sprk-{workload}-{env}-kv` are drift.
4. **No orphan / duplicate secrets** — every secret the template/app reads is provisioned by the seeder
   under exactly that canonical name; no alias fan-out (one value → one name), no orphan (a referenced
   secret never seeded), no duplicate (one value under multiple names).

### Reference syntax (single form)

KV references use the Key Vault-reference form with an env-parameterized vault name:
`@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=<Canonical-Name>)`. Do not mix the
`#{KEY_VAULT_NAME}#`/SecretUri token schemes for the same value.

### Enforcement

`scripts/naming-conformance-check.ps1` implements rules R1–R3 (read-only; renames nothing). It runs
`-SelfTest` (a seeded env-token/casing violation MUST fail, conformant names MUST pass) and scans the
canonical secret-name sources. **Activation is per-surface + advisory-until-remediated**: because the
live environments still carry the 017-census drift (r1's remediation backlog), the gate runs
**advisory** (reports, does not block) until `customer-provisioning-orchestration-r1` applies the
current→canonical rename map; it flips to **blocking** per-surface as each surface reaches zero
violations. This is the same ownership seam as the Graph app-role constants (task 062): r3 owns the
guardrail, r1 owns the live-environment application. The blocking-gate wiring into `.github/workflows`
is a coordinated follow-on with `ci-cd-unit-test-remediation-r1` (owns existing-workflow edits) — see
`projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`.

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
