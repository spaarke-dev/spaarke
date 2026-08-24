# AppSettings Token Reference

> ## 🔴 Secret-free BFF identity — read before following any credential step on this page
>
> **2026-08-24, `spaarke-auth-v4-dataverse-MI` task 033 (ADR-028 **A4**; exception **E-3 CLOSED**).**
> The BFF authenticates as a confidential client — **including on the OBO / delegated path** — using a
> **federated credential issued to its user-assigned managed identity**. It holds **no client secret**.
>
> | Removed | |
> |---|---|
> | App settings | `API_CLIENT_SECRET`, `AzureAd__ClientSecret`, `Dataverse__ClientSecret`, `AgentToken__ClientSecret` |
> | Key Vault | `BFF-API-ClientSecret`, `bff-api-client-secret`, and the orphaned `Graph-API-ClientSecret` |
>
> Set instead: `Graph__Credentials__Order__0=ManagedIdentityFederated` and
> `Graph__Credentials__RequireSecretFreeIdentity=true`.
>
> **Do not re-create the secret.** A secret listed *beneath* MI-FIC in the order is worse than no migration:
> a broken federated credential would fall through to it silently while every health signal stayed green.
> With `RequireSecretFreeIdentity=true` the app **refuses to start** outside Development if `ClientSecret`
> returns to the order.
>
> Any instruction below that tells you to create, store, reference or rotate a BFF client secret is
> **superseded**. Still valid: ADR-028 **E-1** per-customer SPE owning-app secrets, and
> `PowerBi:ClientSecret` while task 042 is deferred.
> Canonical: [`ADR-028`](../../../../.claude/adr/ADR-028-spaarke-auth-architecture.md) ·
> [`auth-deployment-setup.md`](../../../../docs/guides/auth-deployment-setup.md)


This document describes the tokens used in `appsettings.template.json` for multi-tenant deployment.

## Token Format

Tokens use the format `#{TOKEN_NAME}#` which is compatible with Azure DevOps and GitHub Actions variable substitution.

## Token Reference

