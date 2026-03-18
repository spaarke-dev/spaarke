# Production Environment Setup — Design Document

## Document Information

| Field | Value |
|-------|-------|
| **Author** | Ralph Schroeder |
| **Date** | 2026-03-11 |
| **Version** | 1.0 |
| **Status** | Draft |
| **Project** | production-environment-setup-r1 |

---

## 1. Executive Summary

Spaarke currently runs in a single dev environment with legacy naming, manual deployment steps, and no formalized process for standing up new environments. This project delivers the infrastructure, automation, and operational procedures to deploy Spaarke to production — starting with a demo environment that validates the end-to-end process, then enabling repeatable customer onboarding.

The architecture follows a **hybrid model**: shared platform resources (BFF API, AI services, monitoring) deployed once, with per-customer resources (Storage, Key Vault, Service Bus, Redis, Dataverse environment) provisioned on demand. This balances cost efficiency with customer isolation and supports both Spaarke-hosted and customer-hosted SPE scenarios.

---

## 2. Problem Statement

### Current State

- **One environment**: `spe-api-dev-67e2xz` serves development and testing
- **Legacy naming**: Resources use mixed `spe-*`, `sdap-*`, and `spaarke-*` prefixes (convention exists but is "Proposed" status)
- **Manual deployment**: No production deployment has ever been performed. Post-deployment steps (app registration, SPE setup, solution import) are manual
- **Missing automation**: 6 deployment scripts referenced in `infrastructure/README.md` don't exist. 3 deployment guides are TODO
- **No customer provisioning**: No process or tooling to onboard a new customer
- **Single-tenant configuration**: BFF API configured for one Entra ID tenant, one Dataverse environment, one SPE container type

### What We Need

1. A **shared platform** deployed to production Azure resources with proper naming, SKUs, and security
2. A **repeatable customer provisioning process** — scriptable, documented, testable
3. A **demo environment** as the first "customer" to validate the entire process end-to-end
4. **Bicep restructuring** — split existing templates into platform + customer stacks
5. **Deployment scripts** that automate the full sequence: infrastructure → secrets → config → app deployment → Dataverse → verification
6. **CI/CD integration** — GitHub Actions workflows for ongoing deployments

---

## 3. Architecture Decision: Hybrid Deployment Model (Path C)

After evaluating three deployment models, we've chosen the **hybrid approach**:

### Why Not Model 1 (Pure Shared)?

Model 1 (`model1-shared.bicep`) puts everything in one resource group. While cost-efficient, it creates multi-tenant complexity in every resource (shared Redis with customer prefixes, shared Service Bus with routing, shared storage with container-level isolation). For a first production release, this complexity isn't justified.

### Why Not Model 2 (Pure Dedicated)?

Model 2 (`model2-full.bicep`) duplicates everything per customer, including expensive AI services. At $800-1200/month baseline per customer, this doesn't scale. And the AI resources (OpenAI, AI Search, Doc Intelligence) don't benefit from per-customer isolation — they're stateless or index-scoped.

### Path C: Shared Platform + Per-Customer Data

```
SHARED PLATFORM (deploy once)                PER-CUSTOMER (deploy per onboarding)
rg-spaarke-platform-prod                     rg-spaarke-{customer}-prod
──────────────────────────                   ─────────────────────────────
App Service Plan + BFF API                   Storage Account
  ├── Tenant-aware request routing             └── Customer documents & processing
  ├── Per-customer rate limiting              Key Vault
  └── Autoscaling (P1v3, 1-4 instances)        └── Customer-specific secrets
                                              Service Bus Namespace
Azure OpenAI (shared, quota-managed)            └── Customer job queues
  ├── GPT-4o (150 capacity)                   Redis Cache
  ├── GPT-4o-mini (120 capacity)                └── Customer session/cache data
  └── text-embedding-3-large (350 cap)
                                              Dataverse Environment (dedicated)
AI Search (shared, index-per-customer)          ├── Spaarke managed solution
  └── Standard2, 2 replicas                    ├── Security roles & config
                                                └── Customer data
Document Intelligence (shared, stateless)
  └── S0 tier                                 SPE Containers
                                                ├── Spaarke-hosted (default)
App Insights + Log Analytics (shared)           └── Customer-hosted (enterprise)
  └── 180-day retention, tenant-tagged

Platform Key Vault
  └── Shared secrets (OpenAI keys, AI Search keys)
```

### Resource-by-Resource Justification

