# ADR-009: Redis-First Caching (Concise)

> **Status**: Accepted
> **Domain**: Data/Caching
> **Last Updated**: 2026-06-26 (operational MUSTs added by `spaarke-redis-cache-remediation-r1`)

---

## Decision

Use **Redis as distributed cache**. Per-request cache for within-request de-dupe. No hybrid L1+L2 without profiling proof.

**Rationale**: Hybrid caching adds complexity and coherence issues without demonstrated benefit.

---

## Constraints

### ✅ MUST

- **MUST** use `IDistributedCache` for cross-request caching
- **MUST** use `RequestCache` for within-request de-dupe
- **MUST** version cache keys (rowversion/etag)
- **MUST** use short TTLs for security data
- **MUST** document ADR-009 exception for any `IMemoryCache` use

### ❌ MUST NOT

- **MUST NOT** cache authorization decisions (cache data only)
- **MUST NOT** add L1 cache without profiling proof
- **MUST NOT** use `IMemoryCache` for non-metadata without justification

---

## Operational MUSTs (added 2026-06-26 by `spaarke-redis-cache-remediation-r1`)

### ✅ MUST (operational)

- **MUST** use canonical resource name `spaarke-bff-redis-{env}` for the top-level Redis Cache for Redis instance (env-suffixed). Sub-resources (cache keys, KV secret names) MUST be env-agnostic — environment is implicit in the parent service hostname.
- **MUST** size by environment per the SKU table below.

  | Environment | SKU | Capacity | Rationale |
  |---|---|---|---|
  | dev | Basic | C0 | ~$15/mo; no HA; acceptable for dev |
  | staging | Standard | C0+ | HA fidelity to prod; minimum non-prod with HA |
  | prod | Standard C2+ or Premium P1+ | sized to traffic | C2+ for traffic floor; Premium for VNet/geo-replication/Entra ID auth (S1) |
