# SDAP Authentication Patterns Architecture

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Nine-pattern authentication taxonomy governing how every component in SDAP acquires tokens and authorizes operations.

---

## Overview

SDAP authentication is organized into nine distinct patterns that cover every token acquisition and exchange scenario across the platform — from PCF controls embedded on Dataverse forms, to background job processing with no user context, to standalone React Code Pages bootstrapping MSAL in an iframe.

The key architectural decision is the split between **On-Behalf-Of (OBO)** and **app-only** authentication. SharePoint Embedded (SPE) containers enforce user-level permissions, meaning app-only tokens receive 403 on file operations. This forces OBO for all user-facing file access and drives the design of `GraphClientFactory`, which provides `ForUserAsync()` (OBO, per-request) and `ForApp()` (app-only, cached singleton).

Authorization is enforced at the endpoint level via endpoint filters (ADR-008) — there is no global authorization middleware. Each filter implements `IEndpointFilter` and is attached to specific endpoint groups using extension methods like `.AddDocumentAuthorizationFilter("read")`.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| `GraphClientFactory` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` | Creates Graph clients via OBO (`ForUserAsync`) or app-only (`ForApp`). OBO tokens cached in Redis. App-only client cached as Lazy singleton. |
| `IGraphClientFactory` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs` | Contract: `ForUserAsync(HttpContext, CancellationToken)` and `ForApp()` |
| `SimpleTokenCredential` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SimpleTokenCredential.cs` | Wraps a pre-acquired OBO access token as `TokenCredential` for Graph SDK v5 |
| `GraphTokenCache` | `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Redis cache for OBO tokens. SHA256-hashed keys. 55-min TTL. Graceful degradation on cache failure. |
| `TokenHelper` | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/TokenHelper.cs` | Extracts bearer token from `Authorization` header. Throws `UnauthorizedAccessException` if missing. |
| `AuthorizationService` | `src/server/shared/Spaarke.Core/Auth/AuthorizationService.cs` | Chain-of-responsibility rule evaluator. Queries Dataverse for access snapshot, evaluates `IAuthorizationRule` chain, fail-closed on error. |
| `DocumentAuthorizationFilter` | `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` | Endpoint filter for document operations. Extracts user ID from claims, resource ID from route. |
| `AiAuthorizationFilter` | `src/server/api/Sprk.Bff.Api/Api/Filters/AiAuthorizationFilter.cs` | Endpoint filter for AI endpoints. Extracts document IDs from request body, validates via `IAiAuthorizationService`. |
| `EntityAccessFilter` | `src/server/api/Sprk.Bff.Api/Api/Filters/EntityAccessFilter.cs` | Validates user can associate documents with target entity. Runs after `OfficeAuthFilter`. |
| `SpeFileStore` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` | Facade delegating to `ContainerOperations`, `DriveItemOperations`, `UploadSessionManager`, `UserOperations` |
| `DriveItemOperations` | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs` | File listing, download, delete. Uses `IGraphClientFactory` for both app-only and OBO clients. |

### Endpoint Filter Inventory

| Filter | Applied To | Authorization Model |
|--------|-----------|-------------------|
| `DocumentAuthorizationFilter` | Document CRUD endpoints | Claims-based user ID + route resource ID |
| `AiAuthorizationFilter` | AI analysis endpoints | Claims-based user ID + request body document IDs |
| `AnalysisAuthorizationFilter` | Analysis endpoints | Document-level access |
| `CommunicationAuthorizationFilter` | Email/communication endpoints | Communication permissions |
| `EntityAccessFilter` | Office save endpoints | Entity association permissions |
| `ExternalCallerAuthorizationFilter` | External-facing endpoints | External caller validation |
| `FinanceAuthorizationFilter` | Finance endpoints | Finance feature access |
| `JobOwnershipFilter` | Background job endpoints | Job ownership verification |
| `OfficeAuthFilter` | Office add-in endpoints | Office token validation |
| `PlaybookAuthorizationFilter` | Playbook endpoints | Playbook access |
| `RecordSearchAuthorizationFilter` | Record search endpoints | Search permissions |
| `SemanticSearchAuthorizationFilter` | Semantic search endpoints | Search access |
| `SpeAdminAuthorizationFilter` | SPE admin endpoints | Admin role verification |
| `TenantAuthorizationFilter` | Tenant-scoped endpoints | Tenant membership |
| `WorkspaceAuthorizationFilter` | Workspace endpoints | Workspace membership |
| `WorkspaceLayoutAuthorizationFilter` | Layout endpoints | Layout permissions |
| `VisualizationAuthorizationFilter` | Visualization endpoints | Visualization access |

---

## Nine-Pattern Taxonomy

| # | Pattern | When to Use | Auth Type |
|---|---------|-------------|-----------|
| 1 | **MSAL.js in PCF** | PCF controls acquiring BFF API tokens via `acquireTokenSilent` | Delegated |
| 2 | **OBO for Graph API** | BFF calling Graph for file operations, send-as-user email — user context required | OBO (delegated) |
| 3 | **ClientSecret for Dataverse** | BFF metadata queries to Dataverse — no per-user context needed | App-only |
| 4 | **OBO for AI file access** | AI analysis downloading SPE files — app-only returns 403; user identity maps to SPE permissions | OBO (delegated) |
| 5 | **OBO for Dataverse authorization** | AI authorization checks — `RetrievePrincipalAccess` does NOT work with OBO tokens; use direct query pattern | OBO (delegated) |
| 6 | **App-only for email processing** | Webhooks and background jobs — no HttpContext, no user context | App-only |
| 7 | **MSAL ssoSilent for Code Pages** | React Code Pages embedded as iframe or opened via `navigateTo` — dual strategy: Xrm platform first, MSAL ssoSilent fallback | Delegated |
| 8 | **Parent-to-Child token bridge** | Parent page shares `window.__SPAARKE_BFF_TOKEN__` with child dialog — eliminates 500-1300ms MSAL init | Token pass-through |
| 9 | **resolveRuntimeConfig + initAuth for wizard code pages** | Standalone dialog code pages under `src/solutions/` — `fetch.bind(window)` returns 401; must use `authenticatedFetch` from `@spaarke/auth` | Delegated |

---

## Data Flow

### OBO Token Exchange (Pattern 2 — Primary Flow)

1. **PCF/Code Page** sends request to BFF API with bearer token (audience: `api://{BFF-AppId}/user_impersonation`)
2. **TokenHelper.ExtractBearerToken** extracts the token from the `Authorization` header
3. **GraphClientFactory.ForUserAsync** receives the HttpContext
4. **GraphTokenCache** computes SHA256 hash of the user token and checks Redis
5. **Cache hit** (~5ms): Returns cached Graph token, wraps in `SimpleTokenCredential`, creates `GraphServiceClient`
6. **Cache miss** (~200ms): MSAL `AcquireTokenOnBehalfOf` exchanges user token for Graph token with scope `https://graph.microsoft.com/.default`
7. **GraphTokenCache** stores the new Graph token in Redis (55-min TTL, keyed by SHA256 hash)
8. **GraphServiceClient** is returned, configured with the OBO token and `IHttpClientFactory`-provided HttpClient (retry, circuit breaker, timeout)