| Resource | Shared or Per-Customer | Reasoning |
|----------|:---------------------:|-----------|
| **App Service (BFF API)** | Shared | One deployment, tenant routing via auth context. Per-customer = N deployments to maintain |
| **Azure OpenAI** | Shared | Expensive, quota-based. Per-customer would multiply model deployments and costs. Use per-customer rate limiting in BFF |
| **AI Search** | Shared | Per-customer indexes within one service. Standard2 supports 200 indexes |
| **Document Intelligence** | Shared | Stateless — processes documents, returns results. No customer data stored |
| **App Insights + Log Analytics** | Shared | Centralized monitoring. Filter by `customerId` dimension |
| **Storage Account** | Per-Customer | Customer documents and processing files. Strongest case for isolation — data sovereignty, lifecycle, easy teardown |
| **Key Vault** | Per-Customer | Customer-specific credentials and connection strings. Clean access policies per customer |
| **Service Bus** | Per-Customer | Customer job queues. Isolation prevents one customer's job backlog from affecting others |
| **Redis Cache** | Per-Customer | Customer cache and session data. Isolation prevents cache eviction contention |
| **Dataverse** | Per-Customer | Customer data, security roles, customizations. Dedicated environments = full isolation |
| **SPE Containers** | Per-Customer | Document storage — inherently per-customer (see spe-multi-tenant-architecture-r1 project) |

### Scaling Considerations for Shared Resources

| Resource | Risk Level | Bottleneck Point | Mitigation |
|----------|-----------|-------------------|------------|
| **App Service** | Low | 20+ concurrent customers | Autoscaling P1v3 (1-4 instances), rate limiting |
| **Azure OpenAI** | **High** | 10+ concurrent AI users | Per-customer token budgets, multi-deployment routing, quota increase requests |
| **AI Search** | Low | 200 indexes (100+ customers) | Add second instance, partition by customer tier |
| **Doc Intelligence** | Very Low | Bulk processing only | Queue-based throttling |

---

## 4. Scope

### In Scope

#### Domain A: Bicep Restructuring
- Split existing templates into `platform.bicep` and `customer.bicep`
- Reuse existing modules (`app-service.bicep`, `redis.bicep`, `service-bus.bicep`, `storage-account.bicep`, `openai.bicep`, `ai-search.bicep`, `monitoring.bicep`)
- Create production parameter files (`platform-prod.bicepparam`, `demo-customer.bicepparam`, `customer-template.bicepparam`)
- Validate Bicep with `az deployment group what-if` before deploying
- Retain existing Model 1 and Model 2 templates for reference/future use

#### Domain B: Platform Deployment (Shared Resources)
- Deploy shared platform resources to `rg-spaarke-platform-prod`
- App Service with production SKU (P1v3), autoscaling rules, deployment slots
- Azure OpenAI with production model deployments and capacity
- AI Search Standard2 with 2 replicas
- Document Intelligence S0
- Monitoring (App Insights + Log Analytics, 180-day retention)
- Platform Key Vault with shared secrets
- Custom domain + SSL for BFF API endpoint

