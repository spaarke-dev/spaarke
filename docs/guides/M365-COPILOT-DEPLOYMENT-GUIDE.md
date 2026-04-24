# M365 Copilot Integration Deployment Guide

> **Last Updated**: March 26, 2026
>
> **Project**: Spaarke M365 Copilot Integration
>
> **Audience**: Developers and administrators deploying the Spaarke Copilot agent to a new environment

This guide walks through deploying all components of the Spaarke M365 Copilot integration from scratch. It covers Azure infrastructure, BFF API configuration, the Teams app package with declarative agent, and end-to-end verification.

---

## Prerequisites

Before starting deployment, ensure the following are in place:

| Requirement | Details |
|-------------|---------|
| **Azure subscription** | With an existing resource group (e.g., `spe-infrastructure-westus2`) |
| **M365 tenant** | With Microsoft 365 Copilot licenses assigned to target users |
| **Azure CLI** | Installed and authenticated (`az login`) |
| **Power Platform environment** | With Copilot enabled in admin center settings |
| **.NET 8 SDK** | For building the BFF API (`dotnet --version` should return 8.x) |
| **Node.js 18+** | Required if using Teams Agents Toolkit for local development |
| **Entra ID permissions** | Ability to create app registrations and grant admin consent |
| **Teams Admin access** | To upload custom apps to the org catalog |

---

## Architecture Overview

```
+---------------------+       +------------------------+       +------------------+
|  Teams App Package  | ----> |   M365 Copilot Host    | ----> | Declarative Agent|
| (manifest.json,     |       | (Teams, MDA Copilot    |       | (declarativeAgent|
|  icons, agent def)  |       |  side pane)            |       |  .json)          |
+---------------------+       +------------------------+       +--------+---------+
                                                                         |
                                                                         v
                                                               +------------------+
                                                               |   API Plugin     |
                                                               | (spaarke-api-    |
                                                               |  plugin.json +   |
                                                               |  OpenAPI spec)   |
                                                               +--------+---------+
                                                                         |
                                                                         v
                                                               +------------------+
                                                               |   BFF API        |
                                                               | (Sprk.Bff.Api)   |
                                                               | /api/agent/*     |
                                                               +--------+---------+
                                                                         |
                                                          +--------------+--------------+
                                                          |              |              |
                                                          v              v              v
                                                   +-----------+  +-----------+  +-----------+
                                                   | Dataverse |  |    SPE    |  | Azure AI  |
                                                   | (matters, |  | (document |  | (OpenAI,  |
                                                   |  tasks)   |  |  storage) |  |  Search)  |
                                                   +-----------+  +-----------+  +-----------+
```

**Data flow**: A user message in M365 Copilot is routed to the Spaarke declarative agent, which invokes functions defined in the API plugin. The plugin calls the BFF API's `/api/agent/*` endpoints, which orchestrate queries against Dataverse, SPE document storage, and Azure AI services.

---

## Step 1: Entra App Registration

Create an app registration for the bot identity. This app ID is used by both the Bot Service and the BFF API for token validation.

```powershell
# Create the app registration
az ad app create --display-name "Spaarke Copilot Bot Dev" --sign-in-audience AzureADMyOrg

# Note the appId from the output — you will need it for all subsequent steps
# Example: f257a0a9-1061-4f9b-8918-3ad056fe90db
```

Record the following values:

| Value | Where to Find | Used In |
|-------|---------------|---------|
| **Application (client) ID** | Output of `az ad app create`, or Entra portal | Bot Service `appId`, manifest.json `${{TEAMS_APP_ID}}`, BFF API config |
| **Directory (tenant) ID** | Entra portal > Overview | BFF API `AgentToken:TenantId` |
| **Client secret** | Entra portal > Certificates & secrets > New client secret | BFF API `AgentToken:ClientSecret` |

### Configure API Permissions (if needed)

For the initial deployment, the app registration does not require additional API permissions beyond the defaults. The BFF API uses its own service principal for Dataverse and Graph access. If you need the bot to call Microsoft Graph directly in the future, add permissions here.

