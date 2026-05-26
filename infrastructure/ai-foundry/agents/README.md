# Spaarke Legal AI Agent — Deployment Guide

This directory contains the Azure AI Foundry agent definition for the **Spaarke Legal AI
Assistant**. The agent is BYOK-compatible: all resource identifiers are environment
variable placeholders so the same definition deploys to any Azure AI Foundry project,
including customer-supplied projects with customer-managed keys.

## Files

| File | Purpose |
|------|---------|
| `spaarke-legal-agent.yaml` | Agent identity, model, system prompt, and tool registrations |
| `bff-function-tools.yaml` | OpenAI function schemas for BFF-side tools (analyze-data, research-legal) |
| `README.md` | This file — deployment instructions and environment variable reference |

---

## Required Environment Variables

Set these before running any deployment command. For local development, create a
`.env.agent` file (gitignored) and source it. For CI/CD, inject as pipeline secrets.

### Foundry Project

| Variable | Description | Dev Value |
|----------|-------------|-----------|
| `FOUNDRY_SUBSCRIPTION_ID` | Azure subscription ID | `484bc857-3802-427f-9ea5-ca47b43db0f0` |
| `FOUNDRY_RESOURCE_GROUP` | Resource group containing the Foundry project | `spe-infrastructure-westus2` |
| `FOUNDRY_PROJECT_NAME` | AI Foundry **project** name (not hub) | `sprkspaarkedev-aif-proj` |
| `FOUNDRY_ENDPOINT` | AI Foundry project endpoint URL | `https://sprkspaarkedev-aif-proj.api.azureml.ms` |
| `FOUNDRY_ENVIRONMENT` | Deployment environment label | `dev` |

### Model

| Variable | Description | Dev Value |
|----------|-------------|-----------|
| `FOUNDRY_MODEL_DEPLOYMENT` | Azure OpenAI deployment name registered in the Foundry project | `gpt-4o-mini` |

> **BYOK note**: For customer projects, set `FOUNDRY_MODEL_DEPLOYMENT` to the customer's
> own deployment name. The model must support function calling and the Assistants API.

### Bing Grounding

| Variable | Description | Dev Value |
|----------|-------------|-----------|
| `FOUNDRY_BING_CONNECTION_NAME` | Name of the Bing Search connection configured in the Foundry project | `bing-grounding-connection` |

> Create the Bing connection first if it doesn't exist — see "Prerequisites" below.

### BFF API (AgentServiceOptions)

These variables are consumed by the BFF at runtime (not by the agent YAML itself), but
are listed here for completeness because they are required before the agent can function.

| Variable | Description | Dev Value |
|----------|-------------|-----------|
| `AGENT_BFF_BASE_URL` | BFF API base URL — used in `AgentServiceOptions:BffBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| `AGENT_ID` | AI Foundry agent ID — populated **after** first deployment; store in `AgentServiceOptions:AgentId` | *(set after deploy)* |

---

## Prerequisites

### 1. Authenticate with Azure

```bash
az login
az account set --subscription "${FOUNDRY_SUBSCRIPTION_ID}"
```

### 2. Install the Azure AI CLI extension (if not present)

```bash
az extension add --name ml
# Verify:
az extension show --name ml --query version -o tsv
```

### 3. Create Bing Grounding Connection (first time only)

The Bing connection must exist in the Foundry project before the agent definition
can reference it. Obtain a Bing Search API key from Azure Portal first.

```bash
az ml connection create \
  --resource-group "${FOUNDRY_RESOURCE_GROUP}" \
  --workspace-name "${FOUNDRY_PROJECT_NAME}" \
  --type bing \
  --name "${FOUNDRY_BING_CONNECTION_NAME}" \
  --api-key "${BING_SEARCH_API_KEY}"
```

Verify the connection:

```bash
az ml connection show \
  --resource-group "${FOUNDRY_RESOURCE_GROUP}" \
  --workspace-name "${FOUNDRY_PROJECT_NAME}" \
  --name "${FOUNDRY_BING_CONNECTION_NAME}"
```

---

## Deployment — Create the Agent

### Option A: Bash (Linux/macOS/WSL) using envsubst

```bash
# 1. Export all required environment variables (or source your .env.agent file)
export FOUNDRY_SUBSCRIPTION_ID="484bc857-3802-427f-9ea5-ca47b43db0f0"
export FOUNDRY_RESOURCE_GROUP="spe-infrastructure-westus2"
export FOUNDRY_PROJECT_NAME="sprkspaarkedev-aif-proj"
export FOUNDRY_ENDPOINT="https://sprkspaarkedev-aif-proj.api.azureml.ms"
export FOUNDRY_ENVIRONMENT="dev"
export FOUNDRY_MODEL_DEPLOYMENT="gpt-4o-mini"
export FOUNDRY_BING_CONNECTION_NAME="bing-grounding-connection"

# 2. Substitute placeholders and create the agent
envsubst < infrastructure/ai-foundry/agents/spaarke-legal-agent.yaml \
  | az ai agent create \
      --project-endpoint "${FOUNDRY_ENDPOINT}" \
      --file /dev/stdin \
      --functions-file infrastructure/ai-foundry/agents/bff-function-tools.yaml

# 3. Capture the agent ID from the output
AGENT_ID=$(az ai agent list \
  --project-endpoint "${FOUNDRY_ENDPOINT}" \
  --query "[?name=='spaarke-legal-ai-assistant'].id | [0]" \
  -o tsv)

