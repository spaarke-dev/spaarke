# Environment Variables - AI Document Intelligence

> **Solution**: Spaarke_DocumentIntelligence
> **Version**: 1.0.0.0
> **Publisher**: Spaarke
> **Last Updated**: December 12, 2025

---

## Overview

This document defines the 15 Environment Variables required for multi-tenant deployment of the AI Document Intelligence feature. Environment Variables enable customers to deploy Spaarke in their own tenant without code changes.

**Key Benefits:**
- No code changes for different tenants
- Admin-configurable via Power Platform admin center
- Survives solution imports/updates
- Supports multiple environments (dev, test, prod)
- Can reference Azure Key Vault secrets for sensitive values

---

## Environment Variables Reference

### 1. BFF API Base URL

| Property | Value |
|----------|-------|
| **Display Name** | BFF API Base URL |
| **Schema Name** | `sprk_BffApiBaseUrl` |
| **Data Type** | Text |
| **Default Value** | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| **Required** | Yes |

**Purpose:** Base URL for the Spaarke BFF API. PCF controls and Canvas Apps call this endpoint to interact with Azure services (OpenAI, Document Intelligence, SharePoint Embedded).

**Customer Configuration:**
1. Deploy Azure resources via Bicep template
2. Note the App Service URL from deployment output
3. Update this variable to: `https://app-{customerName}-spaarke-bff-{env}.azurewebsites.net`

**Validation:** URL must be HTTPS. Test with `GET {url}/healthz` (should return "Healthy")

---

### 2. Azure OpenAI Endpoint

| Property | Value |
|----------|-------|
| **Display Name** | Azure OpenAI Endpoint |
| **Schema Name** | `sprk_AzureOpenAiEndpoint` |
| **Data Type** | Text |
| **Default Value** | *(empty)* |
| **Required** | Yes (for AI features) |

**Purpose:** Customer's Azure OpenAI resource endpoint for GPT model access.

**Customer Configuration:**
- Format: `https://{resource-name}.openai.azure.com/`
- Find in Azure Portal → Azure OpenAI resource → Keys and Endpoint

---

### 3. Azure OpenAI Key

| Property | Value |
|----------|-------|
| **Display Name** | Azure OpenAI Key |
| **Schema Name** | `sprk_AzureOpenAiKey` |
| **Data Type** | Secret |
| **Data Source** | Azure Key Vault |
| **Default Value** | *(empty)* |
| **Required** | Yes (for AI features) |

**Purpose:** API key for Azure OpenAI authentication.

**Customer Configuration:**
1. Store key in Azure Key Vault
2. Format: `/subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{vault}/secrets/{secret}`
3. Grant Power Platform access to Key Vault

**Security Note:** Never store API keys as plain text. Always use Key Vault reference.

---

### 4. Document Intelligence Endpoint

| Property | Value |
|----------|-------|
| **Display Name** | Document Intelligence Endpoint |
| **Schema Name** | `sprk_DocumentIntelligenceEndpoint` |
| **Data Type** | Text |
| **Default Value** | *(empty)* |
| **Required** | Yes (for document extraction) |

**Purpose:** Azure AI Document Intelligence endpoint for PDF/image text extraction.

**Customer Configuration:**
- Format: `https://{region}.api.cognitive.microsoft.com/`
- Find in Azure Portal → Document Intelligence resource → Keys and Endpoint

---

### 5. Document Intelligence Key

| Property | Value |
|----------|-------|
| **Display Name** | Document Intelligence Key |
| **Schema Name** | `sprk_DocumentIntelligenceKey` |
| **Data Type** | Secret |
| **Data Source** | Azure Key Vault |
| **Default Value** | *(empty)* |
| **Required** | Yes (for document extraction) |

**Purpose:** API key for Azure AI Document Intelligence authentication.

**Customer Configuration:** Store in Key Vault and reference as described in variable #3.

---

### 6. Azure AI Search Endpoint

| Property | Value |
|----------|-------|
| **Display Name** | Azure AI Search Endpoint |
| **Schema Name** | `sprk_AzureAiSearchEndpoint` |
| **Data Type** | Text |
| **Default Value** | *(empty)* |
| **Required** | Yes (for RAG/Knowledge features) |

**Purpose:** Azure AI Search endpoint for RAG knowledge retrieval.

**Customer Configuration:**
- Format: `https://{service-name}.search.windows.net`
- Find in Azure Portal → AI Search resource → Overview

---

### 7. Azure AI Search Key

