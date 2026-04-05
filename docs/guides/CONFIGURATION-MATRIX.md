# Configuration Matrix Guide

> **Last Updated**: April 5, 2026
> **Purpose**: Complete reference of all BFF API configuration settings -- where they live, what values they accept, and their defaults
> **Audience**: Developer, Admin, Operator

---

## Prerequisites

- [ ] Access to `src/server/api/Sprk.Bff.Api/` source code
- [ ] Access to Azure Key Vault for secret values
- [ ] Access to Azure App Service for environment-specific overrides

## Quick Reference

| Item | Value |
|------|-------|
| Template file | `src/server/api/Sprk.Bff.Api/appsettings.template.json` |
| Options classes | `src/server/api/Sprk.Bff.Api/Configuration/*.cs` and `Options/*.cs` |
| Dev App Service | `spe-api-dev-67e2xz` |
| Dev Key Vault | `sprkspaarkedev-aif-kv` |
| Override hierarchy | appsettings.json < appsettings.{Environment}.json < Environment Variables < Key Vault |

---

## How Configuration Works

.NET configuration binds settings from multiple sources in priority order (last wins):

1. **`appsettings.json`** -- Base defaults (checked into repo as template)
2. **`appsettings.{Environment}.json`** -- Environment overrides (gitignored for local dev)
3. **Environment Variables** -- Azure App Service App Settings (set via `az webapp config appsettings set`)
4. **Key Vault References** -- Secrets resolved at runtime via `@Microsoft.KeyVault(SecretUri=...)` syntax
5. **Dataverse Environment Variables** -- Some AI features read config from Dataverse `sprk_*` entities

**Nested keys in environment variables** use double underscore: `DocumentIntelligence__Enabled=true`

---

## Configuration Matrix

### Core Platform

| Setting | Section | Default | Location | Description |
|---------|---------|---------|----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | (root) | `Development` | Env Var | Runtime environment; `Production` requires CORS |
| `AllowedHosts` | (root) | `*` | appsettings | Host filtering |
| `TENANT_ID` | (root) | -- | Env Var / appsettings | Azure AD Tenant ID |
| `API_APP_ID` | (root) | -- | Env Var / appsettings | BFF API app registration client ID |
| `DEFAULT_CT_ID` | (root) | -- | Env Var / appsettings | Default SPE container type ID |

### Azure AD / Authentication (`AzureAd`)

| Setting | Default | Location | Description |
|---------|---------|----------|-------------|
| `AzureAd:Instance` | `https://login.microsoftonline.com/` | appsettings | Azure AD authority |
| `AzureAd:TenantId` | `common` | appsettings | Tenant ID (use `common` for multi-tenant) |
| `AzureAd:ClientId` | -- | Env Var | BFF API app registration client ID |
| `AzureAd:Audience` | `api://{ClientId}` | appsettings | Token audience |

### CORS (`Cors`)

| Setting | Default | Location | Description |
|---------|---------|----------|-------------|
| `Cors:AllowedOrigins:0` | -- | Env Var | Primary Dataverse origin (e.g., `https://spaarkedev1.crm.dynamics.com`) |
| `Cors:AllowedOrigins:1` | -- | Env Var | API subdomain (e.g., `https://spaarkedev1.api.crm.dynamics.com`) |
| `Cors:AllowedOrigins:2` | `https://copilot.microsoft.com` | appsettings | Copilot origin |
| `Cors:AllowedOrigins:3` | `https://teams.microsoft.com` | appsettings | Teams origin |

### Microsoft Graph (`Graph`)

Options class: `GraphOptions` (`Configuration/GraphOptions.cs`)

| Setting | Default | Location | Required | Description |
|---------|---------|----------|----------|-------------|
| `Graph:TenantId` | -- | appsettings / Env Var | Yes | Azure AD Tenant ID |
| `Graph:ClientId` | -- | appsettings / Env Var | Yes | App registration client ID |
| `Graph:ClientSecret` | -- | Key Vault | When MI disabled | Client secret for app-only auth |
| `Graph:Scopes` | `["https://graph.microsoft.com/.default"]` | appsettings | Yes | Graph API scopes |
| `Graph:ManagedIdentity:Enabled` | `false` | Env Var | No | Use User-Assigned Managed Identity |
| `Graph:ManagedIdentity:ClientId` | -- | Env Var | When MI enabled | UAMI Client ID |

