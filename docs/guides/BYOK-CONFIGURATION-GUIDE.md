# BYOK Configuration Guide — AI Platform Unification

> **Last Updated**: 2026-05-16
> **Applies To**: Spaarke AI Platform Unification R1 (all deployment models)
> **Source Task**: AIPU-086 — BYOK configuration verification
>
> This guide documents every environment variable and configuration key required by the
> Spaarke BFF API's AI Platform features, with per-deployment-model requirements and
> guidance for BYOK (Bring Your Own Key / customer-provisioned Azure AI Foundry) deployments.

---

## Deployment Model Matrix

The Spaarke AI Platform supports three deployment models that differ in where Azure
AI resources live and who manages the encryption keys.

| Aspect | Model 1: Multi-Customer (Shared) | Model 2: Dedicated | Model 3: BYOK (Customer Tenant) |
|--------|----------------------------------|--------------------|---------------------------------|
| **Azure Subscription** | Spaarke's subscription | Spaarke's subscription | Customer's subscription |
| **Azure AI Foundry** | Spaarke-shared project | Spaarke-dedicated project | Customer-provisioned project |
| **Azure OpenAI** | Spaarke-managed | Spaarke-managed | Customer-managed |
| **Encryption Keys** | Spaarke-managed (MSFT CMK) | Spaarke-managed (MSFT CMK) | Customer-managed keys (true BYOK) |
| **AI Search Index** | Shared (`spaarke-knowledge-index-v2`) | Dedicated per-customer | Customer-provisioned |
| **Bing Connection** | Spaarke-registered | Spaarke-registered | Customer-registered in their project |
| **BFF Foundry Endpoint** | `AgentService:Endpoint` → Spaarke project | `AgentService:Endpoint` → dedicated project | `AgentService:Endpoint` → customer project |
| **Agent ID** | `AgentService:AgentId` → Spaarke-deployed | `AgentService:AgentId` → dedicated deploy | `AgentService:AgentId` → customer-deployed |
| **AnalysisOptions:DefaultRagModel** | `Shared` | `Dedicated` | `CustomerOwned` |
| **AnalysisOptions:CustomerTenantId** | Not required | Not required | **Required** — customer's Azure AD tenant |

**Configuration switching**: Moving between models requires only environment variable changes.
No code changes are required (enforced by the audit in AIPU-086).

---

## Section 1: Agent Service Options (`AgentService`)

**Options class**: `Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceOptions`
**Config section**: `AgentService`
**DI validation**: `ValidateDataAnnotations()` — deferred (no `ValidateOnStart`).
**Rationale**: `[Required]` fields `Endpoint` and `AgentId` are only needed when `Enabled = true`.
The app starts cleanly with `Enabled = false` and no Foundry config. `GuardEnabled()` enforces
the kill switch at call time (ADR-018).

| Config Key | Env Var (App Service) | Type | Required | Default | Purpose |
|------------|-----------------------|------|----------|---------|---------|
| `AgentService:Enabled` | `AgentService__Enabled` | `bool` | Yes | `false` | Kill switch (ADR-018). Must be `true` to enable Foundry Agent operations. |
| `AgentService:Endpoint` | `AgentService__Endpoint` | `Uri` | When Enabled | — | Azure AI Foundry project endpoint URI. Format: `https://<hub>.services.ai.azure.com/api/projects/<project>` |
| `AgentService:AgentId` | `AgentService__AgentId` | `string` | When Enabled | — | Pre-provisioned AI Foundry Agent ID (retrieved after `az ai agent create`). |
| `AgentService:MaxConcurrency` | `AgentService__MaxConcurrency` | `int` [1–64] | No | `4` | Max concurrent Foundry operations per BFF instance (ADR-016). |
| `AgentService:ThreadCacheExpiryMinutes` | `AgentService__ThreadCacheExpiryMinutes` | `int` [1–1440] | No | `60` | Redis sliding expiry for thread ID cache (ADR-009). |

### Example values by deployment model

| Key | Multi-Customer (Dev) | Dedicated | BYOK (Customer) |
|-----|----------------------|-----------|-----------------|
| `AgentService:Enabled` | `true` | `true` | `true` |
| `AgentService:Endpoint` | `https://sprkspaarkedev-aif-hub.services.ai.azure.com/api/projects/sprkspaarkedev-aif-proj` | `https://<dedicated-hub>.services.ai.azure.com/api/projects/<dedicated-proj>` | `https://<customer-hub>.services.ai.azure.com/api/projects/<customer-proj>` |
| `AgentService:AgentId` | *(deployed agent ID)* | *(dedicated agent ID)* | *(customer-deployed agent ID)* |

