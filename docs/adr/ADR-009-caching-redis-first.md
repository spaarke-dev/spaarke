# ADR-009: Caching policy — Redis-first with per-request cache; no hybrid L1 without proof

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2026-06-26 (operational MUSTs added by `spaarke-redis-cache-remediation-r1`); 2026-10-04 (§2/§3 amended: Azure Managed Redis, Microsoft Entra only — owner D12/D13, `customer-provisioning-orchestration-r1` task 242); 2026-10-05 (latency alert = average BFF-observed call latency per operation — owner, task 242b) |
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
| Authentication | Microsoft Entra with the app's user-assigned managed identity (`Redis__Endpoint` host:10000 + `ManagedIdentity__ClientId`, RESP3) — access keys disabled on the cache; no connection string or Key Vault secret (amended 2026-10-04, task 242) |
| Failure mode | Fail-fast at BFF startup when Redis configured-but-unreachable; in-memory fallback gated to Development/Testing + explicit opt-in |

## Operational MUSTs (added 2026-06-26 by `spaarke-redis-cache-remediation-r1`)

These constraints were introduced after the dev environment drifted (Redis deleted, App Setting left at `false`, silent in-memory fallback active in a deployed environment). They are binding from project completion forward.

### 1. Canonical resource naming (two-tier rule)

- Top-level resource: `spaarke-bff-redis-{env}` (env-suffixed).
- Sub-resources (cache keys, settings): **env-agnostic**. Environment is implicit in the parent service hostname.
- Customer stamps: `sprk-{customerId}-{env}-redis` (`customer.bicep`).
- Rationale: future provisioning of new environments follows a predictable formula; off-pattern instances stand out in resource lists.

### 2. Product and SKU (amended 2026-10-04 — owner D12, task 242)

Every Spaarke Redis is **Azure Managed Redis** (`Microsoft.Cache/redisEnterprise`, `infrastructure/bicep/modules/redis.bicep`):
one database `default`, port 10000, `OSSCluster`, `AllKeysLRU`. Azure Cache for Redis Basic/Standard/Premium retires
2028-09-30 and has blocked new-customer creation since 2026-04-01.

| Environment | SKU | High availability | Rationale |
|---|---|---|---|
| dev / demo | Balanced_B0 | Disabled | Cost; dev fidelity (task 242b) |
| staging | Balanced_B0 | Enabled | Fidelity to customer stamps |
| platform prod (`spaarke-bff-redis-prod`, not deployed) | Balanced_B0 | Enabled | Template only (`redis-prod.bicepparam`); its access-policy principal is a placeholder that blocks deploy until filled |
| customer stamp (prod, both models) | Balanced_B0 | Enabled | Owner D12; size up only on a measured memory metric — Managed Redis has no scale-down, and HA is fixed at create |

*(Was: Basic C0 dev / Standard C0+ staging / Standard C2+ or Premium prod; and, from 2026-09-28, Standard per customer.)*

### 3. Authentication — managed identity only (amended 2026-10-04 — owner D13, ADR-028 A4; task 242)

- The database MUST have `accessKeysAuthentication: Disabled`. No access key, connection string or Key Vault secret
  exists for a deployed Redis.
- Access is granted per identity through `databases/accessPolicyAssignments` (`accessPolicyName: 'default'`) to the
  user-assigned managed identity the app runs as — for a customer stamp, the stamp UAMI only.
- Apps connect with `Redis__Endpoint` (host:10000, a **plain** app setting, never a Key Vault reference) and
  `ManagedIdentity__ClientId`, via `Microsoft.Azure.StackExchangeRedis`
  `ConfigureForAzureWithUserAssignedManagedIdentityAsync` over **RESP3** (RESP2 cannot re-authenticate the pub/sub
  connection when the token refreshes).
- Outside Development/Testing the BFF and the L2 Worker refuse to start on a Redis connection string without
  `Redis__Endpoint`; a connection string is a local-development convenience only.
- *(Was: "App Setting MUST use `@Microsoft.KeyVault(VaultName={vault};SecretName=Redis-ConnectionString)` syntax"
  with the App Service identity holding Key Vault Secrets User on the vault.)*

### 4. Fail-fast at startup in deployed environments

- `CacheModule` implements 4-branch logic:
  - Redis-on (`Enabled=true`): `ConfigurationOptions.AbortOnConnectFail = true`; connection failure throws `InvalidOperationException` at startup naming the configuration source.
  - Redis-off + `AllowInMemoryFallback=true` + Development or Testing (CI fixtures): in-memory `IDistributedCache` + `NullConnectionMultiplexer` registered (Pub/Sub no-op).
  - Redis-off + `AllowInMemoryFallback=true` + any other environment: throws.
  - Redis-off + no fallback opt-in: throws.
- Rationale: silent degradation is the failure mode that caused this project. Throwing surfaces the problem at startup, not on the first cache call hours later.

### 5. Cache key tenant prefix mandatory

