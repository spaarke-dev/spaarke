# ADR-009: Caching policy — Redis-first with per-request cache; no hybrid L1 without proof

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2026-06-26 (operational MUSTs added by `spaarke-redis-cache-remediation-r1`) |
| Authors | Spaarke Engineering |

## Context

A hybrid cache (`IMemoryCache` + Redis + custom HybridCacheService) adds complexity, coherence issues, and extra code paths without demonstrated benefit. SDAP's hot paths need cross-instance reuse.

## Decision

| Rule | Description |
|------|-------------|
| **Redis as L2** | Use distributed cache (Redis) as the only cross-request cache |
| **Per-request L1** | `RequestCache` (scoped) to collapse duplicate reads within a request |
| **No hybrid L1+L2** | Do not implement hybrid without profiling proof |
| **Key versioning** | Version cache keys (rowversion/etag); short TTLs for security data |

## Consequences

**Positive:**
- Simpler code and fewer invalidation bugs
- Cross-instance effectiveness; consistent behavior under scale-out

**Negative:**
- Might leave small latency on the table without L1; add later if data shows need

## Alternatives Considered

Custom HybridCacheService wrapping L1+L2. **Rejected** as premature complexity.

## Operationalization

| Pattern | Implementation |
|---------|----------------|
| Distributed cache (BFF) | `ITenantCache` wrapper (`Sprk.Bff.Api.Infrastructure.Cache.ITenantCache`) — mandatory tenantId; key format `tenant:{tenantId}:{resource}:{id}:v{version}` |
| Distributed cache (shared lib / non-BFF) | `IDistributedCache` + `DistributedCacheExtensions.GetOrCreateAsync(...)` (legacy; BFF callers must migrate to `ITenantCache`) |
| Per-request cache | `RequestCache` (scoped) |
| Cache targets | UAC snapshots, document metadata, embeddings, Graph tokens, session data |
| Never cache | Authorization decisions |
| Instrumentation | `cache.hits`, `cache.misses`, `cache.redis_call_duration_ms` (custom metrics emitted from `TenantCache` wrapper); App Insights Redis dependency telemetry |
| Connection string | Key Vault secret `Redis-ConnectionString` referenced via `@Microsoft.KeyVault(VaultName=...;SecretName=Redis-ConnectionString)` syntax in App Settings |
| Failure mode | Fail-fast at BFF startup when Redis configured-but-unreachable; in-memory fallback gated to Development + explicit opt-in |

## Operational MUSTs (added 2026-06-26 by `spaarke-redis-cache-remediation-r1`)

These constraints were introduced after the dev environment drifted (Redis deleted, App Setting left at `false`, silent in-memory fallback active in a deployed environment). They are binding from project completion forward.

### 1. Canonical resource naming (two-tier rule)

- Top-level resource: `spaarke-bff-redis-{env}` (env-suffixed).
- Sub-resources (cache keys, Key Vault secret names like `Redis-ConnectionString`): **env-agnostic**. Environment is implicit in the parent service hostname.
- Rationale: future provisioning of new environments follows a predictable formula; off-pattern instances stand out in resource lists.

### 2. SKU sizing per environment

| Environment | SKU | Capacity | Rationale |
|---|---|---|---|
| dev | Basic | C0 | ~$15/mo; no HA; acceptable for dev (Q1) |
| staging | Standard | C0+ | HA fidelity to prod |
| prod | Standard C2+ or Premium P1+ | sized to traffic | Standard C2 as starting recommendation; Premium for VNet injection, geo-replication, or Entra ID auth (S1 stretch) |

### 3. Connection string lives in Key Vault

- App Setting MUST use `@Microsoft.KeyVault(VaultName={vault};SecretName=Redis-ConnectionString)` syntax. Plain-text connection strings in App Settings are prohibited.
- App Service Managed Identity MUST have "Key Vault Secrets User" role on the target KV.
- See ADR-028 for KV reference syntax and Managed Identity boundaries.

### 4. Fail-fast at startup in deployed environments

- `CacheModule` implements 4-branch logic:
  - Redis-on (`Enabled=true`): `ConfigurationOptions.AbortOnConnectFail = true`; connection failure throws `InvalidOperationException` at startup naming the configuration source.
  - Redis-off + `AllowInMemoryFallback=true` + `IHostEnvironment.IsDevelopment()`: in-memory `IDistributedCache` + `NullConnectionMultiplexer` registered (Pub/Sub no-op).
  - Redis-off + `AllowInMemoryFallback=true` + NOT Development: throws.
  - Redis-off + no fallback opt-in: throws.
- Rationale: silent degradation is the failure mode that caused this project. Throwing surfaces the problem at startup, not on the first cache call hours later.

### 5. Cache key tenant prefix mandatory

- Format: `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` → on-wire `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`.
- Enforced by the `ITenantCache` wrapper: every public method requires `tenantId` parameter. Compile-time enforcement of multi-tenant invariant.
- System-level exceptions (cross-tenant resources like idempotency event IDs, Dataverse schema metadata, SPE-dashboard aggregates, Graph token by SHA256(user-token)) MUST be explicitly enumerated in `Sprk.Bff.Api/Infrastructure/Cache/SystemCacheKeys.cs` with per-site rationale (NFR-08). Allow-list threshold: 20 sites; escalate for architecture review if exceeded.