### App-Only Flow (Pattern 3, 6)

1. **GraphClientFactory.ForApp** returns a cached `Lazy<GraphServiceClient>` singleton
2. The singleton uses `ClientSecretCredential` with `TENANT_ID`, `API_APP_ID`, `API_CLIENT_SECRET`
3. Scope: `https://graph.microsoft.com/.default` via `AzureIdentityAuthenticationProvider`
4. Uses beta endpoint (`https://graph.microsoft.com/beta`) for SPE admin operations

### Endpoint Authorization Flow (ADR-008)

1. Request arrives at endpoint with `.AddEndpointFilter<XxxAuthorizationFilter>()`
2. Filter extracts user identity from JWT claims (`oid` → `objectidentifier` → `NameIdentifier` fallback chain)
3. Filter extracts resource ID from route values or request body
4. Filter calls `AuthorizationService.AuthorizeAsync(context)`
5. `AuthorizationService` fetches access snapshot from Dataverse via `IAccessDataSource`
6. Ordered `IAuthorizationRule` chain evaluates: first non-Continue decision wins
7. **Default-deny**: if no rule decides, authorization is denied (fail-closed)
8. On error: fail-closed with deny + audit log

---

## OBO vs App-Only Decision Matrix

| Use Case | Auth Type | Why |
|---------|-----------|-----|
| AI analysis (Document Profile, Playbooks) | OBO | SPE containers enforce user-level permissions; app-only returns 403 |
| PCF file upload / download | OBO | User is accessing their own files; SPE enforces permissions |
| Email webhook processing | App-only | No user context in webhook delivery |
| Background job file upload | App-only | No HttpContext in job handlers |
| Dataverse metadata queries (BFF) | ClientSecret | No per-user permissions needed |
| Dataverse authorization checks | OBO | Must query as the user to enforce row-level security |
| Container creation / admin | App-only | Platform-level operation, not user-scoped |
| Folder listing (cache-aside) | App-only | `DriveItemOperations` uses `ForApp()` for cached listings |