> 🟡 **AMENDED 2026-09-28 (D-12 §3) — the prefix is NOT the customer boundary.** CLAUDE.md §6.5 path B.
> The format and the `ITenantCache` enforcement are **unchanged**. What changes is the *claim made about
> them*, plus one new MUST. See the amendment note below the bullets.

- Format: `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` → on-wire `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`.
- Enforced by the `ITenantCache` wrapper: every public method requires `tenantId` parameter. Compile-time enforcement of the **tenant**-scoping invariant.
- 🔴 **MUST (added 2026-09-28): the key part AFTER the tenant segment MUST discriminate the subject** — the user, session, record or other principal whose data is cached — whenever the cached value is not identical for every principal in the tenant. `{resource}:{id}` MUST NOT both be compile-time constants.
- System-level exceptions (cross-tenant resources like idempotency event IDs, Dataverse schema metadata, SPE-dashboard aggregates, Graph token by SHA256(user-token)) MUST be explicitly enumerated in `Sprk.Bff.Api/Infrastructure/Cache/SystemCacheKeys.cs` with per-site rationale (NFR-08). Allow-list threshold: 20 sites; escalate for architecture review if exceeded.

#### 🟡 Amendment note (2026-09-28) — what the prefix does and does not do

**Source**: owner decision D-12 §3 (`projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`).

**The tenant prefix separates Entra tenants. It does not separate customers.** Under D-12 Model 1, every
customer's Dataverse environment is hosted in **Spaarke's** Azure tenant, so `tenantId` holds the **same
GUID for every Model 1 customer**. A key prefixed with it is identical across customers. The original text
called this *"compile-time enforcement of multi-tenant invariant"*, which is true and was being read as
"customer isolation", which it never was.

⚠️ **How much this matters in practice — stated precisely, because an earlier draft of this amendment
overstated it.** A collision needs `{resource}:{id}` to repeat across customers as well. At most call sites
`{id}` is a **GUID or hash** (conversation id, session id, document id, `SHA256(user-token)`,
`userId:resourceId`), which is globally unique — **those keys do not collide on a shared instance.** The
exposure is the keys whose id is *not* unique, which is exactly what the new MUST above forbids. Three such
sites exist today (`agent-thread`, `agent-config`, `approle-module-map`).