### Graph Resilience (`GraphResilience`)

Options class: `GraphResilienceOptions` (`Configuration/GraphResilienceOptions.cs`)

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| `GraphResilience:RetryCount` | `3` | 1-10 | Retry attempts for 429/503/504 |
| `GraphResilience:RetryBackoffSeconds` | `2` | 1-30 | Base exponential backoff (2^attempt * N) |
| `GraphResilience:CircuitBreakerFailureThreshold` | `5` | 3-20 | Consecutive failures before circuit opens |
| `GraphResilience:CircuitBreakerBreakDurationSeconds` | `30` | 10-300 | Open-state duration before half-open |
| `GraphResilience:TimeoutSeconds` | `30` | 5-300 | Per-request timeout |
| `GraphResilience:HonorRetryAfterHeader` | `true` | bool | Honor Graph 429 Retry-After header |

### Redis Cache (`Redis`)

Options class: `RedisOptions` (`Configuration/RedisOptions.cs`)

| Setting | Default | Location | Description |
|---------|---------|----------|-------------|
| `Redis:Enabled` | `false` | Env Var / appsettings | Enable Redis (false = in-memory fallback) |
| `Redis:ConnectionString` | -- | Key Vault | Redis connection string |
| `Redis:InstanceName` | `sdap:` | appsettings | Cache key prefix |
| `Redis:DefaultExpirationMinutes` | `60` | appsettings | Sliding expiration |
| `Redis:AbsoluteExpirationMinutes` | `1440` | appsettings | Absolute expiration (24h) |

### Service Bus (`ServiceBus`)

Options class: `ServiceBusOptions` (`Configuration/ServiceBusOptions.cs`)

| Setting | Default | Range | Location | Description |
|---------|---------|-------|----------|-------------|
| `ServiceBus:ConnectionString` | -- | -- | Key Vault | Service Bus connection string |
| `ServiceBus:QueueName` | `sdap-jobs` | -- | appsettings | Main job queue name |
| `ServiceBus:CommunicationQueueName` | `sdap-communication` | -- | appsettings | Dedicated email processing queue |
| `ServiceBus:MaxConcurrentCalls` | `5` | 1-100 | appsettings | Max concurrent message handlers |
| `ServiceBus:MaxAutoLockRenewalDuration` | `00:05:00` | -- | appsettings | Lock renewal duration |

### Dataverse (`Dataverse`)

Options class: `DataverseOptions` (`Configuration/DataverseOptions.cs`)

| Setting | Default | Location | Required | Description |
|---------|---------|----------|----------|-------------|
| `Dataverse:EnvironmentUrl` | -- | Key Vault / Env Var | Yes | Dataverse URL (e.g., `https://spaarkedev1.crm.dynamics.com`) |
| `Dataverse:ClientId` | -- | Env Var | Yes | App registration client ID |
| `Dataverse:ClientSecret` | -- | Key Vault | Yes | Client secret |
| `Dataverse:TenantId` | -- | Env Var | Yes | Azure AD Tenant ID |

### Document Intelligence / AI (`DocumentIntelligence`)

Options class: `DocumentIntelligenceOptions` (`Configuration/DocumentIntelligenceOptions.cs`)

