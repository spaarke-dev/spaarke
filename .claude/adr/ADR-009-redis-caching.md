# ADR-009: Redis-First Caching (Concise)

> **Status**: Accepted
> **Domain**: Data/Caching
> **Last Updated**: 2026-10-05 (latency alert = average BFF-observed call latency per operation — owner, task 242b review); 2026-10-04 (Azure Managed Redis, Entra only — owner D12/D13, task 242); 2026-06-26 (operational MUSTs added by `spaarke-redis-cache-remediation-r1`)

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

- **MUST** use canonical resource name `spaarke-bff-redis-{env}` for a per-environment Redis (env-suffixed) and `sprk-{customerId}-{env}-redis` for a customer stamp's. Sub-resources (cache keys, settings) MUST be env-agnostic — environment is implicit in the parent service hostname.
- **MUST** use **Azure Managed Redis** (`Microsoft.Cache/redisEnterprise`, `modules/redis.bicep`: database `default`, port 10000, OSSCluster, AllKeysLRU) sized per the table below *(amended 2026-10-04, owner D12 — Azure Cache for Redis retires 2028-09-30)*.

  | Environment | SKU | High availability |
  |---|---|---|
  | dev / demo | Balanced_B0 | Disabled |
  | staging | Balanced_B0 | Enabled |
  | platform prod (not deployed; `redis-prod.bicepparam` template) | Balanced_B0 | Enabled |
  | customer stamp (both models) | Balanced_B0 | Enabled — size up only on a measured memory metric (no scale-down; HA fixed at create) |
