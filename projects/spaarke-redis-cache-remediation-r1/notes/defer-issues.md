# Defer / Issue Tracking — spaarke-redis-cache-remediation-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarke-redis-cache-remediation-r1`
> **Migration note**: entries DEF-001..DEF-006 were migrated from the prior `notes/r7-backlog.md` file (created 2026-06-25). R7-S7 itself shipped inline in this project and is therefore NOT in this file. The `r7-backlog.md` file is preserved at the prior path as a historical decision record but is no longer the canonical tracker.

---

## Open (in priority order)

### DEF-001 — Entra ID auth via Managed Identity + "Redis Data Owner" role

| Field | Value |
|---|---|
| **Status** | ✅ Closed (Done by R2 PR #489, merged 2026-06-27, commit `8180f8d44`) — 2026-06-29 |
| **Urgency** | (closed) |
| **Filed** | 2026-06-25 (migrated 2026-06-26) |
| **Source** | spec.md FR-22 — explicitly deferred from R1; user-confirmed during Phase 3 cutover |
| **GitHub Issue** | [#462](https://github.com/spaarke-dev/spaarke/issues/462) — closed manually 2026-06-29 with PR #489 reference (auto-close didn't fire; PR description omitted "Closes #462" keyword). |
| **Supersession rationale** | Project owner decision (2026-06-26): not worth +$485/mo for ACR Premium SKU to eliminate key rotation alone, AND Managed Redis (which has all-tier Entra ID) is also rejected (see DEF-005). Instead, R2 Theme B builds automation for the rotation procedure — same operational outcome (rotation happens reliably) without infra cost. |
| **R2 deliverables (Theme B)** | `scripts/Rotate-RedisKey.ps1` (safe-window rotation: Secondary → KV → restart → `/healthz` → Primary) + `.github/workflows/redis-key-rotation.yml` (quarterly staggered cron, OIDC per-env SP) + `docs/guides/redis-cache-azure-setup.md` §6 update + missed-rotation alert (FR-11). Shipped via PR #489 merge to master. |

**Description**

Migrate Redis authentication from primary-key (admin-key) auth to Microsoft Entra ID (Azure AD) authentication using the BFF App Service's Managed Identity with the "Redis Data Owner" role assignment. Eliminates the periodic key-rotation procedure currently documented in `redis-cache-azure-setup.md` and the connection-string secret in Key Vault.

Concrete failure mode without this: every 90 days the operator must rotate the Redis primary key, update the KV secret, and restart the BFF — a manual procedure that has historically slipped, leaving stale keys in production.

**Entry-points**

- `infrastructure/bicep/parameters/redis-dev.bicepparam` — change `sku` block to Premium (Entra ID auth is Premium-only on Azure Cache for Redis)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs:46-100` — replace connection-string parsing with `Azure.Identity.DefaultAzureCredential`
- New package: `Microsoft.Azure.StackExchangeRedis` (Microsoft's Entra ID auth provider for StackExchange.Redis)
- Role assignment Bicep: "Redis Data Owner" on BFF App Service Managed Identity (object ID)

**Suggested fix**

Provision Premium SKU → assign MI role → update CacheModule → drop KV secret. Coordinate with DEF-005 (S5) — if Managed Redis is chosen for prod, Entra ID auth is all-tier and de-risks this work.

**Estimated effort**: 1-2 days (assuming Premium already provisioned; +0.5 day if SKU upgrade is part of scope)
**Blockers**: Premium SKU provisioning (cost +~$485/mo over Basic C0 baseline)
**Related**: DEF-005, ADR-028 (Auth v2)

---

### DEF-002 — Pub/Sub separation in prod (dedicated Redis instance)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-25 (migrated 2026-06-26) |
| **Source** | spec FR-22 + amended ADR-009 |
| **GitHub Issue** | [#463](https://github.com/spaarke-dev/spaarke/issues/463) |

**Description**

Amended ADR-009 (FR-20) says Pub/Sub MAY share the cache Redis in dev/staging but SHOULD be separated in prod to avoid contention between cache traffic and pub/sub fan-out (`MembershipCacheInvalidator`, future invalidation channels). Today's BFF wires both through a single `IConnectionMultiplexer`. Provision a second Redis instance dedicated to Pub/Sub in prod.

Concrete failure mode without this: under load, a hot cache key (e.g., dashboard sync writes during peak) can starve Pub/Sub message delivery, delaying membership invalidations across instances and causing cross-instance permission drift (user revoked on instance A still cached as granted on instance B).

**Entry-points**

- `scripts/Deploy-RedisCache.ps1` — add `-Purpose pubsub` parameter; provision second instance
- New `infrastructure/bicep/parameters/redis-pubsub-prod.bicepparam` — smaller SKU (Pub/Sub is lower-memory)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` — register second `IConnectionMultiplexer` as named instance `"pubsub"` per NFR-12 future-extensibility
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipCacheInvalidator.cs` + `MembershipCacheInvalidationSubscriber.cs` — route to dedicated Pub/Sub connection

**Suggested fix**

Extend `ITenantCache.cacheInstance` parameter (per NFR-12) OR introduce a separate `IPubSubBus` abstraction. Document in `caching-architecture.md` Multi-instance Behavior section.

**Estimated effort**: 2-3 days (1 day Bicep + script, 1 day BFF wiring + tests, 0.5 day docs)
**Blockers**: none
**Related**: ADR-009 (amended), NFR-12, DEF-005

---

### DEF-003 — Multi-region Redis (geo-replication)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-06-25 (migrated 2026-06-26) |
| **Source** | spec FR-22 + Out of Scope |
| **GitHub Issue** | [#464](https://github.com/spaarke-dev/spaarke/issues/464) |

**Description**

Provision geo-replicated Redis across two Azure regions (West US 2 + East US 2) for prod availability and disaster recovery. Active geo-replication on Azure Cache for Redis Premium provides automatic async replication between two Premium cache instances in different regions with a single primary endpoint that fails over.

Concrete failure mode without this: a single-region outage (e.g., West US 2 incident) takes down BFF cache, cascading to chat session loss, OBO token re-issue storms, and dashboard cold loads — all measurable RTO impact in DR scenarios.

**Entry-points**

- `infrastructure/bicep/parameters/redis-prod.bicepparam` — add `geoReplication` block
- Add `Microsoft.Cache/redis/linkedServers` Bicep child resource (links secondary to primary)
- Extend `scripts/Deploy-RedisCache.ps1` — `-Region` + `-LinkedReplica` params
- New runbook section in `docs/guides/redis-cache-azure-setup.md` — forced-failover procedure, RTO/RPO measurements, client-side reconnect verification

**Suggested fix**

Gate on DEF-001 (Premium SKU is the prereq). If DEF-005 (Managed Redis) is chosen for prod, this becomes dramatically simpler — active-active is built-in.

**Estimated effort**: 1-2 days infra + 1 day cross-region failover testing window with stakeholders
**Blockers**: DEF-001 (Premium SKU)
**Related**: DEF-005, ADR-009

---

### DEF-004 — Plain-text secret remediation (non-Redis)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-25 (migrated 2026-06-26) |
| **Source** | spec Out of Scope; partial overlap with sister project `spaarke-ai-azure-setup-dev-r1` |
| **GitHub Issue** | [#465](https://github.com/spaarke-dev/spaarke/issues/465) |

**Description**

`ConnectionStrings__Redis` has been migrated to KV reference syntax by R1. Other plain-text secrets remain in `spaarke-bff-dev` / `spaarke-bff-prod` App Settings that should follow the same pattern. Sister project `spaarke-ai-azure-setup-dev-r1` handles `DocumentIntelligence__AiSearchKey`; this entry picks up everything else.

Concrete failure mode without this: a leaked App Settings export (support ticket, audit log, accidental Slack paste) exposes plain-text secrets directly — KV references reveal only the vault + secret name, requiring MI auth to actually retrieve.

**Entry-points**

- Enumerate: `az webapp config appsettings list -g rg-spaarke-{env} -n spaarke-bff-{env}` + filter entries whose value does NOT start with `@Microsoft.KeyVault(`
- NEW `scripts/Audit-PlainTextSecrets.ps1` to automate the enumeration
- Per secret: create KV secret → update App Setting to `@Microsoft.KeyVault(...)` syntax → verify BFF MI resolves → restart + smoke test
- Document rotation procedure for each newly KV-backed secret

**Suggested fix**

Triage list against secrets that are intentionally plain-text (non-sensitive config) vs secrets that should be in KV. Execute highest-sensitivity first.

**Estimated effort**: 0.5 day per secret × N secrets (N ≈ 3-5 dev, 5-8 prod). Total range 2-7 days.
**Blockers**: coordination with sister project on shared KV usage
**Related**: ADR-028, sister project `spaarke-ai-azure-setup-dev-r1` FR-15

---

### DEF-005 — Evaluate Azure Managed Redis for prod

| Field | Value |
|---|---|
| **Status** | Closed (Won't Fix) — 2026-06-26 |
| **Urgency** | (closed) |
| **Filed** | 2026-06-26 (during Phase 3 cutover) |
| **Source** | User question during Phase 3 dev cutover (architectural review) |
| **GitHub Issue** | [#466 (closed)](https://github.com/spaarke-dev/spaarke/issues/466) |
| **Decision record** | [`projects/spaarke-redis-cache-remediation-r2/notes/managed-redis-decision.md`](../../spaarke-redis-cache-remediation-r2/notes/managed-redis-decision.md) |
| **Closure rationale** | Managed Redis is an enterprise/high-throughput solution; Spaarke is below the scale where its differentiating features (RediSearch vector / RedisJSON / Bloom / active-active geo-rep) pay off. The strongest operational draw was Entra ID auth (DEF-001), but R2 Theme B (key rotation automation) solves the same root problem without the migration cost. Conditions that would trigger a revisit are documented in the decision record. |

**Description**

R1 provisioned dev on **Azure Cache for Redis** (`Microsoft.Cache/Redis`, legacy product, Basic C0). Microsoft has since launched **Azure Managed Redis** (`Microsoft.Cache/redisEnterprise`, Redis Enterprise, GA mid-2025) as the recommended forward path for production workloads. Before any prod provisioning, evaluate Managed Redis vs Azure Cache for Redis Premium.

Concrete failure mode without this: provisioning prod on the legacy product locks Spaarke into a 12+ month migration path later when Managed Redis SLA + features become operationally important (99.999% SLA, built-in active-active geo-replication, all-tier Entra ID auth, RedisJSON/RediSearch modules). DEF-001 + DEF-003 also become harder on the legacy product (both are Premium-tier-only).

**Entry-points**

- Spike: provision `Microsoft.Cache/redisEnterprise` Balanced B0 in sandbox; exercise BFF against it via adapted `Deploy-RedisCache.ps1`
- Decision memo: cost-perf comparison at projected prod traffic + SLA-vs-cost analysis + DEF-001/DEF-003 leverage analysis
- If chosen: new project to author `Microsoft.Cache/redisEnterprise` Bicep module + revise R1 scripts

**Suggested fix**

Spike first (2-3 days), then decision memo. If Managed Redis chosen for prod: ~2 weeks (Bicep module rewrite + script split + ADR-009 revision + prod cutover with finance + security review).

**Estimated effort**: 2-3 days spike + decision; +2 weeks if chosen
**Blockers**: prod-tier sizing requirements must be known; finance review needed
**Decision deadline**: before any prod-tier Redis provisioning happens (sister project + future BFF prod cutovers all depend on this call)
**Related**: DEF-001, DEF-002, DEF-003, ADR-009 (amended)
**References**: https://learn.microsoft.com/en-us/azure/redis/overview

---

### DEF-006 — Rename App Insights `spe-insights-dev-67e2xz` → canonical pattern

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-06-26 (during Phase 3 cutover) |
| **Source** | User-flagged during R1 Phase 3 verification |
| **GitHub Issue** | [#467](https://github.com/spaarke-dev/spaarke/issues/467) |

**Description**

BFF App Insights is `spe-insights-dev-67e2xz` (RG `spe-infrastructure-westus2`) — off-pattern legacy name created alongside `spe-redis-dev-67e2xz` (which THIS R1 project just replaced). Same two-tier resource-naming rule that drove R1's Redis rename applies (NFR-03/10 in amended ADR-009): top-level resources `spaarke-bff-<service>-{env}`.

Concrete failure mode without this: future operators auditing resources by canonical name (`spaarke-bff-*`) miss the BFF App Insights and may provision a duplicate, splitting telemetry across two instances.

**Entry-points**

- Provision `spaarke-bff-insights-dev` (same workspace if Log Analytics-backed; otherwise standalone)
- BFF App Settings: `APPLICATIONINSIGHTS_CONNECTION_STRING` → new instance's connection string
- Restart BFF; verify via KQL `traces | summarize count() by cloud_RoleName` on the new instance
- 24-hr verification window
- Decommission `spe-insights-dev-67e2xz` (tag-then-delete, 7-14 day reversibility per R1 pattern)

**Suggested fix**

Pattern-extend `Deploy-RedisCache.ps1` structure (params, `-WhatIf`, cutover, post-deploy validation) to new `Deploy-AppInsights.ps1`. Or fold this into sister project `spaarke-ai-azure-setup-dev-r1` (already doing canonicalization for AI Search + KV).

**Estimated effort**: 0.5-1 day (much simpler than Redis — no `ITenantCache` analog, no atomic migration; just resource provision + connection-string swap + verify + decommission)
**Blockers**: none
**Related**: R1 amended ADR-009 NFR-03/10

---

## In Progress

*None.*

---

## Closed (Done / Won't Fix / Superseded)

### DEF-007 (placeholder) — R7-S7 OTel→Azure Monitor exporter wiring

**Status**: Done (closed 2026-06-26)
**Outcome**: Shipped inline in R1 — both sub-gaps (RedisCacheOptions.ConnectionMultiplexerFactory + MetricsDistributedCache decorator) closed in commits on `work/spaarke-redis-cache-remediation-r1`. KQL verified: Redis HMGET/UNLINK/CLIENT deps visible + `cache.hits=20 / cache.misses=9 / cache.redis_call_duration_ms=15 records` visible.
**Why kept in the file**: traceability for the decision to ship inline rather than defer.

---

## Notes

- The prior file `notes/r7-backlog.md` is preserved as a historical record of the initial backlog organized by "R7 round" stretch goals (S1-S7). This new file `notes/defer-issues.md` is the canonical tracker going forward and uses the universal DEF-/ISS- IDs per `/project-defer-issue-tracking` skill.
- Migration mapping: S1 → DEF-001, S2 → DEF-002, S3 → DEF-003, S4 → DEF-004, S5 → DEF-005, S6 → DEF-006, S7 → Closed (shipped inline).
