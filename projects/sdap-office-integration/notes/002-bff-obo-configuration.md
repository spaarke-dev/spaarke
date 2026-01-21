# Task 002: Update BFF API App Registration for OBO

> **Status**: Ready for Manual Execution
> **Type**: Azure AD Configuration
> **Location**: Azure Portal → App registrations → SPE BFF API

## Overview

Update the existing Spaarke BFF API app registration to support OBO (On-Behalf-Of) flow for the Office add-in. The add-in acquires tokens for the BFF API scope, and the BFF API exchanges them for Graph tokens to access SharePoint Embedded.

## Current BFF API Configuration

| Property | Value |
|----------|-------|
| **Application (client) ID** | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Application ID URI** | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| **Display Name** | SPE BFF API |
| **Tenant ID** | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |

## Configuration Steps

### Step 1: Verify Exposed API Scope

Navigate to: **Azure Portal > App registrations > SPE BFF API > Expose an API**

**Verify Application ID URI** is set:
```
api://1e40baad-e065-4aea-a8d4-4b7ab273458c
```

**Verify `user_impersonation` scope** exists:

| Property | Value |
|----------|-------|
| Scope name | `user_impersonation` |
| Full scope URI | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |
| Who can consent | Admins and users |
| Admin consent display name | Access Spaarke BFF API |
| Admin consent description | Allows the app to access Spaarke BFF API on behalf of the signed-in user |
| User consent display name | Access Spaarke BFF API |
| User consent description | Allows the app to access Spaarke BFF API on your behalf |
| State | Enabled |

> ✅ **Status**: Already configured per `docs/architecture/auth-azure-resources.md`

### Step 2: Add Office Add-in as Authorized Client

Navigate to: **Azure Portal > App registrations > SPE BFF API > Expose an API > Add a client application**

**Add the Office Add-in client ID** (from Task 001):

| Field | Value |
|-------|-------|
| Client ID | `{ADDIN_CLIENT_ID}` (from Task 001) |
| Authorized scopes | ☑ `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |

**Why**: This enables consent propagation - when users consent to the Office add-in, they also consent to the add-in calling the BFF API on their behalf.

**Update knownClientApplications** in manifest:
```json
"knownClientApplications": [
  "5175798e-f23e-41c3-b09b-7a90b9218189",  // Existing: Dataverse (PCF) App
  "{ADDIN_CLIENT_ID}"                       // New: Office Add-in App
]
```

### Step 3: Verify API Permissions for OBO

Navigate to: **Azure Portal > App registrations > SPE BFF API > API permissions**

**Required Delegated Permissions** (for OBO flow):

| API | Permission | Type | Status |
|-----|------------|------|--------|
| Microsoft Graph | `User.Read` | Delegated | ✅ Required |
| Microsoft Graph | `Files.ReadWrite.All` | Delegated | ✅ Required |
| Microsoft Graph | `Sites.ReadWrite.All` | Delegated | ✅ Required |
| Dynamics CRM | `user_impersonation` | Delegated | ✅ Required |

> ✅ **Status**: Already configured per `docs/architecture/auth-azure-resources.md`

### Step 4: Grant Admin Consent

Navigate to: **Azure Portal > App registrations > SPE BFF API > API permissions**

Click **"Grant admin consent for {Tenant}"** if any permissions show "Not granted".

**Verification**: All permissions should show ✅ "Granted for {Tenant}"

## OBO Token Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. Office Add-in (MSAL.js 3.x NAA)                                  │
│    Request token for: api://1e40baad.../user_impersonation          │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ Bearer token
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 2. BFF API validates token                                          │
│    Audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c             │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ OBO Exchange (AcquireTokenOnBehalfOf)
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 3. BFF gets Graph token for: https://graph.microsoft.com/.default   │
│    Token has user's permissions, not service principal's            │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ Graph API calls
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 4. SharePoint Embedded operations via SpeFileStore                  │
│    Upload files, manage metadata, etc.                              │
└─────────────────────────────────────────────────────────────────────┘
```

## Verification Commands

```powershell
# List BFF API app details
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "{id:id,displayName:displayName,identifierUris:identifierUris}"

# List exposed API scopes
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "api.oauth2PermissionScopes"

# List authorized client applications
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "api.preAuthorizedApplications"

# List API permissions
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "requiredResourceAccess"
```

## MSAL.NET OBO Configuration (BFF API)

The BFF API uses this pattern for OBO token exchange:

```csharp
// Build confidential client application
var app = ConfidentialClientApplicationBuilder
    .Create(clientId: "1e40baad-e065-4aea-a8d4-4b7ab273458c")
    .WithClientSecret(clientSecret: API_CLIENT_SECRET)
    .WithAuthority(authority: "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2")
    .Build();

// Acquire Graph token on behalf of user
var result = await app.AcquireTokenOnBehalfOf(
    scopes: new[] { "https://graph.microsoft.com/.default" },
    userAssertion: new UserAssertion(userAccessToken))
    .ExecuteAsync(cancellationToken);

string graphToken = result.AccessToken;
```

## Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| `AADSTS65001: User has not consented` | Add-in not in authorized clients | Add add-in client ID to authorized clients |
| `AADSTS700016: Application not found` | Wrong client ID | Verify add-in client ID from Task 001 |
| `AADSTS50013: Assertion failed` | Wrong client secret | Verify `API_CLIENT_SECRET` in BFF API |
| OBO returns 401 | Missing Graph permissions | Add delegated Graph permissions |

## Related Documentation

- [auth-azure-resources.md](../../../../docs/architecture/auth-azure-resources.md) - Full auth resource inventory
- [obo-flow.md](../../../../.claude/patterns/auth/obo-flow.md) - OBO pattern implementation
- [Task 001 - Azure AD App Registration](001-azure-ad-app-registration.md) - Office add-in app setup

## Acceptance Criteria

- [ ] `user_impersonation` scope is exposed on BFF API
- [ ] Office add-in app ID is listed as authorized client
- [ ] Graph API permissions are configured for OBO (User.Read, Files.ReadWrite.All, Sites.ReadWrite.All)
- [ ] Admin consent is granted for all permissions

---

*Execute these steps in Azure Portal after completing Task 001 (Office add-in app registration).*
