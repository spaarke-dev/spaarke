# R7 Backlog — Deferred Items from `spaarke-redis-cache-remediation-r1`

> **Purpose**: Capture stretch goals S1-S4 explicitly deferred from R1 per spec FR-22 (fifth bullet) and the spec's "Out of Scope" section. Each item is sized for future planning rounds.
> **Source spec**: `projects/spaarke-redis-cache-remediation-r1/spec.md` (FR-22, Out of Scope, Owner Clarifications)
> **Created**: 2026-06-25
> **Owner**: Spaarke product/PM

---

## S1 — Entra ID Authentication via Managed Identity + "Redis Data Owner" Role Assignment

### Summary

Migrate Redis authentication from primary-key (admin-key) auth to Microsoft Entra ID (Azure AD) authentication, using the BFF App Service's Managed Identity with the "Redis Data Owner" role assignment. Eliminates the need for the primary connection-string secret in Key Vault and the periodic key-rotation procedure currently documented in `redis-cache-azure-setup.md`.

### Blockers

- **Premium SKU requirement**: Entra ID auth for Azure Cache for Redis is a **Premium SKU feature only**. Current dev environment uses Basic C0 (per FR-13 / Q1). Cannot enable without redeploying as Premium.
- **Cost implications**: Premium P1 baseline is ~$500/mo vs Basic C0 at ~$15/mo. Needs finance review.

### Entry-Point

- Redeploy Redis instance with Premium SKU (modify `redis-{env}.bicepparam` `sku` block: `name: 'Premium'`, `family: 'P'`, `capacity: 1`).
- Author Bicep role-assignment automation for "Redis Data Owner" role on the BFF App Service Managed Identity (object ID).
- Update `CacheModule.cs` `ConfigurationOptions` to use `Azure.Identity.DefaultAzureCredential` instead of connection-string password.
- Add `Microsoft.Azure.StackExchangeRedis` package (Microsoft-published Entra ID auth provider for StackExchange.Redis).

### Estimated Effort

**1-2 days** (assuming Premium SKU already provisioned; if SKU upgrade is part of the project, add ~0.5 day for redeploy + dev cutover).

### How to Start

1. **Entry file**: `infrastructure/bicep/parameters/redis-dev.bicepparam` — change `sku` to Premium.
2. **First PR action**: Create branch `work/spaarke-redis-entra-auth-r1`; open issue referencing this S1 entry; draft `design.md` with Premium SKU justification + cost analysis.

---

## S2 — Pub/Sub Separation in Prod (Dedicated Redis Instance for Pub/Sub)

### Summary

Per amended ADR-009 (FR-20), Pub/Sub MAY share the cache Redis in dev/staging but SHOULD be separated in prod to avoid contention between cache traffic and pub/sub fan-out (`MembershipCacheInvalidator`, future invalidation channels). Today's BFF wires both through a single `IConnectionMultiplexer`. S2 provisions a second Redis instance dedicated to Pub/Sub in prod, and extends the wrapper API to route Pub/Sub to the dedicated instance via the `cacheInstance` parameter (per NFR-12 future-extensibility design).

### Entry-Point

- Extend `scripts/Deploy-RedisCache.ps1` to optionally provision a second Redis instance: `Deploy-RedisCache.ps1 -Environment prod -Purpose pubsub` (or similar).
- New `infrastructure/bicep/parameters/redis-pubsub-prod.bicepparam` for the dedicated instance (sizing tuned for Pub/Sub: lower memory, smaller SKU possible).
- New cache-instance registry entry per NFR-12: register a second `IConnectionMultiplexer` as named instance `"pubsub"` in `CacheModule.cs`; update `ITenantCache` callers that use Pub/Sub (or expose a separate `IPubSubBus` abstraction) to resolve the named instance.
- Update `MembershipCacheInvalidator.cs` + `MembershipCacheInvalidationSubscriber.cs` to use the dedicated Pub/Sub connection.
- Document topology + failure-mode interaction in `caching-architecture.md` Multi-instance Behavior section.

### Estimated Effort

**2-3 days** (split: ~1 day Bicep + deploy script extension, ~1 day BFF wiring + DI registration + tests, ~0.5 day docs + ADR-009 amendment if needed).

### How to Start

1. **Entry file**: `scripts/Deploy-RedisCache.ps1` — add `-Purpose` parameter.
2. **First PR action**: Open issue scoping prod-only Pub/Sub separation; confirm SKU sizing with infra owner; draft cache-instance registry contract aligned with NFR-12 `cacheInstance` parameter.