#### Domain C: Customer Provisioning Automation
- `Provision-Customer.ps1` — end-to-end script to create per-customer resources:
  1. Validate customer parameters (ID, display name, Dataverse URL)
  2. Deploy customer Bicep template (`customer.bicep`)
  3. Retrieve deployment outputs (connection strings, endpoints)
  4. Populate customer Key Vault with secrets
  5. Register customer in platform configuration (BFF API tenant registry)
  6. Provision Dataverse environment (or configure existing one)
  7. Import Spaarke managed solutions into Dataverse
  8. Configure Dataverse environment variables
  9. Create SPE containers (via SPE multi-tenant project's provisioning API)
  10. Run smoke tests (health check, document upload/download, AI query)
- `Decommission-Customer.ps1` — tear down customer resources cleanly

#### Domain D: BFF API Deployment
- `Deploy-BffApi.ps1` — parameterized for any environment (currently hardcoded to dev)
- Build release artifact (`dotnet publish -c Release`)
- Deploy to staging slot → health check → swap to production
- Configure app settings via Key Vault references
- Parameterize: resource group, app service name, health check URL

#### Domain E: Dataverse Solution Deployment
- `Deploy-DataverseSolutions.ps1` — import all managed solutions to a target environment
- Solution import order (dependencies: SpaarkeCore first, then feature solutions)
- PAC CLI authentication per environment
- Solutions to import:
  - SpaarkeCore (core entities, roles)
  - AnalysisBuilder (Analysis PCF control)
  - DocumentUploadWizard (upload workflow)
  - EventCommands, EventDetailSidePane, EventsPage (events feature)
  - LegalWorkspace (workspace code page)
  - TodoDetailSidePane, CalendarSidePane (productivity features)
  - webresources (shared web resources)

#### Domain F: Demo Environment (First Customer)
- Deploy full platform + demo customer as validation of the entire process
- Demo-specific configuration:
  - Customer ID: `demo`
  - Dataverse: dedicated demo environment
  - SPE: Spaarke-hosted containers with sample documents
  - Users: B2B guest access for demo users
  - Non-confidential test data
  - No business unit segregation needed (single demo BU)
- Validate every step of the provisioning process works end-to-end
- Document gaps and issues discovered during demo deployment

#### Domain G: CI/CD Pipeline
- GitHub Actions workflow for platform deployment (`deploy-platform.yml`)
- GitHub Actions workflow for BFF API deployment (`deploy-bff-api.yml`)
  - Trigger: push to master (paths: `src/server/api/**`)
  - Steps: build → test → deploy staging → health check → swap to production
  - Environment protection rules (staging approval, production approval)
- GitHub Actions workflow for customer provisioning (`provision-customer.yml`)
  - Manual dispatch with customer parameters
  - Runs `Provision-Customer.ps1` with inputs
- Integration with existing `sdap-ci.yml` (build/test/quality gates)

#### Domain H: Resource & Component Naming Standard
- Define and enforce a clear, structured naming standard across all resources and components
- Two standard prefixes: `sprk_` (Dataverse/code) and `spaarke` (Azure resources)
- Descriptive, readable names — no cryptic abbreviations or random suffixes
- Complete naming matrix for: Azure resources, Dataverse components, Entra ID registrations, code namespaces, Service Bus queues, Redis keys
- Finalize `AZURE-RESOURCE-NAMING-CONVENTION.md` from "Proposed" to "Adopted" status
- Document mapping between legacy dev names and production names (dev stays as-is, production starts clean)
- Update BFF API configuration to use environment-agnostic setting names

#### Domain I: Operational Documentation
- Production deployment guide (step-by-step for platform + first customer)
- Customer onboarding runbook (repeatable procedure)
- Incident response procedures (what to do when a customer deployment fails)
- Secret rotation procedures (credential lifecycle management)
- Monitoring and alerting setup guide

### Out of Scope

- **SPE multi-tenant Graph auth** — Separate project (`spe-multi-tenant-architecture-r1`). This project assumes Spaarke-hosted SPE for demo; cross-tenant SPE handled there.
- **Self-service registration app** — Separate project (`spaarke-self-service-registration-app`). Consumes provisioning APIs built here.
- **Customer-hosted AI resources** — Future project. This project deploys Spaarke-hosted AI only.
- **Release management at scale (ring-based deployment)** — Future project when customer count exceeds ~20. This project handles 1-10 customers.
- **VNet/Private endpoints** — Handled by `production-performance-improvement-r1` (Tasks 040-046). Can be layered on after initial deployment.
- **Data migration** — No existing production data to migrate.
- **Power Platform licensing procurement** — Business/legal decision, not technical scope.
- **Disaster recovery / backup strategy** — Important but separate workstream.

### Affected Areas

| Area | Files/Components | Impact |
|------|-----------------|--------|
| Bicep templates | `infrastructure/bicep/` | New `platform.bicep`, `customer.bicep`, parameter files |
| Deployment scripts | `scripts/` | New provisioning, deployment, decommission scripts |
| GitHub Actions | `.github/workflows/` | New deployment workflows, updated existing CI |
| BFF API config | `appsettings.json`, `appsettings.Production.json` | Production configuration, Key Vault references |
| BFF API code | `Infrastructure/DI/`, `Configuration/` | Tenant configuration service (if not already handled by SPE project) |
| Dataverse solutions | `src/solutions/` | Ensure all build to managed solution ZIPs |
| Infrastructure docs | `infrastructure/README.md`, `docs/guides/` | Deployment guides, runbooks |
| Naming convention | `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` | Finalize from "Proposed" to "Adopted" |

---

## 5. Requirements

### Functional Requirements

1. **FR-01: Platform Deployment** — A single Bicep template (`platform.bicep`) MUST deploy all shared platform resources to a new resource group with production SKUs, using parameterized values for environment name, region, and capacity.

2. **FR-02: Customer Provisioning** — A single script (`Provision-Customer.ps1`) MUST create all per-customer Azure resources, configure Dataverse, import solutions, and register the customer in the BFF API — requiring only customer ID and display name as inputs.

3. **FR-03: Customer Decommissioning** — A script (`Decommission-Customer.ps1`) MUST cleanly remove all per-customer resources (Azure resource group, Dataverse environment or BU, SPE containers) without affecting other customers or the shared platform.

4. **FR-04: BFF API Deployment** — `Deploy-BffApi.ps1` MUST be parameterized to deploy to any environment (dev, staging, production) using deployment slots with zero-downtime swap.

5. **FR-05: Dataverse Solution Import** — `Deploy-DataverseSolutions.ps1` MUST import all Spaarke managed solutions to a target Dataverse environment in the correct dependency order, with rollback capability if any import fails.

6. **FR-06: Demo Environment** — The demo environment MUST be deployed using the same scripts and templates that will be used for real customers (no special-casing).

7. **FR-07: Health Verification** — Every deployment (platform, customer, BFF API, Dataverse) MUST include automated verification steps that confirm the deployment succeeded (health checks, connectivity tests, smoke tests).

8. **FR-08: Secret Management** — All credentials and connection strings MUST be stored in Azure Key Vault. App Service configuration MUST use Key Vault references (`@Microsoft.KeyVault(...)`). No secrets in app settings, Bicep parameters, or source code.

9. **FR-09: CI/CD Pipeline** — GitHub Actions workflows MUST exist for: (a) platform infrastructure deployment, (b) BFF API build/test/deploy, (c) customer provisioning. All with environment protection rules.

10. **FR-10: Idempotent Deployment** — Running any deployment script or Bicep template multiple times with the same parameters MUST produce the same result without errors (idempotent).

11. **FR-11: Naming Convention Compliance** — All new production resources MUST follow the adopted naming convention (`sprk*`/`spaarke-*` patterns per `AZURE-RESOURCE-NAMING-CONVENTION.md`).

12. **FR-12: Monitoring** — All production resources MUST report telemetry to shared App Insights. BFF API logs MUST include `customerId` for per-customer filtering.

### Non-Functional Requirements

- **NFR-01: Deployment Time** — Full customer provisioning (Azure + Dataverse + solutions + SPE) MUST complete in under 30 minutes.
- **NFR-02: Platform Availability** — Production BFF API MUST support zero-downtime deployments via deployment slots.
- **NFR-03: Isolation** — A single customer's resource failure MUST NOT impact other customers or the shared platform.
- **NFR-04: Cost Transparency** — Platform costs and per-customer costs MUST be trackable separately via resource group tagging.
- **NFR-05: Audit Trail** — All deployments MUST be logged (who deployed what, when, to which environment).
- **NFR-06: Rollback Capability** — BFF API deployment MUST support rollback to previous version via slot swap. Dataverse solution import MUST support version rollback.

---

## 6. Technical Approach

### 6.1 Bicep Template Restructuring

**Current state:**
```
infrastructure/bicep/
├── model1-shared.bicep          # Everything shared (keep for reference)
├── model2-full.bicep            # Everything dedicated (keep for reference)
├── modules/                     # Reusable modules (keep, these are good)
│   ├── app-service.bicep
│   ├── redis.bicep
│   ├── service-bus.bicep
│   ├── storage-account.bicep
│   ├── openai.bicep
│   ├── ai-search.bicep
│   ├── monitoring.bicep
│   ├── key-vault.bicep
│   └── ai-foundry-hub.bicep
└── parameters/
    ├── dev.bicepparam
    ├── prod.bicepparam
    └── customer-template.bicepparam
```

**Proposed additions:**
```
infrastructure/bicep/
├── platform.bicep               # NEW: Shared platform resources
├── customer.bicep               # NEW: Per-customer resources
├── parameters/
│   ├── platform-dev.bicepparam  # NEW: Platform dev params
│   ├── platform-prod.bicepparam # NEW: Platform prod params
│   ├── demo-customer.bicepparam # NEW: Demo customer params
│   └── customer-template.bicepparam  # UPDATE: Template for new customers
```

**`platform.bicep` provisions:**
- Resource Group: `rg-spaarke-platform-{env}`
- App Service Plan + BFF API Web App
- Azure OpenAI (with 3 model deployments)
- AI Search (Standard2 for prod)
- Document Intelligence
- App Insights + Log Analytics
- Platform Key Vault

**`customer.bicep` provisions:**
- Resource Group: `rg-spaarke-{customerId}-{env}`
- Storage Account
- Key Vault (customer secrets)
- Service Bus Namespace (with standard queues)
- Redis Cache

**Parameters for `customer.bicep`:**
```bicep
param customerId string         // e.g., 'demo', 'contoso'
param environment string        // e.g., 'prod'
param location string           // e.g., 'westus2'
param platformResourceGroup string  // e.g., 'rg-spaarke-platform-prod'
param redisSku string           // 'Basic' | 'Standard' | 'Premium'
param serviceBusSku string      // 'Basic' | 'Standard'
param storageSku string         // 'Standard_LRS' | 'Standard_GRS'
```

### 6.2 Deployment Script Architecture

```
scripts/
├── Deploy-Platform.ps1           # Deploy shared platform infrastructure
├── Deploy-BffApi.ps1             # Deploy BFF API (parameterized, exists but needs update)
├── Provision-Customer.ps1        # Full customer provisioning pipeline
├── Decommission-Customer.ps1     # Customer teardown
├── Deploy-DataverseSolutions.ps1 # Import managed solutions to Dataverse env
├── Test-Deployment.ps1           # Smoke tests for any deployed environment
└── Rotate-Secrets.ps1            # Secret rotation for Key Vault
```

**`Provision-Customer.ps1` Flow:**

```
Parameters: -CustomerId, -DisplayName, -Environment, -DataverseUrl (optional)

Step 1: Validate parameters
  → Check customer doesn't already exist
  → Validate naming convention compliance

Step 2: Deploy Azure resources
  → az deployment group create --template-file customer.bicep --parameters ...
  → Capture outputs (connection strings, endpoints, resource IDs)

Step 3: Populate Key Vault
  → Store: Redis connection string, Service Bus connection string,
           Storage connection string, Dataverse credentials
  → Cross-reference: Platform Key Vault secrets (OpenAI, AI Search, Doc Intel)

Step 4: Register customer in BFF API
  → Update tenant registry (Dataverse table or configuration)
  → Map customer → resource endpoints

Step 5: Provision Dataverse (if -DataverseUrl not provided)
  → Create new Dataverse environment via Power Platform Admin API
  → Or: configure existing environment URL

Step 6: Import Dataverse solutions
  → Call Deploy-DataverseSolutions.ps1 -DataverseUrl $url
  → Import in order: SpaarkeCore → feature solutions → web resources

Step 7: Configure Dataverse
  → Set environment variables (BFF API URL, tenant config)
  → Create default security roles (if not in solution)
  → Create default records (configuration data)

Step 8: Provision SPE containers
  → Call BFF API provisioning endpoint (or direct Graph API)
  → Create default container for customer
  → Assign initial permissions

Step 9: Verify deployment
  → Call Test-Deployment.ps1 -Environment $env -CustomerId $id
  → Health check BFF API
  → Verify Dataverse connectivity
  → Verify SPE container access
  → Verify AI services connectivity

Step 10: Output summary
  → Customer URL, admin credentials location, next steps
```

### 6.3 BFF API Production Configuration

**`appsettings.Production.json`** (Key Vault references):
```json
{
  "TENANT_ID": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=tenant-id)",
  "API_APP_ID": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=api-app-id)",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=api-client-secret)",
  "DEFAULT_CT_ID": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=default-container-type-id)",
  "DATAVERSE_URL": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=dataverse-url)",
  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=servicebus-connection)",
    "Redis": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=redis-connection)",
    "Storage": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=storage-connection)"
  }
}
```

**Note**: For multi-customer scenario, per-customer connection strings come from the tenant registry (Dataverse table), not from static config. The static config above is the **platform default** (demo customer). The BFF API tenant routing service resolves per-customer endpoints at runtime — this is delivered by the SPE multi-tenant architecture project.

### 6.4 GitHub Actions Workflows

**`deploy-platform.yml`:**
```
Trigger: Manual dispatch (workflow_dispatch)
Inputs: environment (dev/prod), region
Steps:
  1. az login (OIDC federated credential)
  2. az deployment group what-if (preview changes)
  3. Wait for approval (environment protection rule)
  4. az deployment group create (platform.bicep)
  5. Store outputs as artifacts
```

**`deploy-bff-api.yml`:**
```
Trigger: push to master (paths: src/server/api/**)
Jobs:
  build-test:
    - dotnet build, dotnet test, dotnet format check
  deploy-staging:
    - dotnet publish → zip → az webapp deploy --slot staging
    - Health check staging slot
  swap-to-production:
    - Environment: production (requires approval)
    - az webapp deployment slot swap
    - Health check production
  rollback (on failure):
    - az webapp deployment slot swap (reverse)
```

**`provision-customer.yml`:**
```
Trigger: Manual dispatch (workflow_dispatch)
Inputs: customerId, displayName, environment, dataverseUrl (optional)
Steps:
  1. Validate inputs
  2. Run Provision-Customer.ps1
  3. Run Test-Deployment.ps1
  4. Report results
```

### 6.5 Demo Environment Specification

The demo environment is the first customer deployed using the production process:

| Setting | Value |
|---------|-------|
| Customer ID | `demo` |
| Display Name | `Spaarke Demo` |
| Resource Group | `rg-spaarke-demo-prod` |
| Dataverse | Dedicated demo environment |
| SPE | Spaarke-hosted containers with sample documents |
| Users | B2B guest access (no BU segregation) |
| Data | Non-confidential test data |
| Expiration | N/A (permanent demo environment) |
| Purpose | Validate deployment process + demo access for prospects |

**Demo-specific configuration (`demo-customer.bicepparam`):**
```bicep
param customerId = 'demo'
param environment = 'prod'
param location = 'westus2'
param platformResourceGroup = 'rg-spaarke-platform-prod'
param redisSku = 'Basic'           // Minimal for demo
param serviceBusSku = 'Standard'
param storageSku = 'Standard_LRS'  // No geo-redundancy needed for demo
```

### 6.6 Resource & Component Naming Standard

**This is a first-class deliverable of this project.** The current dev environment uses a messy mix of `spe-*`, `sdap-*`, and `spaarke-*` prefixes that is difficult to manage and understand. Production MUST establish a clean, consistent, descriptive naming structure from day one.

#### Naming Principles

1. **Two standard prefixes only**: `sprk_` (Dataverse/code — underscore required by platform) and `spaarke` (Azure resources — descriptive, recognizable)
2. **Descriptive names** — Every resource name should make its purpose obvious at a glance. No cryptic abbreviations, random suffixes, or legacy holdovers
3. **Consistent structure** — Same pattern for every resource of the same type
4. **Environment-aware** — Environment suffix (`-dev`, `-prod`) for isolation
5. **Customer-scoped** — Customer identifier included in per-customer resources

#### Naming Patterns

**Azure Resources (use `spaarke-` or `sprk-` depending on length limits):**

```
Long names (>24 chars allowed):   spaarke-{purpose}-{env}
Short names (≤24 char limit):     sprk-{purpose}-{env}
Per-customer:                     sprk-{customer}-{purpose}-{env}
Storage accounts (no hyphens):    sprk{customer}{env}sa
```

**Dataverse Components (use `sprk_` prefix):**

```
Tables:            sprk_{entityname}           e.g., sprk_documentprofile
Columns:           sprk_{columnname}           e.g., sprk_containerid
Security Roles:    Spaarke - {Role Name}       e.g., Spaarke - Standard User
Solutions:         Spaarke{Feature}             e.g., SpaarkeCore, SpaarkeAnalysis
Environment Vars:  sprk_{variablename}         e.g., sprk_bffapiurl
Web Resources:     sprk_{resourcename}         e.g., sprk_documentviewer.html
PCF Controls:      sprk_{ControlName}          e.g., sprk_AiToolAgent
```

**Code Components:**

```
.NET Namespaces:   Sprk.{Area}.{Component}     e.g., Sprk.Bff.Api
npm Packages:      @spaarke/{package}           e.g., @spaarke/ui-components
PCF Projects:      Sprk{ControlName}            e.g., SprkDocumentProfile
Solution Projects: Sprk.{Purpose}               e.g., Sprk.Plugins.Validation
```

**Service Bus Queues (descriptive, no prefix needed — scoped by namespace):**

```
document-processing          (was: sdap-jobs)
document-indexing
ai-indexing
customer-onboarding
communication-inbound
```

**Redis Key Prefixes:**

```
sprk-{env}:{area}:{key}     e.g., sprk-prod:graph:token:{hash}
```

#### Complete Production Naming Matrix

**Shared Platform Resources** (`rg-spaarke-platform-prod`):

| Resource Type | Production Name | Purpose |
|--------------|----------------|---------|
| Resource Group | `rg-spaarke-platform-prod` | All shared platform resources |
| App Service Plan | `spaarke-platform-prod-plan` | Shared compute plan |
| App Service (BFF API) | `spaarke-bff-prod` | BFF API production instance |
| App Service Slot | `spaarke-bff-prod/staging` | Staging slot for zero-downtime deploy |
| Azure OpenAI | `spaarke-openai-prod` | Shared AI models |
| AI Search | `spaarke-search-prod` | Shared search indexes |
| Document Intelligence | `spaarke-docintel-prod` | Shared document processing |
| App Insights | `spaarke-platform-prod-insights` | Centralized monitoring |
| Log Analytics | `spaarke-platform-prod-logs` | Centralized log aggregation |
| Platform Key Vault | `sprk-platform-prod-kv` | Shared secrets (AI keys, platform creds) |

**Per-Customer Resources** (`rg-spaarke-{customer}-prod`):

| Resource Type | Production Name Pattern | Demo Example |
|--------------|------------------------|--------------|
| Resource Group | `rg-spaarke-{customer}-prod` | `rg-spaarke-demo-prod` |
| Storage Account | `sprk{customer}prodsa` | `sprkdemoprodsa` |
| Key Vault | `sprk-{customer}-prod-kv` | `sprk-demo-prod-kv` |
| Service Bus | `spaarke-{customer}-prod-sb` | `spaarke-demo-prod-sb` |
| Redis Cache | `spaarke-{customer}-prod-cache` | `spaarke-demo-prod-cache` |

**Entra ID App Registrations:**

| Registration | Name | Purpose |
|-------------|------|---------|
| BFF API (single-tenant) | `Spaarke BFF API - Production` | BFF API auth + Graph access |
| BFF API (multi-tenant) | `Spaarke Platform - Multi-Tenant` | Cross-tenant Graph access (future) |
| Dataverse S2S | `Spaarke Dataverse S2S - Production` | Server-to-server Dataverse access |
| Website (public) | `Spaarke Website - Production` | Self-service registration app auth |

**Dataverse Environments:**

| Environment | URL Pattern | Purpose |
|-------------|-------------|---------|
| Demo | `spaarke-demo.crm.dynamics.com` | Demo/trial environment |
| Customer | `spaarke-{customer}.crm.dynamics.com` | Per-customer environment |

#### What Changes from Dev

| Component | Dev (Legacy — DO NOT COPY) | Production (Standard) |
|-----------|---------------------------|----------------------|
| Resource Group | `spe-infrastructure-westus2` | `rg-spaarke-platform-prod` |
| App Service | `spe-api-dev-67e2xz` | `spaarke-bff-prod` |
| Key Vault | `spaarke-spekvcert` | `sprk-platform-prod-kv` |
| Service Bus | `spaarke-servicebus-dev` | `spaarke-demo-prod-sb` |
| SB Queue | `sdap-jobs` | `document-processing` |
| Redis prefix | `sdap-dev:` | `sprk-prod:` |
| App Registration | `spe-bff-api` | `Spaarke BFF API - Production` |
| API Scope | `api://spe-bff-api/...` | `api://{app-id-guid}/...` |

**Important**: The dev environment is NOT renamed. Legacy names stay in dev. Production starts clean with the standard naming from day one. This avoids breaking existing dev workflows while ensuring production is clean and manageable.

### 6.7 Dataverse Solution Deployment Order

Solutions have implicit dependencies. Import order:

```
1. SpaarkeCore              ← Core entities, security roles, base components
2. webresources             ← Shared web resources (CSS, scripts, images)
3. AnalysisBuilder          ← Analysis PCF control (depends on core entities)
4. DocumentUploadWizard     ← Upload workflow (depends on core entities)
5. EventCommands            ← Event ribbon commands (depends on core entities)
6. EventDetailSidePane      ← Event details panel (depends on EventCommands)
7. EventsPage               ← Events code page (depends on EventCommands)
8. LegalWorkspace           ← Workspace code page (depends on core entities)
9. TodoDetailSidePane       ← Todo panel (depends on core entities)
10. CalendarSidePane        ← Calendar panel (depends on core entities)
```

All solutions deploy as **managed** (not unmanaged) to production environments. This enables:
- Clean upgrade path (managed solution updates)
- Component protection (users can't modify managed components)
- Clean uninstall (remove solution removes all components)

---

## 7. Phased Implementation

### Phase 1: Bicep Restructuring & Scripts (Foundation)
- Create `platform.bicep` from existing modules
- Create `customer.bicep` from existing modules
- Create parameter files (platform-prod, demo-customer, customer-template)
- Validate with `az deployment group what-if`
- Create `Deploy-Platform.ps1`
- Parameterize existing `Deploy-BffApi.ps1`
- Create `Deploy-DataverseSolutions.ps1`
- Create `Test-Deployment.ps1` (smoke tests)

### Phase 2: Platform Deployment
- Deploy shared platform to `rg-spaarke-platform-prod`
- Configure App Service (custom domain, SSL, deployment slots)
- Configure App Registrations (production Entra ID)
- Populate Platform Key Vault
- Deploy BFF API to production
- Verify health checks and connectivity

### Phase 3: Demo Customer Deployment (Validation)
- Run `Provision-Customer.ps1 -CustomerId demo -DisplayName "Spaarke Demo"`
- Deploy demo Dataverse environment + solutions
- Create demo SPE containers + sample data
- Configure demo user access (B2B guests)
- Run full smoke test suite
- Document issues discovered, fix scripts

### Phase 4: Customer Provisioning Script
- Create `Provision-Customer.ps1` (full pipeline)
- Create `Decommission-Customer.ps1`
- Test with a second "customer" to validate repeatability
- Create customer onboarding runbook

### Phase 5: CI/CD & Operations
- Create `deploy-platform.yml` GitHub Actions workflow
- Create `deploy-bff-api.yml` with staging/production slots
- Create `provision-customer.yml` manual dispatch workflow
- Create `Rotate-Secrets.ps1` for credential lifecycle
- Write operational documentation (deployment guide, incident response)
- Finalize naming convention status from "Proposed" to "Adopted"

---

## 8. Dependencies

### Prerequisites
- Azure subscription with sufficient quota (OpenAI, AI Search, App Service)
- Entra ID admin access for app registration creation
- Power Platform admin access for Dataverse environment provisioning
- GitHub repository admin for Actions secrets and environment protection rules
- DNS access for custom domain configuration

### Dependent Projects
- **production-performance-improvement-r1** — Infrastructure hardening (VNet, autoscaling, Redis hardening) layers on top of this project's deployment. Tasks 040-046 should run after this project completes Phase 2.
- **spe-multi-tenant-architecture-r1** — Multi-tenant Graph auth enables per-customer SPE. This project uses Spaarke-hosted SPE (single-tenant) for demo; multi-tenant comes from SPE project.
- **spaarke-self-service-registration-app** — Consumes the provisioning APIs and scripts built here. Runs after this project delivers the demo environment.

### Execution Order

```
1. production-environment-setup-r1 (THIS PROJECT)
   ├── Phase 1-3: Platform + demo environment
   │
   ├── 2. production-performance-improvement-r1 (parallel, infrastructure tasks)
   │      └── Tasks 040-046: VNet, autoscaling, security hardening
   │
   ├── 3. spe-multi-tenant-architecture-r1 (after Phase 2)
   │      └── Graph auth framework for per-customer SPE
   │
   └── Phase 4-5: Customer provisioning + CI/CD
       │
       └── 4. spaarke-self-service-registration-app (after Phase 4)
              └── Self-service onboarding consuming provisioning scripts
```

---

## 9. Success Criteria

1. [ ] Platform Bicep deploys all shared resources to production without errors
2. [ ] Customer Bicep deploys per-customer resources without errors
3. [ ] BFF API runs on production App Service, responds to health checks
4. [ ] Demo Dataverse environment has all solutions imported and configured
5. [ ] Demo SPE containers are accessible and functional (upload/download works)
6. [ ] Demo AI services work (RAG query returns results, document analysis succeeds)
7. [ ] `Provision-Customer.ps1` successfully onboards a second test customer
8. [ ] `Decommission-Customer.ps1` cleanly removes a test customer
9. [ ] GitHub Actions deploy BFF API via staging slot with zero downtime
10. [ ] All secrets are in Key Vault, none in app settings or source code
11. [ ] All new resources follow adopted naming convention
12. [ ] Deployment guide enables a new team member to deploy independently
13. [ ] Smoke test suite validates deployment end-to-end in under 5 minutes

---

## 10. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Dataverse environment provisioning requires manual admin steps | High | Medium | Document manual steps clearly, automate what Power Platform Admin API supports |
| Azure quota limits block production deployment (especially OpenAI) | Medium | High | Request quota increases before deployment. Have fallback SKUs documented |
| SPE beta API changes during project | Low | High | Pin Graph SDK version, monitor changelog, abstract API calls |
| Managed solution import fails due to dependency issues | Medium | Medium | Test import order in dev first, maintain dependency documentation |
| Custom domain DNS propagation delays | Low | Low | Start DNS changes early, use temporary Azure-assigned URL initially |
| Entra ID app registration permissions differ between dev and prod tenants | Medium | Medium | Document exact permissions needed, test in isolated tenant first |

---

## 11. Open Questions

1. **Production Entra ID tenant**: Same tenant as dev (`a221a95e-...`) or separate production tenant? Affects all app registrations and B2B configuration.

2. **Dataverse environment creation**: Automated via Power Platform Admin API, or manual via admin center? The API has limitations around license assignment.

3. **Custom domain for BFF API**: What domain? `api.spaarke.com`? `bff.spaarke.com`? Or subdomain per customer (`contoso.api.spaarke.com`)?

4. **Production Dataverse URL**: Provision new or use existing? If new, what name? (`spaarke-demo.crm.dynamics.com`?)

5. **GitHub Actions runners**: GitHub-hosted (simpler) or self-hosted (faster, can access VNet)? Affects deployment speed and network access.

6. **Azure region**: Stay in `westus2` (matches dev) or deploy to a different region? Multi-region considerations?

7. **SSL certificate**: Azure-managed (free, auto-renewal) or bring-your-own (more control)?

8. **Cost budget**: Is there a monthly spend target for the production platform + demo customer?

---

## 12. Cost Estimate

### Shared Platform (Monthly)

| Resource | SKU | Estimated Cost |
|----------|-----|---------------|
| App Service Plan (P1v3) | 4 vCPU, 16GB RAM | ~$365 |
| Azure OpenAI | S0 + usage | ~$50-200 (varies) |
| AI Search Standard2 | 2 replicas | ~$500 |
| Document Intelligence S0 | Per-page pricing | ~$20-50 |
| App Insights + Log Analytics | Per-GB ingestion | ~$50-100 |
| Platform Key Vault | Standard | ~$5 |
| **Platform Total** | | **~$990-1,220/month** |

### Per-Customer (Monthly, Demo-Tier)

| Resource | SKU | Estimated Cost |
|----------|-----|---------------|
| Redis (Basic C0) | 250MB | ~$16 |
| Service Bus (Standard) | 1M operations/month | ~$10 |
| Storage (Standard_LRS) | 10GB | ~$2 |
| Customer Key Vault | Standard | ~$5 |
| Dataverse Environment | Included with license | ~$0 (capacity-based) |
| **Customer Total** | | **~$33/month** |

### Total for Platform + Demo

**~$1,025-1,255/month** for the shared platform + demo customer.

Each additional customer adds ~$33-150/month depending on SKU selections (Basic vs Standard Redis, LRS vs GRS storage).

---

*This design document covers the full scope of production environment setup. Transform to spec.md via `/design-to-spec` then initialize via `/project-pipeline`.*