| Property | Value |
|----------|-------|
| **Display Name** | Azure AI Search Key |
| **Schema Name** | `sprk_AzureAiSearchKey` |
| **Data Type** | Secret |
| **Data Source** | Azure Key Vault |
| **Default Value** | *(empty)* |
| **Required** | Yes (for RAG/Knowledge features) |

**Purpose:** Admin API key for Azure AI Search.

**Customer Configuration:** Store in Key Vault and reference as described in variable #3.

---

### 8. Prompt Flow Endpoint

| Property | Value |
|----------|-------|
| **Display Name** | Prompt Flow Endpoint |
| **Schema Name** | `sprk_PromptFlowEndpoint` |
| **Data Type** | Text |
| **Default Value** | *(empty)* |
| **Required** | No (Phase 2) |

**Purpose:** Azure AI Foundry Prompt Flow endpoint for orchestrated AI workflows.

**Customer Configuration:**
- Format: `https://{project}.{region}.inference.ml.azure.com/score`
- Deploy Prompt Flow in AI Foundry and copy the endpoint

---

### 9. Enable AI Features

| Property | Value |
|----------|-------|
| **Display Name** | Enable AI Features |
| **Schema Name** | `sprk_EnableAiFeatures` |
| **Data Type** | Yes/No |
| **Default Value** | Yes |
| **Required** | Yes |

**Purpose:** Master toggle to enable/disable all AI-powered features.

**Customer Configuration:**
- Set to **No** to disable AI features (e.g., for compliance reasons)
- When disabled, AI tabs/buttons are hidden in UI

---

### 10. Enable Multi-Document Analysis

| Property | Value |
|----------|-------|
| **Display Name** | Enable Multi-Document Analysis |
| **Schema Name** | `sprk_EnableMultiDocumentAnalysis` |
| **Data Type** | Yes/No |
| **Default Value** | No |
| **Required** | Yes |

**Purpose:** Feature flag for Phase 2 multi-document analysis capability.

**Customer Configuration:**
- Keep as **No** until Phase 2 features are deployed
- Set to **Yes** to enable analysis across multiple documents

---

### 11. Redis Connection String

| Property | Value |
|----------|-------|
| **Display Name** | Redis Connection String |
| **Schema Name** | `sprk_RedisConnectionString` |
| **Data Type** | Secret |
| **Data Source** | Azure Key Vault |
| **Default Value** | *(empty)* |
| **Required** | Yes (for caching) |

**Purpose:** Azure Cache for Redis connection string for session caching and rate limiting.

**Customer Configuration:**
- Format: `{cache-name}.redis.cache.windows.net:6380,password={key},ssl=True,abortConnect=False`
- Store in Key Vault

---

### 12. Application Insights Key

| Property | Value |
|----------|-------|
| **Display Name** | Application Insights Key |
| **Schema Name** | `sprk_ApplicationInsightsKey` |
| **Data Type** | Secret |
| **Data Source** | Azure Key Vault |
| **Default Value** | *(empty)* |
| **Required** | Yes (for telemetry) |

**Purpose:** Application Insights instrumentation key for logging and monitoring.

**Customer Configuration:**
- Find in Azure Portal → Application Insights → Overview → Instrumentation Key
- Store in Key Vault for consistency

---

### 13. Key Vault URL

| Property | Value |
|----------|-------|
| **Display Name** | Key Vault URL |
| **Schema Name** | `sprk_KeyVaultUrl` |
| **Data Type** | Text |
| **Default Value** | *(empty)* |
| **Required** | Yes |

**Purpose:** Azure Key Vault URL for secret resolution by BFF API.

**Customer Configuration:**
- Format: `https://{vault-name}.vault.azure.net/`
- BFF API uses this to retrieve secrets at runtime

---

### 14. Customer Tenant ID

| Property | Value |
|----------|-------|
| **Display Name** | Customer Tenant ID |
| **Schema Name** | `sprk_CustomerTenantId` |
| **Data Type** | Text |
| **Default Value** | *(empty)* |
| **Required** | No (for cross-tenant scenarios) |

**Purpose:** Customer's Azure AD tenant ID for cross-tenant authentication scenarios.

**Customer Configuration:**
- Format: GUID (e.g., `12345678-1234-1234-1234-123456789abc`)
- Required only for Model 3 (Customer-Owned) RAG deployment

---

### 15. Deployment Environment

