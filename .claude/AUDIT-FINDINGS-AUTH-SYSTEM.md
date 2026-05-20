# Auth System Audit — 2026-05-18

> **Trigger**: Investigating intermittent 401s on `/api/ai/chat/*` led to discovery of a snapshot-based token propagation chain (App.tsx → AiSessionProvider → SprkChat → fetch). Root cause confirmed via App Insights: `IDX10223: token expired`. Symptom is one instance of a system-wide architectural issue.
> **Scope**: Comprehensive audit of auth across BFF API, client packages (`@spaarke/auth`), and every consumer (PCFs, Code Pages, Office Add-ins, shared components).
> **Objective**: Define a resilient, environment-independent auth architecture and a migration path.
> **Status**: Findings + target architecture + migration plan. **No code changes yet** — pending user sign-off on scope.

---

## 1. Executive Summary

The codebase has a strong **infrastructure layer** for auth (`@spaarke/auth` with 6-strategy cascade, proactive refresh, 401 retry, cross-iframe sharing) but a weak **consumer discipline** — most callers snapshot `token: string` from React state at render time and propagate it down component trees, defeating every refresh and retry mechanism the infrastructure provides. The intermittent 401 you observed is a symptom: when a SpaarkeAi page sits idle past token expiry, every BFF call fails because the cached prop is stale.

Three classes of weakness cut across the stack:

1. **Snapshot leaks** — at least 12 client surfaces capture `accessToken` as a string and pass it via props/closures. The auth provider has no mechanism to push refreshes to these snapshots.
2. **Strategy gaps** — `BridgeStrategy` and `XrmStrategy` don't decode the JWT `exp` claim; they assume a 55-min lifetime. If the underlying token rotates early (or is published with a short TTL), stale tokens are returned as valid.
3. **Environment leakage** — `AzureAd__ClientSecret` is plain-text in App Service config; `TenantId: "common"` is hardcoded in the BFF template; `Copilot audience` includes a hardcoded UUID (`auth-3e04ab58-...`); two `buildBffApiUrl()` implementations exist; Office Add-ins use a parallel auth stack that doesn't share with `@spaarke/auth`.

The fix is **not** to add another layer of patches. It's to make function-based auth the **only** contract at every boundary, eliminate the snapshot escape hatches, fix the strategy gaps, and consolidate the configuration model so deploying to a new tenant/environment is mechanical.

---

## 2. Current Architecture (Current State)

### 2.1 Server side (BFF API)

**8 distinct auth flows currently in use** ([AuthorizationModule.cs](../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs), [Program.cs](../src/server/api/Sprk.Bff.Api/Program.cs)):

| # | Flow | Purpose | Credential | Caching |
|---|---|---|---|---|
| 1 | Azure AD JWT (user) | Primary user auth | Bearer token from MSAL | n/a |
| 2 | M365 Copilot Agent JWT | Copilot-to-API calls | Bearer with `auth-3e04ab58.../...` audience | n/a |
| 3 | Graph OBO | User → Graph | OBO exchange via MSAL confidential client | Redis 55min |
| 4 | Dataverse OBO (Copilot only) | Agent → Dataverse | OBO exchange | Redis (tenant-keyed) |
| 5 | App-only Graph | Background jobs | `ClientSecretCredential` | Lazy singleton |
| 6 | App-only Dataverse | Background jobs | `ClientSecretCredential` (multiple sites) | n/a |
| 7 | Power Pages B2B Guest | Portal users | Standard JWT (same scheme) + Contact lookup filter | 60s contact cache |
| 8 | Webhook clientState | Graph subscriptions | Plain string match | n/a |

**Auth policies**: 25 named policies via `AddAuthorization()` (driveitem.*, container.*, SystemAdmin). All resource-scoped.

**Unprotected endpoints** (20+): `/healthz/*`, `/ping`, `/status`, `/debug/*` (7 endpoints — including `/debug/token` which logs JWT claims!), `/msal/config`, `/api/communications/incoming-webhook`, `/api/v1/emails/webhook-trigger`, `/api/finance/rollup/*`, `/api/office/bootstrap`, `/api/visualization/*`, `/api/admin/builder-scope/import`, `/api/ai/rag/*`, `/api/registration/*`.

### 2.2 Client library (`@spaarke/auth`)

**6-strategy token acquisition cascade** ([SpaarkeAuthProvider.ts:87-153](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L87-L153)):

| # | Strategy | Source | Validates JWT exp? | On error |
|---|---|---|---|---|
| 1 | In-memory cache | Per-instance closure | ✅ Yes (5-min buffer) | null |
| 2 | sessionStorage | Same-origin shared | ✅ Yes (5-min buffer) | null |
| 3 | Bridge (frame walk) | `window.__SPAARKE_BFF_TOKEN__` | ❌ **NO — assumes 55min lifetime** | null |
| 4 | Xrm (frame walk) | `Xrm.Utility...getAppProperties().accessToken` | ❌ **NO — assumes 55min lifetime** | null |
| 5 | MSAL silent | `acquireTokenSilent` + `ssoSilent` | ✅ Yes (MSAL internal) | null + warn |
| 6 | MSAL popup | Interactive prompt | ✅ Yes (MSAL internal) | null + warn |

**Public API** ([index.ts:1-31](../src/client/shared/Spaarke.Auth/src/index.ts)):
- Imperative: `initAuth`, `getAuthProvider`, `authenticatedFetch`, `buildBffApiUrl`, `resolveRuntimeConfig`
- ❌ **No React hooks exported** — consumers wire their own React glue, leading to snapshot patterns

**Proactive refresh**: Exists (`_startProactiveRefresh` at [SpaarkeAuthProvider.ts:330-339](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L330-L339)) but **disabled by default** (opt-in via `IAuthConfig.proactiveRefresh`).

**401 retry**: `authenticatedFetch` retries 3× with exponential backoff (500ms/1s/2s) and clears in-memory cache between attempts. sessionStorage is NOT cleared (intentional — avoids cascading other components).

### 2.3 Consumers (callers)

Inventory of 17 distinct consumer surfaces. **Snapshot-prone (12)**:

