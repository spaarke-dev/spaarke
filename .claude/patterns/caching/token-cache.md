# Token Cache Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Caching OBO (On-Behalf-Of) Graph tokens to avoid repeated token exchanges.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` — Server-side OBO token cache
2. `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` — Client-side MSAL token cache

## Constraints
- **ADR-009**: Redis-first for token storage
- MUST hash user tokens with SHA256 before using as cache keys — never store plaintext
- MUST log only first 8 characters of hash prefix for debugging
- MUST fail gracefully on cache errors — cache miss should not break OBO flow

## Key Rules
- Cache key: `sdap:graph:token:{sha256hash}`
- TTL: 55 minutes (5-minute buffer before token expiry)
- Flow: hash user token → check cache → if miss, do OBO exchange → cache result
- Performance targets: 95%+ hit rate, ~5ms hit latency, ~200ms miss latency
