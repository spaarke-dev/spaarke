# Azure Resource Naming Convention

> **Version**: 2.0
> **Date**: March 13, 2026
> **Status**: Adopted

## Overview

This document establishes the **authoritative naming convention** for all Azure resources, Dataverse components, code namespaces, Service Bus queues, Redis keys, and Entra ID registrations in the Spaarke deployment package. It supersedes the previous v1.1 "Proposed" version and is now the binding standard for all production resource naming decisions.

All new production resources **MUST** follow these conventions. The existing dev environment retains its legacy names; production starts clean.

---

## Naming Principles

1. **Two standard prefixes only**: `sprk_` (Dataverse/code — underscore required by platform) and `spaarke` (Azure resources — descriptive, recognizable)
2. **Descriptive names** — Every resource name should make its purpose obvious at a glance. No cryptic abbreviations, random suffixes, or legacy holdovers
3. **Consistent structure** — Same pattern for every resource of the same type
4. **Environment-aware** — Environment suffix (`-dev`, `-prod`) for isolation
5. **Customer-scoped** — Customer identifier included in per-customer resources
6. **No legacy prefixes** — `spe-*` and `sdap-*` prefixes are **prohibited** for new resources

---

## Naming Patterns

### Azure Resources

Use `spaarke-` for resources with generous name length limits, or `sprk-` when constrained to 24 characters or fewer.

```
Long names (>24 chars allowed):   spaarke-{purpose}-{env}
Short names (<=24 char limit):    sprk-{purpose}-{env}
Per-customer (long):              spaarke-{customer}-{purpose}-{env}
Per-customer (short):             sprk-{customer}-{purpose}-{env}
Storage accounts (no hyphens):    sprk{customer}{env}sa
```

### Dataverse Components

All Dataverse components use the `sprk_` publisher prefix (underscore required by platform).

```
Tables:            sprk_{entityname}           e.g., sprk_documentprofile
Columns:           sprk_{columnname}           e.g., sprk_containerid
Security Roles:    Spaarke - {Role Name}       e.g., Spaarke - Standard User
Solutions:         Spaarke{Feature}             e.g., SpaarkeCore, SpaarkeAnalysis
Environment Vars:  sprk_{variablename}         e.g., sprk_bffapiurl
Web Resources:     sprk_{resourcename}         e.g., sprk_documentviewer.html
PCF Controls:      sprk_{ControlName}          e.g., sprk_AiToolAgent
```

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
customer-onboarding
communication-inbound
```

### Redis Key Prefixes

```
sprk-{env}:{area}:{key}     e.g., sprk-prod:graph:token:{hash}
```

---

## Complete Resource Naming Matrix

### Azure Resource Types

| Resource Type | Max Length | Convention | Example Dev | Example Prod |
|--------------|------------|------------|-------------|--------------|
| Resource Group | 90 | `rg-spaarke-{purpose}-{env}` | `rg-spaarke-shared-dev` | `rg-spaarke-platform-prod` |
| Key Vault | 24 | `sprk-{purpose}-{env}-kv` | `sprk-shared-dev-kv` | `sprk-platform-prod-kv` |
| App Service | 60 | `spaarke-{app}-{env}` | `spaarke-bff-dev` | `spaarke-bff-prod` |
| App Service Plan | 40 | `spaarke-{tier}-{env}-plan` | `spaarke-shared-dev-plan` | `spaarke-platform-prod-plan` |
| App Service Slot | — | `{app-service}/staging` | `spaarke-bff-dev/staging` | `spaarke-bff-prod/staging` |
| App Registration | 120 | `Spaarke {Purpose} - {Environment}` | `Spaarke BFF API - Dev` | `Spaarke BFF API - Production` |
| API Scope URI | N/A | `api://{appId}/user_impersonation` | Use Application ID GUID | Use Application ID GUID |
| Service Bus Namespace | 50 | `spaarke-{customer}-{env}-sb` | `spaarke-servicebus-dev` | `spaarke-demo-prod-sb` |
| Service Bus Queue | 260 | `{purpose}` (descriptive) | `document-processing` | `document-processing` |
| Redis Cache | 63 | `spaarke-{customer}-{env}-cache` | `spaarke-cache-dev` | `spaarke-demo-prod-cache` |
| Redis Key Prefix | N/A | `sprk-{env}:` | `sprk-dev:` | `sprk-prod:` |
| Storage Account | 24 | `sprk{customer}{env}sa` | `sprkshareddevsa` | `sprkdemoprodsa` |
| Application Insights | 255 | `spaarke-{purpose}-{env}-insights` | `spaarke-bff-dev-insights` | `spaarke-platform-prod-insights` |
| Log Analytics | 63 | `spaarke-{purpose}-{env}-logs` | `spaarke-shared-dev-logs` | `spaarke-platform-prod-logs` |
| Azure OpenAI | 64 | `spaarke-openai-{env}` | `spaarke-openai-dev` | `spaarke-openai-prod` |
| AI Search | 60 | `spaarke-search-{env}` | `spaarke-search-dev` | `spaarke-search-prod` |
| Document Intelligence | 64 | `spaarke-docintel-{env}` | `spaarke-docintel-dev` | `spaarke-docintel-prod` |
| AI Foundry Hub | 63 | `sprk{customer}{env}-aif-hub` | `sprkspaarkedev-aif-hub` | `sprkspaarke-aif-hub` |
| AI Foundry Project | 63 | `sprk{customer}{env}-aif-proj` | `sprkspaarkedev-aif-proj` | `sprkspaarke-aif-proj` |
| AI Foundry Storage | 24 | `sprk{customer}{env}aifsa` | `sprkspaarkedevaifsa` | `sprkspaarkeaifsa` |
| AI Foundry Key Vault | 24 | `sprk{customer}{env}-aif-kv` | `sprkspaarkedev-aif-kv` | `sprkspaarke-aif-kv` |

