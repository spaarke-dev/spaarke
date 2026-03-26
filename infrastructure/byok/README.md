# Spaarke BYOK (Bring Your Own Key) Deployment Guide

Deploy the complete Spaarke AI stack into your own Azure subscription. This deployment model provides full data sovereignty — all AI resources, secrets, caching, and telemetry remain within your tenant boundary.

## Deployed Resources

| Resource | Purpose |
|----------|---------|
| Azure App Service (Linux, .NET 8) | BFF API hosting |
| Azure OpenAI | Customer-controlled AI models and data boundary |
| Azure AI Search | Physical data isolation for vector search |
| Azure Bot Service | M365 Copilot agent with Teams and DirectLine channels |
| Azure Cache for Redis | Session and token caching |
| Azure Key Vault | Secrets management with RBAC authorization |
| Application Insights + Log Analytics | Telemetry and diagnostics |

## Prerequisites

1. **Azure subscription** with the following resource providers registered:
   - `Microsoft.Web`
   - `Microsoft.CognitiveServices`
   - `Microsoft.Search`
   - `Microsoft.BotService`
   - `Microsoft.Cache`
   - `Microsoft.KeyVault`
   - `Microsoft.Insights`
   - `Microsoft.OperationalInsights`

2. **Azure CLI** v2.50+ installed and authenticated:
   ```bash
   az --version
   az login
   ```

3. **Azure OpenAI access** approved for your subscription. Apply at: https://aka.ms/oai/access

4. **Entra app registration** for the Bot Service:
   - Go to Azure Portal > Microsoft Entra ID > App registrations > New registration
   - Name: `Spaarke Bot - {environment}`
   - Supported account type: **Single tenant**
   - Copy the **Application (client) ID** — this is your `botAppId` parameter
   - Under "Certificates & secrets", create a client secret (store securely)

5. **Dataverse environment** URL (e.g., `https://contoso.crm.dynamics.com`)

