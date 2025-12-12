# Configuration Templates

This folder contains token-replacement templates for customer deployments.

## Files

| File | Purpose |
|------|---------|
| `appsettings.customer-template.json` | Token-based config template for BFF API |

## Token Replacement

Tokens use the format `#{TOKEN_NAME}#` or `#{TOKEN_NAME:default}#` for defaults.

### Required Tokens

| Token | Description | Example |
|-------|-------------|---------|
| `TENANT_ID` | Azure AD tenant ID | `12345678-1234-1234-1234-123456789abc` |
| `API_APP_ID` | BFF API app registration ID | `12345678-1234-1234-1234-123456789abc` |
| `KEY_VAULT_NAME` | Key Vault name (no URL) | `sprkcustprod-kv` |
| `CUSTOMER_ID` | Customer identifier | `acme` |
| `DATAVERSE_URL` | Dataverse org URL | `https://acme.crm.dynamics.com` |
| `DATAVERSE_API_URL` | Dataverse API URL | `https://acme.api.crm.dynamics.com` |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint | `https://acme-openai.openai.azure.com/` |
| `DOC_INTEL_ENDPOINT` | Document Intelligence endpoint | `https://eastus.api.cognitive.microsoft.com/` |
| `AI_SEARCH_ENDPOINT` | Azure AI Search endpoint | `https://acme-search.search.windows.net` |

### Optional Tokens (with defaults)

| Token | Default | Description |
|-------|---------|-------------|
| `SPE_CONTAINER_TYPE_ID` | (empty) | SharePoint Embedded container type |
| `AI_ENABLED` | `true` | Enable AI features |
| `RECORD_MATCHING_ENABLED` | `false` | Enable record matching |
| `AI_SEARCH_INDEX_NAME` | `spaarke-records-index` | AI Search index name |
| `ANALYSIS_ENABLED` | `true` | Enable Analysis feature |
| `PROMPT_FLOW_ENDPOINT` | (empty) | AI Foundry Prompt Flow endpoint |
| `DEFAULT_RAG_MODEL` | `Shared` | RAG deployment model |
| `DEPLOYMENT_ENVIRONMENT` | `Production` | Environment name |
| `ENABLE_DETAILED_LOGGING` | `false` | Verbose logging |

## CI/CD Integration

### Azure DevOps

```yaml
- task: FileTransform@1
  displayName: 'Replace tokens in appsettings'
  inputs:
    folderPath: '$(Build.ArtifactStagingDirectory)'
    fileType: 'json'
    targetFiles: '**/appsettings*.json'
```

### GitHub Actions

```yaml
- name: Replace tokens
  uses: cschleiden/replace-tokens@v1
  with:
    files: '**/appsettings*.json'
  env:
    TENANT_ID: ${{ secrets.TENANT_ID }}
    API_APP_ID: ${{ secrets.API_APP_ID }}
    # ... other secrets
```

## Mapping to Dataverse Environment Variables

These tokens correspond to Dataverse Environment Variables for PCF access:

| Token | Environment Variable |
|-------|---------------------|
| `AZURE_OPENAI_ENDPOINT` | `sprk_AzureOpenAiEndpoint` |
| `DOC_INTEL_ENDPOINT` | `sprk_DocumentIntelligenceEndpoint` |
| `AI_SEARCH_ENDPOINT` | `sprk_AzureAiSearchEndpoint` |
| `PROMPT_FLOW_ENDPOINT` | `sprk_PromptFlowEndpoint` |
| `AI_ENABLED` | `sprk_EnableAiFeatures` |
| `DEPLOYMENT_ENVIRONMENT` | `sprk_DeploymentEnvironment` |