---

## S3 — Multi-Region Redis (Geo-Replication)

### Summary

Provision geo-replicated Redis across two Azure regions (e.g., West US 2 + East US 2) for prod-grade availability and disaster recovery. Active geo-replication on Azure Cache for Redis Premium provides automatic asynchronous data replication between two Premium cache instances in different Azure regions, with a single primary endpoint that fails over automatically.

### Blockers

- **Premium SKU requirement** (same as S1): Geo-replication is a Premium-only feature.
- **Cross-region failover testing window**: Requires a coordinated DR exercise; cannot ship without rehearsed runbook.

### Entry-Point

- Author `infrastructure/bicep/parameters/redis-prod.bicepparam` with `geoReplication` block (link two pre-provisioned Premium instances).
- Add `Microsoft.Cache/redis/linkedServers` Bicep child-resource definition (links secondary to primary).
- Extend `Deploy-RedisCache.ps1` with `-Region` and `-LinkedReplica` parameters.
- Author cross-region failover-testing runbook (per `redis-cache-azure-setup.md` template) covering: forced-failover procedure, RTO/RPO measurements, client-side reconnect verification.
- Update `caching-architecture.md` Failure Mode Catalog with regional outage + failover-latency entries.

### Estimated Effort

**1-2 days infrastructure** (Bicep + script extension + deploy) **+ cross-region failover testing** (separate 1-day exercise with stakeholders).

### How to Start

1. **Entry file**: `infrastructure/bicep/parameters/redis-prod.bicepparam` (NEW or extended) — add `geoReplication` block.
2. **First PR action**: Open issue scoping prod geo-replication; gate on S1 (Premium SKU) being complete; coordinate failover-testing window with platform team.

---

## S4 — Other Plain-Text Secret Remediation

### Summary

