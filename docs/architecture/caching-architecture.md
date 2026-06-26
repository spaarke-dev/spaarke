# Caching Architecture

> **Last Updated**: 2026-06-25
> **Last Reviewed**: 2026-06-25
> **Reviewed By**: spaarke-redis-cache-remediation-r1 (Phase 5 / FR-18)
> **Status**: Active (Phase 1 remediation complete; reflects tenant isolation, fail-fast, Null-Object DI)
> **Purpose**: Describes the Redis-first caching strategy across the BFF API, covering all cache types, TTL tiers, key conventions, tenant isolation, multi-instance behavior, failure modes, invalidation patterns, and observability.

---

## Overview

The Spaarke BFF API follows ADR-009 (Redis-First Caching) as its primary caching strategy. All distributed cache operations go through the `ITenantCache` wrapper (which internally uses `IDistributedCache`), backed by Redis in production AND in deployed dev/staging environments, and an in-memory provider in **local development only**. The caching layer spans five distinct cache types: distributed (Redis), request-scoped, Graph token, embedding, and Graph metadata. Each type targets a specific latency/freshness tradeoff, with TTLs ranging from 60 seconds (security-sensitive authorization data) to 7 days (deterministic AI embeddings).

**Failure semantics differ by environment**:

- **Deployed environments (dev/staging/prod)**: Redis is REQUIRED. Connection failures at startup cause fail-fast (`AbortOnConnectFail=true`). In-memory fallback is forbidden except behind explicit env-guarded escape hatch (see Failure Mode Catalog).
- **Local development (machine-local only)**: In-memory `IDistributedCache` permitted via Null-Object `IConnectionMultiplexer` registration (ADR-032). Single-instance only (no Pub/Sub).
- **Per-call cache errors at runtime (Redis up, transient miss/fault)**: Treated as cache misses, fall through to authoritative source. Cache remains an optimization, never a correctness requirement.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| CacheModule | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` | DI registration: **fail-fast** Redis connection with `AbortOnConnectFail=true` in deployed envs; env-guarded in-memory `AllowFallback` for local dev only; **symmetric Null-Object `IConnectionMultiplexer`** registration when Redis disabled (ADR-032); throws at startup if `Redis:Enabled=false` in non-Development environment unless `AllowFallback=true` |
| ITenantCache | `src/server/shared/Spaarke.Core/Cache/ITenantCache.cs` | **Mandatory wrapper** over `IDistributedCache`; injects `tenant:{tenantId}:` prefix; central seam for metrics, key validation, and future multi-Redis routing (NFR-12). **All Sprk.Bff.Api cache call sites MUST use this wrapper** (FR-06 atomic migration) |
| RedisOptions | `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | Configuration: `Enabled`, `ConnectionString` (Key Vault reference MANDATORY in deployed envs per ADR-028), `InstanceName=spaarke:`, `AllowFallback` (env-guarded; `true` only valid in Development) |
| DistributedCacheExtensions | `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` | GetOrCreateAsync with versioned keys, standard key builder, TTL constants. **Now invoked via ITenantCache, not directly** |
| RequestCache | `src/server/shared/Spaarke.Core/Cache/RequestCache.cs` | Scoped per-request in-memory cache to collapse duplicate loads within a single HTTP request |
| GraphTokenCache | `src/server/api/Sprk.Bff.Api/Services/GraphTokenCache.cs` | Caches OBO Graph tokens by SHA256 hash of user token; 55-min TTL; tenant-scoped via `ITenantCache` |
| EmbeddingCache | `src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs` | Caches AI embedding vectors by SHA256 content hash; 7-day TTL; binary serialization; tenant-scoped |
| GraphMetadataCache | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphMetadataCache.cs` | Caches Graph API responses (file metadata, folder listings, container-to-drive mappings); tenant-scoped |
| CachedAccessDataSource | `src/server/api/Sprk.Bff.Api/Infrastructure/Caching/CachedAccessDataSource.cs` | Decorator over IAccessDataSource; caches authorization DATA (not decisions) in Redis; tenant-scoped |
| AnalysisCacheEntry | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisCacheEntry.cs` | DTO for caching analysis session state in Redis; tenant-scoped |
| IdempotencyService | `src/server/api/Sprk.Bff.Api/Services/Jobs/IdempotencyService.cs` | Distributed idempotency checks for event processing (24h TTL). **System-level exception** to tenant prefix (see Tenant Isolation §System-Level Exception Allow-List) |
| CacheMetrics | `src/server/api/Sprk.Bff.Api/Telemetry/CacheMetrics.cs` | OpenTelemetry metrics: cache.hits, cache.misses, cache.latency by cache.type; **+ tenant_id dimension** (low cardinality enforced) |

