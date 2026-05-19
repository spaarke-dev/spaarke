---
🛑 STOP — DO NOT USE THIS DOCUMENT FOR NEW AUTH WORK 🛑
═══════════════════════════════════════════════════════════════════════════
PRE-V2 CONTENT. Spaarke Auth v2 + Hardening is in active development.
Canonical v2 source: .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md
ADR-027 will become canonical when v2 ships.

DO NOT add `accessToken: string` props anywhere.
DO NOT write raw fetch() with `Authorization: Bearer ${...}` headers.
DO NOT reference BridgeStrategy, XrmStrategy, or window.__SPAARKE_BFF_TOKEN__.
DO use `authenticatedFetch()` from @spaarke/auth.
DO use `useAuth()` hook (after v2 ships).
When in doubt: STOP and consult the audit doc above.

What IS still canonical in this file: OAuth/OBO server-side MUST rules (lines 31-37); MSAL client config invariants (lines 38-44); BFF URL helper rules (lines 92-126) remain canonical. Client-side cascade rules (line 43 §"Required MSAL config", line 70 §"6-strategy chain reference") are pre-v2.
═══════════════════════════════════════════════════════════════════════════
---

# Authentication & Authorization Constraints

> **Domain**: OAuth, OBO, Token Management, Access Control
> **Source ADRs**: ADR-003, ADR-004, ADR-008, ADR-009
> **Last Updated**: 2026-05-13
> **Last Reviewed**: 2026-05-13
> **Status**: Verified
>
> **Client-side MSAL binding requirements** for internal Spaarke surfaces are in
> [`.claude/patterns/auth/spaarke-sso-binding.md`](../patterns/auth/spaarke-sso-binding.md) (canonical).
> The MUST/MUST NOT rules below were updated 2026-05-13 to match. The External
> Workspace SPA (B2B portal) is out of scope for the internal SSO binding —
> see [`docs/architecture/external-access-spa-architecture.md`](../../docs/architecture/external-access-spa-architecture.md).

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

### OAuth/OBO Flow (server-side; ADR-004, ADR-009)

- ✅ **MUST** use `.default` scope for OBO token exchange (not individual scopes)
- ✅ **MUST** use scope format `api://{APP_ID}/user_impersonation` for BFF API
- ✅ **MUST** cache OBO tokens with 55-minute TTL (5-minute buffer before expiry)
- ✅ **MUST** hash user tokens (SHA256) before using as cache keys

### Client-side MSAL (internal Spaarke surfaces; binding 2026-05-12)

- ✅ **MUST** use `cacheLocation: 'localStorage'` (NOT `sessionStorage`) — survives tab/browser close
- ✅ **MUST** use `storeAuthStateInCookie: true` — required for `ssoSilent` when 3rd-party cookies are blocked
- ✅ **MUST** use tenant-specific authority `https://login.microsoftonline.com/{tenantId}` resolved from `Xrm.Utility.getGlobalContext().organizationSettings.tenantId` via frame-walk. Prefer omitting `authority` so `@spaarke/auth` resolves it via `resolveTenantFromXrm()`.
- ✅ **MUST** route token acquisition through `SpaarkeAuthProvider`'s 6-strategy chain (Cache → SessionStorage → Bridge → Xrm → MsalSilent → MsalPopup). See [`spaarke-sso-binding.md`](../patterns/auth/spaarke-sso-binding.md).
- ✅ **MUST** rebuild AND redeploy every consumer of `@spaarke/auth` when the library changes — bundles are NOT auto-updated.

### Authorization Architecture (ADR-003)

- ✅ **MUST** implement new auth logic as `IAuthorizationRule`
- ✅ **MUST** call authorization before `SpeFileStore` operations
- ✅ **MUST** cache UAC snapshots per-request only (not across requests)
- ✅ **MUST** include machine-readable deny codes (e.g., `sdap.access.deny.team_mismatch`)

---

## MUST NOT Rules

### OAuth/OBO Flow (server-side; ADR-004)

- ❌ **MUST NOT** use individual Graph scopes in OBO (use `.default` only)
- ❌ **MUST NOT** store user tokens in plaintext (always hash)
- ❌ **MUST NOT** skip MSAL initialization step in MSAL v3+
- ❌ **MUST NOT** use friendly scope names (use `api://{GUID}/scope` format)

### Client-side MSAL (internal Spaarke surfaces)

- ❌ **MUST NOT** use `sessionStorage` for the MSAL cache on internal surfaces (it wipes on tab close → popup every new tab)
- ❌ **MUST NOT** set `storeAuthStateInCookie: false` — needed for `ssoSilent` under 3rd-party cookie blocking
- ❌ **MUST NOT** use `/organizations` or `/common` as the MSAL authority — `ssoSilent` can't resolve which tenant's session to use inside iframes. Always tenant-specific from Xrm
- ❌ **MUST NOT** instantiate `PublicClientApplication` directly outside `@spaarke/auth` — every component must reuse `getAuthProvider()`
- ❌ **MUST NOT** initialize MSAL in child iframes without first checking the parent token bridge (`window.parent.__SPAARKE_BFF_TOKEN__`)

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