- **MUST** store the Redis connection string in Key Vault and reference it from App Settings via `@Microsoft.KeyVault(VaultName={vault};SecretName=Redis-ConnectionString)`. Plain-text connection strings in App Settings are prohibited.
- **MUST** fail-fast at BFF startup when `Redis:Enabled=true` and the instance is unreachable. `ConfigurationOptions.AbortOnConnectFail = true` in `CacheModule`. The in-memory fallback path is restricted to Development environment + explicit `Redis:AllowInMemoryFallback=true` opt-in; deployed environments throw at startup.
- **MUST** embed tenant ID in every cache key produced by application code. Industry-standard format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` (final on-wire key shape: `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`). System-level exceptions (non-tenant-scoped keys for cross-tenant resources like idempotency, watermarks, schema cache) MUST be explicitly allow-listed in `Sprk.Bff.Api/Infrastructure/Cache/SystemCacheKeys.cs` with per-site rationale (NFR-08).
- **MUST** set `Redis:InstanceName = "spaarke:"` (canonical app prefix). The deprecated `sdap:` brand is dropped.
- **MUST** register `IConnectionMultiplexer` symmetrically (real or `NullConnectionMultiplexer` based on config; never asymmetric `if (flag) { register }`). See ADR-032.
- **MUST** capture Redis dependency calls in Application Insights via the OTel pipeline. Wire `builder.Services.AddOpenTelemetry().UseAzureMonitor()` in `Program.cs` (replaces the classic `AddApplicationInsightsTelemetry()` which does NOT auto-instrument StackExchange.Redis). Add `tracing.AddRedisInstrumentation()` in `TelemetryModule.cs`. Wire `RedisCacheOptions.ConnectionMultiplexerFactory` in `CacheModule.cs` to return the DI-registered `IConnectionMultiplexer` so cache + telemetry share one multiplexer instance — otherwise the instrumented multiplexer is idle and zero Redis dep spans reach App Insights (R7-S7 closure 2026-06-26).
- **MUST** emit custom cache metrics from the `IDistributedCache` layer (decorator pattern). The `MetricsDistributedCache` decorator wraps the inner cache and emits `cache.hits`, `cache.misses` (counters), `cache.redis_call_duration_ms` (histogram) on the `Sprk.Bff.Api.Cache` Meter. Emission MUST NOT be duplicated at the `TenantCache` wrapper layer (double-counting). The decorator catches both tenant-scoped wrapper calls AND the system-cache exception path (`CommunicationAccountService`, MSAL token cache, membership refresh) that injects `IDistributedCache` directly — both go through the same Meter exactly once. R7-S7 sub-gap #2 closure.
- **MUST** define a minimum of 3 alerts in `infrastructure/bicep/alerts.bicep` (NOT just markdown): (a) hit_rate <80% / 15min, (b) P95 >100ms / 5min, (c) memory >80% of SKU. Alerts in the operational runbook only (no Bicep deploy) are insufficient — they don't page on-call.
- **MUST** access the distributed cache through the `ITenantCache` wrapper from `Sprk.Bff.Api`. Direct `IDistributedCache.GetAsync/SetAsync/RemoveAsync` calls in `Sprk.Bff.Api/` are prohibited except for sites enumerated in `SystemCacheKeys.cs`.

### ❌ MUST NOT (operational)

- **MUST NOT** allow silent in-memory fallback in Staging/Production environments. Even Development requires explicit `Redis:AllowInMemoryFallback=true`.
- **MUST NOT** recreate per-customer Redis instances. Per-customer Redis is deprecated (Q-E Architecture 1, FR-12 of `spaarke-redis-cache-remediation-r1`). Future per-customer Redis (e.g., for data-residency) is registered via the wrapper named-instance pattern (`ITenantCache cacheInstance` parameter, NFR-12 — additive change, not redesign).
- **MUST NOT** put plain-text Redis connection strings in App Settings, `appsettings.*.json`, or any code path. Always KV reference.

### Pub/Sub topology

- **MAY** share a single Redis instance for cache + Pub/Sub in dev/staging.
- **SHOULD** separate Pub/Sub from cache in prod (S2 stretch — separate Redis instance dedicated to Pub/Sub avoids fan-out backpressure on the cache).

---

## Implementation Pattern

### Distributed Cache (Default) — via `ITenantCache` wrapper

```csharp
// ✅ DO: Use the ITenantCache wrapper (mandatory tenantId)
var metadata = await _tenantCache.GetOrCreateAsync<DocumentMetadata>(
    tenantId: User.FindFirstValue("tid")!,
    resource: "doc-metadata",
    id: docId,
    version: rowVersion,
    factory: ct => _dataverse.GetDocumentMetadataAsync(docId, ct),
    ttl: TimeSpan.FromMinutes(5));
// On-wire key: spaarke:tenant:{tenantId}:doc-metadata:{docId}:v{rowVersion}
```

### Per-Request Cache

```csharp
// ✅ DO: Use RequestCache for request-scoped de-dupe
var snapshot = await _requestCache.GetOrCreateAsync(
    "uac-snapshot",
    async () => await _accessDataSource.GetSnapshotAsync());
```

### Allowed L1 Exceptions

| Scenario | TTL | Requirement |
|----------|-----|-------------|
| Per-request (`HttpContext.Items`) | Request | Always OK |
| Metadata (entity definitions) | ≤15 min | Document in code |
| Non-metadata hotspots | 1-5s | Profiling evidence required |

**See**: [Caching Pattern](../patterns/data/redis-caching.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-003](ADR-003-authorization-seams.md) | Cache snapshots, not decisions |
| [ADR-010](ADR-010-di-minimalism.md) | No hybrid cache services; `ITenantCache` justified per ≥2-impls test (default today + future named instances per NFR-12) |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | KV reference syntax for connection string; App Service MI reads |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | Publish-size delta ≤+1 MB per BFF-touching task |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | `IConnectionMultiplexer` Null-Object symmetric registration |

## See also

- `docs/architecture/caching-architecture.md` — design rationale + tenant isolation + multi-instance behavior + failure mode catalog
- `docs/guides/redis-cache-azure-setup.md` — operational runbook (provisioning, cutover, rollback, secret rotation, troubleshooting, lessons learned)
- `scripts/Deploy-RedisCache.ps1` — provisioning automation

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-009-caching-redis-first.md](../../docs/adr/ADR-009-caching-redis-first.md)

---

**Lines**: ~85
