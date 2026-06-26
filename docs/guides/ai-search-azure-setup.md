# AI Search — Azure Setup & Operational Guide

> **Last Updated**: 2026-06-26 (created by `spaarke-ai-azure-setup-dev-r1` task 003 per FR-02)
> **Audience**: BFF operators, infrastructure engineers, platform DevOps
> **Status**: Authoritative — operational runbook for provisioning Azure AI Search in any Spaarke environment

This guide is the canonical operational reference for provisioning, configuring, deploying schemas to, validating, rotating secrets for, and troubleshooting the Spaarke AI Search service (`spaarke-search-{env}`) in any environment. A fresh operator should be able to set up a brand-new environment's AI Search end-to-end (service → KV secret → BFF MI role grant → 7 indexes deployed and verified) by following only this document.

For the canonical per-index inventory (naming, schema property policy, vector + embedding configuration, per-index consumer map, retired indexes appendix), see [`docs/architecture/AI-SEARCH-INDEX-CATALOG.md`](../architecture/AI-SEARCH-INDEX-CATALOG.md). This guide is the operator's "how"; the catalog is the architect's "what" — they cross-reference each other and must not disagree. For the environment-level deployment flow that calls this guide, see [`docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`](SPAARKE-DEPLOYMENT-GUIDE.md) §4.6.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Step-by-Step Setup Procedure](#2-step-by-step-setup-procedure)
3. [Per-Index Deploy + Ingestion Reference](#3-per-index-deploy--ingestion-reference)
4. [Troubleshooting](#4-troubleshooting)
5. [Post-Deploy Verification Commands](#5-post-deploy-verification-commands)
6. [Environment-Specific Variables](#6-environment-specific-variables)
7. [Cross-References](#7-cross-references)

---

## 1. Prerequisites

Before running any command in this guide, confirm each of the following:

### 1.1 Tooling

- **Azure CLI 2.55+ installed and logged in** — verify with `az account show`. Login per [Azure CLI authentication docs](https://learn.microsoft.com/en-us/cli/azure/authenticate-azure-cli). Ensure the active subscription matches the target environment.
- **PowerShell 7+ available** — verify with `pwsh -Version` (must be 7.0 or later). Windows PowerShell 5.1 is NOT supported.
- **Azure CLI extensions** — no extra extensions required for `az search`, `az role assignment`, `az keyvault`, or `az servicebus` (all are in the default CLI install).
- **REST API access** — `curl` (or PowerShell `Invoke-RestMethod`) for the post-deploy verification calls in §5.

### 1.2 Azure Resources

- **Resource group exists** for the target environment (`spe-infrastructure-westus2` for dev; `rg-spaarke-{staging|prod|demo}` for higher environments) — see §6 for the per-env table.
- **AI Search service provisioned** (or provision in Step 2.1 below) — top-level resource, env-suffixed per the [two-tier naming policy (NFR-03 / NFR-10)](../architecture/AI-SEARCH-INDEX-CATALOG.md#1-naming-convention).
- **BFF Key Vault accessible** — `spaarke-spekvcert` for dev (RG `SharePointEmbedded`, region `eastus` — cross-region KV reference works); per-env vault name in §6.
- **BFF Managed Identity exists** — `mi-bff-api-{env}` (user-assigned). Confirmed dev: `mi-bff-api-dev` with `principalId 9fd47efb-7962-492b-ac44-e5ccd0268ebb`.
- **Service Bus namespace + queues** (`sdap-jobs`, `sdap-communication`) exist for ingestion job handlers (see §4 troubleshooting for recreate procedure).
- **Azure OpenAI embedding deployment** — `text-embedding-3-large` (3072-dim) deployed in the target environment's OpenAI resource (confirmed dev: `spaarke-openai-dev` in eastus). This is binding per [NFR-11](../../projects/spaarke-ai-azure-setup-dev-r1/spec.md) — 1536-dim vectors are forbidden in any restored index.

### 1.3 RBAC — Operator + BFF Managed Identity

The operator running this guide and the BFF Managed Identity that consumes the indexes at runtime have different role requirements:

| Principal | Scope | Required Role | Why |
|---|---|---|---|
| Operator (you) | Target AI Search service | `Search Service Contributor` (or `Owner`) | Create/modify indexes, read admin keys |
| Operator (you) | Target Key Vault | `Key Vault Administrator` (or `Key Vault Secrets Officer`) | Set/rotate the `AiSearch--AdminKey` secret |
| Operator (you) | Target resource group | `Contributor` (or `Owner`) | Provision the search service, grant role assignments |
| BFF MI | Target AI Search service | `Search Index Data Contributor` | Runtime upsert + query of index documents |
| BFF MI | Target Key Vault | `Key Vault Secrets User` | Resolve `@Microsoft.KeyVault(...)` App Service references |

Verify operator roles before proceeding:

```powershell
$user = az account show --query user.name -o tsv
az role assignment list --assignee $user --scope /subscriptions/<sub-id>/resourceGroups/<rg> -o table
```

### 1.4 Pre-Flight Verification (Phase 3 deploy gate)

Before deploying schemas in a fresh or recreated environment, complete the **10-check pre-flight verification** per [`spec.md` FR-21](../../projects/spaarke-ai-azure-setup-dev-r1/spec.md) (5 Redis prereqs + 5 AI-Search prereqs). The dev evidence is captured in [`projects/spaarke-ai-azure-setup-dev-r1/notes/pre-phase-3-verification.md`](../../projects/spaarke-ai-azure-setup-dev-r1/notes/pre-phase-3-verification.md). The 5 AI-Search-specific checks are: KV admin-key freshness, BFF MI RBAC re-grant, Service Bus queue existence, OpenAI embedding deployment, and empirical resource state. **Failures block Phase 3.**

---

## 2. Step-by-Step Setup Procedure

Run these steps in order. Each step is idempotent — re-running against an already-configured environment is safe.

### Step 2.1 — Provision (or verify) the AI Search service

If the service does not yet exist:

```powershell
# Replace {env}, {region}, {rg} per §6
az search service create `
  --name spaarke-search-{env} `
  --resource-group {rg} `
  --sku standard `
  --location {region} `
  --partition-count 1 `
  --replica-count 1
```

Bicep alternative (preferred for new environments — declares resource shape canonically):

```bicep
resource searchService 'Microsoft.Search/searchServices@2024-03-01-preview' = {
  name: 'spaarke-search-${env}'
  location: location
  sku: { name: 'standard' }
  properties: {
    partitionCount: 1
    replicaCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'standard'  // Required for semantic ranker on spaarke-rag-references
  }
}
```

Verify provisioning succeeded:

```powershell
az search service show `
  -g {rg} -n spaarke-search-{env} `
  --query "{state:provisioningState,sku:sku.name,partitions:partitionCount,replicas:replicaCount}" -o table
```

Expect: `provisioningState = Succeeded`, `sku = standard`.

### Step 2.2 — Set the AI Search admin key in Key Vault

Spaarke standardizes on the canonical secret name **`AiSearch--AdminKey`** (per the 2026-06-26 owner decision — Option C). Legacy aliases `ai-search-key` and `AzureAISearchApiKey` exist as transition and will be retired in a future FR-15 follow-up; do NOT add new code or settings that reference them.

```powershell
# Retrieve the freshly-provisioned admin key
$newKey = (az search admin-key show `
  --service-name spaarke-search-{env} `
  -g {rg} `
  --query primaryKey -o tsv)

# Upsert it into Key Vault under the canonical name
az keyvault secret set `
  --vault-name {kv-name} `
  --name AiSearch--AdminKey `
  --value $newKey | Out-Null
```

Verify the secret is enabled:

```powershell
az keyvault secret show `
  --vault-name {kv-name} `
  --name AiSearch--AdminKey `
  --query "{enabled:attributes.enabled,updated:attributes.updated}" -o table
```

The BFF App Service App Settings reference this secret using the **canonical KV-reference syntax** (per the Redis project 2026-06-26 handoff §4):

```
@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=AiSearch--AdminKey)
```

**NOT** the `SecretUri=` form. The `VaultName=...;SecretName=...` form auto-resolves the latest enabled secret version and survives rotation without requiring an App Settings update.

### Step 2.3 — Grant the BFF Managed Identity `Search Index Data Contributor`

This role is what lets the BFF call the search service at runtime (read + write documents). The role assignment is lost whenever the search service is recreated, so this step must be re-run after any service rebuild.

```powershell
# Resolve the BFF MI principalId
$miPrincipalId = (az identity show `
  -g {bff-rg} `
  -n mi-bff-api-{env} `
  --query principalId -o tsv)

# Resolve the search service resource ID
$searchScope = (az search service show `
  -g {rg} -n spaarke-search-{env} `
  --query id -o tsv)

# Grant the role
az role assignment create `
  --assignee-object-id $miPrincipalId `
  --assignee-principal-type ServicePrincipal `
  --role "Search Index Data Contributor" `
  --scope $searchScope
```

Confirmed dev grant (2026-06-26): MI `9fd47efb-7962-492b-ac44-e5ccd0268ebb` has `Search Index Data Contributor` on `spaarke-search-dev`. The role-assignment Bicep is canonical at `infrastructure/byok/main.bicep:443-454`; this CLI step is the operational equivalent for environments not yet under Bicep authority (a Phase-4 backlog item exists to bring all envs under Bicep).

Verify:

```powershell
az role assignment list --all --assignee $miPrincipalId `
  --query "[?roleDefinitionName=='Search Index Data Contributor'].{scope:scope,role:roleDefinitionName}" -o table
```

Expect one row pointing at the target search service resource ID.

### Step 2.4 — Deploy the 7 canonical index schemas

All schemas deploy via the **single canonical deployer** `scripts/ai-search/Deploy-AllIndexes.ps1` (FR-07 — catalog-driven, idempotent, mirrors the validated `scripts/Deploy-RedisCache.ps1` structure). The script reads the canonical index list from [`AI-SEARCH-INDEX-CATALOG.md`](../architecture/AI-SEARCH-INDEX-CATALOG.md) §4 and PUTs each schema against the target environment's search service.

> **NOTE (Phase 1 of `spaarke-ai-azure-setup-dev-r1`)**: `Deploy-AllIndexes.ps1` is a Phase 3 deliverable (task 020). Until it lands, individual schemas can be deployed via the legacy per-index scripts (`deploy-session-files-index.ps1`, `Deploy-InvoiceSearchIndex.ps1`, etc.) — but these are retired by FR-07 in the same PR that ships the unified deployer. **Do not add new per-index wrapper scripts.**

Deploy all 7 indexes:

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 -Environment {env}
```

Deploy a subset:

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 `
  -Environment {env} `
  -Indexes files-index,records-index,rag-references
```

Dry-run preview (uses `-WhatIf` via `SupportsShouldProcess`):

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 -Environment {env} -WhatIf
```

Prod/demo require explicit `-Force` (NFR-05):

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 -Environment prod -Force
```

Optionally cut over BFF App Settings KV references in the same run:

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 -Environment {env} -CutoverBffSettings
```

### Step 2.5 — Verify post-deploy invariants

Run the post-deploy verifier (no re-deploy, asserts schema invariants per index):

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 -Environment {env} -VerifyOnly
```

The verifier asserts, per index, the canonical post-deploy invariants from the [catalog §4 table](../architecture/AI-SEARCH-INDEX-CATALOG.md#4-active-index-catalog-7-indexes): vector dimension (3072), key field presence, required-filterable fields (e.g., `tenantId` + `sessionId` for `spaarke-session-files`), and schema property policy compliance per [NFR-09](../../projects/spaarke-ai-azure-setup-dev-r1/spec.md). Non-zero exit on any failure.

Manual verification fallback (if the verifier is unavailable, or you want a quick eyeball check) — see §5 below for REST + CLI commands.

---

## 3. Per-Index Deploy + Ingestion Reference

This section is the operator's quick reference for "what data goes into which index, by which script". For schema field definitions, vector configuration, and consumer service mapping, see the [catalog §4 table](../architecture/AI-SEARCH-INDEX-CATALOG.md#4-active-index-catalog-7-indexes).

| # | Index | Schema deploy | Ingestion script | Ingestion notes |
|---|---|---|---|---|
| 1 | `spaarke-files-index` | `Deploy-AllIndexes.ps1 -Indexes files-index` | (deferred — runtime via `RagIndexingPipeline` + `FileIndexingService`) | Schema-only for dev rebuild per FR-18; runtime upserts happen as users upload files |
| 2 | `spaarke-records-index` | `Deploy-AllIndexes.ps1 -Indexes records-index` | `scripts/ai-search/Sync-RecordsToIndex.ps1 -Environment {env}` | Populates `tenantId` (per FR-12); reads from Dataverse |
| 3 | `spaarke-rag-references` | `Deploy-AllIndexes.ps1 -Indexes rag-references` | `scripts/ai-search/Index-AllReferences.ps1 -Environment {env}` | Indexes all `KNW-*.md` golden refs; field-name canonical = `documentType` (per FR-17 fix — never `domain`) |
| 4 | `spaarke-insights-index` | `Deploy-AllIndexes.ps1 -Indexes insights-index` (or Bicep `infra/insights/modules/search-index.bicep` — both valid per FR-11 resolved question) | `PrecedentProjectionSync` (Precedents) + pipeline re-run against event history (Observations) | Insights Bicep deployer retained for the insights index only; other 6 indexes are PowerShell-deployed |
| 5 | `spaarke-session-files` | `Deploy-AllIndexes.ps1 -Indexes session-files` | (runtime — `FileIndexingService` + `PostUploadIndexingEnqueuer`; cleanup: `SessionFilesCleanupJob`) | Transient by design; sessions auto-cleaned per ADR-014; strict `tenantId + sessionId` pair-filter on every query |
| 6 | `spaarke-invoices-index` | `Deploy-AllIndexes.ps1 -Indexes invoices-index` | (runtime — `InvoiceIndexingJobHandler`) | Schema-only for dev rebuild; MVP not in active testing |
| 7 | `spaarke-playbook-embeddings` | `Deploy-AllIndexes.ps1 -Indexes playbook-embeddings` | `scripts/Index-ExistingPlaybooks.ps1 -Environment {env}` | Indexes Dataverse playbook catalog (admin key read from `AiSearch--AdminKey` per the 2026-06-26 script fix) |

After every deploy + ingestion run, sanity-check non-zero document count for ingestible indexes:

```powershell
$adminKey = (az keyvault secret show --vault-name {kv-name} --name AiSearch--AdminKey --query value -o tsv)
$endpoint = "https://spaarke-search-{env}.search.windows.net"

# Replace {index} with one of the 7 canonical names
Invoke-RestMethod `
  -Uri "$endpoint/indexes/{index}/docs/`$count?api-version=2024-07-01" `
  -Headers @{ 'api-key' = $adminKey }
```

For full per-index schema field reference, [click through to the catalog §4 table](../architecture/AI-SEARCH-INDEX-CATALOG.md#4-active-index-catalog-7-indexes).

---

## 4. Troubleshooting

### 4.1 — `401 Unauthorized` from the search service

**Symptom**: `Deploy-AllIndexes.ps1` PUT calls (or any REST call) return `401 Unauthorized`. BFF healthcheck logs `Azure.RequestFailedException: 401`.

**Root cause**: The admin key in Key Vault is stale — almost certainly because the search service was deleted and recreated, which generates new admin keys. App Service KV references resolve to the OLD value cached in Key Vault until the secret is rotated.

**Diagnosis**:

```powershell
$liveKey = (az search admin-key show --service-name spaarke-search-{env} -g {rg} --query primaryKey -o tsv)
$kvKey   = (az keyvault secret show --vault-name {kv-name} --name AiSearch--AdminKey --query value -o tsv)
if ($liveKey -ne $kvKey) {
    Write-Host "DRIFT — KV admin key is stale"
} else {
    Write-Host "KV admin key matches live service key — 401 cause is elsewhere"
}
```

**Remediation** (per FR-21 #1):

```powershell
$newKey = (az search admin-key show --service-name spaarke-search-{env} -g {rg} --query primaryKey -o tsv)
az keyvault secret set --vault-name {kv-name} --name AiSearch--AdminKey --value $newKey | Out-Null

# Force App Service to pick up the new secret (Key Vault references refresh on App Service restart or ~24h cache TTL)
az webapp restart -g {bff-rg} -n spaarke-bff-{env}
```

### 4.2 — Role assignment missing for BFF Managed Identity

**Symptom**: BFF calls succeed against KV (KV-ref resolves) but fail against the search service with `403 Forbidden`.

**Root cause**: `Search Index Data Contributor` role assignment was lost — typically because the search service was recreated (role assignments live on the search service resource, not the MI).

**Diagnosis**:

```powershell
$miPrincipalId = (az identity show -g {bff-rg} -n mi-bff-api-{env} --query principalId -o tsv)
az role assignment list --all --assignee $miPrincipalId `
  --query "[?roleDefinitionName=='Search Index Data Contributor'].{scope:scope,role:roleDefinitionName}" -o table
```

Expect at least one row pointing at the target search service resource ID. If the list is empty, the role is missing.

**Remediation**: re-run Step 2.3 above. For dev, the canonical fix is to re-run `infrastructure/byok/main.bicep:443-454` (Bicep authority). The CLI fallback in Step 2.3 is the operational equivalent for ad-hoc grants.

### 4.3 — Vector dimension mismatch on ingestion

**Symptom**: `Add-ReferenceToIndex.ps1`, `Sync-RecordsToIndex.ps1`, or any embedding-generating ingestion path fails with:

```
Azure.RequestFailedException: 400 Bad Request:
The vector field 'contentVector' has dimensionality 1536, expected 3072.
```

**Root cause**: BFF embedding client (or PowerShell ingestion script) is configured for `text-embedding-3-small` (1536-dim) while the schema declares 3072-dim. This is the classic FR-20 drift — `appsettings.template.json:248` historically defaulted to `text-embedding-3-small`.

**Diagnosis**:

```powershell
# Check what model the BFF App Service thinks it's using
az webapp config appsettings list -g {bff-rg} -n spaarke-bff-{env} `
  --query "[?name=='AzureOpenAI__EmbeddingModelName'].value" -o tsv

# Or check what's in appsettings.template.json
grep -n '"EmbeddingModelName"' src/server/api/Sprk.Bff.Api/appsettings.template.json
```

Expect: `text-embedding-3-large`.

**Remediation**: align the BFF appsettings to `text-embedding-3-large` per FR-20. Re-deploy BFF. Re-run failed ingestion. See [catalog §3 Vector + Embedding Configuration](../architecture/AI-SEARCH-INDEX-CATALOG.md#3-vector--embedding-configuration) for the binding policy (NFR-11) and the canonical config values (3072-dim, HNSW cosine, `m=4, efConstruction=400, efSearch=500`).

**Architectural rollback NOT recommended**: per the catalog, rolling back to 1536-dim requires re-deploying the OpenAI deployment AND rewriting all 7 schemas AND re-ingesting all data. The 3072-dim policy is authoritative.

### 4.4 — Service Bus queue missing (ingestion jobs enqueue but never dequeue)

**Symptom**: `Sync-RecordsToIndex.ps1` and `Index-ExistingPlaybooks.ps1` complete successfully (jobs enqueued) but no documents appear in the indexes. BFF logs show jobs being scheduled but no handler invocations.

**Root cause**: Service Bus queues `sdap-jobs` and/or `sdap-communication` are missing or disabled in the target environment's Service Bus namespace.

**Diagnosis** (per FR-21 #3):

```powershell
az servicebus queue list -g {rg} --namespace-name <sb-namespace> `
  --query "[?contains(['sdap-jobs','sdap-communication'], name)].{name:name,status:status}" -o table
```

Expect two rows, both with `status = Active`. If missing, recreate via Bicep — the queues are declared in `infrastructure/bicep/customer.json:92-100` (and the per-customer Bicep template):

```powershell
az deployment group create `
  -g {rg} `
  --template-file infrastructure/bicep/customer.bicep `
  --parameters infrastructure/bicep/parameters/customer-{env}.bicepparam
```

### 4.5 — Empty index after deploy (post-deploy verifier failure)

**Symptom**: `Deploy-AllIndexes.ps1 -VerifyOnly` exits non-zero; logs `Index 'spaarke-X-Y' missing required field 'tenantId'` (or similar).

**Root cause options**:

1. **Schema file out-of-date** — local `infrastructure/ai-search/*.json` does not match what was deployed (someone hand-edited a deployed index outside the PS deployer).
2. **Partial deploy** — the deployer hit an exception mid-flight; some fields landed, others didn't.
3. **Wrong env target** — the verifier ran against a different env's search service than expected.

**Diagnosis + remediation**:

```powershell
# Confirm the deployed schema for the failing index
$adminKey = (az keyvault secret show --vault-name {kv-name} --name AiSearch--AdminKey --query value -o tsv)
$endpoint = "https://spaarke-search-{env}.search.windows.net"

Invoke-RestMethod `
  -Uri "$endpoint/indexes/{index-name}?api-version=2024-07-01" `
  -Headers @{ 'api-key' = $adminKey } | ConvertTo-Json -Depth 20

# Compare against the local schema file
Get-Content infrastructure/ai-search/{index-name}.json
```

If the schemas differ, re-run the deployer for that index:

```powershell
pwsh -File scripts/ai-search/Deploy-AllIndexes.ps1 -Environment {env} -Indexes {index-name} -Force
```

Note: Azure AI Search field flags cannot be changed post-create — if a field-flag drift is detected, the index requires deletion + recreate + re-ingestion. The deployer's `-Force` flag handles this orchestration. **In prod/demo, this step requires owner sign-off** (NFR-05).

---

## 5. Post-Deploy Verification Commands

After every deploy, run the commands in this section to confirm the environment is healthy. The post-deploy verifier in `Deploy-AllIndexes.ps1 -VerifyOnly` (Step 2.5) runs these programmatically; the commands below are the manual fallback + ad-hoc debug toolkit.

Set up shell variables once (replace placeholders per §6):

```powershell
$env       = 'dev'      # or staging|prod|demo
$searchSvc = "spaarke-search-$env"
$endpoint  = "https://$searchSvc.search.windows.net"
$kvName    = 'spaarke-spekvcert'  # see §6 for per-env values
$adminKey  = (az keyvault secret show --vault-name $kvName --name AiSearch--AdminKey --query value -o tsv)
$headers   = @{ 'api-key' = $adminKey }
```

### 5.1 — List all indexes (expect exactly 7)

```powershell
Invoke-RestMethod -Uri "$endpoint/indexes?api-version=2024-07-01" -Headers $headers `
  | Select-Object -ExpandProperty value `
  | Select-Object name `
  | Sort-Object name
```

Expect 7 rows: `spaarke-files-index`, `spaarke-insights-index`, `spaarke-invoices-index`, `spaarke-playbook-embeddings`, `spaarke-rag-references`, `spaarke-records-index`, `spaarke-session-files`. Any extra index (especially `spaarke-knowledge-index-v2`, `discovery-index`, `spaarke-knowledge-shared`) is a defect per the [retired-index appendix](../architecture/AI-SEARCH-INDEX-CATALOG.md#5-retired-indexes-appendix).

### 5.2 — Per-index field-flag verification

For each index, confirm canonical field flags from the [catalog Schema Property Policy §2](../architecture/AI-SEARCH-INDEX-CATALOG.md#2-schema-property-policy):

```powershell
# Replace {index-name} with one of the 7 canonical names
$index = Invoke-RestMethod -Uri "$endpoint/indexes/{index-name}?api-version=2024-07-01" -Headers $headers
$index.fields | Select-Object name, type, filterable, sortable, facetable, retrievable, searchable | Format-Table
```

Spot-check: `tenantId` (where present) must be `filterable=true, retrievable=true, searchable=false`. Vector fields must be `retrievable=false, stored=true, searchable=true`.

### 5.3 — Vector field profile + dimensionality verification

```powershell
$index = Invoke-RestMethod -Uri "$endpoint/indexes/{index-name}?api-version=2024-07-01" -Headers $headers
$index.vectorSearch.profiles  | Format-Table name, algorithm
$index.vectorSearch.algorithms | Format-Table name, kind, hnswParameters
$index.fields | Where-Object { $_.type -like '*Single*' } | Select-Object name, dimensions, vectorSearchProfile | Format-Table
```

Expect: every vector field has `dimensions = 3072`, HNSW algorithm with `metric = cosine` and `m=4, efConstruction=400, efSearch=500`.

### 5.4 — Semantic config verification (where applicable)

`spaarke-rag-references` and `spaarke-records-index` use semantic ranker:

```powershell
$index = Invoke-RestMethod -Uri "$endpoint/indexes/spaarke-rag-references?api-version=2024-07-01" -Headers $headers
$index.semantic.configurations | ConvertTo-Json -Depth 10
```

For `spaarke-rag-references`, expect the semantic config to reference field `documentType` (NOT `domain` — that's the FR-17 fix).

### 5.5 — Per-index document count

```powershell
foreach ($name in @('spaarke-files-index','spaarke-records-index','spaarke-rag-references','spaarke-insights-index','spaarke-session-files','spaarke-invoices-index','spaarke-playbook-embeddings')) {
    $count = Invoke-RestMethod -Uri "$endpoint/indexes/$name/docs/`$count?api-version=2024-07-01" -Headers $headers
    Write-Host ("{0,-35} count={1}" -f $name, $count)
}
```

Expected post-ingestion for dev (per FR-18):

- `spaarke-records-index` — non-zero (Dataverse records)
- `spaarke-rag-references` — non-zero (KNW-*.md golden refs)
- `spaarke-playbook-embeddings` — non-zero (Dataverse playbooks)
- `spaarke-insights-index` — non-zero (Precedents at minimum; Observations after re-projection)
- `spaarke-files-index`, `spaarke-session-files`, `spaarke-invoices-index` — zero (schema-only for dev rebuild)

### 5.6 — BFF functional verification (FR-19)

After ingestion, confirm the BFF's AI endpoints return real (non-error, non-empty) results:

```powershell
$bff = "https://spaarke-bff-$env.azurewebsites.net"

# 1. Healthcheck
Invoke-RestMethod -Uri "$bff/healthz"

# 2. Entity search (uses spaarke-records-index)
# POST /api/ai/search with scope=entity — see endpoint contract in src/server/api/Sprk.Bff.Api/Api/Ai/

# 3. RAG query (uses spaarke-files-index or spaarke-rag-references)
# POST /api/ai/rag/query

# 4. Insights search (uses spaarke-insights-index)
# POST /api/ai/insights/search

# 5. Playbook dispatch (uses spaarke-playbook-embeddings)
# Triggered via Dataverse action; verify Application Insights logs for successful dispatch routing
```

See [`spec.md` FR-19](../../projects/spaarke-ai-azure-setup-dev-r1/spec.md) for the full functional acceptance criteria.

---

## 6. Environment-Specific Variables

Per the [two-tier naming policy (NFR-03 / NFR-10)](../architecture/AI-SEARCH-INDEX-CATALOG.md#1-naming-convention), top-level resources are env-suffixed for global DNS uniqueness; sub-resources (indexes) are env-agnostic — the same canonical index names exist in every environment, scoped by the parent search service hostname.

| Setting | Dev | Staging | Prod | Demo |
|---|---|---|---|---|
| **AI Search service** | `spaarke-search-dev` | `spaarke-search-staging` | `spaarke-search-prod` | `spaarke-search-demo` |
| **Resource group (AI Search)** | `spe-infrastructure-westus2` | `rg-spaarke-staging` | `rg-spaarke-prod` | `rg-spaarke-demo` |
| **Region** | `westus2` | `westus2` (TBD) | `westus2` (TBD) | TBD |
| **SKU** | Standard | TBD (Standard min) | TBD (Standard min) | TBD |
| **Key Vault (BFF)** | `spaarke-spekvcert` | TBD | `sprk-platform-prod-kv` | TBD |
| **Key Vault RG** | `SharePointEmbedded` (eastus — cross-region KV ref works) | TBD | TBD | TBD |
| **KV secret name (admin key)** | `AiSearch--AdminKey` | `AiSearch--AdminKey` | `AiSearch--AdminKey` | `AiSearch--AdminKey` |
| **BFF App Service** | `spaarke-bff-dev` | `spaarke-bff-staging` | `spaarke-bff-prod` | `spaarke-bff-demo` |
| **BFF App Service RG** | `rg-spaarke-dev` | `rg-spaarke-staging` | `rg-spaarke-prod` | `rg-spaarke-demo` |
| **BFF Managed Identity** | `mi-bff-api-dev` | `mi-bff-api-staging` | `mi-bff-api-prod` | `mi-bff-api-demo` |
| **BFF MI principalId** | `9fd47efb-7962-492b-ac44-e5ccd0268ebb` | (resolve via `az identity show`) | (resolve via `az identity show`) | (resolve via `az identity show`) |
| **OpenAI resource** | `spaarke-openai-dev` (eastus) | TBD | TBD | TBD |
| **OpenAI embedding deployment** | `text-embedding-3-large` (3072-dim) | `text-embedding-3-large` | `text-embedding-3-large` | `text-embedding-3-large` |
| **Service Bus namespace** | (env-specific — verify per FR-21 #3) | TBD | TBD | TBD |

**Per `NFR-05`**: this project explicitly excludes prod and demo work — those columns are placeholders for future environment factory work (`spaarke-environment-factory-r1` is the downstream consumer of this guide). Do NOT run `Deploy-AllIndexes.ps1` against prod or demo during the `spaarke-ai-azure-setup-dev-r1` project execution window.

### Per-customer (sub-tenant) variables

Indexes are env-scoped at the top-level (search service hostname) but tenant-scoped at the document level via the `tenantId` field per the [catalog §4 table](../architecture/AI-SEARCH-INDEX-CATALOG.md#4-active-index-catalog-7-indexes). Customer-specific containers, Dataverse environments, and per-customer App Service resources are configured separately — see [`docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`](SPAARKE-DEPLOYMENT-GUIDE.md) §4.7 for the per-customer provisioning flow.

---

## 7. Cross-References

### Architecture

- [`docs/architecture/AI-SEARCH-INDEX-CATALOG.md`](../architecture/AI-SEARCH-INDEX-CATALOG.md) — Canonical per-index catalog (naming, schema policy, vector + embedding config, consumer map, retired indexes). **Always read alongside this guide.**
- [`docs/architecture/AI-ARCHITECTURE.md`](../architecture/AI-ARCHITECTURE.md) — System-wide AI architecture (BFF + AI Search consumer relationships).
- [`docs/architecture/rag-architecture.md`](../architecture/rag-architecture.md) — RAG (Retrieval-Augmented Generation) architecture and 7-index landscape narrative.
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — Insights engine architecture (consumer of `spaarke-insights-index`).
- [`docs/architecture/caching-architecture.md`](../architecture/caching-architecture.md) — Redis caching architecture (Phase 1.5 sibling).

### Operational

- [`docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`](SPAARKE-DEPLOYMENT-GUIDE.md) §4.6 — Where this guide is referenced from the full environment deployment flow. Appendix D lists `Deploy-AllIndexes.ps1`.
- [`docs/guides/redis-cache-azure-setup.md`](redis-cache-azure-setup.md) — Sibling operational guide for Redis (Phase 1.5 companion); structural template for this guide.
- [`docs/guides/AI-EMBEDDING-STRATEGY.md`](AI-EMBEDDING-STRATEGY.md) — Embedding model strategy and chunking; canonical embedding model = `text-embedding-3-large`.
- [`docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md`](MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md) — Multi-container BU operator runbook (canonical index names per BU).
- [`docs/guides/RAG-CONFIGURATION.md`](RAG-CONFIGURATION.md) — RAG configuration knobs (chunk size, top-K, score thresholds).
- [`docs/guides/auth-deployment-setup.md`](auth-deployment-setup.md) — Spaarke Auth v2 deployment (Key Vault + Managed Identity reference patterns; canonical for §1.3 + Step 2.2).

### Project context

- [`projects/spaarke-ai-azure-setup-dev-r1/spec.md`](../../projects/spaarke-ai-azure-setup-dev-r1/spec.md) — Spec for this project (21 FRs, 14 NFRs). FR-02 is this guide's acceptance criterion.
- [`projects/spaarke-ai-azure-setup-dev-r1/design.md`](../../projects/spaarke-ai-azure-setup-dev-r1/design.md) — Design rationale, resource inventory, 5-phase plan.
- [`projects/spaarke-ai-azure-setup-dev-r1/notes/pre-phase-3-verification.md`](../../projects/spaarke-ai-azure-setup-dev-r1/notes/pre-phase-3-verification.md) — Pre-Phase-3 10-check evidence (FR-21).
- [`projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md`](../../projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md) — Redis project handoff (canonical KV-ref syntax, Bicep+PS hybrid pattern, lessons applicable to AI Search ops).

### ADRs

- **ADR-013** — AI services bounded concurrency (ingestion `SemaphoreSlim` patterns: max 16 indexing, max 5 search).
- **ADR-017** — Background jobs (Service Bus pattern; `RagIndexingJobHandler` + `BulkRagIndexingJobHandler` contract).
- **ADR-028** — Spaarke Auth v2 (KV-ref + Managed Identity resolution; canonical for Step 2.2 syntax).
- **ADR-014** — *(currently misattributed: caching; inline-cited for tenant isolation principle — FR-06 resolves drift)*.
- **ADR-004** — *(currently misattributed: job contract; inline-cited for idempotent re-indexing principle — FR-06 resolves drift)*.

---

*Operator guide v1.0 — created 2026-06-26 (`spaarke-ai-azure-setup-dev-r1` task 003 per FR-02). Updated atomically whenever the canonical setup procedure changes (e.g., new env added, KV secret name changes, role-grant pattern changes). Cross-reference with `AI-SEARCH-INDEX-CATALOG.md` on every change.*