> See: `.claude/patterns/auth/DEPRECATED-spaarke-auth-initialization.md` (⛔ deprecated — superseded by Spaarke Auth v2 `useAuth()`; see [AUDIT-FINDINGS-AUTH-SYSTEM.md](../AUDIT-FINDINGS-AUTH-SYSTEM.md)) and `.claude/patterns/auth/xrm-webapi-vs-bff-auth.md`

### BFF Base URL Convention (CRITICAL — Use `buildBffApiUrl()` Helper)

**Rule**: All BFF API URL construction MUST go through the `buildBffApiUrl()` helper.
Template literal concatenation (`${bffBaseUrl}/api/...`) is banned as of 2026-04-05.

- ✅ **MUST** use `buildBffApiUrl(baseUrl, path)` from `@spaarke/auth` (Code Pages / PCF / workspaces) or from `../shared/utils/environmentVariables` (PCF shared utils)
- ✅ **MUST** get the base URL from `getApiBaseUrl()` (PCF) or `resolveRuntimeConfig()` (Code Pages) — both return HOST ONLY
- ✅ **MUST** pass the endpoint path with or without leading `/api/` — the helper is idempotent
- ❌ **MUST NOT** construct URLs as `` `${bffBaseUrl}/api/ai/search` `` or `` `${bffBaseUrl}/ai/search` `` — use the helper
- ❌ **MUST NOT** add your own `/api`-stripping normalization in service constructors — the source functions already handle it
- ❌ **MUST NOT** read `sprk_BffApiBaseUrl` directly from Dataverse and pass it to services — always go through `getApiBaseUrl()` or `resolveRuntimeConfig()`

**Why this rule exists**: The Dataverse env var `sprk_BffApiBaseUrl` stores `https://host/api` (with `/api`). The resolution functions strip it so all consumer code works with HOST ONLY, and then every caller must add `/api/` back when building request URLs. Manual concatenation has caused multiple production bugs — URLs missing `/api/` (404s) or duplicated `/api/api/` (also 404s). The `buildBffApiUrl()` helper makes this impossible by normalizing both sides.

**Correct usage**:
```typescript
// Code Page
import { buildBffApiUrl, authenticatedFetch } from '@spaarke/auth';
const config = await resolveRuntimeConfig();
const url = buildBffApiUrl(config.bffBaseUrl, '/ai/visualization/related/123');
const response = await authenticatedFetch(url);

// PCF Control
import { getApiBaseUrl, buildBffApiUrl } from '../../shared/utils/environmentVariables';
const base = await getApiBaseUrl(context.webAPI);
const url = buildBffApiUrl(base, '/ai/chat/sessions');

// Relative path (authenticatedFetch auto-resolves + adds /api/)
await authenticatedFetch('/ai/chat/sessions'); // resolveUrl() routes through buildBffApiUrl()
```

**Additional safety net**: `authenticatedFetch()` internally routes relative URLs through `buildBffApiUrl()`, so passing a relative path (with or without `/api/`) will always produce the correct URL.

> See: [`docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`](../../docs/architecture/AUTH-AND-BFF-URL-PATTERN.md) for the full reference
> See: [`.claude/patterns/auth/bff-url-normalization.md`](../patterns/auth/bff-url-normalization.md) for the helper pattern

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

- [Spaarke SSO Binding](../patterns/auth/spaarke-sso-binding.md) - **Canonical** — binding requirements + 6-strategy chain
- [OAuth Scopes](../patterns/auth/oauth-scopes.md) - Scope format and configuration
- [OBO Flow](../patterns/auth/obo-flow.md) - On-Behalf-Of token exchange
- [Token Caching](../patterns/auth/token-caching.md) - Server & client token caching
- [MSAL Client](../patterns/auth/DEPRECATED-msal-client.md) — ⛔ DEPRECATED, superseded by Spaarke Auth v2 (see [AUDIT-FINDINGS-AUTH-SYSTEM.md](../AUDIT-FINDINGS-AUTH-SYSTEM.md))
- [Spaarke Auth Initialization](../patterns/auth/DEPRECATED-spaarke-auth-initialization.md) — ⛔ DEPRECATED, bootstrap superseded by `useAuth()` (see [AUDIT-FINDINGS-AUTH-SYSTEM.md](../AUDIT-FINDINGS-AUTH-SYSTEM.md))
- [Xrm WebApi vs BFF Auth](../patterns/auth/xrm-webapi-vs-bff-auth.md) - Decision matrix

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