| Property | Value |
|----------|-------|
| **Display Name** | Deployment Environment |
| **Schema Name** | `sprk_DeploymentEnvironment` |
| **Data Type** | Text |
| **Default Value** | Development |
| **Required** | Yes |

**Purpose:** Identifies the deployment environment for configuration and logging.

**Customer Configuration:**
- Valid values: `Development`, `Test`, `Staging`, `Production`
- Used for environment-specific behavior and log categorization

---

## Quick Reference Table

| # | Schema Name | Type | Default | Required |
|---|-------------|------|---------|----------|
| 1 | `sprk_BffApiBaseUrl` | Text | `https://spe-api-dev-67e2xz...` | Yes |
| 2 | `sprk_AzureOpenAiEndpoint` | Text | *(empty)* | Yes |
| 3 | `sprk_AzureOpenAiKey` | Secret | *(empty)* | Yes |
| 4 | `sprk_DocumentIntelligenceEndpoint` | Text | *(empty)* | Yes |
| 5 | `sprk_DocumentIntelligenceKey` | Secret | *(empty)* | Yes |
| 6 | `sprk_AzureAiSearchEndpoint` | Text | *(empty)* | Yes |
| 7 | `sprk_AzureAiSearchKey` | Secret | *(empty)* | Yes |
| 8 | `sprk_PromptFlowEndpoint` | Text | *(empty)* | No |
| 9 | `sprk_EnableAiFeatures` | Yes/No | Yes | Yes |
| 10 | `sprk_EnableMultiDocumentAnalysis` | Yes/No | No | Yes |
| 11 | `sprk_RedisConnectionString` | Secret | *(empty)* | Yes |
| 12 | `sprk_ApplicationInsightsKey` | Secret | *(empty)* | Yes |
| 13 | `sprk_KeyVaultUrl` | Text | *(empty)* | Yes |
| 14 | `sprk_CustomerTenantId` | Text | *(empty)* | No |
| 15 | `sprk_DeploymentEnvironment` | Text | Development | Yes |

---

## Current Development Environment Values

**Environment**: SPAARKE DEV 1
**Resource Group**: `spe-infrastructure-westus2`

| Variable | Dev Value | Source Azure Resource |
|----------|-----------|----------------------|
| `sprk_BffApiBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net` | App Service: spe-api-dev |
| `sprk_AzureOpenAiEndpoint` | `https://spaarke-openai-dev.openai.azure.com/` | Azure OpenAI: spaarke-openai-dev |
| `sprk_AzureOpenAiKey` | Key Vault reference (see below) | Azure OpenAI: spaarke-openai-dev |
| `sprk_DocumentIntelligenceEndpoint` | `https://westus2.api.cognitive.microsoft.com/` | Doc Intelligence: spaarke-docintel-dev |
| `sprk_DocumentIntelligenceKey` | Key Vault reference (see below) | Doc Intelligence: spaarke-docintel-dev |
| `sprk_AzureAiSearchEndpoint` | `https://spaarke-search-dev.search.windows.net/` | AI Search: spaarke-search-dev |
| `sprk_AzureAiSearchKey` | Key Vault reference (see below) | AI Search: spaarke-search-dev |
| `sprk_PromptFlowEndpoint` | `https://sprkspaarkedev-aif-proj.westus2.inference.ml.azure.com/score` | AI Foundry: sprkspaarkedev-aif-proj |
| `sprk_EnableAiFeatures` | `Yes` | N/A (configuration) |
| `sprk_EnableMultiDocumentAnalysis` | `No` | N/A (configuration) |
| `sprk_RedisConnectionString` | Key Vault reference (see below) | Redis Cache: (TBD) |
| `sprk_ApplicationInsightsKey` | Key Vault reference (see below) | App Insights: sprkspaarkedev-aif-insights |
| `sprk_KeyVaultUrl` | `https://sprkspaarkedev-aif-kv.vault.azure.net/` | Key Vault: sprkspaarkedev-aif-kv |
| `sprk_CustomerTenantId` | *(empty)* | N/A (Spaarke tenant) |
| `sprk_DeploymentEnvironment` | `Development` | N/A (configuration) |

### Key Vault Secret References (for Secret-type variables)

For secret-type variables, use Azure Key Vault references in this format:

