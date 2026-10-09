# @spaarke/auth client library (a01-auth-library)

> Code root: master @ 45202953f. Area key: a01-auth-library.
>
> Scope: `src/client/shared/Spaarke.Auth/**` only. Consumers (PCFs, Code Pages, Office Add-ins, external SPA, BFF) are cross-area pointers only.
>
> Path shorthand used in sections 2-8: `LIB/` = `src/client/shared/Spaarke.Auth/src/`, `LIBT/` = `src/client/shared/Spaarke.Auth/tests/`. Every other path is written relative to the code root.
>
> Evidence tags: `[CODE path:line]` read in code; `[CONFIG path:line]` read in a config/manifest/lockfile; `[DOC-ONLY path:line]` stated in a document and NOT verified in code; `[LIVE?]` code cannot prove it. Where a doc disagrees with code, the code wins and the doc claim is listed in section 6.
>
> Controlling commit for current behaviour: `2598b2fb9` "fix(auth): B2B guests sign in against the environment's tenant on Dataverse surfaces (#1453) - tasks 122 + 124", 2026-10-08 17:22 -0400, which touched 20 of the library's files (+2195/-263) [CODE git log/show 2598b2fb9]. Library version string is still `2.0.0` [CODE LIB/version.ts:12] [CONFIG src/client/shared/Spaarke.Auth/package.json:3].

---

## 1. Scope and touchpoint inventory

### 1.1 Search patterns used

All run with ripgrep/grep over `src/client/shared/Spaarke.Auth/src` and `tests` (and, for cross-area existence checks only, over `src/**`):

| # | Pattern | Purpose |
|---|---|---|
| P1 | `organizations\|/common\|login.microsoftonline\|ciamlogin\|authority` | authority construction and multi-tenant fallbacks |
| P2 | `localStorage\|sessionStorage\|cacheLocation\|storeAuthStateInCookie\|BroadcastChannel(` | every cache / storage location |
| P3 | `acquireTokenSilent\|ssoSilent\|acquireTokenPopup\|logoutPopup\|handleRedirectPromise\|createNestablePublicClientApplication\|new PCA\|PublicClientApplication(\|getAllAccounts\|clearCache()` | every MSAL call |
| P4 | `__SPAARKE_[A-Z_]*__\|__spaarke_[a-z_]*__` | window globals and storage keys |
| P5 | `sprk_[A-Za-z]*` | Dataverse environment variables |
| P6 | `fetch(` | every network call |
| P7 | `\.Xrm\|getGlobalContext\|organizationSettings\|userSettings\|getClientUrl` | host (Xrm) reads |
| P8 | `new AuthError\|new ApiError\|throw new Error` | error paths |
| P9 | `INV-[0-9]` | invariant citations in code, tests and docs |
| P10 | `_MS = \|MAX_RETRIES\|RETRY_BASE` | timers and retry constants |
| P11 | `requireSilentOnly\|requireTenantAuthority\|knownAuthorities\|forceRefresh\|strategy:` | option plumbing and gaps |
| P12 | `^\s*(it\|test\|describe)(` over `tests/` | test-case inventory |
| P13 | `"@spaarke/auth":` over `src/**/package.json`; `new SpaarkeAuthProvider\|OfficeNaaStrategy\|getMsalInstance\|new PublicClientApplication` over `src/**` | cross-area existence only |
| V1 [ADDED by verifier] | `^\s*(it\|test)\.each` over `tests/` | parameterised test declarations missed by P12 |
| V2 [ADDED by verifier] | `Spaarke\.Auth/src` over consumer `tsconfig*.json`, `vite.config.*`, `webpack*.js` | whether consumers bundle the library from source or from `dist/` |
| V3 [ADDED by verifier] | `tenantId:` over `LIB/strategies/`; `getMsalInstance` over `LIB/` | whether `TokenResult.tenantId` is ever populated; library-internal `getMsalInstance` callers |
| V4 [ADDED by verifier] | `ConfigureHttpJsonOptions\|PropertyNamingPolicy\|JsonSerializerDefaults` over `src/server/api/Sprk.Bff.Api/**/*.cs` | `/api/config/client` wire casing evidence |
| V5 [ADDED by verifier] | `new PublicClientApplication\|createNestablePublicClientApplication` over `src/**` (content, not file list) | distinguish real constructions from comment mentions (the P13 false positive on `CommunicationPage`) |

### 1.2 File inventory (every file under the scope)

