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
│   ├── customer.bicep     # The customer stamp — Model 1 AND Model 2 (D-12); deployed by L2 H2a
│   ├── stacks/            # Composed deployments
│   │   └── model2-full.bicep      # Model 2: Complete deployment
│   │                              # (model1-shared / model1-customer retired by task 225a, 2026-10-01)
│   │
│   └── parameters/        # Environment-specific parameters
│       └── customer-template.bicepparam
│
├── scripts/               # Deployment automation (TODO)
│   └── Deploy-Model2-Full.ps1
│
└── docs/                  # Deployment documentation (TODO)
    ├── MODEL1-DEPLOYMENT-GUIDE.md
    ├── MODEL2-DEPLOYMENT-GUIDE.md
    └── PREREQUISITES.md
```

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
`/provision-environment`, see `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`. (Until tasks 225b + 228 land,
H2a refuses Model 1 runs — fails closed — rather than deploy into a non-dedicated subscription.) The former
`stacks/model1-shared.bicep` / `model1-customer.bicep` and their `parameters/{dev,staging,prod}.bicepparam` were
retired by `customer-provisioning-orchestration-r1` task 225a (2026-10-01).

### Deploy Model 2 (Customer Deployment)

```powershell
# 1. Copy and customize the parameter file
cp bicep/parameters/customer-template.bicepparam bicep/parameters/contoso.bicepparam
# Edit contoso.bicepparam with customer-specific values

# 2. Deploy (customer-template.bicepparam is `using '../customer.bicep'`, so the template must match).
#    Normal path: the L2 control plane deploys this via /provision-environment (handler H2a).
az deployment sub create \
  --location eastus \
  --template-file bicep/customer.bicep \
  --parameters bicep/parameters/contoso.bicepparam

# 3. Store secrets in Key Vault (from deployment outputs)
$outputs = az deployment sub show --name <deployment-name> --query properties.outputs -o json | ConvertFrom-Json
az keyvault secret set --vault-name <kv-name> --name redis-connection-string --value $outputs.redisConnectionString.value
az keyvault secret set --vault-name <kv-name> --name servicebus-connection-string --value $outputs.serviceBusConnectionString.value
# ... repeat for other secrets
```

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

| Resource Type | Pattern | Example |
|---------------|---------|---------|
| Resource Group | `rg-spaarke-{customer}-{env}` | `rg-spaarke-contoso-prod` |
| App Service | `sprk{customer}{env}-api` | `sprkcontosoprod-api` |
| Key Vault | `sprk{customer}{env}-kv` | `sprkcontosoprod-kv` |
| Redis | `sprk{customer}{env}-redis` | `sprkcontosoprod-redis` |
| Service Bus | `sprk{customer}{env}-sb` | `sprkcontosoprod-sb` |
| OpenAI | `sprk{customer}{env}-openai` | `sprkcontosoprod-openai` |
| AI Search | `sprk{customer}{env}-search` | `sprkcontosoprod-search` |

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
