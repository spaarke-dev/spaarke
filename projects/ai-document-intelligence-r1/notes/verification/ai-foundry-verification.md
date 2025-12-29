# AI Foundry Infrastructure Verification Report

> **Project**: AI Document Intelligence R1
> **Task**: 003 - Verify AI Foundry Hub Connections
> **Date**: 2025-12-28
> **Subscription**: Spaarke SPE Subscription 1
> **Resource Group**: spe-infrastructure-westus2

---

## Summary

| Metric | Value |
|--------|-------|
| **Resources Expected** | 4 (Hub, Project, OpenAI, Doc Intelligence) |
| **Resources Found** | 8 (+ supporting infrastructure) |
| **Resources Missing** | 0 |
| **All Connections Working** | Yes |

---

## Verification Results

### AI Foundry Hub

| Property | Value |
|----------|-------|
| **Name** | sprkspaarkedev-aif-hub |
| **Location** | westus2 |
| **Status** | Deployed |

---

### AI Foundry Project

| Property | Value |
|----------|-------|
| **Name** | sprkspaarkedev-aif-proj |
| **Location** | westus2 |
| **Hub** | sprkspaarkedev-aif-hub |
| **Status** | Deployed |

**Configured Connections:**

| Connection Name | Type | Target |
|-----------------|------|--------|
| azure-openai-connection | Azure OpenAI | https://spaarke-openai-dev.openai.azure.com/ |
| ai-search-connection | Azure AI Search | https://spaarke-search-dev.search.windows.net/ |

---

### Azure OpenAI

| Property | Value |
|----------|-------|
| **Name** | spaarke-openai-dev |
| **Kind** | OpenAI |
| **SKU** | S0 |
| **Endpoint** | https://spaarke-openai-dev.openai.azure.com/ |
| **Provisioning State** | Succeeded |

**Deployed Models:**

| Deployment Name | Model | Version | Capacity |
|-----------------|-------|---------|----------|
| gpt-4o-mini | gpt-4o-mini | 2024-07-18 | 10 |
| text-embedding-3-small | text-embedding-3-small | 1 | 120 |

---

### Document Intelligence

| Property | Value |
|----------|-------|
| **Name** | spaarke-docintel-dev |
| **Kind** | FormRecognizer |
| **Endpoint** | https://westus2.api.cognitive.microsoft.com/ |
| **Provisioning State** | Succeeded |

---

## Supporting Infrastructure

| Resource | Name | Type |
|----------|------|------|
| Key Vault | sprkspaarkedev-aif-kv | Microsoft.KeyVault/vaults |
| Storage Account | sprkspaarkedevaifsa | Microsoft.Storage/storageAccounts |
| Log Analytics | sprkspaarkedev-aif-logs | Microsoft.OperationalInsights/workspaces |
| Application Insights | sprkspaarkedev-aif-insights | Microsoft.Insights/components |

---

## Endpoint Summary for BFF API Configuration

| Service | Endpoint URL |
|---------|--------------|
| Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| Document Intelligence | `https://westus2.api.cognitive.microsoft.com/` |
| AI Search | `https://spaarke-search-dev.search.windows.net/` |

---

## Recommendations

1. **Configure Dataverse environment variables** with the endpoint URLs above
2. **Verify API keys** are accessible via Key Vault or environment variables
3. **Test BFF API connectivity** to Azure OpenAI (Task 004)

### Environment Variable Mapping

| Dataverse Variable | Azure Resource |
|--------------------|----------------|
| sprk_AzureOpenAiEndpoint | https://spaarke-openai-dev.openai.azure.com/ |
| sprk_DocumentIntelligenceEndpoint | https://westus2.api.cognitive.microsoft.com/ |
| sprk_AzureAiSearchEndpoint | https://spaarke-search-dev.search.windows.net/ |

---

## Notes

- All AI infrastructure is deployed and operational
- AI Foundry project has pre-configured connections to Azure OpenAI and AI Search
- Models deployed: gpt-4o-mini (for chat) and text-embedding-3-small (for embeddings)
- No Prompt Flow deployments found (expected for R1 - Prompt Flow is R3 scope)

---

*Verification completed: 2025-12-28*
