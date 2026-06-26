# Spaarke Redis Cache Remediation (R1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-25
> **Source**: `projects/spaarke-redis-cache-remediation-r1/design.md` (v0.3)
> **Project ID**: `spaarke-redis-cache-remediation-r1`
> **Sister project**: `spaarke-ai-azure-setup-dev-r1` (this project is a PREREQUISITE — see NFR-11)

---

## Executive Summary

Fix the BFF's Redis/cache configuration (`Redis__Enabled = false` drift in dev → in-memory fallback active in a deployed environment), harden ADR-009 with operational guidance it currently lacks (SKU, fail-fast, secret management, tenant isolation, observability), rename to canonical resource naming (`spe-redis-dev-67e2xz` → `spaarke-bff-redis-dev`), and produce repeatable Infrastructure-as-Code (extending existing `redis.bicep`) + canonical documentation so any environment (dev, staging, prod) can be provisioned in <30 min from a runbook.

Architecture 1 (Platform-only Redis) is the resolved direction: one Redis per environment with tenant isolation via mandatory cache-key prefix; wrapper API designed for future multi-Redis without major refactor. This project is a **prerequisite** for `spaarke-ai-azure-setup-dev-r1` (Phase 3 of that project gated on this project's Phase 3 cutover).

---

## Scope

### In Scope

**Phase 1 — CacheModule hardening (code-only, production-like even in dev)**
1. `CacheModule.cs:31` one-line fix: `AbortOnConnectFail = false` → `true` for deployed environments
2. Fail-fast at startup when `Redis:Enabled=true` and Redis unreachable
3. Explicit opt-in for in-memory fallback (`Redis:AllowInMemoryFallback`) with environment guard (`IHostEnvironment.IsDevelopment()`)
4. Null-Object `IConnectionMultiplexer` (`Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs`) — Pub/Sub no-op log; data fetch throws with clear message
5. Tenant-key-isolation wrapper API (`ITenantCache` or `IDistributedCacheTenantExtensions`) — mandatory tenantId parameter, industry-standard key format `tenant:{tenantId}:{resource}:{id}:v{version}`
6. **ALL 117 BFF cache call sites atomic migration** to wrapper (single coordinated PR — prevents read-old/write-new bug class)
7. `Redis:InstanceName` default change: `sdap:` → `spaarke:` (drops deprecated brand; aligns with canonical naming)
8. System-level cache exception allow-list documented in wrapper (feature flags, system config — explicitly justified)
9. Tests for all four CacheModule branches (Redis-on, Redis-off-AllowFallback-Dev, Redis-off-AllowFallback-non-Dev throws, Redis-off-NoFallback throws)

**Phase 2 — Bicep + provisioning artifacts (extend existing, modern format)**
10. **Extend existing** `infrastructure/bicep/modules/redis.bicep` (do NOT duplicate) — verify SKU object parameterization, multi-env naming, KV secret upsert, optional vnet/staticIP/tags
11. Investigate adjacent `.bicepparam` files for drift; identify and fix any inconsistency
12. Author new `.bicepparam` parameter files (modern typed format): `redis-dev.bicepparam`, `redis-staging.bicepparam`, `redis-prod.bicepparam` (or extend existing env-specific files)
13. New `scripts/Deploy-RedisCache.ps1` (extracted from `Provision-Customer.ps1`) — idempotent, multi-env, `-WhatIf`, `-VerifyOnly`, `-CutoverBffSettings`; KV secret upsert; leverages existing `tests/manual/RedisValidationTests.ps1` for post-deploy verification
14. Refactor `scripts/Provision-Customer.ps1` lines 422-487 to call `Deploy-RedisCache.ps1` (removing inline Redis logic); deprecate per-customer Redis provisioning per Q-E Architecture 1

**Phase 3 — Dev environment cutover (canonical naming + production-like)**
15. Provision `spaarke-bff-redis-dev` (Basic C0, `spe-infrastructure-westus2` RG) via new `Deploy-RedisCache.ps1`
16. Upsert `Redis-ConnectionString` in `spaarke-spekvcert` (or appropriate dev KV) with new instance's connection string
17. Update `spaarke-bff-dev` App Settings: `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, `ConnectionStrings__Redis=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)`
18. Restart BFF; verify `/healthz` 200 + startup log `"Distributed cache: Redis enabled with instance name 'spaarke:'"`
19. Smoke test: chat session creation produces a `spaarke:tenant:{tenantId}:session:{id}:v1` key in Redis (verifies tenant prefix invariant)
20. **24-hr** verification window (shortened from 48 hr — legacy is empty, no data to verify); then decommission legacy `spe-redis-dev-67e2xz` (delete OR tag `decommission=2026-07-XX`)

**Phase 4 — Observability**
21. App Insights Redis dependency telemetry wired (verify dependency calls appear in Live Metrics)
22. Custom metrics emitted from cache wrapper: `cache.hits`, `cache.misses`, `cache.hit_rate`, `cache.redis_p95_ms` (with `resource` dimension)
23. Alert definitions documented (in code or `docs/guides/redis-cache-azure-setup.md`): hit rate <80% for 15min; Redis P95 >100ms for 5min; memory >80% of SKU limit

**Phase 5 — Canonical docs + ADR-009 amendments + lessons + R7 backlog**
24. **UPDATE existing** `docs/architecture/caching-architecture.md` (kebab-case, already well-structured ~100+ lines): add Tenant Isolation section (mandatory `tenant:{tenantId}:` prefix on all keys; system-level exception allow-list); add Multi-instance Behavior section (Pub/Sub semantics; in-memory single-instance limitation); add Cache Instance Registry section (Architecture 1 + future multi-Redis extensibility); update Key Conventions section (replace `sdap:` examples with `spaarke:`); update Component table for CacheModule Phase 1 changes; add Failure Mode Catalog beyond "fail-open"
25. **NEW** `docs/guides/redis-cache-azure-setup.md` (kebab-case): operational runbook covering prerequisites, provision commands per environment, verification, cutover protocol (dev empty-Redis vs. staging/prod key-migration options per G13), rollback, secret rotation, decommission, troubleshooting, known limitation (in-memory mode = single-instance only)
26. **UPDATE** ADR-009 in lockstep (both `.claude/adr/ADR-009-redis-caching.md` concise AND `docs/adr/ADR-009-caching-redis-first.md` full): add operational MUSTs (SKU table, KV reference mandate, fail-fast in deployed envs, cache key tenant-prefix format, InstanceName=`spaarke:`, Pub/Sub topology guidance, App Insights mandate, two-tier resource-naming rule)
27. **UPDATE** `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` (existing UPPERCASE file, kept as-is pending broader doc-cleanup): add new §4.5 "Phase 1.5: Redis Cache" before AI Search §4.6 from sister project; add `Deploy-RedisCache.ps1` to Appendix D Script Reference
28. Document secret rotation procedure (rotate primary key → re-upsert KV secret → BFF picks up via KV reference; brief restart window if needed)
29. Capture lessons learned (how the drift originated, what guardrails would have prevented it)
30. Author R7 backlog (S1 Entra ID auth, S2 Pub/Sub separation, S3 multi-region, S4 other secrets deferred to sister project + future cleanup)

### Out of Scope

- **AI Search restoration** — owned by sister project `spaarke-ai-azure-setup-dev-r1`
- **Production Redis provisioning** — this project produces the Bicep + script; prod provisioning is separate go/no-go with finance + security review
- **Migration from admin-key auth to Microsoft Entra ID auth** — deferred to S1 stretch goal (Premium SKU feature)
- **Pub/Sub separation in prod** — deferred to S2 stretch goal
- **`DocumentIntelligence__AiSearchKey` plain-text key migration** — moved to sister project's FR-15 (adjacent secret remediation belongs with AI-Search work)
- **Other plain-text secret remediation** apart from Redis — deferred to S4 stretch goal
- **Multi-region Redis (geo-replication)** — deferred to S3 stretch goal
- **Customer-specific Redis instances** — per Q-E Architecture 1, deprecated from `Provision-Customer.ps1`; future per-customer Redis (if needed for data-residency) registered via wrapper named-instance pattern (small additive change)
- **Renaming existing UPPERCASE doc files** (e.g., `SPAARKE-DEPLOYMENT-GUIDE.md`) — deferred to broader doc-cleanup project; this project keeps existing file names and applies kebab-case only to NEW files

### Project Dependencies

**This project is a PREREQUISITE for `spaarke-ai-azure-setup-dev-r1`.** That project's Phase 3 (Deploy Infrastructure) MUST NOT begin until this project's Phase 3 (Dev environment cutover) completes successfully. The gate signal is this project's Success Criterion #1 (dev BFF startup log shows Redis-enabled with `spaarke:` InstanceName).

### Affected Areas

| Path | Description |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` | Phase 1 — fail-fast + env guard + Null-Object + AbortOnConnectFail fix |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs` | NEW — Null-Object pattern per ADR-032 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/ITenantCache.cs` (or `IDistributedCacheTenantExtensions.cs`) | NEW — tenant-key-isolation wrapper API |
| `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` | UPDATE — InstanceName references + key conventions |
| All 117 BFF files using `IDistributedCache` | UPDATE — atomic migration to wrapper |
| `src/server/api/Sprk.Bff.Api/Configuration/RedisOptions.cs` | UPDATE — `InstanceName` default `sdap:` → `spaarke:`; add `AllowInMemoryFallback` |
| `src/server/api/Sprk.Bff.Api/appsettings.json` + `appsettings.template.json` | UPDATE — InstanceName default change |
| `infrastructure/bicep/modules/redis.bicep` | EXTEND — verify parameterization, address gaps |
| `infrastructure/bicep/parameters/redis-dev.bicepparam` | NEW (or extend existing dev.bicepparam) |
| `infrastructure/bicep/parameters/redis-staging.bicepparam` | NEW |
| `infrastructure/bicep/parameters/redis-prod.bicepparam` | NEW |
| `scripts/Deploy-RedisCache.ps1` | NEW — extracted from Provision-Customer.ps1 |
| `scripts/Provision-Customer.ps1` lines 422-487, 1437 | REFACTOR — call Deploy-RedisCache.ps1; deprecate per-customer Redis |
| `tests/manual/RedisValidationTests.ps1` | EXTEND — add tenant-isolation key-format check, fail-fast assertion |
| `docs/architecture/caching-architecture.md` | UPDATE — major additions per FR-24 |
| `docs/guides/redis-cache-azure-setup.md` | NEW (kebab-case) — operational guide |
| `.claude/adr/ADR-009-redis-caching.md` | UPDATE — operational MUSTs |
| `docs/adr/ADR-009-caching-redis-first.md` | UPDATE — operational MUSTs (in lockstep) |
| `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` | UPDATE — add §4.5 + Appendix D entry |
| `src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs` | UPDATE — symmetric DI registration verification per ADR-032 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupJob.cs` | UPDATE — verify resolves correctly with Null-Object multiplexer |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` | UPDATE — verify resolves correctly with Null-Object multiplexer |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipCacheInvalidator.cs` + `MembershipCacheInvalidationSubscriber.cs` | UPDATE — verify Pub/Sub no-op semantics |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs` | NEW or extend — coverage for 4 branches |

