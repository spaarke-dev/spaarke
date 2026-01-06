# Environment Variables Verification Report

> **Project**: AI Document Intelligence R1
> **Task**: 002 - Verify Environment Variables in Solution
> **Date**: 2025-12-28
> **Environment**: spaarkedev1.crm.dynamics.com

---

## Summary

| Metric | Value |
|--------|-------|
| **Variables Expected** | 12 |
| **Variables Found** | 15 (12 expected + 3 additional) |
| **Variables Missing** | 0 |
| **Current Values Set** | 0 (using defaults) |

---

## Verification Results

### Expected Variables (All Found)

| # | Schema Name | Display Name | Type | Default Value | Status |
|---|-------------|--------------|------|---------------|--------|
| 1 | sprk_BffApiBaseUrl | BFF API Base URL | String | `https://spe-api-dev-67e2xz.azurewebsites.net` | **EXISTS** |
| 2 | sprk_AzureOpenAiEndpoint | Azure OpenAI Endpoint | String | *(not set)* | **EXISTS** |
| 3 | sprk_AzureOpenAiKey | Azure OpenAI Key | Secret | *(masked)* | **EXISTS** |
| 4 | sprk_DocumentIntelligenceEndpoint | Document Intelligence Endpoint | String | *(not set)* | **EXISTS** |
| 5 | sprk_AzureAiSearchEndpoint | Azure AI Search Endpoint | String | *(not set)* | **EXISTS** |
| 6 | sprk_PromptFlowEndpoint | Prompt Flow Endpoint | String | *(not set)* | **EXISTS** |
| 7 | sprk_EnableAiFeatures | Enable AI Features | Boolean | `yes` | **EXISTS** |
| 8 | sprk_RedisConnectionString | Redis Connection String | Secret | *(masked)* | **EXISTS** |
| 9 | sprk_ApplicationInsightsKey | Application Insights Key | Secret | *(masked)* | **EXISTS** |
| 10 | sprk_KeyVaultUrl | Key Vault URL | String | *(not set)* | **EXISTS** |
| 11 | sprk_CustomerTenantId | Customer Tenant ID | String | *(not set)* | **EXISTS** |
| 12 | sprk_DeploymentEnvironment | Deployment Environment | String | `Development` | **EXISTS** |

### Additional Variables Found

| # | Schema Name | Display Name | Type | Default Value | Status |
|---|-------------|--------------|------|---------------|--------|
| 13 | sprk_EnableMultiDocumentAnalysis | Enable Multi-Document Analysis | Boolean | `no` | EXISTS |
| 14 | sprk_AzureAiSearchKey | Azure AI Search Key | Secret | *(masked)* | EXISTS |
| 15 | sprk_DocumentIntelligenceKey | Document Intelligence Key | Secret | *(masked)* | EXISTS |

---

## Current Values

**Note**: No `environmentvariablevalue` records were found. All variables are using their **default values**.

For secrets (type=Secret), values are managed through Azure Key Vault references or direct entry in the maker portal.

---

## Variable Details

### String Variables (Non-Secret)

| Variable | Default Value | Notes |
|----------|---------------|-------|
| sprk_BffApiBaseUrl | `https://spe-api-dev-67e2xz.azurewebsites.net` | Dev API endpoint |
| sprk_DeploymentEnvironment | `Development` | Current environment tier |
| sprk_AzureOpenAiEndpoint | *(empty)* | Needs value |
| sprk_DocumentIntelligenceEndpoint | *(empty)* | Needs value |
| sprk_AzureAiSearchEndpoint | *(empty)* | Needs value |
| sprk_PromptFlowEndpoint | *(empty)* | Needs value |
| sprk_KeyVaultUrl | *(empty)* | Needs value |
| sprk_CustomerTenantId | *(empty)* | Needs value |

### Boolean Variables

| Variable | Default Value | Notes |
|----------|---------------|-------|
| sprk_EnableAiFeatures | `yes` | AI features enabled |
| sprk_EnableMultiDocumentAnalysis | `no` | Multi-doc disabled by default |

### Secret Variables

| Variable | Notes |
|----------|-------|
| sprk_AzureOpenAiKey | Azure OpenAI API key |
| sprk_DocumentIntelligenceKey | Document Intelligence API key |
| sprk_AzureAiSearchKey | Azure AI Search API key |
| sprk_RedisConnectionString | Redis cache connection |
| sprk_ApplicationInsightsKey | App Insights instrumentation key |

---

## Recommendations

1. **Populate empty string values** - Several endpoints need values before API testing
2. **Configure secrets** - Set API keys via maker portal or Key Vault references
3. **Verify in appsettings** - Ensure BFF API reads these via Dataverse SDK

### Required Before API Testing (Task 004)

| Variable | Action Needed |
|----------|---------------|
| sprk_AzureOpenAiEndpoint | Set Azure OpenAI endpoint URL |
| sprk_KeyVaultUrl | Set Key Vault URL for secret resolution |
| sprk_AzureOpenAiKey | Configure secret value |

---

## Solution Membership

Environment variables are typically in:
- **Spaarke_DocumentIntelligence** solution
- **spaarke_core** solution

---

*Verification completed: 2025-12-28*
