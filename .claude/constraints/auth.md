# Authentication & Authorization Constraints

> **Domain**: OAuth, OBO, Token Management, Access Control
> **Source ADRs**: ADR-003, ADR-004, ADR-008, ADR-009
> **Last Updated**: 2025-12-19

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

### Authorization Architecture (ADR-003)

- ❌ **MUST NOT** create new service layers for auth (use rules instead)
- ❌ **MUST NOT** make direct Graph/SPE calls outside `SpeFileStore`
- ❌ **MUST NOT** cache authorization decisions (cache data only)
- ❌ **MUST NOT** reuse UAC snapshots across requests/jobs

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

**Note**: Single rule model (`OperationAccessRule`) - Dataverse `RetrievePrincipalAccess` handles all permission computation including teams, roles, and sharing.

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

**Lines**: ~130
**Purpose**: Single-file reference for all authentication and authorization constraints