| Consumer | File | Pattern |
|---|---|---|
| SprkChat hooks (3 of them) | [SprkChat/hooks/](../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/) | Receives `accessToken` prop, captures in closure for async fetch |
| SpaarkeAi App.tsx | [App.tsx:76-90](../src/solutions/SpaarkeAi/src/App.tsx#L76-L90) | One-shot `useEffect` snapshots token at mount |
| PlaybookBuilder Code Page | [aiPlaybookService.ts:279](../src/client/code-pages/PlaybookBuilder/src/services/aiPlaybookService.ts#L279) | Raw fetch + `Bearer ${accessToken}` |
| DocumentRelationshipViewer | [VisualizationApiService.ts:40](../src/client/code-pages/DocumentRelationshipViewer/src/services/VisualizationApiService.ts#L40) | Direct header assignment |
| Office Add-in SseClient | [SseClient.ts:78](../src/client/office-addins/shared/taskpane/services/SseClient.ts#L78) | EventSource cannot auto-retry on 401 |
| Office Add-in SaveFlow | [useSaveFlow.ts:526,864](../src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts#L526) | Raw fetch from hook closure |
| BFF adapter docs | [bffDataServiceAdapter.ts](../src/client/shared/Spaarke.UI.Components/src/utils/adapters/bffDataServiceAdapter.ts) | Example code teaches the bad pattern |
| AnalysisWorkspace (partial) | `src/client/code-pages/AnalysisWorkspace/` | Mixed — some paths correct, some snapshot |

**Already correct (per-request token, 8)**:
- Office Add-in `ApiClient` ([ApiClient.ts:64,88](../src/client/office-addins/shared/services/ApiClient.ts#L64))
- SemanticSearch Code Page (MSAL per-request)
- UniversalDatasetGrid PCF (`authenticatedFetch`)
- UniversalQuickCreate PCF (`@spaarke/auth`)
- SdapApiClient (callback pattern)
- SpeDocumentViewer PCF (deprecated AuthService)
- AnalysisWorkspace (correct paths only)

---

## 3. Findings, by Severity

### 🔴 CRITICAL — Resilience / Security

**C-1. Plain-text client secrets in App Service config** (already deployed to dev)
- `AzureAd__ClientSecret` and `AgentToken__ClientSecret` are stored as literal values, not `@Microsoft.KeyVault(SecretUri=...)` references. Secret value visible to anyone with `az webapp config appsettings list` access. Secret was exposed in tool output during this audit.
- **Action**: Rotate the secret. Convert to Key Vault reference (template already has the pattern for `Dataverse:ClientSecret`).

**C-2. `/debug/token` endpoint anonymous in production code**
- Anyone can POST a token and have it parsed + claims logged. TODO comment says "remove before production".
- **Action**: Remove all `/debug/*` endpoints before next deploy or gate them behind `SystemAdmin` policy + non-prod environment check.

**C-3. Webhook `clientState` is plain string comparison, not HMAC**
- `/api/communications/incoming-webhook`, `/api/v1/emails/webhook-trigger` validate via string compare ([CommunicationEndpoints.cs:401](../src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs#L401)). If endpoint URL + clientState leak, attacker can forge notifications. `DEVELOPMENT_MODE` flag silently disables validation entirely.
- **Action**: Switch to HMAC-SHA256 signature validation with secret rotation. Remove dev-mode bypass.

### 🟠 HIGH — Architectural

**H-1. Token snapshot propagation is the dominant pattern**
- 12+ surfaces capture `accessToken: string` at render time and use it for fetches that may run minutes/hours later. The provider's refresh + retry mechanisms can't see these — they're closed over a stale value.
- This is the root cause of the SpaarkeAi 401 you observed.
- **Action**: Replace `token: string` API surfaces with function-based contracts (`getAccessToken: () => Promise<string>`, `authenticatedFetch`). Make `token: string` impossible to pass around.

**H-2. BridgeStrategy and XrmStrategy skip JWT `exp` validation**
- Both assume a 55-min token lifetime ([BridgeStrategy.ts:14](../src/client/shared/Spaarke.Auth/src/strategies/BridgeStrategy.ts#L14), [XrmStrategy.ts:85](../src/client/shared/Spaarke.Auth/src/strategies/XrmStrategy.ts#L85)). If the publisher's token has a shorter actual lifetime (e.g. policy-driven 30min), expired tokens are returned as valid. Saved by the 401 retry path, but adds an unnecessary failed-call round-trip.
- **Action**: Decode `exp` claim in both strategies, reject if past expiry minus 5min buffer (same convention as `CacheStrategy`).

**H-3. Proactive refresh disabled by default**
- The mechanism exists but no consumer enables it. With proactive refresh on, the in-memory cache rotates ~5 min before expiry — eliminating the "first call after idle is 401" pattern entirely.
- **Action**: Enable `proactiveRefresh: true` as the default in `IAuthConfig` (current default is `false`). Make opt-out explicit.

**H-4. `useAiSession()` exposes `token: string` as a value**
- Every component that destructures `.token` from this hook is a future snapshot bug ([AiSessionProvider.tsx:99-104](../src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx#L99-L104)). The API surface needs to change.
- **Action**: Replace `token: string | null` with `getAccessToken: () => Promise<string>` and `authenticatedFetch: AuthenticatedFetchFn`. Keep `isAuthenticated: boolean` for UI gating only.

**H-5. App.tsx token snapshot is a one-shot `useEffect`**
- ([App.tsx:81-105](../src/solutions/SpaarkeAi/src/App.tsx#L81-L105)). `useEffect` with `[]` deps runs once at mount. After token expires (~80min), the React state is permanently stale. Same pattern likely exists in other Code Pages.
- **Action**: Stop snapshotting. Provider exposes functions; UI components call them per-request.

**H-6. Office Add-ins use a parallel auth stack**
- Office Add-ins use [authConfig.ts](../src/client/office-addins/shared/auth/authConfig.ts) (direct MSAL.js, NAA broker) — not `@spaarke/auth`. Two auth implementations to maintain, two sets of bugs to fix.
- **Action**: Unify on `@spaarke/auth`. Add NAA as a 7th strategy (or alternative `MsalNaaStrategy`) so Office Add-ins use the same cascade.

### 🟡 MEDIUM — Operational / Hygiene

**M-1. `buildBffApiUrl()` duplicated in PCF utils**
- Two implementations ([buildBffApiUrl.ts](../src/client/shared/Spaarke.Auth/src/buildBffApiUrl.ts), [environmentVariables.ts:265-280](../src/client/pcf/shared/utils/environmentVariables.ts#L265-L280)). Same behavior today; divergence risk over time.
- **Action**: Delete PCF version. Import from `@spaarke/auth`.

**M-2. `AzureAd:TenantId: "common"` hardcoded in template**
- ([appsettings.template.json:36](../src/server/api/Sprk.Bff.Api/appsettings.template.json#L36)). `"common"` allows ANY Microsoft tenant to validate. The Graph client uses an explicit tenant ([GraphClientFactory.cs:44]), so there's a config mismatch.
- **Action**: Change to `#{TENANT_ID}#` placeholder, substituted at deploy.

**M-3. M365 Copilot audience UUID hardcoded**
- ([appsettings.template.json:226](../src/server/api/Sprk.Bff.Api/appsettings.template.json#L226)). `auth-3e04ab58-8450-44d6-b95b-daca16b6cbdb` is the Teams Developer Portal SSO app ID. Cross-tenant deployments need this parameterized.
- **Action**: Introduce `#{COPILOT_SSO_PROVIDER_APP_ID}#` placeholder.

**M-4. Inconsistent credential strategy server-side**
- Cosmos / AI / External Dataverse use `DefaultAzureCredential` (managed identity). Graph app-only and Dataverse service principal use `ClientSecretCredential`. Three secrets to rotate vs. zero with managed identity everywhere.
- **Action**: Migrate Graph app-only and Dataverse to `DefaultAzureCredential` (App Service has a system-assigned managed identity).

**M-5. JWT validator clears `ValidAudience` without idempotency guard**
- [AuthorizationModule.cs:36-47](../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L36-L47). `PostConfigure` mutates the options; if called twice or chained incorrectly, audience list could be lost.
- **Action**: Add idempotency check + log warning if audience list ends up empty.

**M-6. `resolveRuntimeConfig()` caches for 5 minutes**
- ([resolveRuntimeConfig.ts:66-68](../src/client/shared/Spaarke.Auth/src/resolveRuntimeConfig.ts#L66-L68)). Useful for stability, but hides env var changes that happen post-deploy. No way to invalidate from outside `clearRuntimeConfigCache()`.
- **Action**: Acceptable; document it. Optionally add a "version" env var that invalidates the cache when bumped.

### 🟢 LOW — Test coverage / polish

**L-1. No end-to-end tests for the 6-strategy cascade** — only per-strategy unit tests.
**L-2. No tests for `authenticatedFetch` 401 retry path.**
**L-3. No tests for proactive refresh timer.**
**L-4. No cross-iframe token sharing tests** (single-frame only).
**L-5. No cross-tenant detection** — bridge token from Tenant A used while user is in Tenant B would silently fail at the BFF (403) instead of being caught client-side.

---

## 4. Target Architecture

### 4.1 Principles

1. **Function-based auth is the only contract.** No `token: string` field on any hook return, context value, or component prop. The runtime boundary: `authenticatedFetch` is the **only** code that materializes a Bearer token as a string for HTTP. `getAccessToken()` exists as a narrow escape hatch for the SSE case (where `fetch()` wrapping fails on `ReadableStream` lifecycle) and is **explicitly commented** as restricted-use. TypeScript "branded types" don't survive minification — enforcement is the API shape, reinforced by lint rules that ban `Authorization: \`Bearer \${...}\`` outside `authenticatedFetch.ts`.

2. **MSAL's localStorage cache is the cross-tab/iframe sharing mechanism.** `cacheLocation: 'localStorage'` already shares tokens across all same-origin tabs and iframes in a session. We don't transport tokens via `BroadcastChannel` or `window.__SPAARKE_BFF_TOKEN__` global — both are unnecessary (MSAL covers it) and the latter is a same-origin security smell. **BroadcastChannel is reserved for invalidation events only** (logout, token-revocation broadcasts) where messages flow between contexts but the token itself doesn't.

3. **One auth provider, pluggable strategies.** `@spaarke/auth` exposes a single provider interface with strategy plugins:
   - `BrowserMsalStrategy` — Dataverse PCFs + Code Pages (MSAL.js with tenant-specific authority, localStorage + cookie)
   - `OfficeNaaStrategy` — Outlook/Word Add-ins (MSAL.js with NAA broker)
   - Future: `PortalB2cStrategy`, `MobileStrategy`, etc. — added without touching consumers
   
   The 6-strategy cascade (Cache, SessionStorage, Bridge, Xrm, MsalSilent, MsalPopup) is reduced to: **MSAL strategy + in-memory cache wrapper with explicit JWT exp validation**. The other layers were historical accumulations; MSAL's own cache + JWT decode covers their use cases more cleanly.

4. **Defense in depth on freshness.** Three layers:
   - Proactive refresh ~5 min before expiry (timer, default-on)
   - Per-request `getAccessToken()` (MSAL silent acquisition)
   - 401 retry with cache clear + backoff (3 attempts)

5. **Environment independence is structural, not procedural.** Every env-specific value comes from exactly one source per layer:
   - Client: Dataverse environment variables (`sprk_*`), resolved at runtime via `resolveRuntimeConfig()`
   - Server: App Service config with Key Vault references for secrets
   - No env values hardcoded in code or committed config files

6. **Managed identity everywhere on the server.** No client secrets in App Service config. `DefaultAzureCredential` for Cosmos, AI, Graph app-only, Dataverse.

7. **Anonymous endpoints are explicit and minimal.** `/healthz`, `/ping`, `/status` only. Webhooks use HMAC signatures. No debug endpoints in production builds.

8. **Deployment model is per-customer-tenant install.** Each customer deploys Spaarke into their own Azure tenant with their own App Service, Key Vault, Dataverse org, and app registrations. Tokens never cross customer boundaries; data never crosses customer boundaries. This shapes security scope significantly:
   - **Don't need**: cross-customer data isolation in code, multi-tenant query filters, shared-instance privilege separation
   - **Customer owns**: Conditional Access policies, MFA enforcement, secret rotation cadence, Sentinel/Monitor integration, identity governance
   - **We own**: secure deployment artifacts, hardened code, audit emission contract, claims handling, mechanical environment tokenization

### 4.2 Public APIs (target state)

**`@spaarke/auth` — new exports**:

```typescript
// Hook-based React API (NEW — eliminates snapshot bugs)
export function useAuth(): {
  isAuthenticated: boolean;
  getAccessToken: () => Promise<string>;        // always fresh
  authenticatedFetch: AuthenticatedFetchFn;     // bound to current provider
  tenantId: string;                              // sync getter from cached JWT
};

// Imperative API (unchanged — for non-React callers)
export function initAuth(config?: IAuthConfig): Promise<SpaarkeAuthProvider>;
export function getAuthProvider(): SpaarkeAuthProvider;
export function authenticatedFetch(url: string, init?: RequestInit): Promise<Response>;
export function buildBffApiUrl(base: string, path: string): string;
export function resolveRuntimeConfig(): Promise<IRuntimeConfig>;

// REMOVED: any API that exposes token as a string
```

**`AiSessionProvider` — new context value**:

```typescript
interface AiSessionContextValue {
  // Auth (function-based, not value-based)
  isAuthenticated: boolean;
  getAccessToken: () => Promise<string>;
  authenticatedFetch: AuthenticatedFetchFn;
  // REMOVED: token: string | null
  
  // Session/playbook/streaming — unchanged
  bffBaseUrl: string;
  chatSessionId: string | null;
  setChatSessionId: (id: string) => void;
  // ...
}
```

**`SprkChat` props — function-based**:

```typescript
interface ISprkChatProps {
  apiBaseUrl: string;
  authenticatedFetch: AuthenticatedFetchFn;        // REQUIRED — replaces accessToken
  getAccessToken: () => Promise<string>;            // REQUIRED — for SSE streaming
  // REMOVED: accessToken: string
  // ... rest unchanged
}
```

### 4.3 Environment-independence checklist

For deployment to a new tenant/environment, exactly these values change:

**Client side** (Dataverse environment variables — set per environment via solution import or admin UI):
- `sprk_BffApiBaseUrl` → host of the BFF (e.g. `https://spe-api-tenantX.azurewebsites.net/api`)
- `sprk_BffApiAppId` → BFF service principal app ID (GUID)
- `sprk_MsalClientId` → MSAL public client app ID (GUID)
- `sprk_TenantId` → Azure AD tenant GUID (used for MSAL authority)

**Server side** (App Service config + Key Vault):
- App Service: `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__Audience`, `KeyVault__Url`
- Key Vault: `BFF-API-ClientSecret`, `Dataverse-ServiceUrl`, `Dataverse-ClientSecret` (or replaced by managed identity), `ServiceBus-ConnectionString`, `Redis-ConnectionString`, `Communication-WebhookSecret`, `Email-WebhookSecret`, `Copilot-SSO-ProviderAppId`

**Deployment script** (`scripts/deploy-bff-api.ps1` or similar) substitutes a single tokenization pass over `appsettings.template.json` with these variables. Adding a new environment = one parameter file + Key Vault populated.

**Test for env-independence**: clone the repo, set 4 client env vars in Dataverse, set 8 App Service settings + populate Key Vault, deploy. Nothing else in code or config should require touching.

---

## 4.4 Regression Invariants (DO NOT BREAK)

The codebase has a hard-won set of MSAL configuration values that MUST be preserved through any refactor. These come from [`.claude/patterns/auth/spaarke-sso-binding.md`](patterns/auth/spaarke-sso-binding.md) (canonical, confirmed with product owner 2026-05-12) and a real incident where MSAL fired popups on every new tab due to wrong tenant authority. **Every Phase below has an explicit regression-test gate on these.**

### Invariant set (verified against [config.ts:13-42](../src/client/shared/Spaarke.Auth/src/config.ts#L13-L42) and SpaarkeAuthProvider construction):

| # | Invariant | Source of truth | Test |
|---|---|---|---|
| INV-1 | `cacheLocation: 'localStorage'` (NEVER `sessionStorage`) | [SpaarkeAuthProvider.ts:54](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L54) | Close browser → reopen → app loads without popup |
| INV-2 | `storeAuthStateInCookie: true` (NEVER `false`) | [SpaarkeAuthProvider.ts:60](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L60) | 3rd-party cookies blocked → `ssoSilent` still works |
| INV-3 | `authority: https://login.microsoftonline.com/{tenantId}` resolved from `Xrm.Utility.getGlobalContext().organizationSettings.tenantId` via frame-walk. **NEVER `/organizations` or `/common`** | [config.ts:20-42](../src/client/shared/Spaarke.Auth/src/config.ts#L20-L42) | Check console log `[SpaarkeAuth]` — `authority` must contain the actual tenant GUID, not `/organizations` |
| INV-4 | Single shared provider via `getAuthProvider()` — every component reuses the same `SpaarkeAuthProvider` instance | [initAuth.ts:5](../src/client/shared/Spaarke.Auth/src/initAuth.ts#L5) singleton | No component instantiates `new PublicClientApplication()` directly |
| INV-5 | 6-strategy cascade in this exact order: Cache → SessionStorage → Bridge → Xrm → MsalSilent → MsalPopup | [SpaarkeAuthProvider.ts:87-153](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L87-L153) | New tabs / iframes find tokens via bridge/sessionStorage; MsalPopup never fires in steady state |
| INV-6 | Tokens survive tab close, browser close, idle > 60min (refresh-token auto-renew via localStorage) | INV-1 + INV-2 combined | Close all tabs → reopen 65 min later → app loads, no prompt |
| INV-7 | `clearCache()` clears in-memory only, **NOT sessionStorage** (would cascade other components). Only `clearAllCaches()` on explicit logout. | [SpaarkeAuthProvider.ts:162-175](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L162-L175) | 401 in one PCF doesn't trigger MSAL prompts in sibling PCFs |
| INV-8 | Every consumer of `@spaarke/auth` must be rebuilt + redeployed when library changes (no auto-update via bundling) | Bundling reality, [spaarke-sso-binding.md §"Bundling Reality (CRITICAL)"](patterns/auth/spaarke-sso-binding.md#bundling-reality-critical) | After library change, verify every PCF + Code Page bundle has the new version |

### Regression detection

When MsalPopup fires (which it should NOT in steady state), the cause is almost always:
- A consumer using an old library bundle (INV-8 violated → INV-3 has `/organizations` instead of tenant GUID)
- A consumer that bypassed the cascade (INV-4 violated → `new PublicClientApplication()` directly)
- A consumer using `sessionStorage` for MSAL cache (INV-1 violated → wiped on tab close)

**Verification command** (paste into Edge DevTools console after any auth-related change):

```javascript
// Clear all auth state to force first-strategy paths
localStorage.clear(); sessionStorage.clear();
document.cookie.split(';').forEach(c => {
  document.cookie = c.split('=')[0].trim() + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';
});
// CLOSE BROWSER COMPLETELY. Reopen. Navigate to a Spaarke surface.
// PASS: no popup; console shows `authority: https://login.microsoftonline.com/{actual-tenant-guid}/`
// FAIL: popup fires OR authority contains `/organizations` or `/common`
```

### How Phase 1 specifically preserves these invariants

The migration plan touches `AiSessionProvider` and `App.tsx` consumer code — **not the MSAL configuration inside `SpaarkeAuthProvider`**. Specifically:
- Adding `useAuth()` hook is new code; doesn't alter the constructor at [SpaarkeAuthProvider.ts:44-83](../src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts#L44-L83)
- Enabling `proactiveRefresh: true` only affects the in-memory cache timer (line 81-83); doesn't change MSAL config
- Fixing Bridge/Xrm JWT exp validation adds a decode + compare step; doesn't change MSAL config
- Refactoring `AiSessionProvider` context API and `App.tsx` stops the snapshot pattern; `getAuthProvider()` is still called the same way

**Required for sign-off on each Phase**: a screenshot of the DevTools console with `authority: https://login.microsoftonline.com/{tenant-guid}` showing the actual tenant ID (not `/organizations`).

---

## 4.5 Future Auth Requirements (Anticipation Map)

Auth touches many surfaces beyond AI/SprkChat. The target architecture must accommodate these without rework. Status as of this audit:

### A. SharePoint Embedded (SPE) — file access

**Current state**: ✅ **Fully covered.** Flows through existing Graph OBO chain.
- User JWT → BFF → OBO exchange → Graph token with `FileStorageContainer.Selected` and `Files.Read.All` scopes → SPE container / drive item operations
- `SpeFileStore` is the single facade ([ADR-007](docs/adr/ADR-007-spe-storage-seam.md)) — all SPE operations route through it; no Graph SDK leaks
- Resource-level authorization via `ResourceAccessHandler` + `DocumentAuthorizationFilter` ([AuthorizationModule.cs:78-149](../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L78-L149)) covers driveitem.* and container.* operations
- 25 named policies map to SPE operations (preview, download, upload, replace, delete, move, copy, share, version, etc.)
- App-only admin operations (container creation, container type config) handled via `ClientSecretCredential` (Phase 6 will migrate to managed identity)

**Future considerations**:
- **F-A1.** If SPE adds new permission models (e.g., container-level conditional access tied to AAD groups), the `ResourceAccessHandler` will need an additional rule. Current architecture supports this — add `IAuthorizationRule` implementation.
- **F-A2.** Cross-container scenarios (linking documents across SPE containers) need per-container auth checks. Already supported by per-resource authorization pattern.
- **F-A3.** External user access to SPE files (B2B guests browsing matter files) — partially supported via `ExternalCallerAuthorizationFilter`; verify SPE-specific paths route through it.
- **F-A4.** If Microsoft introduces "Containers v2" or new SPE auth concepts, abstraction stays at `SpeFileStore` facade level. No client-side impact.

**Architectural commitment**: Treat SPE auth as a **derivative of Graph OBO + resource authorization**. No SPE-specific token type; no separate auth surface. Everything goes through `SpeFileStore` server-side and never touches the client directly. ✅ Already aligned.

### B. Multi-tenant SaaS (selling Spaarke to multiple customer tenants)

**Current state**: ⚠️ **Single-tenant per deployment.** The BFF audience (`api://{API_APP_ID}`) is tied to one Azure AD app, and one Dataverse org. Deploying to a new customer tenant = new app registration, new BFF instance, new Dataverse env.

**This is intentional today and probably correct**, but the target architecture must be deliberate about it:

| Approach | Pros | Cons | When |
|---|---|---|---|
| **Per-tenant deployment** (current) | Strong isolation; per-tenant Azure costs; can use managed identity per env | Operational overhead — each new customer = new deployment | Spaarke is high-value, low-volume (current model) |
| **Multi-tenant single deployment** | One BFF for many customers; lower per-customer cost | Tenant isolation in code (every query needs tenant filter); complex blast radius; cross-tenant bug risk | High-volume SaaS at scale |
| **Hybrid (managed cluster)** | Per-tenant Dataverse, shared BFF infra | Most operational complexity, but flexible | Enterprise customers on shared compute |

**Architectural commitment**: Keep per-tenant deployment as the primary model. Make deployment-time tokenization **completely mechanical** (Phase 5/7 makes this true). Don't introduce multi-tenant patterns in code (tenant-filtered queries, tenant-aware caching keys) unless/until business model demands it.

**Specific invariants to maintain for future flexibility**:
- All env-specific values stay in Dataverse env vars (client) + App Service config + Key Vault (server). No hardcoded tenant IDs, audiences, or URLs in code.
- All caching keys (Redis, Cosmos) include `tenantId` prefix. Currently true for `AgentTokenService` ([line 28](../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Agent/AgentTokenService.cs#L28)); verify for other caches.
- Background jobs scope by tenant. Verify `ServiceBusJobProcessor` carries tenant context through job payloads.

### C. External users (B2B guests + B2C anonymous + Power Pages portal)

**Current state**:
- ✅ **B2B guests**: `ExternalCallerAuthorizationFilter` ([line 12](../src/server/api/Sprk.Bff.Api/Infrastructure/Authorization/ExternalCallerAuthorizationFilter.cs#L12)) resolves Dataverse Contact via email claim. Works because B2B guests get a JWT from the home tenant that the BFF validates against `ValidAudiences` + Contact lookup gates access.
- ⚠️ **Power Pages portal**: documented as Power Pages B2B but separate JWT validation could be needed (issuer differs).
- ❌ **B2C anonymous**: not supported today. Would need a separate auth scheme.

**Future considerations**:
- **F-C1.** If portal traffic grows, the `ExternalCallerAuthorizationFilter` becomes hot path. Cache contact lookups longer (currently 60s) and add per-user rate limiting.
- **F-C2.** Self-service registration is partially supported (`RegistrationModule`, `/api/registration/*` endpoints, currently AllowAnonymous). Needs to be tied to a stronger auth scheme or rate-limited heavily.
- **F-C3.** If B2C is ever needed (true anonymous users), introduce a third auth scheme alongside JWT and Power Pages. Architecture supports adding schemes via `AddAuthentication(...).AddJwtBearer("scheme-name", ...)`.

**Architectural commitment**: Treat each external user class as a **named authentication scheme** (currently 2: default + Power Pages). Adding a new class = new scheme. Don't conflate.

### D. Service-to-service / integrations (Logic Apps, Power Automate, webhook receivers)

**Current state**:
- ✅ **Webhook receivers**: `/api/communications/incoming-webhook`, `/api/v1/emails/webhook-trigger` use `clientState` validation (Phase 6 will replace with HMAC).
- ⚠️ **API key auth**: `/api/admin/builder-scope/import`, `/api/ai/rag/*` use API key in header. Currently `AllowAnonymous` + header check.
- ✅ **Service bus messages**: managed identity to Service Bus (no token in payload).

**Future considerations**:
- **F-D1.** API key auth should be formalized. Today it's per-endpoint custom validation. Recommend a single `AddAuthentication("ApiKey", ...)` scheme that all such endpoints share.
- **F-D2.** If Logic Apps / Power Automate connectors are built, they typically use OAuth client credentials flow (app-only). Already supported by Graph app-only pattern — extend to BFF if needed.
- **F-D3.** Mobile clients (if ever) use the same JWT scheme as web. No new auth flow needed.

**Architectural commitment**: Add API key as a first-class authentication scheme (Phase 6 expansion). No more ad-hoc header validation.

### E. Conditional Access compliance

**Current state**: 
- MSAL handles CA prompts (MFA, device compliance) via the `MsalPopupStrategy` fallback.
- `MsalSilentStrategy` catches `MsalUiRequiredException` and falls through. Behavior: if CA requires interactive auth, the user gets a popup.

**Future considerations**:
- **F-E1.** If a tenant policy requires "compliant device" or "MFA every X hours", popups may fire mid-session. Acceptable per [INV-3 / spaarke-sso-binding §"Acceptable prompts"](patterns/auth/spaarke-sso-binding.md#binding-requirements) (item 2).
- **F-E2.** Conditional Access on the BFF API (per-scope MFA) would need scope-specific token requests. Currently we use the single `user_impersonation` scope; extension is straightforward.

**Architectural commitment**: Don't try to bypass CA. Trust MSAL to handle prompts; ensure CA-driven popups don't crash the app (current behavior is good).

### F. Logout / token revocation

**Current state**: ⚠️ **Weak.** `clearAllCaches()` exists but is not consistently called on logout. No backend revocation of OBO tokens.

**Future considerations**:
- **F-F1.** Implement a real logout flow: `MsalInstance.logoutPopup()` or `logoutRedirect()` + `clearAllCaches()` + `clearBridgeToken()` + clear cookies.
- **F-F2.** OBO tokens cached in Redis survive for 55min after user logout — a security gap. Consider per-user invalidation on logout (add user-ID-keyed Redis pattern).
- **F-F3.** Conditional Access "force sign-out everywhere" requires server-side revocation list. Defer until needed.

**Architectural commitment**: Add a `logout()` method to `@spaarke/auth` that does the full sequence. Update server-side OBO cache to support per-user invalidation. Phase 7 deliverable.

### G. Audit trail integrity

**Current state**: ✅ **Reasonable.** User identity from JWT claims (`oid`, `upn`, `email`) flows into logs via structured logging. App Insights captures every auth failure with audience/issuer/error.

**Future considerations**:
- **F-G1.** When acting on behalf of users (OBO), audit logs should record both the user identity AND the agent/app identity (`appid` claim). Currently mixed.
- **F-G2.** Webhook handlers should record the source service identity once HMAC is in place (Phase 6).

**Architectural commitment**: Phase 7 doc should specify "audit log identity columns" (user OID, app ID, on-behalf-of flag) and align all log sites.

### H. Mobile / native clients

**Current state**: ❌ **Not supported.** The architecture is browser-only (MSAL.js).

**Future considerations**:
- **F-H1.** If a mobile app is ever needed, use MSAL.NET or MSAL Android/iOS. Backend auth (BFF JWT validation) doesn't change.
- **F-H2.** Mobile gets the same `api://{API_APP_ID}/user_impersonation` scope. No new server-side work.

**Architectural commitment**: BFF auth contract is mobile-ready as-is. No action needed.

### Future-scenario summary

| Scenario | Today's status | Future readiness | Required architecture changes |
|---|---|---|---|
| **A. SPE file access** | ✅ Fully covered via Graph OBO | ✅ Ready | None |
| **B. Multi-tenant SaaS** | ⚠️ Per-tenant deployment | ✅ Ready if env-independence achieved (Phase 5/7) | None for current model |
| **C. External users** | ✅ B2B works, ❌ B2C not supported | ⚠️ Add scheme when B2C needed | New auth scheme if B2C |
| **D. Service-to-service** | ⚠️ Ad-hoc API key validation | ⚠️ Formalize API key scheme | Phase 6 expansion |
| **E. Conditional Access** | ✅ MSAL handles | ✅ Ready | None |
| **F. Logout / revocation** | ❌ Weak | ⚠️ Phase 7 logout flow + per-user OBO invalidation | Add `logout()` API |
| **G. Audit trail** | ✅ Reasonable | ⚠️ Phase 7 doc + align log sites | Standardize log fields |
| **H. Mobile clients** | ❌ Not supported | ✅ Ready (BFF contract unchanged) | None |

---

## 5. Final Scope — Spaarke Auth v2 + Reasonable Hardening

Single deliverable, organized as six workstreams. Code tasks only — non-code work (threat model documents, paid security testing, compliance attestations) is **out of scope for implementation** but enumerated in §7 (SOC 2 / Enterprise Review Readiness Map) so the team knows what's needed for SOC 2 / enterprise customer review when the time comes.

Workstreams A and B can run in parallel after A1-A2 land. Workstream C is independent (server-side). D, E run alongside.

### Workstream A — Core library rebuild (`@spaarke/auth` v2)

| # | Task | Hours |
|---|---|---|
| A1 | Refactor `@spaarke/auth` to pluggable strategy pattern. Define `AuthStrategy` interface; implement `BrowserMsalStrategy` (lift existing `SpaarkeAuthProvider` MSAL config logic unchanged — INV-1..INV-7 preserved by literal code lift). Drop `XrmStrategy` and `BridgeStrategy`. Reduce cache layers: MSAL.localStorage is the persistence layer; a thin in-memory wrapper validates JWT `exp` with 5-min buffer. | 4 |
| A2 | Replace public API with function-based contract. Add `useAuth()` hook returning `{isAuthenticated, getAccessToken, authenticatedFetch, tenantId, logout}`. `getAccessToken` is the narrow escape for SSE; `authenticatedFetch` is the primary API. Remove all `accessToken: string` exposure from `@spaarke/auth`. Add ESLint rule banning `Authorization: \`Bearer \${...}\`` literals outside `authenticatedFetch.ts`. | 3 |
| A3 | Enable `proactiveRefresh: true` as default. Decode JWT `exp` in the cache layer; force re-acquisition 5 min before expiry. | 1 |
| A4 | Add `logout()` API: MSAL `logoutPopup` + cache clear + BroadcastChannel invalidation message + `POST /api/auth/logout` (server endpoint that purges per-user OBO entries from Redis). | 2 |
| A5 | Library version stamp: `SpaarkeAuthProvider.version`. Log on init. Detects un-rebuilt consumers (INV-8 violation surfaces as console warning instead of silent regression). | 1 |
| **A total** | | **11** |

### Workstream B — Consumer migration

| # | Task | Hours |
|---|---|---|
| B1 | **SpaarkeAi** (~10 files): `App.tsx`, `AiSessionProvider`, `ThreePaneShell`, `ConversationPane`, `WelcomePanel`, `ChatHistoryPanel`, `WorkspaceLandingWidget`, `ChatPanel`, `FeedbackButtons`. Stop snapshotting; use `useAuth()`. `SprkChat` API change: drop `accessToken`, require `authenticatedFetch` + `getAccessToken`. `useSseStream` calls `getAccessToken()` per-stream-open. | 5 |
| B2 | **Other Code Pages**: PlaybookBuilder ([aiPlaybookService.ts:279](../src/client/code-pages/PlaybookBuilder/src/services/aiPlaybookService.ts#L279)), DocumentRelationshipViewer ([VisualizationApiService.ts:40](../src/client/code-pages/DocumentRelationshipViewer/src/services/VisualizationApiService.ts#L40)), AnalysisWorkspace (mixed paths). Same pattern. | 4 |
| B3 | **PCFs** verification: confirm UniversalDatasetGrid, UniversalQuickCreate, SpeDocumentViewer, all others use `authenticatedFetch`. Rebuild + redeploy each (INV-8). | 2 |
| B4 | **Office Add-ins**: register `OfficeNaaStrategy` with `@spaarke/auth`. Fix `SseClient.ts:78` and `useSaveFlow.ts:526,864` (raw Bearer snapshots). Existing `ApiClient` per-request pattern stays correct. | 4 |
| B5 | **Shared components**: update `bffDataServiceAdapter` example/docs to function-based. Delete duplicate `buildBffApiUrl` from `src/client/pcf/shared/utils/environmentVariables.ts:265-280`; import from `@spaarke/auth`. | 1 |
| **B total** | | **16** |

### Workstream C — Server hardening (BFF)

| # | Task | Hours |
|---|---|---|
| C1 | Rotate `AzureAd__ClientSecret` + `AgentToken__ClientSecret`. Convert App Service config entries to Key Vault references (template already uses pattern for `Dataverse:ClientSecret`). Coordinate App Service restart. | 2 |
| C2 | Migrate Graph app-only ([GraphClientFactory.cs:90](../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs#L90)) + Dataverse service identity (multiple job handlers) to `DefaultAzureCredential`. Verify App Service system-assigned managed identity has required permissions. | 3 |
| C3 | Remove `/debug/*` endpoints (or `#if DEBUG` guard for non-prod only). Specifically `/debug/token`, `/debug/dlq`, `/debug/handlers`, `/debug/services`, and other 7 anonymous debug routes. | 1 |
| C4 | Replace webhook `clientState` with HMAC-SHA256 signature validation. Endpoints: `/api/communications/incoming-webhook`, `/api/v1/emails/webhook-trigger`. Add `Communication:WebhookSigningKey` / `Email:WebhookSigningKey` Key Vault refs. Remove `DEVELOPMENT_MODE` bypass. | 3 |
| C5 | Formalize named API key auth scheme. Add `AddApiKey()` extension method. Replace ad-hoc header validation on `/api/admin/builder-scope/import`, `/api/ai/rag/*`. API keys per Key Vault. | 3 |
| C6 | Add idempotency guard in [AuthorizationModule.cs:29-48](../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L29-L48) `PostConfigure<JwtBearerOptions>`. Log warning if audience list ends up empty. | 1 |
| C7 | Fix `appsettings.template.json`: change `AzureAd:TenantId: "common"` to `#{TENANT_ID}#` placeholder. Parameterize Copilot audience UUID (`auth-3e04ab58-...`) as `#{COPILOT_SSO_PROVIDER_APP_ID}#`. | 1 |
| C8 | Audit logging middleware: enriches every authenticated request with `oid`, `appid`, `obo` flag, `tenantId`, `correlationId` into structured log scope. Standard log fields make customer Sentinel/Monitor integration mechanical (customer pipes via their Diagnostic Settings — no code change per customer). | 3 |
| C9 | Rate limiting policies on anonymous + API key endpoints: `/api/registration/*`, `/api/v1/emails/webhook-trigger`, `/api/communications/incoming-webhook`, `/api/admin/builder-scope/import`, `/api/ai/rag/*`. Per-IP + per-user buckets. | 2 |
| **C total** | | **19** |

### Workstream D — Reasonable security hardening

What enterprise customers reasonably expect for a hosted-in-their-tenant product. **Not** Fort Knox (no DPoP, no hardware-bound keys, no privilege separation across multiple SPs for one install — those are deferred or out of scope given the per-tenant model).

| # | Task | Hours |
|---|---|---|
| D1 | Add CSP + Trusted Types middleware on the BFF for all HTML/JS responses (PowerPages embedded scenarios, debug HTML if any). Strict policy: `script-src 'self'`, no inline, no eval. Trusted Types enforcement in production builds. For Code Pages (hosted in Dataverse iframe), document required CSP additions to the Dataverse env or app shell. | 3 |
| D2 | Enable Continuous Access Evaluation (CAE) on Microsoft.Identity.Web. `services.AddAuthentication().AddMicrosoftIdentityWebApi(opts => opts.ClientCapabilities = new[] {"cp1"})`. Tokens get evaluated mid-session for revocation/disable events; admin disables user account → token stops working within minutes, not hours. | 1 |
| D3 | Identity claims hardening audit. Grep codebase for `email`, `upn`, `preferred_username` claims used as canonical identity. Replace with `oid` (Azure AD object ID — immutable per user per tenant). Specifically audit: audit log writes, authorization checks, Contact lookups (the lookup may still use email, but the resulting ID should be Dataverse Contact GUID, not the email itself). | 3 |
| D4 | Step-up auth scaffolding: middleware that returns 401 with `claims` challenge when an endpoint is tagged `[RequiresStepUp]` and the current token's `acr` claim doesn't meet a minimum. Apply to 2-3 sensitive ops as proof points (e.g., delete-matter, export-data, modify-admin-permissions). Customer's CA policy decides what `acr` levels mean. | 3 |
| D5 | Refresh token rotation test: integration test that confirms MSAL issues a new RT on each refresh and invalidates the old. Catches a class of regression where Microsoft changes MSAL defaults. | 1 |
| **D total** | | **11** |

### Workstream E — CI / Bundling hygiene

| # | Task | Hours |
|---|---|---|
| E1 | Secret scanning in CI: `gitleaks` GitHub Action runs on every PR. Blocks merge on detection. Add `.gitleaks.toml` allowlist for known false positives (test fixtures, etc.). | 2 |
| E2 | Auth regression test pack: scripted browser test (Playwright) that runs the [spaarke-sso-binding ritual](patterns/auth/spaarke-sso-binding.md#verification-after-changes) against a synthetic consumer bundle. Verifies INV-1..INV-7 (authority contains tenant GUID, no popup after fresh load, token survives synthetic idle, 401 retry succeeds). Runs on every PR touching `@spaarke/auth` or any consumer. | 4 |
| E3 | Dependabot for npm + nuget. Auto-PRs for security updates. | 1 |
| **E total** | | **7** |

### Workstream F — Engineering canonical docs (minimal)

Only the engineering-team-facing docs that travel with the code change. **No customer-facing security documentation, no threat model, no compliance attestation work** — see §7.

| # | Task | Hours |
|---|---|---|
| F1 | Write `ADR-027: Spaarke Auth Architecture`. Captures: function-based contract rule, pluggable strategy model, MSAL invariants (INV-1..INV-8), per-tenant deployment assumption, deferred items (DPoP, multi-SP), CAE adoption. | 2 |
| F2 | Update `.claude/patterns/auth/spaarke-sso-binding.md` and `.claude/constraints/auth.md` to reflect v2: function-based contract is the MUST rule; `accessToken: string` in public APIs is the MUST NOT. | 1 |
| F3 | Add a new-environment deployment checklist to `docs/guides/auth-deployment-setup.md`: 4 client Dataverse env vars + 8 App Service settings + Key Vault secret names + verification steps. Mechanical setup, no decision-making per env. | 1 |
| **F total** | | **4** |

### Totals

| Workstream | Hours | Parallel? |
|---|---|---|
| A — Library rebuild | 11 | Foundation |
| B — Consumer migration | 16 | After A1-A2 |
| C — Server hardening | 19 | Yes (independent) |
| D — Security hardening | 11 | Mostly server-side; parallel with C |
| E — CI / bundling | 7 | Yes (independent) |
| F — Docs | 4 | At the end |
| **Total** | **68** | ~8-9 working days (5-6 with parallelization) |

### Phasing options

Two reasonable paths:

**Option 1 — Single coherent push** (recommended). All workstreams in one project. Single migration of all ~30 consumers in one batch (minimizes INV-8 risk). One PR (large but coherent) or a small handful of stacked PRs. 5-6 working days with parallelization.

**Option 2 — Three short cycles**. (a) Library + SpaarkeAi consumers (A + B1) — fixes the user-visible 401. (b) Server hardening + remaining consumers (C + B2-B5). (c) Security hardening + CI + docs (D + E + F). Each cycle is independently shippable.

Option 2 lets you pause for review between cycles. Option 1 ships faster and locks in INV-8 (consumers all in sync). My recommendation: **Option 1** if you can take the runway; **Option 2** if you want check-in points.

---

## 6. What's Explicitly Out of Scope (and Why)

These items would appear on a true "Fort Knox" enterprise auth roadmap but are **not** included in v2:

| Item | Why out of scope |
|---|---|
| **DPoP / sender-constrained tokens** | Major client+server complexity. The per-tenant deployment model means tokens never leave the customer's tenant; the threat model doesn't include cross-customer token replay. Evaluate for v3 if a customer explicitly requires it. |
| **Multi-SP privilege separation within a single install** | In per-tenant model, a compromised SP affects one customer, not all. Worth doing eventually, but blast radius is already bounded. Document as v3 roadmap. |
| **HSM-backed key management** | Customer choice — they can require AAD certificate-based credentials in their tenant. We don't enforce it. |
| **WebAuthn / FIDO2 / passkeys** | AAD handles transparently if the customer's tenant enables it. No code change needed unless we want to recommend a CA policy. |
| **Mobile client support** | No mobile clients exist. BFF contract is mobile-ready as-is. |
| **Cross-customer collaboration / federation** | Not in product scope. Each customer install is isolated. |
| **B2C anonymous portal** | Not in current product. Adding later is a new named auth scheme, doesn't change v2. |
| **OBO chain depth > 1** | Current OBO is one hop deep (User → BFF → Graph or Dataverse). Don't add multi-hop unless the product requires it. |
| **Real-time token revocation list / introspection cache** | CAE (D2) gets us 90% of the value with 10% of the complexity. Full per-call introspection is over-engineered for this product. |
| **Cryptographic audit log chaining** | Customer pipes our structured logs to their Sentinel/Monitor; **their** audit storage provides immutability. We don't reimplement what Azure Monitor already does. |
| **Threat model document** | Operational work, not code. Required for SOC 2 (§7). Should be written but isn't a v2 code task. |
| **Pen test / external security review** | Operational engagement, not code. Required for enterprise customer reviews (§7). Schedule before GA. |
| **Per-customer-tenant audit reports** | Operational. Customer owns their AAD audit + Sentinel. We provide the log emission, they pipe it where they want. |

---

## 7. SOC 2 / Enterprise Review Readiness Map

What the v2 + Hardening scope above delivers as code/configuration, and what is required *additionally* (non-code) for SOC 2 audit or enterprise customer security review. This section documents what's needed; the actual non-code work is not in the implementation plan above.

### 7.1 Code/configuration evidence the v2 scope provides

| Requirement | What v2 delivers | Evidence artifact |
|---|---|---|
| **Authentication** | MSAL.js with tenant-specific authority; AAD JWT validation; named schemes for B2B / API key | `@spaarke/auth` library, `AuthorizationModule.cs`, `ADR-027` |
| **Authorization** | 25 named policies + `ResourceAccessHandler` + per-operation deny codes | `AuthorizationModule.cs:81-169` |
| **Encryption in transit** | HTTPS-only (App Service); HSTS headers | App Service config + HSTS middleware |
| **Encryption at rest** | Azure-default for all stores (App Service, Cosmos, Service Bus, Redis, Key Vault, SPE containers) | Azure resource configs |
| **Secret management** | All secrets in Key Vault (after Workstream C1); managed identity for outbound (after C2) | Key Vault audit log; deployment template |
| **Audit logging** | Structured log enrichment with `oid`, `appid`, `obo`, `tenantId`, `correlationId` (after C8) | Audit middleware code + sample log entries |
| **Session management** | MSAL refresh token rotation (verified by D5); CAE enabled (D2); proactive refresh (A3); logout API (A4) | `@spaarke/auth` v2 |
| **Anonymous endpoint inventory** | `/healthz`, `/ping`, `/status` only after C3 (debug endpoints removed) | Endpoint route table; CI test |
| **Webhook integrity** | HMAC-SHA256 (after C4) | `WebhookValidationFilter.cs` |
| **Identity claim hygiene** | `oid` as canonical user ID everywhere (after D3) | Grep results; code review of audit log calls |
| **Rate limiting** | Policies on anonymous + API key endpoints (after C9) | Rate limit configuration |
| **Step-up auth capability** | `[RequiresStepUp]` attribute + middleware (after D4) | Step-up middleware code; 2-3 tagged endpoints as proof |
| **CSP / XSS posture** | Strict CSP headers + Trusted Types (after D1) | Middleware code; CSP response header in test |
| **Secret leak prevention** | Gitleaks in CI (after E1) | CI run history; `.gitleaks.toml` |
| **Auth regression detection** | Spaarke-sso-binding ritual automated (after E2) | Playwright test results |

### 7.2 Required additionally for SOC 2 (non-code work — do before audit engagement)

These are operational/documentation deliverables. None require library code changes; all require time and (in some cases) procurement.

| Requirement | What's needed | Estimated effort |
|---|---|---|
| **Written security policies** | Information security policy, access control policy, incident response policy, change management policy. Templates available (Vanta, Drata, SecureFrame) or copy from open-source. | 1-2 weeks calendar |
| **Threat model document** | STRIDE analysis + attack trees + mitigations matrix. Reviewed annually. One-time write, then maintain. | 1 week |
| **Risk register** | List of risks, severity, mitigations, owners. Reviewed quarterly. | 0.5 week one-time |
| **Access review process** | Quarterly review of who has access to what. Just a recurring meeting + a spreadsheet, but must be documented. | Operational |
| **Background checks** | For all engineers with prod access. HR procurement. | Procurement |
| **Vendor risk management** | Subprocessor list (Microsoft, etc.); their SOC 2 attestations on file. | Procurement |
| **Business continuity / DR plan** | Backup procedures, RTO/RPO, failover plan. Test annually. | 1 week one-time + annual test |
| **Incident response runbooks** | Token compromise, admin compromise, secret leak, ransomware. Annual tabletop exercise. | 1 week one-time |
| **Annual penetration test** | Third-party engagement. ~$15-30k. Required for SOC 2 Type II + most enterprise customers. | Procurement + 2-week engagement |
| **Annual security awareness training** | All engineers. Vendor-provided (Knowbe4, etc.). | Procurement |
| **SOC 2 Type II audit engagement** | 12-month observation period + auditor engagement. ~$30-60k for Type II. | Procurement + 12 months |
| **Customer security questionnaire responses** | CAIQ, SIG, custom forms. Library of pre-written answers using v2 architecture as reference. | 1 week initial; recurring per customer |

### 7.3 Required additionally for enterprise customer review (typically simpler than SOC 2)

Customers reviewing Spaarke for their tenant typically ask for:

| Requirement | Source from v2 + Hardening |
|---|---|
| Architecture diagram + data flow | Diagram from `ADR-027` + this audit doc |
| Auth flow documentation | `ADR-027` + `spaarke-sso-binding.md` |
| Encryption posture | Azure defaults + Key Vault refs |
| Secret management process | Key Vault refs + rotation runbook (1-pager, not in v2 scope) |
| Audit log capability | C8 middleware emits standard fields → customer pipes to their Sentinel |
| Vulnerability management | Dependabot (E3) + Gitleaks (E1) + (nice-to-have: SAST tool) |
| Incident response | Runbook (operational, not v2 code) |
| Pen test results | (operational, not v2 code) |
| Subprocessor list | (operational, not v2 code) |
| **Customer responsibilities** (document explicitly) | CA policies, MFA enforcement, secret rotation cadence in their tenant, Sentinel/Monitor setup, identity governance, user training. We provide hooks; they configure. |

### 7.4 Customer responsibilities in the per-tenant deployment model

This is worth surfacing in deployment docs (F3). In per-customer-tenant install, the customer owns:

- **Conditional Access policies** in their AAD tenant — MFA enforcement, device compliance, sign-in frequency, location restrictions, etc.
- **Service principal credentials** in their tenant — their rotation cadence, their HSM choice if any.
- **Audit log destination** — they pipe our structured logs to their Sentinel, Log Analytics, or third-party SIEM via Diagnostic Settings.
- **User identity governance** — provisioning, deprovisioning, access reviews. They do this in AAD; we honor whatever AAD says.
- **Network controls** — VNet integration, private endpoints, IP allow-lists at App Service. We document the recommended config; they implement.
- **Secret rotation cadence** — they own the Key Vault, they set the rotation policy.
- **Compliance certifications** — if they need SOC 2 / HIPAA / FedRAMP for their installation, they engage their auditors. We provide architecture evidence; they make the evidence-to-audit mapping.

Documenting these explicitly in F3 prevents customer support burden later ("why don't you do X?" → because that's in your tenant, not ours).

---

## 6. What about the in-progress `authenticatedFetch` work?

The SprkChat hooks change I implemented earlier (added optional `authenticatedFetch?` prop, hooks use it when provided, fall back to raw fetch) is **superseded** by Phase 1+2. Specifically:

- The "fall back to raw fetch with accessToken" path goes away in Phase 2 (SprkChat will require `authenticatedFetch` + `getAccessToken`; no fallback).
- The wiring in ConversationPane stays conceptually but moves to using `useAuth()` from `@spaarke/auth` instead of importing `authenticatedFetch` directly.

**Options**:
- (a) **Discard the uncommitted changes** and do Phase 1+2 cleanly from scratch.
- (b) **Commit the current changes as an interim fix** (it does resolve the user-visible 401), then do Phase 1+2 as a follow-up that supersedes it.
- (c) **Fold the current changes into Phase 1+2** — the type and prop changes are reusable.

If you want the 401 fixed for users TODAY while we do the proper architecture work, (b) is pragmatic. If you'd rather do it once and right, (c).

---

## 7. Risks & Open Questions

- **Q1**: Are all the affected consumers (PlaybookBuilder, DocumentRelationshipViewer, AnalysisWorkspace, Office Add-ins) "pre-production" in the same sense as SpaarkeAi? If any are in active use, their migration order matters.
- **Q2**: Are there external consumers of `@spaarke/auth` (e.g., other repos)? Removing the value-based `token` API is a breaking change.
- **Q3**: NAA integration for Office Add-ins is non-trivial. The current `authConfig.ts` works — is it worth the unification cost in this round, or defer to a separate project?
- **Q4**: Secret rotation requires coordination (App Service restart, downstream callers re-authenticate). Plan the maintenance window.

---

## 8. Pre-Flight: Conflict Sources to Address Before v2 Starts

A comprehensive repo sweep (not just AI-loaded surfaces) found **48 conflict sources** between current state and the v2 design. Most are **already inside the v2 scope** — they'll be changed as part of Workstreams A-F. A smaller set is genuinely **pre-flight**: content that will actively mislead Claude Code agents or human developers reading current guidance *during* the v2 build if not addressed first.

### 8.1 What's already inside v2 scope (no separate pre-flight needed)

These conflict sources are part of the Workstream they live in. Listed here so we know what's *not* missing:

| Conflict source | Workstream | What v2 does |
|---|---|---|
| `src/client/shared/Spaarke.Auth/src/strategies/BridgeStrategy.ts`, `XrmStrategy.ts`, `tokenBridge.ts` | A1 | Delete; cascade reduced to MSAL + cache |
| `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts` (6-strategy chain code) | A1 | Constructor MSAL config preserved by lift; cascade rewritten |
| `src/client/shared/Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` (exposes `token: string`) | B1 | Context value changed to `getAccessToken` + `authenticatedFetch` |
| `src/solutions/SpaarkeAi/src/App.tsx` (snapshot useEffect) | B1 | Replaced with `useAuth()` consumption |
| `src/client/code-pages/PlaybookBuilder/.../aiPlaybookService.ts:279`, `DocumentRelationshipViewer/.../VisualizationApiService.ts:40`, `AnalysisWorkspace/.../analysisApi.ts`, `dataverseClient.ts`, `templateStore.ts` | B2 | All raw `Bearer ${token}` rewritten to `authenticatedFetch` |
| `src/client/code-pages/SemanticSearch/src/services/auth/MsalAuthProvider.ts`, `authInit.ts` | B2 | Migrate to `@spaarke/auth` v2 |
| `src/client/external-spa/src/auth/bff-client.ts` | B2 | Same |
| `src/client/office-addins/shared/{auth/authConfig.ts, taskpane/services/SseClient.ts, taskpane/hooks/useSaveFlow.ts, api/OfficeApiClient.ts, auth/DialogAuthService.ts, auth/NaaAuthService.ts}` | B4 | Refactored as `OfficeNaaStrategy`; staleness bugs fixed |
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` (TenantId "common", plain ClientSecret refs, hardcoded Copilot UUID) | C1+C7 | Placeholders fixed; secrets to Key Vault refs |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/DebugEndpointExtensions.cs` (`/debug/*` endpoints) | C3 | Deleted |
| `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` (`/debug/{documentId}`) | C3 | Removed |
| `src/client/pcf/shared/utils/environmentVariables.ts` (duplicate `buildBffApiUrl`) | B5 | Deleted; imports from `@spaarke/auth` |
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/bffDataServiceAdapter.ts` (teaches old pattern in example docs) | B5 | Example rewritten to function-based |
| `src/client/shared/Spaarke.Auth/tests/tokenBridge.test.ts` | A1 | Deleted with strategy; new MSAL.localStorage + BroadcastChannel tests written |

**Conclusion**: ~30 of the 48 conflicts are addressed by the existing v2 plan. No additional workstream needed for these.

### 8.2 Pre-flight items (must address BEFORE Workstream A starts)

These are **AI-loaded guidance documents and high-visibility references** that — if left unchanged during the v2 build — will cause agents (or humans) to follow stale patterns. Header banners alone are insufficient; agents skim file content and can miss them. We use **three layers of defense**:

1. **Filename surgery** — fully-superseded files renamed `DEPRECATED-*.md`. Name appears in every Grep/Glob/file reference → impossible to miss.
2. **Aggressive in-file banner** — visually disruptive STOP block at top, with file-specific "what's still canonical" exception list.
3. **Project CLAUDE.md prohibition** — explicit `MUST NOT` rules loaded into every Claude Code session in this worktree.

This is belt-and-suspenders. Each layer alone is ~50% effective; together ~95-99%.

#### Layer 1 — Filename surgery (rename + update references)

| # | Action | File | What's still canonical |
|---|---|---|---|
| **PF-1** | Rename | `.claude/patterns/auth/msal-client.md` → `DEPRECATED-msal-client.md` | Nothing — file fully superseded by `@spaarke/auth` v2 |
| **PF-2** | Rename | `.claude/patterns/auth/spaarke-auth-initialization.md` → `DEPRECATED-spaarke-auth-initialization.md` | Nothing — bootstrap pattern fully superseded by `useAuth()` |
| **PF-3** | Update references | `.claude/patterns/auth/INDEX.md` + any other linkers | Point to new filenames; add "DEPRECATED" status column |

#### Layer 2 — Aggressive in-file STOP banners

Template (with file-specific "what's still canonical" line filled in):

```markdown
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

What IS still canonical in this file: [file-specific exception]
═══════════════════════════════════════════════════════════════════════════
---
```

| # | File | "What's still canonical" exception |
|---|---|---|
| **PF-4** | `.claude/patterns/auth/spaarke-sso-binding.md` | §"Required MSAL Configuration" (INV-1..INV-7) and §"Bundling Reality (CRITICAL)" — these remain canonical and are preserved by v2 literal lift. Cascade and strategy details are pre-v2. |
| **PF-5** | `.claude/constraints/auth.md` | OAuth/OBO server-side MUST rules (lines 31-37); MSAL client config invariants (lines 38-44); BFF URL helper rules (lines 92-126). Client-side cascade rules (lines 43, 70) are pre-v2. |
| **PF-6** | `.claude/patterns/auth/token-caching.md` | Server-side Redis OBO caching content remains canonical. Client-side cache cascade is pre-v2. |
| **PF-7** | `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md` | `buildBffApiUrl()` URL construction unchanged in v2. Token acquisition section is pre-v2. |
| **PF-8** | `docs/architecture/sdap-auth-patterns.md` | OBO flow taxonomy + server-side auth patterns remain canonical. Client-side cascade/snapshot patterns are pre-v2. |
| **PF-9** | `DEPRECATED-msal-client.md` (post-rename) | Nothing — file is fully deprecated. Banner says "this file will be deleted when v2 ships." |
| **PF-10** | `DEPRECATED-spaarke-auth-initialization.md` (post-rename) | Nothing — same as above. |

#### Layer 3 — Project CLAUDE.md prohibition (highest enforcement)

| # | File | Action |
|---|---|---|
| **PF-11** | `projects/spaarke-ai-platform-unification-r2/CLAUDE.md` | Add a top-level section: `## 🚨 ACTIVE AUTH V2 REFACTOR — DO NOT REGRESS`. Includes explicit MUST NOT list: no `accessToken: string` props, no raw Bearer fetch, no BridgeStrategy/XrmStrategy/window globals, no React-state token snapshots, do not follow `DEPRECATED-*` files, etc. Points to `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` as authoritative until ADR-027 lands. **This is the most-loaded file (auto-loaded every session in this worktree); the prohibition lives here.** |
| **PF-12** | Root `CLAUDE.md` (this repo) | Add one row to §15 "Pointers": `Active auth v2 design (pre-ADR-027) → AUDIT-FINDINGS-AUTH-SYSTEM.md`. Cross-worktree awareness. |
| **PF-13** | `.claude/CHANGELOG.md` | Log entry: "Auth v2 in-progress markers applied; `DEPRECATED-*` renames; project CLAUDE.md prohibition added. Retired when v2 ships (Workstream F)." |

#### Pre-flight execution

Single PR titled "auth-v2-pre-flight-markers" containing all 13 actions. Estimated **~90 minutes** of focused work. Lands BEFORE Workstream A1.

#### What the agent experiences after pre-flight

Three independent signals all say STOP:

1. **Session startup**: Loads project CLAUDE.md → reads `🚨 ACTIVE AUTH V2 REFACTOR` section → explicit MUST NOT list in working memory before any code work begins.
2. **Search results**: Greps for `MSAL strategy` or `auth pattern` → results include `DEPRECATED-msal-client.md`, `DEPRECATED-spaarke-auth-initialization.md` → filename itself is a stop signal.
3. **File read**: Opens any pre-v2 auth doc → immediately hits the `🛑 STOP` banner → file-specific exception list tells agent whether to stop entirely or read past banner cautiously.

When the agent's task triggers all three, the probability of following old patterns drops from ~50% (header alone) to near zero.

### 8.3 Items that stay as-is (historical / contextual)

These are NOT updated as part of v2. Either they're frozen historical project context, or they describe operational state that will be naturally updated post-v2.

| File / location | Reason for no action |
|---|---|
| `projects/**/CLAUDE.md`, `projects/**/spec.md`, `projects/**/plan.md`, `projects/**/notes/*.md` | Historical project context. Specs describe what was built at that point in time. Changing them rewrites history. Active projects (like this one — `spaarke-ai-platform-unification-r2`) will get an update entry in `current-task.md` when v2 lands, but the rest of their docs stay frozen. |
| `docs/architecture/auth-azure-resources.md` | Inventory of current Azure AD app registrations + secrets. Document gets updated as resources change (post-v2 deploy), but no edit needed now. |
| `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md`, `docs/guides/office-addins-admin-guide.md`, `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md`, `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md`, `docs/guides/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md` | Operational guides describing current setup. They're not auto-loaded by Claude Code (no slash command consumes them); they're human references for ops teams. Update naturally after v2 deploy as part of Workstream F3 (new-environment setup checklist). |
| `docs/procedures/production-release.md` | Release process doc. References current secret handling. Update after Workstream C1 (secret rotation) lands. |
| `.claude/skills/bff-deploy/SKILL.md` | BFF deployment skill. References current config structure. Update as part of Workstream C7 when template placeholders change. Not blocking. |
| `.claude/AUDIT-FINDINGS-CLAUDEMD.md`, `.claude/AUDIT-FINDINGS-SKILLS.md` | Audit findings from prior projects, not auth-related. No conflict. |
| Test fixtures with `accessToken: 'mock-token'` (Office Add-ins tests, `Spaarke.Auth/tests/`) | Test mocks. Get updated as part of consumer migration in Workstream B; until then, current behavior matches current code. Not blocking. |
| All `dist/`, `dist_temp/`, `node_modules/`, `bin/`, `obj/` outputs | Build artifacts; rebuilt automatically. |

### 8.4 What v2's Workstream F delivers (the canonical post-v2 docs)

Reminder of what Workstream F (Engineering canonical docs) produces, so we don't double-write:

- **`ADR-027`** — single canonical architecture doc for v2. Captures function-based contract, pluggable strategies, MSAL invariants (INV-1..INV-8), per-tenant deployment model, deferred items.
- **Update to `.claude/patterns/auth/spaarke-sso-binding.md`** — partial rewrite: MSAL invariants stay; cascade section retired and replaced with pointer to ADR-027.
- **Update to `.claude/constraints/auth.md`** — MUST rule additions/replacements: "no `token: string` in public APIs"; cascade rules removed.
- **New `docs/guides/auth-deployment-setup.md`** — checklist for setting up auth in a new customer tenant: 4 Dataverse env vars + 8 App Service settings + Key Vault secret names + verification steps.
- **Update or retirement of `.claude/patterns/auth/{msal-client.md, token-caching.md, spaarke-auth-initialization.md}`** — three patterns are partly/fully superseded; will be either rewritten or marked retired with redirect to ADR-027.

### 8.5 Pre-flight execution order

1. **Apply pre-flight banners (PF-1 through PF-10)** as a single small PR ("auth v2 in-progress markers"). ~70 min total.
2. Start Workstream A (library rebuild). The first deliverable in Workstream F is `ADR-027`, which removes the "in progress" status of the banners.
3. As Workstream F lands (F1 → F2 → F3), retire each pre-flight banner naturally — replace with the final state pointing at `ADR-027`.

This avoids a flag-day style "everything changes at once" with broken intermediate states. Banners signal "transition in progress" → v2 ships → banners replaced with permanent canonical references.

---

## 9. Recommendation

**Approve Spaarke Auth v2 + Reasonable Hardening (Workstreams A-F in §5).** ~68 hours total, ~5-6 working days with parallelization.

Workstream rationale:
- **A + B** rebuild the architecture: function-based contract, pluggable strategies, eliminate snapshot bugs structurally. Preserves all MSAL invariants (INV-1..INV-7) by literal code lift of `SpaarkeAuthProvider` constructor. Single migration of all consumers in one batch (minimizes INV-8 risk).
- **C** hardens the server: managed identity, named auth schemes, HMAC webhooks, audit middleware, rate limits, debug endpoint removal, secret rotation to Key Vault.
- **D** adds reasonable security defense-in-depth: CSP/Trusted Types, CAE, claims hardening, step-up scaffolding, refresh-token-rotation verification. Not Fort Knox — matched to per-tenant deployment threat model.
- **E** prevents regression: secret scanning, automated `spaarke-sso-binding` ritual, Dependabot. Catches INV violations on the first PR.
- **F** captures the architecture in `ADR-027` + updated patterns + new-environment setup checklist.

Discarded from the original Phase 1-7 plan:
- BroadcastChannel as a token transport (kept only for invalidation events; MSAL.localStorage already shares tokens cross-tab/iframe).
- TypeScript branded types as the enforcement mechanism (runtime boundary via `authenticatedFetch` is the real enforcement; ESLint rule reinforces).

Explicitly deferred to a future v3 evaluation (none of these is needed for SOC 2 or typical enterprise review in the per-tenant model):
- DPoP / sender-constrained tokens
- Multi-SP privilege separation within an install
- Cryptographic audit log chaining (Azure Monitor handles immutability)
- Cross-customer / federation features

### Phasing

**Recommended: Option 1 — single coherent push.** All workstreams shipped together. Minimizes INV-8 risk by rebuilding all ~30 consumers in lockstep.

If review checkpoints are preferred, **Option 2 — three cycles**: (a) A + B1 (library + SpaarkeAi); (b) C + B2-B5 (server hardening + remaining consumers); (c) D + E + F (security hardening + CI + docs). Each is independently shippable.

### What to do with the uncommitted SprkChat `authenticatedFetch` patch

**Discard.** It was an interim fix targeting a single staleness path. Workstream A+B replaces it with the right contract at the right level (no fallback to raw fetch, the `accessToken` prop is removed entirely). Re-doing the SprkChat hooks as part of B1 takes maybe 30 minutes; carrying the interim code forward adds confusion.

### Non-code work (operational, not in this scope)

The v2 + Hardening code delivers the architectural evidence for SOC 2 / enterprise review. The operational deliverables (threat model document, pen test engagement, written security policies, SOC 2 audit) are enumerated in §7 and should be scheduled separately as runway-to-GA work. They are not blockers for v2 implementation.

### Asks for sign-off

1. **Scope**: approve Workstreams A-F as defined.
2. **Phasing**: Option 1 (single push) or Option 2 (three cycles)?
3. **Secret rotation timing**: as part of Workstream C1 in this push (requires brief App Service restart in dev), or scheduled separately?

On sign-off, I'll draft `ADR-027` first (so the architecture is locked in writing before code), then proceed with Workstream A.
