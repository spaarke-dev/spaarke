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
| CacheModule | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` | DI registration: **fail-fast** Redis connection with `AbortOnConnectFail=true` in deployed envs; env-guarded in-memory fallback (`Redis:AllowInMemoryFallback`) for Development and Testing only; **symmetric Null-Object `IConnectionMultiplexer`** registration when Redis disabled (ADR-032); throws at startup if `Redis:Enabled=false` outside Development/Testing — with or without the fallback flag |
| ITenantCache | `src/server/shared/Spaarke.Core/Cache/ITenantCache.cs` | **Mandatory wrapper** over `IDistributedCache`; injects `tenant:{tenantId}:` prefix; central seam for metrics, key validation, and future multi-Redis routing (NFR-12). **All Sprk.Bff.Api cache call sites MUST use this wrapper** (FR-06 atomic migration) |
| RedisOptions | `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | Configuration: `Enabled`, `Endpoint` (Azure Managed Redis host:10000 — the BFF authenticates with its managed identity over RESP3; required in deployed envs, task 242), `ConnectionString` (Development/Testing only), `InstanceName=spaarke:`, `AllowInMemoryFallback` (env-guarded; honoured only in Development and Testing) |
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
| 60s | Authorization document access (`CachedAccessDataSource`) | `spaarke:tenant:{tenantId}:auth-access:{authMode}:{userOid}:{documentId}:v2` | Most security-sensitive; short TTL reduces stale permission risk. A FAULTED or degraded snapshot is never cached (task 132). The document id is normalised (`D` format) so an eviction reaches it however the request spelled it; evicted for ALL users of the document on a BFF re-own or share write |
| 60s | Authorization record access (`CachedAccessDataSource`) | `spaarke:tenant:{tenantId}:auth-record-access:{entitySet}:{userOid}:{recordId}:v2` | Same; evicted for ALL users of a record on a BFF re-own or share write (task 132). A table an Assign cascade re-owns (`sharepointdocumentlocation`, `sharepointdocument`) is never cached |
| 60s | External grant set (`ExternalParticipationService`) | `spaarke:tenant:{tenantId}:external-access-grant:{contactId}:v5` | Grant data; evicted by every BFF grant write (task 137); a fault-derived set is never cached (task 132) |
| 2 min | Membership identity (`IdentityNormalizationService`) | `spaarke:tenant:{tenantId}:membership-identity:{systemUserId}:v2` | Teams, BU, linked contact; was 10 min until task 132; evicted by BFF team / BU writes |
| 2 min | Membership resolution (`MembershipResolverService`) | `spaarke:tenant:{tenantId}:membership-resolved:{subject}:{entityType}:{optionsHash}:v5` | Was 5 min until task 132; evicted per user (team / BU writes) and per entity type (re-own) |
| 2 min | Impersonated root sets (`ImpersonatedRootSetSource`) | `spaarke:tenant:{tenantId}:impersonated-root-set:{systemUserId}:{entityType}:v1` | Was 5 min until task 132; evicted per user (share, team) and per entity type (re-own) |
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
- 🔴 **Subject segment — MANDATORY (added 2026-09-28, ADR-009 §5 amendment)**: the key part **after** the tenant segment **MUST discriminate the subject** — the user, session, record or other principal whose data is cached — whenever the cached value is not identical for every principal in the tenant. **`{resource}:{id}` MUST NOT both be compile-time constants.** A key that varies by `tenantId` and nothing else is served to every user in the tenant. See *Tenant Isolation → What the tenant prefix does and does not do* below.
- **Domain segments**: `auth`, `graph`, `embedding`, `ai`, `idem`, `system`.
- **Version suffix**: `:v{version}` (e.g., `:v3`, `:v{etag}`) used for ETag-versioned metadata and content-versioned entries. New version creates new key; old key expires via TTL.
- **Builder**: `ITenantCache.BuildKey(category, identifier, parts...)` (or the wrapper's `GetOrSetAsync` overloads) produces keys in this format. Direct string concatenation in caller code is forbidden.

**Example progression** (showing the FR-07 prefix change):

| Era | Key example |
|-----|-------------|
| Pre-remediation | `sdap:auth:access:user123:doc456` |
| Post-remediation (Phase 1) | `spaarke:tenant:3f2a91c4-7b88-4e1d-9a6c-0d5e2f814bb7:auth-access:obo:user123:doc456:v1` |
| Versioned metadata | `spaarke:tenant:3f2a91c4-7b88-4e1d-9a6c-0d5e2f814bb7:graph:metadata:driveA:item789:v"abc123etag"` |

> ⚠️ **The authorization snapshot keys reached that shape only with unified-access-control-r2 task 132 (2026-10).**
> Until then `CachedAccessDataSource` wrote plain `IDistributedCache` keys (`sdap:auth:access:{mode}:{oid}:{doc}`,
> `sdap:auth:record:…`) with no tenant segment and no version, plus two write-only user-level keys; this table claimed
> the tenant-scoped shape regardless. Task 132 moved the two live keys onto `ITenantCache` (ADR-009 path C) and deleted
> the write-only ones.

> ⚠️ **`{tenantId}` is an Entra tenant GUID**, and the examples above show one. They previously showed a
> domain name (`contoso.onmicrosoft.com`), which read as "one customer per key prefix" — the exact
> misreading the section below corrects.

## Tenant Isolation

**Binding invariant** (FR-05, Success Criterion #9): Every cache key written from BFF API code MUST carry a `tenant:{tenantId}:` prefix immediately after the instance name. Combined with the mandatory **subject segment** (Key Conventions above), this prevents one principal's cached authorization, metadata, or AI output being served to a different principal under similar identifiers.

### 🔴 What the tenant prefix does and does not do (added 2026-09-28, D-12 §3 / ADR-009 §5 amendment)

**The tenant prefix separates Entra tenants. It does not separate customers.**

Under [D-12](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md) **Model 1**,
every customer's Dataverse environment is hosted in **Spaarke's** Azure tenant, so `tenantId` holds the
**same GUID for every Model 1 customer** and a key prefixed with it is identical across customers. This
section previously claimed the prefix *"prevents cross-tenant data leakage"* and that claim was being read
as customer isolation, which it never was.

**What delivers customer separation is the dedicated per-customer Redis instance** — a resource boundary in
the customer's own Azure subscription, not a key convention. Per D-12 §3, Redis is **dedicated per customer
in both models** — since owner decision D12 (2026-09-30, task 242) **Azure Managed Redis `Balanced_B0` with
high availability, Microsoft Entra only** (access keys disabled; the stamp UAMI holds the only access-policy
assignment). Azure Cache for Redis Basic/Standard/Premium retires 2028-09-30 and has blocked new-customer
creation since 2026-04-01. Losing this cache costs a cold start, not data. Redis is the clearest case for a
boundary rather than a filter because **its access control is per-instance, not per-keyspace**: any identity
the cache admits reaches every key.

⚠️ **How much the shared case would have mattered, stated precisely.** A collision also needs
`{resource}:{id}` to repeat across customers. At most call sites `{id}` is a GUID or hash (conversation id,
session id, document id, `SHA256(user-token)`, `userId:resourceId`) and is globally unique, so those keys do
not collide even on a shared instance. The exposure is keys whose id is *not* unique — which is exactly what
the **subject-segment MUST** forbids. Three such sites exist today (`agent-thread`, `agent-config`,
`approle-module-map`).

🔴 **The subject-segment MUST closes a gap the tenant prefix never covered, and dedication does not fix.**
`AgentServiceClient` composes `spaarke:tenant:{tenantId}:agent-thread:thread:v1`, where `agent-thread` and
`thread` are compile-time constants — the key varies by `tenantId` and nothing else, so **every user in a
tenant resumes the same Foundry conversation thread**. That is a cross-**user** leak *inside one customer*,
independent of tenancy model, and a dedicated Redis instance does nothing about it. (Latent, not live:
`AgentServiceOptions.Enabled` defaults to `false` and no `appsettings` sets it.) Contrast
`chat:session:{tenantId}:{sessionId}`, which is safe because `sessionId` discriminates the subject.

**Both controls stay in force.** The tenant prefix and the subject segment remain mandatory as
belt-and-braces on top of the dedicated instance; neither is optional because the other exists.

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

- **Multi-tenant invariant**: a Spaarke deployment may serve more than one Entra tenant. Cache poisoning or accidental cross-tenant key collision is a security incident.
- **Defense in depth**: even if authorization logic is correct upstream, a malformed cache key could surface another principal's metadata/embeddings. This is the prefix's actual job — it is a second line behind the dedicated per-customer Redis instance, not the customer boundary itself.
- **Auditability**: tenant-prefixed keys make Redis traffic analysis (and per-tenant memory accounting) trivial.

## Multi-instance Behavior

The BFF API is designed to run as multiple App Service instances behind a load balancer in deployed environments. Cache invalidation and real-time job status fan-out depend on Redis Pub/Sub.

### Deployed environments (Redis backed)

- A single `ConnectionMultiplexer` is registered as singleton; serves both `IDistributedCache` and Pub/Sub channels.
- Pub/Sub channels (e.g., job status updates published by `JobStatusService`, cache invalidation notifications) fan out across all instances via Redis.
- Cache consistency is eventual: a write on instance A is immediately readable from instance B via Redis; invalidations propagate via Pub/Sub.

### Local development (in-memory mode)

- When `Redis:Enabled=false` AND `Redis:AllowInMemoryFallback=true` AND `ASPNETCORE_ENVIRONMENT` is `Development` or `Testing` (the latter for CI test hosts), `CacheModule` registers `AddDistributedMemoryCache()` for `IDistributedCache` AND a **Null-Object `IConnectionMultiplexer`** (per ADR-032) so consumers depending on the multiplexer interface don't crash.
- **Known limitation (Q-B)**: In-memory mode is **single-instance only**. The Null-Object's `Subscribe(...)` is a no-op — Pub/Sub messages are never delivered. Running multiple local instances against in-memory cache will produce stale views; the operational guide [`redis-cache-azure-setup.md`](../guides/redis-cache-azure-setup.md) documents this limitation.
- This mode is **forbidden** in deployed environments. `CacheModule` throws at startup if `Redis:Enabled=false` in any environment other than Development or Testing — `AllowInMemoryFallback=true` does not change that (CacheModule branch c).

## Cache Instance Registry

### Architecture 1 (current — 2026-06-25)

A single "default" Redis instance serves one BFF deployment. Because **each customer has their own BFF App
Service in their own Azure subscription (D-12), each customer has their own Redis instance** — the
non-production rows below are shared development infrastructure, not a customer-serving topology.

| Environment | Redis instance | Resource group | SKU |
|-------------|---------------|----------------|-----|
| dev | `spaarke-bff-redis-dev` | `spe-infrastructure-westus2` | Azure Managed Redis Balanced_B0 non-HA, Entra only (`redis-dev.bicepparam`; access policy: the dev BFF's and the L2 Worker's managed identities). Cut over 2026-10-05 (task 242b): the BFF and Worker reach it through `Redis__Endpoint`; the old Basic C0 cache of the same name is being retired |
| demo | `spaarke-bff-redis-demo` | `rg-spaarke-demo` | Azure Managed Redis Balanced_B0 non-HA, Entra only (`redis-demo.bicepparam`; access policy: the demo BFF's managed identity) |
| staging | `spaarke-bff-redis-staging` (not deployed) | — | Azure Managed Redis Balanced_B0, HA (`redis-staging.bicepparam`) |
| customer (prod, both models) | `sprk-{customerId}-{env}-redis`, one per customer, in the customer's own subscription | the customer's own resource group | **Azure Managed Redis Balanced_B0, high availability, Entra only** (owner D12; size up only on a measured memory metric — no scale-down) |

**Customer separation is the dedicated instance, not the key prefix.** The `tenant:{tenantId}:` prefix and
the subject segment remain mandatory on top of it (see Tenant Isolation).

### Future extensibility (NFR-12 — Architecture 2)

The `ITenantCache` wrapper is designed as the routing seam for further multi-Redis scenarios:

- **Per-region Redis** (e.g., compliance-driven data residency): wrapper could route by tenant ID → region map.
- **Tiered Redis** (hot vs. cold; small Standard for hot keys + large Premium for analytical embeddings): wrapper could route by cache-type or key-prefix hint.

These remain explicit non-goals for the current Phase 1 remediation; the wrapper's central seam ensures we do not have to refactor 199 call sites to introduce them later. Adding a new physical Redis instance requires a future ADR amendment and corresponding wrapper-registry changes.

## Invalidation Patterns

| Pattern | Used By | Trigger |
|---------|---------|---------|
| TTL expiration | All caches | Automatic; each entry has `AbsoluteExpirationRelativeToNow` |
| Explicit delete | GraphMetadataCache | After file upload, delete, rename, or metadata update |
| Version-based key rotation | DistributedCacheExtensions, GraphMetadataCache, membership identity (v2) / resolution (v5) | New ETag/version creates new key; old key expires naturally. Task 132 bumped the two membership versions so no pre-fix (possibly fault-derived) entry is served |
| Fire-and-forget cache write | CachedAccessDataSource, ExternalParticipationService | Snapshot / grant set cached asynchronously after the Dataverse fetch — **unless it is FAULTED** (task 132): a failed read, a 429 / 5xx / timeout, or a degraded probe-derived answer is returned to its request and never stored |
| Token removal | GraphTokenCache | On logout or token invalidation via `RemoveTokenAsync` |
| BFF write-path eviction (SCAN + DEL) | `IMembershipCacheInvalidator.InvalidateUserAccessAsync` / `InvalidateRecordOwnerChangeAsync` / `InvalidateRecordShareChangeAsync` (task 132) | Team add / remove and BU bind (per user: identity, membership, root sets); re-own (per entity type: membership, root sets; per record: snapshots); every POA share write — grant, rights change, revoke — notified by `DataverseWebApiService` itself through `IRecordShareWriteObserver` (round 55), whoever called it (per root type: root sets; per record: snapshots). Every tenant segment; no HttpContext needed; patterns built from the readers' own key builders, and only for a cache that can hold the type (a child an Assign cascade re-owns gets no pattern and no SCAN). Active whenever Redis is the cache — independent of the junction channel switch |
| Pub/Sub broadcast invalidation | `MembershipCacheInvalidator.PublishInvalidationAsync` + `MembershipCacheInvalidationSubscriber` (junction-row writes) | Redis Pub/Sub channel, only with `Membership:CacheInvalidator:Enabled=true`; no-op in in-memory dev mode |

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

### Emission point: `MetricsDistributedCache` decorator (R7-S7 closure)

The `Sprk.Bff.Api.Cache` Meter is owned by `TenantCache` (static fields) but emission is at the **`IDistributedCache` decorator layer** via `MetricsDistributedCache`. Rationale: ~11 system-cache call sites (`CommunicationAccountService`, MSAL token cache, `MembershipResolverService`) inject `IDistributedCache` directly per the `SystemCacheKeys.cs` allow-list — they bypass `TenantCache`. Emitting at the wrapper layer left those sites invisible.

The decorator is wired in `CacheModule.DecorateDistributedCacheWithMetrics`: it removes the `IDistributedCache` registration produced by `AddStackExchangeRedisCache(...)` (or `AddDistributedMemoryCache()` in dev fallback) and re-registers a `MetricsDistributedCache` that wraps it. Every cache I/O — whether routed through `TenantCache` or through the system-cache path — is now counted exactly once.

### Instruments emitted

- `cache.hits` (Counter): emitted on `GetAsync` returning non-null bytes. Dimensions: `op=get`, `tier=raw`
- `cache.misses` (Counter): emitted on `GetAsync` returning null/empty bytes. Dimensions: `op=get`, `tier=raw`
- `cache.redis_call_duration_ms` (Histogram): emitted on every op (get/set/refresh/remove). Dimensions: `op`, `tier=raw`

> **Known limitation** (tracked in `notes/defer-issues.md` C-1 / DEF-007): the decorator does not currently track failures. Exceptions thrown by the inner cache (Redis timeout, connection drop) skip the `sw.Stop() + Record` calls, so a Redis outage produces zero metric — not a high error rate. Add `try/finally` + `cache.failures` counter with `outcome` dimension in R2.
>
> **Known limitation** (tracked as I-1 in R2 design): the `resource` dimension that the old `TenantCache`-layer emission carried (`session`, `document-analysis`, etc.) is not preserved at the decorator layer because cache keys are opaque strings at that layer. Pre-existing dashboards that filter by `resource` are broken. R2 will restore `resource` at the wrapper layer with bounded cardinality.

### OTel→Azure Monitor exporter (mandatory for any of this to reach App Insights)

The classic `AddApplicationInsightsTelemetry()` SDK does NOT auto-instrument StackExchange.Redis AND has no exporter for OTel-emitted custom Meters. R7-S7 wired the modern pipeline:

- `Program.cs` — `builder.Services.AddOpenTelemetry().UseAzureMonitor()` (replaces classic SDK); package `Azure.Monitor.OpenTelemetry.AspNetCore 1.4.0`. Guarded on `APPLICATIONINSIGHTS_CONNECTION_STRING` presence so test hosts continue to start.
- `TelemetryModule.cs` — `tracing.AddRedisInstrumentation()` in the `WithTracing` block; package `OpenTelemetry.Instrumentation.StackExchangeRedis 1.12.0-beta.1`.
- `CacheModule.cs` — `RedisCacheOptions.ConnectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer)`. Without this, `Microsoft.Extensions.Caching.StackExchangeRedis` builds its own internal multiplexer and the instrumented DI-registered one is idle.

### Verification queries

```kql
// Custom cache metrics — should be non-empty within 10 min of post-deploy traffic
customMetrics
| where timestamp > ago(10m)
| where name startswith 'cache.'
| summarize total=sum(value), records=count() by name

// Redis dependency telemetry — should show HMGET / UNLINK / CLIENT / SET / GET
dependencies
| where timestamp > ago(10m)
| where type contains 'Redis'
| summarize count() by type, name
```

Both queries returning empty after 10 min of traffic = exporter / instrumentation / factory wiring drift. See ADR-009 §9 for the required wiring.

## Failure Mode Catalog

| Failure mode | Environment | Detection | System behavior | Alert threshold | Operator action |
|--------------|-------------|-----------|-----------------|-----------------|-----------------|
| **Redis unreachable at startup** | Deployed (dev/staging/prod) | `AbortOnConnectFail=true` raises `RedisConnectionException` during `CacheModule` init (or the Entra token request fails) | Process exits non-zero; App Service restart loop; health probe fails | First failed startup (immediate page) | Verify `Redis__Endpoint` (host:10000) and that the identity named by `ManagedIdentity__ClientId` holds an access-policy assignment on the cache's database; check Redis instance status; check NSG / private endpoint; consult [`redis-cache-azure-setup.md`](../guides/redis-cache-azure-setup.md) §Troubleshooting |
| **Redis unreachable at runtime (transient)** | All | Per-call exception caught in `ITenantCache` | Cache treated as miss; falls through to source; warning logged; latency increases | >5% miss-rate spike over baseline for 5 min | Investigate Redis CPU / memory / network; check for failover event |
| **Pub/Sub channel degraded** | Deployed (multi-instance) | Subscriber message delivery latency > 1s OR delivery failures | Stale-cache risk: invalidation events do not fan out; tenants on instance A may see stale data after instance B writes | Pub/Sub delivery latency P95 > 500 ms for 5 min | Investigate Redis Pub/Sub channel health; consider scaling SKU; check `JobStatusService` connection state |
| **Pub/Sub absent (in-memory dev mode)** | Local dev only | Null-Object `Subscribe(...)` no-op | **Single-instance only invariant** holds; multi-instance dev = stale views | N/A (dev-only; documented limitation) | Single instance only locally; switch to deployed dev for multi-instance validation |
| **SKU undersize (memory or throughput)** | All | Redis memory usage > 80%, eviction rate spike, or Redis CPU > 70% | Eviction of hot keys → cache hit rate drops; P95 endpoint latency degrades (Graph round-trips no longer absorbed) | Memory > 75%, hit rate < 60% sustained for 10 min, OR P95 endpoint latency > 1.5x baseline | Scale the Azure Managed Redis SKU up (e.g., Balanced_B0 → B1 → B3; there is no scale-down); check for runaway cache writes / TTL misconfig |
| **Access-policy assignment missing** | Deployed | The managed identity has no access-policy assignment on the Managed Redis database (e.g. a re-created identity) | Startup connect fails with an authentication error (no key fallback exists) | First failed startup | Re-deploy the Bicep that assigns it (`customer.bicep` / `redis-{env}.bicepparam` principal list) |
| **In-memory fallback in non-Development env** | Deployed (misconfig) | `CacheModule` throws at startup if `Redis:Enabled=false` AND env != Development | Process exits non-zero (fail-fast); prevents silent degraded prod | Any occurrence | Restore Redis config; do NOT use `AllowFallback=true` outside local dev |
| **Cross-tenant key leakage** | All | Code path bypassing `ITenantCache`; key missing `tenant:` segment | Stored data potentially served to wrong tenant | Any direct `IDistributedCache.*` invocation outside wrapper + tests (grep gate) | Treat as security incident; rotate affected keys; PR fix via wrapper |

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Redis (Azure Managed Redis) | StackExchange.Redis + `Microsoft.Azure.StackExchangeRedis` (Entra, RESP3) via `ITenantCache` → `IDistributedCache` | Required in deployed envs; Null-Object in local dev only |
| Depends on | Managed identity | `Redis__Endpoint` + `ManagedIdentity__ClientId` (plain settings) | No Redis key, connection string or Key Vault secret exists (task 242, ADR-028 A4 / D13) |
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
- **MUST**: Every cache key carry `tenant:{tenantId}:` prefix UNLESS on the System-Level Exception Allow-List (FR-05). ⚠️ This separates **Entra tenants**, not customers — customer separation is the dedicated per-customer Redis instance (D-12 §3)
- **MUST**: The key part **after** the tenant segment discriminate the subject (user / session / record) whenever the cached value is not identical for every principal in the tenant — `{resource}:{id}` MUST NOT both be compile-time constants (ADR-009 §5, added 2026-09-28)
- **MUST**: `Redis:InstanceName = "spaarke:"` in all environments (FR-07)
- **MUST**: Deployed envs authenticate to Redis with the managed identity only (`Redis__Endpoint`, RESP3; access keys disabled on the cache). A connection string is accepted only in Development/Testing (task 242, owner D12/D13; supersedes FR-14's Key Vault-reference rule)
- **MUST**: Fail-fast (`AbortOnConnectFail=true`) when Redis is configured but unreachable in deployed envs (ADR-009 amended)
- **MUST**: Handle runtime cache errors gracefully; never let cache errors propagate to the caller
- **MUST**: Keep authorization cache TTLs at 2 minutes or less (security-sensitive data) — true of the code since unified-access-control-r2 task 132 (identity / membership / impersonated root sets 2 min; snapshots and grant sets 60 s)
- **MUST NOT**: Cache a fault-derived result as if it were an answer — a failed read is returned to its request (fail closed) and never stored (task 132)
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
- **Authorization cache staleness**: a change made OUTSIDE the BFF (MDA Assign / Change BU, admin UI, flows) can take up to the bounds in § Access cache residual staleness to take effect — at most 4 minutes (identity 2 min + membership 2 min, stacked). The BFF's own team / BU / owner / share writes evict immediately. Signed off by the owner (rounds 3 R3/R4).
- **System-level allow-list creep**: Each new entry on the System-Level Exception Allow-List weakens tenant isolation defense-in-depth. Treat additions as architecture decisions, not routine code changes.
- **Tenant-ID resolution in background work**: `ServiceBusJobProcessor` and other background paths must explicitly pass `tenantId` to `ITenantCache` (no ambient `HttpContext`). Reuse the event payload's tenant claim.

## Access cache residual staleness (unified-access-control-r2 task 132 · defect C12)

After task 132, staleness remains only for changes the BFF cannot observe (no Dataverse plugins, ADR-002): the
Dataverse admin UI, MDA Assign / Change BU, flows, imports. **Owner sign-off: rounds 3 R3/R4, 2026-09-30 (BINDING) —
"access changes must take effect in MINUTES, never hourly", safety net ≤ 5 min.** Every bound below is ≤ 4 minutes.

| # | Cache | TTL | Invalidated by (BFF writes) | Outside-BFF bound | Direction |
|---|---|---|---|---|---|
| 1 | External grant set | 60 s | grant / revoke / close / expiry (every tenant; organization members fanned out — task 137) | 60 s | after a removal the old access persists ≤ bound (over-grant); after an addition new access appears ≤ bound (under-grant) |
| 2 | Membership identity (teams, BU, linked contact) | 2 min | team add / remove, BU bind | 2 min | same |
| 3 | Membership resolution | 2 min | the user's team / BU writes (per user); every re-own of the entity type (per entity) | **4 min** (identity + membership stacked) | same |
| 4 | Access snapshots (RetrievePrincipalAccess answers) | 60 s | every re-own of the record and every BFF share write on it — grant / rights change / revoke, notified by `DataverseWebApiService` itself, whoever called it (all users) | 60 s — includes a user's team change (keyed by Entra oid, not evicted) | same |
| 5 | Impersonated root sets | 2 min | all users of the root type on every BFF share write (the POA seam) and on a re-own; per user on team add / remove | 2 min | same |
| 6 | Fault-derived results | never cached | — | — | a fault denies one request, never a TTL |

A read that started before an eviction and writes after it can re-cache a pre-change answer for one TTL (inherent to
cache-aside eviction). The rows an Assign cascade re-owns as a side effect (`sharepointdocumentlocation`,
`sharepointdocument`) are held by no access cache at all — no BFF write follows the cascade's owner changes, so they are
read live (batch 4 integration residual). Record: `projects/unified-access-control-r2/notes/task-132-access-cache-faults-and-staleness.md`.

## Related

- [ADR-009](../../.claude/adr/ADR-009-redis-caching.md) — Redis-first caching (amended Phase 5)
- [ADR-009 (full)](../adr/ADR-009-caching-redis-first.md) — Caching: Redis First (amended Phase 5)
- [ADR-003](../../.claude/adr/ADR-003-authorization-seams.md) — Cache data, not decisions
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2 (Key Vault references, Managed Identity)
- [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — BFF publish hygiene
- [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — BFF Null-Object kill-switch pattern
- [redis-cache-azure-setup.md](../guides/redis-cache-azure-setup.md) — Operational guide: provision, cutover, rollback, secret rotation, decommission, troubleshooting
- [auth-performance-monitoring.md](auth-performance-monitoring.md) — Auth performance metrics including cache hit rates