| path:line | what it is | kind |
|---|---|---|
| `src/client/shared/Spaarke.Auth/package.json:1-40` | package `@spaarke/auth` v2.0.0; `main: dist/index.js`; peerDependency `@azure/msal-browser ^3.0.0`; no `react` dependency | manifest |
| `src/client/shared/Spaarke.Auth/package-lock.json:47-48,60-61` | installed dev copies: `@azure/msal-browser 3.30.0`, `@azure/msal-common 14.16.1` | lockfile |
| `src/client/shared/Spaarke.Auth/tsconfig.json:1-32` | ES2020 / ESNext modules, `outDir dist`, tests excluded | build config |
| `src/client/shared/Spaarke.Auth/jest.config.js:1-9` | jsdom test environment, `tests/**/*.test.ts` | test config |
| `src/client/shared/Spaarke.Auth/.eslintrc.json:11-26` | lint rule banning `` `Bearer ${...}` `` template literals everywhere except `src/authenticatedFetch.ts` | lint config |
| `src/client/shared/Spaarke.Auth/.gitignore:1-20` | `dist/` and stray `src/**/*.js|.d.ts` ignored (library is consumed from source, see INV-8 in section 8) | repo config |
| `LIB/index.ts:1-65` | public export surface (types, errors, `resolveConfig`, `VERSION`, `resolveRuntimeConfig`, strategies, `SpaarkeAuthProvider`, `authenticatedFetch`, `buildBffApiUrl`, `initAuth`/`getAuthProvider`, `useAuth`, `resolveTenantIdSync`, `isValidTenant`, `createCodePageAuthInitializer`, `createRuntimeConfigStore`) | module |
| `LIB/types.ts:2-73` | `IAuthConfig` (clientId, authority, tenantId, redirectUri, bffApiScope, bffBaseUrl, proactiveRefresh, requireXrm, requireSilentOnly, requireTenantAuthority) | types |
| `LIB/types.ts:83-90` | `TokenResult` (`accessToken`, `expiresOn` unix-ms, optional `tenantId`) | types |
| `LIB/types.ts:97` | `AuthenticatedFetchFn` | types |
| `LIB/types.ts:100-107` | `IProblemDetails` (RFC 7807) | types |
| `LIB/types.ts:110-118` | window globals `__SPAARKE_MSAL_CLIENT_ID__`, `__SPAARKE_BFF_URL__`, `__SPAARKE_BFF_API_SCOPE__`, `__SPAARKE_TENANT_ID__` | types |
| `LIB/config.ts:7` | `DEFAULT_CLIENT_ID = undefined` (no built-in client id) | constant |
| `LIB/config.ts:17` | `FALLBACK_AUTHORITY = 'https://login.microsoftonline.com/organizations'` | constant |
| `LIB/config.ts:36-47` | `discoverTenantSync()` window → persisted runtime config → Xrm | function |
| `LIB/config.ts:55-81` | `resolveTenantSync()` authority → tenantId → `discoverTenantSync` | function |
| `LIB/config.ts:84,87,90` | `DEFAULT_BFF_SCOPE = undefined`; `TOKEN_EXPIRY_BUFFER_MS = 5 min`; `PROACTIVE_REFRESH_INTERVAL_MS = 4 min` | constants |
| `LIB/config.ts:101-162` | `resolveConfig()` merges config/window/defaults; throws on missing clientId; throws `tenant_unresolved` or falls back to `/organizations` | function |
| `LIB/tenant.ts:29,32` | `GUID_RE`, `DOMAIN_RE` tenant validators | constants |
| `LIB/tenant.ts:39-47` | `REJECTED_TENANTS` (`common`, `organizations`, `consumers`, `undefined`, `null`, MSA tenant GUID, empty GUID) | constant |
| `LIB/tenant.ts:49` | `AUTHORITY_HOST = 'https://login.microsoftonline.com'` (hard-coded commercial cloud) | constant |
| `LIB/tenant.ts:55-69` | `normalizeTenant()` / `isValidTenant()` | function |
| `LIB/tenant.ts:77-94` | `tenantFromAuthority()` (https only; 1 or 2 path segments; second must be `v2.0`; host NOT checked) | function |
| `LIB/tenant.ts:97-104` | `buildTenantAuthority()`, `sameTenant()` | function |
| `LIB/tenant.ts:107-152` | `hostFrames()`, `xrmGlobalContext()`, `xrmTenant()` frame-walk (window, parent, top) | function |
| `LIB/tenant.ts:155-183` | `DATAVERSE_HOST_RE` (dynamics.com, dynamics.cn, microsoftdynamics.us/.de, appsplatform.us); `isDataverseOrigin()`; `isDataverseHost()` | function |
| `LIB/initAuth.ts:8,16` | module-level singleton `_provider`; `_selection` promise serialising provider selection | state |
| `LIB/initAuth.ts:45-62` | `initAuth()` public entry (coalesce → serialised select → eager `getAccessToken()` on a new provider) | function |
| `LIB/initAuth.ts:69-86` | `coalesceWithoutDiscovery()` | function |
| `LIB/initAuth.ts:88-151` | `selectProvider()` (coalesce same clientId; upgrade `/organizations` provider; replace on different clientId) | function |
| `LIB/initAuth.ts:158-168` | `withDiscoveredTenant()` → async `discoverTenantId()` | function |
| `LIB/initAuth.ts:174-179` | `getAuthProvider()` throws `not_initialized` | function |
| `LIB/SpaarkeAuthProvider.ts:41-66` | constructor: `resolveConfig`, `requireXrm` check, default `BrowserMsalStrategy`, `InMemoryCache`, version log, logout-broadcast listener, proactive refresh | class |
| `LIB/SpaarkeAuthProvider.ts:69-88` | `getAccessToken()` (returns `''` on exhaustion, never throws) | method |
| `LIB/SpaarkeAuthProvider.ts:94-103` | `clearCache()` (in-memory only) / `clearAllCaches()` (cascades to strategy) | method |
| `LIB/SpaarkeAuthProvider.ts:118-122` | `logout()` broadcast + strategy logout | method |
| `LIB/SpaarkeAuthProvider.ts:125-168` | `isAuthenticated()`, `getConfig()`, `getCachedTenantId()`, `getTenantId()` | method |
| `LIB/SpaarkeAuthProvider.ts:181-216` | `dispose()`, `whenIdle()`, `supersede()` (#1453 replacement forwarding) | method |
| `LIB/SpaarkeAuthProvider.ts:218-240` | `_extractTidFromCachedToken()` (JWT `tid`), `_isXrmAvailable()` | private |
| `LIB/SpaarkeAuthProvider.ts:242-261` | `_startProactiveRefresh()` (4-min timer, gated on `isAuthenticated()`) | private |
| `LIB/strategies/AuthStrategy.ts:15-48` | `AuthStrategy` interface (`name`, `acquire`, `clearCache`, `logout`) | interface |
| `LIB/strategies/BrowserMsalStrategy.ts:7` | `EXPIRY_BUFFER_MS = 5 min` (duplicated constant) | constant |
| `LIB/strategies/BrowserMsalStrategy.ts:36-69` | `resolveLoginHint()` UPN from chosen account → `Xrm.userSettings.userPrincipalName`; never display name | function |
| `LIB/strategies/BrowserMsalStrategy.ts:75-84` | `decodeJwtExpMs()` | function |
| `LIB/strategies/BrowserMsalStrategy.ts:136-160` | MSAL `Configuration` (`localStorage`, `storeAuthStateInCookie: true`, `logLevel: 3`, `piiLoggingEnabled: false`), `_tenant` | ctor |
| `LIB/strategies/BrowserMsalStrategy.ts:162-218` | `acquire()` acquireTokenSilent → ssoSilent → acquireTokenPopup (suppressed by `requireSilentOnly`) | method |
| `LIB/strategies/BrowserMsalStrategy.ts:220-263` | `clearCache()`, `logout()` (logoutPopup → clearCache fallback), `getMsalInstance()` | method |
| `LIB/strategies/BrowserMsalStrategy.ts:278-299` | `_pickAccount()` (page account → single in-tenant account → single account → null), `_accept()` | private |
| `LIB/strategies/BrowserMsalStrategy.ts:306-347` | `_validate()` (JWT exp preferred, 5-min buffer), `_ensureInitialized()` (dynamic import, `new PCA`, `initialize`, `handleRedirectPromise`) | private |
| `LIB/strategies/OfficeNaaStrategy.ts:12,29-32` | `EXPIRY_BUFFER_MS`; `naaRedirectUri()` = `brk-multihub://<hostname>` | constant/function |
| `LIB/strategies/OfficeNaaStrategy.ts:65-70` | `IOfficeNaaConfig` (`fallbackRedirectUri`, `forceFallback`) | types |
| `LIB/strategies/OfficeNaaStrategy.ts:86-149` | `detectNaaSupport()` (Office globals; Office-on-the-web → false; PC build ≥ 13530; Mac ≥ 16.44; unknown → true) | function |
| `LIB/strategies/OfficeNaaStrategy.ts:206-256` | `acquire()` → `_tryAcquireWith()` acquireTokenSilent(`forceRefresh:false`) → acquireTokenPopup(`prompt:'select_account'` when no account) | method |
| `LIB/strategies/OfficeNaaStrategy.ts:258-307` | `clearCache()`, `logout()`, `isNaaActive()`, `getMsalInstance()` | method |
| `LIB/strategies/OfficeNaaStrategy.ts:314-358` | `_validate()`, `_clearStaleInteractionLock()` (deletes `*interaction.status*` sessionStorage keys), `_pickAccount()` = `accounts[0]` | private |
| `LIB/strategies/OfficeNaaStrategy.ts:360-403` | `_ensureInitialized()` (lock clear → NAA detect → `createNestablePublicClientApplication` or `new PublicClientApplication`) | private |
| `LIB/strategies/OfficeNaaStrategy.ts:412-463` | `_buildNaaConfig()` / `_buildFallbackConfig()` (`sessionStorage`, `storeAuthStateInCookie: false`, `allowRedirectInIframe: true`, `logLevel: 3`) | private |
| `LIB/strategies/OfficeNaaStrategy.ts:470-476` | `_drainRedirectIfAny()` | private |
| `LIB/strategies/InMemoryCache.ts:5,11-20` | `EXPIRY_BUFFER_MS`; `decodeJwtExpMs()` | constant/function |
| `LIB/strategies/InMemoryCache.ts:50-153` | `InMemoryCache` (`acquire` with single in-flight sharing and generation counter; `whenIdle`; `clearCache`; `logout`; `invalidate`; `getCachedToken`; `_isFresh`) | class |
| `LIB/authenticatedFetch.ts:7,10` | `RETRY_BASE_MS = 500`, `MAX_RETRIES = 3` | constants |
| `LIB/authenticatedFetch.ts:31-82` | `authenticatedFetch()` (no_token guard; Bearer header; 401 retry w/ backoff; `auth_exhausted`; ProblemDetails → `ApiError`) | function |
| `LIB/authenticatedFetch.ts:93-115` | `resolveUrl()` via `buildBffApiUrl`; `tryParseProblemDetails()` | function |
| `LIB/useAuth.ts:13-58` | `UseAuthResult` + `useAuth()` (plain function; no React) | function |
| `LIB/errors.ts:4-27` | `AuthError` (default code `auth_failed`), `ApiError` | class |
| `LIB/broadcastChannel.ts:19-70` | `BroadcastChannel('spaarke-auth-events')`; `broadcastLogout()`, `onAuthBroadcast()`; only `{type:'logout'}` | module |
| `LIB/broadcastChannel.ts:73-82` | `_resetBroadcastChannelForTests()` — exported from the module but NOT from `LIB/index.ts` (test-only) [ADDED by verifier] [CODE LIB/broadcastChannel.ts:73-82] [CODE LIB/index.ts:1-65 (no such export)] | test hook |
| `LIB/buildBffApiUrl.ts:61-85` | `buildBffApiUrl()` idempotent `/api` prefixing; throws on empty base | function |
| `LIB/resolveRuntimeConfig.ts:33-61` | `IRuntimeConfig`; `ENV_VAR_NAMES` (`sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId`) | types/constants |
| `LIB/resolveRuntimeConfig.ts:68-89` | 5-min in-memory cache; `clearRuntimeConfigCache()` | state |
| `LIB/resolveRuntimeConfig.ts:95-137` | localStorage `__spaarke_rtc__` (60-min TTL); `readCachedRuntimeTenant()` | storage |
| `LIB/resolveRuntimeConfig.ts:150-244` | `/api/config/client` tenant fallback: `__spaarke_bff_tenant__` (24 h), 60-s failure backoff, 8-s timeout, shared in-flight map; `fetchBffClientTenant()` | network |
| `LIB/resolveRuntimeConfig.ts:262-293` | `resolveXrmContext()` frame-walk for `getClientUrl` | function |
| `LIB/resolveRuntimeConfig.ts:304-386` | `queryEnvironmentVariables()` two Dataverse Web API GETs with `credentials: 'include'` | network |
| `LIB/resolveRuntimeConfig.ts:416-544` | `resolveRuntimeConfig()` (Xrm → env vars → validate → tenant precedence → BFF fallback → cache + persist) | function |
| `LIB/resolveRuntimeConfig.ts:559-603` | `discoverTenantId()` (memo → cached config → `sprk_TenantId` → `/api/config/client`) | function |
| `LIB/resolveRuntimeConfig.ts:619-624` | `normalizeUrl()` strips trailing `/` and `/api` | function |
| `LIB/resolveTenantIdSync.ts:40-59` | `resolveTenantIdSync()` authority → cached `tid` → host chain | function |
| `LIB/createCodePageAuthInitializer.ts:35-94` | `CodePageAuthInitConfig` | types |
| `LIB/createCodePageAuthInitializer.ts:161-216` | factory returning `{ensureAuthInitialized, authenticatedFetch, getTenantId}` | function |
| `LIB/createRuntimeConfigStore.ts:65-158` | `RuntimeConfigStoreOptions`, `RuntimeConfigStore` | types |
| `LIB/createRuntimeConfigStore.ts:207-320` | factory; `setRuntimeConfig()` publishes window globals | function |
| `LIB/version.ts:12` | `VERSION = '2.0.0'` | constant |
| `LIBT/authenticatedFetch.test.ts:93-130` | 4 tests: no_token sends nothing; header always present; concurrent cold calls → one acquisition | tests |
| `LIBT/broadcastChannel.test.ts:73-110` | 4 tests | tests |
| `LIBT/BrowserMsalStrategy.test.ts:84-337` | 17 tests incl. `account selection (#1453)` ×7 | tests |
| `LIBT/config.test.ts:43-284` | 24 test declarations = 21 `it(` + 3 `it.each(` (the three tables carry 8 + 8 + 6 rows, so 43 executed cases) (validation, sync discovery chain, `requireTenantAuthority`) [CORRECTED by verifier: was "24 tests"] [CODE grep P12 + `it.each` over LIBT/config.test.ts:144,156,267] | tests |
| `LIBT/dataverseOrigin.test.ts:36-48` | 3 tests (Dataverse origin without Xrm) | tests |
| `LIBT/errors.test.ts:4-27` | 4 tests | tests |
| `LIBT/initAuth.idempotency.test.ts:76-93` | 3 tests | tests |
| `LIBT/initAuth.tenantDiscovery.test.ts:141-378` | 19 tests (discovery order, fail-closed, timeouts, replacement, precedence) | tests |
| `LIBT/InMemoryCache.test.ts:45-256` | 16 tests incl. `in-flight sharing (#1453)` ×7 (describe at :131) [CORRECTED by verifier: was "15 tests incl. … ×6"] [CODE grep P12 over LIBT/InMemoryCache.test.ts] | tests |
| `LIBT/SpaarkeAuthProvider.proactiveRefresh.test.ts:70-84` | 2 tests (timer never cold-acquires) [CORRECTED by verifier: was ":70-77"] | tests |
| `LIBT/tenant.test.ts:14-207` | 16 test declarations = 8 `it(` + 8 `it.each(` across 6 describes (validators, `tenantFromAuthority`, backoff, `isDataverseHost`, `resolveTenantIdSync`, store publishes tenant) [CORRECTED by verifier: was "~12 tests"] [CODE grep P12 + `it.each` over LIBT/tenant.test.ts] | tests |
| `LIBT/useAuth.test.ts:37-74` | 6 tests (shape; no token-string fields) | tests |

### 1.3 Cross-area pointers found while enumerating (not traced here)

- a02 (Dataverse PCF/Code Page consumers): 41 `package.json` files depend on `@spaarke/auth` — 38 by `file:` link and 3 shared libraries by version range (`"*"` in `src/client/shared/Spaarke.Notifications/package.json`, `"^2.0.0"` in `src/client/shared/Spaarke.DailyBriefing.Components/package.json` and `src/client/shared/Spaarke.LegalWorkspace/package.json`) [CORRECTED by verifier: was "41 `package.json` files depend on `@spaarke/auth` by `file:` link"] [CONFIG grep `"@spaarke/auth":` over `src/**/package.json`]. 38 consumer `tsconfig*.json` / `vite.config.*` / `webpack*.js` files additionally alias or include `…/Spaarke.Auth/src` directly, so the library is compiled from SOURCE into each consumer bundle (the package's `main: dist/index.js` is not what consumers bundle; `dist/` is gitignored) [ADDED by verifier] [CONFIG grep `Spaarke\.Auth/src` over consumer build configs] [CONFIG src/client/shared/Spaarke.Auth/.gitignore:5]. 5 consumer call sites in 5 files pass `requireSilentOnly` (`true` in `src/solutions/AllDocuments/src/versionHistory.ts:75`, `src/solutions/CommunicationReconciliation/src/services/authInit.ts:47`, `src/solutions/EmailPage/src/services/authInit.ts:47`, `src/solutions/WorkspaceLayoutWizard/src/main.tsx:146`; explicit `false` in `src/solutions/SpaarkeAi/src/services/authInit.ts:84`); NO consumer passes `requireTenantAuthority` [CORRECTED by verifier: was "7 consumer sites pass `requireSilentOnly`/`requireTenantAuthority`"] [CODE grep `requireSilentOnly|requireTenantAuthority` over `src/**` excluding the library]. 4 consumer files read `window.__SPAARKE_BFF_BASE_URL__` (written by this library, never read by it) [CODE grep]. `src/client/code-pages/CommunicationPage/src/services/authInit.ts` does NOT construct `PublicClientApplication` — the grep hit is its header comment stating the page "NEVER self-bootstraps MSAL (`new PublicClientApplication`)"; the file imports `initAuth`/`resolveRuntimeConfig` from `@spaarke/auth` and calls `initAuth({clientId, tenantId, bffBaseUrl, bffApiScope, proactiveRefresh: true})` [CORRECTED by verifier: was "constructs `PublicClientApplication` directly (INV-7 relevance) [CODE grep, not traced]"] [CODE src/client/code-pages/CommunicationPage/src/services/authInit.ts:4-5,12,43-50]. Outside the library the only real `PublicClientApplication` / `createNestablePublicClientApplication` constructions are in the external SPA (`src/client/external-spa/src/auth/msal-config.ts:125`, `msal-auth.ts:291`) [ADDED by verifier] [CODE grep].
- a03 (Office Add-ins): `src/client/office-addins/shared/services/AuthService.ts:84-98` calls this library's `resolveConfig()` then `new SpaarkeAuthProvider(resolved, new OfficeNaaStrategy(...))` directly, bypassing `initAuth()`; `src/client/office-addins/shared/services/authenticatedJsonFetch.ts:7-11` states `getAuthProvider()` is therefore unusable there [CODE].
- External SPA / Teams tab: own MSAL (`src/client/external-spa/src/auth/msal-auth.ts`, `msal-config.ts`, `src/client/external-spa/src/host/TeamsHostAdapter.ts:151`) — not this library, per ADR-028 A1/A2 [CODE grep] [DOC-ONLY .claude/adr/ADR-028-spaarke-auth-architecture.md:35,78,125].
- BFF: `GET /api/config/client` is anonymous, rate-limited (`anonymous` policy, comment says 10/min/IP), returns `ClientConfigResponse(BffBaseUrl, MsalClientId, MsalAuthority, MsalScopes, TenantId)` from `AzureAd:*` config [CODE src/server/api/Sprk.Bff.Api/Api/ConfigEndpoints.cs:60-71,108-143,207-215]. Wire casing of `tenantId` depends on the BFF's JSON options — see section 7. [ADDED by verifier] `GetClientConfig` returns via `Results.Ok(response)` with no explicit `JsonSerializerOptions` [CODE ConfigEndpoints.cs:143]; the same file's comment at :40-43 states the framework default for `Results.Ok(...)` is camelCase (and builds a `JsonSerializerDefaults.Web` options object for a sibling endpoint at :44,172); no `ConfigureHttpJsonOptions`/global `PropertyNamingPolicy` override was found in `Program.cs` [CODE grep]. So `tenantId` camelCase is the expected wire form, but it rests on the ASP.NET Core default, not on repo code → [LIVE?]. Also: when `AzureAd:TenantId` is `common`/`organizations` the BFF builds a `/organizations` `MsalAuthority` but still returns the raw value in `TenantId` [CODE ConfigEndpoints.cs:123-125,141]; the library reads only `tenantId` and `normalizeTenant()` rejects both values, so the library ends up with `''` from that BFF [CODE LIB/resolveRuntimeConfig.ts:228-233] [CODE LIB/tenant.ts:39-47].

---

## 2. How it works (code-level flow)

### 2.1 Flow A — `initAuth()` bootstrap (default `BrowserMsalStrategy`)

1. Caller invokes `initAuth(config?)` [CODE LIB/initAuth.ts:45].
2. If a provider already exists and the call "coalesces without discovery" (no `clientId` requested, or same `clientId` AND the existing authority already has a valid tenant), the existing provider is returned immediately and the eager acquire is skipped [CODE LIB/initAuth.ts:48-49,69-78]. Differences in `tenantId`, `bffApiScope`, `bffBaseUrl`, flags are ignored on coalesce [CODE LIB/initAuth.ts:73-74] [CODE LIBT/initAuth.tenantDiscovery.test.ts:262-268].
3. Otherwise the call is queued on `_selection` so only one `selectProvider()` runs at a time; a rejected selection does not block later ones [CODE LIB/initAuth.ts:51-53].
4. `selectProvider()`: if a provider exists and the call REQUESTED the same `clientId` but the existing authority is NOT tenant-specific (the `/organizations` fallback), the library runs discovery (the upgrade branch is guarded by `requestedClientId && !tenantFromAuthority(existing.authority)` [CODE LIB/initAuth.ts:115]; a call with NO `clientId` never reaches it — it is coalesced immediately by step 2 [CODE LIB/initAuth.ts:74]) [CORRECTED by verifier: was "if a provider exists for the same (or unspecified) `clientId` but its authority is NOT tenant-specific (the `/organizations` fallback), the library runs discovery"]; if a tenant is found it waits for the old provider's in-flight acquisition (`whenIdle`), builds a new provider, logs "Replacing provider on non-tenant authority", and `supersede()`s the old one (old instance keeps the shared MSAL cache and forwards every call to the new one) [CODE LIB/initAuth.ts:111-133] [CODE LIB/SpaarkeAuthProvider.ts:212-216]. Otherwise it coalesces [CODE LIB/initAuth.ts:135-136].
5. For a genuinely different `clientId` (or first init): `withDiscoveredTenant()` first tries `resolveTenantSync(config, log=false)`; on miss, if any clientId is available, it calls `discoverTenantId(bffBaseUrl)` (async: `sprk_TenantId` then `/api/config/client`) and injects the result as `config.tenantId` [CODE LIB/initAuth.ts:158-168].
6. `new SpaarkeAuthProvider(effective)` → `resolveConfig()` (section 2.2). Construction happens BEFORE the old provider is disposed, so a `tenant_unresolved` throw leaves the previous provider intact [CODE LIB/initAuth.ts:140-149].
7. Provider constructor: `requireXrm` check (throws `xrm_required`) [CODE LIB/SpaarkeAuthProvider.ts:44-46]; default strategy `new BrowserMsalStrategy(config)` wrapped in `new InMemoryCache(inner)` [CODE LIB/SpaarkeAuthProvider.ts:48-49]; logs `[SpaarkeAuth] v2.0.0 initialized` [CODE LIB/SpaarkeAuthProvider.ts:51] [CODE LIB/version.ts:12]; registers the logout-broadcast listener [CODE LIB/SpaarkeAuthProvider.ts:56-61]; starts the proactive refresh timer only if `proactiveRefresh` [CODE LIB/SpaarkeAuthProvider.ts:63-65].
8. For a newly created provider `initAuth` eagerly calls `provider.getAccessToken()` to warm the cache [CODE LIB/initAuth.ts:57-59]. On a cold MSAL cache with no Entra session this reaches `acquireTokenPopup` unless `requireSilentOnly` is set (section 2.3 step 6).

### 2.2 Flow B — `resolveConfig()` and tenant/authority resolution (synchronous)

1. `clientId` = `userConfig.clientId` ?? `window.__SPAARKE_MSAL_CLIENT_ID__` ?? undefined → throws plain `Error('MSAL Client ID not configured…')` if absent [CODE LIB/config.ts:102-116].
2. `bffApiScope` = `userConfig.bffApiScope` ?? `''` — there is NO window-global or env-var fallback for the scope inside `resolveConfig` (the declared `window.__SPAARKE_BFF_API_SCOPE__` is never read) [CODE LIB/config.ts:84,107,155] [CODE grep P4: only LIB/types.ts:114].
3. `bffBaseUrl` = `userConfig.bffBaseUrl` ?? `window.__SPAARKE_BFF_URL__` ?? `''` [CODE LIB/config.ts:108-109].
4. `requireTenantAuthority` = explicit boolean, else `isDataverseHost()` (Xrm reachable from window/parent/top, or page hostname matches `DATAVERSE_HOST_RE`) [CODE LIB/config.ts:125-126] [CODE LIB/tenant.ts:155-183].
5. Tenant precedence (`resolveTenantSync`): (a) `userConfig.authority` if `tenantFromAuthority()` yields a valid single tenant, else warn + skip [CODE LIB/config.ts:56-62]; (b) `userConfig.tenantId` if a string that `normalizeTenant()` accepts (non-string values such as an accidental Promise are warned and ignored) [CODE LIB/config.ts:69-77]; (c) `discoverTenantSync()`: `window.__SPAARKE_TENANT_ID__` → persisted `__spaarke_rtc__.tenantId` (≤60 min old) → `Xrm…organizationSettings.tenantId` across frames [CODE LIB/config.ts:36-47] [CODE LIB/resolveRuntimeConfig.ts:135-137] [CODE LIB/tenant.ts:141-152].
6. Validation applied to every candidate: trimmed; rejected if in `REJECTED_TENANTS` (`common`, `organizations`, `consumers`, `undefined`, `null`, MSA GUID, empty GUID); accepted only if GUID or dotted domain [CODE LIB/tenant.ts:39-60].
7. Outcome: resolved → `authority` = explicit authority or `https://login.microsoftonline.com/{tenant}` [CODE LIB/config.ts:131-133] [CODE LIB/tenant.ts:97-99]. Unresolved AND `requireTenantAuthority` → throws `AuthError('tenant_unresolved')` [CODE LIB/config.ts:134-140]. Unresolved AND NOT required → `console.error` and `authority = https://login.microsoftonline.com/organizations`, `tenantId = ''` [CODE LIB/config.ts:141-148]. So the `/organizations` fallback still exists outside a Dataverse host (e.g. Office Add-ins, tests) [CODE LIBT/config.test.ts:105-110].
8. Other defaults: `redirectUri` = `window.location.origin`; `proactiveRefresh=false`; `requireXrm=false`; `requireSilentOnly=false` [CODE LIB/config.ts:154-159].

### 2.3 Flow C — token acquisition, `BrowserMsalStrategy.acquire()`

1. `provider.getAccessToken()` → if superseded, forward to successor [CODE LIB/SpaarkeAuthProvider.ts:70]; else `InMemoryCache.acquire()` [CODE LIB/SpaarkeAuthProvider.ts:72].
2. `InMemoryCache.acquire()`: return the cached `TokenResult` if its JWT `exp` (fallback: strategy `expiresOn`) is ≥ 5 min away; else return the single in-flight promise if one exists; else start one inner acquisition on a microtask, tagged with the current generation [CODE LIB/strategies/InMemoryCache.ts:62-76,148-153]. Result cached only if non-empty and fresh and the generation is unchanged (a `clearCache()`/`logout()` in between bumps the generation so a late token is not cached) [CODE LIB/strategies/InMemoryCache.ts:96-106,128-132].
3. `BrowserMsalStrategy._ensureInitialized()`: dynamic `import('@azure/msal-browser')`, `new PublicClientApplication(config)`, `initialize()`, `handleRedirectPromise()`; any failure → instance `null` → `acquire()` returns empty [CODE LIB/strategies/BrowserMsalStrategy.ts:326-347,163-164]. MSAL config: `auth.clientId/authority/redirectUri`; `cache.cacheLocation='localStorage'`; `cache.storeAuthStateInCookie=true`; `system.loggerOptions.logLevel=3`, `piiLoggingEnabled=false`; no `knownAuthorities`; no `tokenRenewalOffsetSeconds` override [CODE LIB/strategies/BrowserMsalStrategy.ts:137-156].
4. Account selection `_pickAccount()`: (i) the account that last produced a token on this page (in-memory `_pageAccount`, by `homeAccountId`+`tenantId`), if still cached; (ii) if the authority has no tenant → `accounts[0]`; (iii) accounts whose `tenantId` equals the resolved tenant; if none, all accounts; return it only if exactly one candidate, else `null` [CODE LIB/strategies/BrowserMsalStrategy.ts:278-290]. MSAL's persisted active account is deliberately not consulted [CODE LIB/strategies/BrowserMsalStrategy.ts:128-132] [CODE LIBT/BrowserMsalStrategy.test.ts:288-297].
5. Step 1 `acquireTokenSilent({scopes:[bffApiScope], account})` only when an account was chosen; no `forceRefresh` [CODE LIB/strategies/BrowserMsalStrategy.ts:170-179]. Step 2 `ssoSilent({scopes, loginHint})` with `loginHint` = chosen account `username` (UPN) → `Xrm.userSettings.userPrincipalName` (frame-walk) → `undefined` (never `userSettings.userName`) [CODE LIB/strategies/BrowserMsalStrategy.ts:36-69,181-190].
6. Step 3 `acquireTokenPopup({scopes, loginHint})` unless `requireSilentOnly` (then empty result and an info log) [CODE LIB/strategies/BrowserMsalStrategy.ts:200-215]. Each successful result goes through `_validate()`: must have `accessToken`; expiry = JWT `exp` or MSAL `expiresOn`; rejected (console.error) if < 5 min away; `_accept()` records `_pageAccount` [CODE LIB/strategies/BrowserMsalStrategy.ts:293-324].
7. Exhaustion: strategy returns `{accessToken:'', expiresOn:0}` [CODE LIB/strategies/BrowserMsalStrategy.ts:217]; provider logs "All token acquisition exhausted" with clientId prefix, scope, authority, bffBaseUrl and returns `''` [CODE LIB/SpaarkeAuthProvider.ts:81-87].

### 2.4 Flow D — `authenticatedFetch(url, init)`

1. `getAuthProvider()` (throws `AuthError('not_initialized')` if `initAuth` never ran) [CODE LIB/authenticatedFetch.ts:32] [CODE LIB/initAuth.ts:174-179].
2. URL: absolute `http(s)://` used as-is; relative → `buildBffApiUrl(bffBaseUrl, url)` which strips trailing `/` from base, ensures a leading `/`, and prefixes `/api` unless already present; if `bffBaseUrl` is `''` the relative URL is sent as-is [CODE LIB/authenticatedFetch.ts:93-99] [CODE LIB/buildBffApiUrl.ts:61-85].
3. Loop up to 3 attempts: `token = await provider.getAccessToken()`; if empty → throw `AuthError('no_token')` and send nothing [CODE LIB/authenticatedFetch.ts:39-43] [CODE LIBT/authenticatedFetch.test.ts:93]. Set `Authorization: Bearer <token>` (this is the lint-exempt file) [CODE LIB/authenticatedFetch.ts:44-45] [CONFIG src/client/shared/Spaarke.Auth/.eslintrc.json:19-26]. `fetch(resolvedUrl, {...init, headers})` [CODE LIB/authenticatedFetch.ts:47].
4. `response.ok` → return [CODE LIB/authenticatedFetch.ts:50-52]. `401` and attempts remain → `provider.clearCache()` (in-memory only; MSAL cache untouched), sleep 500 ms then 1000 ms, retry [CODE LIB/authenticatedFetch.ts:55-60] [CODE LIB/SpaarkeAuthProvider.ts:94-97] [CODE LIB/strategies/InMemoryCache.ts:124-126]. Any other non-2xx → break [CODE LIB/authenticatedFetch.ts:63].
5. After loop: still 401 → `AuthError('auth_exhausted')`; otherwise parse `application/json` or `application/problem+json` body with `title` or `status` → `ApiError(detail ?? title ?? 'HTTP n', status, problemDetails)` [CODE LIB/authenticatedFetch.ts:71-81,102-115]. [ADDED by verifier] A third code, `AuthError('no_response')`, exists at [CODE LIB/authenticatedFetch.ts:67-69] but is unreachable in practice: with `MAX_RETRIES = 3` the loop always runs at least once and every iteration either throws (`no_token`), returns, or assigns `lastResponse`; a `fetch` rejection propagates as the raw `TypeError`, not as `no_response` [CODE LIB/authenticatedFetch.ts:39-64].

### 2.5 Flow E — `useAuth()`

1. Not a React hook: no React import, no hooks, no state; it calls `getAuthProvider()` and returns `{ isAuthenticated: provider.isAuthenticated(), getAccessToken, authenticatedFetch, tenantId: provider.getCachedTenantId(), logout }` [CODE LIB/useAuth.ts:49-58] [CONFIG src/client/shared/Spaarke.Auth/package.json:26-39 (no react dependency)]. `tenantId` is the JWT `tid` of the currently cached access token or `''` [CODE LIB/SpaarkeAuthProvider.ts:143-146,218-229]. No field is a token string [CODE LIBT/useAuth.test.ts:47].

### 2.6 Flow F — proactive refresh and logout

1. Timer every 4 min: skip if `!isAuthenticated()` (no fresh in-memory token), else `invalidate()` the in-memory entry and `getAccessToken()` (re-enters the strategy's silent chain; MSAL's own cache decides whether a network renewal happens) [CODE LIB/SpaarkeAuthProvider.ts:242-261] [CODE LIBT/SpaarkeAuthProvider.proactiveRefresh.test.ts:70-83].
2. `logout()`: `broadcastLogout()` posts `{type:'logout'}` on `BroadcastChannel('spaarke-auth-events')` [CODE LIB/SpaarkeAuthProvider.ts:118-122] [CODE LIB/broadcastChannel.ts:19,38-46]; `InMemoryCache.logout()` drops + bumps generation, then `BrowserMsalStrategy.logout()` → `msal.logoutPopup({account})`; on throw → `msal.clearCache()` (Entra session may persist) [CODE LIB/strategies/InMemoryCache.ts:113-116] [CODE LIB/strategies/BrowserMsalStrategy.ts:240-258]. Other same-origin contexts that registered a listener call `clearAllCaches()` → `msal.clearCache()` [CODE LIB/SpaarkeAuthProvider.ts:56-61,100-103]. [ADDED by verifier] The channel object is a module-level singleton per bundle [CODE LIB/broadcastChannel.ts:24-35], and `BroadcastChannel` does not deliver a message to the object that posted it, so the provider in the bundle that called `logout()` relies on its own `_cache.logout()` for cleanup, not on the broadcast; only OTHER bundles/frames/tabs on the same origin receive it (browser semantics — [LIVE?] for Dataverse iframes, as already listed in section 7). No server-side OBO cache invalidation [CODE LIB/SpaarkeAuthProvider.ts:114-116 (comment)] [LIVE?] (server behaviour is another area).
3. `dispose()`: clears timer and listener; clears MSAL cache too unless `preserveSharedCache` or superseded [CODE LIB/SpaarkeAuthProvider.ts:181-197].

### 2.7 Flow G — `resolveRuntimeConfig()` (Dataverse-hosted bootstrap, pre-auth)

1. Return the in-memory config if < 5 min old [CODE LIB/resolveRuntimeConfig.ts:418-420].
2. `resolveXrmContext()` (window → parent → top, first ctx with `getClientUrl`). If none: use `__spaarke_rtc__` from localStorage (≤ 60 min, all three required fields present) or throw `Error('…must be called from within a Dataverse web resource')` [CODE LIB/resolveRuntimeConfig.ts:423-440,112-128].
3. `clientUrl = ctx.getClientUrl()`; throw if empty [CODE LIB/resolveRuntimeConfig.ts:442-448]. `xrmTenantId = normalizeTenant(ctx.organizationSettings?.tenantId) ?? ''` [CODE LIB/resolveRuntimeConfig.ts:453].
4. `queryEnvironmentVariables(clientUrl, [4 names])`: GET `{clientUrl}/api/data/v9.2/environmentvariabledefinitions?$filter=schemaname eq '…' or …&$select=…` then GET `environmentvariablevalues?$filter=_environmentvariabledefinitionid_value eq '…'…`, both with `credentials: 'include'` and OData 4.0 headers, no bearer token; override value wins over default value; definitions request failure throws, values failure is tolerated [CODE LIB/resolveRuntimeConfig.ts:304-386,456-461]. That the Dataverse Web API honours the session cookie from a web resource is [LIVE?].
5. Validate: `sprk_BffApiBaseUrl` missing → throw; `sprk_BffApiAppId` missing → throw; `msalClientId` = `sprk_MsalClientId` → `window.__SPAARKE_MSAL_CLIENT_ID__` → throw [CODE LIB/resolveRuntimeConfig.ts:464-491].
6. Tenant: `sprk_TenantId` (validated; invalid value warned and ignored) → `xrmTenantId` → `fetchBffClientTenant(normalizedBffUrl)` (`/api/config/client`, reads `.tenantId`, validated, persisted 24 h per BFF host) → `''` [CODE LIB/resolveRuntimeConfig.ts:499-523,204-244].
7. Build `{ bffBaseUrl: normalizeUrl(raw) (strips trailing `/` and `/api`), bffOAuthScope: 'api://{sprk_BffApiAppId}/user_impersonation', msalClientId, tenantId }`; cache in memory and in `__spaarke_rtc__`; log a redacted summary with `source=env-var|xrm-fallback|bff-config|none` [CODE LIB/resolveRuntimeConfig.ts:526-543,619-624].

### 2.8 Flow H — `discoverTenantId()` (async, used only by `initAuth`)

1. Memoised `discoveredTenant` → cached config's tenant → `clientUrl` from Xrm or, if the page hostname is a Dataverse domain, `window.location.origin` [CODE LIB/resolveRuntimeConfig.ts:559-573].
2. If a `clientUrl` exists and the last env-var miss is ≥ 60 s old: query only `sprk_TenantId` with an 8-s `AbortSignal` timeout; on miss/failure record `envTenantMissAt` [CODE LIB/resolveRuntimeConfig.ts:575-595,153,159,162-176].
3. `fetchBffClientTenant(bffBaseUrl)` (requires an `http(s)://` base; otherwise `''`) [CODE LIB/resolveRuntimeConfig.ts:597-602,204-206]. Never throws.

### 2.9 Flow I — Code Page factories

1. `createCodePageAuthInitializer(cfg)`: once-only `_initPromise`; optional `beforeInit()`; `initAuth({clientId, bffBaseUrl, bffApiScope, tenantId?, requireTenantAuthority?, proactiveRefresh (default true), requireSilentOnly (default false)})`; on failure logs with `logLabel`, nulls the promise (retry allowed) and rethrows; returns `{ensureAuthInitialized, authenticatedFetch (awaits init then shared fetch), getTenantId (awaits init then provider.getTenantId())}` [CODE LIB/createCodePageAuthInitializer.ts:161-216].
2. `createRuntimeConfigStore(opts)`: `setRuntimeConfig(config)` stores it and publishes `window.__SPAARKE_BFF_BASE_URL__`, `window.__SPAARKE_BFF_URL__` (only if unset), `window.__SPAARKE_MSAL_CLIENT_ID__`, and `window.__SPAARKE_TENANT_ID__` (only if valid) [CODE LIB/createRuntimeConfigStore.ts:230-254]; getters throw before `setRuntimeConfig` [CODE LIB/createRuntimeConfigStore.ts:260-268]; `getTenantId()` optionally lazy-resolves via `resolveTenantIdSync()` with a telemetry callback [CODE LIB/createRuntimeConfigStore.ts:282-310].
3. `resolveTenantIdSync()`: provider authority tenant → cached-token `tid` → `discoverTenantSync()` → `''`; safe before `initAuth` [CODE LIB/resolveTenantIdSync.ts:40-59].

### 2.10 Flow J — `OfficeNaaStrategy` (Office Add-ins)

1. Construction takes an already-resolved `Required<IAuthConfig>` plus `IOfficeNaaConfig`; nothing in `initAuth()`/`IAuthConfig` accepts a strategy, so the only way to use it is `new SpaarkeAuthProvider(resolvedConfig, strategy)` [CODE LIB/strategies/OfficeNaaStrategy.ts:201-204] [CODE LIB/SpaarkeAuthProvider.ts:41] [CODE LIB/types.ts:2-73 (no `strategy` field)] [CODE LIB/initAuth.ts:143 (strategy never passed)].
2. `_ensureInitialized()`: delete every sessionStorage key containing `interaction.status` [CODE LIB/strategies/OfficeNaaStrategy.ts:341-352,369]; `useNaa = !forceFallback && detectNaaSupport()`; `detectNaaSupport()` returns false when `Office`/`Office.context` is absent, waits `Office.onReady`, returns FALSE for `PlatformType.OfficeOnline` (deliberate: standard popup is used on the web), PC build ≥ 13530, Mac ≥ 16.44, true for unknown platforms, false on any throw [CODE LIB/strategies/OfficeNaaStrategy.ts:86-149,371]. [ADDED by verifier] Two further `false` paths: `Office.context.diagnostics` absent [CODE LIB/strategies/OfficeNaaStrategy.ts:107-110], and a PC `version` string with fewer than three dot-separated parts [CODE LIB/strategies/OfficeNaaStrategy.ts:129-134]. The header comment at :75-76 ("NAA is supported in: Office on the web (all browsers)") contradicts the code's deliberate `OfficeOnline → false` at :116-125 — see section 6.
3. NAA path: `createNestablePublicClientApplication({auth:{clientId, authority, redirectUri: caller value if it starts with 'brk-' else 'brk-multihub://<hostname>', supportsNestedAppAuth:true, navigateToLoginRequestUrl:false}, cache:{sessionStorage, storeAuthStateInCookie:false}, system:{logLevel:3, piiLoggingEnabled:false, allowRedirectInIframe:true}})` then `handleRedirectPromise()` [CODE LIB/strategies/OfficeNaaStrategy.ts:378-385,412-436,29-32].
4. Fallback path: `new PublicClientApplication({auth:{clientId, authority, redirectUri: fallbackRedirectUri || '<origin>/auth-callback.html', navigateToLoginRequestUrl:false}, cache:{sessionStorage, cookie:false}, …})`, `initialize()`, `handleRedirectPromise()` [CODE LIB/strategies/OfficeNaaStrategy.ts:386-393,439-463].
5. `acquire()`: account = `getAllAccounts()[0]` (no tenant filtering) [CODE LIB/strategies/OfficeNaaStrategy.ts:355-358]; if account → `acquireTokenSilent({scopes, account, forceRefresh:false})`; then ALWAYS `acquireTokenPopup({scopes, loginHint: account?.username, prompt: account ? undefined : 'select_account'})` — `requireSilentOnly` is not consulted anywhere in this file [CODE LIB/strategies/OfficeNaaStrategy.ts:218-256] [CODE grep P11: no `requireSilentOnly` in OfficeNaaStrategy.ts]. Validation identical to Browser strategy [CODE LIB/strategies/OfficeNaaStrategy.ts:314-331].
6. `logout()`: returns without doing anything if no account is cached; else `logoutPopup({account})` → `clearCache()` fallback [CODE LIB/strategies/OfficeNaaStrategy.ts:277-297].

### 2.11 Invariants INV-1..INV-8 as actually implemented

The numbering below follows `.claude/patterns/auth/spaarke-sso-binding.md:36-45` (the file the ADR names as canonical). Other documents and the code's own comments number them differently — see section 6 and defect D6.

| INV | Canonical doc statement | Implementation status |
|---|---|---|
| INV-1 `cacheLocation: 'localStorage'` | [DOC-ONLY .claude/patterns/auth/spaarke-sso-binding.md:38] | Implemented in `BrowserMsalStrategy` only [CODE LIB/strategies/BrowserMsalStrategy.ts:144]; `OfficeNaaStrategy` uses `sessionStorage` on both paths [CODE LIB/strategies/OfficeNaaStrategy.ts:425,452]. Cross-tab sharing therefore relies on MSAL's localStorage only for Dataverse surfaces; the library's own `InMemoryCache` is per provider instance, per bundle [CODE LIB/strategies/InMemoryCache.ts:53]. |
| INV-2 `storeAuthStateInCookie: true` | [DOC-ONLY spaarke-sso-binding.md:39] | `true` in `BrowserMsalStrategy` [CODE BrowserMsalStrategy.ts:148]; `false` in both `OfficeNaaStrategy` configs [CODE OfficeNaaStrategy.ts:426,453]. The code comment explicitly disclaims the doc's rationale ("does not by itself make ssoSilent succeed") [CODE BrowserMsalStrategy.ts:145-147]. Effect is [LIVE?]. |
| INV-3 tenant-specific authority, never `/organizations` or `/common` | [DOC-ONLY spaarke-sso-binding.md:40] | Enforced (throw) only when `requireTenantAuthority` is true, which defaults to `isDataverseHost()` [CODE LIB/config.ts:125-140]. Outside a Dataverse host the `/organizations` fallback is still produced, logged as an error [CODE LIB/config.ts:141-148]. `/common`, `/consumers` and garbage segments are rejected everywhere [CODE LIB/tenant.ts:39-60]. A provider created on `/organizations` is replaced when a later same-clientId init resolves a tenant [CODE LIB/initAuth.ts:115-133]. |
| INV-4 tenant from `sprk_TenantId` (primary) → Xrm frame-walk (fallback), not hard-coded | [DOC-ONLY spaarke-sso-binding.md:41] | Not hard-coded: true. Order differs: caller `authority` → caller `tenantId` → `window.__SPAARKE_TENANT_ID__` → `__spaarke_rtc__` → Xrm → (async) memoised `discoveredTenant` / in-memory `cachedConfig.tenantId` (≤ 5 min) [ADDED by verifier] [CODE LIB/resolveRuntimeConfig.ts:560-565] → `sprk_TenantId` → `/api/config/client` [CODE LIB/tenant.ts:14-24] [CODE LIB/config.ts:36-81] [CODE LIB/resolveRuntimeConfig.ts:559-603]. Inside `resolveRuntimeConfig` the env var does beat Xrm [CODE LIB/resolveRuntimeConfig.ts:499-505]. |
| INV-5 UPN (not display name) as `loginHint` | [DOC-ONLY spaarke-sso-binding.md:42] | Implemented: account `username` → `userSettings.userPrincipalName` → undefined; `userSettings.userName` is never used [CODE LIB/strategies/BrowserMsalStrategy.ts:36-69]. NOTE the code cites "ADR-028 INV-5" for a DIFFERENT rule ("popup only on explicit user action") [CODE LIB/types.ts:51] [CODE LIB/SpaarkeAuthProvider.ts:248] [CODE LIB/strategies/BrowserMsalStrategy.ts:197] [CODE LIB/createCodePageAuthInitializer.ts:72]; that rule is implemented as `requireSilentOnly` (Browser strategy only) and the proactive-refresh gate [CODE LIB/strategies/BrowserMsalStrategy.ts:200-205] [CODE LIB/SpaarkeAuthProvider.ts:253]. |
| INV-6 prefer omitting `authority` so the library builds it | [DOC-ONLY spaarke-sso-binding.md:43] | A consumer rule; the library accepts an explicit authority but validates and may discard it [CODE LIB/config.ts:56-62]. Consumer compliance is cross-area (a02/a03). |
| INV-7 all consumers share ONE `PublicClientApplication` via `getAuthProvider()` | [DOC-ONLY spaarke-sso-binding.md:44] | Only per JavaScript bundle: `_provider` is a module-level variable [CODE LIB/initAuth.ts:8] and the package is linked into each consumer (38 by `file:`, 3 shared libs by version range) and bundled there from `src/` via build-config aliases [CORRECTED by verifier: was "linked by `file:` into each consumer and bundled there [CONFIG … 41 consumers] [CONFIG … `main: dist/index.js`]"] [CONFIG src/**/package.json `"@spaarke/auth"` ×41] [CONFIG 38 consumer tsconfig/vite/webpack files referencing `Spaarke.Auth/src`]. Separately bundled PCFs/pages on one form each hold their own PCA that share MSAL's localStorage. Within one bundle, duplicate inits coalesce [CODE LIB/initAuth.ts:69-78,111-136]. |
| INV-8 rebuild + redeploy every consumer on library change | [DOC-ONLY spaarke-sso-binding.md:45] | Mechanism supported by the `VERSION` console line [CODE LIB/SpaarkeAuthProvider.ts:51] [CODE LIB/version.ts:12]; but `VERSION` was not bumped by #1453 (not in that commit's diff stat) so old and new bundles both log `v2.0.0` [CODE git show --stat 2598b2fb9]. Whether every consumer has been rebuilt is [LIVE?]. |

---

## 3. Identities, tokens and credentials

| step | identity | token or credential type | authority / tenant | audience / scope | cache / storage | tag |
|---|---|---|---|---|---|---|
| Dataverse env-var reads (`queryEnvironmentVariables`) | the browser's existing Dataverse user session | Dataverse session cookie (`credentials: 'include'`), no bearer | n/a (org Web API at `getClientUrl()`) | `{clientUrl}/api/data/v9.2/environmentvariabledefinitions` and `…values` | result cached in memory 5 min + localStorage `__spaarke_rtc__` 60 min | [CODE LIB/resolveRuntimeConfig.ts:310-367,68,95-110] [LIVE?] cookie acceptance |
| `GET {bff}/api/config/client` | anonymous | none | n/a | BFF host | in-flight map per host; localStorage `__spaarke_bff_tenant__` 24 h; failure remembered 60 s; 8-s timeout | [CODE LIB/resolveRuntimeConfig.ts:150-244] |
| MSAL `acquireTokenSilent` (Browser) | signed-in user (chosen cached account) | OAuth2 access token (JWT) renewed from MSAL's refresh token | `https://login.microsoftonline.com/{tenant}` (or `/organizations` outside Dataverse) | `api://{sprk_BffApiAppId}/user_impersonation` (as passed in `bffApiScope`) | MSAL localStorage (+ auth-state cookie); copy in `InMemoryCache` until `exp - 5 min` | [CODE LIB/strategies/BrowserMsalStrategy.ts:137-157,173] [CODE LIB/resolveRuntimeConfig.ts:528] [LIVE?] refresh-token presence |
| MSAL `ssoSilent` (Browser) | user identified by Entra session cookie + optional UPN hint | same | same | same | same | [CODE LIB/strategies/BrowserMsalStrategy.ts:183-187] [LIVE?] cookie/iframe behaviour |
| MSAL `acquireTokenPopup` (Browser) | user, interactive | same | same | same | same | [CODE LIB/strategies/BrowserMsalStrategy.ts:207-215] |
| MSAL NAA `acquireTokenSilent`/`acquireTokenPopup` (Office) | the Office host's signed-in user via the NAA broker | access token issued through the host broker | config authority; redirect `brk-multihub://<hostname>` | same scope | MSAL sessionStorage, no cookie state | [CODE LIB/strategies/OfficeNaaStrategy.ts:218-256,412-436] [LIVE?] broker behaviour |
| MSAL fallback PCA (Office, incl. Office on the web) | user, interactive popup | access token | config authority; redirect `<origin>/auth-callback.html` | same scope | MSAL sessionStorage | [CODE LIB/strategies/OfficeNaaStrategy.ts:386-393,439-463] |
| `authenticatedFetch` → BFF | user (bearer) | `Authorization: Bearer <access token>` | n/a | BFF URL from `buildBffApiUrl` | none | [CODE LIB/authenticatedFetch.ts:44-47] |
| `logout()` | user | MSAL `logoutPopup` (ends Entra session [LIVE?]) | config authority | n/a | clears in-memory + MSAL cache; broadcasts to same-origin contexts | [CODE LIB/SpaarkeAuthProvider.ts:118-122] [CODE LIB/strategies/BrowserMsalStrategy.ts:240-258] |
| Token `tid` extraction | n/a | reads unverified JWT payload (`atob`) | n/a | n/a | n/a | [CODE LIB/SpaarkeAuthProvider.ts:218-229] |

No client secret, certificate, managed identity, API key or confidential-client code exists in the library [CODE grep P3/P6 over LIB/: only the calls listed above].

---

## 4. Behavior by user type

### U1 Spaarke staff (member of Spaarke's tenant, licensed Dataverse user)

- Inside a Dataverse surface: `resolveRuntimeConfig()` reads the four env vars with the Dataverse cookie; `initAuth` builds the authority from `sprk_TenantId` (or the published/persisted/Xrm tenant); `acquireTokenSilent` with the single cached account, else `ssoSilent` with the UPN hint, else popup [CODE sections 2.1-2.3]. Member tokens carry `tid` = Spaarke tenant, so `getCachedTenantId()` returns it [CODE LIB/SpaarkeAuthProvider.ts:143-146] [LIVE?] claim contents.
- Two staff accounts on one browser: both are "in tenant" → no account guessed; `ssoSilent` without hint relies on the Entra session [CODE LIB/strategies/BrowserMsalStrategy.ts:285-289] [CODE LIBT/BrowserMsalStrategy.test.ts:278-286]; whether Entra resolves that silently with two sessions is [LIVE?].
- Failure modes: `tenant_unresolved` if nothing yields a tenant inside Dataverse [CODE LIB/config.ts:134-140]; `no_token` from `authenticatedFetch` after silent+popup exhaustion [CODE LIB/authenticatedFetch.ts:41-43].

### U2 Model 1 customer staff (B2B guest in Spaarke's tenant)

- Dataverse surfaces: the #1453 change exists for this user. A tenant-specific authority is mandatory inside Dataverse (throw instead of `/organizations`) [CODE LIB/config.ts:10-15,134-148]; account selection prefers the cached account whose `tenantId` equals the resolved tenant (the guest's Spaarke-tenant account over their home-tenant account) [CODE LIB/strategies/BrowserMsalStrategy.ts:287-289] [CODE LIBT/BrowserMsalStrategy.test.ts:245-257]; a single cached home-tenant account is still used for silent redemption [CODE LIB/strategies/BrowserMsalStrategy.ts:268-270,285-289] [LIVE?] whether Entra issues the Spaarke-tenant token silently from it. The `tid` of the resulting token should be Spaarke's tenant [LIVE?]. [ADDED by verifier] The filter compares MSAL's `AccountInfo.tenantId`, which msal-common 14.16.1 documents as "Full tenant or organizational id that this account belongs to" (with per-tenant `tenantProfiles` carrying `isHomeTenant`) [CODE c:/code_files/spaarke/src/client/shared/Spaarke.Auth/node_modules/@azure/msal-common/dist/account/AccountInfo.d.ts:6,14-28 (sibling checkout, same lockfile version — outside the code root)]; that a guest's cached Spaarke-tenant account actually carries `tenantId` = Spaarke tenant (rather than the home tenant) is MSAL runtime behaviour [LIVE?]. With NO cached account, the `ssoSilent`/popup `loginHint` is whatever Dataverse reports as `userSettings.userPrincipalName` for the guest [CODE LIB/strategies/BrowserMsalStrategy.ts:55-61]; whether that is the `#EXT#` alias, the home UPN, or empty for a B2B guest is [LIVE?] (added to section 7). With two or more cached accounts and none in the Spaarke tenant, the library guesses nothing and relies on `ssoSilent` without a cached-account hint [CODE LIB/strategies/BrowserMsalStrategy.ts:288-289] [CODE LIBT/BrowserMsalStrategy.test.ts:326].
- What fails: any path that lands on `/organizations` — the library's own rationale is AADSTS700016 in the home tenant [CODE LIB/config.ts:10-15 (comment)] [LIVE?]. That path is closed inside Dataverse, open outside it (Office Add-ins, tests) [CODE LIB/config.ts:141-148].
- Office Add-ins (run in the guest's HOME tenant per the task definition — this host premise is taken from the task brief, not from code [LIVE?] [ADDED by verifier]): `OfficeNaaStrategy` picks `accounts[0]` with no tenant filter [CODE LIB/strategies/OfficeNaaStrategy.ts:355-358]; the authority is whatever the add-in resolved (cross-area a03: `src/client/office-addins/shared/services/AuthService.ts:84-90` passes `tenantId` only if configured, else `/organizations` fallback since the add-in is not a Dataverse host [CODE LIB/config.ts:125-126,141-148]). Whether the NAA broker will mint a Spaarke-tenant token for a home-tenant host identity is [LIVE?]. Recorded as risk R-U2 in section 8.

### U3 Licence-free workforce user (no Dataverse systemuser; contact-only)

- The library is identity-agnostic: for a workforce account the flow is identical to U1 [CODE sections 2.2-2.3]. However every Dataverse-hosted entry point requires a Dataverse session (`resolveRuntimeConfig` throws without Xrm or a prior cache) [CODE LIB/resolveRuntimeConfig.ts:423-440], which a user without a systemuser cannot establish [LIVE?]. ADR-028 A3 states that the module-host SPA serving such users MUST NOT use this library [DOC-ONLY .claude/adr/ADR-028-spaarke-auth-architecture.md:125]. Mapping to a contact happens server-side — cross-area. Net: not applicable to this library except via Office Add-ins, where the library would acquire a workforce token like U1 [CODE LIB/strategies/OfficeNaaStrategy.ts:218-256].

### U4 CIAM external contact (`*.ciamlogin.com`)

- Not supported by design. `buildTenantAuthority()` hard-codes `https://login.microsoftonline.com` [CODE LIB/tenant.ts:49,97-99]; an explicit CIAM `authority` would pass `tenantFromAuthority()` (host is not checked) [CODE LIB/tenant.ts:77-94] but neither MSAL config declares `knownAuthorities` [CODE LIB/strategies/BrowserMsalStrategy.ts:137-156] [CODE LIB/strategies/OfficeNaaStrategy.ts:412-463], so MSAL would be expected to reject the authority at runtime [LIVE?]. ADR-028 A1 assigns CIAM to a standalone MSAL module outside this library [DOC-ONLY .claude/adr/ADR-028-spaarke-auth-architecture.md:35,48]. Not applicable.

### U5 Model 2 customer staff (native account in the customer's own tenant)

- The library has no Spaarke-specific constants: client id, BFF app id, BFF URL and tenant all come from the environment (`sprk_*` env vars, window globals, `/api/config/client`) [CODE LIB/resolveRuntimeConfig.ts:56-61] [CODE LIB/config.ts:102-109]. With a customer-tenant app registration in `sprk_MsalClientId` and the customer tenant in `sprk_TenantId`, the authority becomes the customer tenant and the flow is U1's [CODE LIB/config.ts:131-133].
- Gaps: (1) `AUTHORITY_HOST` is the commercial cloud only, while `DATAVERSE_HOST_RE` recognises sovereign Dataverse domains — a sovereign-cloud Model 2 would detect "Dataverse host" yet build a commercial-cloud authority [CODE LIB/tenant.ts:49,155-156]; (2) `/api/config/client` tenant is persisted per BFF host for 24 h [CODE LIB/resolveRuntimeConfig.ts:151] — harmless for a dedicated stamp; (3) whether a Model 2 BFF's `AzureAd:TenantId` is the customer tenant is [LIVE?] (cross-area).

### U6 Service and non-user identities

- Not applicable. The library is a browser public-client only: no client-credential, managed-identity, certificate, API-key or webhook code paths exist [CODE grep P3/P6 over LIB/ (section 3)]. The only non-user calls are the anonymous `/api/config/client` GET [CODE LIB/resolveRuntimeConfig.ts:219-223] and cookie-authenticated Dataverse env-var reads [CODE LIB/resolveRuntimeConfig.ts:316-367].

---

## 5. Configuration consumed

| key | where read (path:line) | source / default |
|---|---|---|
| `clientId` | `LIB/config.ts:102-105` | `IAuthConfig.clientId` → `window.__SPAARKE_MSAL_CLIENT_ID__` → throw [CODE] |
| `authority` | `LIB/config.ts:56-62` | `IAuthConfig.authority`, used only if its tenant segment validates [CODE] |
| `tenantId` | `LIB/config.ts:69-80`; `LIB/initAuth.ts:158-168` | `IAuthConfig.tenantId` → `window.__SPAARKE_TENANT_ID__` → localStorage `__spaarke_rtc__.tenantId` → Xrm `organizationSettings.tenantId` → env var `sprk_TenantId` → `/api/config/client` `.tenantId` → throw (Dataverse host) / `/organizations` [CODE] |
| `requireTenantAuthority` | `LIB/config.ts:125-126` | explicit boolean → `isDataverseHost()` [CODE] |
| `redirectUri` | `LIB/config.ts:154` | `IAuthConfig.redirectUri` → `window.location.origin` [CODE]; Office NAA overrides to `brk-multihub://<hostname>` unless caller value starts with `brk-` [CODE LIB/strategies/OfficeNaaStrategy.ts:420] |
| `bffApiScope` | `LIB/config.ts:107,155` | `IAuthConfig.bffApiScope` → `''` (no window/env fallback) [CODE]; Code Pages derive it as `api://{sprk_BffApiAppId}/user_impersonation` [CODE LIB/resolveRuntimeConfig.ts:528] |
| `bffBaseUrl` | `LIB/config.ts:108-109`; `LIB/initAuth.ts:165` | `IAuthConfig.bffBaseUrl` → `window.__SPAARKE_BFF_URL__` → `''` [CODE] |
| `proactiveRefresh` / `requireXrm` / `requireSilentOnly` | `LIB/config.ts:157-159` | default `false` / `false` / `false` [CODE]; `createCodePageAuthInitializer` defaults `proactiveRefresh=true`, `requireSilentOnly=false` [CODE LIB/createCodePageAuthInitializer.ts:168-169] |
| `window.__SPAARKE_BFF_API_SCOPE__` | declared `LIB/types.ts:114` | never read by the library [CODE grep P4] |
| `window.__SPAARKE_BFF_BASE_URL__` | written `LIB/createRuntimeConfigStore.ts:234` | never read by the library [CODE grep P4] (4 consumer files read it — cross-area) |
| Dataverse env vars `sprk_BffApiBaseUrl`, `sprk_BffApiAppId`, `sprk_MsalClientId`, `sprk_TenantId` | `LIB/resolveRuntimeConfig.ts:56-61,456-461,575-582` | Dataverse Web API, override value over default value [CODE] |
| `/api/config/client` → `tenantId` | `LIB/resolveRuntimeConfig.ts:219-233` | BFF `AzureAd:TenantId` [CODE src/server/api/Sprk.Bff.Api/Api/ConfigEndpoints.cs:112-143] |
| localStorage `__spaarke_rtc__` | `LIB/resolveRuntimeConfig.ts:95-128` | TTL 60 min; fields `bffBaseUrl`, `bffOAuthScope`, `msalClientId`, `tenantId`, `_ts` [CODE] |
| localStorage `__spaarke_bff_tenant__` | `LIB/resolveRuntimeConfig.ts:150-198` | TTL 24 h, keyed by BFF base URL [CODE] |
| MSAL cache (localStorage / sessionStorage) | `LIB/strategies/BrowserMsalStrategy.ts:144`; `LIB/strategies/OfficeNaaStrategy.ts:425,452` | MSAL-managed key format [LIVE?] |
| sessionStorage `*interaction.status*` | `LIB/strategies/OfficeNaaStrategy.ts:341-352` | deleted at Office strategy init [CODE] |
| `BroadcastChannel` name | `LIB/broadcastChannel.ts:19` | `spaarke-auth-events` [CODE] |
| `Office.context.diagnostics.platform/version` | `LIB/strategies/OfficeNaaStrategy.ts:106-141` | NAA thresholds PC ≥ 13530, Mac ≥ 16.44, OfficeOnline → fallback [CODE] |
| `IOfficeNaaConfig.fallbackRedirectUri` / `forceFallback` | `LIB/strategies/OfficeNaaStrategy.ts:440-442,371` | `<origin>/auth-callback.html` / `false` [CODE] |
| `RuntimeConfigStoreOptions` | `LIB/createRuntimeConfigStore.ts:208-213` | `enableWaitForConfig=false`, `lazyTenantResolveWithTelemetry=false` [CODE] |
| Timers / limits | `LIB/config.ts:87,90`; `LIB/authenticatedFetch.ts:7,10`; `LIB/resolveRuntimeConfig.ts:68,97,151,153,159` | expiry buffer 5 min; proactive 4 min; 401 retries 3 with 500/1000 ms backoff; config cache 5 min; rtc TTL 60 min; BFF tenant TTL 24 h; failure backoff 60 s; discovery timeout 8 s [CODE] |
| MSAL logger | `LIB/strategies/BrowserMsalStrategy.ts:151-153`; `LIB/strategies/OfficeNaaStrategy.ts:429-431,456-458` | `logLevel: 3` (= `LogLevel.Verbose` in msal-common 14.16.1 [CODE c:/code_files/spaarke/src/client/shared/Spaarke.Auth/node_modules/@azure/msal-common/dist/logger/Logger.d.ts:14-20 (sibling checkout, same lockfile version)]), `piiLoggingEnabled: false` |
| Package / peer versions | `src/client/shared/Spaarke.Auth/package.json:26-28,30`; `package-lock.json:47-48,60-61` | peer `@azure/msal-browser ^3.0.0`; installed 3.30.0 / msal-common 14.16.1 [CONFIG]; consumers' bundled MSAL versions are cross-area |

---

## 6. Document claims checked

| doc path:line | claim | verdict | code evidence |
|---|---|---|---|
| `.claude/patterns/auth/spaarke-sso-binding.md:22` | "`SpaarkeAuthProvider` constructs MSAL with these values" | STALE | MSAL is constructed by the strategies, not the provider [CODE LIB/strategies/BrowserMsalStrategy.ts:334] [CODE LIB/strategies/OfficeNaaStrategy.ts:379,387]; provider holds no MSAL config [CODE LIB/SpaarkeAuthProvider.ts:41-66] |
| `spaarke-sso-binding.md:27,38` | INV-1 `localStorage` for every internal surface | VERIFIED for Browser strategy; CONTRADICTED for Office strategy | [CODE BrowserMsalStrategy.ts:144]; [CODE OfficeNaaStrategy.ts:425,452] uses `sessionStorage` deliberately [CODE OfficeNaaStrategy.ts:176-178] |
| `spaarke-sso-binding.md:29,39` | INV-2 `storeAuthStateInCookie: true` "required for ssoSilent when 3rd-party cookies are blocked" | value VERIFIED (Browser), CONTRADICTED (Office `false`); rationale UNVERIFIABLE | [CODE BrowserMsalStrategy.ts:145-148] comment says it does not by itself make ssoSilent succeed; [CODE OfficeNaaStrategy.ts:426,453] |
| `spaarke-sso-binding.md:31-32,40` | INV-3 authority NEVER `/organizations` or `/common` | VERIFIED inside Dataverse host; CONTRADICTED outside | [CODE LIB/config.ts:134-148] [CODE LIBT/config.test.ts:105-110] |
| `spaarke-sso-binding.md:41` | INV-4 tenant from `sprk_TenantId` (primary) → Xrm (fallback) | STALE (order incomplete) | actual seven-step order [CODE LIB/tenant.ts:14-24] [CODE LIB/config.ts:36-81] [CODE LIB/resolveRuntimeConfig.ts:559-603] |
| `spaarke-sso-binding.md:42` | INV-5 UPN not display name as loginHint | VERIFIED | [CODE BrowserMsalStrategy.ts:36-69] |
| `spaarke-sso-binding.md:44` | INV-7 all consumers share ONE `PublicClientApplication` | CONTRADICTED (one per bundle) | [CODE LIB/initAuth.ts:8] module singleton; [CONFIG src/**/package.json `file:` links ×41]; add-ins construct their own provider [CODE src/client/office-addins/shared/services/AuthService.ts:98] |
| `spaarke-sso-binding.md:57` | "Future strategies: `OfficeNaaStrategy` (Phase B4)" | STALE | exists and is exported [CODE LIB/strategies/OfficeNaaStrategy.ts:185] [CODE LIB/index.ts:23] |
| `spaarke-sso-binding.md:55` | InMemoryCache validates JWT exp with 5-min buffer | VERIFIED | [CODE LIB/strategies/InMemoryCache.ts:5,148-153] |
| `.claude/adr/ADR-028-spaarke-auth-architecture.md:8` | "Full version: docs/adr/ADR-028-spaarke-auth-architecture.md" | CONTRADICTED (file absent; the ADR itself says so at :104) | `ls docs/adr/ADR-028*` → no such file [CODE ls] |
| `ADR-028:21` | tenant resolved via `sprk_TenantId` → Xrm frame-walk | STALE | as INV-4 row |
| `ADR-028:35` | MUST NOT instantiate `PublicClientApplication` outside `@spaarke/auth` | library side VERIFIED; consumer side cross-area | inside LIB only two sites [CODE BrowserMsalStrategy.ts:334, OfficeNaaStrategy.ts:379,387]; outside: ONLY the external SPA files (sanctioned by A1/A2) — `src/client/external-spa/src/auth/msal-config.ts:125`, `msal-auth.ts:291`; `CommunicationPage/src/services/authInit.ts` only MENTIONS the constructor in a comment saying it never uses it [CORRECTED by verifier: was "outside: `src/client/code-pages/CommunicationPage/src/services/authInit.ts`, external SPA files (sanctioned by A1/A2)"] [CODE grep P13] [CODE src/client/code-pages/CommunicationPage/src/services/authInit.ts:4-5,43-50] |
| `ADR-028:66,78,125` | `@spaarke/auth` is "Xrm-context-bound + MSAL v3" | MSAL v3 VERIFIED; "Xrm-bound" STALE/overstated | [CONFIG package.json:27] [CONFIG package-lock.json:47-48]; `initAuth`+`BrowserMsalStrategy` run without Xrm (fallback path) [CODE LIB/config.ts:141-148]; only `resolveRuntimeConfig` requires Xrm or a cache [CODE LIB/resolveRuntimeConfig.ts:423-440] |
| `.claude/constraints/auth.md:50` | resolve tenant via `sprk_TenantId` → Xrm fallback | STALE | as INV-4 row |
| `.claude/constraints/auth.md:90-91` | MUST NOT use `sessionStorage` / MUST NOT set `storeAuthStateInCookie: false` (internal surfaces) | CONTRADICTED by `OfficeNaaStrategy` unless Add-ins are not "internal surfaces" (the doc does not say) | [CODE OfficeNaaStrategy.ts:425-426,452-453] |
| `.claude/constraints/auth.md:104` | MUST call `initAuth()` once before rendering | library contract VERIFIED (`not_initialized` otherwise); Office Add-ins do not call it | [CODE LIB/initAuth.ts:174-179]; [CODE src/client/office-addins/shared/services/authenticatedJsonFetch.ts:7-11] |
| `.claude/constraints/auth.md:192` | `authenticatedFetch` routes relative URLs through `buildBffApiUrl` | VERIFIED (only when `bffBaseUrl` is non-empty) | [CODE LIB/authenticatedFetch.ts:93-99] |
| `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md:286-287` [CORRECTED by verifier: was ":284-285"] | INV-1/INV-2 at `SpaarkeAuthProvider.ts:54,60` | STALE (line refs and location) | config lives in strategies [CODE BrowserMsalStrategy.ts:144,148] |
| `AUDIT-FINDINGS-AUTH-SYSTEM.md:288` [CORRECTED by verifier: was ":286"] | INV-3 authority "resolved from `Xrm…organizationSettings.tenantId` via frame-walk" | STALE | Xrm is the third sync source; env var/window first [CODE LIB/config.ts:36-47] |
| `AUDIT-FINDINGS-AUTH-SYSTEM.md:290` | INV-5 = 6-strategy cascade Cache → SessionStorage → Bridge → Xrm → MsalSilent → MsalPopup | STALE (cascade deleted) | single strategy behind `InMemoryCache` [CODE LIB/strategies/AuthStrategy.ts:10-13] [CODE LIB/SpaarkeAuthProvider.ts:48-49] |
| `AUDIT-FINDINGS-AUTH-SYSTEM.md:292` | INV-7 `clearCache()` in-memory only; `clearAllCaches()` on logout | VERIFIED (under a different number than sso-binding's INV-7) | [CODE LIB/SpaarkeAuthProvider.ts:94-103] |
| `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md:177` [CORRECTED by verifier: was ":172-175"] | Office Add-ins: "Token acquired via direct MSAL; not using @spaarke/auth (yet)" | STALE | add-ins use `SpaarkeAuthProvider` + `OfficeNaaStrategy` [CODE src/client/office-addins/shared/services/AuthService.ts:84-98] |
| `AUTH-AND-BFF-URL-PATTERN.md:191` | tenant-specific authority "via `resolveDefaultAuthority()`" | STALE | no such symbol in LIB [CODE grep: 0 hits]; actual `resolveTenantSync`/`buildTenantAuthority` [CODE LIB/config.ts:55-81] |
| `AUTH-AND-BFF-URL-PATTERN.md:199-200` [CORRECTED by verifier: was ":197-199"] | log lines `[SpaarkeAuth:BrowserMsal] acquireTokenSilent OK — scope: …/SDAP.Access`, `[SpaarkeAuth:InMemoryCache] hit` | STALE | actual prefixes `[BrowserMsalStrategy]`, `[SpaarkeAuth] Token acquired via in-memory-cache(browser-msal)`; scope is `user_impersonation` [CODE BrowserMsalStrategy.ts:171,184] [CODE LIB/SpaarkeAuthProvider.ts:74] [CODE LIB/resolveRuntimeConfig.ts:528] |
| `AUTH-AND-BFF-URL-PATTERN.md:138-143` | Code Page `initAuth({clientId, bffApiScope, bffBaseUrl, proactiveRefresh})` without tenant works | VERIFIED (via async discovery) | [CODE LIB/initAuth.ts:158-168] [CODE LIBT/initAuth.tenantDiscovery.test.ts:141-151] |
| `.claude/patterns/auth/spaarke-auth-initialization.md:7-8` | `index.ts` exports `ensureAuthInitialized`; `config.ts` holds runtime config getters | STALE | `ensureAuthInitialized` is a member of the initializer triple, not an export [CODE LIB/index.ts:52] [CODE LIB/createCodePageAuthInitializer.ts:215]; getters live in `createRuntimeConfigStore.ts` [CODE LIB/createRuntimeConfigStore.ts:270-310] |
| `spaarke-auth-initialization.md:17` | `resolveRuntimeConfig` "reads from Dataverse environment variable or falls back to defaults" | CONTRADICTED | throws when any required value is missing [CODE LIB/resolveRuntimeConfig.ts:464-491]; the only fallback is the localStorage cache [CODE LIB/resolveRuntimeConfig.ts:427-435] |
| `.claude/patterns/auth/msal-client.md:7-8` | legacy `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` | STALE | directory does not exist [CODE ls] |
| Code comments `LIB/index.ts:21-22`, `LIB/strategies/AuthStrategy.ts:5-8`, `LIB/strategies/OfficeNaaStrategy.ts:173` | consumers use `initAuth({ strategy: new OfficeNaaStrategy(config) })` | CONTRADICTED by the code they annotate | `IAuthConfig` has no `strategy` [CODE LIB/types.ts:2-73]; `initAuth` never passes one [CODE LIB/initAuth.ts:143] |
| Code comments `LIB/strategies/BrowserMsalStrategy.ts:152`, `OfficeNaaStrategy.ts:430,457` | `logLevel: 3 // Warning` | CONTRADICTED | msal-common `LogLevel.Warning = 1`, `Verbose = 3` [CODE c:/code_files/spaarke/src/client/shared/Spaarke.Auth/node_modules/@azure/msal-common/dist/logger/Logger.d.ts:14-20] |
| `LIB/strategies/BrowserMsalStrategy.ts:260` (comment) | `getMsalInstance()` exists "so SpaarkeAuthProvider can resolve tenant ID from accounts" | STALE | provider never calls it; `tid` comes from the cached JWT [CODE LIB/SpaarkeAuthProvider.ts:143-168] [CODE grep `getMsalInstance` in LIB: strategies only] |
| `LIB/useAuth.ts:43-45` (comment) | "Task 015 adds a BroadcastChannel listener that will trigger a re-render on logout" | CONTRADICTED | `useAuth` has no listener and no React state [CODE LIB/useAuth.ts:49-58]; the listener is in the provider and only clears caches [CODE LIB/SpaarkeAuthProvider.ts:56-61] |
| `LIB/config.ts:94-95` (docstring) [ADDED by verifier] | `resolveConfig` "Throws if required values (clientId, bffApiScope) are not provided" | CONTRADICTED for `bffApiScope` | only `clientId` is checked; `bffApiScope` silently defaults to `''` [CODE LIB/config.ts:107,111-116,155]; the strategies then call MSAL with `scopes: ['']` [CODE LIB/strategies/BrowserMsalStrategy.ts:166] [CODE LIB/strategies/OfficeNaaStrategy.ts:219] — see D16 |
| `LIB/authenticatedFetch.ts:26` (JSDoc) [ADDED by verifier] | "@returns Fetch Response (status 2xx-3xx)" | CONTRADICTED | success test is `response.ok` (2xx only); any 3xx that reaches the loop becomes an `ApiError` [CODE LIB/authenticatedFetch.ts:50-52,63,76-81] (supports D13) |
| `LIB/strategies/OfficeNaaStrategy.ts:75-76` (comment) [ADDED by verifier] | "NAA is supported in: Office on the web (all browsers)" | CONTRADICTED by the code it annotates | `detectNaaSupport()` deliberately returns `false` for `PlatformType.OfficeOnline` so the fallback PCA/popup path is used on the web [CODE LIB/strategies/OfficeNaaStrategy.ts:116-125] |
| `LIB/types.ts:14-16` (JSDoc) [ADDED by verifier] | sync discovery looks in "`Xrm.organizationSettings.tenantId` (frame-walk), `window.__SPAARKE_TENANT_ID__` and the persisted runtime config" (Xrm listed first) | STALE ordering (list order ≠ code order) | code order is window global → persisted config → Xrm [CODE LIB/config.ts:36-47]; the `createCodePageAuthInitializer.ts:45-48` docstring states the correct order |

---

## 7. Needs live verification

| item | why code cannot prove it | suggested test |
|---|---|---|
| Dataverse Web API accepts the session cookie for `environmentvariabledefinitions`/`values` from a web resource / code page iframe (`credentials: 'include'`, no bearer) | server behaviour | load a Code Page in the MDA and in a new window; observe the two GETs return 200 without `Authorization` |
| `storeAuthStateInCookie: true` has any effect on `ssoSilent` in Dataverse iframes with third-party cookies blocked | browser + Entra behaviour; code comment disclaims it | Edge with 3P cookies blocked; clear storage; load PCF; check `[BrowserMsalStrategy] ssoSilent` outcome |
| `ssoSilent` with `loginHint: undefined` when two tenant accounts are cached on one browser (the `_pickAccount` → `null` path) | Entra decides via session cookies; may return `interaction_required` | sign two users of the same tenant into one Edge profile; load a PCF; observe popup or silent success |
| B2B guest (U2): silent redemption of a Spaarke-tenant token from a single cached HOME-tenant account; `tid` of the issued token | real token claims | guest user, fresh profile, home-tenant Entra session only; capture console `[BrowserMsalStrategy]` lines and decode the access token |
| U2 in Outlook/Word (home-tenant host): NAA broker returns a token for the Spaarke-tenant authority | broker behaviour; `_pickAccount` takes `accounts[0]` | Model 1 guest in desktop Outlook; check `[OfficeNaaStrategy] initialized via NAA broker` and the acquire result |
| 401-retry futility: MSAL `acquireTokenSilent` without `forceRefresh` returns the same cached access token after `provider.clearCache()` | MSAL cache semantics | force a BFF 401 (e.g. revoke) and observe whether the three attempts send identical bearer tokens |
| `detectNaaSupport` thresholds (PC 13530, Mac 16.44) and Office-on-the-web standard-popup path | host runtime | run add-in on each host; read `[OfficeNaaStrategy] platform=… version=…` and which init line follows |
| `brk-multihub://<hostname>` and `<origin>/auth-callback.html` are registered on the add-in app registration (SPA/native) | Entra app settings | app registration redirect URIs vs. deployed SWA hostnames |
| `window.location.origin` of every Dataverse org is a registered SPA redirect URI for `sprk_MsalClientId` | Entra app settings | app registration → Authentication → SPA redirect list |
| `/api/config/client` serialises `TenantId` as `tenantId` (library reads lower-camel) and the BFF's `AzureAd:TenantId` is a real tenant (not `common`) | BFF JSON options and app settings are outside this area. [ADDED by verifier] `GetClientConfig` uses `Results.Ok` with no explicit options and no global naming-policy override was found, so the ASP.NET Core default (camelCase) is expected [CODE src/server/api/Sprk.Bff.Api/Api/ConfigEndpoints.cs:40-43,143] — still a framework default, not repo code | `curl https://<bff>/api/config/client` and inspect casing + value |
| B2B guest (U2) with NO cached MSAL account: what `Xrm…userSettings.userPrincipalName` returns for a guest (home UPN, `#EXT#` alias, or empty) — it becomes the `ssoSilent`/popup `loginHint` [CODE LIB/strategies/BrowserMsalStrategy.ts:55-61] [ADDED by verifier] | host runtime value | log `userSettings.userPrincipalName` from a PCF as a Model 1 guest with cleared MSAL storage; compare against the `[BrowserMsalStrategy] ssoSilent hint=…` line |
| MSAL behaviour when `bffApiScope` is `''` (`scopes: ['']`) — consumers that call `initAuth()` without a scope and without `__SPAARKE_BFF_API_SCOPE__` support hit this [CODE LIB/config.ts:107,155] [ADDED by verifier] | MSAL request validation | call `initAuth({clientId})` only, observe whether `acquireTokenSilent`/`ssoSilent` throw an `empty_input_scopes` style error or mint a Graph-default token |
| MSAL `logLevel: 3` produces verbose console output in production bundles | depends on consumer bundler and MSAL logger wiring | open any PCF; count `[MSAL]`-prefixed console lines |
| `logoutPopup` ends the Entra session (and subsequent `ssoSilent` fails) | Entra behaviour | call `useAuth().logout()`; reload; observe whether a popup is required |
| `BroadcastChannel('spaarke-auth-events')` reaches sibling PCF iframes and `navigateTo` dialogs on `*.crm.dynamics.com` | browser origin model for Dataverse iframes | logout in one PCF; observe `Received logout broadcast` in another |
| `Xrm…organizationSettings.tenantId` is empty in nested Code Page iframes (the reason for the async discovery) | host runtime | log the value from a nested Code Page on first load |
| An explicit CIAM authority without `knownAuthorities` is rejected by MSAL (`untrusted_authority`) | MSAL runtime validation | pass `authority: https://spaarkeextid.ciamlogin.com/<tid>` in a test page |
| Every deployed consumer bundle contains the #1453 library (INV-8) — undetectable from the console because `VERSION` was not bumped | deployment state | inspect deployed bundles for the `[Spaarke.initAuth] Duplicate init coalesced` / `tenant_unresolved` strings or bump `VERSION` and redeploy |

---

## 8. Defects, risks and inconsistencies

**D1 — 401 retry cannot obtain a different token (medium).** On a 401 `authenticatedFetch` calls `provider.clearCache()`, which only drops the in-memory entry [CODE LIB/authenticatedFetch.ts:55-59] [CODE LIB/SpaarkeAuthProvider.ts:94-97] [CODE LIB/strategies/InMemoryCache.ts:124-126]; the strategies then call `acquireTokenSilent` without `forceRefresh` (Office passes `forceRefresh: false` explicitly) [CODE LIB/strategies/BrowserMsalStrategy.ts:173] [CODE LIB/strategies/OfficeNaaStrategy.ts:228]. If MSAL serves its cached access token (expected while it is not near expiry — [LIVE?]), the three attempts send the same bearer with 1.5 s of added latency before `auth_exhausted`. The comment's stated purpose for the retry (token refresh) is not achievable in that case.

**D2 — `OfficeNaaStrategy` ignores `requireSilentOnly` and always escalates to `acquireTokenPopup` (medium).** The popup step is unconditional [CODE LIB/strategies/OfficeNaaStrategy.ts:236-253]; no reference to `requireSilentOnly` exists in the file [CODE grep P11]. On the fallback PCA path (which `detectNaaSupport` deliberately selects for Office on the web [CODE LIB/strategies/OfficeNaaStrategy.ts:116-125]) this is a real interactive popup, and it is reachable from non-user-initiated acquires (the add-in's eager `getAccessToken()` after construction — cross-area `src/client/office-addins/shared/services/AuthService.ts:99-104`, whose comment claims "never prompts on startup"). This contradicts the popup-only-on-user-intent rule the code cites as "ADR-028 INV-5" [CODE LIB/types.ts:51].

**D3 — MSAL logger set to Verbose while comments say Warning (low).** `logLevel: 3` in all three MSAL configs [CODE LIB/strategies/BrowserMsalStrategy.ts:152] [CODE LIB/strategies/OfficeNaaStrategy.ts:430,457]; msal-common 14.16.1 defines `Warning = 1`, `Verbose = 3` [CODE c:/code_files/spaarke/src/client/shared/Spaarke.Auth/node_modules/@azure/msal-common/dist/logger/Logger.d.ts:14-20]. `piiLoggingEnabled: false` limits exposure; console noise in production is [LIVE?].

**D4 — No supported way to run `OfficeNaaStrategy` through `initAuth()`; the singleton-based API is unusable in that host (medium).** `IAuthConfig` has no `strategy` field and `initAuth` always builds the default strategy [CODE LIB/types.ts:2-73] [CODE LIB/initAuth.ts:143] [CODE LIB/SpaarkeAuthProvider.ts:48], while three comments document `initAuth({ strategy })` [CODE LIB/index.ts:21-22] [CODE LIB/strategies/AuthStrategy.ts:5-8] [CODE LIB/strategies/OfficeNaaStrategy.ts:173]. Consequence: a host that needs the Office strategy must construct `SpaarkeAuthProvider` itself, so `getAuthProvider()`, `authenticatedFetch`, `useAuth()`, `resolveTenantIdSync()` and `createCodePageAuthInitializer` all throw or misreport (`not_initialized`) there [CODE LIB/initAuth.ts:174-179] — confirmed by the add-in's own comment (cross-area `src/client/office-addins/shared/services/authenticatedJsonFetch.ts:7-11`).

**D5 — `/organizations` fallback survives outside Dataverse hosts, exactly where U2 guests use their HOME tenant (medium).** `requireTenantAuthority` defaults to `isDataverseHost()` [CODE LIB/config.ts:125-126]; outside it the library logs an error and uses `/organizations` [CODE LIB/config.ts:141-148]. Office Add-ins are such hosts; the library's own rationale says this authority fails for guests (AADSTS700016) [CODE LIB/config.ts:10-15 (comment)] [LIVE?]. Whether the add-ins always supply `tenantId` is cross-area (a03).

**D6 — Invariant numbering is inconsistent across the canonical doc, the audit doc and code comments (low).** sso-binding INV-4 = env-var order, INV-5 = UPN hint, INV-6 = omit authority, INV-7 = one PCA [DOC-ONLY .claude/patterns/auth/spaarke-sso-binding.md:41-44]; AUDIT-FINDINGS INV-4 = one provider, INV-5 = 6-strategy cascade, INV-6 = token survival, INV-7 = clearCache in-memory [DOC-ONLY .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md:289-292]; code cites "INV-5" for popup-only-on-intent [CODE LIB/types.ts:51] and "INV-7" for clearCache semantics [CODE LIB/SpaarkeAuthProvider.ts:92]. ADR-028 only says "preserve INV-1..INV-8" and delegates to sso-binding [DOC-ONLY .claude/adr/ADR-028-spaarke-auth-architecture.md:22].

**D7 — `VERSION` not bumped for a behaviour-changing release, defeating INV-8 detection (medium).** `VERSION = '2.0.0'` and `package.json` version unchanged [CODE LIB/version.ts:12] [CONFIG package.json:3]; the #1453 commit touched 20 library files but neither of these [CODE git show --stat 2598b2fb9]. A consumer bundled before #1453 (still able to produce `/organizations` inside Dataverse) logs the same `v2.0.0` line as a current one [CODE LIB/SpaarkeAuthProvider.ts:51].

**D8 — `OfficeNaaStrategy._pickAccount` takes `accounts[0]` with no tenant match; `logout()` is a no-op without an account (low).** [CODE LIB/strategies/OfficeNaaStrategy.ts:355-358,281-282]. The Browser strategy's #1453 selection logic was not ported. Risk R-U2: a guest's home-tenant account can be chosen against a Spaarke-tenant authority [LIVE?].

**D9 — Duplicate `initAuth` coalesces across differing `bffApiScope` / `bffBaseUrl` / `tenantId` (low-medium).** Coalescing keys only on `clientId` and on whether the existing authority is tenant-specific [CODE LIB/initAuth.ts:73-74,114-136]; a second init with a different tenant is silently ignored [CODE LIBT/initAuth.tenantDiscovery.test.ts:262-268]. Two Code Pages in one bundle pointing at different BFFs would share the first's config.

**D10 — Commercial-cloud authority host is hard-coded while Dataverse host detection includes sovereign clouds (low).** `AUTHORITY_HOST` is always `https://login.microsoftonline.com` [CODE LIB/tenant.ts:49,97-99], yet `DATAVERSE_HOST_RE` treats `dynamics.cn`, `microsoftdynamics.us/.de` and `appsplatform.us` pages as Dataverse hosts [CODE LIB/tenant.ts:155-156], so a sovereign-cloud deployment would fail closed on the tenant rule and then sign in against the wrong cloud's STS. (`/api/config/client` would return a cloud-correct authority, but the library reads only its `tenantId` [CODE LIB/resolveRuntimeConfig.ts:228-229].)

**D11 — Dead / undocumented surface (low).** `window.__SPAARKE_BFF_API_SCOPE__` declared but never read [CODE LIB/types.ts:114] [CODE grep P4]; `window.__SPAARKE_BFF_BASE_URL__` written but never read by the library [CODE LIB/createRuntimeConfigStore.ts:234]; `BrowserMsalStrategy.getMsalInstance()` has no library caller [CODE grep]; `useAuth` is named as a hook but is a plain function without React (lint `rules-of-hooks` would constrain callers unnecessarily) [CODE LIB/useAuth.ts:49-58]. [ADDED by verifier] `TokenResult.tenantId` (optional) is declared [CODE LIB/types.ts:88-89] but never populated: both strategies' `_validate()` return only `{accessToken, expiresOn}` [CODE LIB/strategies/BrowserMsalStrategy.ts:323] [CODE LIB/strategies/OfficeNaaStrategy.ts:330] [CODE grep `tenantId:` over LIB/strategies]; the provider derives `tid` from the JWT instead [CODE LIB/SpaarkeAuthProvider.ts:218-229]. `AuthError('no_response')` is an unreachable code path [CODE LIB/authenticatedFetch.ts:67-69] (section 2.4).

**D15 — Four independent frame-walk implementations with slightly different semantics (low). [ADDED by verifier]** (1) `tenant.ts` `hostFrames()` — window, parent, top (top only if ≠ parent); used by `xrmGlobalContext`/`xrmTenant`/`isDataverseHost` [CODE LIB/tenant.ts:107-121,124-134,141-152,174-183]; (2) `resolveRuntimeConfig.ts` `resolveXrmContext()` — same three frames, first ctx with `getClientUrl` [CODE LIB/resolveRuntimeConfig.ts:262-293]; (3) `BrowserMsalStrategy.resolveLoginHint()` — window, parent, top (top pushed even when equal to parent) [CODE LIB/strategies/BrowserMsalStrategy.ts:43-54]; (4) `SpaarkeAuthProvider._isXrmAvailable()` — `window.Xrm ?? parent.Xrm ?? top.Xrm`, i.e. the FIRST frame that has any `Xrm` object is the only one checked for `Utility.getGlobalContext` [CODE LIB/SpaarkeAuthProvider.ts:231-240]. So `requireXrm` can throw `xrm_required` in a frame whose own `Xrm` lacks `Utility` even though `isDataverseHost()` (which keeps walking) would be true. Only consumers passing `requireXrm: true` are affected (none found in section 1.3 greps, so practical impact is nil today).

**D16 — `bffApiScope` is not validated; an empty scope reaches MSAL (low-medium). [ADDED by verifier]** `resolveConfig` throws only for a missing `clientId` [CODE LIB/config.ts:111-116]; `bffApiScope` falls back to `''` with no window-global or env-var source [CODE LIB/config.ts:84,107,155], contradicting its own docstring [CODE LIB/config.ts:94-95]. A consumer calling `initAuth()` without `bffApiScope` (the documented "Basic initialization" example at [CODE LIB/initAuth.ts:34-35]) sends `scopes: ['']` to `acquireTokenSilent`/`ssoSilent`/`acquireTokenPopup` [CODE LIB/strategies/BrowserMsalStrategy.ts:166,173,185,210]. The MSAL outcome is [LIVE?] (section 7). `createCodePageAuthInitializer` makes `bffApiScope` a required field, so Code Pages using the factory are not exposed [CODE LIB/createCodePageAuthInitializer.ts:41].

**D12 — Stale persisted tenant sources (low).** `__spaarke_rtc__.tenantId` (≤ 60 min) is read BEFORE live Xrm [CODE LIB/config.ts:36-47]; `__spaarke_bff_tenant__` is trusted for 24 h per BFF host [CODE LIB/resolveRuntimeConfig.ts:151,180-190]. Both are same-origin / same-BFF scoped, so the practical risk is a tenant reconfiguration propagating late.

**D13 — `authenticatedFetch` treats every non-2xx, including 3xx such as 304, as an `ApiError` (low).** `response.ok` is the only success test [CODE LIB/authenticatedFetch.ts:50-52,63,76-81].

**D14 — Documentation drift (low, see section 6).** Section 6 records 25 verdict rows that are STALE or CONTRADICTED in whole or in part, spread over `.claude/patterns/auth/*`, `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `.claude/constraints/auth.md`, `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` and `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`, plus five in-code comments that no longer match the code they sit next to (`initAuth({ strategy })` ×3, `logLevel // Warning`, `getMsalInstance` purpose, `useAuth` listener). [ADDED by verifier] Four more in-code comment/code mismatches were added to section 6 by verification: `resolveConfig` "throws for bffApiScope", `authenticatedFetch` "2xx-3xx", `OfficeNaaStrategy` "NAA supported on Office on the web", and the `IAuthConfig.authority` discovery-order wording.

Security observations (no defect): the library never stores a token itself; `atob` JWT parsing is unverified by design (used only for `exp`/`tid` hints) [CODE LIB/SpaarkeAuthProvider.ts:218-229] [CODE LIB/strategies/InMemoryCache.ts:11-20]; persisted localStorage values are non-secret configuration [CODE LIB/resolveRuntimeConfig.ts:103-110,192-198]; the only Bearer materialisation site is lint-exempted and is the one place that sets the header [CONFIG .eslintrc.json:11-26] [CODE LIB/authenticatedFetch.ts:45].

---

## 9. Pending (unmerged) changes affecting this area

Commands (read-only): `git -C <wt> diff origin/master...HEAD --stat -- src/client/shared/Spaarke.Auth`, `git -C <wt> log origin/master..HEAD -- src/client/shared/Spaarke.Auth`, `git -C <wt> status --short`, `git -C <wt> merge-base --is-ancestor …`.

| worktree (branch) | HEAD | branch-introduced changes under `src/client/shared/Spaarke.Auth` | uncommitted changes | relevance |
|---|---|---|---|---|
| `C:/code_files/spaarke-wt-customer-provisioning-orchestration-r1` (`work/customer-provisioning-orchestration-r1`) | `d2ef3ed9c` 2026-10-08 (re-checked by verifier; merge-base with `origin/master` is now `3f1d78f30`, which INCLUDES #1453 — `git merge-base --is-ancestor 2598b2fb9 HEAD` → yes) [CORRECTED by verifier: was "`a9018175e` … the branch's merge-base (`1ef1b0949`) predates #1453, so this worktree currently RUNS the pre-#1453 library"] | none (three-dot diff `origin/master...HEAD --stat -- src/client/shared/Spaarke.Auth` empty; no branch commits touch the library) | none in scope (`git status --short -- src/client/shared/Spaarke.Auth` empty; whole-worktree status clean) | On merge it inherits master's library unchanged; it already carries the #1453 library. ADR-028 A6 (keyless stamps) and the 240a client-access work are server/provisioning side — no client-library impact found. |
| `C:/code_files/spaarke-wt-spaarkeai-word-add-in-r1` (`work/spaarkeai-word-add-in-r1`) | `8acf93bd1` 2026-10-08 — contains #1453 and is fully merged into master (merge-base = HEAD) | none | 85 modified + 2 untracked files worktree-wide (`git status --short | wc -l` = 87; `git diff --stat` = 85 files, +455/−255), of which the auth-relevant set is 17 consumer `authInit.ts` files (`src/client/pcf/{CommunicationActions,CommunicationAttachments,CommunicationConversationPanel,CommunicationMessageActions,CommunicationTimeline,CommunicationTimelineRegarding,DocumentRelationshipViewer,RelatedDocumentCount,SemanticSearchControl,TrackingFieldTrio}/…/authInit.ts`, `src/client/code-pages/{DocumentRelationshipViewer,PlaybookBuilder}/src/services/authInit.ts`, `src/solutions/{CommunicationReconciliation,DailyBriefing,EmailPage,Reporting,SpeAdminApp}/src/services/authInit.ts`), one test mock (`CommunicationAttachments/__tests__/__mocks__/spaarkeAuth.ts`) and two NEW tests (`PlaybookBuilder/src/services/__tests__/authInit.test.ts`, `CommunicationAttachments/__tests__/authInit.test.ts`); the remainder are manifests, `solution.xml`, `pack.ps1` and app files [CORRECTED by verifier: was "10 modified CONSUMER files (…, one test mock)"]. Two diff shapes: PCF `authInit.ts` files add an optional `tenantId` and pass `...(isValidTenant(tenantId) ? { tenantId: tenantId.trim() } : {})` to `initAuth`, importing `isValidTenant` from `@spaarke/auth` (35 `isValidTenant` lines in the diff); `PlaybookBuilder/src/services/authInit.ts` instead adds `setAuthRuntimeConfig(IRuntimeConfig)` and spreads `clientId`/scope/tenant from the stored runtime config into `initAuth` [ADDED by verifier] [CODE git diff in that worktree] | Nothing in this area's scope; the "task 122 @spaarke/auth tenant fix" library side is already on master as #1453 (commit title "tasks 122 + 124"). The consumer edits are a02 scope and are not on master. |
| `C:/code_files/spaarke-wt-unified-access-control-r2` (`work/unified-access-control-r2`) | `a119cd9fd` 2026-10-08 [CORRECTED by verifier: was "`ad53afe0e`"] | none (three-dot diff empty; merge-base `e34c7c0e2` predates #1453 — `git merge-base --is-ancestor 2598b2fb9 HEAD` → no) | none in scope (worktree status clean) | Model 1 guest classification is server-side (areas a04+); this worktree still runs the pre-#1453 client library until it merges master. |

No pending change alters current behaviour of `src/client/shared/Spaarke.Auth/**`. The in-scope statement of record is master @ 45202953f as traced above.

---

## 10. Verification log

Verifier pass: independent re-read of every `src/client/shared/Spaarke.Auth/**` source, config and test file under `C:/code_files/spaarke-wt-spaarke-auth-system-of-record-r1` (worktree HEAD `d5ec906a5` = `45202953f` + one docs-only commit; `git diff --stat 45202953f HEAD -- src/client/shared/Spaarke.Auth` is empty, so the library is byte-identical to master @ 45202953f) [CODE git]. Read-only; no builds, no network, no git writes.

**Claims checked: 212** (every `[CODE …]` / `[CONFIG …]` / `[DOC-ONLY …]` tag in sections 1-9 was re-opened at the cited path:line; each `[LIVE?]` was checked only for being genuinely unprovable from code).

**Confirmed: 198.** Every `LIB/*` and `LIBT/*` path:line citation resolved to the described code. In particular, the hard cases held: the `/organizations` fallback is produced only when `requireTenantAuthority` is false [CODE LIB/config.ts:125-148]; `_pickAccount` in-tenant filtering and the "exactly one candidate" rule [CODE LIB/strategies/BrowserMsalStrategy.ts:278-290]; `OfficeNaaStrategy` ignores `requireSilentOnly` and uses `accounts[0]` [CODE LIB/strategies/OfficeNaaStrategy.ts:218-256,355-358]; `InMemoryCache` generation counter and single in-flight promise [CODE LIB/strategies/InMemoryCache.ts:62-106]; `VERSION`/`package.json` not touched by #1453 [CODE git show --stat 2598b2fb9]; `logLevel: 3 = Verbose` [CODE sibling-checkout msal-common 14.16.1 Logger.d.ts:14-20]; U4 CIAM: no `knownAuthorities` in either MSAL config and `AUTHORITY_HOST` hard-coded [CODE LIB/tenant.ts:49] [CODE LIB/strategies/BrowserMsalStrategy.ts:137-156] [CODE LIB/strategies/OfficeNaaStrategy.ts:412-463]; the `docs/adr/ADR-028-*` full version does not exist [CODE ls]; `src/client/pcf/UniversalQuickCreate` does not exist [CODE ls]; all ADR-028 line citations (8, 21, 22, 35, 48, 66, 78, 104, 125), `.claude/constraints/auth.md` (50, 90-91, 104, 192), `spaarke-sso-binding.md` (22, 27-32, 38-45, 55, 57), `spaarke-auth-initialization.md` (7-8, 17), `msal-client.md` (7-8) and `AUTH-AND-BFF-URL-PATTERN.md:138-143,191` are at the stated lines.

**Corrected: 14** (each marked inline `[CORRECTED by verifier: was "…"]`):
1. §1.3 — `CommunicationPage/src/services/authInit.ts` does NOT construct `PublicClientApplication`; the grep hit was a comment saying it never does. Same correction applied to the §6 `ADR-028:35` row.
2. §1.3 — "7 consumer sites pass `requireSilentOnly`/`requireTenantAuthority`" → 5 sites in 5 files, all `requireSilentOnly`; none pass `requireTenantAuthority`.
3. §1.3 and §2.11 INV-7 — "41 `package.json` files … by `file:` link" → 41 dependants: 38 `file:`, 3 version-range (shared libs); consumers bundle from `Spaarke.Auth/src` via 38 build-config aliases, not from `dist/index.js`.
4. §1.2 — `config.test.ts` "24 tests" → 21 `it` + 3 `it.each` (43 executed cases).
5. §1.2 — `InMemoryCache.test.ts` "15 tests incl. in-flight ×6" → 16 incl. ×7.
6. §1.2 — `tenant.test.ts` "~12 tests" → 8 `it` + 8 `it.each`.
7. §1.2 — proactive-refresh test lines `70-77` → `70-84`.
8. §2.1 step 4 — the `/organizations`-provider upgrade requires a REQUESTED `clientId` [CODE LIB/initAuth.ts:115]; an init with no `clientId` is coalesced before `selectProvider` runs [CODE LIB/initAuth.ts:74].
9. §6 — `AUDIT-FINDINGS-AUTH-SYSTEM.md` INV-1/2 rows are at :286-287 (not :284-285); INV-3 at :288 (not :286).
10. §6 — `AUTH-AND-BFF-URL-PATTERN.md` "not using @spaarke/auth (yet)" is at :177 (not :172-175).
11. §6 — `AUTH-AND-BFF-URL-PATTERN.md` stale log lines are at :199-200 (not :197-199).
12. §9 — provisioning worktree HEAD is `d2ef3ed9c`, merge-base `3f1d78f30`, which already CONTAINS #1453; the "runs the pre-#1453 library" statement was stale.
13. §9 — word-add-in worktree has 85 modified + 2 untracked files (17 `authInit.ts`, 1 mock, 2 new tests among them), not "10 modified consumer files"; PlaybookBuilder's diff has a different shape (`setAuthRuntimeConfig`) from the `isValidTenant` pattern described.
14. §9 — unified-access-control-r2 HEAD is `a119cd9fd` (still excludes #1453 — that part was right).

**Refuted outright: 1** (item 1 above — a factual claim about a file that does the opposite of what was stated). Items 2-14 are miscounts, off-by-N line references, a flow-condition imprecision, and stale worktree state; none inverted a behavioural conclusion about the library.

**Additions: 14** (each marked `[ADDED by verifier]`):
- §1.1 — verifier search patterns V1-V5.
- §1.2 — `_resetBroadcastChannelForTests` (module export, not in `index.ts`).
- §1.3 — `/api/config/client` casing evidence (`Results.Ok`, no global naming override, file comment asserting camelCase default) and the `common`/`organizations` `TenantId` pass-through that the library rejects.
- §2.4 — unreachable `AuthError('no_response')`.
- §2.6 — the posting bundle does not receive its own `BroadcastChannel` message.
- §2.10 — two more `detectNaaSupport()` false paths; comment/code contradiction on Office-on-the-web.
- §2.11 INV-4 — memoised/cached-config step inside `discoverTenantId`.
- §4 U2 — MSAL `AccountInfo.tenantId` semantics (sibling-checkout typings) and the guest `loginHint` open question; host premise for add-ins tagged as task-brief assumption.
- §6 — four new comment-vs-code rows (`resolveConfig` docstring, `authenticatedFetch` "2xx-3xx", `OfficeNaaStrategy` NAA-on-web, `IAuthConfig.authority` order wording).
- §7 — three new live-verification rows (JSON casing evidence, guest `userPrincipalName`, empty-scope MSAL behaviour).
- §8 D11 — `TokenResult.tenantId` never populated; `no_response` unreachable.
- §8 D15 — four divergent frame-walk implementations; `_isXrmAvailable` stops at the first frame with any `Xrm`.
- §8 D16 — `bffApiScope` unvalidated; `scopes: ['']` reaches MSAL for bare `initAuth()` callers.
- §8 D14 — pointer to the new §6 rows.

**Confidence: high** for everything inside `src/client/shared/Spaarke.Auth/**` — every file was read in full and every cited line re-checked; the library is small (3,600 source lines) and self-contained. **Medium** for the cross-area pointers in §1.3/§6/§9: the greps were re-run and the cited external files were opened at the cited lines, but those files were not traced beyond the lines quoted (they belong to a02/a03/BFF areas). **Not provable here** (left as `[LIVE?]`): MSAL runtime semantics (empty scope, `untrusted_authority`, `AccountInfo.tenantId` for guests, `acquireTokenSilent` cache behaviour on 401 retry), Entra/broker behaviour, Dataverse cookie acceptance for the env-var reads, and the ASP.NET Core camelCase default for `/api/config/client`.
