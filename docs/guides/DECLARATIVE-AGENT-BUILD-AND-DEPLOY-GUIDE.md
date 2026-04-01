# Declarative Agent Build and Deploy Guide

> **Last Updated**: 2026-03-26
> **Project**: ai-m365-copilot-integration (R1)
> **Status**: Validated through deployment to dev tenant

This guide documents the complete process for building and deploying a Declarative Agent with API Plugin to M365 Copilot, including all lessons learned from the initial deployment.

---

## What Is a Declarative Agent?

A Declarative Agent is a configuration-driven extension for M365 Copilot. It consists of manifest files (no code running in Copilot itself) that tell Copilot:
- **Who it is** — name, instructions, conversation starters
- **What it can do** — API Plugin functions pointing to your backend API
- **How to call your API** — OpenAPI spec with endpoint definitions

The agent runs inside Microsoft's Copilot orchestrator. Your BFF API handles all the actual work.

## Architecture

```
Teams App Package (.zip)
├── manifest.json              ← Teams app identity + Copilot agent declaration
├── declarativeAgent.json      ← Instructions + conversation starters + plugin ref
├── spaarke-api-plugin.json    ← Function definitions (what Copilot can call)
├── spaarke-bff-openapi.yaml   ← OpenAPI spec (how to call your API)
├── color.png                  ← 192x192 app icon
└── outline.png                ← 32x32 transparent outline icon
```

**Key insight**: The Teams app package is the universal deployment vehicle for ALL M365 extensions — not just Teams. Once uploaded, the Declarative Agent appears in:
- Model-driven apps (MDA) — Copilot side pane
- Teams — Copilot chat
- Outlook — Copilot side pane
- Copilot Chat web (copilot.microsoft.com)

---

## File-by-File Reference

### manifest.json

```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/teams/vDevPreview/MicrosoftTeams.schema.json",
  "manifestVersion": "devPreview",
  "version": "1.0.1",
  "id": "<YOUR-ENTRA-APP-ID>",
  "developer": { "name": "...", "websiteUrl": "...", "privacyUrl": "...", "termsOfUseUrl": "..." },
  "name": { "short": "Spaarke AI", "full": "..." },
  "description": { "short": "...", "full": "..." },
  "icons": { "color": "color.png", "outline": "outline.png" },
  "accentColor": "#4F6BED",
  "copilotAgents": {
    "declarativeAgents": [
      { "id": "spaarkeAiAgent", "file": "declarativeAgent.json" }
    ]
  },
  "permissions": ["identity", "messageTeamMembers"],
  "validDomains": ["your-api.azurewebsites.net"]
}
```