---

### Shared Platform Resources (`rg-spaarke-platform-prod`)

These resources are deployed once and shared across all customers.

| Resource Type | Production Name | Purpose |
|--------------|----------------|---------|
| Resource Group | `rg-spaarke-platform-prod` | All shared platform resources |
| App Service Plan | `spaarke-platform-prod-plan` | Shared compute plan (P1v3) |
| App Service (BFF API) | `spaarke-bff-prod` | BFF API production instance |
| App Service Slot | `spaarke-bff-prod/staging` | Staging slot for zero-downtime deploy |
| Azure OpenAI | `spaarke-openai-prod` | Shared AI models (GPT-4o, GPT-4o-mini, embeddings) |
| AI Search | `spaarke-search-prod` | Shared search indexes (Standard2, 2 replicas) |
| Document Intelligence | `spaarke-docintel-prod` | Shared document processing (S0) |
| App Insights | `spaarke-platform-prod-insights` | Centralized monitoring |
| Log Analytics | `spaarke-platform-prod-logs` | Centralized log aggregation (180-day retention) |
| Platform Key Vault | `sprk-platform-prod-kv` | Shared secrets (AI keys, platform creds) |

### Per-Customer Resources (`rg-spaarke-{customer}-prod`)

These resources are provisioned for each customer onboarding.

| Resource Type | Production Name Pattern | Demo Example |
|--------------|------------------------|--------------|
| Resource Group | `rg-spaarke-{customer}-prod` | `rg-spaarke-demo-prod` |
| Storage Account | `sprk{customer}prodsa` | `sprkdemoprodsa` |
| Key Vault | `sprk-{customer}-prod-kv` | `sprk-demo-prod-kv` |
| Service Bus Namespace | `spaarke-{customer}-prod-sb` | `spaarke-demo-prod-sb` |
| Redis Cache | `spaarke-{customer}-prod-cache` | `spaarke-demo-prod-cache` |

---

### Entra ID App Registrations

| Registration | Name | Purpose |
|-------------|------|---------|
| BFF API (single-tenant) | `Spaarke BFF API - Production` | BFF API auth + Graph access |
| BFF API (multi-tenant) | `Spaarke Platform - Multi-Tenant` | Cross-tenant Graph access (future) |
| Dataverse S2S | `Spaarke Dataverse S2S - Production` | Server-to-server Dataverse access |
| Website (public) | `Spaarke Website - Production` | Self-service registration app auth |

**API Scope URI**: Use the Application ID GUID, not a friendly name.

```
api://{application-id-guid}/user_impersonation
```

This avoids coupling scope URIs to human-readable names that may change.

---

### Dataverse Environments

| Environment | URL Pattern | Purpose |
|-------------|-------------|---------|
| Dev | `spaarkedev1.crm.dynamics.com` | Development/testing (existing) |
| Demo | `spaarke-demo.crm.dynamics.com` | Demo/trial environment |
| Customer | `spaarke-{customer}.crm.dynamics.com` | Per-customer environment |

---

### Service Bus Queue Names

Queues are descriptive and scoped by the per-customer Service Bus namespace.

| Queue Name | Purpose | Legacy Name (Dev) |
|------------|---------|-------------------|
| `document-processing` | Document upload and processing jobs | `sdap-jobs` |
| `document-indexing` | Document indexing for search | _(new)_ |
| `ai-indexing` | AI semantic indexing jobs | _(new)_ |
| `customer-onboarding` | Customer provisioning workflow | _(new)_ |
| `communication-inbound` | Inbound communication processing | _(new)_ |

