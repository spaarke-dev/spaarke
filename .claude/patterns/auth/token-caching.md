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
- Cache key: `sdap:graph:token:{sha256hash}`
- Fail gracefully: cache errors should not break the OBO flow

## Client-side (MSAL)
- MSAL cache: `localStorage` (NOT `sessionStorage` — must survive tab close so neighbor tabs don't re-prompt)
- Cookie state enabled (`storeAuthStateInCookie: true`) for `ssoSilent` under 3rd-party cookie blocking
- Cross-iframe sharing via the same-origin key `__spaarke_bff_token_cache__` (used by `SessionStorageStrategy` — strategy #2 in the chain)
- Parent-frame bridge via `window.__SPAARKE_BFF_TOKEN__` (strategy #3)
- See `.claude/patterns/auth/spaarke-sso-binding.md` for the full 6-strategy chain and the binding requirements behind these settings.
