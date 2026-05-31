# Spaarke Insights Engine — Infrastructure

> **Deliverable**: D-P2 (SPEC §3.1, task 010)
> **Scope**: per-tenant Bicep deployment unit for the Insights Engine — `spaarke-insights-index` on the existing AI Search service + Function App shell (no functions deployed) + per-tenant UAMI + Key Vault RBAC.

## Single-tenant deployment pattern (D-52)

**One customer = one parameter file = one `az deployment group create`.** No tenant loops in Bicep, no shared "control plane" yet — the simplest possible isolation boundary.

```
infra/insights/
├── main.bicep                                     # composes the modules
├── parameters/
│   ├── dev.json                                   # Spaarke Dev (internal)
│   └── <customer>.json                            # one file per customer (Phase 2+)
├── modules/
│   ├── managed-identity.bicep                     # per-tenant UAMI
│   ├── monitoring.bicep                           # resolves existing App Insights / Log Analytics
│   ├── keyvault-secrets.bicep                     # grants UAMI 'Key Vault Secrets User'
│   ├── function-app.bicep                         # Flex Consumption shell (no functions)
│   └── search-index.bicep                         # PUTs the index schema via deploymentScript
└── schemas/
    └── spaarke-insights-index.index.json          # canonical index schema (SPEC §3.4)
```

## What this deploys

For the target tenant:

1. **Per-tenant UAMI** — `insights-<tenant>-uami`. This is the Function App's identity and the auth boundary for everything the Insights Engine does on that tenant's behalf. Per D-27/ADR-024, no `ClientSecretCredential` is created.
2. **Function App shell** — `insights-<tenant>-func` on a `insights-<tenant>-plan` (Flex Consumption FC1, dotnet-isolated 8.0, 2048 MB per instance, 1 always-ready). No functions are deployed in Phase 1 — task 050 ships the SPE-upload consumer.
3. **Storage account** — `insights<tenant>stg` (no dashes; storage naming rules). Required by Flex Consumption for the deployment package + runtime state.
4. **Key Vault RBAC grant** — `Key Vault Secrets User` on the existing `sprkspaarkedev-aif-kv` for the per-tenant UAMI. Lets the Function App resolve `@Microsoft.KeyVault(...)` references at app start.
5. **`spaarke-insights-index`** — created/updated on the existing `spaarke-search-dev` service via a one-shot `deploymentScript`. Schema per [SPEC §3.4](../../projects/ai-spaarke-insights-engine-r1/SPEC.md): `artifactType` discriminator, `tenantId` first-class, 3072-dim `contentVector` (matches `text-embedding-3-large` on `spaarke-openai-dev`), vectorFilterMode=preFilter friendly (all filter fields are `filterable=true`).

## What this does NOT deploy

Out of scope per Phase 1 / SPEC §3.6:
- No Cosmos DB (graph deferred to Phase 1.5 per D-P17)
- No Service Bus topic (D-P8 / task 050 decides whether to add one)
- No Dataverse plugins or service endpoints
- No new App Insights or Log Analytics (reuses existing)
- No SAS keys, no `ClientSecretCredential` (per D-27/D-24)

## Deploying to Spaarke Dev

Prereqs:
- Logged in to Azure CLI on subscription `484bc857-3802-427f-9ea5-ca47b43db0f0` (Spaarke Dev)
- Bicep CLI available (`az bicep version`)
- Permissions: `Contributor` on the deployment resource group + `Owner` on the search service (deploymentScript needs to assign RBAC for its own ACI UAMI)

```bash
az deployment group create \
  --resource-group spe-infrastructure-westus2 \
  --template-file infra/insights/main.bicep \
  --parameters @infra/insights/parameters/dev.json \
  --name insights-engine-spaarkedev-$(date +%Y%m%d%H%M%S)
```

What-if preview (no changes applied):

```bash
az deployment group what-if \
  --resource-group spe-infrastructure-westus2 \
  --template-file infra/insights/main.bicep \
  --parameters @infra/insights/parameters/dev.json
```

## Verifying the deployment

After deployment completes, verify the index exists with the expected schema:

```bash
SEARCH_KEY=$(az search admin-key show \
  -g spe-infrastructure-westus2 \
  --service-name spaarke-search-dev \
  --query primaryKey -o tsv)

curl -s -H "api-key: $SEARCH_KEY" \
  "https://spaarke-search-dev.search.windows.net/indexes/spaarke-insights-index?api-version=2024-07-01" \
  | jq '.fields[] | {name, type, filterable, dimensions}'
```