| Setting | Default | Range | Location | Description |
|---------|---------|-------|----------|-------------|
| `DocumentIntelligence:Enabled` | `true` | bool | Env Var | Master switch for AI summarization |
| `DocumentIntelligence:StreamingEnabled` | `true` | bool | appsettings | Enable SSE streaming responses |
| `DocumentIntelligence:OpenAiEndpoint` | -- | URL | Key Vault | Azure OpenAI endpoint |
| `DocumentIntelligence:OpenAiKey` | -- | -- | Key Vault | Azure OpenAI API key |
| `DocumentIntelligence:SummarizeModel` | `gpt-4o-mini` | string | Env Var | Model deployment name |
| `DocumentIntelligence:EmbeddingModel` | `text-embedding-3-large` | string | Env Var | Embedding model name |
| `DocumentIntelligence:EmbeddingDimensions` | `3072` | int | appsettings | Vector dimensions |
| `DocumentIntelligence:MaxOutputTokens` | `500` | 100-4000 | appsettings | Max summary tokens |
| `DocumentIntelligence:Temperature` | `0.3` | 0.0-1.0 | appsettings | Generation temperature |
| `DocumentIntelligence:DocIntelEndpoint` | -- | URL | Key Vault | Document Intelligence endpoint |
| `DocumentIntelligence:DocIntelKey` | -- | -- | Key Vault | Document Intelligence API key |
| `DocumentIntelligence:DocIntelTimeoutSeconds` | `30` | 5-300 | appsettings | Doc Intel request timeout |
| `DocumentIntelligence:DocIntelCircuitBreakerThreshold` | `3` | 1-20 | appsettings | CB failure threshold |
| `DocumentIntelligence:DocIntelCircuitBreakerBreakSeconds` | `60` | 10-600 | appsettings | CB open duration |
| `DocumentIntelligence:MaxFileSizeBytes` | `10485760` | int | appsettings | Max file size (10MB) |
| `DocumentIntelligence:MaxInputTokens` | `100000` | int | appsettings | Max tokens sent to model |
| `DocumentIntelligence:MaxConcurrentStreams` | `3` | 1-10 | appsettings | Max SSE streams per user |
| `DocumentIntelligence:RecordMatchingEnabled` | `false` | bool | Env Var | Enable record matching |
| `DocumentIntelligence:AiSearchEndpoint` | -- | URL | Key Vault | AI Search endpoint |
| `DocumentIntelligence:AiSearchKey` | -- | -- | Key Vault | AI Search API key |
| `DocumentIntelligence:AiSearchIndexName` | `spaarke-records-index` | string | appsettings | Search index name |
| `DocumentIntelligence:StructuredOutputEnabled` | `true` | bool | appsettings | Enable JSON structured output |

### Analysis (`Analysis`)

Options class: `AnalysisOptions` (`Configuration/AnalysisOptions.cs`)

| Setting | Default | Range | Location | Description |
|---------|---------|-------|----------|-------------|
| `Analysis:Enabled` | `true` | bool | Env Var / Dataverse | Master switch (maps to `sprk_EnableAiFeatures`) |
| `Analysis:MultiDocumentEnabled` | `false` | bool | Env Var / Dataverse | Multi-document analysis |
| `Analysis:PromptFlowEndpoint` | -- | URL | Key Vault | AI Foundry Prompt Flow endpoint |
| `Analysis:PromptFlowKey` | -- | -- | Key Vault | Prompt Flow API key |
| `Analysis:ExecuteFlowName` | `analysis-execute` | string | appsettings | Execute flow deployment name |
| `Analysis:ContinueFlowName` | `analysis-continue` | string | appsettings | Continue flow deployment name |
| `Analysis:DefaultRagModel` | `Shared` | Shared/Dedicated/CustomerOwned | appsettings | Default RAG deployment model |
| `Analysis:SharedIndexName` | `spaarke-knowledge-index-v2` | string | appsettings | Shared RAG index name |
| `Analysis:TenantFilterField` | `customerId` | string | appsettings | Tenant isolation field |
| `Analysis:MaxKnowledgeResults` | `5` | 1-20 | appsettings | Knowledge results per query |
| `Analysis:MinRelevanceScore` | `0.7` | 0.0-1.0 | appsettings | Min relevance threshold |
| `Analysis:MaxWorkingVersions` | `10` | 1-50 | appsettings | Working version limit |
| `Analysis:SessionTimeoutMinutes` | `60` | 5-1440 | appsettings | Session timeout |
| `Analysis:MaxChatHistoryMessages` | `20` | 1-50 | appsettings | Chat history depth |
| `Analysis:MaxChatInputTokens` | `50000` | int | appsettings | Max input tokens for chat |
| `Analysis:MaxDocumentContextLength` | `100000` | 1000-200000 | appsettings | Max doc text in prompt |
| `Analysis:EnableDocxExport` | `true` | bool | appsettings | DOCX export enabled |
| `Analysis:EnablePdfExport` | `true` | bool | appsettings | PDF export enabled |
| `Analysis:EnableEmailExport` | `true` | bool | appsettings | Email export enabled |
| `Analysis:EnableTeamsExport` | `false` | bool | appsettings | Teams export enabled |
| `Analysis:MaxConcurrentStreams` | `3` | 1-10 | appsettings | Max concurrent streams |
| `Analysis:StreamChunkDelayMs` | `10` | 0-100 | appsettings | SSE chunk delay |
| `Analysis:DeploymentEnvironment` | `Development` | string | Env Var / Dataverse | Environment name |
| `Analysis:CustomerTenantId` | -- | string | Env Var / Dataverse | Customer tenant ID |
| `Analysis:KeyVaultUrl` | -- | URL | Env Var / Dataverse | Key Vault URL |

