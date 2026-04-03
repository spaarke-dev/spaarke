# BFF Authentication & URL Construction Pattern

> **Last Updated**: April 2, 2026
> **Status**: Authoritative reference — all BFF API integrations MUST follow this pattern
> **Audience**: Developers, AI agents (Claude Code), CI/CD pipelines

---

## The Golden Rule

```
bffBaseUrl = HOST ONLY (no /api suffix)
fetch URL  = ${bffBaseUrl}/api/...
```

**Every BFF API call in the Spaarke codebase follows this convention.** The `/api` prefix is the caller's responsibility, never part of the base URL.

---

## Why This Matters (The Double /api Bug)

The Dataverse environment variable `sprk_BffApiBaseUrl` stores the BFF URL **with** `/api` suffix:

```
sprk_BffApiBaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net/api"
```

If code reads this raw value and also prepends `/api/` to endpoint paths, the result is:

```
https://spe-api-dev-67e2xz.azurewebsites.net/api/api/ai/search  ← 404 Not Found
```

This has been a **recurring production issue**. The fix is normalization at the point of config resolution.

---

## Two Config Resolution Paths

### Path A: @spaarke/auth `resolveRuntimeConfig()` (Code Pages, Wizards, LegalWorkspace)

```typescript
import { resolveRuntimeConfig } from '@spaarke/auth';

const config = await resolveRuntimeConfig();
// config.bffBaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"  (HOST ONLY)
// normalizeUrl() strips trailing slashes and /api suffix
```

**How `normalizeUrl()` works** (in `@spaarke/auth/src/resolveRuntimeConfig.ts`):
```typescript
function normalizeUrl(raw: string): string {
  return raw
    .trim()
    .replace(/\/+$/, '')      // strip trailing slashes
    .replace(/\/api$/i, '');   // strip trailing /api
}
```

**Safe usage:**
```typescript
const response = await authenticatedFetch(`${config.bffBaseUrl}/api/documents/123`);
```

### Path B: PCF `environmentVariables.ts` (PCF Controls)

PCF controls query Dataverse environment variables via `Xrm.WebApi`:

```typescript
import { getApiBaseUrl } from '../shared/utils/environmentVariables';

const apiBaseUrl = await getApiBaseUrl(webApi);
// Returns the RAW env var value: "https://spe-api-dev-67e2xz.azurewebsites.net/api"
//                                                                              ^^^^
```

**Any service receiving this value MUST normalize it:**
```typescript
constructor(apiBaseUrl: string) {
  // Strip trailing slashes and /api to prevent double /api/api/
  this.apiBaseUrl = apiBaseUrl.replace(/\/+$/, '').replace(/\/api$/i, '');
}
```

Then construct URLs the same way:
```typescript
const endpoint = `${this.apiBaseUrl}/api/ai/search`;
```

---

## Auth Initialization Patterns

### Pattern 1: Code Pages & Wizards (React 18, bundled MSAL)

Bootstrap in `main.tsx`:
```typescript
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from '@spaarke/auth';

// 1. Resolve config from Dataverse env vars (pre-auth, uses session cookie)
const config = await resolveRuntimeConfig();

// 2. Store config in singleton + window globals
setRuntimeConfig(config);

// 3. Initialize MSAL auth (eagerly acquires token)
await initAuth({
  clientId: config.msalClientId,
  bffApiScope: config.bffOAuthScope,
  bffBaseUrl: config.bffBaseUrl,        // HOST ONLY
  proactiveRefresh: true,
});

// 4. Render app — authenticatedFetch() is ready to use anywhere
```

### Pattern 2: PCF Controls (React 16, platform-provided)