---

## Requirements

### Functional Requirements

#### Phase 1 — CacheModule hardening

1. **FR-01** — `CacheModule.cs:31` `AbortOnConnectFail = false` → `true` for deployed environments.
   **Acceptance**: One-line fix verified; in deployed env (non-Development), startup connection failure throws immediately rather than silently swallowing.

2. **FR-02** — Fail-fast at startup when Redis is configured but unreachable.
   **Acceptance**: When `Redis:Enabled=true` and `ConnectionMultiplexer.Connect()` throws (or AbortOnConnectFail triggers), BFF startup fails with clear error message naming the connection string source (Key Vault secret URI vs App Setting fallback) and what operator should check.

3. **FR-03** — Explicit opt-in for in-memory fallback (`Redis:AllowInMemoryFallback`) with environment guard.
   **Acceptance**:
   - `Redis:Enabled=false` + `AllowInMemoryFallback=true` + `IHostEnvironment.IsDevelopment()` = true → register in-memory `IDistributedCache` + Null-Object `IConnectionMultiplexer`
   - `Redis:Enabled=false` + `AllowInMemoryFallback=true` + NOT Development → **THROW at startup** with message "AllowInMemoryFallback is restricted to Development environments. Set Redis:Enabled=true for ASPNETCORE_ENVIRONMENT={env}"
   - `Redis:Enabled=false` + `AllowInMemoryFallback` not set → THROW at startup
   - Tests cover all three paths

