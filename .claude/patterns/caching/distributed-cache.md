# Distributed Cache Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Adding Redis-backed caching for cross-request data (tokens, metadata, idempotency).

## Read These Files
1. `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` — GetOrCreate helpers with versioned keys
2. `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` — Redis config (Enabled flag, connection string)
3. `src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs` — Hit/miss/latency metrics

## Constraints
- **ADR-009**: Redis-first — no hybrid L1 cache unless profiling proves need
- MUST set `AbortOnConnectFail = false` (graceful degradation)
- Dev uses in-memory fallback (`AddDistributedMemoryCache`)

## Key Rules
- Key format: `sdap:{category}:{identifier}[:v:{version}]` — version suffix for invalidation
- TTLs: security data 5min, metadata 15min, tokens 55min, idempotency 24h
- Use `GetOrCreateAsync<T>` extension (factory pattern) — not raw Get/Set
- Track hit rates via CacheMetrics — target 95%+
