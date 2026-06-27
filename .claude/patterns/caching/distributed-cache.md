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
- **ADR-009** (amended 2026-06-26): Redis-first — no hybrid L1 cache unless profiling proves need; operational MUSTs for SKU/KV-ref/fail-fast/tenant-prefix/InstanceName/Pub-Sub/App-Insights/naming.
- MUST set `AbortOnConnectFail = true` in deployed environments (per FR-01). Dev fallback gated by `Redis:AllowInMemoryFallback=true` + `IHostEnvironment.IsDevelopment()` (per FR-03).
- Dev in-memory fallback registers `NullConnectionMultiplexer` for symmetric DI per ADR-032.

## Key Rules
- **BFF callers MUST use `ITenantCache` wrapper** (`Sprk.Bff.Api.Infrastructure.Cache.ITenantCache`); direct `IDistributedCache.GetAsync/SetAsync/RemoveAsync` calls are prohibited except for `SystemCacheKeys`-enumerated exceptions.
- Key format (BFF): `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}` — `InstanceName="spaarke:"` is prepended by StackExchangeRedisCache; the wrapper produces `tenant:{tenantId}:{resource}:{id}:v{version}`.
- Key format (shared lib non-BFF callers): `spaarke:{category}:{identifier}[:v{version}]` via `DistributedCacheExtensions.CreateKey()` — version suffix for invalidation.
- TTLs: security data 5min, metadata 15min, tokens 55min, idempotency 24h.
- Use `ITenantCache.GetOrCreateAsync<T>` (BFF) or `DistributedCacheExtensions.GetOrCreateAsync<T>` (shared lib) — factory pattern; not raw Get/Set.
- Track hit rates via metrics emitted from `TenantCache` (`cache.hits`, `cache.misses`, `cache.redis_call_duration_ms` with `resource` dimension) — target 95%+ for hot keys.
