# Spaarke Infrastructure

This folder contains Infrastructure-as-Code (IaC) for deploying Spaarke Azure resources.

## Overview

Spaarke supports two deployment models:

| Model | Description | Use Case |
|-------|-------------|----------|
| **Model 1** | Dedicated stamp in **Spaarke's** Azure tenant (D-12) | Customers Spaarke hosts — every resource dedicated, own subscription |
| **Model 2** | Dedicated stamp in the **customer's** Azure tenant | Enterprise customers hosting in their own tenant |

Both models deploy the same per-customer stamp (`bicep/customer.bicep`, via the L2 control plane); there is no shared
Model 1 infrastructure.

## Directory Structure

```
infrastructure/
├── bicep/
│   ├── modules/           # Reusable Bicep modules
│   │   ├── app-service.bicep
│   │   ├── app-service-plan.bicep
│   │   ├── key-vault.bicep
│   │   ├── redis.bicep
│   │   ├── service-bus.bicep
│   │   ├── storage-account.bicep
│   │   ├── openai.bicep
│   │   ├── ai-search.bicep
│   │   ├── doc-intelligence.bicep
│   │   └── monitoring.bicep
│   │
│   ├── customer.bicep     # The ONLY customer-stamp template — Model 1 AND Model 2 (D-12, D19);
│   │                      # deployed only by L2 handler H2a, into the customer's own subscription
│   ├── stacks/            # Standalone stacks (not used by customer.bicep)
│   │   └── ai-foundry-stack.bicep
│   │                      # (model1-shared / model1-customer retired by task 225a, 2026-10-01;
│   │                      #  model2-full + stacks/{dev,staging,prod}.bicepparam retired by task 249, 2026-10-02)
│   │
│   └── parameters/        # Per-customer + platform parameters
│       ├── customer-template.bicepparam   # using '../customer.bicep'
│       └── demo-customer.bicepparam       # using '../customer.bicep'
│
└── scripts/               # Misc. helpers — there is no customer deployment script; L2 H2a deploys
```

