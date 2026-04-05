# Authentication Azure Resources & GUIDs

> **Source**: AUTHENTICATION-ARCHITECTURE.md
> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (OpenAI region corrected; config keys aligned with `DocumentIntelligence` section)
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
"knownClientApplications": [
    "5175798e-f23e-41c3-b09b-7a90b9218189",
    "170c98e1-d486-4355-bcbe-170454e0207c"
]
```

---

### Why Two Client App Registrations?

SDAP has two separate SPA app registrations that both acquire tokens for the BFF API:

| App Registration | Client ID | Used By | SPA Redirect URI |
|------------------|-----------|---------|------------------|
| **Dataverse App** (PCF Client) | `5175798e-f23e-41c3-b09b-7a90b9218189` | PCF controls | `https://spaarkedev1.crm.dynamics.com` |
| **DSM-SPE Dev 2** (Code Page Client) | `170c98e1-d486-4355-bcbe-170454e0207c` | Code Pages (HTML web resources) | Code page contexts (Dataverse environment origins) |

Both are listed in `knownClientApplications` on the BFF API app (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) to enable consent propagation.

**Historical reason**: PCF controls were the original SPA client and used the Dataverse App registration with a redirect URI pointing to the Dataverse org URL. When Code Pages were introduced, they required their own app registration because SPA redirect URIs must differ per registration — Code Pages run in a different context (HTML web resources loaded as iframes) and needed separate redirect URI configuration.

**Future**: The planned `@spaarke/auth` shared package will standardize token acquisition across PCF and Code Pages, potentially allowing consolidation to a single app registration.

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
| **Region** | West US 2 (dev — production deploys to `westus3` per `infrastructure/bicep/parameters/platform-prod.bicepparam`) |
| **Endpoint** | `https://spaarke-openai-dev.openai.azure.com/` |
| **SKU** | S0 (Standard) |

**Model Deployments**:
| Deployment Name | Model | Purpose |
|-----------------|-------|---------|
| `gpt-4o-mini` | gpt-4o-mini (2024-07-18) | Document summarization |

**App Service Settings** (bound to `DocumentIntelligenceOptions`, section `DocumentIntelligence`):
```
DocumentIntelligence__Enabled=true
DocumentIntelligence__OpenAiEndpoint=https://spaarke-openai-dev.openai.azure.com/
DocumentIntelligence__OpenAiKey=(from Key Vault: ai-openai-key)
DocumentIntelligence__SummarizeModel=gpt-4o-mini
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

## Client Secrets Lifecycle

### Client Secrets Inventory

Two app registrations have active client secrets used by the BFF API:

| App Registration | Client ID | Secret Prefix | Expires | Purpose |
|------------------|-----------|---------------|---------|---------|
| **BFF API App** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `l8b8Q~J` | 2027-12-18 | OBO token exchange (Graph, Dataverse), Playbook retrieval |
| **DSM-SPE Dev 2** | `170c98e1-d486-4355-bcbe-170454e0207c` | `~Ac8Q~JGnsrv` | 2027-09-22 | Dataverse ServiceClient S2S auth (`AuthType=ClientSecret`) |

### BFF API App Secrets (`1e40baad`)

| Created | Expires | Description | First 5 chars | Used By | Status |
|---------|---------|-------------|---------------|---------|--------|
| 2025-12-18 | 2027-12-18 | Dataverse-Checkout-20251218 | `l8b8Q~J` | Graph (OBO), Dataverse (OBO), Playbooks | ✅ Active |

### DSM-SPE Dev 2 Secrets (`170c98e1`)

| Created | Expires | Description | First 5 chars | Used By | Status |
|---------|---------|-------------|---------------|---------|--------|
| 2025-09-29 | 2027-09-22 | BFF-API-ClientSecret | `~Ac8Q~JGnsrv` | Dataverse ServiceClient (S2S) | ✅ Active |

### Secret Storage Locations

| Environment | Storage Method | Config Key |
|-------------|----------------|------------|
| **Local Dev** | User Secrets (`dotnet user-secrets`) | `API_CLIENT_SECRET` |
| **Azure App Service** | App Settings (Environment Variables) | `API_CLIENT_SECRET` |
| **Production** (future) | Azure Key Vault reference | `@Microsoft.KeyVault(SecretUri=...)` |

### How to Set Secrets

**Local Development**:
```bash
cd src/server/api/Sprk.Bff.Api
dotnet user-secrets set "API_CLIENT_SECRET" "your-secret-value"
```

**Azure App Service**:
```bash
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings API_CLIENT_SECRET="your-secret-value"
```

**Azure Key Vault** (future):
```bash
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name API-CLIENT-SECRET \
  --value "your-secret-value"