---

## Pattern Details

### Pattern 2: OBO for Graph — GraphClientFactory

OBO exchanges the user's BFF token for a Graph token. The exchange result is cached in Redis (55-min TTL, keyed by SHA-256 hash of the input token). Cache hit: ~5ms. Cache miss (OBO exchange): ~200ms.

**Scope**: Always use `https://graph.microsoft.com/.default` (not individual Graph scopes). Using individual scopes causes AADSTS70011 errors.

**App-only client**: Cached as `Lazy<GraphServiceClient>` singleton — credential and auth provider are stateless and thread-safe. OBO clients remain per-request since they depend on user tokens.

**API version**: OBO clients use v1.0 endpoint. App-only clients use beta endpoint (for SPE admin operations).

### Pattern 4: OBO for AI File Access

AI analysis services **must use OBO** (`DownloadFileAsUserAsync(httpContext, ...)`) to download SPE files. App-only tokens do not have SPE container-level permissions. `HttpContext` must be propagated through the entire call chain from endpoint to file access: endpoint handler -> service -> `DriveItemOperations` -> `IGraphClientFactory.ForUserAsync(httpContext)`.

### Pattern 5: Dataverse Authorization — Direct Query Pattern

`RetrievePrincipalAccess` does NOT work with OBO (delegated) tokens — returns HTTP 404 (`0x80060888`). Use the direct query pattern instead: `GET /sprk_documents({id})?$select=sprk_documentid` with the OBO token. Success (200) = user has Read access; failure (403/404) = no access. This leverages Dataverse's native row-level security.

**Critical bug that was fixed**: After OBO exchange, the token must be explicitly set on the HttpClient (`_httpClient.DefaultRequestHeaders.Authorization = Bearer {oboToken}`). Forgetting this causes subsequent requests to use the old service principal token and return 401.

### Pattern 7: Code Page Dual-Strategy Auth

Token acquisition priority for React Code Pages:
1. In-memory cache
2. Xrm platform strategies (`getGlobalContext().getAccessToken()`, `__crmTokenProvider`, etc.) — works when page opened via `navigateTo`
3. MSAL `ssoSilent` (hidden iframe using existing Azure AD session cookie) — fallback for embedded iframe mode

**Authority**: Use `https://login.microsoftonline.com/organizations` for portability. Avoid hardcoding tenant ID (known tech debt in SemanticSearch and DocumentRelationshipViewer — should migrate to `@spaarke/auth`).

**Client IDs**: Code Pages use `170c98e1-d486-4355-bcbe-170454e0207c` (DSM-SPE Dev 2). PCF controls use `5175798e-f23e-41c3-b09b-7a90b9218189`. Both are in `knownClientApplications` on the BFF app.