4. **FR-04** — Null-Object `IConnectionMultiplexer` (`Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs`) per ADR-032.
   **Acceptance**: Implements `IConnectionMultiplexer` with safe no-op semantics:
   - `GetSubscriber().Publish(...)` = no-op log entry
   - `GetSubscriber().Subscribe(...)` = no-op subscription (never delivers; documented as multi-instance limitation in operational guide)
   - `GetDatabase()` returns a stub that throws `NotSupportedException` with message "In-memory cache mode does not support direct Redis database operations. Use IDistributedCache."
   - Registered SYMMETRICALLY with real `IConnectionMultiplexer` per ADR-032 (no asymmetric anti-pattern)
   - Tests verify Pub/Sub no-op semantics + direct database access throws

5. **FR-05** — Tenant-key-isolation wrapper API.
   **Acceptance**: New `ITenantCache` (or `IDistributedCacheTenantExtensions`) public methods require a tenant ID parameter. Internal key construction: `tenant:{tenantId}:{resource}:{id}:v{version}` (Redis InstanceName `spaarke:` prepends → final key `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`). Includes `GetAsync`, `SetAsync`, `RemoveAsync`, `GetOrCreateAsync` variants. Future-extensible signature: `cacheInstance: "default"` parameter (today only "default" registered; future named instances additive). Tests prove keys carry tenant scope.

6. **FR-06** — ALL 117 BFF cache call sites atomic migration.
   **Acceptance**: Single coordinated PR migrates every direct `IDistributedCache.GetAsync/SetAsync/RemoveAsync` call in `src/server/api/Sprk.Bff.Api/` to use `ITenantCache` wrapper. Grep verification: `grep -r "IDistributedCache\." src/server/api/Sprk.Bff.Api/` returns ZERO direct calls outside the wrapper and its tests. System-level cache exceptions (feature flags, system config) explicitly allow-listed with JSON comment justification.

7. **FR-07** — `Redis:InstanceName` default change from `sdap:` to `spaarke:`.
   **Acceptance**: Default updated in `appsettings.json`, `appsettings.template.json`, `RedisOptions.cs` (or its consumer), and any Bicep param files setting this value. Dev cutover uses new value; dev Redis is empty so no migration cost. Documented in `caching-architecture.md` Key Conventions section.

8. **FR-08** — Test coverage for all four `CacheModule` scenarios.
   **Acceptance**: New unit tests in `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs` cover: Redis-on (real Connection + Null DI symmetry), Redis-off-AllowFallback-Dev (in-memory + Null-Object), Redis-off-AllowFallback-non-Dev (throws), Redis-off-NoFallback (throws). All pass via `dotnet test`.

#### Phase 2 — Bicep + provisioning artifacts

9. **FR-09** — Extend existing `infrastructure/bicep/modules/redis.bicep` (do NOT duplicate).
   **Acceptance**: Existing module reviewed; parameterization verified for: `name`, `location`, `sku` (object: `name`, `family`, `capacity`), `minimumTlsVersion` (default `"1.2"`), `enableNonSslPort` (default `false`), `redisVersion`, optional `vnetSubnetId`, optional `staticIP`, optional `tags`. Outputs include: `name`, `hostName`, `sslPort`, resource ID, primary key. Any missing parameter/output added; no new module file created.

10. **FR-10** — Investigate adjacent `.bicepparam` files for drift; identify and fix.
    **Acceptance**: All files in `infrastructure/bicep/parameters/` reviewed; inconsistencies (especially anything Redis-related or naming-convention-related) documented in PR description and fixed. Dev/staging/prod params reconciled.