# Then reference in App Service:
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings API_CLIENT_SECRET="@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/API-CLIENT-SECRET/)"
```

### Configuration Migration History

| Date | Change | Reason |
|------|--------|--------|
| 2025-12-18 | Created `l8b8Q~...` secret | Document checkout feature deployment |
| 2026-01-07 | Consolidated to `API_CLIENT_SECRET` | Unified Graph, Dataverse, Playbook auth under single secret |

### Where This Secret Is Used

The `API_CLIENT_SECRET` is used by multiple services for service principal authentication:

1. **GraphClientFactory** - Microsoft Graph API calls (SPE operations)
2. **DataverseAccessDataSource** - Dataverse Web API calls (OBO token exchange for authorization)
3. **PlaybookService** - Dataverse Web API calls (playbook retrieval)

All three use the **same app registration** (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) with different scopes:
- Graph: `https://graph.microsoft.com/.default`
- Dataverse: `https://spaarkedev1.crm.dynamics.com/.default`

---

## OBO Token Exchange for Dataverse

**Pattern**: On-Behalf-Of (OBO) authentication allows the BFF API to call Dataverse Web API with the user's permissions, not the service principal's permissions.

### When OBO Is Used

| Service | OBO Usage | Purpose |
|---------|-----------|---------|
| **GraphClientFactory** | ✅ Yes (SPE operations) | Download files from SharePoint Embedded using user's Graph token |
| **DataverseAccessDataSource** | ✅ Yes (authorization checks) | Validate user has Read access to documents via direct query |
| **PlaybookService** | ❌ No (service principal) | Retrieve playbook definitions (system operation, no user context required) |

### OBO Flow for Dataverse Authorization

This is the complete flow for AI Summary/Analysis authorization:

```
1. User authenticates to PCF (MSAL.js)
   → Acquires token for BFF API scope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
   → Token contains user's Azure AD OID claim

2. PCF calls BFF endpoint: POST /api/ai/analysis/execute
   → Headers: Authorization: Bearer {user_bff_token}

3. BFF extracts user token from request
   → TokenHelper.ExtractBearerToken(httpContext)
   → Validates token audience and signature

4. BFF performs OBO exchange for Dataverse token
   → Uses MSAL.NET (Microsoft.Identity.Client)
   → Calls ConfidentialClientApplicationBuilder.AcquireTokenOnBehalfOf()
   → Input: User's BFF token
   → Output: Dataverse token with user's permissions
   → Scopes: https://spaarkedev1.crm.dynamics.com/.default

5. BFF sets Dataverse token on HttpClient
   → _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken)

6. BFF queries Dataverse as the user
   → GET /api/data/v9.2/systemusers?$filter=azureactivedirectoryobjectid eq '{user_oid}'
   → GET /api/data/v9.2/sprk_documents({documentId})?$select=sprk_documentid
   → Both queries execute with user's permissions (row-level security enforced)

7. If queries succeed → User has access
   → Proceed with AI analysis
   → If queries fail (403/404) → User doesn't have access
   → Return 403 to PCF
```