echo "Agent created with ID: ${AGENT_ID}"
```

### Option B: PowerShell (Windows)

```powershell
# 1. Set environment variables
$env:FOUNDRY_SUBSCRIPTION_ID = "484bc857-3802-427f-9ea5-ca47b43db0f0"
$env:FOUNDRY_RESOURCE_GROUP  = "spe-infrastructure-westus2"
$env:FOUNDRY_PROJECT_NAME    = "sprkspaarkedev-aif-proj"
$env:FOUNDRY_ENDPOINT        = "https://sprkspaarkedev-aif-proj.api.azureml.ms"
$env:FOUNDRY_ENVIRONMENT     = "dev"
$env:FOUNDRY_MODEL_DEPLOYMENT = "gpt-4o-mini"
$env:FOUNDRY_BING_CONNECTION_NAME = "bing-grounding-connection"

# 2. Substitute placeholders (PowerShell string replacement)
$agentYaml = Get-Content infrastructure\ai-foundry\agents\spaarke-legal-agent.yaml -Raw
$agentYaml = $agentYaml `
  -replace '\$\{FOUNDRY_MODEL_DEPLOYMENT\}',    $env:FOUNDRY_MODEL_DEPLOYMENT `
  -replace '\$\{FOUNDRY_BING_CONNECTION_NAME\}', $env:FOUNDRY_BING_CONNECTION_NAME `
  -replace '\$\{FOUNDRY_ENVIRONMENT\}',          $env:FOUNDRY_ENVIRONMENT

$tempFile = [System.IO.Path]::GetTempFileName() + ".yaml"
$agentYaml | Set-Content $tempFile -Encoding UTF8

# 3. Create the agent
az ai agent create `
  --project-endpoint $env:FOUNDRY_ENDPOINT `
  --file $tempFile `
  --functions-file infrastructure\ai-foundry\agents\bff-function-tools.yaml

Remove-Item $tempFile

# 4. Retrieve and store the agent ID
$agentId = az ai agent list `
  --project-endpoint $env:FOUNDRY_ENDPOINT `
  --query "[?name=='spaarke-legal-ai-assistant'].id | [0]" `
  -o tsv

Write-Host "Agent created with ID: $agentId"
```

### Option C: Automated script (recommended for CI/CD)

Use the deploy script once it is created:

```powershell
scripts/Deploy-FoundryAgent.ps1 -Environment dev
```

> This script wraps the PowerShell steps above, reads `.env.agent` or Key Vault
> secrets, performs substitution, creates/updates the agent, and writes the
> resulting agent ID to the BFF App Service configuration.

---

## Store the Agent ID

After deployment, store the agent ID in the BFF App Service configuration under
`AgentServiceOptions:AgentId`. The BFF reads this value to target the correct agent.

```bash
# App Service environment variable (Azure CLI)
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings "AgentServiceOptions__AgentId=${AGENT_ID}"
```

Or set it via Azure Portal: App Service → Environment variables → `AgentServiceOptions__AgentId`.

---

## Update the Agent

To update an existing agent (e.g., after changing the system prompt or tools):

```bash
# Bash
AGENT_ID=$(az ai agent list \
  --project-endpoint "${FOUNDRY_ENDPOINT}" \
  --query "[?name=='spaarke-legal-ai-assistant'].id | [0]" \
  -o tsv)

envsubst < infrastructure/ai-foundry/agents/spaarke-legal-agent.yaml \
  | az ai agent update \
      --project-endpoint "${FOUNDRY_ENDPOINT}" \
      --agent-id "${AGENT_ID}" \
      --file /dev/stdin \
      --functions-file infrastructure/ai-foundry/agents/bff-function-tools.yaml
```

---

## Verify Deployment

```bash
# List all agents in the project
az ai agent list \
  --project-endpoint "${FOUNDRY_ENDPOINT}" \
  --query "[].{name:name, id:id, model:model}" \
  -o table

# Show agent details
az ai agent show \
  --project-endpoint "${FOUNDRY_ENDPOINT}" \
  --agent-id "${AGENT_ID}"
```

Expected output includes:
- `name`: `spaarke-legal-ai-assistant`
- `model`: value of `${FOUNDRY_MODEL_DEPLOYMENT}` (e.g. `gpt-4o-mini`)
- `tools`: list including `code_interpreter` and `bing_grounding`

---

## BYOK Customer Deployment

For a customer-owned Foundry project:

1. Set `FOUNDRY_PROJECT_NAME`, `FOUNDRY_ENDPOINT`, and `FOUNDRY_RESOURCE_GROUP` to the
   customer's values.
2. Set `FOUNDRY_MODEL_DEPLOYMENT` to the model deployment name in the customer's
   Azure OpenAI resource (customer-managed key).
3. Create the Bing connection in the customer's project and set
   `FOUNDRY_BING_CONNECTION_NAME` to match.
4. Run the deployment steps above — **no file changes required**.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| `ResourceNotFound` on agent create | Foundry endpoint or project name wrong | Verify `FOUNDRY_ENDPOINT` and `FOUNDRY_PROJECT_NAME` |
| `ConnectionNotFound` for Bing | Bing connection not created in project | Run the `az ml connection create` command in Prerequisites |
| `ModelNotFound` | Model deployment name doesn't exist in the project's OpenAI connection | Create the deployment: `az cognitiveservices account deployment create ...` |
| Agent created but BFF can't reach it | `AgentServiceOptions__AgentId` not set in App Service | Set it via `az webapp config appsettings set` |
| System prompt truncated | Instructions exceed 8000 characters | Trim the `instructions` block in `spaarke-legal-agent.yaml` |

---

## Related Files

| Path | Description |
|------|-------------|
| `infrastructure/ai-foundry/README.md` | AI Foundry hub and project overview |
| `infrastructure/ai-foundry/connections/` | Foundry connection definitions |
| `docs/architecture/auth-AI-azure-resources.md` | Azure OpenAI and AI Foundry resource reference |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Agent/` | BFF AgentService implementation |