Per spec "Out of Scope": `DocumentIntelligence__AiSearchKey` plain-text key migration has already been moved to sister project `spaarke-ai-azure-setup-dev-r1` (its FR-15). Additional plain-text secrets remain in `spaarke-bff-dev` and `spaarke-bff-prod` App Settings that should be migrated to Key Vault references one-by-one (matching the canonical pattern enforced by this R1 project's FR-14 for `ConnectionStrings__Redis`).

### Entry-Point

- Enumerate remaining plain-text secrets: `az webapp config appsettings list -g rg-spaarke-{env} -n spaarke-bff-{env}` and filter for entries whose value does NOT start with `@Microsoft.KeyVault(`.
- Triage list against secrets that are intentionally plain-text (non-sensitive config) vs secrets that should be in KV (API keys, connection strings, tokens, OAuth secrets).
- For each "should be in KV" secret: create KV secret → update App Setting to `@Microsoft.KeyVault(VaultName=...;SecretName=...)` syntax → verify BFF picks up via MI → restart and smoke test.
- Document rotation procedure for each newly KV-backed secret in the corresponding guide (e.g., `auth-deployment-setup.md`, `redis-cache-azure-setup.md`, or a new `secret-rotation-procedures.md`).

### Sister Project Coordination

- **Already covered by sister project**: `DocumentIntelligence__AiSearchKey` (sister `spaarke-ai-azure-setup-dev-r1` FR-15). S4 picks up everything else.
- Coordinate with sister project on shared KV (`spaarke-spekvcert` vs `sprkspaarkedev-aif-kv`) to avoid duplicate KV provisioning.

### Estimated Effort

**0.5 day per secret × N secrets** (N to be determined by enumeration). Expected N for dev: ~3-5; for prod: ~5-8 based on historical patterns. Total range: **2-7 days** depending on N.

### How to Start

1. **Entry file**: NEW `scripts/Audit-PlainTextSecrets.ps1` (or extend `Provision-Customer.ps1` audit step) that enumerates App Settings + flags plain-text secrets.
2. **First PR action**: Open issue `S4-secret-remediation-audit`; run audit script against `spaarke-bff-dev` + `spaarke-bff-prod`; attach output to issue; triage list; create one sub-issue per "should-be-KV" secret; execute in priority order (highest-sensitivity first).

---

## S5 — Evaluate Azure Managed Redis (`Microsoft.Cache/redisEnterprise`) for Prod

### Summary

This project provisioned dev on **Azure Cache for Redis** (`Microsoft.Cache/Redis`, legacy product, Basic C0 at ~$15/mo) — the right product for dev cost-wise. Microsoft has since launched **Azure Managed Redis** (`Microsoft.Cache/redisEnterprise`, Redis Enterprise under the hood, GA mid-2025) as the recommended path forward for production workloads. Before any prod provisioning happens, evaluate Managed Redis vs Azure Cache for Redis Premium tier.

### Why this matters now

The recommendation surfaced during dev cutover (2026-06-26). Spec was written assuming Azure Cache for Redis throughout. Switching mid-project for dev was rejected on cost (~4× higher minimum) and rework grounds — but the prod decision is genuinely open and should be made deliberately.

### Comparison

| Aspect | Azure Cache for Redis (current) | Azure Managed Redis (candidate) |
|---|---|---|
| Resource provider | `Microsoft.Cache/Redis` | `Microsoft.Cache/redisEnterprise` |
| Engine | Open-source Redis 6.0 / 7.0 / 7.2 | Redis Enterprise (Redis 7.4+) |
| Smallest non-dev SKU | Standard C0 (~$50/mo) | Balanced B0 (~$60+/mo) |
| Prod sizing baseline | Premium P1 (~$500/mo) for HA + VNet | Balanced B3+ (~$500-1000/mo) — comparable price at scale |
| SLA | 99.9% Standard / 99.95% Premium ZR | 99.999% (Enterprise) |
| Active-active geo-replication | Premium only (passive, manual failover) | Built-in across all Enterprise tiers — collapses S3 multi-region work |
| Modules (RedisJSON / RediSearch / RedisBloom) | None | Built-in (unlocks future RediSearch use cases) |
| StackExchange.Redis compatibility | Native | Native (identical connection string format) |
| Entra ID auth | Premium only (S1 dependency) | All tiers (de-risks S1 prod path) |
| Microsoft positioning | Legacy — still GA + maintained | The recommended forward path |

### What survives unchanged on switch

The architectural patterns laid down by this project work on either product:
- `ITenantCache` wrapper + key format `spaarke:tenant:{tid}:{res}:{id}:v{n}`
- Symmetric DI registration of `IConnectionMultiplexer` (StackExchange.Redis client)
- `SystemCacheKeys.cs` allow-list
- Custom metrics emission (`cache.hits`, `cache.misses`, `cache.redis_call_duration_ms`)
- Observability + alerting strategy
- Operational runbook (provision command + cutover protocol)

### What needs rework

- `infrastructure/bicep/modules/redis.bicep` — new module for `Microsoft.Cache/redisEnterprise`
- `infrastructure/bicep/parameters/redis-prod.bicepparam` — new param schema (Managed Redis SKU shape)
- `scripts/Deploy-RedisCache.ps1` — accept either provider; or split into `Deploy-CacheForRedis.ps1` + `Deploy-ManagedRedis.ps1`
- `tests/manual/RedisValidationTests.ps1` — verify on either provider (connection-string-level identical, but Pub/Sub semantics differ slightly)
- ADR-009 amendment — note Managed Redis as the prod path; revise SKU table

### Combined leverage with S1, S2, S3

- **S1** (Entra ID auth) is a Premium-tier feature on Azure Cache for Redis, but is **all-tier** on Managed Redis. Going Managed for prod **de-risks S1**.
- **S2** (Pub/Sub separation) is still applicable. Managed Redis has slightly better cluster-aware Pub/Sub semantics but the architecture is the same: provision a second instance + use `cacheInstance` named registration.
- **S3** (multi-region geo-replication) is **dramatically simpler** on Managed Redis — active-active is built-in. On Azure Cache for Redis it requires Premium tier + manual failover orchestration.

### Entry-Point

- Spike: provision a `Microsoft.Cache/redisEnterprise` Balanced B0 instance in a sandbox subscription; exercise the BFF against it via `Deploy-RedisCache.ps1` adapted for the new provider.
- Decision memo: cost-perf comparison at projected prod traffic + SLA-vs-cost analysis + S1/S3 leverage analysis.
- If chosen: prod provisioning project (`spaarke-managed-redis-prod-r1` or similar) authoring new Bicep module + revising R1 scripts.

### Estimated Effort

- **Spike + decision memo**: 2-3 days
- **If Managed Redis chosen for prod**: ~2 weeks (Bicep module rewrite + script split + ADR-009 revision + prod cutover with finance + security review)

### How to Start

1. **Entry file**: this entry. Open a tracked issue in the team's backlog referencing this S5 + the article links in the project conversation log.
2. **First PR action**: branch `work/spaarke-managed-redis-evaluation`; sandbox provisioning + cost analysis.
3. **Decision deadline**: before any prod-tier Redis provisioning happens (sister project + future BFF prod cutovers all depend on this call).

### Reference

- Azure Managed Redis overview: https://learn.microsoft.com/en-us/azure/redis/overview
- Azure Managed Redis pricing: https://azure.microsoft.com/en-us/products/managed-redis
- This decision recorded by main session on 2026-06-26 during Phase 3 dev cutover (with the operator electing to keep dev on Azure Cache for Redis Basic C0 for cost reasons + project-mid-flight rework cost).

---

## S6 — Rename App Insights `spe-insights-dev-67e2xz` → `spaarke-bff-insights-dev` (canonical pattern)

### Summary

The current BFF App Insights instance is `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`) — off-pattern legacy name created alongside `spe-redis-dev-67e2xz` (which THIS project replaced). It is the live receiver for `spaarke-bff-dev` telemetry. Surfaced by the user during this project's Phase 3 cutover (2026-06-26) but explicitly out of scope here.