✅ **DECIDED (owner, 2026-09-28): one Redis instance per customer**, in that customer's own subscription.
*(The tier in that decision — STANDARD — was superseded on 2026-09-30 by owner D12: Azure Managed Redis
Balanced_B0 with high availability, Entra only; see §2/§3. The tier reasoning below is kept as the record.)*
*(This paragraph previously said the question was open — it was, for part of that day.)*
**Premium is not required**: RDB persistence is unneeded (the upload-bytes entry is *"the hot-tier peer of
the durable blob copy"*), and VNet injection is unused and Microsoft-deprecated in favour of private
endpoint, which works on all tiers. Standard restores the SLA and replication that Basic C0 lacks.
⚠️ Confirm Standard meets the **performance** bar before provisioning. Evidence:
`projects/unified-access-control-r2/notes/D-12-redis-shared-vs-dedicated.md` and `…/D-12-resource-sharing-analysis.md` §4.

**The dedication decision and the new MUST are independent.** The MUST is required either way, and it is
the only thing that fixes the cross-*user* case — dedicating per customer still leaves every **user** inside
that customer sharing one `agent-thread` key.

🔴 **The new MUST above closes a real gap that this ADR never covered.** Requiring only the *tenant* segment
permits a key whose remaining parts are constants. Verified live example: `AgentServiceClient` composes
`spaarke:tenant:{tenantId}:agent-thread:thread:v1`, where `agent-thread` and `thread` are compile-time
constants — so the key varies by `tenantId` and nothing else, and **every user in a tenant resumes the same
Foundry conversation thread**. That is a cross-**user** leak *inside one tenant*, wholly independent of
tenancy model, and ADR-009 as written permitted it. (Latent, not live: `AgentServiceOptions.Enabled`
defaults to `false` and no `appsettings` sets it.) Contrast `chat:session:{tenantId}:{sessionId}`, which is
safe because `sessionId` discriminates.

⚠️ **The System-Level Exception allow-list is unchanged but should be re-read under this framing.** It
enumerates keys deliberately shared *across tenants*. Under Model 1 "across tenants" and "across customers"
are no longer the same question — an entry that is safe to share across Entra tenants may still be unsafe to
share across customers. Re-validate each entry's rationale on that axis when next touched; no entry is known
to fail today.

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

**Pipeline wiring (R7-S7 closure 2026-06-26 — required for any of this to actually reach App Insights):**

The classic `AddApplicationInsightsTelemetry()` SDK does NOT auto-instrument StackExchange.Redis AND has no exporter for OpenTelemetry-emitted custom Meters. Both gaps must be closed:

1. `Program.cs` — `builder.Services.AddOpenTelemetry().UseAzureMonitor()` (replaces the classic SDK). Package: `Azure.Monitor.OpenTelemetry.AspNetCore 1.4.0`. Guard on `APPLICATIONINSIGHTS_CONNECTION_STRING` presence so test hosts continue to start.
2. `TelemetryModule.cs` — `tracing.AddRedisInstrumentation()` in the `WithTracing` block. Package: `OpenTelemetry.Instrumentation.StackExchangeRedis 1.12.0-beta.1`.
3. `CacheModule.cs` — `RedisCacheOptions.ConnectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer)`. Without this, `Microsoft.Extensions.Caching.StackExchangeRedis` builds its own internal multiplexer and the instrumented DI-registered one is idle in the cache hot path. Verified empirically: omitting this returns zero Redis dep spans even with full instrumentation registered.

**Verification (must run after every deploy that touches CacheModule / TelemetryModule / Program.cs):**

```kql
// Redis dependency telemetry (expect HMGET/UNLINK/CLIENT after 10 min of traffic)
dependencies
| where timestamp > ago(10m)
| where type contains 'Redis'
| summarize count() by type, name
```

**Custom cache metrics (R7-S7 sub-gap #2 closure):**

Emission is at the `IDistributedCache` decorator layer (`MetricsDistributedCache`), NOT at the `TenantCache` wrapper. Rationale: ~11 system-cache call sites (`CommunicationAccountService`, MSAL token cache, membership refresh) inject `IDistributedCache` directly per the `SystemCacheKeys.cs` allow-list and bypass `TenantCache`. Emitting at the `TenantCache` layer left those sites invisible; emitting at the decorator catches every cache I/O exactly once.

- `cache.hits`, `cache.misses` (counters)
- `cache.redis_call_duration_ms` (histogram)

All on the `Sprk.Bff.Api.Cache` Meter (registered in `TelemetryModule.cs:25`). Tag dimensions kept bounded: `op` (get/set/refresh/remove) and `tier` (raw — placeholder for future wrapper-layer re-emission with `resource` tag if needed). Hit-rate derived downstream.

```kql
customMetrics
| where timestamp > ago(10m)
| where name startswith 'cache.'
| summarize total=sum(value), records=count() by name
```

**Alerts (3 minimum) MUST be Bicep-deployed:**

- Hit_rate <80% / 15min → "cache key/version drift; investigate"
- Average BFF-observed cache call latency per operation (`cache.redis_call_duration_ms`) >100ms / 5min → "network issue, SKU undersize or client-side delay" *(amended 2026-10-05, owner, task 242b — was "P95 >100ms": App Insights stores the histogram pre-aggregated, so a true P95 is not available there, and the previous alert queried a metric nothing emits, so it never fired. Compare with the `redis` dependency durations to tell server time from client time.)*
- `usedmemorypercentage` >80 / 15min → "scale to next SKU"

Markdown-only alert documentation (`docs/guides/redis-cache-azure-setup.md` §8) is NOT sufficient — alerts must be deployed via `infrastructure/bicep/alerts.bicep` and verified firing in a test condition. Markdown-only fails the operational MUST.

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
| `Infrastructure/Caching/EndpointResponseCache.cs` | `IMemoryCache` | Per-call TTL | Deliberate in-process L1 response cache — see documented exception below |

### Documented exceptions

- **`EndpointResponseCache` (`Infrastructure/Caching/EndpointResponseCache.cs`)** — dated **2026-07-10**, surfaced by the `spaarke-ai-architecture-redesign-r2` end-to-end audit (finding F-6 triage). This type is an **intentional** in-process L1 response cache introduced by `ci-cd-unit-test-remediation-r1` task 087 (spec **FR-A06**) to consolidate scattered `IMemoryCache` usage out of endpoint handlers into a single, testable seam. It is a deliberate per-process L1 design and MUST NOT be converted to `IDistributedCache` (that would defeat its response-memoization purpose). Resolved via ADR-conflict resolution **path A** (project-scoped documented exception). The `Spaarke.ArchTests` `ADR009_CachingTests.ServicesShouldPreferDistributedCache` check allowlists this type **by exact name only**; no other `*Cache` type rides the exemption.

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
- `docs/guides/redis-cache-azure-setup.md` — operational runbook (provision, cutover, rollback, troubleshooting, lessons learned; key-rotation sections retired by task 242b)
- `scripts/Deploy-RedisCache.ps1` — per-environment provisioning (Azure Managed Redis; sets `Redis__Endpoint`, no Key Vault step since task 242)

**Related ADRs**:
- [ADR-010 DI Minimalism](../../.claude/adr/ADR-010-di-minimalism.md) — `ITenantCache` interface justification (≥2 future implementations: default today + named instances per NFR-12)
- [ADR-028 Spaarke Auth Architecture](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — A4 secret-free identity: Redis is reached with the app's user-assigned managed identity (task 242)
- [ADR-029 BFF Publish Hygiene](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — publish-size delta rule
- [ADR-032 BFF Null-Object Kill-Switch](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — symmetric `IConnectionMultiplexer` registration

**When to load this full ADR**: Historical context, exception details, compliance checklists.
