# Spaarke Infrastructure Packaging Strategy

> **Version**: 1.0
> **Date**: December 4, 2025
> **Status**: Draft
> **Purpose**: Define how Azure resources and Power Platform components are packaged for both deployment models

---

## Executive Summary

Spaarke uses a **modular, repeatable infrastructure packaging** approach that supports:

1. **Model 1 (Spaarke-Hosted)**: Multi-tenant with dedicated resources per customer
2. **Model 2 (Customer-Hosted)**: Complete deployment package for customer tenants

Infrastructure-as-Code uses **Bicep** for Azure resources and **Power Platform managed solutions** for Dataverse/model-driven apps.

---

## 1. Deployment Models

### Model 1 vs Model 2 Resource Ownership

| Layer | Model 1 (Spaarke-Hosted) | Model 2 (Customer-Hosted) |
|-------|--------------------------|---------------------------|
| **Foundation** | Spaarke tenant | Customer tenant |
| **Shared Services** | Spaarke-owned | Customer-owned |
| **Platform Services** | Spaarke-owned (shared) | Customer-owned |
| **Application** | Spaarke-owned | Customer-deployed |
| **Customer Resources** | Spaarke-owned (isolated) | Customer-owned |
| **AI Services** | Spaarke-owned (shared) | Customer-owned (BYOK) |

### Infrastructure Layers

| Layer | Contents |
|-------|----------|
| **Layer 1: Foundation** | Entra ID tenant, Azure Subscription, Resource Providers |
| **Layer 2: Shared Services** | Key Vault, Log Analytics, App Insights |
| **Layer 3: Platform Services** | App Service Plan, Redis Cache, Service Bus, Storage |
| **Layer 4: Application** | App Registration, Sprk.Bff.Api App Service |
| **Layer 5: Customer Resources** | SPE Container, AI Search Index, Dataverse Environment |
| **Layer 6: AI Services** | Azure OpenAI, Azure AI Search, Document Intelligence |

---

## 2. Resource Inventory

### Entra ID Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **BFF API App Registration** | Sprk.Bff.Api authentication | Spaarke tenant | Customer tenant |
| **PCF/UI App Registration** | Client-side auth (MSAL) | Spaarke tenant | Customer tenant |
| **SPE App Registration** | SharePoint Embedded access | Spaarke tenant | Customer tenant |
| **Application Users** | Service accounts in Dataverse | Per-environment | Per-environment |

### Azure Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **App Service Plan** | Compute for BFF API | Shared (S1/P1) | Dedicated |
| **App Service** | Sprk.Bff.Api hosting | Multi-tenant slots | Single |
| **Key Vault** | Secrets, certificates | Shared + per-customer | Dedicated |
| **Redis Cache** | Token caching, sessions | Shared (Premium) | Dedicated |
| **Service Bus** | Job queue | Shared namespace | Dedicated |
| **App Insights** | Telemetry | Shared | Dedicated |

### AI Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **Azure OpenAI** | LLM inference | Shared (metered) | Customer BYOK |
| **Azure AI Search** | Vector search | Shared (multi-index) | Dedicated |
| **Document Intelligence** | OCR/extraction | Shared | Dedicated |

### Power Platform Resources

| Resource | Purpose | Packaging |
|----------|---------|-----------|
| **Dataverse Solution** | Entities, forms, views | Managed solution `.zip` |
| **PCF Controls** | React components | Part of solution |
| **Security Roles** | RBAC | Part of solution |
| **Environment Variables** | Runtime config | Part of solution |

---

## 3. Packaging Strategy

### Package Types

Infrastructure is organized into two parallel packaging tracks:

**Azure (Bicep)**:
- `infrastructure/bicep/modules/` — Reusable Bicep modules per resource type
- `infrastructure/bicep/stacks/` — Composed deployments for each scenario: `model1-shared.bicep`, `model1-customer.bicep`, `model2-full.bicep`, `ai-foundry-stack.bicep`
- `infrastructure/bicep/parameters/` — Environment-specific parameter files: `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam`, `customer-template.bicepparam`, `platform-prod.bicepparam`, `model2-customer-template.bicepparam`, `demo-customer.bicepparam`