### Create a Client Secret

```powershell
# Create a client secret (valid for 2 years)
az ad app credential reset --id YOUR-APP-ID --years 2
```

Store the secret securely in Azure Key Vault (see Step 4).

---

## Step 2: Deploy Bot Service

The Bot Service resource connects M365 Copilot to the BFF API messaging endpoint.

### Important Constraints

- **Location MUST be `global`** -- Bot Service does not support regional locations like `westus2`. Deploying to a region will fail.
- **`enableManagedIdentity` MUST be `false`** -- Bot Service does not support managed identity in the current API version. Setting this to `true` causes deployment errors.

### Deploy with Bicep

The Bicep template is at `infrastructure/bot-service/main.bicep` with dev parameters at `infrastructure/bot-service/parameters.dev.json`.

```powershell
az deployment group create --resource-group spe-infrastructure-westus2 --template-file infrastructure/bot-service/main.bicep --parameters infrastructure/bot-service/parameters.dev.json --parameters appId=YOUR-APP-ID location=global enableManagedIdentity=false
```

Replace `YOUR-APP-ID` with the application (client) ID from Step 1.

### What the Template Deploys

| Resource | Name | Purpose |
|----------|------|---------|
| Bot Service | `spaarke-bot-dev` | Azure Bot (F0 SKU, SingleTenant) |
| Teams Channel | `MsTeamsChannel` | Enables Teams integration |
| DirectLine Channel | `DirectLineChannel` | Enables Copilot and web integrations |

The messaging endpoint is configured as `https://spe-api-dev-67e2xz.azurewebsites.net/api/agent/message` (set in `parameters.dev.json`).

### Verify Deployment

```powershell
az bot show --resource-group spe-infrastructure-westus2 --name spaarke-bot-dev --query "{name:name, endpoint:properties.endpoint, appId:properties.msaAppId}" -o table
```

Expected output should show the bot name, messaging endpoint, and app ID.

---

## Step 3: Deploy BFF API

The BFF API (`Sprk.Bff.Api`) hosts the `/api/agent/*` endpoints that the Copilot plugin calls.

### Option A: Use the Deploy Script (Recommended)

```powershell
.\scripts\Deploy-BffApi.ps1 -Environment dev
```

This script handles build, package, and deployment to the App Service.

### Option B: Manual Deployment

#### Build

```powershell
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o ./publish
```

#### Package

```powershell
# Create a ZIP archive of the publish output
Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force
```

#### Deploy via Kudu ZIP Deploy

```powershell
# Get publishing credentials
$creds = az webapp deployment list-publishing-credentials --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --query "[publishingUserName,publishingPassword]" -o tsv
$user = ($creds -split "`n")[0]
$pass = ($creds -split "`n")[1]

# Upload via curl
curl -X POST "https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/zipdeploy" -u "${user}:${pass}" --data-binary "@publish.zip" -H "Content-Type: application/zip"
```

### Verify API Health

```powershell
# Basic health check
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: "pong"

# Verify agent endpoints exist (401 is expected without auth token)
curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/api/agent/playbooks
# Expected: 401
```

A 401 response on agent endpoints confirms the endpoint is registered and authentication is enforced. A 404 would indicate the agent endpoints are not mapped (see Troubleshooting).

---

## Step 4: Configure BFF API Settings

The BFF API needs configuration for bot authentication and Copilot agent features.

### AgentToken Configuration

Add these settings to the App Service configuration or Azure Key Vault:

| Setting | Value | Description |
|---------|-------|-------------|
| `AgentToken:TenantId` | Your Entra tenant ID | Azure AD tenant |
| `AgentToken:ClientId` | App registration client ID from Step 1 | Bot app identity |
| `AgentToken:ClientSecret` | Client secret from Step 1 | Bot app credential |
| `AgentToken:AgentAppId` | Same as ClientId (for bot-to-API auth) | Bot Framework app validation |
| `AgentToken:DataverseEnvironmentUrl` | `https://spaarkedev1.crm.dynamics.com` | Target Dataverse environment |

