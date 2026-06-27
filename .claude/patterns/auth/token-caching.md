---
> **Auth v2 / ADR-028 status (2026-05-19)**
> - Server-side (§"Server-side (Redis)") — **canonical** in v2 (OBO cache pattern unchanged).
> - Client-side (§"Client-side (MSAL — v2)") — **rewritten for v2**: MSAL `localStorage` cache + JWT exp validation + `BroadcastChannel` for invalidation events only. The retired 6-strategy cascade (`BridgeStrategy`, `XrmStrategy`, `MsalSilentStrategy`, `__spaarke_bff_token_cache__`, `window.__SPAARKE_BFF_TOKEN__`) is gone.
> - Canonical client contract: [`ADR-028`](../../adr/ADR-028-spaarke-auth-architecture.md) — `useAuth()` + `authenticatedFetch` from `@spaarke/auth`. Never raw `fetch(... Authorization: Bearer ...)` or `accessToken: string` props.
═══════════════════════════════════════════════════════════════════════════
---

# Token Caching Pattern

> **Last Updated**: 2026-05-13
> **Status**: Verified

## When
Implementing or debugging token caching for OBO flows (server-side Redis or client-side MSAL).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` — Server-side Redis cache with SHA256 keys
2. `src/client/shared/Spaarke.Auth/src/SpaarkeAuthProvider.ts` — Client-side 6-strategy chain (see `.claude/patterns/auth/spaarke-sso-binding.md`)

## Server-side (Redis)
- **ADR-009**: Redis-first for server-side token storage
- MUST hash tokens with SHA256 before using as cache keys — never store plaintext
- MUST log only first 8 chars of hash for debugging
- Server TTL: 55 minutes (5-min buffer before token expiry)
- Cache key: `spaarke:graph-token:{sha256hash}` (system-level exception per ADR-009 amendment + `SystemCacheKeys.GraphToken` — keyed by SHA256(user-token), not tenant-scoped)
- Fail gracefully: cache errors should not break the OBO flow

## Client-side (MSAL — v2, per ADR-028)
- MSAL cache: `localStorage` (NOT `sessionStorage` — must survive tab close so neighbor tabs don't re-prompt) — **INV-1**
- Cookie state enabled (`storeAuthStateInCookie: true`) for `ssoSilent` under 3rd-party cookie blocking — **INV-2**
- Cross-iframe sharing: **MSAL's built-in `localStorage` cache** is the sharing mechanism (same-origin browser security boundary). The pre-v2 `SessionStorageStrategy` (`__spaarke_bff_token_cache__`) and `BridgeStrategy` (`window.__SPAARKE_BFF_TOKEN__`) were retired in Phase A — MSAL.localStorage covers their use cases more cleanly.
- BroadcastChannel (`spaarke-auth-events`) is used for **invalidation events only** (logout/revocation broadcasts) — never for token transport.
- In-memory cache wrapper validates JWT `exp` with 5-min buffer before returning a cached token; otherwise re-acquires via MSAL.
- See [`.claude/patterns/auth/spaarke-sso-binding.md`](spaarke-sso-binding.md) for the canonical MSAL binding invariants (INV-1..INV-8) and [`ADR-028`](../../adr/ADR-028-spaarke-auth-architecture.md) for the full v2 architecture.