---

## Section 2: Code Interpreter Options (`CodeInterpreter`)

**Options class**: `Sprk.Bff.Api.Services.Ai.Foundry.CodeInterpreterOptions`
**Config section**: `CodeInterpreter`
**DI validation**: `ValidateDataAnnotations()` — deferred (no `ValidateOnStart`).
**Rationale**: Code Interpreter has no external resource IDs of its own — it runs via the
same `AgentServiceClient` Foundry connection. `Enabled = false` causes tools to return a
graceful unavailability string rather than throwing (ADR-018).

| Config Key | Env Var (App Service) | Type | Required | Default | Purpose |
|------------|-----------------------|------|----------|---------|---------|
| `CodeInterpreter:Enabled` | `CodeInterpreter__Enabled` | `bool` | Yes | `false` | Kill switch. When false, `CodeInterpreterTools` returns a user-readable message. |
| `CodeInterpreter:MaxConcurrency` | `CodeInterpreter__MaxConcurrency` | `int` [1–32] | No | `2` | Max concurrent sandbox invocations per BFF instance (ADR-016). |
| `CodeInterpreter:SandboxTimeoutSeconds` | `CodeInterpreter__SandboxTimeoutSeconds` | `int` [5–300] | No | `30` | Semaphore wait + sandbox run timeout (ADR-016). |

### Notes

- Code Interpreter does not require a separate Foundry connection name — it is enabled at the
  **agent definition level** (the `code_interpreter` tool entry in `spaarke-legal-agent.yaml`).
- For BYOK: the customer's Foundry agent must include `code_interpreter` in its tool list.
  No additional BFF config is needed beyond `CodeInterpreter:Enabled = true`.

---

## Section 3: Bing Grounding Options (`BingGrounding`)

**Options class**: `Sprk.Bff.Api.Services.Ai.Foundry.BingGroundingOptions`
**Config section**: `BingGrounding`
**DI validation**: `ValidateDataAnnotations()` — deferred (no `ValidateOnStart`).
**Rationale**: `BingConnectionName` is only needed when `Enabled = true`. The connection
is registered in the customer's AI Foundry project; no API key is stored in BFF config
because auth uses Managed Identity through the Azure AI Projects SDK (ADR-015).

| Config Key | Env Var (App Service) | Type | Required | Default | Purpose |
|------------|-----------------------|------|----------|---------|---------|
| `BingGrounding:Enabled` | `BingGrounding__Enabled` | `bool` | Yes | `false` | Kill switch (ADR-018). When false, `LegalResearchTools` returns a graceful degradation message. |
| `BingGrounding:BingConnectionName` | `BingGrounding__BingConnectionName` | `string` | When Enabled | — | Name of the Bing Grounding connection registered in the AI Foundry project. Retrieve from AI Foundry Studio > Connections. |
| `BingGrounding:MaxConcurrency` | `BingGrounding__MaxConcurrency` | `int` [1–32] | No | `3` | Max concurrent Bing Grounding operations per BFF instance (ADR-016). |
| `BingGrounding:MaxResultsPerQuery` | `BingGrounding__MaxResultsPerQuery` | `int` [1–10] | No | `5` | Max Bing results per query (ADR-015 data minimisation). |

### Example values by deployment model