### 6. `InstanceName` is `spaarke:`

- Drops the deprecated `sdap:` brand (FR-07). Set in `RedisOptions.InstanceName` default + `appsettings.template.json`.
- `grep -r "sdap:" src/server/api/Sprk.Bff.Api/` MUST return zero matches (verified at task 018).

### 7. Symmetric Null-Object DI registration

- Per ADR-032: `IConnectionMultiplexer` is registered in both the Redis-on path (real) and the dev-fallback path (`NullConnectionMultiplexer`). Never asymmetric `if (flag) { register }` — that pattern would break consumers that resolve `IConnectionMultiplexer` when the flag is off.
- `NullConnectionMultiplexer` semantics: Pub/Sub is no-op (P2 Quiet); `GetDatabase()` throws `NotSupportedException` with message directing the operator to use `IDistributedCache`/`ITenantCache` (P3 Fail-fast).

### 8. Pub/Sub topology

- MAY share Redis with cache in dev/staging.
- SHOULD separate Pub/Sub from cache in prod (S2 stretch) to avoid fan-out backpressure on the cache instance.
- In dev in-memory mode (single instance), Pub/Sub is no-op (`NullConnectionMultiplexer.GetSubscriber().Subscribe()` never delivers) — documented limitation per Q-B; cache entries can become stale across instances; in-memory mode is local-single-instance only.

### 9. Observability mandate (App Insights)

- Redis dependency telemetry MUST appear in Application Insights Live Metrics within 5 min of post-cutover traffic. Standard ApplicationInsights SDK auto-captures Redis deps once `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- Custom metrics emitted from `TenantCache` wrapper: `cache.hits`, `cache.misses` (counters), `cache.redis_call_duration_ms` (histogram), all with `resource` dimension. Hit-rate derived downstream.
- Minimum 3 alert rules defined (documented in `docs/guides/redis-cache-azure-setup.md` §8 Troubleshooting):
  - hit_rate <80% / 15min → "cache key/version drift; investigate"
  - P95 >100ms / 5min → "network issue or SKU undersize"
  - usedmemorypercentage >80 / 15min → "scale to next SKU"

## Exceptions

### Allowed L1 Caching Scenarios

| Scenario | TTL | Justification |
|----------|-----|---------------|
| Per-request (`HttpContext.Items`) | Request lifetime | Always allowed - no coherence issues |
| **Metadata caching** (entity definitions, navigation properties) | Up to 15 minutes | Metadata rarely changes; documented in code |
| Non-metadata hotspots | 1-5 seconds | Only after profiling proves Redis latency dominates p99 |

### Current L1 Implementations

| Location | Cache Type | TTL | ADR Reference |
|----------|------------|-----|---------------|
| `NavMapEndpoints.cs` | `IMemoryCache` | 15 min | Justified metadata hotspot (see code comments) |

### Requirements for New L1 Caching

1. **Must document** ADR-009 compliance in code comments
2. **Metadata only** for TTLs > 5 seconds
3. **Non-metadata** requires profiling evidence

## Success Metrics

| Metric | Target |
|--------|--------|
| Dataverse/Graph read counts | Reduced |
| Authorization latency | Stable |
| Cache staleness defects | Zero |

## Compliance

**Architecture tests:** `ADR009_CachingTests.cs` validates caching patterns.

**Code review checklist:**
- [ ] `IMemoryCache` use documents ADR-009 exception
- [ ] Metadata caching has appropriate TTL (≤15 min)
- [ ] Non-metadata L1 has profiling justification
- [ ] Authorization decisions not cached

## AI-Directed Coding Guidance

- Prefer `IDistributedCache` + `DistributedCacheExtensions.GetOrCreateAsync(...)` for cross-request caching.
- Use `RequestCache` for within-request de-dupe; do not add new ad-hoc `HttpContext.Items` caching.
- `IMemoryCache` is allowed only for explicitly documented metadata hotspots (see `NavMapEndpoints.cs`).

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-009 Concise](../../.claude/adr/ADR-009-redis-caching.md) - ~85 lines (now ~170 lines after 2026-06-26 operational MUSTs added)
- [Data Constraints](../../.claude/constraints/data.md) - MUST/MUST NOT rules
- [AI Caching Constraints](../../.claude/constraints/ai.md) - AI-specific caching rules

**Operational references**:
- `docs/architecture/caching-architecture.md` — design (tenant isolation, multi-instance behavior, instance registry, failure mode catalog)
- `docs/guides/redis-cache-azure-setup.md` — operational runbook (provision, cutover, rollback, secret rotation, troubleshooting, lessons learned)
- `scripts/Deploy-RedisCache.ps1` — provisioning automation

**Related ADRs**:
- [ADR-010 DI Minimalism](../../.claude/adr/ADR-010-di-minimalism.md) — `ITenantCache` interface justification (≥2 future implementations: default today + named instances per NFR-12)
- [ADR-028 Spaarke Auth Architecture](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Key Vault reference syntax
- [ADR-029 BFF Publish Hygiene](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — publish-size delta rule
- [ADR-032 BFF Null-Object Kill-Switch](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — symmetric `IConnectionMultiplexer` registration

**When to load this full ADR**: Historical context, exception details, compliance checklists.
