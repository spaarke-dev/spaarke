# Spaarke AI in Microsoft 365 Copilot — Admin Guide

> **Last Updated**: March 26, 2026
> **Purpose**: Deployment, configuration, monitoring, and troubleshooting for the Spaarke AI M365 Copilot integration.
> **Audience**: IT administrators, DevOps engineers, system administrators

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Architecture Overview](#architecture-overview)
- [Deployment Steps](#deployment-steps)
- [Configuration](#configuration)
- [Managing Playbook Visibility](#managing-playbook-visibility)
- [BYOK Deployment (Customer-Hosted)](#byok-deployment-customer-hosted)
- [Monitoring and Telemetry](#monitoring-and-telemetry)
- [Troubleshooting](#troubleshooting)
- [Security Considerations](#security-considerations)
- [Reference](#reference)

---

## Prerequisites

### Licensing

| Requirement | Details |
|-------------|---------|
| **Microsoft 365 Copilot licenses** | Required for all users who will access Spaarke AI in the Copilot side pane |
| **Power Apps licenses** | Users must have access to the Spaarke model-driven app |
| **Spaarke platform license** | Active Spaarke subscription with AI capabilities enabled |

### Azure Resources

| Resource | Purpose |
|----------|---------|
| **Entra ID (Azure AD) app registration** | OAuth 2.0 authentication for the Spaarke agent — enables SSO token flow |
| **Azure Bot Service** | Routes M365 Copilot activities to the Spaarke BFF API |
| **BFF API (App Service)** | Backend API that handles all agent requests (`spe-api-dev-67e2xz`) |
| **Azure OpenAI** | AI model inference for playbook execution and email drafting |
| **Azure AI Search** | Semantic document search index |

### Admin Permissions

- **Microsoft 365 admin** — To approve the agent in the org app catalog
- **Azure AD admin** — To create or consent to the Entra app registration
- **Power Platform admin** — To manage the Spaarke model-driven app
- **Azure subscription contributor** — To manage Bot Service and infrastructure resources

---

## Architecture Overview

```
M365 Copilot Side Pane (MDA)
        |
        v
Declarative Agent (declarativeAgent.json)
        |
        ├── API Plugin (spaarke-api-plugin.json)
        |       |
        |       v
        |   BFF API (/api/agent/*)
        |       |
        |       ├── Document Search (SPE via Graph)
        |       ├── Playbook Execution (Azure OpenAI)
        |       ├── Matter Queries (Dataverse)
        |       └── Email Drafting (Communications module)
        |
        └── Azure Bot Service
                |
                v
            SpaarkeAgentHandler (M365 Agents SDK)
                |
                v
            BFF API (agent gateway adapter endpoints)
```

**Key principle**: Agent endpoints are thin adapters over existing BFF services. No new AI logic is introduced — the BFF API remains the single orchestration layer.

---

## Deployment Steps

### Step 1: Register the Entra App

1. In the [Azure portal](https://portal.azure.com), navigate to **Entra ID > App registrations > New registration**.
2. Configure the registration:

   | Setting | Value |
   |---------|-------|
   | Name | `Spaarke M365 Copilot Agent` |
   | Supported account types | Accounts in this organizational directory only (single tenant) |
   | Redirect URI | `https://token.botframework.com/.auth/web/redirect` |

3. Under **API permissions**, add the following delegated permissions:
   - `User.Read`
   - `Files.Read.All` (for SPE document access via Graph)
   - `Sites.Read.All` (for SPE container access)
   - Grant admin consent for the organization.

4. Under **Certificates & secrets**, create a new client secret. Record the **Application (client) ID** and **Client secret** for Bot Service configuration.

5. Under **Expose an API**, set the Application ID URI (e.g., `api://{client-id}`) and add the `access_as_user` scope for SSO.

[Screenshot: Entra app registration with API permissions configured]

### Step 2: Register Azure Bot Service

1. In the Azure portal, create a new **Azure Bot** resource.

   | Setting | Value |
   |---------|-------|
   | Bot handle | `spaarke-copilot-agent` |
   | Pricing tier | Standard |
   | Microsoft App ID | Use the Entra app registration client ID from Step 1 |
   | Messaging endpoint | `https://spe-api-dev-67e2xz.azurewebsites.net/api/agent/message` |

2. Under **Channels**, enable:
   - **Microsoft Teams** — For development and testing
   - **M365 Extensions** — For Copilot integration in model-driven apps

3. Under **Configuration > OAuth Connection Settings**, add an OAuth connection:

   | Setting | Value |
   |---------|-------|
   | Name | `SpaarkeAuth` |
   | Service Provider | Azure Active Directory v2 |
   | Client ID | Entra app client ID |
   | Client secret | Entra app client secret |
   | Tenant ID | Your Azure AD tenant ID |
   | Scopes | `User.Read Files.Read.All Sites.Read.All` |

[Screenshot: Azure Bot Service configuration with messaging endpoint and OAuth connection]

### Step 3: Deploy Agent Manifest Files

The agent is defined by three manifest files:

| File | Location | Purpose |
|------|----------|---------|
| `declarativeAgent.json` | `src/solutions/copilot-agent/` | Agent identity, instructions, conversation starters |
| `spaarke-api-plugin.json` | `src/solutions/copilot-agent/` | Function definitions mapping to BFF API endpoints |
| `spaarke-bff-openapi.yaml` | `src/solutions/copilot-agent/` | OpenAPI spec exposing the BFF API surface |

**Deploy using M365 Agents Toolkit:**

1. Open the project in Visual Studio Code with the M365 Agents Toolkit extension installed.
2. Sign in to your M365 tenant.
3. Run **Provision** to register the agent.
4. Run **Deploy** to push the manifest files.
5. In the [Microsoft 365 admin center](https://admin.microsoft.com), navigate to **Settings > Integrated apps**.
6. Find **Spaarke AI** and approve it for the organization (or specific user groups).

[Screenshot: M365 admin center showing Spaarke AI agent in the Integrated apps list]

### Step 4: Configure BFF API

Ensure the BFF API has the required configuration for agent endpoints:

```json
{
  "Agent": {
    "BotId": "<azure-bot-service-app-id>",
    "BotPassword": "<azure-bot-service-client-secret>",
    "TenantId": "<azure-ad-tenant-id>",
    "OAuthConnectionName": "SpaarkeAuth"
  }
}
```

These values should be stored in Azure Key Vault and referenced via App Service configuration:

| App Setting | Key Vault Reference |
|-------------|-------------------|
| `Agent__BotId` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=agent-bot-id)` |
| `Agent__BotPassword` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=agent-bot-password)` |
| `Agent__TenantId` | `@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=agent-tenant-id)` |

### Step 5: Validate the Deployment

1. Open the Spaarke model-driven app as an end user with a Copilot license.
2. Click the Copilot icon to open the side pane.
3. Verify the Spaarke AI agent appears with conversation starters.
4. Test a basic query: "What are my overdue tasks?"
5. Test document search: "Find the NDA for [known matter]"

[Screenshot: Copilot side pane showing successful Spaarke AI agent response]

---

## Configuration

### BFF API Endpoint

The agent communicates with the BFF API at the configured messaging endpoint. For different environments:

| Environment | BFF API URL |
|-------------|-------------|
| Development | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Staging | Configure per deployment |
| Production | Configure per deployment |

Update the Bot Service messaging endpoint if the BFF API URL changes.

### Agent Behavior

The Declarative Agent manifest (`declarativeAgent.json`) controls:

- **System instructions** — Defines the agent's personality, scope, and behavioral rules
- **Conversation starters** — Suggested prompts shown when the Copilot pane opens
- **Capabilities** — Which API plugin functions are available

To modify agent behavior, edit the manifest file and redeploy via M365 Agents Toolkit.

### Adaptive Card Schema

All Adaptive Card templates use schema version 1.5 (the maximum supported by M365 Copilot). Card templates are located in the BFF API and are rendered server-side by the `AdaptiveCardFormatterService`.

---

## Managing Playbook Visibility

Administrators can control which AI analysis playbooks are available through the Copilot agent.

### Enabling or Disabling Playbooks

1. Open the **Spaarke** model-driven app as an administrator.
2. Navigate to **Administration > AI Configuration > Playbook Visibility**.
3. For each playbook, toggle the **Available in Copilot** setting:
   - **On** — Playbook appears in Copilot search and playbook menus
   - **Off** — Playbook is hidden from Copilot but remains available in the Analysis Workspace

[Screenshot: Playbook visibility configuration showing toggle for each playbook]

### Per-Role Restrictions

Playbook visibility can be scoped to security roles:

| Configuration | Effect |
|---------------|--------|
| **All users** | Any user with Copilot access sees the playbook |
| **Specific roles** | Only users with the assigned security role see the playbook |
| **Disabled** | Playbook is not available in Copilot for any user |

To configure per-role restrictions:

1. In **AI Configuration > Playbook Visibility**, select a playbook.
2. Under **Role Restrictions**, add or remove security roles.
3. Save changes. The update takes effect on the next Copilot interaction (no restart required).

### Playbook Timeout Configuration

For playbooks that may exceed the API plugin response timeout:

| Setting | Default | Description |
|---------|---------|-------------|
| **Quick playbook threshold** | 30 seconds | Playbooks under this limit return results inline |
| **Long playbook behavior** | Deep-link | Playbooks over the threshold return a link to the Analysis Workspace |

Adjust these thresholds in **AI Configuration > Agent Settings**.

---

## BYOK Deployment (Customer-Hosted)

For customers who require their own Azure infrastructure (data residency, compliance, or isolation requirements), Spaarke provides Bicep deployment templates.

### What BYOK Deploys

| Resource | Purpose |
|----------|---------|
| Azure App Service | Hosts the BFF API |
| Azure OpenAI | AI model inference |
| Azure AI Search | Semantic document search |
| Azure Bot Service | M365 Copilot channel routing |
| Azure Key Vault | Secret management |
| Application Insights | Monitoring and telemetry |

### Deployment Steps

1. Clone the BYOK templates from `infrastructure/byok/`.
2. Configure the parameter file with customer-specific values:

   ```bash
   az deployment group create \
     --resource-group <customer-rg> \
     --template-file infrastructure/byok/main.bicep \
     --parameters infrastructure/byok/parameters.customer.json
   ```

3. After deployment, configure the Entra app registration and Bot Service as described in the standard deployment steps above.
4. Deploy the BFF API to the customer's App Service.
5. Update the Declarative Agent manifest with the customer's BFF API endpoint.
6. Deploy the agent to the customer's M365 tenant via M365 Agents Toolkit.

### BYOK Configuration Parameters

| Parameter | Description |
|-----------|-------------|
| `location` | Azure region for all resources |
| `environmentPrefix` | Naming prefix (e.g., `contoso-spaarke`) |
| `openAiModelDeployment` | GPT model deployment name |
| `searchServiceSku` | AI Search tier (basic, standard, etc.) |
| `appServicePlanSku` | App Service plan tier |
| `tenantId` | Customer's Azure AD tenant ID |

---

## Monitoring and Telemetry

### Application Insights Dashboards

The Spaarke agent logs telemetry to Application Insights. Key metrics to monitor:

| Metric | What It Measures | Where to Find It |
|--------|------------------|-----------------|
| **Agent interactions** | Total Copilot conversations and messages | Custom Events > `AgentInteraction` |
| **Playbook invocations** | Playbook runs triggered from Copilot | Custom Events > `PlaybookInvocation` |
| **Handoff events** | Users clicking "Open in Analysis Workspace" | Custom Events > `WorkspaceHandoff` |
| **Response latency** | End-to-end response time for agent queries | Request metrics > `/api/agent/*` |
| **Error rate** | Failed agent requests | Failures > `/api/agent/*` |
| **Token usage** | Azure OpenAI token consumption from agent queries | Custom Metrics > `AiTokensUsed` |

### Setting Up Alerts

Recommended alert rules:

| Alert | Condition | Severity |
|-------|-----------|----------|
| High error rate | Agent endpoint failure rate > 5% over 5 minutes | Warning |
| Slow responses | P95 latency > 10 seconds for `/api/agent/*` | Warning |
| Auth failures | > 10 `401` responses in 5 minutes | Critical |
| Playbook timeout spike | > 20% of playbook invocations timeout in 15 minutes | Warning |

Configure alerts in the Azure portal under **Application Insights > Alerts > New alert rule**.

### Log Queries (KQL)

**Agent interaction volume by type:**
```kql
customEvents
| where name == "AgentInteraction"
| summarize count() by tostring(customDimensions.InteractionType), bin(timestamp, 1h)
| render timechart
```

**Playbook invocation success rate:**
```kql
customEvents
| where name == "PlaybookInvocation"
| summarize
    total = count(),
    succeeded = countif(tostring(customDimensions.Status) == "Completed"),
    failed = countif(tostring(customDimensions.Status) == "Failed")
    by bin(timestamp, 1h)
| extend successRate = round(100.0 * succeeded / total, 1)
```

> **Important**: Telemetry does not log document content, prompts, or AI model output per data governance policy (ADR-015). Only identifiers, timings, and interaction types are recorded.

---

## Troubleshooting

### Authentication Failures

**Symptom**: Users see "Unable to authenticate" or "Sign-in required" errors in the Copilot pane.

| Possible Cause | Resolution |
|----------------|------------|
| Entra app registration missing permissions | Verify `User.Read`, `Files.Read.All`, `Sites.Read.All` delegated permissions are granted with admin consent |
| Bot Service OAuth connection misconfigured | Check the OAuth connection name matches `Agent__OAuthConnectionName` in BFF API config |
| Client secret expired | Rotate the client secret in Entra app registration and update Key Vault |
| Tenant ID mismatch | Verify the tenant ID in Bot Service, Entra app, and BFF API config all match |
| User lacks Copilot license | Confirm the user has an active M365 Copilot license assigned |

**Diagnostic steps:**

1. Check Application Insights for `401` or `403` errors on `/api/agent/*` endpoints.
2. Verify the Bot Service messaging endpoint is reachable: `curl https://<bff-url>/healthz`.
3. Test the OAuth connection in the Azure portal under **Bot Service > Configuration > Test Connection**.

### Adaptive Card Rendering Issues

**Symptom**: Cards appear blank, malformed, or show raw JSON in the Copilot pane.

| Possible Cause | Resolution |
|----------------|------------|
| Card schema version exceeds 1.5 | Verify all templates use Adaptive Card schema 1.5 or lower |
| Unsupported action type | `Action.OpenUrl` is not supported for Custom Engine Agents — use `Action.Submit` or text-based deep links |
| Card payload too large | Reduce card content; paginate large result sets |
| Template binding errors | Check BFF API logs for `AdaptiveCardFormatterService` errors |

### Agent Not Appearing in Copilot

**Symptom**: The Spaarke AI agent does not appear in the Copilot side pane.

| Possible Cause | Resolution |
|----------------|------------|
| Agent not approved in admin center | Go to M365 admin center > Integrated apps and approve the Spaarke AI agent |
| Agent not deployed to org catalog | Re-run M365 Agents Toolkit Deploy step |
| User not in assigned group | If the agent is scoped to specific groups, add the user |
| Copilot not enabled for the MDA | Ensure M365 Copilot is enabled for model-driven apps in Power Platform admin center |

### Playbook Timeout Errors

**Symptom**: Playbook results never appear, or the user sees a timeout message.

| Possible Cause | Resolution |
|----------------|------------|
| Playbook exceeds API plugin timeout | Configure the playbook as "long-running" so it returns a deep-link instead of inline results |
| BFF API under heavy load | Check App Service scaling; review Application Insights for CPU/memory pressure |
| Azure OpenAI rate limits | Check Azure OpenAI metrics for throttling (HTTP 429); consider increasing TPM quota |
| Upstream service unavailable | Check BFF API health endpoint (`/healthz`) and dependent service status |

### Document Search Returns No Results

**Symptom**: User searches for a document but gets "No documents found."

| Possible Cause | Resolution |
|----------------|------------|
| User lacks authorization | User must have access to the matter or project containing the document |
| Document not indexed | Check Azure AI Search index status; re-index if needed |
| Search query too vague | Advise users to include specific terms (document name, matter, type) |
| BFF API search endpoint error | Check Application Insights for errors on search endpoints |

---

## Security Considerations

### Authorization Model

- All document access flows through the BFF API with per-matter and per-project authorization.
- SPE containers are configured with `discoverabilityDisabled = true` — Copilot cannot access documents directly.
- The agent never caches document content. All queries are executed in real-time with the user's delegated permissions.
- Tenant isolation is enforced at every layer: AI Search queries include a `tenantId` filter, and SPE containers are scoped per tenant.

### Data Governance

- Document content and AI prompts are never logged (ADR-015).
- Only interaction identifiers, timings, and result counts are recorded in telemetry.
- The agent does not store conversation history beyond the active session.
- ProblemDetails error responses never include document content or prompt text (ADR-019).

### Token Flow

```
User (M365 Copilot)
    → SSO token (Entra ID)
        → OBO exchange (BFF API)
            → Graph API token (SPE document access)
            → Dataverse token (entity queries)
```

The OBO (On-Behalf-Of) token exchange ensures that the agent operates with the user's delegated permissions. No service-level access is used for document retrieval.

### Rate Limiting

All agent gateway endpoints are rate-limited per ADR-016:

| Endpoint | Rate Limit |
|----------|------------|
| `/api/agent/message` | Per-user, per-minute throttle |
| `/api/agent/run-playbook` | Per-user concurrency limit |
| `/api/agent/playbooks` | Standard API rate limits |

Rate limit responses return HTTP 429 with a `Retry-After` header. The agent translates these into user-friendly messages.

### Secret Management

| Secret | Storage |
|--------|---------|
| Bot Service app password | Azure Key Vault |
| Entra app client secret | Azure Key Vault |
| Azure OpenAI API key | Azure Key Vault (managed identity preferred) |
| AI Search admin key | Azure Key Vault (managed identity preferred) |

Rotate secrets on a regular schedule per your organization's policy. See [Secret Rotation Procedures](./SECRET-ROTATION-PROCEDURES.md) for step-by-step instructions.

---

## Reference

| Document | Description |
|----------|-------------|
| [M365 Copilot User Guide](./M365-COPILOT-USER-GUIDE.md) | End-user guide for using Spaarke AI in Copilot |
| [AI Deployment Guide](./AI-DEPLOYMENT-GUIDE.md) | General AI infrastructure deployment |
| [Environment Deployment Guide](./ENVIRONMENT-DEPLOYMENT-GUIDE.md) | Full environment deployment steps |
| [Secret Rotation Procedures](./SECRET-ROTATION-PROCEDURES.md) | Secret rotation runbook |
| [Monitoring and Alerting Guide](./MONITORING-AND-ALERTING-GUIDE.md) | General monitoring setup |
| [Incident Response](./INCIDENT-RESPONSE.md) | Incident response procedures |
| [Project Spec](../../projects/ai-m365-copilot-integration/spec.md) | Full implementation specification |