**Power Platform**:
- `power-platform/solutions/SpaarkeCore/` — Core entities, forms, views (managed solution ZIP)
- `power-platform/solutions/SpaarkePCF/` — PCF controls (managed solution ZIP)
- `power-platform/solutions/SpaarkeAI/` — AI configuration entities (managed solution ZIP)

### Model 1: Spaarke-Hosted Deployment

**One-time setup (per environment)**: `Deploy-Model1-Shared.ps1` deploys shared infrastructure — App Service Plan, Sprk.Bff.Api, Key Vault, Redis, Service Bus, Azure OpenAI, AI Search, App Insights, and SPE ContainerType.

**Per-customer onboarding**: `Deploy-Model1-Customer.ps1` creates customer-specific AI Search index, SPE Container, Dataverse environment (if needed), and customer record in Dataverse.

### Model 2: Customer-Hosted Deployment

`Deploy-Model2-Full.ps1` executes a complete deployment to the customer tenant: creates App Registrations, deploys all Azure resources via Bicep, creates SPE ContainerType and Container, imports Power Platform solutions, creates Application User, and configures environment variables.

---

## 4. Configuration Management

### Configuration Strategy: Two Mechanisms

Spaarke uses **two distinct config mechanisms** depending on whether config is consumed by the BFF API (server-side) or client components (browser-side):

| Config Type | Storage | Varies By Env | Varies By Tenant |
|-------------|---------|---------------|------------------|
| **Client auth config** | Dataverse Environment Variables | Yes | Yes |
| **Server auth/infra** | Azure App Service + Key Vault | Yes | No (shared BFF) |
| **Feature Flags** | Azure App Service config | Yes | No |
| **AI Config** | Azure App Service config + Key Vault | Yes | No |
| **Business Rules** | Dataverse Environment Variables | No | Yes |

**Key constraint**: Client-side components (code pages, PCF controls, Office add-ins) run in the browser and cannot read Azure App Service settings. They call `resolveRuntimeConfig()` from `@spaarke/auth` at startup — this queries the 7 Dataverse Environment Variables via REST API using session cookie auth (before MSAL is initialized).

### Client-Side: Dataverse Environment Variables (7 vars)

Set once per Dataverse environment after solution import. No hardcoded values ship in the solution package. If any required variable is missing, `resolveRuntimeConfig()` throws and the page fails to load — there are no silent fallbacks.

| Variable | Purpose |
|----------|---------|
| `sprk_BffApiBaseUrl` | BFF API base URL |
| `sprk_BffApiAppId` | BFF API OAuth audience |
| `sprk_MsalClientId` | UI MSAL client ID for Entra ID sign-in |
| `sprk_TenantId` | Entra ID tenant ID |
| `sprk_AzureOpenAiEndpoint` | Azure OpenAI endpoint |
| `sprk_ShareLinkBaseUrl` | Base URL for document share links |
| `sprk_SharePointEmbeddedContainerId` | SPE Container ID |

### Server-Side: Azure App Service + Key Vault

Configures Sprk.Bff.Api. Sensitive values use Key Vault references (`@Microsoft.KeyVault(VaultName=...;SecretName=...)`). In local dev, values go in `appsettings.Development.json` or user secrets.

### Model 1: Per-Customer Configuration

For Model 1, customer-specific configuration is stored in a `sprk_CustomerConfiguration` Dataverse entity, including SPE Container ID, AI Search index name, enabled AI features, and usage tier/limits.

---

## 5. Model-Specific Deployment Details

### Model 2: Complete Deployment Package

```
model2-deployment-package/
├── README.md                    # Step-by-step deployment guide
├── PREREQUISITES.md             # Required permissions, licenses
├── Deploy-Model2-Full.ps1       # Main deployment script
├── bicep/                       # All Bicep templates
├── power-platform/              # Solution files
├── validation/                  # Post-deployment health checks
└── config/
    └── customer-template.json   # Config template to fill out
```

---

## Appendix: Related ADRs

| ADR | Description |
|-----|-------------|
| ADR-012 | Infrastructure Packaging Strategy |
| ADR-014 | Multi-Tenant Resource Isolation |

---

## References

- [Bicep Documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Power Platform ALM](https://learn.microsoft.com/en-us/power-platform/alm/)
- [SharePoint Embedded Provisioning](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/admin-exp/cta)

---

*Document Owner: Spaarke Engineering*
