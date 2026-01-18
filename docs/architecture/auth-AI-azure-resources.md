# AI Azure Resources

> **Last Updated**: January 16, 2026
> **Purpose**: Quick reference for AI-related Azure resource IDs and configuration.
> **Secrets**: Actual secrets stored in `config/ai-config.local.json` (gitignored)
> **Verified**: AI Search & Visualization Module (2026-01-12), RAG Pipeline R1 (2026-01-16)

---

## Quick Reference - All Endpoints

| Service | Endpoint |
|---------|----------|
| Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| Document Intelligence | `https://westus2.api.cognitive.microsoft.com/` |
| Azure AI Search | `https://spaarke-search-dev.search.windows.net/` |
| AI Foundry Studio | [Portal Link](https://ai.azure.com) |

---

## Azure OpenAI Service

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-openai-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **SKU** | S0 (Standard) |
| **Endpoint** | `https://spaarke-openai-dev.openai.azure.com/` |
| **Resource ID** | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev` |

### Model Deployments

| Deployment Name | Model | Version | Capacity | Purpose |
|-----------------|-------|---------|----------|---------|
| `gpt-4o-mini` | gpt-4o-mini | 2024-07-18 | 10 TPM | Document analysis, chat |
| `text-embedding-3-small` | text-embedding-3-small | 1 | 120 TPM | Vector embeddings (legacy 1536 dims) - **DEPRECATED** |
| `text-embedding-3-large` | text-embedding-3-large | 1 | 120 TPM | Vector embeddings for RAG (3072 dims) - **PRIMARY** |

**MIGRATION COMPLETE (2026-01-12)**: All RAG documents migrated to 3072-dim vectors via `text-embedding-3-large`. The 1536-dim fields remain for backward compatibility but new indexing uses 3072-dim exclusively.

### Create text-embedding-3-large Deployment

```bash
az cognitiveservices account deployment create \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --deployment-name text-embedding-3-large \
  --model-name text-embedding-3-large \
  --model-version "1" \
  --model-format OpenAI \
  --sku-capacity 120 \
  --sku-name Standard
```

### Rate Limits

| Limit Type | Value |
|------------|-------|
| Requests per minute | 100 |
| Tokens per minute | 10,000 (gpt-4o-mini) |

---

## Key Vault Secrets

Secrets are stored in Key Vault: `spaarke-spekvcert` (SharePointEmbedded resource group)

| Secret Name | Description | Last Updated |
|-------------|-------------|--------------|
| `ai-openai-endpoint` | Azure OpenAI endpoint URL | 2025-12-09 |
| `ai-openai-key` | Azure OpenAI API key (Key1) | 2025-12-09 |
| `ai-docintel-endpoint` | Document Intelligence endpoint URL | 2025-12-09 |
| `ai-docintel-key` | Document Intelligence API key (Key1) | 2025-12-09 |
| `rag-api-key` | RAG indexing API key for `/api/ai/rag/enqueue-indexing` | 2026-01-17 |
| `servicebus-connection-string` | Azure Service Bus connection string | 2026-01-17 |

### Key Vault Access

The App Service (`spe-api-dev-67e2xz`) needs `Key Vault Secrets User` role to access these secrets.

---

## App Service Configuration

Settings configured in `spe-api-dev-67e2xz`:

| Setting | Value | Notes |
|---------|-------|-------|
| `Ai__Enabled` | `true` | Master switch for AI features |
| `Ai__OpenAiEndpoint` | `https://spaarke-openai-dev.openai.azure.com/` | Can use Key Vault reference |
| `Ai__OpenAiKey` | (configured) | Should use Key Vault reference in production |
| `Ai__SummarizeModel` | `gpt-4o-mini` | Deployment name, not model name |
| `Ai__DocIntelEndpoint` | `https://westus2.api.cognitive.microsoft.com/` | Document Intelligence endpoint |
| `Ai__DocIntelKey` | (configured) | Should use Key Vault reference in production |

### Key Vault Reference Format (Production)

```
Ai__OpenAiEndpoint=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ai-openai-endpoint/)
Ai__OpenAiKey=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ai-openai-key/)
Ai__DocIntelEndpoint=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ai-docintel-endpoint/)
Ai__DocIntelKey=@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/ai-docintel-key/)
```

---

## Azure CLI Commands

### View OpenAI Resource
```bash
az cognitiveservices account show \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2
```

### List Model Deployments
```bash
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table
```

### Get API Keys
```bash
az cognitiveservices account keys list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2
```

### Rotate API Key
```bash
az cognitiveservices account keys regenerate \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --key-name key1
```

---

## Azure Document Intelligence

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-docintel-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **SKU** | S0 (Standard) |
| **Endpoint** | `https://westus2.api.cognitive.microsoft.com/` |
| **Resource ID** | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-docintel-dev` |

### Supported Document Types

| Extension | Model Used | Notes |
|-----------|------------|-------|
| `.pdf` | prebuilt-read | Full text extraction |
| `.docx` | prebuilt-read | Word documents |
| `.doc` | prebuilt-read | Legacy Word documents |

### Rate Limits

| Limit Type | Value |
|------------|-------|
| Requests per second | 15 |
| Concurrent requests | 15 |

### Azure CLI Commands

```bash
# View Document Intelligence Resource
az cognitiveservices account show \
  --name spaarke-docintel-dev \
  --resource-group spe-infrastructure-westus2

# Get API Keys
az cognitiveservices account keys list \
  --name spaarke-docintel-dev \
  --resource-group spe-infrastructure-westus2

# Rotate API Key
az cognitiveservices account keys regenerate \
  --name spaarke-docintel-dev \
  --resource-group spe-infrastructure-westus2 \
  --key-name key1
```

---

## Azure AI Search

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-search-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **SKU** | Standard |
| **Endpoint** | `https://spaarke-search-dev.search.windows.net/` |

### Indexes

| Index Name | Purpose | Vector Dims | Status |
|------------|---------|-------------|--------|
| `spaarke-records-index` | Record matching (Matters, Projects, etc.) | N/A | Active |
| `spaarke-knowledge-index-v2` | RAG knowledge retrieval + Document Visualization | 3072 | **Active** |
| `spaarke-knowledge-index` | RAG knowledge retrieval (R3 legacy) | 1536 | Deprecated |

### RAG Knowledge Index (`spaarke-knowledge-index-v2`)

Current production index with 3072-dim vectors and document visualization support:

| Feature | Configuration |
|---------|---------------|
| **Chunk Vector Field** | `contentVector3072` - 3072 dimensions (text-embedding-3-large) |
| **Document Vector Field** | `documentVector3072` - 3072 dimensions (averaged from chunks) |
| **Vector Algorithm** | HNSW (m=4, efConstruction=400, efSearch=500, cosine) |
| **Semantic Config** | `knowledge-semantic-config` |
| **Multi-Tenant Fields** | `tenantId`, `deploymentId`, `deploymentModel` |
| **File Fields** | `speFileId` (required), `documentId` (optional for orphans), `fileName`, `fileType` |

**New Features (AI Search & Visualization Module - 2026-01-12)**:
- **Document-level vectors**: `documentVector3072` enables similarity search across entire documents
- **Orphan file support**: Files without Dataverse records supported (`documentId` nullable)
- **File metadata**: `speFileId`, `fileName`, `fileType` for better identification

**Index Definition**: [`infrastructure/ai-search/spaarke-knowledge-index-v2.json`](../../infrastructure/ai-search/spaarke-knowledge-index-v2.json)

### RAG Deployment Models (R3)

The RAG system supports 3 deployment models for multi-tenant isolation:

| Model | Index Location | Use Case |
|-------|---------------|----------|
| **Shared** | `spaarke-knowledge-index` with `tenantId` filter | Default, cost-effective |
| **Dedicated** | `{tenantId}-knowledge` in Spaarke subscription | Per-customer isolation |
| **CustomerOwned** | Customer's own Azure AI Search instance | Full data sovereignty (BYOK) |

**Service**: `IKnowledgeDeploymentService` routes requests to the correct deployment model based on tenant configuration.

---

## AI Foundry Infrastructure

### AI Foundry Hub

| Property | Value |
|----------|-------|
| **Name** | `sprkspaarkedev-aif-hub` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **Type** | Microsoft.MachineLearningServices/workspaces |

### AI Foundry Project

| Property | Value |
|----------|-------|
| **Name** | `sprkspaarkedev-aif-proj` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **Parent Hub** | `sprkspaarkedev-aif-hub` |

### Configured Connections

| Connection Name | Target Service | Endpoint |
|-----------------|---------------|----------|
| azure-openai-connection | Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| ai-search-connection | Azure AI Search | `https://spaarke-search-dev.search.windows.net/` |

### Supporting Resources

| Resource | Name | Purpose |
|----------|------|---------|
| Key Vault | `sprkspaarkedev-aif-kv` | Connection secrets |
| Storage Account | `sprkspaarkedevaifsa` | Flow artifacts, models |
| Application Insights | `sprkspaarkedev-aif-insights` | Runtime monitoring |
| Log Analytics | `sprkspaarkedev-aif-logs` | Diagnostic logs |

### Azure CLI Commands

```bash
# View AI Foundry Hub
az ml workspace show \
  --name sprkspaarkedev-aif-hub \
  --resource-group spe-infrastructure-westus2

# View AI Foundry Project
az ml workspace show \
  --name sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2

# List Connections
az ml connection list \
  --workspace-name sprkspaarkedev-aif-proj \
  --resource-group spe-infrastructure-westus2
```

---

## Local Development

For local development, copy `config/ai-config.local.json.template` to `config/ai-config.local.json` and fill in the values.

The local config file is gitignored and contains:
- OpenAI endpoint
- OpenAI API key (for local testing only)

---

## Dataverse Entity Extraction Fields

The following Dataverse fields store AI-extracted entity data on the `sprk_document` entity:

### Extraction Fields

| Display Name | Logical Name | Type | Purpose |
|--------------|--------------|------|---------|
| Extract Dates | `sprk_extractdates` | Multiline Text | Dates found in document (newline-separated) |
| Extract Document Type | `sprk_extractdocumenttype` | Text | AI classification (contract, invoice, etc.) |
| Extract Fees | `sprk_extractfees` | Multiline Text | Monetary amounts (newline-separated) |
| Extract Organization | `sprk_extractorganization` | Multiline Text | Organization names (newline-separated) |
| Extract People | `sprk_extractpeople` | Multiline Text | Person names (newline-separated) |
| Extract Reference | `sprk_extractreference` | Multiline Text | Reference numbers (newline-separated) |

### Document Type Choice Field

The `sprk_DocumentType` choice field maps AI classifications to validated options:

| Label | Value | AI Mapping |
|-------|-------|------------|
| Contract | 100000000 | `contract` |
| Invoice | 100000001 | `invoice` |
| Proposal | 100000002 | `proposal` |
| Report | 100000003 | `report` |
| Letter | 100000004 | `letter` |
| Memo | 100000005 | `memo` |
| Email | 100000006 | `email` |
| Agreement | 100000007 | `agreement` |
| Statement | 100000008 | `statement` |
| Other | 100000009 | `other` (default) |

### Data Flow

```
Document Upload → AI Analysis → ExtractedEntities model → Dataverse fields
                                      │
                                      ├─ Organizations → sprk_extractorganization
                                      ├─ People → sprk_extractpeople
                                      ├─ Amounts → sprk_extractfees
                                      ├─ Dates → sprk_extractdates
                                      ├─ References → sprk_extractreference
                                      └─ DocumentType → sprk_extractdocumenttype + sprk_DocumentType (choice)
```

See [SPAARKE-AI-ARCHITECTURE.md](../guides/SPAARKE-AI-ARCHITECTURE.md#5-entity-extraction--dataverse-integration) for extensibility details.

---

## Azure Service Bus (RAG Job Queue)

| Property | Value |
|----------|-------|
| **Namespace** | `spaarke-servicebus-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **SKU** | Standard |
| **Queue** | `sdap-jobs` |

### Queue Configuration

| Setting | Value | Description |
|---------|-------|-------------|
| Max delivery count | 3 | Retries before dead-letter |
| Lock duration | 5 minutes | Message processing window |
| Max size | 1 GB | Queue size limit |

### Job Types Processed

| Job Type | Handler | Description |
|----------|---------|-------------|
| `RagIndexing` | `RagIndexingJobHandler` | Index files to AI Search |
| `ProcessEmailToDocument` | `EmailToDocumentJobHandler` | Convert emails to documents |

### Azure CLI Commands

```bash
# View Service Bus namespace
az servicebus namespace show \
  --name spaarke-servicebus-dev \
  --resource-group spe-infrastructure-westus2

# View queue
az servicebus queue show \
  --name sdap-jobs \
  --namespace-name spaarke-servicebus-dev \
  --resource-group spe-infrastructure-westus2

# Get connection string
az servicebus namespace authorization-rule keys list \
  --name RootManageSharedAccessKey \
  --namespace-name spaarke-servicebus-dev \
  --resource-group spe-infrastructure-westus2 \
  --query primaryConnectionString -o tsv
```

---

## RAG Pipeline Services (R1)

The RAG Pipeline (Phase 1 Complete) adds file indexing capabilities:

| Service | Purpose | Entry Points |
|---------|---------|--------------|
| `IFileIndexingService` | End-to-end file indexing | `IndexFileAsync`, `IndexFileAppOnlyAsync`, `IndexContentAsync` |
| `ITextChunkingService` | Text chunking with overlap | `ChunkTextAsync` |
| `IIdempotencyService` | Duplicate prevention | Locks, processed markers |
| `RagIndexingJobHandler` | Background job processing | `ProcessAsync` |

**Documentation**: See [RAG-ARCHITECTURE.md](../guides/RAG-ARCHITECTURE.md) for full pipeline details.

---

## Related Documents

- [auth-azure-resources.md](auth-azure-resources.md) - All Azure resource inventory
- [../guides/ai-document-summary.md](../guides/ai-document-summary.md) - AI feature documentation
- [../guides/RAG-ARCHITECTURE.md](../guides/RAG-ARCHITECTURE.md) - RAG architecture and file indexing pipeline
- [../guides/RAG-CONFIGURATION.md](../guides/RAG-CONFIGURATION.md) - RAG configuration reference
- [../../reference/adr/ADR-013-ai-architecture.md](../../reference/adr/ADR-013-ai-architecture.md) - AI architecture decisions

---

*Created: December 9, 2025*
*Updated: January 17, 2026 (RAG Pipeline R1, Service Bus)*