### CopilotAgent Feature Toggles

| Setting | Default | Description |
|---------|---------|-------------|
| `CopilotAgent:Enabled` | `true` | Master toggle for Copilot agent features |
| `CopilotAgent:PlaybookExecutionEnabled` | `true` | Allow playbook invocation from Copilot |
| `CopilotAgent:EmailDraftingEnabled` | `true` | Allow email drafting from Copilot |

### Apply Settings via Azure CLI

```powershell
az webapp config appsettings set --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --settings `
  AgentToken__TenantId="YOUR-TENANT-ID" `
  AgentToken__ClientId="YOUR-APP-ID" `
  AgentToken__ClientSecret="YOUR-SECRET" `
  AgentToken__AgentAppId="YOUR-APP-ID" `
  AgentToken__DataverseEnvironmentUrl="https://spaarkedev1.crm.dynamics.com" `
  CopilotAgent__Enabled="true"
```

For production environments, store secrets in Azure Key Vault and use Key Vault references:

```
@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/AgentToken-ClientSecret/)
```

---

## Step 5: Package and Sideload Declarative Agent

The declarative agent package lives at `src/solutions/CopilotAgent/`.

### Package Contents

The Teams app package must contain these files in a **flat ZIP archive** (no subdirectories):

| File | Source Location | Description |
|------|-----------------|-------------|
| `manifest.json` | `appPackage/manifest.json` | Teams app manifest (devPreview schema) |
| `declarativeAgent.json` | `declarativeAgent.json` | Agent definition with instructions and conversation starters |
| `spaarke-api-plugin.json` | `spaarke-api-plugin.json` | API plugin with 24 function definitions |
| `spaarke-bff-openapi.yaml` | `spaarke-bff-openapi.yaml` | OpenAPI spec for BFF agent endpoints |
| `color.png` | Must be created/provided | 192x192 full-color icon |
| `outline.png` | Must be created/provided | 32x32 transparent outline icon |

### Prepare the Manifest

Edit `appPackage/manifest.json` and replace the placeholder with your app ID:

```json
"id": "f257a0a9-1061-4f9b-8918-3ad056fe90db"
```

Replace the `${{TEAMS_APP_ID}}` placeholder with the actual Entra app registration ID from Step 1.

### Prepare Icons

- **color.png**: 192x192 pixels, full-color brand icon (used in app listings)
- **outline.png**: 32x32 pixels, transparent background with white outline (used in Teams header)

### Create the Package

Build a flat ZIP archive containing all six files at the root level:

```powershell
# From the repository root
$files = @(
    "src/solutions/CopilotAgent/appPackage/manifest.json",
    "src/solutions/CopilotAgent/declarativeAgent.json",
    "src/solutions/CopilotAgent/spaarke-api-plugin.json",
    "src/solutions/CopilotAgent/spaarke-bff-openapi.yaml",
    "src/solutions/CopilotAgent/appPackage/color.png",
    "src/solutions/CopilotAgent/appPackage/outline.png"
)

# Copy to a temp directory (flat structure)
$staging = New-Item -ItemType Directory -Path "./copilot-package-staging" -Force
foreach ($f in $files) {
    Copy-Item $f -Destination $staging.FullName
}

# Create ZIP
Compress-Archive -Path "$($staging.FullName)/*" -DestinationPath "./spaarke-copilot-agent.zip" -Force

# Clean up
Remove-Item $staging.FullName -Recurse -Force
```

### Upload to Organization

**Option A: Teams Admin Center (recommended for org-wide deployment)**

1. Navigate to https://admin.teams.microsoft.com/policies/manage-apps
2. Click "Upload new app"
3. Select `spaarke-copilot-agent.zip`
4. Approve the app for the organization

**Option B: Sideload for Testing**

1. Open Microsoft Teams
2. Go to Apps > Manage your apps
3. Click "Upload a custom app"
4. Select `spaarke-copilot-agent.zip`
5. The agent is now available only to your account for testing