| Token | Description | Example Value |
|-------|-------------|---------------|
| `#{TENANT_ID}#` | Azure AD tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `#{API_APP_ID}#` | BFF API app registration client ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `#{DEFAULT_CT_ID}#` | Default container type ID for SPE | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` |
| `#{KEY_VAULT_URL}#` | Key Vault URL (with trailing slash) | `https://spaarke-kv-dev.vault.azure.net/` |
| `#{DATAVERSE_ORG_NAME}#` | Dataverse organization name | `spaarkedev1` |
| `#{REDIS_INSTANCE_NAME}#` | Redis cache instance prefix | `spaarke:` |
| `#{SERVICE_BUS_QUEUE_NAME}#` | Service Bus queue name | `sdap-jobs` |
| `#{AI_SUMMARIZE_MODEL}#` | OpenAI model for summarization | `gpt-4o-mini` |
| `#{AI_REASONING_MODEL}#` | OpenAI deployment for Reasoning-tier Actions (`DocumentIntelligence:ReasoningModel`; ADR-016 model-tier routing). Leave unset/empty until a reasoning-class deployment exists — `ModelTierDeploymentResolver` falls back to `StandardModel` when empty/whitespace, so Reasoning-tagged Actions still execute rather than 404ing. Added by `ai-advanced-capabilities-nda-r1` task 013; see `projects/ai-advanced-capabilities-nda-r1/notes/task-013-reasoning-provisioning.md` for the provisioning runbook + model recommendation. | `` (empty) in dev until provisioned; recommended target `gpt-5` (`reasoning_effort=medium`) once deployed |
| `#{AI_SEARCH_INDEX_NAME}#` | AI Search index for records (Analysis + AiSearch:AllowedIndexes) | `spaarke-records-index` |
| `#{SHARED_KNOWLEDGE_INDEX_NAME}#` | **Canonical single read/write RAG knowledge index** — feeds BOTH `AiSearch:KnowledgeIndexName` (reads) and `Analysis:SharedIndexName` (deprecated writes) per FR-26 / FAILURE-MODES G-9. Set once; both keys resolve to it. | `spaarke-files-index` |
| `#{DISCOVERY_INDEX_NAME}#` | AI Search discovery-tier index (`AiSearch:DiscoveryIndexName` + AllowedIndexes) | `spaarke-discovery-index` |
| `#{RAG_REFERENCES_INDEX_NAME}#` | AI Search golden-reference index (`AiSearch:RagReferencesIndexName` + AllowedIndexes) | `spaarke-rag-references` |
| `#{INSIGHTS_INDEX_NAME}#` | AI Search derived-intelligence index (AllowedIndexes) | `spaarke-insights-index` |
| `#{SESSION_FILES_INDEX_NAME}#` | AI Search session-scoped chat-upload index (AllowedIndexes) | `spaarke-session-files` |
| `#{INVOICES_INDEX_NAME}#` | AI Search invoices index (AllowedIndexes) | `spaarke-invoices-index` |
| `#{PLAYBOOK_EMBEDDINGS_INDEX_NAME}#` | AI Search playbook-embeddings index (AllowedIndexes) | `spaarke-playbook-embeddings` |
| `#{DEPLOYMENT_ENVIRONMENT}#` | Environment name | `Development`, `Test`, `Production` |
| `#{CUSTOMER_TENANT_ID}#` | Customer tenant for cross-tenant (or null) | `null` or GUID |
| `#{RECORD_MATCHING_ENABLED}#` | Enable record matching (boolean) | `true` or `false` |
| `#{ANALYSIS_ENABLED}#` | Enable analysis features (boolean) | `true` or `false` |
| `#{MULTI_DOCUMENT_ENABLED}#` | Enable multi-doc analysis (boolean) | `true` or `false` |
| `#{COPILOT_SSO_PROVIDER_APP_ID}#` | M365 Copilot SSO provider app ID (Teams Developer Portal). Used in `AgentToken:CopilotAudience` as `api://#{COPILOT_SSO_PROVIDER_APP_ID}#/#{API_APP_ID}#`. | `auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb` |
| `#{COPILOT_AGENT_APP_ID}#` | Spaarke Copilot Agent app registration ID (`AgentToken:AgentAppId`) | GUID |

## Key Vault Secrets Required

The template references these Key Vault secrets:

| Secret Name | Description |
|-------------|-------------|
| `ServiceBus-ConnectionString` | Azure Service Bus connection string |
| `Redis-ConnectionString` | Azure Redis connection string |
| `Dataverse-ServiceUrl` | Dataverse environment URL |
| `BFF-API-ClientSecret` | App registration client secret |
| `ai-openai-endpoint` | Azure OpenAI endpoint URL |
| `ai-openai-key` | Azure OpenAI API key |
| `ai-docintel-endpoint` | Document Intelligence endpoint |
| `ai-docintel-key` | Document Intelligence API key |
| `ai-search-endpoint` | AI Search endpoint URL |
| `ai-search-key` | AI Search admin key (legacy operational alias — mirrors AiSearch--AdminKey value) |
| `AiSearch--AdminKey` | AI Search admin key (canonical per spec FR-21; added 2026-06-26 task 001 Option C remediation) |
| `AzureAISearchApiKey` | AI Search admin key (legacy app-settings alias — mirrors AiSearch--AdminKey value; referenced by `AiSearch__ReferencesApiKey`. The `AiSearch__ApiKeySecretName` reference was removed by auth-v4 task 053: the property it bound to was read by nothing) |
| `PromptFlow-Endpoint` | AI Foundry Prompt Flow endpoint |
| `PromptFlow-Key` | AI Foundry Prompt Flow API key |
| `AppInsights-ConnectionString` | Application Insights connection string |

## Usage in CI/CD

### Azure DevOps

