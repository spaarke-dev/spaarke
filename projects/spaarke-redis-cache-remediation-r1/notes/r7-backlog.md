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

## Cross-References

- **Spec source**: [`spec.md`](../spec.md) FR-22 (fifth bullet) + Out of Scope section
- **Sister project**: `spaarke-ai-azure-setup-dev-r1` (S4 partial overlap; coordinate KV usage)
- **ADR-009 amended**: [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) + [`docs/adr/ADR-009-caching-redis-first.md`](../../docs/adr/ADR-009-caching-redis-first.md) (S2 Pub/Sub separation guidance "SHOULD in prod" lives here)
- **Operational guide**: [`docs/guides/redis-cache-azure-setup.md`](../../docs/guides/redis-cache-azure-setup.md) (lessons-learned, secret-rotation procedures, failure-mode catalog — S2/S3/S4 will extend)
- **Cache wrapper API**: `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/ITenantCache.cs` `cacheInstance` parameter (NFR-12 future-extensibility — S2 will leverage)

---

*Backlog captured per FR-22 fifth bullet. Each item is non-binding; effort estimates are planning hints, not commitments. Reassess at R7 kickoff.*
