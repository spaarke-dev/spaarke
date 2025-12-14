# AppSettings Token Reference

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
| `#{REDIS_INSTANCE_NAME}#` | Redis cache instance prefix | `sdap-dev:` |
| `#{SERVICE_BUS_QUEUE_NAME}#` | Service Bus queue name | `sdap-jobs` |
| `#{AI_SUMMARIZE_MODEL}#` | OpenAI model for summarization | `gpt-4o-mini` |
| `#{AI_SEARCH_INDEX_NAME}#` | AI Search index for records | `spaarke-records-index` |
| `#{SHARED_KNOWLEDGE_INDEX_NAME}#` | AI Search index for RAG knowledge | `spaarke-knowledge-shared` |
| `#{DEPLOYMENT_ENVIRONMENT}#` | Environment name | `Development`, `Test`, `Production` |
| `#{CUSTOMER_TENANT_ID}#` | Customer tenant for cross-tenant (or null) | `null` or GUID |
| `#{RECORD_MATCHING_ENABLED}#` | Enable record matching (boolean) | `true` or `false` |
| `#{ANALYSIS_ENABLED}#` | Enable analysis features (boolean) | `true` or `false` |
| `#{MULTI_DOCUMENT_ENABLED}#` | Enable multi-doc analysis (boolean) | `true` or `false` |

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
| `ai-search-key` | AI Search admin key |
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
REDIS_INSTANCE_NAME=sdap-dev:
SERVICE_BUS_QUEUE_NAME=sdap-jobs
AI_SUMMARIZE_MODEL=gpt-4o-mini
AI_SEARCH_INDEX_NAME=spaarke-records-index
SHARED_KNOWLEDGE_INDEX_NAME=spaarke-knowledge-shared
DEPLOYMENT_ENVIRONMENT=Development
CUSTOMER_TENANT_ID=null
RECORD_MATCHING_ENABLED=false
ANALYSIS_ENABLED=true
MULTI_DOCUMENT_ENABLED=false
```
