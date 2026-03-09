# Authentication & Authorization Patterns Index

> **Domain**: OAuth / OBO / MSAL / Token Management / Access Control
> **Last Updated**: 2026-03-09

---

## Available Patterns

### Authentication (Identity)

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [oauth-scopes.md](oauth-scopes.md) | Scope format, permission inventory, auth mode decision guide | ~200 |
| [obo-flow.md](obo-flow.md) | On-Behalf-Of token exchange (Graph API) | ~195 |
| [dataverse-obo.md](dataverse-obo.md) | On-Behalf-Of token exchange (Dataverse) | ~170 |
| [token-caching.md](token-caching.md) | Server & client token caching | ~245 |
| [msal-client.md](msal-client.md) | Client-side MSAL patterns (PCF + Code Pages) | ~280 |
| [service-principal.md](service-principal.md) | Service-to-service auth (app-only) | ~280 |

### Graph API Integration

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [graph-sdk-v5.md](graph-sdk-v5.md) | Graph SDK v5 setup, ForApp vs ForUserAsync, architecture rules | ~220 |
| [graph-endpoints-catalog.md](graph-endpoints-catalog.md) | BFF Graph API surface map (OBO + internal endpoints) | ~170 |
| [graph-webhooks.md](graph-webhooks.md) | Subscription lifecycle, renewal, extending to new resources | ~180 |

### Authorization (Access Control)

| Pattern | Purpose | Lines |
|---------|---------|-------|
| [uac-access-control.md](uac-access-control.md) | UAC permission checking | ~115 |

**Full UAC Reference**: [docs/architecture/uac-access-control.md](../../../docs/architecture/uac-access-control.md)

---

## When to Load

| Task | Load These Patterns |
|------|---------------------|
| Configure OAuth scopes | `oauth-scopes.md` |
| Implement Graph API OBO flow | `obo-flow.md`, `graph-sdk-v5.md` |
| Implement Dataverse OBO flow | `dataverse-obo.md` |
| Add token caching | `token-caching.md` |
| Build PCF authentication | `msal-client.md` |
| Build Code Page authentication | `msal-client.md` |
| Implement app-only (S2S) auth | `service-principal.md`, `graph-sdk-v5.md` |
| Implement authorization checks | `uac-access-control.md` |
| Add new operations to policy | `uac-access-control.md` |
| Add new Graph API feature | `graph-sdk-v5.md`, `oauth-scopes.md`, `graph-endpoints-catalog.md` |
| Work with Graph webhooks/subscriptions | `graph-webhooks.md`, `graph-sdk-v5.md` |
| Understand BFF Graph API surface | `graph-endpoints-catalog.md` |
| Choose OBO vs App-Only auth mode | `oauth-scopes.md` (Auth Mode Decision Guide) |
| Add new Graph API permission | `oauth-scopes.md` (Adding New Graph Permissions) |

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
- [Resilience Pattern](../api/resilience.md) - Graph-specific retry/circuit breaker via `GraphHttpMessageHandler`
- [SDAP Auth Patterns](../../../docs/architecture/sdap-auth-patterns.md) - Full architecture reference (OBO, app-only, token flow)

