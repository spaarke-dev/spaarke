# RAG Configuration Reference

> **Version**: 1.4
> **Created**: 2025-12-29
> **Updated**: 2026-01-23
> **Project**: AI Document Intelligence R3 + RAG Pipeline R1 + Semantic Search UI R2

---

## Table of Contents

1. [App Service Configuration](#app-service-configuration)
   - [Scheduled RAG Indexing](#scheduled-rag-indexing-catch-up-service)
   - [Bulk Indexing Admin Endpoints](#bulk-indexing-admin-endpoints)
2. [Index Configuration](#index-configuration)
3. [Deployment Model Configuration](#deployment-model-configuration)
4. [Embedding Cache Configuration](#embedding-cache-configuration)
5. [Search Options](#search-options)
6. [Semantic Search Configuration](#semantic-search-configuration) *(R1)*
7. [Semantic Search UI Configuration](#semantic-search-ui-configuration) *(R2 - NEW)*
8. [Environment Variables](#environment-variables)
9. [Code Configuration Examples](#code-configuration-examples)

---

## App Service Configuration

### Required Settings

Configure these in Azure App Service > Configuration > Application Settings:

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `DocumentIntelligence__Enabled` | Yes | `false` | Enable RAG features |
| `DocumentIntelligence__AiSearchEndpoint` | Yes | - | Azure AI Search endpoint URL |
| `DocumentIntelligence__AiSearchKey` | Yes | - | Azure AI Search admin API key |
| `DocumentIntelligence__AiSearchIndexName` | No | `spaarke-knowledge-index-v2` | Default index name (Shared model) |
| `DocumentIntelligence__EmbeddingModel` | No | `text-embedding-3-large` | Azure OpenAI embedding model deployment (3072 dims) |

### AI Services (Required for Embeddings)

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Ai__Enabled` | Yes | `false` | Enable AI features |
| `Ai__OpenAiEndpoint` | Yes | - | Azure OpenAI endpoint URL |
| `Ai__OpenAiKey` | Yes | - | Azure OpenAI API key |

### Redis Cache (Required for Embedding Cache)

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Redis__Enabled` | Yes | `false` | Enable Redis caching |
| `Redis__ConnectionString` | Yes | - | Redis connection string |

### Azure Service Bus (Required for Job Queue)

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `ServiceBus__ConnectionString` | Yes | - | Azure Service Bus connection string |
| `ServiceBus__QueueName` | No | `sdap-jobs` | Queue name for job processing |

**Resource Details (Dev Environment)**:
- **Namespace**: `spaarke-servicebus-dev`
- **Queue**: `sdap-jobs`
- **Region**: West US 2

**Setting the Connection String:**

```bash
# Azure App Service
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings "ServiceBus__ConnectionString=<connection-string-from-keyvault>"

# Local development (PowerShell)
$env:ServiceBus__ConnectionString = "<your-connection-string>"
```

**Key Vault Reference (Production)**:
```
ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/servicebus-connection-string/)
```

### Scheduled RAG Indexing (Catch-up Service)

The scheduled indexing service runs periodically to catch up on documents not indexed during upload. It submits bulk indexing jobs to the queue for background processing.

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `ScheduledRagIndexing__Enabled` | No | `false` | Enable scheduled bulk indexing |
| `ScheduledRagIndexing__IntervalMinutes` | No | `60` | Interval between indexing runs |
| `ScheduledRagIndexing__MaxDocumentsPerRun` | No | `100` | Max documents per batch |
| `ScheduledRagIndexing__MaxConcurrency` | No | `5` | Concurrent document processing |
| `ScheduledRagIndexing__TenantId` | Yes* | - | Tenant ID (*required if enabled) |

**Configuration Example:**

```json
{
  "ScheduledRagIndexing": {
    "Enabled": false,
    "IntervalMinutes": 60,
    "MaxDocumentsPerRun": 100,
    "MaxConcurrency": 5,
    "TenantId": "your-tenant-id"
  }
}
```

**Notes:**
- Only enable in production if you need catch-up indexing for documents missed on upload
- The service queries documents where `sprk_hasfile=true` AND `sprk_ragindexedon=null`
- Uses app-only authentication (Pattern 6) for Dataverse and SPE access
- Progress tracked via `BatchJobStatusStore` (Redis)

### Bulk Indexing Admin Endpoints

Admin endpoints for manual bulk document indexing (requires `SystemAdmin` authorization):

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/ai/rag/admin/bulk-index` | POST | Submit bulk indexing job |
| `/api/ai/rag/admin/bulk-index/{jobId}/status` | GET | Get job progress |

**Bulk Indexing Request:**
```json
{
  "tenantId": "required-tenant-id",
  "filter": "unindexed",      // "unindexed" or "all"
  "matterId": "optional",     // Filter by Matter ID
  "createdAfter": null,       // Date filter
  "createdBefore": null,      // Date filter
  "documentType": null,       // e.g., ".pdf"
  "maxDocuments": 1000,       // Batch limit
  "maxConcurrency": 5,        // Parallel processing
  "forceReindex": false       // Re-index already indexed docs
}
```

### RAG Indexing API Key (Job Queue Pattern)

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Rag__ApiKey` | Yes | - | API key for `/api/ai/rag/enqueue-indexing` endpoint |

**Security Notes:**
- This API key authenticates background jobs, scripts, and automated indexing operations
- The key is validated via `X-Api-Key` HTTP header
- For production, migrate to Key Vault: `@Microsoft.KeyVault(SecretUri=https://vault.vault.azure.net/secrets/rag-api-key/)`
- Rotate the key periodically (recommended: every 90 days)

**Setting the API Key:**

```bash
# Azure App Service
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings "Rag__ApiKey=rag-key-{guid}"

# Local development (PowerShell)
$env:Rag__ApiKey = "rag-key-{guid}"
```

**Using the API Key in Scripts:**

```powershell
# Set environment variable (don't commit to source control!)
$env:RAG_API_KEY = "rag-key-{guid}"

# Use in API calls
$headers = @{
    "X-Api-Key" = $env:RAG_API_KEY
    "Content-Type" = "application/json"
}
Invoke-WebRequest -Uri "$bffApiUrl/api/ai/rag/enqueue-indexing" -Headers $headers -Method Post -Body $json
```

### Example App Settings (appsettings.json)

```json
{
  "Ai": {
    "Enabled": true,
    "OpenAiEndpoint": "https://spaarke-openai-dev.openai.azure.com/",
    "OpenAiKey": "<from-key-vault>",
    "SummarizeModel": "gpt-4o-mini"
  },
  "DocumentIntelligence": {
    "Enabled": true,
    "AiSearchEndpoint": "https://spaarke-search-dev.search.windows.net",
    "AiSearchKey": "<from-key-vault>",
    "AiSearchIndexName": "spaarke-knowledge-index-v2",
    "EmbeddingModel": "text-embedding-3-large"
  },
  "Redis": {
    "Enabled": true,
    "ConnectionString": "<redis-connection-string>"
  },
  "ServiceBus": {
    "ConnectionString": "<from-key-vault>",
    "QueueName": "sdap-jobs"
  },
  "Rag": {
    "ApiKey": "<from-key-vault-or-secure-storage>"
  }
}
```

### Key Vault References (Production)

For production environments, use Key Vault references instead of plain text secrets:

```
DocumentIntelligence__AiSearchKey=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ai-search-key/)
Ai__OpenAiKey=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ai-openai-key/)
ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/servicebus-connection-string/)
Rag__ApiKey=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/rag-api-key/)
```

---

## Index Configuration

### Index Definition File

Location: `infrastructure/ai-search/spaarke-knowledge-index-v2.json`

### Creating the Index

```bash
# Using Azure CLI REST
az rest --method PUT \
  --uri "https://spaarke-search-dev.search.windows.net/indexes/spaarke-knowledge-index-v2?api-version=2024-07-01" \
  --headers "Content-Type=application/json" "api-key=<your-api-key>" \
  --body @infrastructure/ai-search/spaarke-knowledge-index-v2.json
```

### Index Schema Reference

```json
{
  "name": "spaarke-knowledge-index-v2",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "tenantId", "type": "Edm.String", "filterable": true },
    { "name": "deploymentId", "type": "Edm.String", "filterable": true },
    { "name": "deploymentModel", "type": "Edm.String", "filterable": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true },
    { "name": "speFileId", "type": "Edm.String", "filterable": true },
    { "name": "fileName", "type": "Edm.String", "searchable": true },
    { "name": "fileType", "type": "Edm.String", "filterable": true },
    { "name": "documentName", "type": "Edm.String", "searchable": true },
    { "name": "documentType", "type": "Edm.String", "searchable": true, "filterable": true },
    { "name": "chunkIndex", "type": "Edm.Int32", "filterable": true },
    { "name": "chunkCount", "type": "Edm.Int32" },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "contentVector3072", "type": "Collection(Edm.Single)", "dimensions": 3072, "vectorSearchProfile": "knowledge-vector-profile" },
    { "name": "documentVector3072", "type": "Collection(Edm.Single)", "dimensions": 3072, "vectorSearchProfile": "knowledge-vector-profile" },
    { "name": "tags", "type": "Collection(Edm.String)", "searchable": true, "filterable": true },
    { "name": "metadata", "type": "Edm.String" },
    { "name": "createdAt", "type": "Edm.DateTimeOffset", "filterable": true },
    { "name": "updatedAt", "type": "Edm.DateTimeOffset", "filterable": true }
  ],
  "vectorSearch": {
    "algorithms": [{
      "name": "knowledge-hnsw",
      "kind": "hnsw",
      "hnswParameters": {
        "m": 4,
        "efConstruction": 400,
        "efSearch": 500,
        "metric": "cosine"
      }
    }],
    "profiles": [{
      "name": "knowledge-vector-profile",
      "algorithmConfigurationName": "knowledge-hnsw"
    }]
  },
  "semantic": {
    "configurations": [{
      "name": "knowledge-semantic-config",
      "prioritizedFields": {
        "titleField": { "fieldName": "documentName" },
        "contentFields": [{ "fieldName": "content" }],
        "keywordsFields": [{ "fieldName": "tags" }]
      }
    }]
  }
}
```

### Vector Search Tuning

| Parameter | Default | Range | Impact |
|-----------|---------|-------|--------|
| `m` | 4 | 4-10 | Higher = better recall, more memory |
| `efConstruction` | 400 | 100-1000 | Higher = better index quality, slower build |
| `efSearch` | 500 | 100-1000 | Higher = better recall, slower query |
| `metric` | cosine | cosine, euclidean, dotProduct | cosine is standard for text embeddings |

---

## Deployment Model Configuration

### Shared Model (Default)

No additional configuration required. The system automatically uses:
- Index: `spaarke-knowledge-index`
- Isolation: `tenantId` filter

### Dedicated Model

Configure via `KnowledgeDeploymentConfig`:

```csharp
var config = new KnowledgeDeploymentConfig
{
    TenantId = "enterprise-customer",
    Name = "Enterprise Customer Dedicated Index",
    Model = RagDeploymentModel.Dedicated,
    // IndexName is auto-generated: enterprisecustomer-knowledge
    IsActive = true
};

await deploymentService.SaveDeploymentConfigAsync(config);
```

**Index Naming Rules**:
- Lowercase only
- Alphanumeric and hyphens only
- Format: `{sanitized-tenant-id}-knowledge`

### CustomerOwned Model

Configure via `KnowledgeDeploymentConfig`:

```csharp
var config = new KnowledgeDeploymentConfig
{
    TenantId = "byok-customer",
    Name = "BYOK Customer Index",
    Model = RagDeploymentModel.CustomerOwned,
    SearchEndpoint = "https://customer-search.search.windows.net",
    IndexName = "customer-knowledge-index",
    ApiKeySecretName = "byok-customer-search-key",  // Key Vault secret name
    IsActive = true
};

// Validate before saving
var validation = await deploymentService.ValidateCustomerOwnedDeploymentAsync(config);
if (!validation.IsValid)
{
    throw new InvalidOperationException(validation.ErrorMessage);
}

await deploymentService.SaveDeploymentConfigAsync(config);
```

**Required Fields for CustomerOwned**:

| Field | Description | Example |
|-------|-------------|---------|
| `SearchEndpoint` | Customer's AI Search URL | `https://customer-search.search.windows.net` |
| `IndexName` | Customer's index name | `customer-knowledge-index` |
| `ApiKeySecretName` | Key Vault secret name | `customer-api-key-secret` |

**Key Vault Secret Setup**:

```bash
# Store customer's API key in Key Vault
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name "byok-customer-search-key" \
  --value "<customer-api-key>"
```

---

## Embedding Cache Configuration

### Cache Settings

| Setting | Value | Description |
|---------|-------|-------------|
| Key Prefix | `sdap:embedding:` | Redis key prefix |
| TTL | 7 days | Time-to-live for cached embeddings |
| Serialization | Binary | float[] → byte[] via Buffer.BlockCopy |

### Cache Key Format

```
sdap:embedding:{base64-sha256-hash}
```

Example:
```
sdap:embedding:2jmj7l5rSw0yVb/vlWAYkK/YBwk=
```

### Disabling Cache

To disable embedding cache (not recommended for production):

```csharp
// In Program.cs, don't register IEmbeddingCache
// The RagService will generate embeddings without caching
```

### Cache Metrics

Monitor via OpenTelemetry:

```
cache_hits_total{cacheType="embedding"}
cache_misses_total{cacheType="embedding"}
cache_hit_rate{cacheType="embedding"}
```

---

## Search Options

### RagSearchOptions

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `TenantId` | string | Yes | - | Tenant for isolation |
| `TopK` | int | No | 10 | Maximum results |
| `MinScore` | float | No | 0.5 | Minimum relevance score (0-1) |
| `DocumentTypes` | string[] | No | null | Filter by document type |
| `Tags` | string[] | No | null | Filter by tags |
| `IncludeMetadata` | bool | No | false | Include metadata in results |

### Example Search Request

```json
{
  "query": "What are the payment terms?",
  "options": {
    "tenantId": "customer-123",
    "topK": 5,
    "minScore": 0.6,
    "documentTypes": ["contract", "agreement"],
    "tags": ["legal", "finance"]
  }
}
```

### Score Interpretation

| Score Range | Quality | Recommendation |
|-------------|---------|----------------|
| 0.8 - 1.0 | Excellent | Highly relevant |
| 0.6 - 0.8 | Good | Relevant |
| 0.4 - 0.6 | Fair | Possibly relevant |
| < 0.4 | Poor | Consider excluding |

---

## Semantic Search Configuration

> **Added in**: Semantic Search Foundation R1 (2026-01-20)

Semantic Search provides entity-scoped document search via the `/api/ai/search` endpoint.

### Enabling Semantic Search

Semantic Search requires both `DocumentIntelligence` and `Analysis` to be enabled:

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `DocumentIntelligence__Enabled` | Yes | `false` | Enables AI Search infrastructure |
| `Analysis__Enabled` | Yes | `true` | Enables analysis features (includes semantic search) |

**Note**: Semantic Search endpoints are only mapped if BOTH settings are `true`. See [conditional endpoint mapping](#conditional-endpoint-mapping).

### SemanticSearchRequest Options

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `query` | string | Yes | - | Search query text |
| `scope` | string | Yes | - | Scoping mode: `entity` or `documentIds` |
| `entityType` | string | Conditional | - | Required when `scope=entity` (matter, project, invoice, account, contact) |
| `entityId` | string | Conditional | - | Required when `scope=entity` |
| `documentIds` | string[] | Conditional | - | Required when `scope=documentIds` (max 100) |
| `options.hybridMode` | string | No | `rrf` | Search mode: `rrf`, `vector`, or `keyword` |
| `options.top` | int | No | 10 | Maximum results (1-100) |
| `options.skip` | int | No | 0 | Pagination offset |
| `options.minRelevanceScore` | float | No | 0.0 | Minimum score threshold (0-1) |
| `options.documentTypes` | string[] | No | null | Filter by document type |
| `options.fileTypes` | string[] | No | null | Filter by file extension |
| `options.tags` | string[] | No | null | Filter by tags |
| `options.dateRange.from` | DateTime | No | null | Created date filter (start) |
| `options.dateRange.to` | DateTime | No | null | Created date filter (end) |
| `options.includeContent` | bool | No | true | Include chunk content in results |

### Example Semantic Search Request

```json
{
  "query": "What are the payment terms in the contract?",
  "scope": "entity",
  "entityType": "matter",
  "entityId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "options": {
    "hybridMode": "rrf",
    "top": 10,
    "minRelevanceScore": 0.5,
    "documentTypes": ["contract"],
    "includeContent": true
  }
}
```

### Example DocumentIds Scope Request

```json
{
  "query": "payment schedule",
  "scope": "documentIds",
  "documentIds": [
    "doc-id-1",
    "doc-id-2",
    "doc-id-3"
  ],
  "options": {
    "top": 20
  }
}
```

### Validation Error Codes

| Error Code | HTTP Status | Description |
|------------|-------------|-------------|
| `QUERY_TOO_LONG` | 400 | Query exceeds maximum length |
| `INVALID_SCOPE` | 400 | Invalid scope value (must be `entity` or `documentIds`) |
| `SCOPE_NOT_SUPPORTED` | 400 | `scope=all` is not supported in R1 |
| `ENTITY_TYPE_REQUIRED` | 400 | Missing entityType when scope=entity |
| `ENTITY_ID_REQUIRED` | 400 | Missing entityId when scope=entity |
| `DOCUMENT_IDS_REQUIRED` | 400 | Missing documentIds when scope=documentIds |

### Conditional Endpoint Mapping

Semantic Search endpoints are only registered when both conditions are met:

```csharp
// Program.cs
if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
    app.Configuration.GetValue<bool>("Analysis:Enabled", true))
{
    app.MapSemanticSearchEndpoints();
}
```

This prevents 500 errors when the service is disabled but endpoints are still mapped.

### DI Registration

Semantic Search services are registered via `AddSemanticSearch()`:

```csharp
// Registered services (Program.cs)
services.AddSingleton<IQueryPreprocessor, NoOpQueryPreprocessor>();     // R1: no-op
services.AddSingleton<IResultPostprocessor, NoOpResultPostprocessor>(); // R1: no-op
services.AddScoped<ISemanticSearchService, SemanticSearchService>();
```

---

## Semantic Search UI Configuration

> **Added in**: Semantic Search UI R2 (2026-01-23)

Configuration for the PCF control and ribbon button integration.

### PCF Control Configuration

The SemanticSearchControl PCF requires the following configuration when added to a form:

| Property | Required | Description |
|----------|----------|-------------|
| `entityType` | Yes | Logical name of the parent entity (e.g., `sprk_matter`, `sprk_project`) |
| `entityId` | Yes | Bound to the record's primary key field |
| `bffApiUrl` | Yes | BFF API base URL (from environment variable `sprk_BffApiBaseUrl`) |
| `tenantId` | Yes | Organization ID for multi-tenant isolation |

**Example Form Configuration**:

```xml
<!-- Form XML configuration for SemanticSearchControl -->
<control id="semanticSearch" classid="{class-id}"
         uniqueid="{unique-id}" isunbound="false">
  <parameters>
    <entityType>sprk_matter</entityType>
    <entityId type="SingleLine.Text">sprk_matterid</entityId>
    <bffApiUrl>https://spe-api-dev-67e2xz.azurewebsites.net/api</bffApiUrl>
    <tenantId>{organizationId}</tenantId>
  </parameters>
</control>
```

### Send to Index Endpoint Configuration

The `/api/ai/rag/send-to-index` endpoint uses existing BFF API configuration plus:

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Analysis__SharedIndexName` | No | `spaarke-knowledge-index-v2` | Index name for tracking in Dataverse |

**Note**: The Send to Index endpoint uses OBO authentication from the user's session - no separate API key is required (unlike `/enqueue-indexing`).

### Ribbon Button Configuration

The ribbon buttons are configured in the `DocumentRibbons` solution.

**Solution Details**:
- **Name**: DocumentRibbons
- **Version**: 1.3.0.0
- **Publisher**: Spaarke
- **Location**: `infrastructure/dataverse/ribbon/DocumentRibbons/`

**Web Resource Configuration**:

| Web Resource | Version | Description |
|--------------|---------|-------------|
| `sprk_/scripts/sprk_DocumentOperations.js` | 1.24.0 | Ribbon button handlers |

**Environment Variable Dependencies**:

The JavaScript web resource reads these Dataverse environment variables:

| Variable | Schema Name | Purpose |
|----------|-------------|---------|
| BFF API URL | `sprk_BffApiBaseUrl` | Base URL for BFF API calls |

**Setting the Environment Variable**:

```powershell
# Using PAC CLI
pac env select --environment "Dev"
pac env variable set --name "sprk_BffApiBaseUrl" --value "https://spe-api-dev-67e2xz.azurewebsites.net/api"
```

### Dataverse Document Fields

The Send to Index operation updates these fields on the `sprk_document` entity:

| Field | Schema Name | Type | Description |
|-------|-------------|------|-------------|
| Search Indexed | `sprk_searchindexed` | Boolean | True if document is indexed |
| Search Index Name | `sprk_searchindexname` | String | Name of the search index |
| Search Indexed On | `sprk_searchindexedon` | DateTime | UTC timestamp of last indexing |

**Creating the Fields** (if not present):

```powershell
# Using PAC CLI
pac modelbuilder build --tableName "sprk_document" --outputDirectory "./models"
```

Or add manually via make.powerapps.com:
1. Navigate to Tables > Document
2. Add columns:
   - `sprk_searchindexed` (Yes/No, default: No)
   - `sprk_searchindexname` (Single Line Text, max 256)
   - `sprk_searchindexedon` (Date and Time)

### PCF Control Deployment

The SemanticSearchControl is deployed as a managed solution.

**Build and Package**:

```powershell
cd src/client/pcf/SemanticSearchControl
npm run build
msbuild /t:rebuild /p:Configuration=Release
```

**Deploy via PAC CLI**:

```powershell
pac pcf push --publisher-prefix sprk
```

**Or import as solution**:

```powershell
# The solution zip is in Solution/bin/
pac solution import --path Solution/bin/SpaarkeSemanticSearch_v1.0.15.zip
```

### Version Alignment

Ensure version numbers are aligned across these files:

| File | Field | Current |
|------|-------|---------|
| `ControlManifest.Input.xml` | `version` | 1.0.15 |
| `Solution/src/Other/Solution.xml` | `Version` | 1.0.15 |
| `Solution/bin/ControlManifest.xml` | `version` | 1.0.15 |
| Control UI footer | Display version | 1.0.15 |

See [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) for version bumping procedures.

---

## Environment Variables

### Dataverse Environment Variables

| Variable | Schema Name | Default |
|----------|-------------|---------|
| BFF API URL | `sprk_BffApiBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net/api` |
| AI Search Endpoint | `sprk_AiSearchEndpoint` | `https://spaarke-search-dev.search.windows.net` |
| AI Search Index | `sprk_AiSearchIndexName` | `spaarke-knowledge-index-v2` |

### Setting Environment Variables

```bash
# Azure App Service
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings "DocumentIntelligence__Enabled=true"

# Local Development (PowerShell)
$env:DocumentIntelligence__Enabled = "true"
$env:DocumentIntelligence__AiSearchEndpoint = "https://spaarke-search-dev.search.windows.net"
```

---

## Code Configuration Examples

### DI Registration (Program.cs)

```csharp
// Register AI Search client
if (!string.IsNullOrEmpty(docIntelOptions.AiSearchEndpoint))
{
    builder.Services.AddSingleton(sp =>
    {
        var endpoint = new Uri(docIntelOptions.AiSearchEndpoint);
        var credential = new AzureKeyCredential(docIntelOptions.AiSearchKey);
        return new SearchIndexClient(endpoint, credential);
    });

    // Register deployment service
    builder.Services.AddSingleton<IKnowledgeDeploymentService>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<KnowledgeDeploymentService>>();
        var options = sp.GetRequiredService<IOptions<DocumentIntelligenceOptions>>();
        var searchIndexClient = sp.GetRequiredService<SearchIndexClient>();
        var keyVaultClient = sp.GetService<SecretClient>();
        return new KnowledgeDeploymentService(logger, options, searchIndexClient, keyVaultClient);
    });

    // Register embedding cache
    builder.Services.AddSingleton<IEmbeddingCache, EmbeddingCache>();

    // Register RAG service
    builder.Services.AddScoped<IRagService, RagService>();

    // Register text chunking service (RAG Pipeline R1)
    builder.Services.AddSingleton<ITextChunkingService, TextChunkingService>();

    // Register file indexing service (RAG Pipeline R1)
    builder.Services.AddScoped<IFileIndexingService, FileIndexingService>();

    // Register idempotency service for job processing (RAG Pipeline R1)
    builder.Services.AddSingleton<IIdempotencyService, IdempotencyService>();

    // Register RAG indexing job handler (RAG Pipeline R1)
    builder.Services.AddScoped<RagIndexingJobHandler>();
}
```

### Endpoint Registration (Program.cs)

```csharp
// Map RAG endpoints
app.MapRagEndpoints();
```

### Custom Deployment Config

```csharp
// Create dedicated deployment for enterprise customer
public async Task SetupEnterpriseCustomer(string tenantId)
{
    var config = new KnowledgeDeploymentConfig
    {
        TenantId = tenantId,
        Name = $"Dedicated deployment for {tenantId}",
        Model = RagDeploymentModel.Dedicated,
        IsActive = true
    };

    await _deploymentService.SaveDeploymentConfigAsync(config);

    // Index will be created on first use with name: {tenantId}-knowledge
}
```

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) | Architecture overview |
| [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) | Troubleshooting guide |
| [AI-DEPLOYMENT-GUIDE.md](AI-DEPLOYMENT-GUIDE.md) | Full deployment guide |

---

*Document created: 2025-12-29*
*Updated: 2026-01-23*
*AI Document Intelligence R3 - Phase 1 Complete*
*RAG Pipeline R1 - Phase 1 Complete*
*Semantic Search Foundation R1 - Complete (configuration section added)*
*Semantic Search UI R2 - PCF control and ribbon button configuration added*