11. **FR-11** — New `Deploy-RedisCache.ps1` script extracted from `Provision-Customer.ps1`.
    **Acceptance**: Script exists at `scripts/Deploy-RedisCache.ps1`. Parameters: `-Environment {dev|staging|prod}`, `-WhatIf`, `-VerifyOnly`, `-CutoverBffSettings`. Idempotent (check existence; deploy via `az deployment group create`). Upserts primary key into appropriate Key Vault under `Redis-ConnectionString` (full StackExchange-compatible connection string format). Post-deploy verification leverages `tests/manual/RedisValidationTests.ps1` (extends if needed; does NOT rewrite). Returns non-zero exit on any failure.

12. **FR-12** — Refactor `Provision-Customer.ps1` to call `Deploy-RedisCache.ps1`; deprecate per-customer Redis.
    **Acceptance**: `Provision-Customer.ps1` lines 422-487 (Redis provisioning block) replaced with call to `Deploy-RedisCache.ps1`. Per-customer Redis logic removed (per Q-E Architecture 1). Documentation in script header notes the deprecation and the path forward if a customer ever needs dedicated Redis (register via wrapper named-instance pattern).

#### Phase 3 — Dev environment cutover

13. **FR-13** — Provision `spaarke-bff-redis-dev` via new deploy script.
    **Acceptance**: `Deploy-RedisCache.ps1 -Environment dev` runs successfully; `az redis show -g spe-infrastructure-westus2 -n spaarke-bff-redis-dev` returns Basic C0, running state.

14. **FR-14** — KV secret + dev BFF app settings cutover.
    **Acceptance**:
    - `Redis-ConnectionString` secret in `spaarke-spekvcert` (or appropriate dev KV) populated with new instance's connection string
    - Dev BFF app settings updated: `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, `ConnectionStrings__Redis=@Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)`, `Redis__AllowInMemoryFallback=false` (explicit)
    - BFF restarted; `/healthz` returns 200; startup log contains `"Distributed cache: Redis enabled with instance name 'spaarke:'"`; NO in-memory warning line
    - Smoke test: chat session creation produces a `spaarke:tenant:{tenantId}:session:{id}:v1` key in Redis (verified via `az redis cli` or App Insights dependency telemetry)

15. **FR-15** — Decommission legacy `spe-redis-dev-67e2xz` after 24-hr verification.
    **Acceptance**: After 24-hr verification window of green dev BFF operation, legacy resource either DELETED OR tagged with `decommission=YYYY-MM-DD`. Decision documented in cutover record.

#### Phase 4 — Observability

16. **FR-16** — App Insights Redis dependency telemetry + custom metrics.
    **Acceptance**:
    - App Insights captures Redis dependency calls (verified in Live Metrics within 5 min of post-cutover traffic)
    - Custom metrics emitted from cache wrapper: `cache.hits`, `cache.misses`, `cache.hit_rate`, `cache.redis_p95_ms` with `resource` dimension
    - Metrics visible in Application Insights metrics explorer

17. **FR-17** — Alert definitions documented and (where applicable) deployed.
    **Acceptance**: Three alerts defined: (a) hit rate <80% for 15 min → "cache key/version drift; investigate"; (b) Redis P95 >100 ms for 5 min → "network issue or SKU undersize"; (c) memory >80% of SKU limit → "scale to next SKU". Definitions in `docs/guides/redis-cache-azure-setup.md` Troubleshooting section + (optionally) deployed via Bicep alert rules or App Insights workbook.

#### Phase 5 — Canonical docs + ADR amendments

18. **FR-18** — UPDATE existing `docs/architecture/caching-architecture.md`.
    **Acceptance**: Substantial additions to existing well-structured doc:
    - New Tenant Isolation section: mandatory `tenant:{tenantId}:` prefix; system-level exception allow-list; rationale (multi-tenant invariant)
    - New Multi-instance Behavior section: Pub/Sub fan-out via Redis; in-memory mode single-instance limitation
    - New Cache Instance Registry section: Architecture 1 today (single "default" instance); future multi-Redis extensibility pattern
    - Update Key Conventions section: replace `sdap:` examples with `spaarke:`; update key-format examples to include `tenant:` prefix
    - Update Component table: describe Phase 1 CacheModule changes (fail-fast, AbortOnConnectFail, env-guarded AllowFallback, Null-Object)
    - New Failure Mode Catalog section: Redis unreachable in deployed env = startup fail; Pub/Sub degraded = stale-cache risk on multi-instance; SKU undersize = P95 latency degradation (with alert thresholds)
    - Cross-link to new `redis-cache-azure-setup.md` operational guide
    - Last-updated date refreshed

19. **FR-19** — NEW `docs/guides/redis-cache-azure-setup.md` (kebab-case).
    **Acceptance**: New operational guide with sections: Prerequisites (KV exists, MI has read perm, App Service exists, App Settings template loaded), Provision Command (per environment with expected output), Verification commands, Cutover Protocol (dev = empty Redis clean slate; staging/prod = G13 key-migration options with key-warming or cache-miss-window choice), Rollback procedure, Secret Rotation procedure, Decommission procedure, Troubleshooting (connection failures, latency spikes, hit-rate degradation), Known Limitation note ("in-memory fallback mode does NOT support multi-instance deployment — single-instance local dev only"). A fresh operator can provision a new env Redis end-to-end in <30 min from this doc alone.

20. **FR-20** — UPDATE ADR-009 in lockstep (concise + full).
    **Acceptance**: Both `.claude/adr/ADR-009-redis-caching.md` (concise) AND `docs/adr/ADR-009-caching-redis-first.md` (full) updated with NEW operational MUSTs:
    - SKU table (dev=Basic C0, staging=Standard C0+, prod=Standard C2+ or Premium P1+)
    - Connection string MUST come from Key Vault via `@Microsoft.KeyVault(SecretUri=...)` reference
    - Fail-fast at startup when Redis configured but unreachable in deployed environments
    - Cache key MUST embed tenant ID with industry-standard format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}`
    - `InstanceName` MUST be `spaarke:` (canonical app prefix)
    - Pub/Sub MAY share Redis with cache in dev/staging; SHOULD be separated in prod (S2 stretch)
    - App Insights MUST capture Redis dependency calls; minimum custom metrics + alerts listed
    - Two-tier resource-naming rule (top-level env-suffixed `spaarke-bff-redis-{env}`; sub-resources env-agnostic — cache keys)
    Both files updated in the same PR. Last-updated date refreshed on both.

