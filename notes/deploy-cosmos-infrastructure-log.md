# Task 120 — Deploy Cosmos DB Infrastructure

**Status**: READY FOR EXECUTION
**Date Prepared**: 2026-05-17
**Dependencies**: None (first deployment in chain)

---

## Pre-Deployment Checklist

- [ ] Azure CLI authenticated (`az login` + correct subscription selected)
- [ ] Subscription has `Microsoft.DocumentDB` resource provider registered
- [ ] Resource group `rg-spaarke-platform-dev` exists (created by platform.bicep)
- [ ] BFF API App Service managed identity principal ID is known (output from platform.bicep: `apiPrincipalId`)
- [ ] Verify Bicep template has not changed since last review: `infrastructure/bicep/modules/cosmos-db.bicep`

---

## Infrastructure Summary (from cosmos-db.bicep)

| Property | Value |
|---|---|
| Account Name | `spaarke-cosmos-dev` (pattern: `spaarke-cosmos-{env}`) |
| Capacity Mode | Serverless (pay-per-RU, no reserved throughput) |
| Database Name | `spaarke-ai` |
| Consistency | Session |
| Region | westus2 (single region, no zone redundancy) |
| TLS | 1.2+ enforced |
| Auth | RBAC-only (`disableLocalAuth: false` but app uses DefaultAzureCredential) |
| Backup | Continuous (7-day retention) |
| Public Network Access | Disabled |

### Containers

| Container | Partition Key | TTL | Analytical Store | Purpose |
|---|---|---|---|---|
| `sessions` | `/tenantId` | 90 days (7,776,000s) | No | AI conversation sessions |
| `prompts` | `/tenantId` | 90 days (7,776,000s) | No | Individual prompts/completions |
| `audit` | `/tenantId` | None (permanent) | Yes (indefinite) | Immutable AI audit trail |
| `memory` | `/tenantId` | 90 days (7,776,000s) | No | AI memory snapshots |
| `feedback` | `/tenantId` | None (permanent) | No | User feedback on AI responses |

---

## Deployment Commands

### Option A: Deploy via platform.bicep (full stack, preferred)

This deploys Cosmos DB as module 8 of the platform stack along with all other shared infrastructure.

```bash
az deployment sub create \
  --location westus2 \
  --template-file infrastructure/bicep/platform.bicep \
  --parameters environmentName=dev \
  --parameters location=westus2 \
  --name platform-deploy-$(date +%Y%m%d-%H%M%S)
```

### Option B: Deploy Cosmos module standalone (if other infra already exists)

```bash
# Get the BFF API managed identity principal ID
APP_SERVICE_PRINCIPAL_ID=$(az webapp identity show \
  --resource-group rg-spaarke-platform-dev \
  --name spaarke-bff-dev \
  --query principalId -o tsv)

# Deploy Cosmos DB module only
az deployment group create \
  --resource-group rg-spaarke-platform-dev \
  --template-file infrastructure/bicep/modules/cosmos-db.bicep \
  --parameters accountName=spaarke-cosmos-dev \
  --parameters databaseName=spaarke-ai \
  --parameters appServicePrincipalId=$APP_SERVICE_PRINCIPAL_ID \
  --name cosmos-deploy-$(date +%Y%m%d-%H%M%S)
```

---

## RBAC Assignment Verification

The Bicep template auto-assigns the `Cosmos DB Built-in Data Contributor` role (ID: `00000000-0000-0000-0000-000000000002`) to the App Service managed identity. Verify after deployment:

```bash
# List Cosmos DB SQL role assignments
az cosmosdb sql role assignment list \
  --account-name spaarke-cosmos-dev \
  --resource-group rg-spaarke-platform-dev \
  --output table

# Expected: One assignment with:
#   Role Definition ID containing: 00000000-0000-0000-0000-000000000002
#   Principal ID matching App Service managed identity
```

If RBAC was not assigned (e.g., standalone deploy without principalId), assign manually:

```bash
APP_SERVICE_PRINCIPAL_ID=$(az webapp identity show \
  --resource-group rg-spaarke-platform-dev \
  --name spaarke-bff-dev \
  --query principalId -o tsv)

COSMOS_ACCOUNT_ID=$(az cosmosdb show \
  --name spaarke-cosmos-dev \
  --resource-group rg-spaarke-platform-dev \
  --query id -o tsv)

az cosmosdb sql role assignment create \
  --account-name spaarke-cosmos-dev \
  --resource-group rg-spaarke-platform-dev \
  --role-definition-id 00000000-0000-0000-0000-000000000002 \
  --principal-id $APP_SERVICE_PRINCIPAL_ID \
  --scope $COSMOS_ACCOUNT_ID
```

---

## Smoke Test Procedure

### 1. Verify account exists and is accessible

```bash
az cosmosdb show \
  --name spaarke-cosmos-dev \
  --resource-group rg-spaarke-platform-dev \
  --query "{name:name, endpoint:documentEndpoint, status:provisioningState}" \
  --output table
```

### 2. Verify database and containers exist

```bash
# List databases
az cosmosdb sql database list \
  --account-name spaarke-cosmos-dev \
  --resource-group rg-spaarke-platform-dev \
  --query "[].name" -o tsv
# Expected: spaarke-ai

# List containers
az cosmosdb sql container list \
  --account-name spaarke-cosmos-dev \
  --resource-group rg-spaarke-platform-dev \
  --database-name spaarke-ai \
  --query "[].{name:name, partitionKey:resource.partitionKey.paths[0], ttl:resource.defaultTtl}" \
  --output table
# Expected: sessions, prompts, audit, memory, feedback — all with /tenantId partition key
```

### 3. Verify connectivity from BFF API

After BFF API deployment (Task 121), confirm the API can write to Cosmos:
- Send a test chat message via the SpaarkeAi UI
- Check Data Explorer in Azure Portal for a new document in `sessions` container
- Or check BFF API logs for Cosmos write confirmation

---

## Results Template

| Check | Expected | Actual | Pass/Fail |
|---|---|---|---|
| Account provisioned | `spaarke-cosmos-dev` exists | | |
| Account endpoint | `https://spaarke-cosmos-dev.documents.azure.com:443/` | | |
| Capacity mode | Serverless | | |
| Database created | `spaarke-ai` | | |
| Container: sessions | `/tenantId`, TTL=7776000 | | |
| Container: prompts | `/tenantId`, TTL=7776000 | | |
| Container: audit | `/tenantId`, TTL=-1, analytical=on | | |
| Container: memory | `/tenantId`, TTL=7776000 | | |
| Container: feedback | `/tenantId`, TTL=-1 | | |
| RBAC assigned | Data Contributor to App Service MI | | |
| Backup policy | Continuous 7-day | | |
| TLS version | 1.2+ | | |

**Deployed By**: _______________
**Date Executed**: _______________
