```markdown
# Authentication Azure Resources & GUIDs

> **Source**: AUTHENTICATION-ARCHITECTURE.md
> **Last Updated**: December 4, 2025
> **Applies To**: Debugging, deployment, configuration lookup

---

## TL;DR

Quick reference for all Azure resource IDs, app registration GUIDs, and configuration values used in SDAP authentication. Use this when debugging auth issues or configuring new environments.

---

## Applies When

- Debugging authentication errors (need to verify correct IDs)
- Deploying to new environment
- Configuring app settings
- Checking which app registration to modify
- Verifying token audiences/issuers

---

## Azure AD App Registrations

### Dataverse App (PCF Client)

| Property | Value |
|----------|-------|
| **Application (client) ID** | `5175798e-f23e-41c3-b09b-7a90b9218189` |
| **Display Name** | Dataverse App (or similar) |
| **Platform** | Single-page application (SPA) |
| **Purpose** | PCF control acquires tokens for BFF |

**Redirect URIs**:
```
https://spaarkedev1.crm.dynamics.com
http://localhost:8181  (dev only)
```

**API Permissions** (Delegated):
- Microsoft Graph: `User.Read`, `offline_access`
- BFF API: `user_impersonation`

---

### BFF API App (Middle-Tier)

| Property | Value |
|----------|-------|
| **Application (client) ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Application ID URI** | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Display Name** | SPE BFF API (or similar) |
| **Platform** | Web |
| **Purpose** | Validates user tokens, performs OBO, connects to Dataverse |

**Exposed API Scopes**:
```
api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
```

**API Permissions** (Delegated):
- Microsoft Graph: `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `User.Read`
- Dynamics CRM: `user_impersonation`

**Known Client Applications** (for consent propagation):
```json
"knownClientApplications": ["5175798e-f23e-41c3-b09b-7a90b9218189"]
```

---

## Azure AD Tenant

| Property | Value |
|----------|-------|
| **Tenant ID** | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| **Authority** | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0` |
| **Token Issuer** | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0` |

---

## Azure Resources

### App Service (BFF API)

| Property | Value |
|----------|-------|
| **Name** | `spe-api-dev-67e2xz` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | West US 2 |
| **URL** | `https://spe-api-dev-67e2xz.azurewebsites.net` |

**View Logs**:
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

---

### Application Insights

| Property | Value |
|----------|-------|
| **Name** | `spe-insights-dev-67e2xz` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Connection String** | In App Service configuration |

**Query Auth Failures**:
```kusto
requests
| where resultCode == 401 or resultCode == 403
| project timestamp, name, resultCode, duration
| order by timestamp desc
```

---

### Key Vault

| Property | Value |
|----------|-------|
| **Name** | `spaarke-spekvcert` |
| **Resource Group** | `SharePointEmbedded` |
| **Purpose** | Stores BFF API secrets including certificates and AI keys |

**Key Vault Secrets**:
| Secret Name | Purpose |
|-------------|---------|
| `ai-openai-endpoint` | Azure OpenAI endpoint URL |
| `ai-openai-key` | Azure OpenAI API key |

**Secret Reference** (in App Service):
```
@Microsoft.KeyVault(SecretUri=https://spe-kv-dev-67e2xz.vault.azure.net/secrets/API-CLIENT-SECRET/)
```

---

### Azure OpenAI

| Property | Value |
|----------|-------|
| **Name** | `spaarke-openai-dev` |
| **Resource Group** | `spe-infrastructure-westus2` |
| **Region** | East US |
| **Endpoint** | `https://spaarke-openai-dev.openai.azure.com/` |
| **SKU** | S0 (Standard) |

**Model Deployments**:
| Deployment Name | Model | Purpose |
|-----------------|-------|---------|
| `gpt-4o-mini` | gpt-4o-mini (2024-07-18) | Document summarization |

**App Service Settings**:
```
Ai__Enabled=true
Ai__OpenAiEndpoint=https://spaarke-openai-dev.openai.azure.com/
Ai__OpenAiKey=(from Key Vault: ai-openai-key)
Ai__SummarizeModel=gpt-4o-mini
```

---

### Managed Identity

| Property | Value |
|----------|-------|
| **Type** | System-assigned |
| **Principal** | App Service's identity |
| **Purpose** | Access Key Vault secrets |

**Required Role Assignment**:
- Key Vault: `Key Vault Secrets User`

---

## Dataverse Environment

| Property | Value |
|----------|-------|
| **Environment URL** | `https://spaarkedev1.crm.dynamics.com` |
| **Environment Name** | SPAARKE DEV 1 |
| **Application User** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Security Role** | System Administrator |

---

## Configuration Quick Reference

### PCF Control (MSAL.js)

```typescript
const msalConfig = {
    auth: {
        clientId: "5175798e-f23e-41c3-b09b-7a90b9218189",
        authority: "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2",
        redirectUri: "https://spaarkedev1.crm.dynamics.com"
    }
};

const tokenScope = "api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation";
```

### BFF API (appsettings.json)

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    "Audience": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  },
  "Dataverse": {
    "ServiceUrl": "https://spaarkedev1.crm.dynamics.com"
  }
}
```

### BFF API (Environment Variables)

```
API_APP_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c
API_CLIENT_SECRET=@Microsoft.KeyVault(SecretUri=...)
TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
```

---

## GUID Quick Lookup

| What | GUID |
|------|------|
| Tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Dataverse (PCF) App | `5175798e-f23e-41c3-b09b-7a90b9218189` |
| BFF API App | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Graph Resource | `https://graph.microsoft.com` |
| Dataverse Resource | `https://spaarkedev1.crm.dynamics.com` |

---

## Verification Commands

```bash
# Check App Service configuration
az webapp config appsettings list --name spe-api-dev-67e2xz -g spe-infrastructure-westus2

# Verify Key Vault access
az keyvault secret show --vault-name spe-kv-dev-67e2xz --name API-CLIENT-SECRET

# List Dataverse Application Users
pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com

# Test token acquisition (PowerShell)
$token = az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c
```

---

## Related Articles

- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Auth flow implementation
- [auth-security-boundaries.md](auth-security-boundaries.md) - Security zones
- [auth-performance-monitoring.md](auth-performance-monitoring.md) - Monitoring

---

*Extracted from AUTHENTICATION-ARCHITECTURE.md resource inventory and GUID reference*
```
