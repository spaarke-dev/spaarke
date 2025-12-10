# AI Azure Resources

> **Last Updated**: December 9, 2025
> **Purpose**: Quick reference for AI-related Azure resource IDs and configuration.
> **Secrets**: Actual secrets stored in `config/ai-config.local.json` (gitignored)

---

## Azure OpenAI Service

| Property | Value |
|----------|-------|
| **Resource Name** | `spaarke-openai-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | East US |
| **SKU** | S0 (Standard) |
| **Endpoint** | `https://spaarke-openai-dev.openai.azure.com/` |
| **Resource ID** | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.CognitiveServices/accounts/spaarke-openai-dev` |

### Model Deployments

| Deployment Name | Model | Version | Capacity | Purpose |
|-----------------|-------|---------|----------|---------|
| `gpt-4o-mini` | gpt-4o-mini | 2024-07-18 | 10K TPM | Document summarization |

### Rate Limits

| Limit Type | Value |
|------------|-------|
| Requests per minute | 100 |
| Tokens per minute | 10,000 |

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

## Local Development

For local development, copy `config/ai-config.local.json.template` to `config/ai-config.local.json` and fill in the values.

The local config file is gitignored and contains:
- OpenAI endpoint
- OpenAI API key (for local testing only)

---

## Related Documents

- [auth-azure-resources.md](auth-azure-resources.md) - All Azure resource inventory
- [../guides/ai-document-summary.md](../guides/ai-document-summary.md) - AI feature documentation
- [../../reference/adr/ADR-013-ai-architecture.md](../../reference/adr/ADR-013-ai-architecture.md) - AI architecture decisions

---

*Created: December 9, 2025*
