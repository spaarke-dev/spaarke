# Token Cache Pattern

> **Last Reviewed**: 2026-05-20 (auth v2 drift audit)
> **Reviewed By**: doc-drift-audit
> **Status**: Verified (v2)

## When
Caching OBO (On-Behalf-Of) Graph tokens to avoid repeated token exchanges.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` — Server-side OBO token cache (canonical for both server-side OBO and v2 architecture)
2. `src/client/shared/Spaarke.Auth/src/index.ts` + `BrowserMsalStrategy.ts` — v2 client-side cache (`InMemoryCache` wrapper around pluggable `AuthStrategy`; MSAL `localStorage` for cross-tab/iframe sharing). Per ADR-028, consumers MUST use `@spaarke/auth` via `initAuth()` + `useAuth()` / `authenticatedFetch` — do NOT instantiate `PublicClientApplication` directly.
3. `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` — **Pre-v2 holdout**: this is the ONLY remaining PCF still using a local `MsalAuthProvider` (all other PCFs + Code Pages migrated in Phase B). Scheduled for `auth-v3-hardening` Phase H cleanup. Do NOT pattern new PCFs on this file; use `@spaarke/auth` (Read These Files #2).

## Constraints
- **ADR-009**: Redis-first for token storage (server side)
- **ADR-028**: Client-side token cache is `InMemoryCache` (per-tab, JWT exp validated with 5-min buffer); MSAL `localStorage` handles cross-tab/iframe sharing via browser SOP. No `SessionStorageStrategy`, no `BridgeStrategy`, no `window.__SPAARKE_BFF_TOKEN__`.
- MUST hash user tokens with SHA256 before using as cache keys — never store plaintext
- MUST log only first 8 characters of hash prefix for debugging
- MUST fail gracefully on cache errors — cache miss should not break OBO flow

## Key Rules
- Cache key: `spaarke:graph-token:{sha256hash}` (system-level exception per ADR-009 amendment + `SystemCacheKeys.GraphToken` — keyed by SHA256(user-token), not tenant-scoped)
- TTL: 55 minutes (5-minute buffer before token expiry)
- Flow: hash user token → check cache → if miss, do OBO exchange → cache result
- Performance targets: 95%+ hit rate, ~5ms hit latency, ~200ms miss latency