Auth init in `useEffect`:
```typescript
import { initAuth, authenticatedFetch } from '@spaarke/auth';
import { getApiBaseUrl, getEnvironmentVariable } from '../shared/utils/environmentVariables';

// Resolve config from Dataverse env vars via Xrm.WebApi
const apiBaseUrl = await getApiBaseUrl(webApi);
const tenantId = await getEnvironmentVariable(webApi, 'sprk_TenantId');
const clientAppId = await getEnvironmentVariable(webApi, 'sprk_MsalClientId');
const bffAppId = await getEnvironmentVariable(webApi, 'sprk_BffApiAppId');

// CRITICAL: Strip /api from the raw env var value
const normalizedBaseUrl = apiBaseUrl.replace(/\/+$/, '').replace(/\/api$/i, '');

await initAuth({
  clientId: clientAppId,
  bffApiScope: `api://${bffAppId}/user_impersonation`,
  bffBaseUrl: normalizedBaseUrl,         // HOST ONLY after normalization
  proactiveRefresh: true,
});
```

### Pattern 3: Office Add-ins (build-time config)

```typescript
// .env.production provides BFF_API_BASE_URL (HOST ONLY)
const apiBaseUrl = process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net';
// Token acquired via direct MSAL; not using @spaarke/auth (yet)
```

---

## Token Acquisition Strategy (5-Strategy Cascade)

`SpaarkeAuthProvider.getAccessToken()` tries these in order:

| # | Strategy | Speed | When It Works |
|---|----------|-------|---------------|
| 1 | **In-memory cache** | ~0.1ms | Token already acquired this session |
| 2 | **Bridge token** | ~0.1ms | Parent iframe published a token (dialog scenarios) |
| 3 | **Xrm platform** | ~10-50ms | Dataverse host exposes token via `getCurrentAppProperties()` |
| 4 | **MSAL silent** | ~100-200ms | Existing Azure AD session (acquireTokenSilent / ssoSilent) |
| 5 | **MSAL popup** | ~500-1300ms | Interactive login (last resort) |

If all strategies return null, `getAccessToken()` returns empty string `""`.
`authenticatedFetch()` will then send a request without a valid Bearer token → BFF returns 401.

**Diagnostic logging** (enabled in current build):
```
[SpaarkeAuth:MsalSilent] Accounts: 1 scope: api://1e40baad-.../user_impersonation
[SpaarkeAuth] Token acquired via MSAL silent
```

If all strategies fail:
```
[SpaarkeAuth] All 5 token strategies failed. Config: { clientId: "170c98e1...", ... }
```

---

## `authenticatedFetch()` Behavior

```typescript
authenticatedFetch(url, init?)
```

1. **Resolves relative URLs**: `/api/documents/123` → `${bffBaseUrl}/api/documents/123`
2. **Acquires token** via `SpaarkeAuthProvider.getAccessToken()`
3. **Sets `Authorization: Bearer <token>`** header
4. **On 401**: Clears cache, retries with exponential backoff (500ms, 1000ms, 2000ms)
5. **After 3 failed retries**: Throws `AuthError('Authentication failed after all retry attempts', 'auth_exhausted')`
6. **On other HTTP errors**: Throws `ApiError` with RFC 7807 ProblemDetails

---

## Dataverse Environment Variables

| Variable | Value Format | Example | Used By |
|----------|-------------|---------|---------|
| `sprk_BffApiBaseUrl` | URL **with** `/api` | `https://spe-api-dev-67e2xz.azurewebsites.net/api` | All modules (normalized on read) |
| `sprk_BffApiAppId` | Azure AD App ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | OAuth scope construction |
| `sprk_MsalClientId` | Azure AD Client ID | `170c98e1-...` | MSAL initialization |
| `sprk_TenantId` | Azure AD Tenant ID | `a1b2c3d4-...` | Tenant isolation |

**Important**: `sprk_BffApiBaseUrl` is stored WITH `/api` for historical reasons. All code MUST strip it before use.

---

## Checklist: Adding a New BFF API Integration

- [ ] Use `@spaarke/auth`'s `authenticatedFetch()` — never raw `fetch()` for BFF calls
- [ ] Obtain `bffBaseUrl` from `resolveRuntimeConfig()` (code pages) or normalize from `getApiBaseUrl()` (PCF)
- [ ] Verify `bffBaseUrl` is HOST ONLY (no `/api` suffix) before constructing URLs
- [ ] Construct URLs as `${bffBaseUrl}/api/your/endpoint`
- [ ] If accepting `apiBaseUrl` as constructor parameter, normalize it:
  ```typescript
  this.apiBaseUrl = apiBaseUrl.replace(/\/+$/, '').replace(/\/api$/i, '');
  ```
- [ ] Handle `AuthError` with code `'auth_exhausted'` gracefully in UI
- [ ] Handle `ApiError` with ProblemDetails for user-friendly error messages
- [ ] Test with both URL formats: with and without `/api` suffix

---

## Troubleshooting

### "Something went wrong" / 404 Not Found
**Symptom**: `POST https://host/api/api/ai/search 404`
**Cause**: Double `/api` — base URL includes `/api` and endpoint path also starts with `/api/`
**Fix**: Normalize the base URL in the service constructor (strip `/api`)

### 401 Unauthorized (all retries exhausted)
**Symptom**: `AuthError: Authentication failed after all retry attempts`
**Check**:
1. Console for `[SpaarkeAuth] All 5 token strategies failed` — shows config values
2. Is `bffApiScope` non-empty? (should be `api://<appId>/user_impersonation`)
3. Is `clientId` correct? (must match Azure AD app registration)
4. Is MSAL popup blocked by browser? (check popup blocker)
5. Hard refresh (`Ctrl+Shift+R`) to clear stale sessionStorage

### "Missing Parameters — tenantId: (missing)"
**Symptom**: Dialog opens but tenant ID is empty
**Cause**: `getTenantId()` returns empty string because:
1. `Xrm.organizationSettings.tenantId` was empty at bootstrap (timing issue)
2. MSAL auth init failed so `getCachedTenantId()` has no accounts
**Fix**: Ensure `ensureAuthInitialized()` completes before rendering; it patches tenant ID from MSAL JWT

---

## Audit Status (April 2, 2026)

All locations verified — no double `/api` vulnerabilities:

| Module | Files Checked | Status |
|--------|--------------|--------|
| `@spaarke/auth` (shared lib) | 8 | SAFE — `normalizeUrl()` strips `/api` |
| Code Pages (5 apps) | 15+ | SAFE — use `resolveRuntimeConfig()` |
| PCF Controls (6 controls) | 12+ | SAFE — defensive normalization in constructors |
| Solutions/Wizards (6 wizards) | 8+ | SAFE — use `resolveRuntimeConfig()` |
| Shared UI Components (adapters) | 5 | SAFE — accept host-only URL from caller |
| Office Add-ins | 4 | SAFE — build-time config, host-only default |
| PCF shared utils (`environmentVariables.ts`) | 1 | SAFE — default changed to host-only |