Customer deployment documentation lives in
[`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md).
CI only **validates** the Bicep (`.github/workflows/deploy-infrastructure.yml`, "Validate Bicep
Infrastructure": lint + compile, no deploy).

## Quick Start

### Prerequisites

1. **Azure CLI** installed and authenticated
2. **Bicep CLI** (included with Azure CLI 2.20+)
3. **PowerShell 7+** for deployment scripts
4. **Azure subscription** with appropriate permissions:
   - Contributor on subscription (for resource group creation)
   - User Access Administrator (for RBAC assignments)

### Model 1 (no shared infrastructure)

There is no shared Model 1 tier any more (D-12, 2026-09-28): a Model 1 customer is a **dedicated stamp** in
Spaarke's Azure tenant, built from `bicep/customer.bicep` by the L2 control plane (handler H2a) — run
`/provision-environment`, see `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`, into the customer's own subscription
(the operator creates it and grants the L2 identity Owner with `bicep/modules/controlplane-subscription-rbac.bicep` —
task 228). The former
`stacks/model1-shared.bicep` / `model1-customer.bicep` and their `parameters/{dev,staging,prod}.bicepparam` were
retired by `customer-provisioning-orchestration-r1` task 225a (2026-10-01).

### Deploy Model 2 (Customer Deployment)

Customer stamps are deployed **only** by the L2 control plane (owner decision D19, 2026-10-02): run
`/provision-environment`; handler H2a deploys `bicep/customer.bicep` into the customer's own subscription and
H4 populates the stamp's Key Vault. See `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`.
*(Retired by task 249, 2026-10-02: the hand-run `az deployment sub create --template-file bicep/customer.bicep`
+ manual `az keyvault secret set` recipe that stood here — a stamp deployed outside L2 has no run record and
no H4/H13 verification.)*

## Post-Deployment Steps

After infrastructure deployment, complete these steps:

### 1. Create App Registrations

App registrations are created via Microsoft Graph API (not Bicep). Use:
```powershell
./scripts/Register-AppRegistrations.ps1 -CustomerId "contoso"
```

### 2. Create SPE ContainerType

```powershell
./scripts/Setup-SPE-ContainerType.ps1 -CustomerId "contoso" -OwningAppId "<bff-api-app-id>"
```

### 3. Store Secrets in Key Vault

The deployment outputs connection strings. Store them in Key Vault:
```powershell
az keyvault secret set --vault-name <kv-name> --name redis-connection-string --value "<connection-string>"
az keyvault secret set --vault-name <kv-name> --name servicebus-connection-string --value "<connection-string>"
az keyvault secret set --vault-name <kv-name> --name openai-api-key --value "<api-key>"
az keyvault secret set --vault-name <kv-name> --name aisearch-admin-key --value "<admin-key>"
```

### 4. Deploy Application Code

```powershell
# Deploy Sprk.Bff.Api to App Service
az webapp deploy --resource-group <rg-name> --name <app-name> --src-path ./publish/Sprk.Bff.Api.zip
```

### 5. Import Power Platform Solutions

```powershell
pac auth create --url <dataverse-url>
pac solution import --path ./power-platform/solutions/SpaarkeCore_managed.zip
```

### 6. Create AI Search Index

The AI Search index must be created via API:
```powershell
./scripts/Create-AISearchIndex.ps1 -SearchEndpoint <endpoint> -IndexName <name>
```

## Resource Naming Convention

As composed by `bicep/customer.bicep` (authoritative table:
[`AZURE-RESOURCE-NAMING-CONVENTION.md` § Per-Customer Resources](../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)):

| Resource Type | Pattern | Example |
|---------------|---------|---------|
| Resource Group | `rg-spaarke-{customer}-{env}` | `rg-spaarke-contoso-prod` |
| Managed Identity (UAMI) | `mi-spaarke-{customer}-{env}` | `mi-spaarke-contoso-prod` |
| Key Vault | `take('sprk-{customer}-{env}-kv', 24)` | `sprk-contoso-prod-kv` |
| Storage Account | `sprk{customer}{env}sa` | `sprkcontosoprodsa` |
| Service Bus | `spaarke-{customer}-{env}-sbus` | `spaarke-contoso-prod-sbus` |
| Cosmos DB | `spaarke-{customer}-{env}-cosmos` | `spaarke-contoso-prod-cosmos` |
| App Service Plan / App Service | `sprk-{customer}-{env}-{plan\|api}` | `sprk-contoso-prod-api` |
| Redis / OpenAI / AI Search / Doc Intelligence | `sprk-{customer}-{env}-{redis\|openai\|search\|docintel}` | `sprk-contoso-prod-openai` |
| App Insights / Log Analytics | `sprk-{customer}-{env}-{insights\|logs}` | `sprk-contoso-prod-insights` |

## Environment Variables

The App Service is configured with these environment variables:

| Variable | Source | Description |
|----------|--------|-------------|
| `TENANT_ID` | Key Vault | Azure AD tenant ID |
| `API_APP_ID` | Key Vault | BFF API app registration client ID |
| `API_CLIENT_SECRET` | Key Vault | BFF API app registration secret |
| `DATAVERSE_URL` | App Settings | Dataverse environment URL |
| `SPE_CONTAINER_TYPE_ID` | App Settings | SharePoint Embedded ContainerType ID |
| `Redis__ConnectionString` | Key Vault | Redis connection string |
| `ConnectionStrings__ServiceBus` | Key Vault | Service Bus connection string |
| `OPENAI_ENDPOINT` | App Settings | Azure OpenAI endpoint |
| `OPENAI_API_KEY` | Key Vault | Azure OpenAI API key |
| `AI_SEARCH_ENDPOINT` | App Settings | Azure AI Search endpoint |
| `AI_SEARCH_API_KEY` | Key Vault | Azure AI Search admin key |

## Related Documentation

- [Infrastructure Packaging Strategy](../docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md)
- [AI Architecture](../docs/architecture/AI-ARCHITECTURE.md)
- [ADR-012: Infrastructure Packaging](../docs/reference/adr/ADR-012-infrastructure-packaging.md) (TODO)

## Troubleshooting

### Deployment Fails with "Resource Already Exists"

Use `--mode Incremental` (default) for updates, or delete existing resources first.

### Key Vault Access Denied

Ensure the deploying user has Key Vault Administrator role or equivalent access policy.

### OpenAI Model Not Available

Check model availability in your region: [Azure OpenAI Model Availability](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models#model-summary-table-and-region-availability)

### AI Search Semantic Search Error

Semantic search requires Standard tier or higher. Basic tier does not support semantic ranking.
