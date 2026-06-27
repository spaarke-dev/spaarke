# Authentication & Authorization Constraints

> **Domain**: OAuth, OBO, Token Management, Access Control, Server Hardening
> **Source ADRs**: **ADR-028 (canonical — Spaarke Auth v2)**, ADR-003 (server seams), ADR-004 (OBO), ADR-008 (filters), ADR-009 (Redis caching)
> **Last Updated**: 2026-05-19 (Spaarke Auth v2 + Hardening sign-off)
> **Last Reviewed**: 2026-05-19
> **Status**: Verified · v2-aligned
>
> **Architecture**: [ADR-028 Spaarke Auth Architecture](../adr/ADR-028-spaarke-auth-architecture.md)
> **Client-side MSAL invariants**: [`spaarke-sso-binding.md`](../patterns/auth/spaarke-sso-binding.md) (INV-1..INV-8)
> **New-env setup**: [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md)
> **External Workspace SPA (B2B portal) is OUT OF SCOPE** for the internal contract — see [`docs/architecture/external-access-spa-architecture.md`](../../docs/architecture/external-access-spa-architecture.md). The External SPA legitimately uses `sessionStorage` (per-tab, multi-tenant guest isolation) and is allowed one D-AUTH-7 raw Bearer exception site.

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

### Client-side function-based contract (Spaarke Auth v2 — ADR-028)

- ✅ **MUST** use `useAuth()` (React) or `authenticatedFetch` (non-React) from `@spaarke/auth` — NEVER snapshot the token in component state
- ✅ **MUST** call `getAccessToken()` per-request (NOT once) when a raw token is needed (SSE/XHR D-AUTH-7 exception sites only) — fresh acquisition routes through MSAL's own cache
- ✅ **MUST** preserve INV-1..INV-8 MSAL configuration invariants — see [`spaarke-sso-binding.md`](../patterns/auth/spaarke-sso-binding.md)
- ✅ **MUST** resolve tenant via `sprk_TenantId` env var (primary) → Xrm frame-walk (fallback); prefer omitting `authority` so `@spaarke/auth` builds it from the tenant
- ✅ **MUST** rebuild AND redeploy every consumer of `@spaarke/auth` when the library version changes — INV-8 (Bundling Reality). A missed consumer is the popup-firing component.
- ✅ **MUST** when adding a NEW consumer with a local `authInit.ts` that exports an async `getTenantId`, use import alias on the runtimeConfig import (`getTenantId as getRuntimeTenantId`) to avoid silent runtime name collision. See memory `feedback-name-collision-in-consumer-authinit`.

### Authorization Architecture (ADR-003)

- ✅ **MUST** implement new auth logic as `IAuthorizationRule`
- ✅ **MUST** call authorization before `SpeFileStore` operations
- ✅ **MUST** cache UAC snapshots per-request only (not across requests)
- ✅ **MUST** include machine-readable deny codes (e.g., `sdap.access.deny.team_mismatch`)

### SPE File Access — Writer-Identity Matching (binding — Pattern 4, 2026-06-08)

- ✅ **MUST** match the identity that reads an SPE file with the identity that wrote it. The Spaarke MI is intentionally NOT registered as a guest app on the SPE container type — it is on its own writes' ACLs only.
- ✅ **MUST** dispatch post-upload RAG indexing synchronously in the OBO request scope via `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync(request, httpContext, ct)` for **any user-OBO-uploaded file** (Create* wizard uploads, SprkChat persist, PCF/Code Page uploads). A Service Bus job that runs under MI later will 403 on the SPE download.
- ✅ **MUST** use `IPostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync(request, ct)` (Service Bus enqueue) ONLY when the file was written by MI itself (Office Add-in finalize, Email-to-Document, post-analysis re-index).
- ❌ **MUST NOT** enqueue a Service Bus job that reads an SPE file written by a different identity than the job handler runs under. Background-job handlers run under MI (`_factory.ForApp()`); they cannot read user-OBO-uploaded files.
- ❌ **MUST NOT** wire new post-upload pipelines through `JobSubmissionService.SubmitJobAsync(RagIndexingJobPayload)` from request-scoped endpoints. Use the helper's OBO method.