Expected: `id`, `tenantId`, `artifactType`, `subject`, `predicate`, `value` (complex), `valueJson`, `confidence`, `evidence` (complex collection), `asOf`, `producedBy`, `content`, `contentVector` (Collection(Edm.Single), 3072 dims), `status`.

Verify the Function App shell:

```bash
az functionapp show \
  -g spe-infrastructure-westus2 \
  -n insights-spaarkedev-func \
  --query "{name:name, state:state, kind:kind, plan:appServicePlanId, hostName:defaultHostName, alwaysReady:functionAppConfig.scaleAndConcurrency.alwaysReady}" \
  -o jsonc
```

## Onboarding a new customer

Per D-52, adding a new customer is mechanical:

1. **Decide a 2–20 character lowercase tenant short name** — used in every resource name. Recommend something like `acmelegal`, `bigfirmllp`. Lowercase only, alphanumeric + dashes.
2. **Copy `parameters/dev.json` to `parameters/<tenant>.json`** and update:
   - `tenantShortName` (REQUIRED — must be unique)
   - `tenantDisplayName` (human label)
   - `searchServiceName` / `searchServiceResourceGroup` (if customer has a dedicated service; else share dev/prod)
   - `keyVaultName`, `appInsightsName`, `logAnalyticsWorkspaceName` (similarly per-tenant or shared per environment)
   - `environmentName` (`dev` / `test` / `prod`)
3. **Run `az deployment group create`** with the new parameter file targeting the customer's resource group.

That's the entire customer-onboarding lift for the Insights Engine substrate. Function code is then deployed via `func azure functionapp publish insights-<tenant>-func` once task 050 ships.

## Re-running the index schema deployment

The `deploy-spaarke-insights-index` deploymentScript runs on every Bicep deployment (`forceUpdateTag: utcNow()`) and PUTs the index schema. PUT is idempotent: if the index already exists with the same schema, the operation is a no-op from a data perspective; if the schema differs, the index is updated in place. Note: **adding a new field** is non-breaking. **Changing a field's type or removing a field** requires dropping and recreating the index (AI Search limitation, not a Bicep limitation).

## ADR / decision compliance

| Decision / ADR | How this satisfies it |
|---|---|
| D-08 (text-embedding-3-large, 3072 dim) | `contentVector` field dimensions=3072 |
| D-27 / ADR-024 (no ClientSecretCredential) | Per-tenant UAMI + RBAC; no client secrets created |
| D-33 (vectorFilterMode=preFilter friendly) | All filter fields (`tenantId`, `artifactType`, `predicate`, `subject`, `status`, `value.raw.*`) are `filterable: true` |
| D-52 (single-tenant deployment unit) | One parameter file per customer; no tenant loops |
| D-53 revised (one derived index) | Single `spaarke-insights-index` with `artifactType` discriminator (not 5 separate indexes) |
| ADR-001 (Functions only for out-of-band integration) | Function App shell created for D-P8 SPE-upload consumer (out-of-band integration); no BFF endpoints rehomed |
| ADR-010 (DI minimalism) | N/A — infra layer |

## Known constraints / future work

- **`AzureWebJobsStorage`** still uses an account-key connection string because Flex Consumption requires it at provision time for the deployment container. Task 050 / D-P8 may migrate to identity-based `AzureWebJobsStorage__credential = managedidentity` once the function code ships. Tracked as future work; does not violate D-24 because no NEW SAS keys are created — only the auto-generated storage account key is used by the platform itself.
- **`vectorizer`** is not configured on the index in Phase 1. The push-mode ingest pipeline (D-P8) will embed via the BFF's existing `IOpenAiClient`; query-time embedding happens via the same path. If query-time text-to-vector via `VectorizableTextQuery` is wanted later, add an `AzureOpenAIVectorizer` to the index and grant the search service identity `Cognitive Services OpenAI User` on `spaarke-openai-dev`.
- **`semantic` ranker billing**: the index ships with a semantic configuration, but billing only kicks in when callers pass `queryType=semantic`. Phase 1 sticks to hybrid (BM25 + vector) for the cohort retrieval in `predict-matter-cost`; semantic ranking is an option to evaluate in Phase 1.5+.