### AI Search (`AiSearch`)

Options class: `AiSearchOptions` (`Options/AiSearchOptions.cs`)

| Setting | Default | Location | Description |
|---------|---------|----------|-------------|
| `AiSearch:Endpoint` | -- | Env Var / Key Vault | AI Search endpoint |
| `AiSearch:ApiKeySecretName` | `AzureAISearchApiKey` | appsettings | Key Vault secret name for API key |
| `AiSearch:KnowledgeIndexName` | `spaarke-knowledge-index-v2` | appsettings | Knowledge base index |
| `AiSearch:DiscoveryIndexName` | `discovery-index` | appsettings | Discovery search index |
| `AiSearch:RagReferencesIndexName` | `spaarke-rag-references` | appsettings | RAG references index |
| `AiSearch:SemanticConfigName` | `semantic-config` | appsettings | Semantic search config |

### Tool Framework (`ToolFramework`)

Options class: `ToolFrameworkOptions` (`Configuration/ToolFrameworkOptions.cs`)

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| `ToolFramework:Enabled` | `true` | bool | Master switch for tool framework |
| `ToolFramework:DisabledHandlers` | `[]` | string[] | Handler IDs to disable |
| `ToolFramework:DefaultExecutionTimeoutSeconds` | `60` | int | Default tool timeout |
| `ToolFramework:MaxParallelToolExecutions` | `3` | int | Max parallel tools |
| `ToolFramework:VerboseLogging` | `false` | bool | Debug logging (false in production) |

### Email Processing (`Email` / `EmailProcessing`)

Options class: `EmailProcessingOptions` (`Configuration/EmailProcessingOptions.cs`)

| Setting | Default | Location | Description |
|---------|---------|----------|-------------|
| `Email:Enabled` | `true` | Env Var | Master switch |
| `Email:DefaultContainerId` | -- | Env Var | SPE container ID for email docs |
| `Email:ProcessInbound` | `true` | appsettings | Process received emails |
| `Email:ProcessOutbound` | `true` | appsettings | Process sent emails |
| `Email:MaxAttachmentSizeMB` | `25` | appsettings | Max per-attachment size |
| `Email:MaxTotalSizeMB` | `100` | appsettings | Max total email size |
| `Email:FilterRuleCacheTtlMinutes` | `5` | appsettings | Filter rule cache TTL |
| `Email:DefaultAction` | `Ignore` | appsettings | Action when no filter matches (AutoSave/Ignore/ReviewRequired) |
| `Email:EnableWebhook` | `true` | appsettings | Webhook-based processing |
| `Email:EnablePolling` | `true` | appsettings | Polling-based backup |
| `Email:PollingIntervalMinutes` | `5` | appsettings | Polling interval |
| `Email:PollingLookbackHours` | `24` | appsettings | Lookback window |
| `Email:PollingBatchSize` | `100` | appsettings | Max emails per poll |
| `Email:MinImageSizeKB` | `5` | appsettings | Min image size (skip signature images) |
| `Email:MaxEmlFileNameLength` | `100` | appsettings | Max .eml filename length |
| `Email:AutoEnqueueAi` | `true` | appsettings | Auto-queue for AI processing |
| `Email:AutoIndexToRag` | `false` | appsettings | Auto-index to RAG |
| `Email:WebhookSecret` | -- | Key Vault | Dataverse webhook secret |

