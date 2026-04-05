# Caching Architecture

> **Last Updated**: April 2026
> **Purpose**: Describes the Redis-first caching strategy across the BFF API, covering all cache types, TTL tiers, key conventions, invalidation patterns, and observability.

---

## Overview

The Spaarke BFF API follows ADR-009 (Redis-First Caching) as its primary caching strategy. All distributed cache operations go through `IDistributedCache`, backed by Redis in production and an in-memory provider in local development. The caching layer spans five distinct cache types: distributed (Redis), request-scoped, Graph token, embedding, and Graph metadata. Each type targets a specific latency/freshness tradeoff, with TTLs ranging from 60 seconds (security-sensitive authorization data) to 7 days (deterministic AI embeddings).

Cache failures are always graceful: every cache consumer treats errors as misses and falls through to the authoritative data source. This "fail-open" pattern ensures caching is an optimization, never a requirement for correctness.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| CacheModule | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` | DI registration: Redis (production) or in-memory (dev); configures IConnectionMultiplexer for pub/sub |
| RedisOptions | `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | Configuration: Enabled flag, ConnectionString, InstanceName prefix |
| DistributedCacheExtensions | `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` | GetOrCreateAsync with versioned keys, standard key builder, TTL constants |
| RequestCache | `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` | Scoped per-request in-memory cache to collapse duplicate loads within a single HTTP request |
| GraphTokenCache | `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Caches OBO Graph tokens by SHA256 hash of user token; 55-min TTL |
| EmbeddingCache | `src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs` | Caches AI embedding vectors by SHA256 content hash; 7-day TTL; binary serialization |
| GraphMetadataCache | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphMetadataCache.cs` | Caches Graph API responses (file metadata, folder listings, container-to-drive mappings) |
| CachedAccessDataSource | `src/server/api/Sprk.Bff.Api/Infrastructure/Caching/CachedAccessDataSource.cs` | Decorator over IAccessDataSource; caches authorization DATA (not decisions) in Redis |
| AnalysisCacheEntry | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisCacheEntry.cs` | DTO for caching analysis session state in Redis |
| IdempotencyService | `src/server/api/Sprk.Bff.Api/Services/Jobs/IdempotencyService.cs` | Distributed idempotency checks for event processing (24h TTL) |
| CacheMetrics | `src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs` | OpenTelemetry metrics: cache.hits, cache.misses, cache.latency by cache.type |

## Cache Types

### 1. Distributed Cache (Redis / IDistributedCache)

The primary cache layer. All services inject `IDistributedCache` and use `DistributedCacheExtensions.GetOrCreateAsync` for the standard get-or-populate pattern. Versioned keys (`{key}:v:{version}`) support cache invalidation without explicit deletes.

### 2. Request Cache (Scoped)

`RequestCache` is registered as Scoped (one instance per HTTP request). It collapses duplicate data loads within a single request pipeline -- for example, when multiple endpoint filters and services need the same authorization data. No TTL; entries live only for the request lifetime.

### 3. Graph Token Cache

`GraphTokenCache` caches OBO-exchanged Graph API access tokens to reduce Azure AD token exchange calls. User tokens are SHA256-hashed for cache keys (never stored plaintext). Target: 95% cache hit rate, 97% reduction in auth latency.

### 4. Embedding Cache

`EmbeddingCache` caches AI embedding vectors (float arrays) by SHA256 hash of the source content text. Uses binary serialization (Buffer.BlockCopy to byte array) rather than JSON for efficiency with large vectors. Target: >80% hit rate for document-heavy workloads.

### 5. Graph Metadata Cache

`GraphMetadataCache` caches Graph API metadata responses (file metadata, folder children, container-to-drive mappings) to reduce Graph API round-trips from 100-300ms to ~5ms on hit. Includes explicit invalidation methods for write operations.

## TTL Tiers

| TTL | Cache Type | Key Pattern | Rationale |
|-----|-----------|-------------|-----------|
| 60s | Authorization resource access | `sdap:auth:access:{userId}:{resourceId}` | Most security-sensitive; short TTL reduces stale permission risk |
| 2 min | Authorization roles/teams | `sdap:auth:roles:{userId}`, `sdap:auth:teams:{userId}` | Security-sensitive but user-level (reusable across resources) |
| 2 min | Folder listings | `sdap:graph:children:{driveId}:{itemId}` | Folder contents change frequently with uploads/deletes |
| 5 min | File metadata | `sdap:graph:metadata:{driveId}:{itemId}` | Document metadata with ETag-versioned keys |
| 5 min | Security data (standard) | Via `DistributedCacheExtensions.SecurityDataTtl` | UAC snapshots and similar authorization data |
| 5 min | Idempotency locks | `sdap:idem:lock:{eventId}` | Processing lock duration for concurrent event handling |
| 15 min | General metadata | Via `DistributedCacheExtensions.MetadataTtl` | Document metadata and less sensitive data |
| 55 min | Graph OBO tokens | `sdap:graph:token:{tokenHash}` | 5-minute buffer before token expiration (tokens last 60 min) |
| 2 hours | Analysis sessions | `sdap:ai:analysis:{analysisId}` | Analysis state; rebuild from Dataverse on miss |
| 24 hours | Container-to-drive mappings | `sdap:graph:drive:{containerId}` | Stable mappings that rarely change |
| 24 hours | Idempotency markers | `sdap:idem:{eventId}` | Prevent reprocessing for the deduplication window |
| 7 days | AI embeddings | `sdap:embedding:{contentHash}` | Deterministic for same model version; high cost savings |

## Key Conventions

All cache keys follow the pattern `sdap:{domain}:{type}:{identifier}[:v:{version}]`:

- Prefix `sdap:` is the instance name (configurable via `RedisOptions.InstanceName`)
- Domain segments: `auth`, `graph`, `embedding`, `ai`, `idem`
- Version suffix `:v:{version}` used for ETag-versioned metadata and content-versioned entries
- `DistributedCacheExtensions.CreateKey(category, identifier, parts...)` produces keys in this format

## Invalidation Patterns

| Pattern | Used By | Trigger |
|---------|---------|---------|
| TTL expiration | All caches | Automatic; each entry has `AbsoluteExpirationRelativeToNow` |
| Explicit delete | GraphMetadataCache | After file upload, delete, rename, or metadata update |
| Version-based key rotation | DistributedCacheExtensions, GraphMetadataCache | New ETag/version creates new key; old key expires naturally |
| Fire-and-forget cache write | CachedAccessDataSource | Authorization snapshot cached asynchronously after Dataverse fetch |
| Token removal | GraphTokenCache | On logout or token invalidation via `RemoveTokenAsync` |

## Data Flow

1. Request arrives at BFF API endpoint
2. `RequestCache` (scoped) collapses duplicate lookups within the same request
3. Service-specific cache (`GraphTokenCache`, `EmbeddingCache`, `GraphMetadataCache`, `CachedAccessDataSource`) checks Redis via `IDistributedCache`
4. On cache hit: return cached value immediately (~5-10ms)
5. On cache miss: call authoritative source (Graph API, Azure AD, Azure OpenAI, Dataverse), cache result with appropriate TTL, return value
6. On cache error: log warning, treat as miss, proceed to authoritative source (fail-open)
7. `CacheMetrics` records hit/miss/latency per cache type for OpenTelemetry dashboards

## Observability

`CacheMetrics` (meter name: `Sprk.Bff.Api.Cache`) exposes three OpenTelemetry instruments:

- `cache.hits` (Counter): total hits, dimensioned by `cache.type`
- `cache.misses` (Counter): total misses, dimensioned by `cache.type`
- `cache.latency` (Histogram): operation latency in ms, dimensioned by `cache.result` and `cache.type`

Cache types reported: `graph`, `embedding`, `auth-access`, `graph-metadata`, `graph-folder-listing`, `graph-container-drive`.

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Redis (Azure Cache for Redis) | StackExchange.Redis via IDistributedCache | Production; falls back to in-memory in dev |
| Consumed by | GraphClientFactory | GraphTokenCache | OBO token caching |
| Consumed by | RagService, SemanticSearchService | EmbeddingCache | AI embedding caching |
| Consumed by | SpeFileStore, DriveItemOperations | GraphMetadataCache | Graph API response caching |
| Consumed by | AuthorizationService | CachedAccessDataSource | Authorization data caching |
| Consumed by | AnalysisOrchestrationService | AnalysisCacheEntry via IDistributedCache | Analysis session state |
| Consumed by | ServiceBusJobProcessor | IdempotencyService | Event deduplication |
| Consumed by | OpenTelemetry pipeline | CacheMetrics | Observability |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Redis-first | No hybrid L1/L2 cache | Simplicity; single source of truth for cache state until profiling proves need | ADR-009 |
| Fail-open on errors | All cache consumers catch exceptions and fall through | Cache is optimization, not correctness requirement; availability trumps performance | ADR-009 |
| SHA256 hashing for keys | Token and embedding caches hash input content | Consistent key length, prevents sensitive data in cache keys/logs | — |
| Binary serialization for embeddings | Buffer.BlockCopy float[] to byte[] | More efficient than JSON for large float arrays (1536-dimension vectors) | — |
| Decorator pattern for authorization | CachedAccessDataSource wraps IAccessDataSource | Cache data, not decisions; authorization logic always runs fresh per-request | ADR-003 |
| In-memory fallback for dev | `AddDistributedMemoryCache()` when Redis disabled | Local development works without Redis infrastructure | ADR-009 |

## Constraints

- **MUST**: Use `IDistributedCache` for all distributed caching (ADR-009)
- **MUST**: Handle cache failures gracefully; never let cache errors propagate to the caller
- **MUST**: Use `sdap:` prefix for all cache keys
- **MUST**: Keep authorization cache TTLs at 2 minutes or less (security-sensitive data)
- **MUST NOT**: Cache authorization decisions; only cache authorization data (ADR-003)
- **MUST NOT**: Store plaintext tokens in cache keys or logs; always hash with SHA256
- **MUST NOT**: Use hybrid L1 (in-memory) + L2 (Redis) caching unless profiling proves need (ADR-009)

## Known Pitfalls

- **Redis connection resilience**: CacheModule configures `AbortOnConnectFail=false`, 5s timeouts, and exponential retry. If Redis is down, the entire cache layer degrades gracefully but latency increases significantly
- **IConnectionMultiplexer singleton**: A single `ConnectionMultiplexer` is registered for both `IDistributedCache` and pub/sub (used by `JobStatusService`). Connection issues affect both caching and real-time job status
- **Embedding cache size**: 1536-float vectors at 4 bytes each = ~6KB per cached embedding. High-volume workloads can accumulate significant Redis memory; the 7-day TTL provides natural eviction
- **Authorization cache staleness**: 2-minute TTL means permission changes (role assignment, team membership) can take up to 2 minutes to take effect. This is an acceptable tradeoff documented in ADR-003

## Related

- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching
- [ADR-003](../../.claude/adr/ADR-003-authorization-seams.md) — Cache data, not decisions
- [auth-performance-monitoring.md](auth-performance-monitoring.md) — Auth performance metrics including cache hit rates