### MSAL Configuration for OBO

**Code Location**: `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`

```csharp
// Build confidential client application
var app = ConfidentialClientApplicationBuilder
    .Create(clientId: "1e40baad-e065-4aea-a8d4-4b7ab273458c")
    .WithClientSecret(clientSecret: API_CLIENT_SECRET)  // l8b8Q~J...
    .WithAuthority(authority: "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2")
    .Build();

// Acquire Dataverse token on behalf of user
var result = await app.AcquireTokenOnBehalfOf(
    scopes: new[] { "https://spaarkedev1.crm.dynamics.com/.default" },
    userAssertion: new UserAssertion(userAccessToken))
    .ExecuteAsync(cancellationToken);

string dataverseToken = result.AccessToken;
```

### Required Azure AD Permissions

For OBO to work, the BFF API app registration **must** have:

**API Permissions (Delegated)**:
- `Dynamics CRM` → `user_impersonation` (delegated)
  - This allows the API to call Dataverse **as the user**

**Exposed API Scopes**:
- `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`
  - This is what the PCF control requests when acquiring tokens

**Known Client Applications**:
```json
{
  "knownClientApplications": ["5175798e-f23e-41c3-b09b-7a90b9218189"]
}
```
- This enables consent propagation: When user consents to PCF app, they also consent to BFF API calling Dataverse on their behalf

### OBO Token Characteristics

