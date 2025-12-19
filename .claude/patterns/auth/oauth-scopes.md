# OAuth Scopes Pattern

> **Domain**: OAuth / Scope Configuration
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-004, ADR-008

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalQuickCreate/control/services/auth/msalConfig.ts` | Client scope config |
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

### Graph API Scope (BFF → Graph via OBO)
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

---

## Token Audience Claims

| Token | `aud` Claim | Validated By |
|-------|-------------|--------------|
| Token A (User → BFF) | `api://{BFF_APP_ID}` | BFF API |
| Token B (BFF → Graph) | `https://graph.microsoft.com` | Graph API |

---

## Common Errors

### AADSTS70011: Invalid Scope
**Cause**: Using individual scopes instead of `.default` for OBO
**Fix**: Use `https://graph.microsoft.com/.default`

### AADSTS50013: Audience Mismatch
**Cause**: Token `aud` doesn't match expected audience
**Fix**: Verify `AzureAd:Audience` matches App Registration

### AADSTS65001: Consent Required
**Cause**: Admin consent not granted for Graph permissions
**Fix**: Grant admin consent in Azure Portal

---

## App Registration Requirements

### BFF API App (Confidential Client)
- **Type**: Web application
- **Supported account types**: Single tenant
- **Expose an API**: `api://{APP_ID}/user_impersonation`
- **API Permissions**: Microsoft Graph (delegated)
  - `Files.Read.All`
  - `FileStorageContainer.Selected`
  - Grant admin consent

### PCF Client App (Public Client)
- **Type**: SPA / Public client
- **Supported account types**: Single tenant
- **API Permissions**: BFF API
  - `api://{BFF_APP_ID}/user_impersonation`

---

## Related Patterns

- [OBO Flow](obo-flow.md) - Token exchange implementation
- [MSAL Client](msal-client.md) - Client-side scope requests

---

**Lines**: ~100
