# SDAP Authentication Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (Authentication & Security section)
> **Last Updated**: December 3, 2025
> **Applies To**: Authentication code, token handling, Azure AD configuration

---

## TL;DR

SDAP uses three authentication patterns: (1) MSAL.js in PCF for user tokens, (2) On-Behalf-Of (OBO) flow for Graph API, (3) ClientSecret for server-to-Dataverse. User identity flows through OBO to Graph; app identity used for Dataverse metadata queries.

---

## Applies When

- Debugging authentication failures (401, AADSTS errors)
- Adding new API endpoints that need authentication
- Understanding why user context needed vs app context
- Configuring Azure AD app registrations
- Troubleshooting token scope issues

---

## Authentication Flow Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│ 1. USER (Browser) → Dataverse SSO                                       │
│    Already authenticated via Entra ID                                   │
└─────────────────────────────────────────────────────────────────────────┘
                    │
                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 2. PCF CONTROL → MSAL.js                                                │
│    Scope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation│
│    Method: acquireTokenSilent (ssoSilent)                               │
│    Result: User access token for BFF API                                │
└─────────────────────────────────────────────────────────────────────────┘
                    │
                    ↓ Authorization: Bearer {user_token}
┌─────────────────────────────────────────────────────────────────────────┐
│ 3. BFF API → Validates JWT                                              │
│    Issuer: https://login.microsoftonline.com/{tenant}/v2.0              │
│    Audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c                 │
└─────────────────────────────────────────────────────────────────────────┘
           │                                      │
           ↓ OBO Exchange                         ↓ ClientSecret
┌──────────────────────────┐      ┌──────────────────────────────────────┐
│ 4. GRAPH API             │      │ 5. DATAVERSE                          │
│    grant_type: jwt-bearer│      │    AuthType: ClientSecret             │
│    User context preserved│      │    App-only (no user context)         │
│    For: File operations  │      │    For: Metadata queries              │
└──────────────────────────┘      └──────────────────────────────────────┘
```

---

## Pattern 1: MSAL.js in PCF Control

**When**: User needs token to call BFF API

```typescript
// MsalAuthProvider.ts
export class MsalAuthProvider {
    private msalInstance: PublicClientApplication;
    
    constructor() {
        this.msalInstance = new PublicClientApplication({
            auth: {
                clientId: "5175798e-f23e-41c3-b09b-7a90b9218189",  // PCF App
                authority: "https://login.microsoftonline.com/{tenant}",
                redirectUri: "https://spaarkedev1.crm.dynamics.com"
            },
            cache: {
                cacheLocation: 'localStorage'
            }
        });
    }

    async getToken(scopes: string[]): Promise<string> {
        const account = this.msalInstance.getAllAccounts()[0];
        
        const result = await this.msalInstance.acquireTokenSilent({
            scopes,
            account
        });
        
        return result.accessToken;
    }
}

// Usage - CORRECT scope format
const token = await authProvider.getToken([
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
]);
```

**Common Mistake**: Using friendly name `api://spe-bff-api/...` instead of full Application ID URI.

---

## Pattern 2: On-Behalf-Of (OBO) Token Exchange

**When**: BFF API needs to call Graph API on behalf of user (file operations)

```csharp
// GraphClientFactory.cs
public async Task<GraphServiceClient> CreateClientAsync(string userAccessToken)
{
    // Exchange user token for Graph token
    var tokenResponse = await _confidentialClient.AcquireTokenOnBehalfOf(
        scopes: new[] { "https://graph.microsoft.com/.default" },
        userAssertion: new UserAssertion(userAccessToken)
    ).ExecuteAsync();

    return new GraphServiceClient(
        new DelegateAuthenticationProvider(request =>
        {
            request.Headers.Authorization = 
                new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
            return Task.CompletedTask;
        }));
}
```

**OBO Request Details**:
```
POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token

grant_type:          urn:ietf:params:oauth2:grant-type:jwt-bearer
client_id:           1e40baad-e065-4aea-a8d4-4b7ab273458c
client_secret:       {BFF_CLIENT_SECRET}
assertion:           {user_access_token}
requested_token_use: on_behalf_of
scope:               https://graph.microsoft.com/.default
```

