# RAG Configuration Reference

> **Version**: 1.1
> **Created**: 2025-12-29
> **Updated**: 2026-01-16
> **Project**: AI Document Intelligence R3 + RAG Pipeline R1

---

## Table of Contents

1. [App Service Configuration](#app-service-configuration)
2. [Index Configuration](#index-configuration)
3. [Deployment Model Configuration](#deployment-model-configuration)
4. [Embedding Cache Configuration](#embedding-cache-configuration)
5. [Search Options](#search-options)
6. [Environment Variables](#environment-variables)
7. [Code Configuration Examples](#code-configuration-examples)

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
*Updated: 2026-01-16*
*AI Document Intelligence R3 - Phase 1 Complete*
*RAG Pipeline R1 - Phase 1 Complete*
