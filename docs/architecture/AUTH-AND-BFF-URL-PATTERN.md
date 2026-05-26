---
> **Auth v2 / [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) status (2026-05-19)**
>
> - **`buildBffApiUrl()` URL construction (§"The Golden Rule")** — canonical in v2, unchanged.
> - **Token acquisition (§"Token Acquisition (Spaarke Auth v2)")** — updated to v2 two-layer model (`InMemoryCache` over pluggable `AuthStrategy`). The retired 6-strategy cascade is documented as "Pre-v2 historical".
> - **MUST NOT** reference `BridgeStrategy`, `XrmStrategy`, `MsalSilentStrategy`, `MsalRedirectStrategy`, `window.__SPAARKE_BFF_TOKEN__`, `tokenBridge`, `accessToken: string` typed props.
> - Use `useAuth()` + `authenticatedFetch` from `@spaarke/auth`.
>
> See also: [`auth-deployment-setup.md`](../guides/auth-deployment-setup.md) for the operator runbook.
---

# BFF Authentication & URL Construction Pattern

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2 + /api helper consolidation
> **Status**: Verified — helper-based construction mandatory as of 2026-04-05
> **Audience**: Developers, AI agents (Claude Code), CI/CD pipelines

---

## The Golden Rule (Updated 2026-04-05)

**Use `buildBffApiUrl()`. Always. Template literals are banned.**

```typescript
// ✅ CORRECT
import { buildBffApiUrl } from '@spaarke/auth';
const url = buildBffApiUrl(bffBaseUrl, '/ai/visualization/related/123');

// ❌ WRONG — banned
const url = `${bffBaseUrl}/api/ai/visualization/related/123`;
```

**Why**: `bffBaseUrl` is HOST ONLY (no `/api` suffix). Callers previously had to manually add `/api/` when building fetch URLs. This manual step was error-prone and caused multiple production bugs:

- Missing `/api/` → 404
- Duplicate `/api/api/` → 404
- Different call sites in the same component disagreeing on the format

The `buildBffApiUrl()` helper is idempotent and makes this class of bug impossible.

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

**Safe usage with helper (MANDATORY):**
```typescript
import { resolveRuntimeConfig, buildBffApiUrl, authenticatedFetch } from '@spaarke/auth';

const config = await resolveRuntimeConfig();
const url = buildBffApiUrl(config.bffBaseUrl, '/documents/123');
const response = await authenticatedFetch(url);
```

### Path B: PCF `environmentVariables.ts` (PCF Controls)

PCF controls query Dataverse environment variables via `Xrm.WebApi`. The `getApiBaseUrl()` function normalizes the raw value and returns HOST ONLY:

```typescript
import { getApiBaseUrl, buildBffApiUrl } from '../shared/utils/environmentVariables';
import { authenticatedFetch } from '@spaarke/auth';

const base = await getApiBaseUrl(context.webAPI);
// Returns HOST ONLY: "https://spe-api-dev-67e2xz.azurewebsites.net"

const url = buildBffApiUrl(base, '/ai/visualization/related/123');
// → "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/visualization/related/123"

const response = await authenticatedFetch(url);
```

**The helper is also exported from `@spaarke/auth`** for parity. PCFs that already import from `@spaarke/auth` may use either import.

**Idempotency guarantee** — these all produce the identical output:

| Input path           | Output                            |
|---------------------|-----------------------------------|
| `/ai/search`        | `{base}/api/ai/search`            |
| `/api/ai/search`    | `{base}/api/ai/search` (same)     |
| `ai/search`         | `{base}/api/ai/search` (same)     |

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

## Token Acquisition (Spaarke Auth v2 — ADR-028)