## Cache Types

### 1. Distributed Cache (Redis / IDistributedCache via ITenantCache)

The primary cache layer. All services inject **`ITenantCache`** (not `IDistributedCache` directly) and use the wrapper's `GetOrSetAsync` for the standard get-or-populate pattern. The wrapper injects the mandatory `tenant:{tenantId}:` prefix into every key. Versioned keys (`{key}:v{version}`) support cache invalidation without explicit deletes.

### 2. Request Cache (Scoped)

`RequestCache` is registered as Scoped (one instance per HTTP request). It collapses duplicate data loads within a single request pipeline -- for example, when multiple endpoint filters and services need the same authorization data. No TTL; entries live only for the request lifetime. Not tenant-scoped because the request itself carries a single tenant context.

### 3. Graph Token Cache

`GraphTokenCache` caches OBO-exchanged Graph API access tokens to reduce Azure AD token exchange calls. User tokens are SHA256-hashed for cache keys (never stored plaintext). Target: 95% cache hit rate, 97% reduction in auth latency.

### 4. Embedding Cache

`EmbeddingCache` caches AI embedding vectors (float arrays) by SHA256 hash of the source content text. Uses binary serialization (Buffer.BlockCopy to byte array) rather than JSON for efficiency with large vectors. Target: >80% hit rate for document-heavy workloads.

### 5. Graph Metadata Cache

`GraphMetadataCache` caches Graph API metadata responses (file metadata, folder children, container-to-drive mappings) to reduce Graph API round-trips from 100-300ms to ~5ms on hit. Includes explicit invalidation methods for write operations.

## TTL Tiers

| TTL | Cache Type | Key Pattern | Rationale |
|-----|-----------|-------------|-----------|
| 60s | Authorization resource access | `spaarke:tenant:{tenantId}:auth:access:{userId}:{resourceId}` | Most security-sensitive; short TTL reduces stale permission risk |
| 2 min | Authorization roles/teams | `spaarke:tenant:{tenantId}:auth:roles:{userId}`, `spaarke:tenant:{tenantId}:auth:teams:{userId}` | Security-sensitive but user-level (reusable across resources) |
| 2 min | Folder listings | `spaarke:tenant:{tenantId}:graph:children:{driveId}:{itemId}` | Folder contents change frequently with uploads/deletes |
| 5 min | File metadata | `spaarke:tenant:{tenantId}:graph:metadata:{driveId}:{itemId}:v{etag}` | Document metadata with ETag-versioned keys |
| 5 min | Security data (standard) | Via `DistributedCacheExtensions.SecurityDataTtl` | UAC snapshots and similar authorization data |
| 5 min | Idempotency locks | `spaarke:system:idem:lock:{eventId}` | **System-level** (cross-tenant event dedup); see Tenant Isolation exception list |
| 15 min | General metadata | Via `DistributedCacheExtensions.MetadataTtl` | Document metadata and less sensitive data |
| 55 min | Graph OBO tokens | `spaarke:tenant:{tenantId}:graph:token:{tokenHash}` | 5-minute buffer before token expiration (tokens last 60 min) |
| 2 hours | Analysis sessions | `spaarke:tenant:{tenantId}:ai:analysis:{analysisId}` | Analysis state; rebuild from Dataverse on miss |
| 24 hours | Container-to-drive mappings | `spaarke:tenant:{tenantId}:graph:drive:{containerId}` | Stable mappings that rarely change |
| 24 hours | Idempotency markers | `spaarke:system:idem:{eventId}` | **System-level** dedup window; not tenant-scoped |
| 7 days | AI embeddings | `spaarke:tenant:{tenantId}:embedding:{contentHash}` | Deterministic for same model version; high cost savings |

