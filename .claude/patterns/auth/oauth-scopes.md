# OAuth Scopes Pattern

> **Domain**: OAuth / Scope Configuration
> **Last Validated**: 2026-03-09
> **Source ADRs**: ADR-004, ADR-008

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalQuickCreate/control/services/auth/msalConfig.ts` | Client scope config |
| `src/client/shared/Spaarke.Auth/src/config.ts` | Code Page MSAL scope config |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | OBO scope usage |
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` | Server audience config |

---

## Scope Format Rules

### BFF API Scope (Client → BFF)
```
api://{BFF_APP_ID}/user_impersonation
```

**Example**:
```typescript
// PCF client requests this scope
const BFF_API_SCOPES = [
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
];
```

### Graph API Scope (BFF → Graph via OBO or App-Only)
```
https://graph.microsoft.com/.default
```

**CRITICAL**: Always use `.default` scope for OBO, never individual permissions.

```csharp
// CORRECT: Use .default
await _cca.AcquireTokenOnBehalfOf(
    new[] { "https://graph.microsoft.com/.default" },
    new UserAssertion(userToken)
);

// WRONG: Individual scopes cause AADSTS70011 error
await _cca.AcquireTokenOnBehalfOf(
    new[] { "Files.Read.All", "FileStorageContainer.Selected" },
    ...
);
```

---

## Complete Graph Permission Inventory

### BFF API App Registration — Delegated Permissions (OBO)

These permissions are granted via admin consent and returned when using `.default` scope in OBO flow:

| Permission | Type | Domain | Used By |
|-----------|------|--------|---------|
| `Files.Read.All` | Delegated | SPE file operations | `SpeFileStore`, `DriveItemOperations` |
| `Files.ReadWrite.All` | Delegated | SPE file upload/delete | `UploadSessionManager`, `OBOEndpoints` |
| `FileStorageContainer.Selected` | Delegated | SPE container access | `ContainerOperations` |
| `Sites.FullControl.All` | Delegated | SPE admin operations | `ContainerOperations` |
| `Mail.Send` | Delegated | Send-as-user email | `CommunicationService.SendAsUserAsync()` |

### BFF API App Registration — Application Permissions (App-Only)

Used by `GraphClientFactory.ForApp()` with `ClientSecretCredential`:

| Permission | Type | Domain | Used By |
|-----------|------|--------|---------|
| `Files.Read.All` | Application | Background file operations | `DriveItemOperations.ListChildrenAsync()` |
| `FileStorageContainer.Selected` | Application | Container management | `ContainerOperations` |
| `Mail.Send` | Application | Outbound from shared mailbox | `CommunicationService.SendAsync()` |
| `Mail.Read` | Application | Inbound email monitoring | `InboundEmailService` |
| `User.Read.All` | Application | User lookup (webhooks) | `GraphSubscriptionManager` |

### PCF Client App Registration (Public Client)

| Permission | Type | Used By |
|-----------|------|---------|
| `api://{BFF_APP_ID}/user_impersonation` | Delegated | All PCF controls calling BFF API |

### Code Page App Registration (SPA)

| Permission | Type | Used By |
|-----------|------|---------|
| `api://{BFF_APP_ID}/user_impersonation` | Delegated | `@spaarke/auth` in Code Pages |

---

## Auth Mode Decision Guide

When adding a new Graph-based feature, choose the correct auth mode:

| Scenario | Auth Mode | Factory Method | Why |
|----------|-----------|----------------|-----|
| User reads/writes SPE files | **OBO** | `ForUserAsync(ctx, ct)` | SPE enforces user-level permissions |
| User sends email as themselves | **OBO** | `ForUserAsync(ctx, ct)` | Email appears from user's mailbox |
| Background email from shared mailbox | **App-Only** | `ForApp()` | No user context in background jobs |
| Webhook subscription management | **App-Only** | `ForApp()` | Service-level operation |
| Inbound email monitoring | **App-Only** | `ForApp()` | Background job reads mailbox |
| Container listing (admin) | **App-Only** | `ForApp()` | Platform admin operation |
| AI analysis reading user files | **OBO** | `ForUserAsync(ctx, ct)` | Must propagate user context — app-only returns 403 for SPE |

**Key Rule**: If the operation involves SPE (SharePoint Embedded) containers/files and requires user-level access, you **must** use OBO. App-only tokens return HTTP 403 for SPE container operations.

---

## Configuration Settings

### BFF API (appsettings.json)
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "{TENANT_ID}",
    "ClientId": "{API_APP_ID}",
    "Audience": "api://{API_APP_ID}"
  }
}
```

### PCF Client (msalConfig.ts)
```typescript
export const msalConfig: Configuration = {
  auth: {
    clientId: "{PCF_CLIENT_ID}",  // Different from BFF
    authority: `https://login.microsoftonline.com/{TENANT_ID}`,
    redirectUri: "https://{org}.crm.dynamics.com"
  }
};

export const loginRequest = {
  scopes: [
    "api://{BFF_API_APP_ID}/user_impersonation"
  ]
};
```

### Code Page MSAL (@spaarke/auth)
```typescript
// Code Pages use multi-tenant authority — no hardcoded tenant
const DEFAULT_AUTHORITY = 'https://login.microsoftonline.com/organizations';
const DEFAULT_BFF_SCOPE = 'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation';
```

---

## Token Audience Claims

| Token | `aud` Claim | Validated By |
|-------|-------------|--------------|
| Token A (User → BFF) | `api://{BFF_APP_ID}` | BFF API |
| Token B (BFF → Graph) | `https://graph.microsoft.com` | Graph API |

---

## Adding New Graph Permissions

When a new feature requires additional Graph API permissions:

1. **Identify the permission** — Check [Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
2. **Choose type** — Delegated (OBO, user context) or Application (app-only, background)
3. **Add to app registration** — Azure Portal → App registrations → API permissions
4. **Grant admin consent** — Required for both delegated and application permissions in enterprise apps
5. **Verify consent** — Check `Enterprise applications → Permissions` tab shows "Granted for {tenant}"
6. **Update this document** — Add the new permission to the inventory table above
7. **No code changes needed** — `.default` scope automatically includes all consented permissions

**Common Errors When Adding Permissions**:

| Error | Cause | Fix |
|-------|-------|-----|
| `AADSTS65001` | Admin consent not granted | Grant consent in Azure Portal |
| `AADSTS70011` | Used individual scope instead of `.default` | Always use `.default` |
| `AADSTS50013` | Token audience mismatch | Verify `AzureAd:Audience` matches app registration |
| `403 Forbidden` from Graph | Permission not in app registration | Add permission and grant consent |

---

## Related Patterns

- [OBO Flow](obo-flow.md) - Token exchange implementation
- [MSAL Client](msal-client.md) - Client-side scope requests
- [Graph SDK v5](graph-sdk-v5.md) - Graph client setup and ForApp/ForUserAsync usage
- [Graph Webhooks](graph-webhooks.md) - Subscription lifecycle management
- [Graph Endpoints Catalog](graph-endpoints-catalog.md) - BFF Graph API surface

---

**Lines**: ~190