---

## Step 6: Verify in Model-Driven App (MDA)

### Prerequisites

1. **Copilot must be enabled** in the Power Platform Admin Center for the target environment
   - Go to https://admin.powerplatform.microsoft.com
   - Select the environment (e.g., `spaarkedev1`)
   - Settings > Features > Copilot > Enable

2. The Teams app must be published to the org catalog (Step 5)

### Verification Steps

1. Open a model-driven app in the target Dataverse environment (e.g., `https://spaarkedev1.crm.dynamics.com`)
2. Open the **Copilot side pane** (chat icon in the top-right corner)
3. Verify **"Spaarke AI"** appears as an available agent with conversation starters:
   - "What are my overdue tasks?"
   - "Find documents for the Acme matter"
   - "Run a risk scan on this contract"
   - "What analysis tools are available?"
   - "Draft an update to outside counsel"
   - "Show my assignments this week"
4. Test a query: Click "What are my overdue tasks?" -- this should invoke the `listEvents` function through the API plugin and return task data from Dataverse
5. Test document search: "Find documents for [matter name]" -- should invoke `semanticSearch`
6. Test playbook listing: "What analysis tools are available?" -- should invoke `listPublicPlaybooks`

---

## Step 7: BYOK Deployment (Customer-Hosted)

For customer-hosted (Bring Your Own Key) deployments where the customer provides their own Azure subscription, refer to:

- **Template**: `infrastructure/byok/main.bicep`
- **Parameter template**: `infrastructure/byok/parameters.template.json`
- **Full guide**: `infrastructure/byok/README.md`

### Key Differences from Hosted Deployment

| Aspect | Hosted (Spaarke) | BYOK (Customer) |
|--------|-------------------|------------------|
| Azure subscription | Spaarke-owned | Customer-provided |
| Azure OpenAI | `spaarke-openai-dev` | Customer deploys their own |
| AI Search | `spaarke-search-dev` | Customer deploys their own |
| Key Vault | `spaarke-spekvcert` | Customer-provided |
| App Service | `spe-api-dev-67e2xz` | Customer deploys from template |
| Entra app registration | Spaarke tenant | Customer tenant (multi-tenant app or per-customer) |

The BYOK Bicep template provisions all required Azure resources in the customer's subscription. Follow the `infrastructure/byok/README.md` for the complete BYOK deployment walkthrough.

---

## Troubleshooting

### 404 on Agent Endpoints

**Symptom**: `GET /api/agent/playbooks` returns 404.

**Cause**: The agent endpoint mappings are not registered in the BFF API startup.

**Fix**: Verify that `EndpointMappingExtensions.cs` includes `MapAgentEndpoints()` in the endpoint configuration. Rebuild and redeploy the API.

---

### 401 on Agent Endpoints

**Symptom**: `GET /api/agent/playbooks` returns 401.

**This is expected behavior** when calling without an authentication token. The 401 confirms the endpoint exists and auth is enforced. Verify authentication works end-to-end by testing through the Copilot interface, which provides the OAuth token automatically.

---

### Bot Service Location Error

**Symptom**: Bicep deployment fails with a location-related error.

**Cause**: Bot Service requires `location: "global"`. Specifying a region like `westus2` will fail.

**Fix**: Pass `--parameters location=global` in the deployment command, or verify `parameters.dev.json` has `"location": { "value": "global" }`.

---

### Bot Service Managed Identity Error

**Symptom**: Deployment fails with an error related to managed identity or `identity` property.

**Cause**: Bot Service does not support managed identity in the current API version.

**Fix**: Pass `--parameters enableManagedIdentity=false` in the deployment command.

---

### PowerShell Line Continuation

**Symptom**: Multi-line PowerShell commands fail with syntax errors.

