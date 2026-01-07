# Authentication & Authorization Patterns Index

> **Domain**: OAuth / OBO / MSAL / Token Management / Access Control
> **Last Updated**: 2026-01-06

---

## Available Patterns

### Authentication (Identity)

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [oauth-scopes.md](oauth-scopes.md) | Scope format and configuration | ~100 |
| [obo-flow.md](obo-flow.md) | On-Behalf-Of token exchange | ~130 |
| [token-caching.md](token-caching.md) | Server & client token caching | ~120 |
| [msal-client.md](msal-client.md) | Client-side MSAL patterns | ~125 |

### Authorization (Access Control)

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [uac-access-control.md](uac-access-control.md) | UAC permission checking | ~80 |

**Full UAC Reference**: [docs/architecture/uac-access-control.md](../../../docs/architecture/uac-access-control.md)

---

## When to Load

| Task | Load These Patterns |
|------|---------------------|
| Configure OAuth scopes | `oauth-scopes.md` |
| Implement OBO flow | `obo-flow.md` |
| Add token caching | `token-caching.md` |
| Build PCF authentication | `msal-client.md` |
| Implement authorization checks | `uac-access-control.md` |
| Add new operations to policy | `uac-access-control.md` |

---

## Canonical Source Files

All patterns reference actual implementations in:

### Server-Side (BFF API)
```
src/server/api/Sprk.Bff.Api/
├── Infrastructure/
│   ├── Auth/TokenHelper.cs           # Bearer token extraction
│   └── Graph/
│       ├── GraphClientFactory.cs     # OBO flow implementation
│       ├── SimpleTokenCredential.cs  # Graph SDK v5 wrapper
│       └── GraphTokenCache.cs        # Redis token caching
├── Api/OBOEndpoints.cs               # User-context endpoints
└── Program.cs                        # JWT validation setup
```

### Client-Side (PCF)
```
src/client/pcf/
├── UniversalQuickCreate/control/services/auth/
│   ├── msalConfig.ts                 # MSAL configuration
│   └── MsalAuthProvider.ts           # Token acquisition
├── SpeFileViewer/control/
│   └── BffClient.ts                  # API client with auth
└── shared/utils/environmentVariables.ts
```

---

## OAuth Flow Overview

```
┌─────────────────┐                    ┌─────────────────┐
│   PCF Control   │                    │    BFF API      │
│  (Public App)   │                    │ (Confidential)  │
└────────┬────────┘                    └────────┬────────┘
         │                                      │
         │ 1. MSAL.acquireTokenSilent/Popup    │
         │    scope: api://{bff}/user_impersonation
         │                                      │
         ▼                                      │
┌─────────────────┐                             │
│    Azure AD     │                             │
│                 │◄────────────────────────────┤
└────────┬────────┘  2. OBO: Exchange token     │
         │              scope: graph.microsoft.com/.default
         │                                      │
         ▼                                      ▼
   Token A (User)                        Token B (Graph)
   aud: api://{bff}                      aud: graph.microsoft.com
```

---

## Related Resources

- [Auth Constraints](../../constraints/auth.md) - Authorization rules (access control)
- [API Constraints](../../constraints/api.md) - Endpoint filter patterns