### Pattern 9: Wizard Code Pages — Tenant ID at Bootstrap

`resolveRuntimeConfig().tenantId` is **empty on first page load** — `Xrm.organizationSettings.tenantId` initializes asynchronously. After `initAuth()` completes, call `getAuthProvider().getTenantId()` to get the JWT-sourced tenant ID from `msal.getAllAccounts()[0].tenantId`. This is authoritative.

**`resolveTenantIdSync()` resolution order** (for synchronous click handlers):
1. MSAL authority URL (if tenant-specific authority was provided)
2. `msal.getAllAccounts()[0].tenantId` (JWT-sourced — correct post-auth)
3. `Xrm.organizationSettings.tenantId` (reliable for PCF, unreliable for standalone bootstrap)

---

## Known Pitfalls

| Pitfall | Symptom | Root Cause | Fix |
|---------|---------|------------|-----|
| Wrong scope URI format | AADSTS500011: Resource principal not found | Using friendly name instead of `api://{guid}/user_impersonation` | Use full URI: `api://{guid}/user_impersonation` |
| Missing Dynamics CRM delegated permission | AADSTS65001: Consent required | BFF app registration lacks `user_impersonation` delegated permission for Dynamics CRM | Add permission in Azure AD portal |
| App-only auth for AI file download | 403 Access Denied from SPE | SPE containers enforce user-level permissions | Use `DownloadFileAsUserAsync(httpContext, ...)` (Pattern 4) |
| OBO token not set on HttpClient after exchange | 401 on Dataverse calls | Code uses old service principal token instead of freshly-exchanged OBO token | Explicitly set `_httpClient.DefaultRequestHeaders.Authorization = Bearer {oboToken}` |
| Calling `RetrievePrincipalAccess` with OBO token | 404 not found (0x80060888) | API does not support delegated tokens | Use direct query pattern (Pattern 5) |
| `authenticatedFetch={fetch.bind(window)}` in wizard | 401 on all BFF calls | Native fetch has no auth headers | Use `authenticatedFetch` from `@spaarke/auth` (Pattern 9) |
| `resolveRuntimeConfig().tenantId` before auth init | Empty string / wrong tenant | Xrm.organizationSettings loads asynchronously | Patch from `getAuthProvider().getTenantId()` after `initAuth()` |
| `DefaultContainerId` as raw GUID | Graph API rejects request | SPE requires Drive ID format | Use `b!xxx` Drive ID format |
| No Application User in Dataverse | AADSTS500011 for ServiceClient | Missing app registration in Dataverse | Create via Power Platform Admin Center |
| HttpContext not propagated to file access | 403 on SPE file download in AI pipeline | OBO requires the original HttpContext to extract the user token | Pass `httpContext` through entire call chain to `ForUserAsync()` |
| Individual Graph scopes in OBO | AADSTS70011: Invalid scope | OBO requires `.default` scope, not individual permissions | Use `https://graph.microsoft.com/.default` |
| Stale cached OBO token after permission change | Access persists after revocation | Redis caches tokens for 55 minutes | Token expires naturally; no manual invalidation mechanism |

---

## Token Scopes Reference