### Why this matters

- Same naming convention as the Redis instance we replaced — applies the **two-tier resource-naming rule** (NFR-03/NFR-10 in this project's amended ADR-009): top-level resources `spaarke-bff-<service>-{env}`.
- Future operators auditing resources by canonical name will miss the BFF App Insights and may provision a duplicate. Renaming closes that drift.

### Approach

Azure Application Insights resources cannot be renamed in place (same as Azure Cache for Redis — what this project just navigated). The pattern:
1. Provision new `spaarke-bff-insights-dev` (same workspace if Log Analytics-backed; otherwise standalone).
2. Update BFF App Settings: `APPLICATIONINSIGHTS_CONNECTION_STRING` → new instance's connection string.
3. Restart BFF; verify telemetry flowing to new instance (`traces | summarize count() by cloud_RoleName`).
4. 24-hr verification window.
5. Decommission `spe-insights-dev-67e2xz` (tag-then-delete).

### Reusable patterns from this project

- `Deploy-RedisCache.ps1` script structure (params, `-WhatIf`, KV/AppSettings cutover, post-deploy validation) → extend to `Deploy-AppInsights.ps1`.
- Cutover-deploy-log + sister-handoff template.
- KV-reference syntax pattern for any secrets (App Insights doesn't typically need KV for connection string but the pattern still applies if you choose to vault it).

### Coordination

Fold this into sister project `spaarke-ai-azure-setup-dev-r1` (already doing canonicalization for AI Search + KV — a third resource fits the workstream) OR carve as its own follow-up. Sister project's spec already references off-pattern legacy resources; adding App Insights is a small additive scope item.

### Estimated effort

**0.5-1 day** (much simpler than Redis: no `ITenantCache`-style code change, no atomic migration; just resource provision + connection-string swap + verify + decommission).

### How to start

1. **Entry file**: this entry + sister project `spaarke-ai-azure-setup-dev-r1/spec.md` (decide whether to fold in).
2. **First PR action**: branch `work/spaarke-appinsights-rename-dev-r1` OR `work/spaarke-ai-azure-setup-dev-r1` (if folding in); open issue referencing S6 + the Phase 3 conversation log.

### Reference

User-flagged during this project's Phase 3 cutover (2026-06-26 conversation log). See `notes/cutover-deploy-log.md`.

---

## S7 — Wire OpenTelemetry → Azure Monitor exporter (BFF telemetry pipeline closure)

### Summary

The BFF currently uses **classic Application Insights SDK** (`services.AddApplicationInsightsTelemetry()`) AND has an **OpenTelemetry pipeline registered** (`services.AddOpenTelemetry().WithMetrics(...).WithTracing(...)` in `TelemetryModule.cs`), but the OTel pipeline has **no exporter wired to Azure Monitor**. Result:

- ✅ Classic SDK auto-instruments HTTP, ServiceBus, KeyVault, Search dependencies — visible in App Insights
- ❌ Classic SDK does NOT auto-instrument StackExchange.Redis (no Redis dependencies in App Insights — verified 2026-06-26)
- ❌ ALL custom Meters (`Sprk.Bff.Api.Cache`, `Sprk.Bff.Api.Ai`, `Sprk.Bff.Api.Rag`, `Sprk.Bff.Api.CircuitBreaker`, `Sprk.Bff.Api.Finance`, plus 7 more registered in `TelemetryModule.cs`) emit measurements via System.Diagnostics.Metrics but NEVER reach App Insights — confirmed 2026-06-26 via `customMetrics | where name startswith 'cache.'` returning empty

### Why this matters

The `spaarke-redis-cache-remediation-r1` project added a `Sprk.Bff.Api.Cache` Meter (`cache.hits`, `cache.misses`, `cache.redis_call_duration_ms`) per FR-16 and `OpenTelemetry.Instrumentation.StackExchangeRedis` per task 040 closure (2026-06-26 commit). Both are **correctly registered**; they will start producing dashboard-visible data the moment an exporter is wired. Until then, the metrics + Redis spans live only in-process.

This affects every Meter in the BFF, not just the cache one. The full custom-metric pipeline (R5 Summarize, Insights Widgets, AI Capabilities, AI Latency, Prompt Shield, etc.) is similarly dark in App Insights.

### Approach

Two viable paths — pick one for the BFF as a whole.

**Path A — Modern `Azure.Monitor.OpenTelemetry.AspNetCore` (recommended, replaces classic SDK)**:

```xml
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />
```
```csharp
// Program.cs — replace AddApplicationInsightsTelemetry()
builder.Services.AddOpenTelemetry().UseAzureMonitor();
```
- One-call wiring; replaces classic SDK with OTel-native AppInsights bridge
- Removes the dual-pipeline drift (classic SDK + OTel)
- Custom Meters + ActivitySources automatically flow to App Insights
- `OpenTelemetry.Instrumentation.StackExchangeRedis` (already added) starts emitting Redis dependency spans

**Path B — Add explicit exporters alongside classic SDK** (less invasive):
```xml
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.0" />
<!-- Or for direct Azure Monitor: -->
<PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" Version="1.4.0" />
```
```csharp
// TelemetryModule.cs
.WithMetrics(m => m.AddAzureMonitorMetricExporter())
.WithTracing(t => t.AddAzureMonitorTraceExporter())
```
- Dual pipeline coexists; ARE you OK with duplicate data?
- More config knobs but more surface area

### Verification recipes (run after wiring)

```kql
// 1) Custom metrics should appear
customMetrics
| where timestamp > ago(15m)
| where name in ('cache.hits', 'cache.misses', 'cache.redis_call_duration_ms')
| summarize sum(value) by name
// Expect: non-empty result

// 2) Redis dependencies should appear
dependencies
| where timestamp > ago(15m)
| where type contains 'Redis'
| summarize count() by name
// Expect: SET / GET / EVAL etc.
```

### Estimated effort

**0.5-1 day**. Path A is a 1-line code change + package add + retest; the heavy lift is the regression sweep (some classic-SDK telemetry shapes differ slightly under the OTel bridge — affects existing dashboards/alerts).

### How to start

1. **Entry file**: `src/server/api/Sprk.Bff.Api/Program.cs` + `Sprk.Bff.Api.csproj`.
2. **First PR action**: branch `work/bff-telemetry-otel-exporter`; design.md justifying Path A vs B; smoke test before merge against `spaarke-bff-dev`.

### Reference

Surfaced during this project's Phase 4 verification (2026-06-26). See `notes/cutover-deploy-log.md`. The classic-SDK + OTel-no-exporter drift is the root cause that prevents FR-16 metrics from being visible in App Insights despite the wrapper emitting them correctly.

### Status (2026-06-26): partial closure landed inline

**Done in this project (commit pending)**:
- Path A wired: added `Azure.Monitor.OpenTelemetry.AspNetCore 1.4.0`; replaced `Program.cs:16` `AddApplicationInsightsTelemetry()` with `AddOpenTelemetry().UseAzureMonitor()` (guarded on `APPLICATIONINSIGHTS_CONNECTION_STRING` to keep tests/local-dev hosts working).
- Deployed to `spaarke-bff-dev` (deploy `2026-06-26 ~13:25 UTC`; 4 critical DLLs hash-verified; `/healthz` green).
- **Proven flowing to App Insights**: `http.server.request.duration`, `http.client.request.duration`, `http.client.open_connections`, `http.client.connection.duration`, `http.client.request.time_in_queue`, `http.server.active_requests`, `http.client.active_requests`, `circuit_breaker.open_count` (custom Meter from BFF code). Built-in dependency telemetry continues for HTTP/AAD/ServiceBus/KeyVault/Search/Cosmos. KQL verified.

**Remaining gaps — require R8 follow-up code work**:

1. **`AddRedisInstrumentation()` not surfacing Redis dep spans even though traffic is flowing.**
   - Evidence: BFF traces show every-minute `"Cached 2 communication accounts (comm:accounts:receive-enabled) with 00:05:00 TTL"` + `"Health check redis with status Healthy"` confirming Redis IS being written/read. But `dependencies | where type contains 'Redis'` returns ZERO across multiple 10-/60-/360-minute windows.
   - Root cause hypothesis: `Microsoft.Extensions.Caching.StackExchangeRedis` creates its **own internal** `ConnectionMultiplexer` from the connection string. The DI-registered `IConnectionMultiplexer` (per CacheModule's symmetric ADR-032 registration) is a **separate** instance. `AddRedisInstrumentation()` hooks the DI-registered one — which is idle in the cache hot path.
   - Fix: wire `RedisCacheOptions.ConnectionMultiplexerFactory` in `CacheModule.cs` to return the DI-registered `IConnectionMultiplexer` (so cache + telemetry share the same multiplexer instance). ~1-line CacheModule change + redeploy + reverify.

2. **`cache.hits` / `cache.misses` / `cache.redis_call_duration_ms` Meter measurements not appearing in `customMetrics`.**
   - Evidence: `customMetrics | where name startswith 'cache.'` returns ZERO across 10-/60-/180-minute windows, despite `Sprk.Bff.Api.Cache` Meter being registered in `TelemetryModule.cs:25` AND `TenantCache.HitsCounter.Add(...)` being called by hot-path code (verified via comm:accounts log).
   - Root cause hypothesis: `TenantCache.Meter` is created at static-init (`internal static readonly Meter Meter = new("Sprk.Bff.Api.Cache", "1.0.0")`) which runs at TenantCache type-load time, potentially BEFORE the OTel MeterProvider is wired up by `UseAzureMonitor()` + `TelemetryModule.AddTelemetryModule()`. Counter measurements recorded before MeterProvider subscribes may be lost. OR: not all migrated cache call sites actually route through `TenantCache.HitsCounter` (some may bypass to `IDistributedCache` directly within `TenantCache.cs` extensions / `Spaarke.Core/Cache/DistributedCacheExtensions.cs`).
   - Fix: (a) Audit Meter construction ordering — possibly move Meter creation to `TenantCache` constructor or use `IMeterFactory` from DI. (b) Audit ALL `cache.hits/misses` increment sites to confirm they fire on every cache path. Probably a half-day investigation + small fix.

Both fixes are non-blocking for: (i) the dev cutover (Phase 3 already cleared sister project per NFR-11/13), (ii) operational health (Redis IS healthy + reachable + responding to BFF traffic per traces), (iii) general telemetry pipeline (it's flowing for HTTP/built-in deps + at least one custom counter).

**Recommended R8 sequencing**: Fix #1 first (1-line code change, immediate Redis dep telemetry restoration). Fix #2 second (deeper audit; cache.* dashboard is the more visible payoff for end users). Both <1 day combined.

---

## Cross-References

- **Spec source**: [`spec.md`](../spec.md) FR-22 (fifth bullet) + Out of Scope section
- **Sister project**: `spaarke-ai-azure-setup-dev-r1` (S4 partial overlap; coordinate KV usage)
- **ADR-009 amended**: [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) + [`docs/adr/ADR-009-caching-redis-first.md`](../../docs/adr/ADR-009-caching-redis-first.md) (S2 Pub/Sub separation guidance "SHOULD in prod" lives here)
- **Operational guide**: [`docs/guides/redis-cache-azure-setup.md`](../../docs/guides/redis-cache-azure-setup.md) (lessons-learned, secret-rotation procedures, failure-mode catalog — S2/S3/S4 will extend)
- **Cache wrapper API**: `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/ITenantCache.cs` `cacheInstance` parameter (NFR-12 future-extensibility — S2 will leverage)

---

*Backlog captured per FR-22 fifth bullet. Each item is non-binding; effort estimates are planning hints, not commitments. Reassess at R7 kickoff.*