**User Token (Input)**:
- Audience: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- Contains: `oid` (user's Azure AD object ID), `scp` (scopes granted)
- Lifetime: 1 hour (default)

**Dataverse Token (Output)**:
- Audience: `https://spaarkedev1.crm.dynamics.com`
- Contains: `oid` (same user), `scp: user_impersonation`
- Lifetime: 1 hour (default)
- **Critical**: This token has the user's Dataverse permissions, NOT the service principal's

### Debugging OBO Issues

**Application Insights Query** (successful OBO):
```kusto
traces
| where message contains "UAC-DIAG"
| where timestamp > ago(1h)
| project timestamp, message
| order by timestamp desc
```

Expected log sequence:
```
[UAC-DIAG] Using OBO authentication for user context
[UAC-DIAG] Set OBO token on HttpClient authorization header
[UAC-DIAG] Dataverse user lookup successful: {systemuserid}
[UAC-DIAG] Direct query GRANTED for user {userId} on document {documentId}
```

**Common OBO Errors**:

| Error | Cause | Solution |
|-------|-------|----------|
| `AADSTS65001: The user or administrator has not consented` | Missing `user_impersonation` permission | Add Dynamics CRM delegated permission in Azure AD |
| `AADSTS50013: Assertion failed signature validation` | Wrong client secret | Verify `API_CLIENT_SECRET` matches Azure AD |
| `AADSTS700016: Application not found` | Wrong tenant or client ID | Verify tenant ID and BFF API client ID |
| `401 Unauthorized` after OBO | Token not set on HttpClient | Ensure `_httpClient.DefaultRequestHeaders.Authorization` is set |
| `404 RetrievePrincipalAccess not found` | Using wrong API with OBO token | Use direct query pattern instead |

### Direct Query Authorization Pattern

**Why**: `RetrievePrincipalAccess` Dataverse action doesn't work with OBO (delegated) tokens, only with application tokens.

**Solution**: Query the document directly. If the query succeeds, the user has Read access (Dataverse enforces this).

```csharp
// Direct query approach (works with OBO tokens)
var url = $"sprk_documents({resourceId})?$select=sprk_documentid";

using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url)
{
    Headers = { Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken) }
};

var response = await _httpClient.SendAsync(requestMessage, ct);

if (response.IsSuccessStatusCode)
{
    // User has Read access
    return AccessRights.Read;
}
else
{
    // 403 or 404 → User doesn't have access
    return AccessRights.None;
}
```

### Related Documentation

- **Full OBO Pattern**: `.claude/patterns/auth/obo-flow.md` (includes Dataverse-specific implementation)
- **Architecture Changes**: `projects/ai-summary-and-analysis-enhancements/ARCHITECTURE-CHANGES.md` (section 3: OBO Authentication Implementation)
- **Auth Patterns**: `docs/architecture/sdap-auth-patterns.md` (Dataverse OBO flow)

---

## Email Processing Configuration

### App Service Settings for Email Processing

| Setting | Value | Notes |
|---------|-------|-------|
| `EmailProcessing__Enabled` | `true` | Feature toggle |
| `EmailProcessing__EnableWebhook` | `true` | Enable webhook endpoint |
| `EmailProcessing__WebhookSecret` | (Key Vault) | HMAC-SHA256 secret |
| `EmailProcessing__DefaultContainerId` | `b!yLRdWEO...` | **MUST be Drive ID format** |

### DefaultContainerId Format

**Critical**: The `DefaultContainerId` must be in **Drive ID format** (`b!xxx`), NOT a raw GUID.

```
❌ WRONG: "58dd5db4-8043-4676-965e-c92e45f07221"
✅ CORRECT: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
```

**Setting via Azure CLI**:
```powershell
# Use PowerShell to avoid bash escaping issues with '!' character
az webapp config appsettings set `
  --name spe-api-dev-67e2xz `
  --resource-group spe-infrastructure-westus2 `
  --settings "EmailProcessing__DefaultContainerId=b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
```

**Note**: In bash, the `!` character causes history expansion issues. Use PowerShell or escape with `\!`.

### Email Processing Authentication

Email processing uses **app-only authentication** (not OBO) because:
1. Dataverse webhooks arrive without user context
2. Background job handlers have no `HttpContext`
3. The app uploads files on its own behalf

See [sdap-auth-patterns.md](sdap-auth-patterns.md) Pattern 6 for details.

---

## GUID Quick Lookup

| What | GUID |
|------|------|
| Tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Dataverse (PCF) App | `5175798e-f23e-41c3-b09b-7a90b9218189` |
| BFF API App | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Graph Resource | `https://graph.microsoft.com` |
| Dataverse Resource | `https://spaarkedev1.crm.dynamics.com` |
| Email Container (Dev) | `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50` |

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

## Secret Rotation Procedure

When a client secret approaches expiration or is compromised, follow this procedure to rotate it with minimal downtime.

### Step 1: Identify Which Secret to Rotate

Refer to the [Client Secrets Inventory](#client-secrets-inventory) above. Determine which app registration's secret needs rotation:

| App Registration | Client ID | Current Secret Prefix | Expires |
|------------------|-----------|----------------------|---------|
| **BFF API App** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `l8b8Q~J` | 2027-12-18 |
| **DSM-SPE Dev 2** | `170c98e1-d486-4355-bcbe-170454e0207c` | `~Ac8Q~JGnsrv` | 2027-09-22 |

### Step 2: Create New Secret in Azure AD

```bash
# Create a new client secret (valid for 24 months)
az ad app credential reset \
  --id <APP_CLIENT_ID> \
  --append \
  --display-name "Rotation-$(date +%Y%m%d)" \
  --end-date $(date -d "+24 months" +%Y-%m-%dT%H:%M:%SZ)
```

**Important**: Copy the secret value immediately -- it is only shown once.

### Step 3: Update Azure Key Vault

```bash
# Store the new secret in Key Vault
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name <SECRET_NAME> \
  --value "<NEW_SECRET_VALUE>"
```

Secret names by app registration:

| App Registration | Key Vault Secret Name |
|------------------|-----------------------|
| **BFF API App** (`1e40baad`) | `API-CLIENT-SECRET` |
| **DSM-SPE Dev 2** (`170c98e1`) | `BFF-API-ClientSecret` |

### Step 4: Update App Service Configuration

If using direct App Service settings (not Key Vault references):

```bash
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings API_CLIENT_SECRET="<NEW_SECRET_VALUE>"
```

If using Key Vault references, the App Service will pick up the new secret automatically on next restart.

### Step 5: Restart App Service

The App Service must be restarted to pick up the new secret value:

```bash
az webapp restart \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2
```

Allow 30-60 seconds for the app to fully restart.

### Step 6: Verify the Rotation Worked

```bash
# Check health endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Check Dataverse-specific health (if applicable)
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse

# Check App Service logs for auth errors
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

Expected results:
- Health endpoint returns 200 "Healthy"
- No `AADSTS` errors in logs
- No `401` or `403` responses in Application Insights

### Step 7: Remove Old Secret

Once the new secret is confirmed working (wait at least 1 hour to ensure all cached tokens have expired):

```bash
# List credentials to find the old secret's key ID
az ad app credential list --id <APP_CLIENT_ID> --query "[].{keyId:keyId, hint:hint, endDateTime:endDateTime}"

# Remove the old secret by key ID
az ad app credential delete --id <APP_CLIENT_ID> --key-id <OLD_KEY_ID>
```

### Step 8: Update Documentation

Update the [Client Secrets Inventory](#client-secrets-inventory) section in this document with the new secret prefix, creation date, and expiration date.

### Rotation Checklist

- [ ] New secret created in Azure AD (with `--append` to avoid downtime)
- [ ] New secret stored in Key Vault (`spaarke-spekvcert`)
- [ ] App Service configuration updated (if using direct settings)
- [ ] App Service restarted
- [ ] Health check passes
- [ ] No auth errors in logs for 1+ hour
- [ ] Old secret removed from Azure AD
- [ ] Documentation updated (this file)

---

## Production App Registrations

> **Created by**: `scripts/Register-EntraAppRegistrations.ps1`
> **Verified by**: `scripts/Test-EntraAppRegistrations.ps1`
> **Tenant**: Same as dev (`a221a95e-6abc-4434-aecc-e48338a1b2f2`)

### Production BFF API App (spaarke-bff-api-prod)

| Property | Value |
|----------|-------|
| **Display Name** | `spaarke-bff-api-prod` |
| **Application (client) ID** | *(set after creation — stored in Key Vault as `BFF-API-ClientId`)* |
| **Application ID URI** | `api://{client-id}` |
| **Platform** | Web |
| **Purpose** | Production BFF API: validates user tokens, performs OBO for Graph + Dataverse |

**Redirect URIs**:
```
https://api.spaarke.com/.auth/login/aad/callback
```

**Exposed API Scopes**:
```
api://{client-id}/user_impersonation
```

**API Permissions** (Delegated):
- Microsoft Graph: `Files.ReadWrite.All`, `Sites.ReadWrite.All`, `User.Read`, `Mail.Send`
- Dynamics CRM: `user_impersonation`

**Known Client Applications**: *(configured after PCF and Code Page prod client registrations are created)*

**Key Vault Secrets** (`sprk-platform-prod-kv`):

| Secret Name | Content |
|-------------|---------|
| `TenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `BFF-API-ClientId` | Application (client) ID |
| `BFF-API-ClientSecret` | Client secret (24-month expiry) |
| `BFF-API-Audience` | `api://{client-id}` |

---

### Production Dataverse S2S App (spaarke-dataverse-s2s-prod)

| Property | Value |
|----------|-------|
| **Display Name** | `spaarke-dataverse-s2s-prod` |
| **Application (client) ID** | *(set after creation — stored in Key Vault as `Dataverse-S2S-ClientId`)* |
| **Platform** | Web (no redirect URI needed) |
| **Purpose** | Server-to-server Dataverse authentication (ServiceClient, AuthType=ClientSecret) |

**API Permissions** (Delegated):
- Dynamics CRM: `user_impersonation`

**Key Vault Secrets** (`sprk-platform-prod-kv`):

| Secret Name | Content |
|-------------|---------|
| `Dataverse-S2S-ClientId` | Application (client) ID |
| `Dataverse-S2S-ClientSecret` | Client secret (24-month expiry) |

---

### Production vs Dev Comparison

| Property | Dev | Production |
|----------|-----|------------|
| **BFF API Name** | SPE BFF API | spaarke-bff-api-prod |
| **BFF API Client ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | *(after creation)* |
| **S2S Name** | DSM-SPE Dev 2 | spaarke-dataverse-s2s-prod |
| **S2S Client ID** | `170c98e1-d486-4355-bcbe-170454e0207c` | *(after creation)* |
| **Redirect URI** | `https://spe-api-dev-67e2xz.azurewebsites.net` | `https://api.spaarke.com` |
| **Secret Storage** | App Service settings / user-secrets | Key Vault (`sprk-platform-prod-kv`) |
| **Naming** | Legacy (pre-convention) | FR-11 compliant (`spaarke-` prefix) |

---

### Post-Creation Manual Steps

1. **Admin Consent**: Global Administrator must grant admin consent for both registrations
2. **Dataverse Application Users**: After Dataverse environment provisioning, register both apps as Application Users with appropriate security roles
3. **Known Client Applications**: After PCF/Code Page production client registrations are created, add their client IDs to `knownClientApplications` on the BFF API app
4. **SPE Container Type Registration**: Run `Register-BffApi-WithCertificate.ps1` (modified for prod) to register the BFF API with the production SPE container type

---

## Production Custom Domain & SSL

> **Configured by**: `scripts/Configure-CustomDomain.ps1`
> **Verified by**: `scripts/Test-CustomDomain.ps1`

### Custom Domain Configuration

| Property | Value |
|----------|-------|
| **Custom Domain** | `api.spaarke.com` |
| **App Service** | `spaarke-bff-prod` |
| **Default Hostname** | `spaarke-bff-prod.azurewebsites.net` |
| **Resource Group** | `rg-spaarke-platform-prod` |
| **SSL Certificate** | Azure-managed (auto-renewal) |
| **HTTPS-Only** | Enabled (HTTP -> HTTPS redirect) |

### DNS Records Required

| Type | Name | Value | TTL |
|------|------|-------|-----|
| CNAME | `api` | `spaarke-bff-prod.azurewebsites.net` | 3600 |

**Alternative** (if CNAME not supported at apex):
- A record: `api` -> App Service IP
- TXT record: `asuid.api` -> domain verification ID

### SSL Certificate Details

- **Type**: Azure-managed free certificate
- **Auto-Renewal**: Yes (Azure handles renewal automatically)
- **Binding**: SNI SSL
- **Coverage**: `api.spaarke.com`

### CORS Configuration

CORS origins are added per customer as they onboard:

```bash
# Add origin for a new customer's Dataverse environment
az webapp cors add \
  --name spaarke-bff-prod \
  --resource-group rg-spaarke-platform-prod \
  --allowed-origins "https://spaarke-{customer}.crm.dynamics.com"
```

### Endpoints

| URL | Purpose |
|-----|---------|
| `https://api.spaarke.com/healthz` | Health check endpoint |
| `https://api.spaarke.com/ping` | Lightweight ping |
| `https://api.spaarke.com/api/*` | BFF API endpoints |

### Verification

```powershell
# Full verification suite
.\Test-CustomDomain.ps1

# Quick manual checks
curl https://api.spaarke.com/healthz
nslookup api.spaarke.com
```

---

## Related Articles

- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Auth flow implementation
- [auth-security-boundaries.md](auth-security-boundaries.md) - Security zones
- [auth-performance-monitoring.md](auth-performance-monitoring.md) - Monitoring

---

*Extracted from AUTHENTICATION-ARCHITECTURE.md resource inventory and GUID reference*