| Variable | Key Vault Secret Name | Full Reference |
|----------|----------------------|----------------|
| `sprk_AzureOpenAiKey` | `openai-api-key` | `@Microsoft.KeyVault(SecretUri=https://sprkspaarkedev-aif-kv.vault.azure.net/secrets/openai-api-key/)` |
| `sprk_DocumentIntelligenceKey` | `docintel-api-key` | `@Microsoft.KeyVault(SecretUri=https://sprkspaarkedev-aif-kv.vault.azure.net/secrets/docintel-api-key/)` |
| `sprk_AzureAiSearchKey` | `search-admin-key` | `@Microsoft.KeyVault(SecretUri=https://sprkspaarkedev-aif-kv.vault.azure.net/secrets/search-admin-key/)` |
| `sprk_RedisConnectionString` | `redis-connection` | `@Microsoft.KeyVault(SecretUri=https://sprkspaarkedev-aif-kv.vault.azure.net/secrets/redis-connection/)` |
| `sprk_ApplicationInsightsKey` | `appinsights-key` | `@Microsoft.KeyVault(SecretUri=https://sprkspaarkedev-aif-kv.vault.azure.net/secrets/appinsights-key/)` |

---

## Maintenance Procedures

### How to Retrieve Current Values from Azure

**Prerequisites:** Azure CLI authenticated (`az login`)

```bash
# Get Azure OpenAI endpoint
az cognitiveservices account show \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --query "properties.endpoint" -o tsv

# Get Azure AI Search endpoint
az search service show \
  --name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  --query "hostName" -o tsv | xargs -I{} echo "https://{}"

# Get Application Insights instrumentation key
az monitor app-insights component show \
  --app sprkspaarkedev-aif-insights \
  --resource-group spe-infrastructure-westus2 \
  --query "instrumentationKey" -o tsv

# Get API keys (for storing in Key Vault)
az cognitiveservices account keys list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  --query "key1" -o tsv

az search admin-key show \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  --query "primaryKey" -o tsv
```

### How to Store Secrets in Key Vault

```bash
# Store OpenAI API key
az keyvault secret set \
  --vault-name sprkspaarkedev-aif-kv \
  --name openai-api-key \
  --value "$(az cognitiveservices account keys list --name spaarke-openai-dev --resource-group spe-infrastructure-westus2 --query key1 -o tsv)"

# Store AI Search admin key
az keyvault secret set \
  --vault-name sprkspaarkedev-aif-kv \
  --name search-admin-key \
  --value "$(az search admin-key show --service-name spaarke-search-dev --resource-group spe-infrastructure-westus2 --query primaryKey -o tsv)"
```

### How to Update Environment Variables in Dataverse

**Option 1: Power Apps Maker Portal (Manual)**
1. Navigate to https://make.powerapps.com
2. Select environment (SPAARKE DEV 1)
3. Go to Solutions → Spaarke_DocumentIntelligence
4. Click "Environment variables" in left nav
5. Click variable to edit → Set "Current value"

**Option 2: Dataverse Web API (Programmatic)**
```bash
# Requires Dataverse access token
TOKEN=$(az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv)

# Update environment variable value
curl -X PATCH "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/environmentvariablevalues(GUID)" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value": "new-value"}'
```

**Note:** Option 2 requires Azure AD authentication with Dataverse permissions. The Power Apps Maker Portal (Option 1) is the simplest approach for manual updates.

### When to Update

| Scenario | Variables to Update |
|----------|---------------------|
| New Azure resource deployed | Corresponding endpoint variable |
| API key rotated | Re-store in Key Vault (variable references don't change) |
| New environment created | All variables for that environment |
| AI Foundry project changed | `sprk_PromptFlowEndpoint` |
| Feature flag toggle | `sprk_EnableAiFeatures`, `sprk_EnableMultiDocumentAnalysis` |

---

## PCF Control Usage

```typescript
// Access environment variables in PCF controls
const context = Xrm.Utility.getGlobalContext();
const envVars = context.organizationSettings.environmentVariables;

// Get BFF API base URL
const apiBaseUrl = envVars["sprk_BffApiBaseUrl"] || "https://spe-api-dev-67e2xz.azurewebsites.net";

// Check if AI features are enabled
const aiEnabled = envVars["sprk_EnableAiFeatures"] === "true";
```

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "API unavailable" in PCF | Incorrect `sprk_BffApiBaseUrl` | Verify URL and test with `/healthz` |
| AI features not working | `sprk_EnableAiFeatures` = No | Set to Yes in environment variables |
| Key Vault errors | Missing permissions | Grant Power Platform access to Key Vault |
| Empty environment variable | Not configured | Set value in Power Platform admin center |

---

*Last updated: December 12, 2025*
