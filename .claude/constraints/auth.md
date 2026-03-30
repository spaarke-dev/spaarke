# Authentication & Authorization Constraints

> **Domain**: OAuth, OBO, Token Management, Access Control
> **Source ADRs**: ADR-003, ADR-004, ADR-008, ADR-009
> **Last Updated**: 2026-03-09

---

## When to Load This File

Load when:
- Implementing OAuth/OBO token flows
- Configuring MSAL (client or server)
- Implementing authorization logic
- Creating new IAuthorizationRule implementations
- Working with UAC (User Access Control) data
- Modifying access to SpeFileStore operations

---

## MUST Rules

### OAuth/OBO Flow (ADR-004, ADR-009)

- ✅ **MUST** use `.default` scope for OBO token exchange (not individual scopes)
- ✅ **MUST** use scope format `api://{APP_ID}/user_impersonation` for BFF API
- ✅ **MUST** cache OBO tokens with 55-minute TTL (5-minute buffer before expiry)
- ✅ **MUST** hash user tokens (SHA256) before using as cache keys
- ✅ **MUST** use sessionStorage for client-side token cache (cleared on tab close)
- ✅ **MUST** try silent token acquisition before popup/redirect
- ✅ **MUST** use `organizations` authority for Code Page MSAL config (not hardcoded tenant)
- ✅ **MUST** check parent token bridge (`window.parent.__SPAARKE_BFF_TOKEN__`) before MSAL initialization in child iframes

### Authorization Architecture (ADR-003)

- ✅ **MUST** implement new auth logic as `IAuthorizationRule`
- ✅ **MUST** call authorization before `SpeFileStore` operations
- ✅ **MUST** cache UAC snapshots per-request only (not across requests)
- ✅ **MUST** include machine-readable deny codes (e.g., `sdap.access.deny.team_mismatch`)

---

## MUST NOT Rules

### OAuth/OBO Flow (ADR-004)

- ❌ **MUST NOT** use individual Graph scopes in OBO (use `.default` only)
- ❌ **MUST NOT** store user tokens in plaintext (always hash)
- ❌ **MUST NOT** use localStorage for tokens (use sessionStorage)
- ❌ **MUST NOT** skip MSAL initialization step in MSAL v3+
- ❌ **MUST NOT** use friendly scope names (use `api://{GUID}/scope` format)
- ❌ **MUST NOT** hardcode tenant ID in Code Page MSAL authority (use `organizations`)
- ❌ **MUST NOT** initialize MSAL in child iframes without first checking parent token bridge

### Authorization Architecture (ADR-003)

- ❌ **MUST NOT** create new service layers for auth (use rules instead)
- ❌ **MUST NOT** make direct Graph/SPE calls outside `SpeFileStore`
- ❌ **MUST NOT** cache authorization decisions (cache data only)
- ❌ **MUST NOT** reuse UAC snapshots across requests/jobs

### @spaarke/auth Shared Library (Code Pages)

- ✅ **MUST** use `@spaarke/auth` (`resolveRuntimeConfig`, `initAuth`, `authenticatedFetch`) for all new Code Pages that call BFF endpoints
- ✅ **MUST** call `resolveRuntimeConfig()` + `setRuntimeConfig()` before rendering the React app
- ✅ **MUST** import `authenticatedFetch` from `authInit.ts` (workspace) or `@spaarke/auth` (standalone wizards)
- ✅ **MUST** use lazy functions (not module-level constants) for any value derived from runtime config
- ❌ **MUST NOT** use module-level `const X = getMsalClientId()` or `const X = getBffBaseUrl()` — these throw before bootstrap
- ❌ **MUST NOT** import `authenticatedFetch` from legacy `bffAuthProvider.ts` in new code
- ❌ **MUST NOT** add `@spaarke/auth` bootstrap to Code Pages that only use `Xrm.WebApi` (unnecessary overhead)
- ❌ **MUST NOT** create new `msalConfig.ts` files with module-level MSAL configuration constants

> See: `.claude/patterns/auth/spaarke-auth-initialization.md` and `.claude/patterns/auth/xrm-webapi-vs-bff-auth.md`

---

## Quick Reference Patterns

### Authorization Flow

```csharp
// 1. Resolve authorization before storage operations
var decision = await _authService.AuthorizeAsync(userId, resourceId, Operation.Read);

if (!decision.IsAuthorized)
    return Results.Problem(statusCode: 403, extensions: new {
        errorCode = decision.DenyCode  // e.g., "sdap.access.deny.team_mismatch"
    });

// 2. Proceed with storage
var file = await _speFileStore.GetFileAsync(resourceId);
```

**See**: [Auth Patterns](../patterns/auth/INDEX.md)

### Authorization Check Pattern

```csharp
// Use operations from OperationAccessPolicy (not generic "read" or "write")
var authContext = new AuthorizationContext
{
    UserId = userId,                    // Azure AD OID (from 'oid' claim)
    ResourceId = documentId.ToString(), // Dataverse record ID
    Operation = "read_metadata",        // Must exist in OperationAccessPolicy
    CorrelationId = httpContext.TraceIdentifier
};

var result = await _authorizationService.AuthorizeAsync(authContext);

if (!result.IsAllowed)
{
    return ProblemDetailsHelper.Forbidden(result.ReasonCode);
}
```

**Note**: Single rule model (`OperationAccessRule`) - Dataverse handles all permission computation (teams, roles, sharing). Uses `RetrievePrincipalAccess` in app-only contexts; uses direct query pattern in OBO contexts because `RetrievePrincipalAccess` does NOT work with OBO tokens.

**See**: [UAC Access Control Pattern](../patterns/auth/uac-access-control.md)

### Two Seams Only

| Seam | Purpose | Interface |
|------|---------|-----------|
| UAC Data | User access control snapshots | `IAccessDataSource` |
| Storage | File operations | `SpeFileStore` |

### Deny Code Format

Pattern: `{domain}.{area}.{action}.{reason}`

Examples:
- `sdap.access.deny.team_mismatch`
- `sdap.access.deny.role_insufficient`
- `sdap.access.deny.resource_locked`

---

## Pattern Files (Complete Examples)

- [OAuth Scopes](../patterns/auth/oauth-scopes.md) - Scope format and configuration
- [OBO Flow](../patterns/auth/obo-flow.md) - On-Behalf-Of token exchange
- [Token Caching](../patterns/auth/token-caching.md) - Server & client token caching
- [MSAL Client](../patterns/auth/msal-client.md) - Client-side MSAL patterns

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-003](../adr/ADR-003-authorization-seams.md) | Authorization seams | Architecture review, new rule design |
| [ADR-004](../adr/ADR-004-bff-api-authorization.md) | OAuth/OBO flow | Token exchange decisions |
| [ADR-008](../adr/ADR-008-endpoint-filters.md) | Endpoint authorization | Auth filter implementation |
| [ADR-009](../adr/ADR-009-redis-first-caching.md) | Token caching | Caching strategy decisions |

---

**Lines**: ~140
**Purpose**: Single-file reference for all authentication and authorization constraints
**Last Updated**: 2026-03-09

