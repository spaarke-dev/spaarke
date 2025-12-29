# AI Azure Resources

> **Last Updated**: December 28, 2025
> **Purpose**: Quick reference for AI-related Azure resource IDs and configuration.
> **Secrets**: Actual secrets stored in `config/ai-config.local.json` (gitignored)
> **Verified**: Task 003 - AI Document Intelligence R1 (2025-12-28)

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
| `text-embedding-3-small` | text-embedding-3-small | 1 | 120 TPM | Vector embeddings for RAG |

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
| **Endpoint** | `https://spaarke-search-dev.search.windows.net/` |

### Indexes

| Index Name | Purpose | Status |
|------------|---------|--------|
| `spaarke-records-index` | Document search | Active |

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

## Related Documents

- [auth-azure-resources.md](auth-azure-resources.md) - All Azure resource inventory
- [../guides/ai-document-summary.md](../guides/ai-document-summary.md) - AI feature documentation
- [../../reference/adr/ADR-013-ai-architecture.md](../../reference/adr/ADR-013-ai-architecture.md) - AI architecture decisions

---

*Created: December 9, 2025*
