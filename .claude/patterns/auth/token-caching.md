# Token Caching Pattern

## When
Implementing or debugging token caching for OBO flows (server-side Redis or client-side session).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` — Server-side Redis cache with SHA256 keys
2. `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` — Client-side sessionStorage cache

## Constraints
- **ADR-009**: Redis-first for server-side token storage
- MUST hash tokens with SHA256 before using as cache keys — never store plaintext
- MUST log only first 8 chars of hash for debugging

## Key Rules
- Server TTL: 55 minutes (5-min buffer before token expiry)
- Cache key: `sdap:graph:token:{sha256hash}`
- Fail gracefully: cache errors should not break the OBO flow
- See caching/token-cache.md for full implementation details
