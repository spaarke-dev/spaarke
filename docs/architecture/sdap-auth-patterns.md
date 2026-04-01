# SDAP Authentication Patterns

> **Last Updated**: March 2026
> **Applies To**: Authentication code, token handling, Azure AD configuration

---

## Nine-Pattern Taxonomy

| # | Pattern | When to Use |
|---|---------|-------------|
| 1 | **MSAL.js in PCF** | PCF controls acquiring BFF API tokens via `acquireTokenSilent` |
| 2 | **OBO for Graph API** | BFF calling Graph (file operations, send-as-user email) — user context required, Redis-cached 55-min TTL |
| 3 | **ClientSecret for Dataverse** | BFF metadata queries to Dataverse — no per-user context needed |
| 4 | **OBO for AI file access** | AI analysis downloading SPE files — app-only returns 403; user identity maps to SPE permissions |
| 5 | **OBO for Dataverse authorization** | AI authorization checks — `RetrievePrincipalAccess` does NOT work with OBO tokens; use direct query pattern instead |
| 6 | **App-only for email processing** | Webhooks and background jobs — no HttpContext, no user context |
| 7 | **MSAL ssoSilent for Code Pages** | React Code Pages that may be embedded as iframe or opened via navigateTo — dual strategy: Xrm platform strategies first, MSAL ssoSilent fallback |
| 8 | **Parent-to-Child token bridge** | Parent page shares `window.__SPAARKE_BFF_TOKEN__` with child dialog — eliminates 500–1300ms MSAL init |
| 9 | **resolveRuntimeConfig + initAuth for wizard code pages** | Standalone dialog code pages under `src/solutions/` — `fetch.bind(window)` returns 401; must use `authenticatedFetch` from `@spaarke/auth` |

---

## OBO vs App-Only Decision

| Use Case | Auth Type | Why |
|---------|-----------|-----|
| AI analysis (Document Profile, Playbooks) | OBO | SPE containers enforce user-level permissions; app-only returns 403 |
| PCF file upload | OBO | User is uploading their own files |
| Email webhook processing | App-only | No user context in webhook delivery |
| Background job file upload | App-only | No HttpContext in job handlers |
| Dataverse metadata queries (BFF) | ClientSecret | No per-user permissions needed |
| Dataverse authorization checks | OBO | Must query as the user to enforce row-level security |

---

## Pattern 2: OBO for Graph — Key Details

OBO exchanges the user's BFF token for a Graph token. The exchange result is cached in Redis (55-min TTL, keyed by SHA-256 hash of the input token). Cache hit: ~5ms. Cache miss (OBO exchange): ~200ms.

**Scope**: Always use `https://graph.microsoft.com/.default` (not individual Graph scopes).

---

## Pattern 4: OBO for AI File Access — Critical Rule

AI analysis services **must use OBO** (`DownloadFileAsUserAsync(httpContext, ...)`) to download SPE files. App-only tokens do not have SPE container-level permissions. `HttpContext` must be propagated through the entire call chain from endpoint to file access.

---

## Pattern 5: Dataverse Authorization — Direct Query Pattern

`RetrievePrincipalAccess` does NOT work with OBO (delegated) tokens — returns HTTP 404 (`0x80060888`). Use the direct query pattern instead: `GET /sprk_documents({id})?$select=sprk_documentid` with the OBO token. Success (200) = user has Read access; failure (403/404) = no access. This leverages Dataverse's native row-level security.

**Critical bug that was fixed**: After OBO exchange, the token must be explicitly set on the HttpClient (`_httpClient.DefaultRequestHeaders.Authorization = Bearer {oboToken}`). Forgetting this causes subsequent requests to use the old service principal token and return 401.

---

## Pattern 7: Code Page Dual-Strategy Auth

Token acquisition priority for React Code Pages:
1. In-memory cache
2. Xrm platform strategies (`getGlobalContext().getAccessToken()`, `__crmTokenProvider`, etc.) — works when page opened via `navigateTo`
3. MSAL `ssoSilent` (hidden iframe using existing Azure AD session cookie) — fallback for embedded iframe mode

**Authority**: Use `https://login.microsoftonline.com/organizations` for portability. Avoid hardcoding tenant ID (known tech debt in SemanticSearch and DocumentRelationshipViewer — should migrate to `@spaarke/auth`).

**Client ID**: Code Pages use `170c98e1-d486-4355-bcbe-170454e0207c` (DSM-SPE Dev 2). PCF controls use `5175798e-f23e-41c3-b09b-7a90b9218189`. Both are in `knownClientApplications` on the BFF app.

---

## Pattern 9: Wizard Code Pages — Tenant ID at Bootstrap

`resolveRuntimeConfig().tenantId` is **empty on first page load** — `Xrm.organizationSettings.tenantId` initializes asynchronously. After `initAuth()` completes, call `getAuthProvider().getTenantId()` to get the JWT-sourced tenant ID from `msal.getAllAccounts()[0].tenantId`. This is authoritative.

**`resolveTenantIdSync()` resolution order** (for synchronous click handlers):
1. MSAL authority URL (if tenant-specific authority was provided)
2. `msal.getAllAccounts()[0].tenantId` (JWT-sourced — correct post-auth)
3. `Xrm.organizationSettings.tenantId` (reliable for PCF, unreliable for standalone bootstrap)

---

## Common Mistakes

| Mistake | Error | Fix |
|---------|-------|-----|
| Wrong scope URI format | AADSTS500011: Resource principal not found | Use `api://{guid}/user_impersonation`, not friendly name |
| Missing Dynamics CRM delegated permission on BFF app | AADSTS65001: Consent required | Add `user_impersonation` delegated permission |
| Using app-only auth for AI file download | 403 Access Denied from SPE | Use `DownloadFileAsUserAsync(httpContext, ...)` (Pattern 4) |
| OBO token not set on HttpClient after exchange | 401 on Dataverse calls | `_httpClient.DefaultRequestHeaders.Authorization = Bearer {oboToken}` |
| Calling `RetrievePrincipalAccess` with OBO token | 404 not found (0x80060888) | Use direct query pattern (Pattern 5) |
| `authenticatedFetch={fetch.bind(window)}` in wizard | 401 on all BFF calls | Use `authenticatedFetch` from `@spaarke/auth` (Pattern 9) |
| Using `resolveRuntimeConfig().tenantId` before auth init | Empty string | Patch from `getAuthProvider().getTenantId()` after `initAuth()` |
| `DefaultContainerId` as raw GUID | Graph API rejects | Use Drive ID format (`b!xxx`) |
| No Application User in Dataverse | AADSTS500011 for ServiceClient | Create via Power Platform Admin Center |

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

## Related Documentation

| Document | Purpose |
|----------|---------|
| [auth-azure-resources.md](auth-azure-resources.md) | App registration GUIDs and Azure config |
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | BFF patterns that use these auth patterns |
| [email-to-document-architecture.md](email-to-document-architecture.md) | Pattern 6 (app-only) in detail |

---

*Last Updated: March 2026*