```yaml
- task: FileTransform@2
  inputs:
    folderPath: '$(Build.ArtifactStagingDirectory)'
    xmlTransformationRules: ''
    jsonTargetFiles: '**/appsettings.json'
```

### GitHub Actions

```yaml
- name: Replace tokens
  uses: cschleiden/replace-tokens@v1
  with:
    files: '**/appsettings.json'
  env:
    TENANT_ID: ${{ secrets.TENANT_ID }}
    API_APP_ID: ${{ secrets.API_APP_ID }}
    # ... other tokens
```

### PowerShell Script

```powershell
$template = Get-Content "appsettings.template.json" -Raw
$template = $template -replace '#{TENANT_ID}#', $env:TENANT_ID
$template = $template -replace '#{API_APP_ID}#', $env:API_APP_ID
# ... other replacements
$template | Set-Content "appsettings.json"
```

## Development Values (Spaarke Dev 1)

```
TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
API_APP_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c
DEFAULT_CT_ID=8a6ce34c-6055-4681-8f87-2f4f9f921c06
KEY_VAULT_URL=https://spaarke-spekvcert.vault.azure.net/
DATAVERSE_ORG_NAME=spaarkedev1
REDIS_INSTANCE_NAME=spaarke:
SERVICE_BUS_QUEUE_NAME=sdap-jobs
AI_SUMMARIZE_MODEL=gpt-4o-mini
AI_REASONING_MODEL=            # empty until reasoning deployment is provisioned (task 013); target: gpt-5
AI_SEARCH_INDEX_NAME=spaarke-records-index
SHARED_KNOWLEDGE_INDEX_NAME=spaarke-files-index
DISCOVERY_INDEX_NAME=spaarke-discovery-index
RAG_REFERENCES_INDEX_NAME=spaarke-rag-references
INSIGHTS_INDEX_NAME=spaarke-insights-index
SESSION_FILES_INDEX_NAME=spaarke-session-files
INVOICES_INDEX_NAME=spaarke-invoices-index
PLAYBOOK_EMBEDDINGS_INDEX_NAME=spaarke-playbook-embeddings
DEPLOYMENT_ENVIRONMENT=Development
CUSTOMER_TENANT_ID=null
RECORD_MATCHING_ENABLED=false
ANALYSIS_ENABLED=true
MULTI_DOCUMENT_ENABLED=false
COPILOT_SSO_PROVIDER_APP_ID=auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb
COPILOT_AGENT_APP_ID=<set per environment>
```

- **`DocumentIntelligence:FastModel` / `StandardModel` / `ReasoningModel`** — model-tier deployment routing (ADR-016; wired end-to-end by `ai-advanced-capabilities-nda-r1` task 010, `ReasoningModel` tokenized by task 013). `FastModel`/`StandardModel` reuse `#{AI_SUMMARIZE_MODEL}#` so existing environments see no behavior/cost change. `ReasoningModel` uses its own distinct token, `#{AI_REASONING_MODEL}#` — do NOT alias it to `AI_SUMMARIZE_MODEL` (it must be free to point at a different deployment/model family, e.g. `gpt-5`, once provisioned). Leave the token unset/empty in environments without a reasoning deployment; set `DocumentIntelligence__ReasoningModel` (or the `AI_REASONING_MODEL` CI/CD variable) once the reasoning-tier deployment exists — see `projects/ai-advanced-capabilities-nda-r1/notes/task-013-reasoning-provisioning.md` for the provisioning runbook and recommended model.

## Notes

- **`#{COPILOT_SSO_PROVIDER_APP_ID}#`** — Was hardcoded as `auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb` in `appsettings.template.json` line 226 prior to task 047. This identifier is the Teams Developer Portal SSO bridge app ID owned by Microsoft. The same value is used by all current Spaarke deployments; the placeholder exists to enable cross-tenant re-pointing if Microsoft ships a successor provider app or per-deployment requirements differ. If `COPILOT_SSO_PROVIDER_APP_ID` is unset in CI/CD, use the default value shown above.