**Why OBO**: User's permissions apply to SharePoint files - can't use app-only.

---

## Pattern 3: ClientSecret for Dataverse

**When**: BFF API needs Dataverse metadata (no user context needed)

```csharp
// DataverseServiceClientImpl.cs
public DataverseServiceClientImpl(IConfiguration config)
{
    var connectionString = 
        $"AuthType=ClientSecret;" +
        $"Url={config["Dataverse:ServiceUrl"]};" +
        $"ClientId={config["API_APP_ID"]};" +
        $"ClientSecret={config["API_CLIENT_SECRET"]}";

    _serviceClient = new ServiceClient(connectionString);
}
```

**Why ClientSecret over ManagedIdentity**: More reliable, easier to configure, Microsoft's recommended approach for ServiceClient.

**Prerequisite**: Application User must exist in Dataverse with System Administrator role (or custom role with metadata read permissions).

---

## Token Scopes Reference

| Token For | Scope | Pattern |
|-----------|-------|---------|
| BFF API (from PCF) | `api://1e40baad-.../user_impersonation` | MSAL.js |
| Graph API (from BFF) | `https://graph.microsoft.com/.default` | OBO |
| Dataverse (from BFF) | `https://{org}.crm.dynamics.com/.default` | ClientCredentials |

---

## App Registration Requirements

### PCF App (5175798e-f23e-41c3-b09b-7a90b9218189)

```
Platform: Single-page application (SPA)
Redirect URIs:
  - https://spaarkedev1.crm.dynamics.com
  - http://localhost:8181 (dev)
  
API Permissions (Delegated):
  - Microsoft Graph: User.Read, offline_access
  - BFF API: user_impersonation
  
Admin Consent: Required
```

### BFF API App (1e40baad-e065-4aea-a8d4-4b7ab273458c)

```
Platform: Web
Client Secret: Stored in Key Vault

Expose an API:
  - App ID URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
  - Scope: user_impersonation

API Permissions (Delegated):
  - Microsoft Graph: Files.ReadWrite.All, Sites.ReadWrite.All, User.Read
  - Dynamics CRM: user_impersonation

Admin Consent: Required for all
```

---

## Dataverse Application User

**Critical**: Without this, ServiceClient auth fails with AADSTS500011.

```
Setup via Power Platform Admin Center:
1. Environments → SPAARKE DEV 1 → Settings
2. Users + permissions → Application users
3. + New app user
4. Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
5. Security Role: System Administrator

Verification:
pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com
```

---

## Common Mistakes

| Mistake | Error | Fix |
|---------|-------|-----|
| Wrong scope URI format | AADSTS500011: Resource principal not found | Use full `api://{guid}/...` not friendly name |
| Missing Dynamics CRM permission | AADSTS500011 for Dataverse | Add `user_impersonation` to BFF app |
| No Application User | ServiceClient connection failed | Create via Power Platform Admin Center |
| Using ManagedIdentity for Dataverse | No User Assigned Managed Identity found | Use ClientSecret connection string |
| Expired client secret | 401 on all BFF calls | Rotate secret in Azure AD + Key Vault |

---

## Debugging Authentication

### Check PCF Token Acquisition
```javascript
// Browser console
[MsalAuthProvider] Token acquired successfully
// vs
[MsalAuthProvider] Failed to acquire token: {...}
```

### Check BFF API Logs
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2

# Look for:
# - AADSTS errors (Azure AD issues)
# - 401 responses (token validation failed)
# - ServiceClient connection errors (Dataverse auth)
```

### Validate Token Claims
```bash
# Decode JWT at jwt.ms or jwt.io
# Check: aud (audience), scp (scopes), exp (expiration)
```

---

## Related Articles

- [sdap-overview.md](sdap-overview.md) - System architecture overview
- [dataverse-oauth-authentication.md](dataverse-oauth-authentication.md) - General Dataverse OAuth patterns
- [sdap-troubleshooting.md](sdap-troubleshooting.md) - Common issues including auth failures

---

*Condensed from authentication sections of architecture guide*