| Token For | Scope | Pattern |
|-----------|-------|---------|
| BFF API (PCF) | `api://1e40baad-.../user_impersonation` | Pattern 1 |
| BFF API (Code Page) | `api://1e40baad-.../user_impersonation` | Pattern 7 or 9 |
| Graph API (user file ops) | `https://graph.microsoft.com/.default` | Pattern 2 (OBO) |
| Graph API (email processing) | `https://graph.microsoft.com/.default` | Pattern 6 (ClientCredentials) |
| Dataverse (metadata) | `https://{org}.crm.dynamics.com/.default` | Pattern 3 (ClientCredentials) |
| Dataverse (authorization) | `https://{org}.crm.dynamics.com/.default` | Pattern 5 (OBO) |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | SPE File Operations | `IGraphClientFactory.ForUserAsync()` / `ForApp()` | Every file operation flows through GraphClientFactory |
| Consumed by | AI Analysis Pipeline | `DownloadFileAsUserAsync(httpContext)` via OBO | HttpContext must propagate from endpoint to file access |
| Consumed by | Email Processing | `IGraphClientFactory.ForApp()` | Background jobs have no user context |
| Consumed by | Office Add-ins | `OfficeAuthFilter` + `EntityAccessFilter` chain | Filter ordering matters: OfficeAuthFilter sets userId for EntityAccessFilter |
| Depends on | Redis (IDistributedCache) | `GraphTokenCache` | OBO token caching; graceful degradation on failure |
| Depends on | MSAL (ConfidentialClientApplication) | `AcquireTokenOnBehalfOf` | OBO exchange when cache misses |
| Depends on | Dataverse (IAccessDataSource) | `AuthorizationService.AuthorizeAsync` | Fetches access snapshots for rule evaluation |
| Depends on | Azure AD App Registrations | Token validation + OBO consent | BFF app, Code Page app, PCF app must all be in `knownClientApplications` |

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Endpoint filters for authorization | Per-endpoint `IEndpointFilter` | Resource-level auth varies by endpoint; global middleware cannot know the resource | ADR-008 |
| Redis-first OBO token caching | SHA256 hash key, 55-min TTL | Reduces Azure AD load by 97%; cache hit ~5ms vs ~200ms for OBO exchange | ADR-009 |
| Fail-closed authorization | Default deny if no rule matches or on error | Security-critical: unauthorized access is worse than a denied request | — |
| App-only client as Lazy singleton | `Lazy<GraphServiceClient>` | Credential and auth provider are stateless/thread-safe; eliminates per-call allocation | PPI-014 |
| OBO clients per-request | New `GraphServiceClient` per `ForUserAsync` call | Each client scoped to a specific user's token | — |
| `.default` scope for OBO | `https://graph.microsoft.com/.default` | Individual scopes cause AADSTS70011; `.default` uses all admin-consented permissions | — |

---

## Constraints

- **MUST** use OBO (`ForUserAsync`) for all user-facing SPE file operations — app-only returns 403
- **MUST** use endpoint filters (ADR-008) for resource authorization — no global middleware
- **MUST** propagate `HttpContext` through the full call chain for OBO operations
- **MUST** use `.default` scope for OBO token exchanges — individual scopes cause errors
- **MUST** cache OBO tokens in Redis (ADR-009) — 55-min TTL, SHA256-hashed keys
- **MUST NOT** inject `GraphServiceClient` directly into endpoints — use `SpeFileStore` facade (ADR-007)
- **MUST NOT** use `RetrievePrincipalAccess` with OBO tokens — use direct query pattern
- **MUST NOT** hardcode tenant ID in Code Page authority URLs — use `organizations` for portability
- **MUST NOT** use `fetch.bind(window)` for authenticated BFF calls in wizard code pages — use `authenticatedFetch` from `@spaarke/auth`

---

## Related

- [OBO Flow pattern pointer](../../.claude/patterns/auth/obo-flow.md) — GraphClientFactory code pointers
- [Token Caching pattern pointer](../../.claude/patterns/auth/token-caching.md) — Redis cache implementation
- [Dataverse OBO pattern pointer](../../.claude/patterns/auth/dataverse-obo.md) — Dataverse-specific OBO
- [Spaarke Auth Initialization pattern](../../.claude/patterns/auth/spaarke-auth-initialization.md) — Code Page bootstrap
- [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) — SpeFileStore facade (no Graph SDK leaks)
- [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint filters for auth
- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching
- [auth-azure-resources.md](auth-azure-resources.md) — App registration GUIDs and Azure config
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF patterns that use these auth patterns
- [email-processing-architecture.md](email-processing-architecture.md) — Pattern 6 (app-only) in detail

---

*Last Updated: 2026-04-05*