## Key Conventions

All cache keys follow the canonical Spaarke pattern:

```
{InstanceName}tenant:{tenantId}:{domain}:{type}:{identifier}[:v{version}]
```

Where `{InstanceName}` is the StackExchange.Redis instance prefix (configured to `spaarke:` per FR-07 / Success Criterion #10).

- **Prefix**: `spaarke:` (canonical, replaces deprecated `sdap:` brand). Configurable via `RedisOptions.InstanceName` but the binding constraint is `spaarke:` in all deployed environments.
- **Tenant segment**: `tenant:{tenantId}:` is MANDATORY for every key (see Tenant Isolation below). The `ITenantCache` wrapper injects this automatically; callers must never construct keys without it.
- **Domain segments**: `auth`, `graph`, `embedding`, `ai`, `idem`, `system`.
- **Version suffix**: `:v{version}` (e.g., `:v3`, `:v{etag}`) used for ETag-versioned metadata and content-versioned entries. New version creates new key; old key expires via TTL.
- **Builder**: `ITenantCache.BuildKey(category, identifier, parts...)` (or the wrapper's `GetOrSetAsync` overloads) produces keys in this format. Direct string concatenation in caller code is forbidden.

**Example progression** (showing the FR-07 prefix change):

| Era | Key example |
|-----|-------------|
| Pre-remediation | `sdap:auth:access:user123:doc456` |
| Post-remediation (Phase 1) | `spaarke:tenant:contoso.onmicrosoft.com:auth:access:user123:doc456` |
| Versioned metadata | `spaarke:tenant:contoso.onmicrosoft.com:graph:metadata:driveA:item789:v"abc123etag"` |

## Tenant Isolation

**Binding invariant** (FR-05, Success Criterion #9): Every cache key written from BFF API code MUST carry a `tenant:{tenantId}:` prefix immediately after the instance name. This prevents cross-tenant data leakage where one tenant's cached authorization, metadata, or AI output could be served to a different tenant under similar identifiers (e.g., same `userId` across separate tenants).

**Enforcement mechanism**:

- The `ITenantCache` wrapper is the **only** sanctioned entry point for distributed cache operations in `Sprk.Bff.Api`. It reads tenant ID from the current `HttpContext` (or explicit `tenantId` parameter for background work) and prepends the prefix.
- `grep -r "IDistributedCache\." src/server/api/Sprk.Bff.Api/` MUST return zero matches outside the wrapper + its tests (Success Criterion #9).
- Code review checklist enforces wrapper-only access on future PRs.

### System-Level Exception Allow-List

A small, explicit set of cache entries are **legitimately cross-tenant** and use the `spaarke:system:` prefix (no `tenant:` segment):

| System key pattern | Rationale | Owner |
|--------------------|-----------|-------|
| `spaarke:system:idem:{eventId}` | Service Bus event dedup window; eventId is globally unique; tenant identity is irrelevant to dedup | IdempotencyService |
| `spaarke:system:idem:lock:{eventId}` | Processing lock for concurrent handlers; same eventId rationale | IdempotencyService |
| `spaarke:system:feature-flag:{flagId}` | (Reserved) System-wide feature flags evaluated before tenant context is available | (future) |
| `spaarke:system:config:{configKey}` | (Reserved) System-wide config requiring cross-tenant cache hit | (future) |

**Adding a new system-level exception requires**:
1. JSON-comment justification at the call site (NFR-08).
2. Code-review approval citing why tenant isolation is inapplicable.
3. Update to this allow-list.
4. Escalation to architecture review if the allow-list grows past 20 entries.

### Rationale

- **Multi-tenant invariant**: Spaarke serves multiple Dataverse tenants. Cache poisoning or accidental cross-tenant key collision is a security incident.
- **Defense in depth**: Even if authorization logic is correct upstream, a malformed cache key could surface another tenant's metadata/embeddings.
- **Auditability**: Tenant-prefixed keys make Redis traffic analysis (and per-tenant memory accounting) trivial.

## Multi-instance Behavior

The BFF API is designed to run as multiple App Service instances behind a load balancer in deployed environments. Cache invalidation and real-time job status fan-out depend on Redis Pub/Sub.

### Deployed environments (Redis backed)

- A single `ConnectionMultiplexer` is registered as singleton; serves both `IDistributedCache` and Pub/Sub channels.
- Pub/Sub channels (e.g., job status updates published by `JobStatusService`, cache invalidation notifications) fan out across all instances via Redis.
- Cache consistency is eventual: a write on instance A is immediately readable from instance B via Redis; invalidations propagate via Pub/Sub.

### Local development (in-memory mode)

- When `Redis:Enabled=false` AND `Redis:AllowFallback=true` AND `ASPNETCORE_ENVIRONMENT=Development`, `CacheModule` registers `AddDistributedMemoryCache()` for `IDistributedCache` AND a **Null-Object `IConnectionMultiplexer`** (per ADR-032) so consumers depending on the multiplexer interface don't crash.
- **Known limitation (Q-B)**: In-memory mode is **single-instance only**. The Null-Object's `Subscribe(...)` is a no-op — Pub/Sub messages are never delivered. Running multiple local instances against in-memory cache will produce stale views; the operational guide [`redis-cache-azure-setup.md`](../guides/redis-cache-azure-setup.md) documents this limitation.
- This mode is **forbidden** in deployed environments. `CacheModule` throws at startup if `Redis:Enabled=false` AND `ASPNETCORE_ENVIRONMENT != "Development"` AND `AllowFallback != true` (and even with `AllowFallback=true`, non-Development envs log a CRITICAL warning).

## Cache Instance Registry

### Architecture 1 (current — 2026-06-25)

A single "default" Redis instance per environment serves the entire BFF API:

| Environment | Redis instance | Resource group | SKU |
|-------------|---------------|----------------|-----|
| dev | `spaarke-bff-redis-dev` | `rg-spaarke-dev` | Basic C0 |
| staging | `spaarke-bff-redis-staging` | `rg-spaarke-staging` | Standard C0+ |
| prod | `spaarke-bff-redis-prod` | `rg-spaarke-prod` | Standard C2+ or Premium P1+ |

There is no per-tenant Redis instance; tenant isolation is provided by key prefixing (above), not by physical separation.

### Future extensibility (NFR-12 — Architecture 2)

The `ITenantCache` wrapper is designed as the routing seam for future multi-Redis scenarios:

- **Per-region Redis** (e.g., compliance-driven data residency): wrapper could route by tenant ID → region map.
- **Tiered Redis** (hot vs. cold; small Standard for hot keys + large Premium for analytical embeddings): wrapper could route by cache-type or key-prefix hint.
- **Per-customer dedicated Redis** (very large tenants requiring isolation beyond key prefix): wrapper could resolve a per-tenant `IConnectionMultiplexer` from a registry.

These remain explicit non-goals for the current Phase 1 remediation; the wrapper's central seam ensures we do not have to refactor 199 call sites to introduce them later. Adding a new physical Redis instance requires a future ADR amendment and corresponding wrapper-registry changes.

## Invalidation Patterns

| Pattern | Used By | Trigger |
|---------|---------|---------|
| TTL expiration | All caches | Automatic; each entry has `AbsoluteExpirationRelativeToNow` |
| Explicit delete | GraphMetadataCache | After file upload, delete, rename, or metadata update |
| Version-based key rotation | DistributedCacheExtensions, GraphMetadataCache | New ETag/version creates new key; old key expires naturally |
| Fire-and-forget cache write | CachedAccessDataSource | Authorization snapshot cached asynchronously after Dataverse fetch |
| Token removal | GraphTokenCache | On logout or token invalidation via `RemoveTokenAsync` |
| Pub/Sub broadcast invalidation | (future) cross-instance invalidation | Redis Pub/Sub channel; no-op in in-memory dev mode |

## Data Flow

1. Request arrives at BFF API endpoint
2. `RequestCache` (scoped) collapses duplicate lookups within the same request
3. Service-specific cache (`GraphTokenCache`, `EmbeddingCache`, `GraphMetadataCache`, `CachedAccessDataSource`) calls **`ITenantCache`**, which prepends `tenant:{tenantId}:` and queries Redis via `IDistributedCache`
4. On cache hit: return cached value immediately (~5-10ms)
5. On cache miss: call authoritative source (Graph API, Azure AD, Azure OpenAI, Dataverse), cache result with appropriate TTL, return value
6. On cache error (runtime — Redis up but transient fault): log warning, treat as miss, proceed to authoritative source (fail-open)
7. On Redis unreachable at startup (deployed env): fail-fast — process exits via `AbortOnConnectFail=true`
8. `CacheMetrics` records hit/miss/latency per cache type for OpenTelemetry dashboards (now dimensioned by tenant_id with cardinality controls)

## Observability

`CacheMetrics` (meter name: `Sprk.Bff.Api.Cache`) exposes three OpenTelemetry instruments:

- `cache.hits` (Counter): total hits, dimensioned by `cache.type` and `tenant_id` (cardinality-capped)
- `cache.misses` (Counter): total misses, dimensioned by `cache.type` and `tenant_id`
- `cache.latency` (Histogram): operation latency in ms, dimensioned by `cache.result`, `cache.type`, and `tenant_id`

Cache types reported: `graph`, `embedding`, `auth-access`, `graph-metadata`, `graph-folder-listing`, `graph-container-drive`, `tenant-cache` (wrapper-level).

App Insights also captures Redis dependency calls (Success Criterion #8) — visible in Live Metrics and the dependency-failure dashboard.

## Failure Mode Catalog

| Failure mode | Environment | Detection | System behavior | Alert threshold | Operator action |
|--------------|-------------|-----------|-----------------|-----------------|-----------------|
| **Redis unreachable at startup** | Deployed (dev/staging/prod) | `AbortOnConnectFail=true` raises `RedisConnectionException` during `CacheModule` init | Process exits non-zero; App Service restart loop; health probe fails | First failed startup (immediate page) | Verify Key Vault reference resolves; check Redis instance status; check NSG / private endpoint; check Managed Identity has KV read; consult [`redis-cache-azure-setup.md`](../guides/redis-cache-azure-setup.md) §Troubleshooting |
| **Redis unreachable at runtime (transient)** | All | Per-call exception caught in `ITenantCache` | Cache treated as miss; falls through to source; warning logged; latency increases | >5% miss-rate spike over baseline for 5 min | Investigate Redis CPU / memory / network; check for failover event |
| **Pub/Sub channel degraded** | Deployed (multi-instance) | Subscriber message delivery latency > 1s OR delivery failures | Stale-cache risk: invalidation events do not fan out; tenants on instance A may see stale data after instance B writes | Pub/Sub delivery latency P95 > 500 ms for 5 min | Investigate Redis Pub/Sub channel health; consider scaling SKU; check `JobStatusService` connection state |
| **Pub/Sub absent (in-memory dev mode)** | Local dev only | Null-Object `Subscribe(...)` no-op | **Single-instance only invariant** holds; multi-instance dev = stale views | N/A (dev-only; documented limitation) | Single instance only locally; switch to deployed dev for multi-instance validation |
| **SKU undersize (memory or throughput)** | All | Redis memory usage > 80%, eviction rate spike, or Redis CPU > 70% | Eviction of hot keys → cache hit rate drops; P95 endpoint latency degrades (Graph round-trips no longer absorbed) | Memory > 75%, hit rate < 60% sustained for 10 min, OR P95 endpoint latency > 1.5x baseline | Scale Redis SKU (e.g., Basic C0 → Standard C1 → Premium P1); check for runaway cache writes / TTL misconfig |
| **Connection-string secret rotation lag** | Deployed | App Settings still references old secret URI after rotation | Cache connections fail with auth error after rotation | Any auth failure on Redis connection | Update KV reference URI; force App Service restart; consult [`redis-cache-azure-setup.md`](../guides/redis-cache-azure-setup.md) §Secret Rotation |
| **In-memory fallback in non-Development env** | Deployed (misconfig) | `CacheModule` throws at startup if `Redis:Enabled=false` AND env != Development | Process exits non-zero (fail-fast); prevents silent degraded prod | Any occurrence | Restore Redis config; do NOT use `AllowFallback=true` outside local dev |
| **Cross-tenant key leakage** | All | Code path bypassing `ITenantCache`; key missing `tenant:` segment | Stored data potentially served to wrong tenant | Any direct `IDistributedCache.*` invocation outside wrapper + tests (grep gate) | Treat as security incident; rotate affected keys; PR fix via wrapper |

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Redis (Azure Cache for Redis) | StackExchange.Redis via `ITenantCache` → `IDistributedCache` | Required in deployed envs; Null-Object in local dev only |
| Depends on | Key Vault | App Settings KV reference for `Redis-ConnectionString` | MANDATORY per ADR-028; no plaintext in App Settings (FR-14) |
| Consumed by | GraphClientFactory | GraphTokenCache → ITenantCache | OBO token caching |
| Consumed by | RagService, SemanticSearchService | EmbeddingCache → ITenantCache | AI embedding caching |
| Consumed by | SpeFileStore, DriveItemOperations | GraphMetadataCache → ITenantCache | Graph API response caching |
| Consumed by | AuthorizationService | CachedAccessDataSource → ITenantCache | Authorization data caching |
| Consumed by | AnalysisOrchestrationService | AnalysisCacheEntry via ITenantCache | Analysis session state |
| Consumed by | ServiceBusJobProcessor | IdempotencyService (system-level keys) | Event deduplication; cross-tenant by design |
| Consumed by | JobStatusService | IConnectionMultiplexer (Pub/Sub) | Real-time job status fan-out across instances |
| Consumed by | OpenTelemetry pipeline | CacheMetrics | Observability with tenant dimension |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Redis-first | No hybrid L1/L2 cache | Simplicity; single source of truth for cache state until profiling proves need | ADR-009 |
| Fail-fast at startup | `AbortOnConnectFail=true` in deployed envs | Surfaces config issues immediately; prevents silent degraded operation | ADR-009 (amended Phase 5) |
| Per-call fail-open | Runtime cache errors caught and treated as misses | Cache is optimization, not correctness requirement; availability trumps performance | ADR-009 |
| ITenantCache wrapper | Sole entry point for distributed cache in BFF | Central seam for tenant prefix, metrics, future multi-Redis routing; eliminates direct `IDistributedCache.*` call sites | ADR-009 (amended), ADR-010 |
| Symmetric Null-Object IConnectionMultiplexer | Always register the interface (real or Null-Object) | Consumers can inject the interface unconditionally; no DI asymmetry per ADR-032 | ADR-032 |
| SHA256 hashing for keys | Token and embedding caches hash input content | Consistent key length, prevents sensitive data in cache keys/logs | — |
| Binary serialization for embeddings | Buffer.BlockCopy float[] to byte[] | More efficient than JSON for large float arrays (1536-dimension vectors) | — |
| Decorator pattern for authorization | CachedAccessDataSource wraps IAccessDataSource | Cache data, not decisions; authorization logic always runs fresh per-request | ADR-003 |
| In-memory mode local-dev only | Permitted via env-guarded `AllowFallback` in Development only | Local dev works without Redis infrastructure; deployed envs require real Redis | ADR-009 (amended), ADR-032 |
| `spaarke:` instance prefix | Replaces deprecated `sdap:` brand across all environments | Canonical app prefix; FR-07 / Success Criterion #10 | ADR-009 (amended) |

## Constraints

- **MUST**: Use `ITenantCache` (not `IDistributedCache` directly) for all distributed caching in `Sprk.Bff.Api/` (ADR-009 amended, FR-06)
- **MUST**: Every cache key carry `tenant:{tenantId}:` prefix UNLESS on the System-Level Exception Allow-List (FR-05)
- **MUST**: `Redis:InstanceName = "spaarke:"` in all environments (FR-07)
- **MUST**: Redis connection string sourced from Key Vault via `@Microsoft.KeyVault(...)` reference in deployed envs (FR-14, ADR-028)
- **MUST**: Fail-fast (`AbortOnConnectFail=true`) when Redis is configured but unreachable in deployed envs (ADR-009 amended)
- **MUST**: Handle runtime cache errors gracefully; never let cache errors propagate to the caller
- **MUST**: Keep authorization cache TTLs at 2 minutes or less (security-sensitive data)
- **MUST**: Symmetric DI registration of `IConnectionMultiplexer` (real or Null-Object) per ADR-032
- **MUST NOT**: Cache authorization decisions; only cache authorization data (ADR-003)
- **MUST NOT**: Store plaintext tokens in cache keys or logs; always hash with SHA256
- **MUST NOT**: Use hybrid L1 (in-memory) + L2 (Redis) caching unless profiling proves need (ADR-009)
- **MUST NOT**: Use `AllowFallback=true` (in-memory mode) outside `Development` environment
- **MUST NOT**: Call `IDistributedCache.*` directly from `Sprk.Bff.Api/` outside the wrapper + its tests (Success Criterion #9 grep gate)
- **MUST NOT**: Use the deprecated `sdap:` prefix in cache keys, config, Bicep params, or App Settings (FR-07)

## Known Pitfalls

- **Pub/Sub silent in local dev**: In-memory mode's Null-Object `IConnectionMultiplexer.Subscribe(...)` is a no-op. Multi-instance local testing of Pub/Sub-dependent features (job status fan-out, future cross-instance invalidation) requires a deployed dev environment with real Redis.
- **IConnectionMultiplexer singleton coupling**: A single `ConnectionMultiplexer` serves both `IDistributedCache` and Pub/Sub (used by `JobStatusService`). Connection issues affect both caching and real-time job status simultaneously.
- **Embedding cache size**: 1536-float vectors at 4 bytes each = ~6KB per cached embedding. High-volume workloads can accumulate significant Redis memory; the 7-day TTL provides natural eviction; monitor against the SKU-undersize alert threshold above.
- **Authorization cache staleness**: 2-minute TTL means permission changes (role assignment, team membership) can take up to 2 minutes to take effect. This is an acceptable tradeoff documented in ADR-003.
- **System-level allow-list creep**: Each new entry on the System-Level Exception Allow-List weakens tenant isolation defense-in-depth. Treat additions as architecture decisions, not routine code changes.
- **Tenant-ID resolution in background work**: `ServiceBusJobProcessor` and other background paths must explicitly pass `tenantId` to `ITenantCache` (no ambient `HttpContext`). Reuse the event payload's tenant claim.

## Related

- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching (amended Phase 5)
- [ADR-009 (full)](../adr/ADR-009-caching-redis-first.md) — Caching: Redis First (amended Phase 5)
- [ADR-003](../../.claude/adr/ADR-003-authorization-seams.md) — Cache data, not decisions
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 (Key Vault references, Managed Identity)
- [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — BFF publish hygiene
- [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — BFF Null-Object kill-switch pattern
- [redis-cache-azure-setup.md](../guides/redis-cache-azure-setup.md) — Operational guide: provision, cutover, rollback, secret rotation, decommission, troubleshooting
- [auth-performance-monitoring.md](auth-performance-monitoring.md) — Auth performance metrics including cache hit rates