**Fix**: In PowerShell, use the backtick (`` ` ``) for line continuation, not backslash (`\`). Alternatively, put the entire command on a single line.

```powershell
# Correct (backtick)
az deployment group create `
  --resource-group spe-infrastructure-westus2 `
  --template-file infrastructure/bot-service/main.bicep

# Also correct (single line)
az deployment group create --resource-group spe-infrastructure-westus2 --template-file infrastructure/bot-service/main.bicep
```

---

### Azure CLI Not Found in PowerShell

**Symptom**: `az` command not recognized after installation.

**Fix**: Restart the terminal session after installing Azure CLI. If the issue persists, check that the Azure CLI install directory is in your system PATH, or use the full path: `C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd`.

---

### Kudu Upload Connection Reset

**Symptom**: `az webapp deploy` or Kudu ZIP upload fails with connection reset for large packages.

**Fix**: Use `curl` directly for the upload instead of `az webapp deploy`:

```powershell
curl -X POST "https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/zipdeploy" -u "$user:$pass" --data-binary "@publish.zip" -H "Content-Type: application/zip"
```

---

### Agent Not Appearing in MDA Copilot

**Symptom**: The Spaarke AI agent does not show up in the Copilot side pane.

**Checklist**:

1. **App in org catalog**: Verify the Teams app was uploaded and approved in the Teams Admin Center
2. **Copilot enabled**: Confirm Copilot is enabled in Power Platform Admin Center for the target environment
3. **User licensed**: Confirm the user has a Microsoft 365 Copilot license assigned
4. **Cache**: Clear browser cache and reload the model-driven app
5. **Manifest**: Verify `manifest.json` has a valid `copilotAgents.declarativeAgents` section pointing to `declarativeAgent.json`
6. **Wait time**: After uploading, it can take up to 24 hours for the agent to propagate to all users

---

### Playbook Execution Timeout

**Symptom**: Playbook invocation from Copilot returns no results or times out.

**Fix**: Check BFF API logs for the playbook execution. Verify Azure OpenAI connectivity and that the `CopilotAgent:PlaybookExecutionEnabled` setting is `true`. Long-running playbooks should return a run ID for polling rather than blocking.

---

## Environment Reference

| Component | Dev Value |
|-----------|-----------|
| Resource Group | `spe-infrastructure-westus2` |
| Azure Subscription | `484bc857-3802-427f-9ea5-ca47b43db0f0` |
| App Service (BFF API) | `spe-api-dev-67e2xz` |
| BFF API URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Bot Service | `spaarke-bot-dev` |
| Bot App ID | `f257a0a9-1061-4f9b-8918-3ad056fe90db` |
| Messaging Endpoint | `https://spe-api-dev-67e2xz.azurewebsites.net/api/agent/message` |
| Dataverse Environment | `https://spaarkedev1.crm.dynamics.com` |
| Key Vault | `spaarke-spekvcert` |
| Azure OpenAI | `spaarke-openai-dev` |
| AI Search | `spaarke-search-dev` |
| Teams Admin Center | `https://admin.teams.microsoft.com` |
| Power Platform Admin | `https://admin.powerplatform.microsoft.com` |

---

## Related Documentation

| Document | Path | Description |
|----------|------|-------------|
| M365 Copilot Admin Guide | `docs/guides/M365-COPILOT-ADMIN-GUIDE.md` | Admin configuration and management |
| M365 Copilot User Guide | `docs/guides/M365-COPILOT-USER-GUIDE.md` | End-user guide for Copilot features |
| BFF API Deployment | `scripts/Deploy-BffApi.ps1` | Automated BFF API deployment script |
| Bot Service Bicep | `infrastructure/bot-service/main.bicep` | Bot Service infrastructure template |
| BYOK Deployment | `infrastructure/byok/README.md` | Customer-hosted deployment guide |
| Declarative Agent | `src/solutions/CopilotAgent/declarativeAgent.json` | Agent definition and instructions |
| API Plugin | `src/solutions/CopilotAgent/spaarke-api-plugin.json` | Plugin with 24 function definitions |
| AI Deployment Guide | `docs/guides/AI-DEPLOYMENT-GUIDE.md` | General AI services deployment |
| Spaarke Deployment Guide | `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` | Full environment + production deployment |