---

### Redis Key Prefix Convention

```
sprk-{env}:{area}:{key}
```

| Area | Key Pattern | Example |
|------|------------|---------|
| `graph` | `graph:token:{hash}` | `sprk-prod:graph:token:abc123` |
| `cache` | `cache:{entity}:{id}` | `sprk-prod:cache:document:42` |
| `session` | `session:{userId}` | `sprk-prod:session:user-guid` |
| `job` | `job:status:{jobId}` | `sprk-prod:job:status:job-guid` |

---

## Current State: Legacy Names Audit

### Dev Environment (DO NOT RENAME)

The dev environment retains its legacy names. These are documented for reference only.

| Resource Type | Current Dev Name | Issue |
|--------------|-----------------|-------|
| Resource Group | `spe-infrastructure-westus2` | Uses legacy `spe` prefix |
| App Service | `spe-api-dev-67e2xz` | Uses legacy `spe` prefix + random suffix |
| Key Vault | `spaarke-spekvcert` | Mixed `spaarke` + `spe` |
| App Registration | `spe-bff-api` | Uses legacy `spe` prefix |
| API Scope | `api://spe-bff-api/user_impersonation` | Uses legacy `spe` prefix |
| Service Bus Namespace | `spaarke-servicebus-dev` | Uses `spaarke` prefix (acceptable) |
| Service Bus Queue | `sdap-jobs` | Uses legacy `sdap` prefix |
| Redis Instance Prefix | `sdap-dev:` | Uses legacy `sdap` prefix |

### Code References (Dev)

| Location | Current Reference | Impact |
|----------|-------------------|--------|
| `appsettings.json` | `spaarke-spekvcert.vault.azure.net` | Key Vault URL |
| `appsettings.json` | `sdap-dev:` Redis instance | Redis key prefix |
| PCF Controls | `api://spe-bff-api/user_impersonation` | OAuth scope |
| Service Bus config | `sdap-jobs` queue | Queue name |
| Bicep modules | `sdap-jobs` | Infrastructure as Code |

---

## Migration Notes: Dev vs Production

### Strategy

**Dev environment stays as-is. Production starts clean.**

This approach was chosen because:
1. **No risk to active development** — Renaming dev resources would break existing workflows, configurations, and developer setups
2. **Clean production baseline** — Production resources use the adopted naming standard from day one, with no legacy artifacts
3. **No data migration needed** — There is no production data to migrate; production is a greenfield deployment
4. **Gradual dev cleanup (optional)** — Dev resources can be renamed in a future project when there is a natural opportunity (e.g., environment rebuild)

### What Changes from Dev to Production

| Component | Dev (Legacy -- DO NOT COPY) | Production (Standard) |
|-----------|---------------------------|----------------------|
| Resource Group | `spe-infrastructure-westus2` | `rg-spaarke-platform-prod` |
| App Service | `spe-api-dev-67e2xz` | `spaarke-bff-prod` |
| Key Vault | `spaarke-spekvcert` | `sprk-platform-prod-kv` |
| Service Bus Namespace | `spaarke-servicebus-dev` | `spaarke-demo-prod-sb` |
| Service Bus Queue | `sdap-jobs` | `document-processing` |
| Redis Key Prefix | `sdap-dev:` | `sprk-prod:` |
| App Registration | `spe-bff-api` | `Spaarke BFF API - Production` |
| API Scope | `api://spe-bff-api/...` | `api://{app-id-guid}/...` |

### Configuration Strategy

Production uses environment variables and Key Vault references to decouple resource names from application code:

```json
// appsettings.Production.json — Key Vault references
{
  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=servicebus-connection)",
    "Redis": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=redis-connection)",
    "Storage": "@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=storage-connection)"
  }
}
```

```bash
# App Service Configuration (environment variables)
KEY_VAULT_NAME=sprk-platform-prod-kv
REDIS_INSTANCE_NAME=sprk-prod:
JOB_QUEUE_NAME=document-processing
BFF_API_CLIENT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

---

## Customer Deployment Naming

For customer-hosted deployments, include the customer identifier:

```
Per-customer:  sprk-{customer}-{purpose}-{env}
```

**Example (Contoso):**

| Resource | Name |
|----------|------|
| Resource Group | `rg-spaarke-contoso-prod` |
| Key Vault | `sprk-contoso-prod-kv` |
| Storage Account | `sprkcontosoprodsa` |
| Service Bus | `spaarke-contoso-prod-sb` |
| Redis | `spaarke-contoso-prod-cache` |

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

*Adopted: March 13, 2026. This is the authoritative naming reference for all Spaarke production resources.*