6. **Region selection**: Choose a region that supports Azure OpenAI models. See [model availability](https://learn.microsoft.com/azure/ai-services/openai/concepts/models#model-summary-table-and-region-availability). Recommended: `westus2`, `eastus2`, `swedencentral`.

## Step-by-Step Deployment

### Step 1: Create the Resource Group

```bash
az group create \
  --name rg-spaarke-byok-prod \
  --location westus2 \
  --tags environment=prod application=spaarke deploymentModel=byok
```

### Step 2: Prepare Parameters

Copy the parameter template and fill in your values:

```bash
cp infrastructure/byok/parameters.template.json infrastructure/byok/parameters.prod.json
```

Edit `parameters.prod.json` and replace all `REPLACE_WITH_*` placeholders:

| Parameter | What to Enter |
|-----------|---------------|
| `dataverseUrl` | Your Dataverse environment URL |
| `botAppId` | Application (client) ID from your Entra app registration |
| `tags.customer` | Your organization name |

Review and adjust optional parameters:

| Parameter | Dev | Staging | Production |
|-----------|-----|---------|------------|
| `appServicePlanSku` | `B1` | `S1` | `P1v3` |
| `aiSearchSku` | `basic` | `standard` | `standard` |
| `aiSearchReplicaCount` | `1` | `1` | `2` |
| `redisSku` | `Basic` | `Standard` | `Premium` |
| `botServiceSku` | `F0` | `F0` | `S1` |

### Step 3: Validate the Deployment

Run a what-if deployment to preview changes without creating resources:

```bash
az deployment group what-if \
  --resource-group rg-spaarke-byok-prod \
  --template-file infrastructure/byok/main.bicep \
  --parameters infrastructure/byok/parameters.prod.json
```

Review the output to confirm all resources will be created as expected.

### Step 4: Deploy

```bash
az deployment group create \
  --name spaarke-byok-$(date +%Y%m%d-%H%M%S) \
  --resource-group rg-spaarke-byok-prod \
  --template-file infrastructure/byok/main.bicep \
  --parameters infrastructure/byok/parameters.prod.json
```

Deployment takes approximately 15-25 minutes. The Azure OpenAI model deployments and Redis cache are the longest-running resources.

### Step 5: Capture Deployment Outputs

```bash
az deployment group show \
  --name <deployment-name> \
  --resource-group rg-spaarke-byok-prod \
  --query properties.outputs \
  --output json
```

Save these outputs — you will need them for post-deployment configuration.

## Post-Deployment Configuration

### 1. Deploy the BFF API Application

Deploy the Spaarke BFF API package to the App Service:

```bash
# Using zip deploy (replace with actual artifact path)
az webapp deploy \
  --resource-group rg-spaarke-byok-prod \
  --name <appServiceName-from-outputs> \
  --src-path ./publish/Sprk.Bff.Api.zip \
  --type zip
```

### 2. Configure Bot Service Authentication

Store the bot client secret in Key Vault:

```bash
az keyvault secret set \
  --vault-name <keyVaultName-from-outputs> \
  --name "bot-client-secret" \
  --value "<your-bot-app-client-secret>"
```

### 3. Configure Dataverse Connection

Add the App Service managed identity to your Dataverse environment:

1. Navigate to the Power Platform Admin Center
2. Select your environment > Settings > Users
3. Add the App Service managed identity as an application user
4. Assign the appropriate Dataverse security role

The App Service managed identity principal ID is available in the deployment outputs (`appServicePrincipalId`).

### 4. Register the M365 Copilot Agent

1. Go to the [Teams Developer Portal](https://dev.teams.microsoft.com/)
2. Create a new app or import the Spaarke app manifest
3. Under "Bot", configure the Bot ID using your `botAppId`
4. Set the messaging endpoint to: `https://<appServiceUrl>/api/messages`
5. Publish the app to your organization's app catalog

### 5. Store Additional Secrets (Optional)

If your deployment requires additional connection strings or API keys:

```bash
# Example: Store a custom API key
az keyvault secret set \
  --vault-name <keyVaultName-from-outputs> \
  --name "custom-api-key" \
  --value "<your-key>"
```

## Verification

Run these checks after deployment to confirm everything is healthy.

### Health Check

```bash
# BFF API health endpoint
curl -s https://<appServiceUrl>/health
# Expected: 200 OK
```

### Resource Verification

```bash
# Verify all resources are provisioned
az resource list \
  --resource-group rg-spaarke-byok-prod \
  --output table

# Verify Azure OpenAI models are deployed
az cognitiveservices account deployment list \
  --resource-group rg-spaarke-byok-prod \
  --name <openAiName-from-outputs> \
  --output table

# Verify Redis connectivity
az redis show \
  --resource-group rg-spaarke-byok-prod \
  --name <redisName> \
  --query provisioningState \
  --output tsv
# Expected: Succeeded

# Verify Bot Service
az bot show \
  --resource-group rg-spaarke-byok-prod \
  --name <botServiceName-from-outputs> \
  --query properties.endpoint \
  --output tsv
# Expected: https://<appServiceUrl>/api/messages
```

### Application Insights Verification

1. Open the Azure Portal
2. Navigate to the Application Insights resource
3. Check "Live Metrics" to confirm telemetry is flowing
4. Check "Failures" blade for any startup errors

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| OpenAI deployment fails | Verify your subscription has Azure OpenAI access approved and the selected region supports the requested models |
| Bot Service returns 401 | Confirm the `botAppId` matches your Entra app registration and the client secret is stored in Key Vault |
| App Service returns 503 | Check Application Insights for startup errors; verify the .NET 8 runtime is available on the App Service Plan |
| Redis connection timeout | Verify the Redis instance provisioning is complete (`provisioningState: Succeeded`); Redis can take 15-20 minutes |
| Key Vault access denied | Confirm the App Service managed identity has the "Key Vault Secrets User" role assignment (deployed automatically) |
| AI Search returns 403 | Verify the App Service managed identity has the "Search Index Data Contributor" role (deployed automatically) |

## Updating the Deployment

To update an existing deployment (e.g., change SKU, add models), modify your parameters file and re-run the deployment command. Bicep deployments are idempotent — only changed resources will be updated.

```bash
az deployment group create \
  --name spaarke-byok-update-$(date +%Y%m%d-%H%M%S) \
  --resource-group rg-spaarke-byok-prod \
  --template-file infrastructure/byok/main.bicep \
  --parameters infrastructure/byok/parameters.prod.json
```

> **Warning**: Changing the `environmentName` parameter will attempt to create new resources with different names rather than updating existing ones. Do not change this value after initial deployment.

## Security Hardening (Production)

For production deployments, consider these additional steps after verifying connectivity:

1. **Restrict network access** on Azure OpenAI and Key Vault to the App Service VNet
2. **Enable private endpoints** for Key Vault, Redis, and AI Search
3. **Disable public network access** on OpenAI after validating private endpoint connectivity
4. **Enable customer-managed keys** (CMK) on AI Search for encryption at rest
5. **Configure IP restrictions** on the App Service to allow only expected traffic sources
6. **Rotate secrets** stored in Key Vault on a regular schedule (recommended: 365-day expiry with 30-day notification)