| Key | Multi-Customer / Dedicated | BYOK (Customer) |
|-----|---------------------------|-----------------|
| `BingGrounding:Enabled` | `true` | `true` |
| `BingGrounding:BingConnectionName` | `bing-grounding-connection` | *(customer's connection name in their Foundry project)* |

---

## Section 4: Analysis Options (`Analysis`)

**Options class**: `Sprk.Bff.Api.Configuration.AnalysisOptions`
**Config section**: `Analysis`
**DI validation**: `ValidateDataAnnotations().ValidateOnStart()` — fail-fast at startup.

The `Analysis` section includes the **deployment model selector** (`DefaultRagModel`) and
the keys needed for customer-owned RAG deployments.

| Config Key | Env Var (App Service) | Type | Required | Default | Purpose |
|------------|-----------------------|------|----------|---------|---------|
| `Analysis:Enabled` | `Analysis__Enabled` | `bool` | Yes | `true` | Master kill switch for the Analysis feature. |
| `Analysis:DefaultRagModel` | `Analysis__DefaultRagModel` | `enum` | No | `Shared` | RAG deployment model: `Shared`, `Dedicated`, or `CustomerOwned`. |
| `Analysis:SharedIndexName` | `Analysis__SharedIndexName` | `string` | No | `spaarke-knowledge-index-v2` | AI Search index name for `Shared` model. |
| `Analysis:CustomerTenantId` | `Analysis__CustomerTenantId` | `string` | When CustomerOwned | — | Customer's Azure AD tenant ID for cross-tenant scenarios. |
| `Analysis:KeyVaultUrl` | `Analysis__KeyVaultUrl` | `string` | When CustomerOwned | — | Customer Key Vault URL for secret resolution at runtime. |
| `Analysis:PromptFlowEndpoint` | `Analysis__PromptFlowEndpoint` | `string` | No | — | AI Foundry Prompt Flow endpoint (optional; falls back to direct Azure OpenAI). |
| `Analysis:DeploymentEnvironment` | `Analysis__DeploymentEnvironment` | `string` | No | `Development` | Environment label for logging and telemetry. |

---

## Section 5: Core Infrastructure Config

These settings apply in all deployment models and are required for BFF startup.

### Azure AD / Authentication

| Config Key | Env Var (App Service) | Required | Purpose |
|------------|-----------------------|----------|---------|
| `AzureAd:TenantId` | `AzureAd__TenantId` | Yes | Azure AD tenant ID (use `common` for multi-tenant). |
| `AzureAd:ClientId` | `AzureAd__ClientId` | Yes | BFF API app registration client ID. |
| `AzureAd:Instance` | `AzureAd__Instance` | No | AAD authority (default: `https://login.microsoftonline.com/`). |

### Redis Cache

| Config Key | Env Var (App Service) | Required | Purpose |
|------------|-----------------------|----------|---------|
| `Redis:Enabled` | `Redis__Enabled` | Yes | Kill switch for Redis caching. |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | When Enabled | Redis connection string (Key Vault reference supported). |
| `Redis:InstanceName` | `Redis__InstanceName` | Yes | Cache key prefix (tenant isolation). |
| `Redis:DefaultExpirationMinutes` | `Redis__DefaultExpirationMinutes` | No | Default sliding expiry (default: 60). |

### Service Bus

| Config Key | Env Var (App Service) | Required | Purpose |
|------------|-----------------------|----------|---------|
| `ServiceBus:ConnectionString` | `ServiceBus__ConnectionString` | Yes | Azure Service Bus connection string. |
| `ServiceBus:QueueName` | `ServiceBus__QueueName` | Yes | Queue name for background jobs. |

### Dataverse

| Config Key | Env Var (App Service) | Required | Purpose |
|------------|-----------------------|----------|---------|
| `Dataverse:ServiceUrl` | `Dataverse__ServiceUrl` | Yes | Dataverse environment URL. |
| `Dataverse:ClientSecret` | `Dataverse__ClientSecret` | Yes | BFF app secret for Dataverse auth (Key Vault reference). |

### Azure OpenAI

| Config Key | Env Var (App Service) | Required | Purpose |
|------------|-----------------------|----------|---------|
| `AzureOpenAI:Endpoint` | `AzureOpenAI__Endpoint` | Yes | Azure OpenAI resource endpoint. |
| `AzureOpenAI:ChatModelName` | `AzureOpenAI__ChatModelName` | Yes | Chat model deployment name (e.g., `gpt-4o`). |
| `AzureOpenAI:EmbeddingModelName` | `AzureOpenAI__EmbeddingModelName` | No | Embedding model (default: `text-embedding-3-small`). |

### Azure AI Search

| Config Key | Env Var (App Service) | Required | Purpose |
|------------|-----------------------|----------|---------|
| `AiSearch:Endpoint` | `AiSearch__Endpoint` | Yes | Azure AI Search endpoint URL. |
| `AiSearch:KnowledgeIndexName` | `AiSearch__KnowledgeIndexName` | No | Knowledge index name (default: `spaarke-knowledge-index-v2`). |
| `AiSearch:DiscoveryIndexName` | `AiSearch__DiscoveryIndexName` | No | Document discovery index name. |
| `AiSearch:SemanticConfigName` | `AiSearch__SemanticConfigName` | No | Semantic search configuration name. |

---

## Section 6: Agent Deployment Environment Variables

These variables are used during agent provisioning (not BFF runtime config). Set them in
your CI/CD pipeline or `.env.agent` file (gitignored) before running deployment commands.

| Variable | Required | Dev Value | Purpose |
|----------|----------|-----------|---------|
| `FOUNDRY_SUBSCRIPTION_ID` | Yes | *(subscription GUID)* | Azure subscription containing the Foundry project. |
| `FOUNDRY_RESOURCE_GROUP` | Yes | `spe-infrastructure-westus2` | Resource group name. |
| `FOUNDRY_PROJECT_NAME` | Yes | `sprkspaarkedev-aif-proj` | AI Foundry project name. |
| `FOUNDRY_ENDPOINT` | Yes | *(project endpoint URL)* | Foundry project endpoint — used by `az ai agent create`. |
| `FOUNDRY_ENVIRONMENT` | No | `dev` | Environment label embedded in agent metadata. |
| `FOUNDRY_MODEL_DEPLOYMENT` | Yes | `gpt-4o-mini` | Azure OpenAI model deployment name registered in the Foundry project. |
| `FOUNDRY_BING_CONNECTION_NAME` | When Bing enabled | `bing-grounding-connection` | Bing connection name registered in the Foundry project. |

> **See also**: `infrastructure/ai-foundry/agents/README.md` for full deployment procedure.

---

## Section 7: Startup Validation Behaviour

### Kill-switch options (AgentService, CodeInterpreter, BingGrounding)

These options use **deferred validation** (`.ValidateDataAnnotations()` only, no `.ValidateOnStart()`).

**Why**: `[Required]` fields like `AgentService:Endpoint` and `AgentService:AgentId` are
only meaningful when `Enabled = true`. Adding `ValidateOnStart()` would crash the app on
startup if the feature is disabled and those fields are absent — which is the normal state
in environments that do not use Foundry.

**Fail-fast mechanism**: When `Enabled = true` but `Endpoint` or `AgentId` is missing,
`ValidateDataAnnotations()` runs on first access and throws `OptionsValidationException`
with a clear error message before any Foundry call is made.

**Kill-switch enforcement**: `AgentServiceClient.GuardEnabled()` is called at the top of
every public method. If `Enabled = false`, it throws `FeatureDisabledException` immediately,
before any network call.

### Fail-fast options (GraphOptions, DataverseOptions, RedisOptions, ServiceBusOptions, AnalysisOptions)

These options use `.ValidateDataAnnotations().ValidateOnStart()`. Missing required fields
cause the app to fail immediately at startup with a descriptive error, preventing silent
misconfiguration in production.

### Testing misconfiguration locally

To verify fail-fast behaviour without a full Azure environment:

```bash
# Verify that AgentService [Required] fields are caught when Enabled=true but fields are missing
dotnet run --project src/server/api/Sprk.Bff.Api/ -- \
  --AgentService:Enabled=true \
  --AgentService:Endpoint="" \
  --AgentService:AgentId=""

# Expected: OptionsValidationException at first options access
# (not at startup — deferred validation by design)
```

```bash
# Verify that the app starts cleanly with AgentService disabled
dotnet run --project src/server/api/Sprk.Bff.Api/ -- \
  --AgentService:Enabled=false

# Expected: app starts successfully. AgentService endpoints return FeatureDisabledException.
```

---

## Section 8: BYOK Deployment Checklist

Use this checklist when deploying to a customer-owned Azure AI Foundry project.

### Pre-deployment

- [ ] Customer has provisioned an Azure AI Foundry **hub** and **project** in their subscription.
- [ ] Customer has created an Azure OpenAI resource and deployed a model that supports the Assistants API (GPT-4o or GPT-4o-mini recommended).
- [ ] Customer has created a Bing Grounding connection in their Foundry project (if `BingGrounding:Enabled = true`).
- [ ] Managed Identity of the BFF App Service is granted `Contributor` (or `AI Developer`) on the customer's Foundry project.
- [ ] Customer's Azure AI Search index exists (for `CustomerOwned` RAG model).

### Agent deployment

- [ ] Set all `FOUNDRY_*` deployment env vars to customer values.
- [ ] Run `infrastructure/ai-foundry/agents/README.md` deployment steps (no YAML changes required).
- [ ] Capture the resulting agent ID.

### BFF configuration

- [ ] Set `AgentService:Enabled = true`.
- [ ] Set `AgentService:Endpoint` to the customer's Foundry project endpoint URL.
- [ ] Set `AgentService:AgentId` to the agent ID from the deployment step.
- [ ] Set `BingGrounding:BingConnectionName` to the connection name registered in the customer's project (if Bing enabled).
- [ ] Set `Analysis:DefaultRagModel = CustomerOwned`.
- [ ] Set `Analysis:CustomerTenantId` to the customer's Azure AD tenant ID.
- [ ] Set `AzureOpenAI:Endpoint` to the customer's Azure OpenAI endpoint (if customer provides their own OpenAI).
- [ ] Verify `AiSearch:Endpoint` points to the customer's AI Search resource (if customer provides their own).

### Verification

- [ ] BFF starts without errors (check Application Insights or App Service logs).
- [ ] `GET /healthz` returns `200 OK`.
- [ ] Send a test message via the Spaarke AI chat interface and verify agent responds.
- [ ] Confirm no resource IDs from Spaarke's dev environment appear in response metadata or logs.

---

## Section 9: Hardcoded Value Audit (AIPU-086)

This section records the results of the code audit performed in AIPU-086.

### Options classes (AgentServiceOptions, CodeInterpreterOptions, BingGroundingOptions)

| Check | Result |
|-------|--------|
| Hardcoded Foundry resource IDs | None found |
| Hardcoded agent IDs (`asst_*`, `proj_*`) | None found |
| Hardcoded Azure endpoints | None found — doc comment example URLs only |
| Hardcoded GUIDs | None found |
| `[Required]` on mandatory fields | All present (`Endpoint`, `AgentId`, `BingConnectionName`) |
| `ValidateDataAnnotations()` registered | Yes (all three options) |
| `ValidateOnStart()` | Intentionally absent (kill-switch deferred validation pattern) |

### AgentServiceClient.cs

| Check | Result |
|-------|--------|
| Hardcoded Foundry endpoint | No — reads from `_options.Endpoint` |
| Hardcoded agent ID | No — reads from `_options.AgentId` |
| `DefaultAzureCredential` (no API key) | Yes — consistent with ADR-015 |

### Infrastructure YAML files

| File | Finding | Action Required |
|------|---------|----------------|
| `infrastructure/ai-foundry/agents/spaarke-legal-agent.yaml` | Fully parameterized via `${VAR}` | None |
| `infrastructure/ai-foundry/connections/ai-search-connection.yaml` | Hardcoded dev endpoint: `spaarke-search-dev.search.windows.net` | Expected — dev-only template; not used in BYOK deployments. Customer creates their own connection. |
| `infrastructure/ai-foundry/connections/azure-openai-connection.yaml` | Hardcoded dev endpoint: `spaarke-openai-dev.openai.azure.com` | Expected — dev-only template; not used in BYOK deployments. Customer creates their own connection. |
| `infrastructure/ai-foundry/agents/README.md` | Dev subscription/resource group values in examples | Expected — documentation examples. |

**Conclusion**: Zero hardcoded Foundry resource IDs in application source code. Infrastructure
template files contain Spaarke dev values which are expected for developer convenience and are
not deployed to BYOK environments.

---

## Section 10: appsettings.json Structure Reference

The full configuration structure expected by the BFF at startup. All `#{PLACEHOLDER}#` values
must be substituted by the deployment pipeline or Key Vault references.

```jsonc
{
  // ─── Agent Service (Azure AI Foundry) ───────────────────────────────────────
  "AgentService": {
    "Enabled": false,           // true to activate Foundry Agent operations
    "Endpoint": "",             // Azure AI Foundry project endpoint URI
    "AgentId": "",              // Agent ID from az ai agent create
    "MaxConcurrency": 4,        // [1–64] Semaphore permits (ADR-016)
    "ThreadCacheExpiryMinutes": 60  // [1–1440] Redis sliding expiry (ADR-009)
  },

  // ─── Code Interpreter ────────────────────────────────────────────────────────
  "CodeInterpreter": {
    "Enabled": false,           // true when Foundry agent includes code_interpreter tool
    "MaxConcurrency": 2,        // [1–32] Static semaphore permits (ADR-016)
    "SandboxTimeoutSeconds": 30 // [5–300] Semaphore wait + run timeout (ADR-016)
  },

  // ─── Bing Grounding ──────────────────────────────────────────────────────────
  "BingGrounding": {
    "Enabled": false,           // true when Foundry project has a Bing connection
    "BingConnectionName": "",   // Connection name from AI Foundry Studio > Connections
    "MaxConcurrency": 3,        // [1–32] Semaphore permits (ADR-016)
    "MaxResultsPerQuery": 5     // [1–10] Bing result cap (ADR-015 data minimisation)
  },

  // ─── Analysis (RAG deployment model selector) ────────────────────────────────
  "Analysis": {
    "Enabled": true,
    "DefaultRagModel": "Shared", // Shared | Dedicated | CustomerOwned
    "CustomerTenantId": null,    // Required for CustomerOwned model
    "KeyVaultUrl": null,         // Required for CustomerOwned model
    // ... (see AnalysisOptions.cs for full schema)
  }
}
```

---

*Generated by AIPU-086 — BYOK configuration verification.*
*Source audit: `src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/` and `infrastructure/ai-foundry/`.*