21. **FR-21** — UPDATE `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` with new §4.5.
    **Acceptance**: New §4.5 "Phase 1.5: Redis Cache" inserted between §4 Phase 1 (Azure Infrastructure) and the AI Search §4.6 (from sister project, if/when added). Contains: brief context paragraph, code block invoking `Deploy-RedisCache.ps1 -EnvironmentName <env>`, cross-link to `caching-architecture.md` + `redis-cache-azure-setup.md`. Appendix D Script Reference includes `Deploy-RedisCache.ps1`. Existing file keeps UPPERCASE name (pending broader doc-cleanup).

22. **FR-22** — R7 backlog + lessons-learned + secret-rotation docs.
    **Acceptance**:
    - Secret rotation procedure documented in `redis-cache-azure-setup.md` (rotate primary key in Azure portal → re-upsert KV secret → BFF picks up via KV reference; brief restart window only if needed)
    - Lessons-learned section in operational guide capturing: how the drift originated (Redis deleted; App Setting left at `false`; in-memory warning ignored for unknown duration), what guardrails would have prevented (fail-fast in deployed envs, alert on in-memory warning, deployment checklist explicitly verifies Redis state)
    - R7 backlog file (or section in project notes): S1 (Entra ID auth via MI + "Redis Data Owner" role assignment), S2 (Pub/Sub separation in prod), S3 (multi-region geo-replication), S4 (other secrets migration — partially in sister project, more remain)

### Non-Functional Requirements

- **NFR-01** — `Deploy-RedisCache.ps1` MUST be idempotent (re-running against an environment where Redis already exists is safe; no destructive side effects without explicit `-Force`)
- **NFR-02** — Post-deploy verifier (using `RedisValidationTests.ps1`) MUST fail-fast on key invariants (vector dim, tenant prefix presence, fail-fast assertion); returns non-zero exit code
- **NFR-03** — Sub-resources (cache keys, Key Vault secret names) MUST be environment-agnostic (`spaarke:tenant:...` keys, `Redis-ConnectionString` secret name) — environment is implicit in the parent Redis service hostname
- **NFR-04** — BFF code changes net publish-size delta MUST be ≤+1 MB compressed (per ADR-029 + CLAUDE.md §10 binding rule). This project adds: 1 new file (NullConnectionMultiplexer.cs ~50 LOC), 1 new file (ITenantCache.cs ~100 LOC), edits to ~117 cache-call sites (string changes, no new types). Expected delta: ≤+0.1 MB
- **NFR-05** — Prod and demo environments MUST remain unchanged throughout this project. Deploy script Environment parameter must reject `prod` and `demo` values (or explicit `-Force` flag with warning) during this project's execution
- **NFR-06** — `Deploy-RedisCache.ps1` MUST support `-DryRun` (show what would deploy without changes) and `-VerifyOnly` (run only post-deploy invariant checks against existing instance)
- **NFR-07** — ALL 117 cache call site migration to wrapper MUST be a single coordinated PR (atomic). Partial migration (some sites using wrapper, others using direct `IDistributedCache`) is prohibited. Reason: read-old/write-new bug class
- **NFR-08** — System-level cache exceptions (non-tenant-scoped keys) MUST be explicitly documented in the wrapper with JSON comment justification per call site. Default for any new cache call is tenant-scoped
- **NFR-09** — `AbortOnConnectFail = true` in deployed environments. Only `false` allowed in Development with explicit `Redis:AllowInMemoryFallback=true`
- **NFR-10** — Naming policy (top-level resource env-suffix vs sub-resource env-agnostic) MUST be documented as canonical in `caching-architecture.md` and `redis-cache-azure-setup.md`. Future environment provisioning MUST follow this rule
- **NFR-11** — **CRITICAL Project Sequencing**: This project's Phase 3 (dev cutover) MUST complete successfully BEFORE `spaarke-ai-azure-setup-dev-r1` begins its Phase 3 (Deploy Infrastructure). Sister project's NFR-13 codifies this dependency. Cross-reference: this project's Success Criterion #1 is the gate signal for sister project
- **NFR-12** — Wrapper API designed for future multi-Redis without major refactor. Specifically: `ITenantCache.GetAsync(tenantId, resource, id, cacheInstance: "default")` — `cacheInstance` parameter accepted today (defaults to "default"; only "default" registered) so future named instances are an additive change, not a redesign
- **NFR-13** — Test coverage MUST include all four `CacheModule` scenarios per FR-08. No scenario may be skipped. Tests must run as part of `dotnet test` in CI

---

## Technical Constraints

### Applicable ADRs