### Communication (`Communication`)

Options class: `CommunicationOptions` (`Configuration/CommunicationOptions.cs`)

| Setting | Default | Location | Required | Description |
|---------|---------|----------|----------|-------------|
| `Communication:DefaultMailbox` | -- | Env Var | No | Default sender mailbox |
| `Communication:ArchiveContainerId` | -- | Env Var | No | SPE container for .eml archives |
| `Communication:WebhookNotificationUrl` | -- | Env Var | Yes | Public HTTPS callback URL for Graph webhooks |
| `Communication:WebhookClientState` | -- | Key Vault | Yes | Webhook validation secret |
| `Communication:ApprovedSenders` | `[]` | appsettings | Yes (min 1) | Approved sender configurations |

### Agent Token (`AgentToken`)

Options class: `AgentTokenOptions` (`Configuration/AgentTokenOptions.cs`)

| Setting | Default | Location | Required | Description |
|---------|---------|----------|----------|-------------|
| `AgentToken:TenantId` | -- | Env Var | Yes | Azure AD Tenant ID |
| `AgentToken:ClientId` | -- | Env Var | Yes | BFF API client ID |
| `AgentToken:ClientSecret` | -- | Key Vault | Yes | BFF API client secret |
| `AgentToken:AgentAppId` | -- | Env Var | Yes | Copilot agent app ID |
| `AgentToken:CopilotAudience` | -- | Env Var | No | Expected token audience |
| `AgentToken:GraphScopes` | `["https://graph.microsoft.com/.default"]` | appsettings | Yes | Graph scopes for OBO |
| `AgentToken:DataverseEnvironmentUrl` | -- | Env Var | Yes | Dataverse URL (no trailing slash) |
| `AgentToken:CacheTtlMinutes` | `55` | appsettings | No | Token cache TTL (1-59) |

### LlamaParse (`LlamaParse`)

Options class: `LlamaParseOptions` (`Options/LlamaParseOptions.cs`)

| Setting | Default | Description |
|---------|---------|-------------|
| `LlamaParse:ApiKeySecretName` | `LlamaParseApiKey` | Key Vault secret name |
| `LlamaParse:BaseUrl` | `https://api.cloud.llamaindex.ai` | API base URL |
| `LlamaParse:ParseTimeoutSeconds` | `120` | Parse operation timeout |
| `LlamaParse:MaxPages` | `500` | Max pages to parse |
| `LlamaParse:Enabled` | `false` | Feature flag |

### Miscellaneous Sections

| Section | Key Settings | Location |
|---------|-------------|----------|
| `Logging:LogLevel:Default` | `Information` | appsettings |
| `ApplicationInsights:ConnectionString` | Key Vault reference | Key Vault |
| `PowerPages:BaseUrl` | Environment-specific URL | Env Var |
| `BingSearch:ApiKey` | Key Vault reference | Key Vault |
| `BingSearch:Endpoint` | `https://api.bing.microsoft.com/v7.0/search` | appsettings |
| `BingSearch:MaxResults` | `10` | appsettings |
| `AzureOpenAI:Endpoint` | Key Vault reference | Key Vault |
| `AzureOpenAI:ChatModelName` | Environment-specific | Env Var |
| `AzureOpenAI:EmbeddingModelName` | `text-embedding-3-small` | appsettings |
| `EmbeddingMigration:Enabled` | `false` (deprecated) | appsettings |
| `ScheduledRagIndexing:Enabled` | `false` | appsettings |
| `ScheduledRagIndexing:IntervalMinutes` | `60` | appsettings |
| `CopilotAgent:EnableDocumentSearch` | `true` | appsettings |
| `CopilotAgent:EnablePlaybookInvocation` | `true` | appsettings |
| `DemoProvisioning:DefaultEnvironment` | `Dev` | appsettings |