- **MUST** authenticate with the app's **user-assigned managed identity only** *(amended 2026-10-04, owner D13 / ADR-028 A4, task 242)*: the database has `accessKeysAuthentication: Disabled`; each identity gets a `databases/accessPolicyAssignments` entry (`default` policy); apps connect with the plain setting `Redis__Endpoint` (host:10000) + `ManagedIdentity__ClientId` through `Microsoft.Azure.StackExchangeRedis` over **RESP3** (pub/sub re-auth). Outside Development/Testing the BFF and L2 Worker refuse a connection string without `Redis__Endpoint`. *(Was: connection string in Key Vault via `@Microsoft.KeyVault(...SecretName=Redis-ConnectionString)`.)*
- **MUST** fail-fast at BFF startup when `Redis:Enabled=true` and the instance is unreachable. `ConfigurationOptions.AbortOnConnectFail = true` in `CacheModule`. The in-memory fallback path is restricted to Development or Testing (CI fixtures) + explicit `Redis:AllowInMemoryFallback=true` opt-in; deployed environments throw at startup.
- 🟡 *Amended 2026-09-28 (D-12 §3): the tenant prefix separates **Entra tenants**, NOT customers — under Model 1 every customer presents Spaarke's `tenantId`. Customer separation is the **dedicated per-customer Redis instance**. The prefix is retained as defence in depth.*
- 🔴 **MUST (added 2026-09-28): the key part AFTER the tenant segment MUST discriminate the subject** (user / session / record) whenever the cached value differs per principal — `{resource}:{id}` MUST NOT both be compile-time constants. Verified counter-example: `spaarke:tenant:{tenantId}:agent-thread:thread:v1` varies by tenant and nothing else, so every user in a tenant shares one Foundry thread.
- **MUST** embed tenant ID in every cache key produced by application code. Industry-standard format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` (final on-wire key shape: `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`). System-level exceptions (non-tenant-scoped keys for cross-tenant resources like idempotency, watermarks, schema cache) MUST be explicitly allow-listed in `Sprk.Bff.Api/Infrastructure/Cache/SystemCacheKeys.cs` with per-site rationale (NFR-08).
- **MUST** set `Redis:InstanceName = "spaarke:"` (canonical app prefix). The deprecated `sdap:` brand is dropped.
- **MUST** register `IConnectionMultiplexer` symmetrically (real or `NullConnectionMultiplexer` based on config; never asymmetric `if (flag) { register }`). See ADR-032.
- **MUST** capture Redis dependency calls in Application Insights via the OTel pipeline. Wire `builder.Services.AddOpenTelemetry().UseAzureMonitor()` in `Program.cs` (replaces the classic `AddApplicationInsightsTelemetry()` which does NOT auto-instrument StackExchange.Redis). Add `tracing.AddRedisInstrumentation()` in `TelemetryModule.cs`. Wire `RedisCacheOptions.ConnectionMultiplexerFactory` in `CacheModule.cs` to return the DI-registered `IConnectionMultiplexer` so cache + telemetry share one multiplexer instance — otherwise the instrumented multiplexer is idle and zero Redis dep spans reach App Insights (R7-S7 closure 2026-06-26).
- **MUST** emit custom cache metrics from the `IDistributedCache` layer (decorator pattern). The `MetricsDistributedCache` decorator wraps the inner cache and emits `cache.hits`, `cache.misses` (counters), `cache.redis_call_duration_ms` (histogram) on the `Sprk.Bff.Api.Cache` Meter. Emission MUST NOT be duplicated at the `TenantCache` wrapper layer (double-counting). The decorator catches both tenant-scoped wrapper calls AND the system-cache exception path (`CommunicationAccountService`, MSAL token cache, membership refresh) that injects `IDistributedCache` directly — both go through the same Meter exactly once. R7-S7 sub-gap #2 closure.
- **MUST** define a minimum of 3 alerts in `infrastructure/bicep/alerts.bicep` (NOT just markdown): (a) hit_rate <80% / 15min, (b) **average BFF-observed cache call latency per operation** (`cache.redis_call_duration_ms`) >100ms / 5min, (c) memory >80% of SKU. *(Amended 2026-10-05, owner, task 242b: was "P95 >100ms". The histogram reaches App Insights pre-aggregated (sum/count/min/max), so a true P95 of BFF-observed latency cannot be computed there; the former rule's query read a metric nothing emits and never fired.)* Alerts in the operational runbook only (no Bicep deploy) are insufficient — they don't page on-call.
- **MUST** access the distributed cache through the `ITenantCache` wrapper from `Sprk.Bff.Api`. Direct `IDistributedCache.GetAsync/SetAsync/RemoveAsync` calls in `Sprk.Bff.Api/` are prohibited except for sites enumerated in `SystemCacheKeys.cs`.

### ❌ MUST NOT (operational)

- **MUST NOT** allow silent in-memory fallback in Staging/Production environments. Even Development/Testing require explicit `Redis:AllowInMemoryFallback=true`.
- 🔴 **REVERSED 2026-09-28 (owner decision, D-12 §3).** Was: *"**MUST NOT** recreate per-customer Redis instances. Per-customer Redis is deprecated."* Now: **MUST provision one Redis instance per customer**, in that customer's own subscription *(the "STANDARD tier" in the 2026-09-28 wording was superseded 2026-09-30 by D12 — Azure Managed Redis B0 HA, Entra only; see above)*. **Why**: Redis access control is **per-instance, not per-keyspace**, and the instance holds **OBO access tokens** and the `uac-access` authorization cache — so on a shared instance every customer's BFF holds a connection to all of it, and key prefixing is a convention *our code* enforces rather than a boundary Redis enforces. ✅ **Premium is not required** (RDB persistence unneeded; VNet injection unused and Microsoft-deprecated — private endpoint works on all tiers). *(Historical: the Standard-tier reasoning of 2026-09-28 is superseded by D12 — Azure Managed Redis B0 HA, Entra only; see the MUSTs above.)* ⚠️ **Note the argument that did NOT carry this**: an earlier draft claimed a blanket cross-customer key collision. That was too strong — most keys carry a GUID/hash id and do not collide; the real exposure is **three enumerable sites**, which the subject-discrimination MUST above fixes for free. The wrapper named-instance pattern (`ITenantCache cacheInstance`, NFR-12) is the registration mechanism and remains additive; only the default disposition flips.
- **MUST NOT** put a Redis key or connection string in App Settings, `appsettings.*.json`, Key Vault or any code path for a deployed environment — there is none to put (access keys disabled). `Redis__Endpoint` is a plain host:port setting. *(Amended 2026-10-04, task 242; was "always KV reference".)*

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
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | A4 secret-free: Redis is reached with the user-assigned managed identity (Entra), no key |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | Publish-size delta ≤+1 MB per BFF-touching task |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | `IConnectionMultiplexer` Null-Object symmetric registration |

## See also

- `docs/architecture/caching-architecture.md` — design rationale + tenant isolation + multi-instance behavior + failure mode catalog
- `docs/guides/redis-cache-azure-setup.md` — operational runbook (provisioning, cutover, rollback, troubleshooting, lessons learned; its key-rotation sections are retired by task 242b — Managed Redis has no keys)
- `scripts/Deploy-RedisCache.ps1` — per-environment provisioning (Azure Managed Redis; sets `Redis__Endpoint`, no Key Vault step since task 242)

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-009-caching-redis-first.md](../../docs/adr/ADR-009-caching-redis-first.md)

---

**Lines**: ~85