**Lessons learned:**
- `manifestVersion` MUST be `"devPreview"` — the `copilotAgents` property is not in GA schema yet (v1.19 doesn't support it)
- `id` MUST be a real GUID (your Entra app registration ID) — NOT a placeholder like `${{TEAMS_APP_ID}}`
- `version` must be incremented for each update upload — Teams Admin Center rejects same-version updates

### declarativeAgent.json

```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/copilot/declarative-agent/v1.2/schema.json",
  "version": "v1.2",
  "name": "Spaarke AI",
  "description": "...",
  "instructions": "<system prompt — max 8000 characters>",
  "conversation_starters": [
    { "title": "Overdue tasks", "text": "What are my overdue tasks?" }
  ],
  "actions": [
    { "id": "spaarkeApiPlugin", "file": "spaarke-api-plugin.json" }
  ]
}
```

**Lessons learned:**
- `instructions` has a **hard limit of 8000 characters** — validation fails silently with "InvalidDeclarativeCopilotDocument" if exceeded
- Use `"actions"` (NOT `"capabilities"`) to reference the API plugin — the schema changed
- Each `action` has `id` and `file` (NOT `name`, `description`, `plugin_file`)

### spaarke-api-plugin.json

```json
{
  "$schema": "https://developer.microsoft.com/json-schemas/copilot/plugin/v2.2/schema.json",
  "schema_version": "v2.2",
  "name_for_human": "Spaarke Legal AI",
  "description_for_human": "...",
  "description_for_model": "...",
  "namespace": "spaarke",
  "runtimes": [
    {
      "type": "OpenApi",
      "auth": { "type": "None" },
      "spec": { "url": "spaarke-bff-openapi.yaml" },
      "run_for_functions": ["functionName1", "functionName2"]
    }
  ],
  "functions": [
    { "name": "functionName1", "description": "Call this when..." }
  ]
}
```

**Lessons learned:**
- Use `"runtimes"` array with `spec.url` — NOT `"api"` at root level (old schema)
- Use `"auth"` inside `runtimes` — NOT at root level
- Functions have ONLY `name` and `description` — NO `operationId` field (the `name` IS the operationId match to the OpenAPI spec)
- `description_for_human` must be **under 100 characters**
- `run_for_functions` must list ALL function names that use this runtime

### spaarke-bff-openapi.yaml

Standard OpenAPI 3.0 spec. Each path operation must have an `operationId` that matches a function `name` in the API plugin.

**Lessons learned:**
- Every `operationId` in the OpenAPI spec must exactly match a function `name` in the plugin — mismatches cause "Function name does not match any operationId" errors
- The `servers` URL must be your actual deployed API endpoint

### Icons

| Icon | Requirements |
|------|-------------|
| **color.png** | 192x192 pixels, any background color |
| **outline.png** | 32x32 pixels, **MUST have transparent background** (ARGB with alpha=0) |

**Lessons learned:**
- Teams validates outline icon transparency at the pixel level
- `System.Drawing.Graphics.Clear(Color.Transparent)` does NOT produce true transparency in all cases
- Must create bitmap with `PixelFormat.Format32bppArgb` and explicitly set pixels to `ARGB(0,0,0,0)`
- The error message shows the actual ARGB values of a non-transparent pixel — helpful for debugging

---

## Deployment Steps

### Prerequisites
- Azure subscription with BFF API deployed
- Entra ID app registration
- M365 tenant with Copilot licenses
- Power Platform environment with Copilot enabled

### Step 1: Create Entra App Registration

```powershell
az ad app create --display-name "Spaarke Copilot Bot Dev" --sign-in-audience AzureADMyOrg
```

Note the `appId` — used throughout.

### Step 2: Deploy Azure Bot Service

```powershell
az deployment group create `
  --resource-group spe-infrastructure-westus2 `
  --template-file infrastructure/bot-service/main.bicep `
  --parameters infrastructure/bot-service/parameters.dev.json `
  --parameters appId=<YOUR-APP-ID> location=global enableManagedIdentity=false
```

**Critical:**
- `location` MUST be `global` — Bot Service doesn't support regional locations
- `enableManagedIdentity` MUST be `false` — Bot Service doesn't support SystemAssigned identity
- PowerShell uses backtick `` ` `` for line continuation, NOT backslash `\`

### Step 3: Deploy BFF API

```powershell
# Build
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o ./publish

# Package
Compress-Archive -Path './publish/*' -DestinationPath './bff-deploy.zip'

# Deploy via Kudu (most reliable for large packages)
$creds = az webapp deployment list-publishing-credentials `
  --resource-group spe-infrastructure-westus2 `
  --name spe-api-dev-67e2xz `
  --query "[publishingUserName,publishingPassword]" -o tsv

curl -X POST "https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/zipdeploy" `
  -u "$user`:$pass" --data-binary @bff-deploy.zip -H "Content-Type: application/zip"
```

**Lesson learned:** `az webapp deploy` may fail with `ConnectionResetError` for large packages (~64MB). Direct curl to Kudu's `/api/zipdeploy` is more reliable.

Verify:
```
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping         → "pong"
curl -s -o /dev/null -w "%{http_code}" .../api/agent/playbooks  → 401
```

### Step 4: Configure App Service Settings

Add these in Azure Portal → App Service → Environment variables:

| Setting | Value |
|---------|-------|
| `AgentToken__TenantId` | Your tenant ID |
| `AgentToken__ClientId` | BFF API app ID (from AzureAd__ClientId) |
| `AgentToken__ClientSecret` | BFF API secret (from Key Vault) |
| `AgentToken__AgentAppId` | Bot/Copilot app ID (from Step 1) |
| `AgentToken__DataverseEnvironmentUrl` | `https://yourorg.crm.dynamics.com` |
| `AgentToken__CacheTtlMinutes` | `55` |
| `CopilotAgent__EnableDocumentSearch` | `true` |
| `CopilotAgent__EnablePlaybookInvocation` | `true` |
| `CopilotAgent__EnableEmailDrafting` | `true` |

**Tip:** Find values with:
```powershell
# BFF API Client ID
az webapp config appsettings list ... --output tsv | grep AzureAd__ClientId

# Client Secret
az keyvault secret show --vault-name spaarke-spekvcert --name BFF-API-ClientSecret --query value -o tsv
```

### Step 5: Package the Declarative Agent

Ensure all files are in a flat ZIP (no subdirectories):
```
manifest.json
declarativeAgent.json
spaarke-api-plugin.json
spaarke-bff-openapi.yaml
color.png (192x192)
outline.png (32x32, transparent)
```

Replace any placeholders in manifest.json (`${{TEAMS_APP_ID}}` → actual GUID).

```powershell
Compress-Archive -Path 'path/to/package/*' -DestinationPath 'spaarke-copilot-agent.zip'
```

### Step 6: Upload to Teams Admin Center

1. Go to: https://admin.teams.microsoft.com/policies/manage-apps
2. Click **"Upload new app"** (first time) or find the app and click **"Upload file"** (updates)
3. Select your ZIP
4. Verify: app shows as "Available to Everyone (org-wide default)"

**For updates:** increment `version` in manifest.json (e.g., `1.0.0` → `1.0.1`)

### Step 7: Configure SSO in Teams Developer Portal

1. Go to: https://dev.teams.microsoft.com
2. Find your app → **Single sign-on**
3. Set **Application (client) ID**: your BFF API app ID
4. Set **Application ID URI**: `api://<BFF-API-APP-ID>`
5. Save

### Step 8: Verify in MDA

1. Ensure Copilot is enabled: Power Platform Admin Center → Environment → Settings → Features → Copilot ON
2. Open a model-driven app
3. Open Copilot side pane
4. Select "Spaarke AI"
5. Try: "What are my overdue tasks?"

---

## Common Errors and Fixes

| Error | Cause | Fix |
|-------|-------|-----|
| "We can't read the manifest file" | Invalid JSON or schema mismatch | Validate all JSON files; use `devPreview` manifest version |
| "Unrecognized member 'api'" | Wrong API plugin schema | Use `runtimes` array, not `api` at root |
| "Unrecognized member 'operationId'" | Functions shouldn't have operationId | Remove `operationId`; `name` IS the operationId |
| "TooLongInstructions" | Instructions > 8000 chars | Trim instructions in declarativeAgent.json |
| "Function name does not match any operationId" | Plugin function names don't match OpenAPI | Ensure each function `name` matches an `operationId` in the YAML |
| "Outline icon is not transparent" | Icon background has non-zero alpha | Create with `Format32bppArgb`, set pixels to `ARGB(0,0,0,0)` |
| "LocationNotAvailableForResourceType" | Bot Service in wrong region | Use `location=global` |
| "CannotSetResourceIdentity" | Bot Service doesn't support managed identity | Set `enableManagedIdentity=false` |
| "This update needs a new version number" | Same version as existing app | Increment `version` in manifest.json |
| 404 on agent endpoints | Code not deployed or endpoints not registered | Check `MapAgentEndpoints()` in EndpointMappingExtensions.cs; redeploy |
| ConnectionResetError on deploy | Large zip upload to Kudu fails | Use direct curl to `/api/zipdeploy` instead of `az webapp deploy` |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-03-26 | Initial upload — schema validated, agent visible in MDA |
| 1.0.1 | 2026-03-26 | Auth switched to None for initial testing, icon transparency fixed |

---

*This guide reflects actual deployment experience. All errors documented were encountered and resolved during the initial setup.*