---

## Key Vault Secrets Reference

Secrets stored in Azure Key Vault and referenced via `@Microsoft.KeyVault(SecretUri=...)`:

| Secret Name | Used By | Description |
|-------------|---------|-------------|
| `ServiceBus-ConnectionString` | `ConnectionStrings:ServiceBus`, `ServiceBus:ConnectionString` | Azure Service Bus |
| `Redis-ConnectionString` | `ConnectionStrings:Redis` | Redis cache |
| `BFF-API-ClientSecret` | `Dataverse:ClientSecret`, `AgentToken:ClientSecret` | BFF API app secret |
| `Dataverse-ServiceUrl` | `Dataverse:ServiceUrl` | Dataverse environment URL |
| `ai-openai-endpoint` | `DocumentIntelligence:OpenAiEndpoint` | Azure OpenAI endpoint |
| `ai-openai-key` | `DocumentIntelligence:OpenAiKey` | Azure OpenAI API key |
| `ai-docintel-endpoint` | `DocumentIntelligence:DocIntelEndpoint` | Document Intelligence endpoint |
| `ai-docintel-key` | `DocumentIntelligence:DocIntelKey` | Document Intelligence key |
| `ai-search-endpoint` | `DocumentIntelligence:AiSearchEndpoint` | AI Search endpoint |
| `ai-search-key` | `DocumentIntelligence:AiSearchKey` | AI Search key |
| `AzureAISearchApiKey` | `AiSearch` (resolved by secret name) | AI Search API key |
| `LlamaParseApiKey` | `LlamaParse` (resolved by secret name) | LlamaParse API key |
| `PromptFlow-Endpoint` | `Analysis:PromptFlowEndpoint` | Prompt Flow scoring endpoint |
| `PromptFlow-Key` | `Analysis:PromptFlowKey` | Prompt Flow API key |
| `AppInsights-ConnectionString` | `ApplicationInsights:ConnectionString` | Application Insights |
| `Email-WebhookSecret` | `Email:WebhookSecret` | Email webhook validation |
| `communication-webhook-secret` | `Communication:WebhookClientState` | Graph webhook validation |
| `BingSearch-ApiKey` | `BingSearch:ApiKey` | Bing Search key |

---

## Verification

After changing configuration:

1. **Local**: Restart the API and check `/healthz` returns `Healthy`
2. **Azure**: After `az webapp config appsettings set`, verify with `curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz`
3. **Startup validation**: Options with `[Required]` annotations and `ValidateOnStart()` will fail fast if missing

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| App fails to start with `OptionsValidationException` | Required config missing | Check the error message for the exact setting name; add it to App Settings or Key Vault |
| `503 Service Unavailable` on AI endpoints | `DocumentIntelligence:Enabled` or `Analysis:Enabled` is `false` | Set to `true` in App Settings |
| CORS errors in browser console | Missing allowed origin | Add origin to `Cors:AllowedOrigins:N` App Setting |
| Key Vault reference shows literal `@Microsoft.KeyVault(...)` | App Service Key Vault integration not configured | Enable managed identity and Key Vault reference resolution on the App Service |
| Graph calls fail with 401 | `Graph:ClientSecret` expired or `ManagedIdentity:Enabled` mismatch | Rotate secret in Key Vault or verify MI config |
| Redis timeout errors | `Redis:ConnectionString` invalid or Redis unreachable | Verify connection string; set `Redis:Enabled=false` to fall back to in-memory |

---

## Related

- [ENVIRONMENT-DEPLOYMENT-GUIDE.md](ENVIRONMENT-DEPLOYMENT-GUIDE.md) -- Full deployment procedures
- [azure-deploy skill](../../.claude/skills/azure-deploy/SKILL.md) -- Azure deployment operations
- [azure-deployment constraints](../../.claude/constraints/azure-deployment.md) -- Required App Settings reference

---

*Last updated: April 5, 2026*