`SpaarkeAuthProvider.getAccessToken()` uses a simplified two-layer model. Canonical references: [ADR-028](../adr/ADR-028-spaarke-auth-architecture.md), [`spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md).

| Layer | Speed | Behavior |
|-------|-------|----------|
| **InMemoryCache wrapper** | ~0.1ms | Validates JWT `exp` with 5-min buffer. Returns cached token if fresh; otherwise delegates to strategy. |
| **AuthStrategy (pluggable)** | varies | `BrowserMsalStrategy` (Dataverse PCFs + Code Pages) or `OfficeNaaStrategy` (Office Add-ins) |

`BrowserMsalStrategy.acquire()` tries MSAL `acquireTokenSilent` → `ssoSilent` → `acquireTokenPopup` in order. MSAL's `localStorage` cache handles cross-tab/iframe sharing (same-origin browser SOP). Required MSAL config (INV-1..INV-3): `cacheLocation: 'localStorage'`, `storeAuthStateInCookie: true`, tenant-specific authority via `resolveDefaultAuthority()`.

> **Pre-v2 historical**: The original 6-strategy cascade (CacheStrategy → SessionStorageStrategy → BridgeStrategy → XrmStrategy → MsalSilentStrategy → MsalPopupStrategy) was **deleted in Phase A of Spaarke Auth v2** (commits leading up to `e649f244`). Cross-iframe sharing via `window.__SPAARKE_BFF_TOKEN__` (BridgeStrategy) and `__spaarke_bff_token_cache__` (SessionStorageStrategy) were retired because MSAL's `localStorage` cache covers their use cases with less complexity. `XrmStrategy` was retired because Xrm tokens are Dataverse-scoped only and never work for BFF API calls. See ADR-028 §"What was retired" for the full rationale.

If all silent paths fail, `BrowserMsalStrategy` falls back to `acquireTokenPopup`. Firing the popup in steady state is a regression (likely INV-3 violation — authority `/organizations` or `/common`).

**Diagnostic logging** (Auth v2 — `BrowserMsalStrategy` + `InMemoryCache`):
```
[SpaarkeAuth:BrowserMsal] acquireTokenSilent OK — scope: api://1e40baad-.../SDAP.Access
[SpaarkeAuth:InMemoryCache] hit (exp valid + 5min buffer)
```

If silent acquisition fails (cache miss + MSAL fallback chain exhausted):
```
[SpaarkeAuth:BrowserMsal] silent paths exhausted; falling back to interactive — clientId: "1e40baad...", tenantId: "{tenant-guid}"
[SpaarkeAuth] InteractionRequiredAuthError → redirect (popup fallback is regression — likely INV-3 violation)
```

> **Pre-v2 historical**: Earlier logs referenced `[SpaarkeAuth:MsalSilent]` (strategy class) and "All 6 token strategies failed" (the retired 6-strategy cascade). Both were deleted in Phase A.

---

## `authenticatedFetch()` Behavior

```typescript
authenticatedFetch(url, init?)
```

1. **Resolves relative URLs via `buildBffApiUrl()`**: Any relative URL (with or without `/api/`) is routed through the helper, guaranteeing a correct `/api/` prefix. This is a safety net for the recurring `/api` missing / duplicated bug.
   - `/ai/chat/sessions` → `${bffBaseUrl}/api/ai/chat/sessions`
   - `/api/ai/chat/sessions` → `${bffBaseUrl}/api/ai/chat/sessions` (same)
2. **Acquires token** via `SpaarkeAuthProvider.getAccessToken()`
3. **Sets `Authorization: Bearer <token>`** header
4. **On 401**: Clears cache, retries with exponential backoff (500ms, 1000ms, 2000ms)
5. **After 3 failed retries**: Throws `AuthError('Authentication failed after all retry attempts', 'auth_exhausted')`
6. **On other HTTP errors**: Throws `ApiError` with RFC 7807 ProblemDetails

**Preferred call style**:
```typescript
// Option 1: Build URL explicitly (works with fetch() or authenticatedFetch())
const url = buildBffApiUrl(config.bffBaseUrl, '/ai/chat/sessions');
await authenticatedFetch(url);

// Option 2: Pass relative path (authenticatedFetch resolves via buildBffApiUrl internally)
await authenticatedFetch('/ai/chat/sessions');
```

---

## Dataverse Environment Variables

| Variable | Value Format | Example | Used By |
|----------|-------------|---------|---------|
| `sprk_BffApiBaseUrl` | URL **with** `/api` | `https://spe-api-dev-67e2xz.azurewebsites.net/api` | All modules (normalized on read) |
| `sprk_BffApiAppId` | Azure AD App ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | OAuth scope construction |
| `sprk_MsalClientId` | Azure AD Client ID | `170c98e1-...` | MSAL initialization |
| `sprk_TenantId` | Azure AD Tenant ID | `a1b2c3d4-...` | Tenant isolation |

**Important**: `sprk_BffApiBaseUrl` is stored WITH `/api` for historical reasons. All code MUST strip it before use.

> ⚠️ **Cross-environment consistency**: The `sprk_BffApiBaseUrl` env var must use the **same** format across all envs (dev, demo, prod). `normalizeUrl()` in `@spaarke/auth` strips a trailing `/api` if present, so the value is functionally equivalent whether stored as `https://host.azurewebsites.net` or `https://host.azurewebsites.net/api`. **However**: PCF controls and Code Pages deployed to multiple environments must all behave the same — inconsistent formats cause confusing operator handoffs. **Recommendation**: store host-only (no `/api`) for clarity. Phase 5 of the `sdap-bff-api-remediation-fix` project ([EXECUTION-LOG.md](../../projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md) §Task 060 step 9) aligned demo with dev's `/api`-suffixed value to match the operator-visible setting, even though runtime behavior is identical.

---

## Checklist: Adding a New BFF API Integration

- [ ] Use `@spaarke/auth`'s `authenticatedFetch()` — never raw `fetch()` for BFF calls
- [ ] Get base URL from `resolveRuntimeConfig()` (code pages) or `getApiBaseUrl()` (PCF) — both return HOST ONLY
- [ ] Construct URLs as `${bffBaseUrl}/api/your/endpoint` — you add the `/api` prefix
- [ ] Do NOT add your own `/api`-stripping normalization — the source functions handle it
- [ ] Do NOT read `sprk_BffApiBaseUrl` directly — always go through the resolution functions
- [ ] Handle `AuthError` with code `'auth_exhausted'` gracefully in UI
- [ ] Handle `ApiError` with ProblemDetails for user-friendly error messages
- [ ] For tenant ID: use `getAuthProvider().getTenantId()` — it reads the JWT `tid` claim from any token source
- [ ] **Verify `sprk_BffApiBaseUrl` value matches dev's format** (currently `/api`-suffixed) across all target Dataverse environments for operator consistency

---

## Troubleshooting

### "Something went wrong" / 404 Not Found
**Symptom**: `POST https://host/api/api/ai/search 404`
**Cause**: Double `/api` — base URL includes `/api` and endpoint path also starts with `/api/`
**Fix**: Normalize the base URL in the service constructor (strip `/api`)

### 401 Unauthorized (all retries exhausted)
**Symptom**: `AuthError: Authentication failed after all retry attempts`
**Check**:
1. Console for `[SpaarkeAuth] All 6 token strategies failed` — shows config values
2. Is `bffApiScope` non-empty? (should be `api://<appId>/user_impersonation`)
3. Is `clientId` correct? (must match Azure AD app registration)
4. Is the `authority` value tenant-specific? `/organizations` or `/common` means an un-rebuilt consumer is using the old library — rebuild + redeploy that PCF or Code Page
5. Is MSAL popup blocked by browser? (check popup blocker)
6. Hard refresh (`Ctrl+Shift+R`) to clear stale localStorage

### "Missing Parameters — tenantId: (missing)"
**Symptom**: Dialog opens but tenant ID is empty
**Root cause (discovered April 2, 2026)**:
1. `Xrm.organizationSettings.tenantId` is empty at bootstrap (Dataverse timing issue — always empty on first load)
2. The token comes via **bridge strategy** (parent iframe), so **MSAL is never invoked**
3. `getTenantId()` checked MSAL accounts → empty (MSAL never ran)
4. Result: no tenant ID anywhere

**Fix (implemented)**: `SpaarkeAuthProvider.getTenantId()` and `getCachedTenantId()` now extract the `tid` claim directly from the cached access token JWT. This works for ALL token sources (bridge, MSAL, Xrm) because every Azure AD JWT contains `tid`. See `_extractTidFromCachedToken()` in `SpaarkeAuthProvider.ts`.

**Resolution order** (current):
1. Cached token JWT `tid` claim ← works even when bridge provides the token
2. MSAL `getAllAccounts()[0].tenantId` ← only populated if MSAL was used
3. Xrm frame-walk `organizationSettings.tenantId` ← empty on first load

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