- **ADR-009** (Redis-First Caching) — **being amended by this project** (FR-20). Current MUSTs preserved; new operational MUSTs added.
- **ADR-010** (DI Minimalism) — `IConnectionMultiplexer` Null-Object justification (interface exists for runtime swap; symmetric registration prevents asymmetric anti-pattern). `ITenantCache` wrapper as a new interface justified by ≥2 future implementations (default + future named instances).
- **ADR-013** (AI services bounded concurrency) — relevant for `GraphTokenCache` + `EmbeddingCache` (which use `IDistributedCache`) interaction with rate-limited AI services. Migration to `ITenantCache` wrapper MUST preserve existing `SemaphoreSlim` patterns.
- **ADR-028** (Spaarke Auth v2) — Redis connection string MUST be in Key Vault, referenced via `@Microsoft.KeyVault(VaultName=...;SecretName=...)` syntax. App Service Managed Identity reads the secret. No plain-text connection strings in App Settings.
- **ADR-029** (BFF Publish Hygiene) — publish-size verification required (NFR-04: ≤+1 MB compressed delta). Run `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, measure compressed output, report absolute size + diff in PR description.
- **ADR-032** (BFF Null-Object Kill-Switch) — `IConnectionMultiplexer` Null-Object pattern MUST follow symmetric DI registration (real + Null-Object both register `IConnectionMultiplexer`; selection by config). No asymmetric anti-pattern (registering only when a flag is on, leaving dependents broken when off).

### MUST Rules (binding)

- ✅ MUST use canonical resource naming `spaarke-bff-redis-{env}` (top-level env-suffix)
- ✅ MUST use env-agnostic cache keys: `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`
- ✅ MUST migrate ALL 117 cache call sites atomically (single PR)
- ✅ MUST use Key Vault references for Redis connection string (no plain text in App Settings)
- ✅ MUST fail-fast at startup in deployed environments when Redis is configured-but-unreachable
- ✅ MUST register `IConnectionMultiplexer` symmetrically (real or Null-Object); no asymmetric anti-pattern
- ✅ MUST update both `.claude/adr/ADR-009-redis-caching.md` AND `docs/adr/ADR-009-caching-redis-first.md` in lockstep
- ✅ MUST extend existing `infrastructure/bicep/modules/redis.bicep` (not duplicate)
- ✅ MUST leverage existing `tests/manual/RedisValidationTests.ps1` (extend; do not rewrite)
- ✅ MUST verify publish-size delta per CLAUDE.md §10 (NFR-04)
- ✅ MUST justify any new BFF interface or service per CLAUDE.md §10 three-question template (Existing / Extension / Cost-of-doing-nothing)
- ❌ MUST NOT touch prod or demo environments during this project's execution
- ❌ MUST NOT introduce new BFF endpoints, services beyond the cache wrapper, DI registrations beyond CacheModule changes, or new packages
- ❌ MUST NOT allow silent in-memory fallback in deployed environments (Production/Staging — only Development with explicit opt-in)
- ❌ MUST NOT allow direct `IDistributedCache.GetAsync/SetAsync/RemoveAsync` calls outside the wrapper after migration (system-level exceptions explicitly allow-listed with justification)
- ❌ MUST NOT cache authorization decisions (existing ADR-009 MUST NOT — preserved)
- ❌ MUST NOT add L1 in-process cache layer on top of Redis without ADR amendment + profiling proof (existing ADR-009 MUST NOT — preserved)
- ❌ MUST NOT recreate per-customer Redis in `Provision-Customer.ps1` (deprecated per Q-E Architecture 1)
- ❌ MUST NOT rename existing UPPERCASE doc files in this project (deferred to broader doc-cleanup); only NEW files use kebab-case

### Existing Patterns to Follow

- **Null-Object pattern**: `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/NullMembershipCacheInvalidator.cs` — mirror for `NullConnectionMultiplexer`
- **CacheModule structural template**: Current `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` — preserve existing Redis-on path; add env-guarded fallback + Null-Object registration
- **Bicep deploy script template**: `infrastructure/ai-search/deploy-session-files-index.ps1` — post-deploy invariant verification pattern (lines 204-215)
- **KV reference pattern**: `spaarke-bff-prod` App Settings — `ConnectionStrings__Redis = @Microsoft.KeyVault(VaultName=...;SecretName=...)`. Apply same pattern to dev BFF
- **Existing `caching-architecture.md`**: well-structured doc with Component table, Cache Types section, TTL Tiers table, Key Conventions, Invalidation Patterns, Data Flow. UPDATE additions append to this structure
- **Existing `Provision-Customer.ps1`**: step-based scripted orchestrator with idempotency markers; new `Deploy-RedisCache.ps1` follows the same pattern (idempotent, resumable, `-WhatIf` aware)

---

## Success Criteria

1. [ ] **Dev BFF startup log** shows `"Distributed cache: Redis enabled with instance name 'spaarke:'"` with NO in-memory warning. Verify: `az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev` immediately after Phase 3 restart.
2. [ ] **Dev BFF App Settings** reference Key Vault for Redis connection string (no plain text). Verify: `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev --query "[?contains(name, 'Redis')]"` shows `@Microsoft.KeyVault(...)` syntax for `ConnectionStrings__Redis`.
3. [ ] **ADR-009 amended** in both `.claude/adr/` and `docs/adr/` (in lockstep) with all new operational MUSTs (per FR-20). Verify: diff both files; both show updated Last Updated date + new constraints sections.
4. [ ] **`CacheModule.cs`** fails startup with clear error when Redis is configured but unreachable in deployed environment. `AbortOnConnectFail = true` confirmed. Verify: integration test that simulates unreachable Redis and asserts startup failure with correct error message.
5. [ ] **Bicep module `redis.bicep` extended** (not duplicated); parameter files for dev/staging/prod committed using `.bicepparam`; adjacent `.bicepparam` drift identified and fixed. Verify: `find infrastructure/bicep -name "*.bicepparam"` shows new files; `git log` shows extension (not new) of `redis.bicep`.
6. [ ] **NEW `docs/guides/redis-cache-azure-setup.md`** (kebab-case) is the canonical operational reference; a new operator can provision a new env Redis end-to-end in <30 min from the doc. Verify: file exists; review against FR-19 acceptance criteria; (post-project) operator dry-run from doc alone.
7. [ ] **EXISTING `docs/architecture/caching-architecture.md` UPDATED** with tenant isolation, multi-instance behavior, instance registry, failure mode catalog, and Phase 1 CacheModule changes (per FR-18 + G12). Verify: diff shows additions to existing structure; `git log` shows update (not new).
8. [ ] **App Insights captures Redis dependency calls**; cache hit rate metric visible in dashboards. Verify: Application Insights Live Metrics shows Redis dependencies post-cutover.
9. [ ] **Tenant-key-isolation helper used in ALL 117 BFF cache call sites** (atomic migration per FR-06 + NFR-07); tests prove keys carry tenant scope; code-review checklist enforces wrapper-only access on future PRs. Verify: `grep -r "IDistributedCache\." src/server/api/Sprk.Bff.Api/` returns ZERO matches outside wrapper + tests.
10. [ ] **`Redis:InstanceName` changed** from `sdap:` to `spaarke:` across appsettings, template, Bicep params (per FR-07). Verify: `grep -r "sdap:" src/server/api/Sprk.Bff.Api/` returns ZERO matches; cache keys in production format `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`.
11. [ ] **R7 backlog** captures S1-S4 deferred items (per FR-22). Verify: backlog doc or notes section exists.
12. [ ] **Legacy `spe-redis-dev-67e2xz`** decommissioned (or tagged with `decommission=YYYY-MM-DD`) after 24-hr verification window. Verify: `az redis show` returns NotFound OR tag list shows `decommission` tag.
13. [ ] **BFF publish size delta ≤+1 MB** compressed (per NFR-04, ADR-029, CLAUDE.md §10). Verify: `dotnet publish -c Release` before/after + compressed-size comparison reported in PR.
14. [ ] **`dotnet test` passes**; new `CacheModule` tests cover all four scenarios (Redis-on, Redis-off-AllowFallback-Dev, Redis-off-AllowFallback-non-Dev throws, Redis-off-NoFallback throws). Verify: test class `CacheModuleTests` exists with ≥4 test methods covering required scenarios.
15. [ ] **`SPAARKE-DEPLOYMENT-GUIDE.md` §4.5** added with `Deploy-RedisCache.ps1` invocation; Appendix D updated (per FR-21). Verify: file inspection.
16. [ ] **Sister project handoff confirmed**: `spaarke-ai-azure-setup-dev-r1` design.md + spec.md reference this project as prerequisite (after AI Search rescoping prompt applied). This project's success criterion 1 is the documented gate signal. Verify: cross-references exist in sister project's spec.md NFR-13.

---

## Dependencies

### Prerequisites
- `spaarke-bff-dev` App Service exists and is currently Running on P1v3 plan (confirmed 2026-06-25)
- `spaarke-spekvcert` Key Vault (or appropriate dev KV) accessible with admin secret access for adding `Redis-ConnectionString`
- App Service Managed Identity has read access to KV secrets (verified during deployment)
- Azure CLI logged in with permissions to: create/delete Redis instances, modify Key Vault secrets, modify App Service app settings, in `spe-infrastructure-westus2` + `rg-spaarke-dev` resource groups
- `infrastructure/bicep/modules/redis.bicep` exists (verified 2026-06-25)
- `tests/manual/RedisValidationTests.ps1` exists (verified 2026-06-25)

### External Dependencies
- None — all work is within the Spaarke repo + existing Azure subscription
- (Future, out of scope) S1 stretch goal would require Premium Redis SKU for Entra ID auth

### Downstream Dependents
- **`spaarke-ai-azure-setup-dev-r1`** Phase 3 cannot begin until this project's Phase 3 completes (per NFR-11)

---

## Owner Clarifications

*All 14 Q&A items resolved during design iteration 2026-06-25; captured here for traceability:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Q1 SKU | Dev Redis SKU? | Basic C0 (~$15/mo, acceptable for dev) | Phase 2 dev.bicepparam |
| Q2 Rename | Rename legacy in place? | Azure Redis cannot be renamed; provision new + decommission | Phase 3 cutover approach |
| Q3 Verification window | How long before decommissioning legacy? | **24 hr** (shortened from 48 hr — legacy is empty, no data to verify) | FR-15 |
| Q4 Adjacent secrets | `DocumentIntelligence__AiSearchKey` in scope? | **OUT of scope** here; moved to sister project `spaarke-ai-azure-setup-dev-r1` | §Out of Scope |
| Q5 Fallback flag scope | App Setting or launch-profile? | **App Setting + environment guard** (`if env.IsDevelopment()`) — cleaner + impossible to misconfigure | FR-03 |
| Q6 ADR approval | Owner-direct vs PR review? | PR review checklist (default; owner can override) | FR-20 process |
| Q7 Phase 4 separation | Merge with Phase 3 or separate? | **Separate** (observability hardening is independent) | §Phasing |
| Q8 Tenant-isolation migration sites | 5 representative? | **ALL 117 sites atomic migration** (not "5 representative" — read/write code must be consistent across entire codebase to prevent read-old/write-new bug class) | FR-06 + NFR-07 |
| Q9 Null-Object namespace | `NullObjects` namespace or co-located? | **`Sprk.Bff.Api.Infrastructure.Cache.NullObjects`** namespace | FR-04 |
| Q10 Prod budget | Specific prod SKU + budget? | Document range in operational guide; defer specific selection to finance review when prod provisioning scoped | §Out of Scope (Phase 5 docs only) |
| Q-A Key format | Industry standard? Old format preservation? | Adopt industry standard `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}`; drop `sdap:` brand for `spaarke:`; tenant prefix MANDATORY; ALL sites atomic | FR-05, FR-06, FR-07 |
| Q-B Multi-instance limitation | Document or build? | **Document only** (one sentence in operational guide); NOT overengineered — in-memory mode is local-dev-single-instance by design | FR-19 known-limitation note |
| Q-C Production-like in dev | Setup as prod? | YES — dev configured production-like (AbortOnConnectFail=true, fail-fast, no shortcuts) so protocols developed here apply to staging/prod | FR-01, FR-02, FR-09 |
| Q-D Bicep param format | Modern .bicepparam? | YES; investigate adjacent files for drift + fix | FR-10 |
| Q-E Multi-Redis architecture | Platform-only / Customer-only / Both? | **Architecture 1 (Platform-only)** with future-extensible wrapper API; per-customer Redis from Provision-Customer.ps1 deprecated | NFR-12, FR-12 |
| Q-F Naming convention | UPPER or kebab? | **kebab-case** (industry standard for docs); existing UPPERCASE files keep names pending broader doc-cleanup | FR-19, FR-25 (NEW files), preserved (existing files) |

---

## Assumptions

*Items where no explicit owner direction was given; proceeding with stated assumptions:*

- **Dev KV name**: Assuming `spaarke-spekvcert` (per `caching-architecture.md` and `CONFIGURATION-MATRIX.md` references) is the canonical dev Key Vault. Verify during Phase 3 cutover; if different KV (e.g., `sprkspaarkedev-aif-kv`), adjust app setting reference.
- **Per-customer Redis decommission**: Assuming `spaarke-{customer}-{env}-cache` instances created by past `Provision-Customer.ps1` runs are either already deleted (demo) or empty/unused. If any active customer is depending on per-customer Redis, FR-12 deprecation needs explicit migration plan for that customer (not anticipated; would surface during Phase 2 investigation).
- **System-level cache exception count**: Assuming small (<10) set of system-level cache keys that legitimately need to be non-tenant-scoped (feature flags, system config). Each documented with JSON comment per NFR-08. If during FR-06 migration the count exceeds 20, escalate for architecture review.
- **Existing `RedisValidationTests.ps1` extensibility**: Assuming the existing harness can be extended with tenant-isolation key-format check + fail-fast assertion without major rewrite. If structural changes are needed, document in Phase 4 PR.
- **App Insights connection already configured**: Assuming dev BFF has `APPLICATIONINSIGHTS_CONNECTION_STRING` set and App Insights resource is provisioned. Verify during Phase 4; if not configured, FR-16 requires provisioning App Insights first (small additive work).
- **`redis-dev.bicepparam` vs extending existing `dev.bicepparam`**: Assuming a dedicated `redis-dev.bicepparam` is cleaner (one Bicep deploy per concern) than extending the existing `dev.bicepparam` (which deploys broader infrastructure). Verify against existing convention during Phase 2 investigation; if extending is the pattern, follow that.
- **Alert deployment mechanism**: Assuming FR-17 alerts are documented in `redis-cache-azure-setup.md` first; whether deployed via Bicep `alerts.bicep` extension or App Insights workbook is a Phase 4 implementation choice (both valid).
- **Migration of `GraphTokenCache`, `EmbeddingCache`, `GraphMetadataCache`, `CachedAccessDataSource`**: These are existing cache wrappers that use `IDistributedCache` internally. Assuming FR-06 atomic migration touches their internal `IDistributedCache` calls (to use `ITenantCache` wrapper) — these wrappers themselves continue to expose their existing public APIs to consumers. If a wrapper's public API needs to change to expose tenant ID (because consumers don't have it), escalate during migration.

---

## Unresolved Questions

*Non-blocking; may arise during execution:*

- [ ] **Will any existing `sprk_*` system-level cache key need to be exempt from the tenant-prefix rule?** Investigation during FR-06: search for keys without obvious tenant scope (e.g., `sprk:metadata:*`, `sprk:config:*`). If any exist, decide: keep as system-level exception with justification OR refactor to derive a tenant context.
- [ ] **`redis.bicep` parameter completeness** — Phase 2 investigation will determine if module needs additional parameters (e.g., diagnostic settings forwarding, private endpoint integration). If extension is non-trivial, document in PR.
- [ ] **Should alert thresholds be tighter for prod?** FR-17 thresholds (80% hit rate, 100ms P95, 80% memory) are sensible defaults. Prod may want tighter (90% hit rate, 50ms P95). Document range; finalize during prod provisioning project (out of scope here).
- [ ] **`GraphTokenCache` 95% target hit rate** mentioned in `caching-architecture.md` — does FR-16 custom metric collection need to support per-cache-type SLO tracking? If yes, add dimension to `cache.hit_rate` metric. Lightweight enough to include now.

---

*AI-optimized specification. Original design: `projects/spaarke-redis-cache-remediation-r1/design.md` v0.3 (2026-06-25)*