> See: [`docs/architecture/sdap-auth-patterns.md` § Pattern 4 — Writer-Identity Matching Rule](../../docs/architecture/sdap-auth-patterns.md#writer-identity-matching-rule-binding--2026-06-08-post-phase-3a-uat-incident)

---

## MUST NOT Rules

### OAuth/OBO Flow (server-side; ADR-004)

- ❌ **MUST NOT** use individual Graph scopes in OBO (use `.default` only)
- ❌ **MUST NOT** store user tokens in plaintext (always hash)
- ❌ **MUST NOT** skip MSAL initialization step in MSAL v3+
- ❌ **MUST NOT** use friendly scope names (use `api://{GUID}/scope` format)

### Client-side (Spaarke Auth v2 — ADR-028)

- ❌ **MUST NOT** add `accessToken: string` or `token: string` props/fields anywhere in Spaarke code — the root-cause snapshot pattern v2 eliminates. Exception: third-party SDK contracts (Power BI `IReportEmbedConfiguration`, MSAL result objects) — these are NOT Spaarke BFF tokens. See memory `feedback-third-party-sdk-accesstoken-is-ok`.
- ❌ **MUST NOT** use `useState`/`useEffect` to snapshot a token in React state — the 401-after-refresh bug
- ❌ **MUST NOT** write raw `fetch(url, { headers: { Authorization: \`Bearer ${...}\` } })` template literals. Use `authenticatedFetch`. Limited D-AUTH-7 exceptions: SSE (EventSource), XHR uploads, Dataverse Web API direct calls (BFF-scoped wrapper would route wrong host), External SPA. Each MUST carry `// Auth v2 (D-AUTH-7):` justification comment.
- ❌ **MUST NOT** reference removed symbols: `BridgeStrategy`, `XrmStrategy`, `window.__SPAARKE_BFF_TOKEN__`, `tokenBridge.ts`, `publishToken`, `bffAuthProvider` — all deleted in v2
- ❌ **MUST NOT** use `sessionStorage` for the MSAL cache on internal surfaces (INV-1 violation; popup every new tab)
- ❌ **MUST NOT** set `storeAuthStateInCookie: false` (INV-2 violation)
- ❌ **MUST NOT** use `/organizations` or `/common` as the MSAL authority on internal surfaces (INV-3 violation)
- ❌ **MUST NOT** instantiate `PublicClientApplication` directly outside `@spaarke/auth` — every component must reuse `getAuthProvider()` (INV-7)

### Authorization Architecture (ADR-003)

- ❌ **MUST NOT** create new service layers for auth (use rules instead)
- ❌ **MUST NOT** make direct Graph/SPE calls outside `SpeFileStore`
- ❌ **MUST NOT** cache authorization decisions (cache data only)
- ❌ **MUST NOT** reuse UAC snapshots across requests/jobs

### @spaarke/auth Shared Library

- ✅ **MUST** use `@spaarke/auth` (`useAuth`, `authenticatedFetch`, `initAuth`, `resolveRuntimeConfig`, `buildBffApiUrl`) for all Code Pages + PCFs + React components that call BFF endpoints
- ✅ **MUST** call `initAuth()` once before rendering the React app (typically in `main.tsx` / `App.tsx`)
- ✅ **MUST** use lazy functions (not module-level constants) for any value derived from runtime config
- ✅ **MUST** when a consumer's `authInit.ts` re-exports an async `getTenantId`, use import alias on runtimeConfig (`getTenantId as getRuntimeTenantId`) — see memory `feedback-name-collision-in-consumer-authinit`
- ❌ **MUST NOT** use module-level `const X = getMsalClientId()` or `const X = getBffBaseUrl()` — these throw before bootstrap
- ❌ **MUST NOT** import auth helpers from legacy `bffAuthProvider.ts` (deleted in v2 task 031)
- ❌ **MUST NOT** add `@spaarke/auth` bootstrap to Code Pages that only use `Xrm.WebApi` (unnecessary overhead)
- ❌ **MUST NOT** create new `msalConfig.ts` files with module-level MSAL configuration constants

> See: [`.claude/patterns/auth/spaarke-sso-binding.md`](../patterns/auth/spaarke-sso-binding.md), [`xrm-webapi-vs-bff-auth.md`](../patterns/auth/xrm-webapi-vs-bff-auth.md)

### Server hardening (Spaarke Auth v2 Phase C — ADR-028)

- ✅ **MUST** use `Microsoft.Identity.Web`'s `AddMicrosoftIdentityWebApi` for inbound JWT validation (BFF + any new .NET API service)
- ✅ **MUST** use `DefaultAzureCredential` (managed identity) for ALL server outbound auth — Graph app-only, Dataverse service identity, Cosmos, Key Vault, AI Search, Service Bus. Exception: per-tenant SpeAdmin container-type ops (per-customer secrets from Key Vault) + OBO flow (OAuth spec requires confidential client + secret).
- ✅ **MUST** validate inbound webhooks via HMAC-SHA256 — fail-closed if signing key is null/empty (Communication + Email Service Endpoint webhooks)
- ✅ **MUST** route admin + bulk API key endpoints through named `AuthenticationHandler<>` schemes (`AuthSchemes.BuilderAdminApiKey`, `AuthSchemes.RagApiKey`); use `CryptographicOperations.FixedTimeEquals` for compare
- ✅ **MUST** guard `PostConfigure<JwtBearerOptions>` with `Interlocked.CompareExchange<int>` (or equivalent) to prevent double-application + handler stacking
- ✅ **MUST** enrich every authenticated server log with `oid`, `appid`, `obo`, `tenantId`, `correlationId` via `ILogger.BeginScope` — see `AuditEnrichmentMiddleware`
- ✅ **MUST** apply rate-limit policies to anonymous + API-key endpoints — webhook-graph (sliding 600/min IP-keyed), api-key-* (auth_scheme-keyed)
- ❌ **MUST NOT** add `/debug/*` endpoints on the BFF — all 11 removed in Phase C task 043
- ❌ **MUST NOT** add plaintext secrets to `appsettings*.json` — Key Vault references only in production; dev OK with plain values (per project's risk tier)
- ❌ **MUST NOT** use `TenantId: "common"` in appsettings — always tenant-specific; templates use `#{TENANT_ID}#` placeholder for CI/CD substitution

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

- [Spaarke SSO Binding](../patterns/auth/spaarke-sso-binding.md) - **Canonical** — MSAL invariants (INV-1..INV-8) + v2 token acquisition model
- [OAuth Scopes](../patterns/auth/oauth-scopes.md) - Scope format and configuration
- [OBO Flow](../patterns/auth/obo-flow.md) - On-Behalf-Of token exchange
- [Token Caching](../patterns/auth/token-caching.md) - Server-side Redis OBO cache (client-side cascade retired in v2)
- [Xrm WebApi vs BFF Auth](../patterns/auth/xrm-webapi-vs-bff-auth.md) - Decision matrix
- [auth-deployment-setup.md](../../docs/guides/auth-deployment-setup.md) - Operator runbook for new-environment setup

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

