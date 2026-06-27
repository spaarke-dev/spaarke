# Spaarke Redis Cache Remediation (R1) — design.md

> **Status**: Draft for `/design-to-spec` — v0.2 (updated 2026-06-25 with Q&A + alignment with `spaarke-ai-azure-setup-dev-r1` naming conventions)
> **Author**: Operator-driven, with Claude Code investigation 2026-06-25
> **Target worktree**: `c:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1\` (to be created)
> **Target branch**: `work/spaarke-redis-cache-remediation-r1`
> **Related ADRs to amend**: ADR-009 (concise + full, both)
> **Sister project**: `spaarke-ai-azure-setup-dev-r1` (this project is a PREREQUISITE to that one — Redis must be live + canonical before the AI Search work cuts over the dev BFF app settings)
> **Naming conventions**: This project adopts the two-tier rule established in `spaarke-ai-azure-setup-dev-r1`/design.md — TOP-LEVEL Azure resources env-suffixed (`spaarke-bff-redis-dev`); SUB-RESOURCES (cache keys, KV secret names) env-agnostic. See §4 Hard Constraints.

---

## 1. Background

The Spaarke BFF (`Sprk.Bff.Api`) uses `IDistributedCache` for cross-request caching across **77 source files** — chat sessions, OBO tokens, playbook/consumer routing lookups, knowledge retrieval, and Pub/Sub fan-out (e.g., `JobStatusService`, `MembershipCacheInvalidator`). The canonical decision is **ADR-009 (Redis-First Caching)** which mandates Redis-backed `IDistributedCache` for cross-request caching.

The intent (per `appsettings.template.json` defaults + ADR-009) is that **all deployed environments use Redis**. Local developer machines may use the in-memory fallback when running `dotnet run` without a local Redis container.

**However**, as of 2026-06-25 the dev BFF (`spaarke-bff-dev` in `rg-spaarke-dev`) is running with `Redis__Enabled = false`. The `CacheModule` then falls into its `AddDistributedMemoryCache()` branch — in-process, not distributed, not actually Redis. This drift originated when the `spe-redis-dev-67e2xz` Redis resource was deleted (and later recreated); the App Setting was never flipped back.

**Recent activity context (2026-06-25)**: `spe-redis-dev-67e2xz` was accidentally deleted during a demo+prod cost-reduction operation that morning, then recreated the same afternoon (Basic C0, same RG, same name retained from original Bicep deployment). The recreated instance is EMPTY (no migration concern from prior data) and OPERATIONAL but NOT IN USE because `Redis__Enabled = false` was never flipped. This project's Phase 3 cutover replaces this instance with the canonically-named `spaarke-bff-redis-dev` and flips the App Setting.

The fallback explicitly logs a warning at startup: *"Distributed cache: Using in-memory cache (not distributed). This should ONLY be used in local development."* — and the dev environment has been ignoring this warning for some unknown period.

We are also reviewing our deployment process and resource packaging for staging + prod. **This is the right moment** to fix the immediate drift, harden ADR-009 with the operational guidance it currently lacks, and produce repeatable Infrastructure-as-Code so any future environment can be provisioned to its designed state in <30 min from a runbook.

## 2. Problem statement

Five distinct but related problems surface from the investigation:

1. **Config drift in dev**: `Redis__Enabled = false` in `spaarke-bff-dev` App Settings; in-memory fallback active in a deployed environment, contrary to ADR-009 intent.
2. **Incomplete CacheModule fallback**: when `Redis:Enabled=false`, `IConnectionMultiplexer` is **not registered**. Services that inject it (`JobStatusService`, `ChatContextMappingService`, `SessionFilesCleanupJob`, `MembershipCacheInvalidator`) crash at DI resolve time. The dev BFF gets away with this only because those resolved chains aren't exercised in common paths.
3. **ADR-009 silent on operational concerns**: no SKU guidance, no fail-fast policy, no secret-management mandate, no observability requirements, no tenant-key-isolation runtime check. ADR is correct in spirit but inadequate as an operational contract.
4. **Inconsistent resource naming**: `spe-redis-dev-67e2xz` is legacy SharePoint-Embedded-era + random-suffix naming. Other Spaarke resources have moved to `spaarke-{component}-{env}` convention. We need a clean target name + Bicep template that can drive dev, staging, and prod with the same module.
5. **Stale cache-key prefix**: the current `InstanceName = "sdap:"` carries forward the deprecated "SDAP" brand. Canonical naming aligns with `spaarke:` to match the broader resource-naming convention. Industry-standard multi-tenant cache key format is `{app}:tenant:{tenantId}:{resource}:{id}:v{version}` — keys lack the tenant prefix today.
6. **No canonical cache architecture or operational documentation**: There is no `docs/architecture/CACHE-ARCHITECTURE.md` and no operational guide for provisioning Redis across environments. ADR-009 is architecturally authoritative but operationally silent. A new operator cannot stand up Redis correctly for a fresh environment from existing docs alone.
7. **No deployment protocols/procedures**: Even though this rebuild is for dev, the design must establish the deployment protocols and procedures for staging + prod cutovers. Includes cache-key migration approach when existing prod data exists (NOT applicable today — dev Redis is empty — but binding for future cutovers).

The combined effect: dev cannot demonstrate the designed cache topology, staging + prod have no repeatable provisioning artifact, ADR-009 alone is insufficient guidance for an operator to provision a fresh environment correctly, and the existing cache-key format leaks the deprecated "SDAP" brand and lacks tenant-isolation.

### Investigation findings to incorporate (2026-06-25)

- `infrastructure/bicep/modules/redis.bicep` **already exists** as a Bicep module. The original design assumed authoring `redis-cache.bicep` from scratch; instead, Phase 2 should **extend the existing `redis.bicep`** (verify parameterization for SKU + multi-env + naming + Key Vault upsert) rather than create a duplicate.
- Bicep parameter file convention is **`.bicepparam`** (modern typed format) — verified by `infrastructure/bicep/parameters/{dev,staging,prod,platform-prod}.bicepparam`. No `.parameters.json` files in repo. The Redis project's new param files MUST use `.bicepparam` for consistency.
- **117 `IDistributedCache`-using files** in `src/server/api/Sprk.Bff.Api/` (corrected from design's earlier "77" estimate). This is the actual scope of the tenant-key-isolation migration in G9 — see Phase 1 for atomic migration approach.
- Per `spaarke-ai-azure-setup-dev-r1` design alignment, the canonical naming convention is two-tier: top-level Azure resources use `spaarke-{component}-{type}-{env}` env-suffix (so: `spaarke-bff-redis-dev`); sub-resources use env-agnostic names (so: cache keys are environment-agnostic — the environment is implicit in the parent Redis service URL).
- **NO standalone `Deploy-RedisCache.ps1` exists**, but Redis provisioning logic is **embedded in `scripts/Provision-Customer.ps1`** (lines 422-487 + 1437):
  - Calls `customer.bicep` (which includes `redis.bicep` as a sub-module)
  - Captures `redisHostName` + `redisConnectionString` outputs
  - Upserts `Redis-ConnectionString` into Key Vault — **same secret name we want**
  - Uses naming convention `spaarke-{customerId}-{env}-cache` — **DIFFERENT from our canonical `spaarke-bff-redis-{env}`**
- **`tests/manual/RedisValidationTests.ps1` EXISTS** — validation harness that Phase 3/4 should leverage rather than rewrite.

### Architecture decision (Q-E RESOLVED 2026-06-25 as Architecture 1)

**Architecture 1 (Platform-only Redis) selected.**

- **ONE platform Redis per environment**: `spaarke-bff-redis-{env}` — shared by BFF across all customers
- **Tenant isolation via mandatory key prefix** per G9: `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}` (final Redis key format)
- **Wrapper API designed for future multi-Redis** without major refactor: `ITenantCache.GetAsync(tenantId, resource, id, cacheInstance: "default")` — `cacheInstance` parameter defaults to "default"; today only "default" is registered; future named instances are a small additive change, not a redesign
- **`Provision-Customer.ps1` per-customer Redis is DEPRECATED**: provisioned-but-unused (no current code references `spaarke-{customer}-{env}-cache`). Phase 2 removes Redis from customer provisioning; the existing `spaarke-demo-cache` (in deleted `rg-spaarke-demo`) was already gone via 2026-06-25 cost-reduction. Future per-customer Redis (if needed for data-residency) registered via the wrapper's named-instance pattern.

**Why Architecture 1 over Architecture 3** (the existing implicit pattern):
- caching-architecture.md confirms ALL 5 BFF cache types are platform-level (Graph tokens, embeddings, Graph metadata, authorization data, analysis sessions). None route per-customer today.
- No code references per-customer Redis (`spaarke-{customer}-{env}-cache`) — confirms it was provisioned-but-unused
- Spaarke.Scheduling framework is platform-level (in-process, no Redis dependency) — no per-customer scheduling need
- Architecture 3 adds infrastructure cost + complexity for a hypothetical future need; Architecture 1 + future-extensible wrapper is YAGNI-compliant

## 3. Goals

| # | Goal | Acceptance test |
|---|---|---|
| G1 | Dev BFF actively uses Redis | `Redis__Enabled = true` in App Settings; BFF startup log shows `"Distributed cache: Redis enabled with instance name 'sdap:'"`; the in-memory warning line is gone. |
| G2 | New canonical Redis resource provisioned | `spaarke-bff-redis-dev` exists, provisioned via Bicep module. Same Bicep module is parameterized for staging + prod. |
| G3 | `CacheModule` fail-fast in deployed envs | When `Redis:Enabled=true` and the configured connection is unreachable at startup, BFF fails startup with a clear error message naming what to check. In-memory fallback ONLY active when an explicit `Redis:AllowInMemoryFallback = true` opt-in is set (developer convenience for `dotnet run`). |
| G4 | Complete fallback parity | When in-memory fallback engages, `IConnectionMultiplexer` is registered as a Null-Object so the DI graph is always valid. No latent DI-resolution crashes for Pub/Sub-dependent services. |
| G5 | Connection string in Key Vault | App Setting `ConnectionStrings__Redis` is a `@Microsoft.KeyVault(SecretUri=...)` reference. No plain Redis connection strings in App Settings. |
| G6 | ADR-009 amended with operational MUSTs | Both concise (`.claude/adr/ADR-009-redis-caching.md`) and full (`docs/adr/ADR-009-caching-redis-first.md`) versions updated in lockstep. New MUSTs cover: SKU sizing per environment, secret management (Key Vault), fail-fast in deployed envs, cache-key naming (incl. tenant prefix), observability requirements, Pub/Sub topology guidance (dev/staging vs. prod). |
| G7 | Repeatable provisioning Bicep | `infrastructure/bicep/modules/redis-cache.bicep` parameterized by name, SKU, region, network rules. Parameter files for `dev`, `staging`, `prod` checked in. Deploy script `scripts/Deploy-RedisCache.ps1` is idempotent and captures the connection string into the right Key Vault as part of the provision. |
| G8 | Observability wired | App Insights captures Redis dependency calls; custom metrics `cache.hits`, `cache.misses`, `cache.hit_rate`, `cache.redis_p95_ms`. Alert definitions in code or doc: hit rate < 80% for 15 min; P95 > 100 ms for 5 min; memory > 80%. |
| G9 | Tenant-key-isolation helper + ALL-sites atomic migration | A wrapper or extension method that requires a tenant ID parameter and constructs `tenant:{tenantId}:{resource}:{id}:v{version}` keys (final Redis key: `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}` after InstanceName prepends `spaarke:`). **ALL 117 BFF cache call sites migrated atomically** as a single coordinated change (not "5 representative" — the read/write code must be consistent across the entire codebase to avoid the BFF reading old keys while writing new ones). Tests prove keys carry tenant scope. Code-review checklist updated. |
| G10 | InstanceName rename + cache-key-format change | `Redis:InstanceName` changes from `sdap:` to `spaarke:` to align with canonical resource naming and drop the deprecated SDAP brand. Combined with G9 tenant prefix, final Redis keys are `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}` — industry-standard multi-tenant cache key format. The dev cutover happens against an EMPTY Redis (no migration cost); staging/prod cutover procedure documented in G14 includes options for cache warming or accepting brief cache-miss window. |
| G11 | Multi-env deployment runbook | NEW `docs/guides/redis-cache-azure-setup.md` (kebab-case per Q-F industry standard) covers the dev → staging → prod sequence end-to-end. A fresh operator can provision a new env Redis + cut the BFF over to it in <30 min from the doc alone. |
| G12 | Update existing `caching-architecture.md` | **UPDATE existing `docs/architecture/caching-architecture.md`** (kebab-case, already present, ~100+ lines well-structured). Add: Tenant Isolation section (mandatory `tenant:{tenantId}:` prefix on all keys; system-level exceptions documented); Multi-instance behavior + Pub/Sub semantics (Redis fan-out; in-memory mode is single-instance only); Cache instance registry (today = "default" single instance; future = named instances for multi-Redis); Failure mode catalog beyond "fail-open" (Redis unreachable = startup fail; Pub/Sub degraded = stale-cache risk; SKU undersize = latency); Updated key examples replacing `sdap:` with `spaarke:` post-rename; CacheModule changes from Phase 1; cross-link to new operational guide. |
| G13 | Cache-key migration protocol (deployment procedure) | Binding procedure for any future cutover where existing cache data exists (NOT today's dev, but binding for staging/prod): documents the two safe options — (a) accept cache-miss window during cutover (cheapest, recommended for small-data envs); (b) include key-warming step in deploy script (for high-traffic prod). Procedure goes into the operational guide. |
| G14 | All-sites atomic migration verified | `grep -r "GetAsync\|SetAsync\|RemoveAsync" src/server/api/Sprk.Bff.Api/` for `IDistributedCache` usage returns ZERO matches NOT using the tenant-isolation wrapper after migration completes. Code-review checklist enforces this on all future BFF PRs. |

### Stretch goals (R7 backlog candidates)

- **S1**: Replace Redis admin-key auth with Microsoft Entra ID + App Service Managed Identity ("Redis Data Owner" role assignment). Premium SKU feature; defer to a later iteration unless trivially achievable.
- **S2**: Separate Redis instances for cache vs. Pub/Sub in prod. Defer; document in ADR-009 as recommended but optional.
- **S3**: Multi-region geo-replication topology. Premium SKU feature; document but defer.
- **S4**: Migrate `DocumentIntelligence__AiSearchKey` + any other plain-text secrets in App Settings to Key Vault references. May be tackled in-scope as a parallel small fix if owner agrees.

## 4. Scope decisions / constraints

### In scope

- BFF `CacheModule.cs` changes (fail-fast even in dev, opt-in in-memory dev-only, Null-Object multiplexer, tenant-isolation helper, observability hooks).
- ADR-009 amendments (both versions, in lockstep).
- **Extension of existing `infrastructure/bicep/modules/redis.bicep`** (NOT new module — investigate current parameterization first; address any gaps in env support, naming, KV upsert).
- **Investigation of all `.bicepparam` files** in `infrastructure/bicep/parameters/` — verify modern format consistency, identify and fix any drift/inconsistency in related parameter files.
- Parameter files for Redis (`dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam`) — extend existing if present, author new if missing.
- Deploy script for Redis (extends or augments existing patterns; idempotent; captures connection string into Key Vault).
- Dev environment cutover end-to-end (rename `spe-redis-dev-67e2xz` → `spaarke-bff-redis-dev`, KV reference, flip `Redis:Enabled` to true).
- **`Redis:InstanceName` change**: `sdap:` → `spaarke:` (drops deprecated SDAP brand, aligns with canonical resource naming).
- **ALL-sites atomic migration to tenant-isolation helper** (~117 BFF cache call sites; coordinated single PR — no incremental migration).
- Canonical documentation:
  - **UPDATE existing** `docs/architecture/caching-architecture.md` (kebab-case, already exists; add tenant isolation, multi-instance behavior, instance registry, failure modes per G12)
  - **NEW** `docs/guides/redis-cache-azure-setup.md` (kebab-case per Q-F industry standard; operational guide for any-env Redis setup)
  - Cross-links from related docs (ADR-009, `SPAARKE-DEPLOYMENT-GUIDE.md` §X — existing file keeps its UPPERCASE name pending broader doc-cleanup)
- Test coverage for the three `CacheModule` branches (Redis-on, Redis-off-allowed-dev, Redis-off-rejected-non-dev).
- Decommission plan for legacy `spe-redis-dev-67e2xz` (shortened to **24-hr verification window** since the legacy Redis is empty — no data to verify; per Q3 resolution).
- One-sentence documentation in operational guide noting: "in-memory fallback mode does NOT support multi-instance deployment — only opt-in for local dev with a single instance."

### Out of scope

- **AI Search restoration** (`spaarke-search-dev` is being addressed by sister project `spaarke-ai-azure-setup-dev-r1`). Do not conflate. This project's BFF changes must not assume AI Search exists.
- **Production Redis provisioning**. This project produces the artifacts; actual prod provisioning is a separate go/no-go with security + finance review.
- **Migration from admin-key auth to Microsoft Entra ID auth** — defer to S1 stretch goal.
- **Pub/Sub separation in prod** — defer to S2 stretch goal.
- **`DocumentIntelligence__AiSearchKey` plain-text key migration** — moved to sister project `spaarke-ai-azure-setup-dev-r1` per 2026-06-25 Q&A. The AI Search project's FR-15 already addresses dev BFF KV-reference migration for AI-Search-related secrets; adjacent secrets belong with that scope to keep this project focused purely on Redis.
- **Other plain-text secret remediation** (apart from Redis) — defer to S4 stretch goal unless owner approves expanding scope.

### Hard constraints

| Constraint | Source |
|---|---|
| ADR-009 amendments must update **both** `.claude/adr/` and `docs/adr/` files in lockstep | Existing project convention (e.g., ADR-030 v2, ADR-037 — both authored as pairs) |
| New BFF DI registrations must be justified per `CLAUDE.md §10` (BFF Hygiene) — three-question template (Existing / Extension / Cost-of-doing-nothing) | `CLAUDE.md §10`, §11 |
| Null-Object pattern for `IConnectionMultiplexer` must follow ADR-032 (BFF Null-Object Kill-Switch) — symmetric registration, no asymmetric anti-pattern | ADR-032 |
| Connection strings stored in Key Vault; App Service Managed Identity reads them | ADR-028 (Spaarke Auth v2) |
| Publish size discipline: ≤+1 MB compressed delta for this project; current baseline ~47 MB | ADR-029 + CLAUDE.md §10 binding rule |
| The legacy `spe-redis-dev-67e2xz` resource must remain functional during cutover; decommission only after verification window (suggest 48 hr of post-cutover stability) | Operational safety |
| App Service Managed Identity must have permissions for the new Key Vault secret (or the same vault if reused) before the App Setting reference flips | Auth + Key Vault permission model |
| **InstanceName changes from `sdap:` to `spaarke:`** as part of this project (per Q-A 2026-06-25 resolution). Dev cutover is against empty Redis — no migration concern. Staging/prod cutover MUST follow G13 cache-key migration protocol. | Canonical naming alignment + tenant-isolation refactor |
| **Naming convention two-tier rule** (from sister project `spaarke-ai-azure-setup-dev-r1`): TOP-LEVEL Azure resources env-suffixed (`spaarke-bff-redis-dev`); SUB-RESOURCES env-agnostic (cache keys carry tenant but NOT env — env is implicit in the parent Redis service URL `spaarke-bff-redis-{env}.redis.cache.windows.net`) | Codified in sister project + binding here |
| **Cache key format MUST be `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}`** — industry-standard multi-tenant Redis convention. Tenant prefix is MANDATORY (no untenanted keys allowed except system-level keys with documented exception). | Multi-tenant isolation invariant |

## 5. Architecture / approach

### Phase 0 — Discovery + ADR amendment (no code or infra changes yet)

- Confirm live Azure state with the verification commands in §6.
- Catalog all `IConnectionMultiplexer` injection sites (`grep` already identified 4 services + 4 DI modules — verify list).
- Draft ADR-009 amendments. Concrete additions:
  - SKU table (dev=Basic C0, staging=Standard C0+, prod=Standard C2+ or Premium P1+)
  - Connection string MUST come from Key Vault via `@Microsoft.KeyVault(SecretUri=...)` reference
  - Fail-fast at startup when Redis is configured but unreachable in deployed environments
  - Cache key MUST embed tenant ID: `sdap:tenant:{tenantId}:...`
  - Pub/Sub MAY share Redis with cache in dev/staging; SHOULD be separated in prod
  - App Insights MUST capture Redis dependency calls; minimum custom metrics + alerts listed
- Get the ADR amendment reviewed + committed before changing code.

### Phase 1 — CacheModule hardening (code-only, set up as production-like even in dev)

**Per Q-C 2026-06-25**: Configure dev BFF as if it's production — no SKU shortcuts, no skipping fail-fast. The dev environment must demonstrate the same operational discipline expected in staging + prod.

- **Fail-fast check**: when `Redis:Enabled=true` and `ConnectionMultiplexer.Connect()` throws OR Redis is unreachable, fail startup with a clear error message naming the connection string source (Key Vault secret URI vs. App Setting fallback) and what to check.
- **CRITICAL one-line fix in CacheModule.cs:31**: change `configOptions.AbortOnConnectFail = false` → `AbortOnConnectFail = true` for deployed environments. Currently `false` swallows startup failures and the BFF starts in a broken state with Redis configured-but-unreachable. The fail-fast intent of G3 requires this flip. Confirm via Phase 1 tests.
- **Explicit opt-in for in-memory fallback** (App Setting with environment guard, per Q5 resolution):
  - `Redis:Enabled = false` + `Redis:AllowInMemoryFallback = true` + `IHostEnvironment.IsDevelopment() == true` → register in-memory `IDistributedCache` + Null-Object `IConnectionMultiplexer`
  - `Redis:Enabled = false` + `Redis:AllowInMemoryFallback = true` + NOT Development → **throw at startup** ("AllowInMemoryFallback is restricted to Development environments. Set `Redis:Enabled=true` for `ASPNETCORE_ENVIRONMENT={env}`")
  - `Redis:Enabled = false` + `Redis:AllowInMemoryFallback != true` → throw at startup ("Redis is required in deployed environments — set `Redis:Enabled=true` or `Redis:AllowInMemoryFallback=true` for local dev")
  - Environment guard prevents accidental in-memory mode in dev/staging/prod even if App Setting is misconfigured.
- **Null-Object `IConnectionMultiplexer`** (`Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs` per Q9): implements the interface with safe no-op semantics for `GetSubscriber()`, `GetDatabase()`, etc. Throws `NotSupportedException` only for operations that genuinely cannot no-op. **Pub/Sub semantics (Q-B resolution)**: `GetSubscriber().Publish(...)` = no-op log entry; `GetSubscriber().Subscribe(...)` = no-op subscription that never delivers. Document in operational guide as "in-memory fallback does NOT support multi-instance scenarios — only opt-in for local dev with a single instance."
- **Tenant-key-isolation helper** (`IDistributedCacheTenantExtensions.cs` or wrapper service `ITenantCache`): public methods require a tenant ID parameter. Internal key construction follows **industry-standard multi-tenant Redis format**: `tenant:{tenantId}:{resource}:{id}:v{version}` (the `InstanceName = "spaarke:"` prepends automatically → final Redis key `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`).
- **ALL-sites atomic migration (per Q-A 2026-06-25 resolution)**: migrate **all 117 BFF cache call sites** to use the tenant-key-isolation helper as ONE coordinated PR. **Not "5 representative sites"** — the read/write code must be consistent across the entire codebase to prevent the BFF from reading old-format keys while writing new-format keys. Use the research subagent to enumerate sites + classify, then apply the migration as a single atomic change. Code-review checklist enforces no direct `IDistributedCache.GetAsync/SetAsync` calls outside the wrapper.
- **System-level cache exception**: a small set of cache keys may legitimately be system-scoped (not tenant-scoped) — e.g., feature-flag cache, system config cache. Document this allow-list in the wrapper with explicit comments justifying each exception. Default for any new cache call is tenant-scoped.
- **InstanceName change**: `Redis:InstanceName` default changes from `sdap:` to `spaarke:` in `appsettings.template.json` + `appsettings.json` + Bicep param files. Dev cutover is against empty Redis (no migration cost). Per G13, staging/prod cutover protocol documented in operational guide.
- **Unit + integration tests** for all four scenarios: Redis-on, Redis-off-AllowFallback-Dev, Redis-off-AllowFallback-non-Dev (must throw), Redis-off-NoFallback (must throw).

### Phase 2 — Bicep module + provisioning artifacts (extend existing, modern .bicepparam format)

**Per Q-D 2026-06-25 + 2026-06-25 investigation**: `infrastructure/bicep/modules/redis.bicep` ALREADY EXISTS. Phase 2 EXTENDS the existing module rather than authoring a new one. Bicep parameter file convention is `.bicepparam` (modern typed format) — confirmed in `infrastructure/bicep/parameters/` (all 9 existing param files use `.bicepparam`; zero `.parameters.json`).

- **Investigate existing `redis.bicep`**: confirm parameterization for `name`, `location`, `sku` (object: `name`, `family`, `capacity`), `minimumTlsVersion`, `enableNonSslPort`, `redisVersion`, optional `vnetSubnetId`, optional `staticIP`, optional `tags`. Output: `name`, `hostName`, `sslPort`, resource ID, primary key (for downstream Key Vault upsert). **Add anything missing**.
- **Investigate adjacent `.bicepparam` files** for drift/inconsistency in related parameter files (`dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam`, `platform-prod.bicepparam`, customer templates). Identify and fix any parameter inconsistency that touches Redis or that should follow the canonical naming convention. Document fixes in PR description.
- **Parameter files for Redis** (per environment, using `.bicepparam`):
  - `dev.bicepparam` — Basic C0 SKU, westus2 region, name `spaarke-bff-redis-dev`, RG `spe-infrastructure-westus2` (matches current dev infra location)
  - `staging.bicepparam` — Standard C0 SKU (TBD: confirm staging SKU during Phase 2 — could go either way), name `spaarke-bff-redis-staging`
  - `prod.bicepparam` — Standard C2+ or Premium P1+ (defer specific SKU to finance review per Q10), name `spaarke-bff-redis-prod`
- **Deploy script `scripts/Deploy-RedisCache.ps1`** (NEW standalone — extracted from `Provision-Customer.ps1` per Q-E Architecture 1, or authored fresh if Architecture 2/3 chosen):
  - Parameters: `-Environment {dev|staging|prod}`, `-WhatIf`, `-VerifyOnly`, `-CutoverBffSettings`
  - Idempotent (check existence; deploy via `az deployment group create`)
  - Upserts primary key into the appropriate Key Vault under `Redis-ConnectionString` (full StackExchange-compatible connection string format)
  - Optionally updates the BFF App Setting references in one shot via `-CutoverBffSettings` param (default off — operator confirms before flipping)
  - Post-deploy verification: leverages **existing `tests/manual/RedisValidationTests.ps1`** rather than rewriting validation logic (DO NOT duplicate)
  - **Refactor `Provision-Customer.ps1`** to call `Deploy-RedisCache.ps1` instead of inlining the Redis Bicep call (per Architecture 1) — ensures customer provisioning and platform provisioning use the same standardized script + naming
- **Leverage `tests/manual/RedisValidationTests.ps1`**: extend if necessary (add tenant-isolation key-format check, fail-fast assertion test, Pub/Sub semantics test). Do NOT replace. Document any extensions in PR.

### Phase 3 — Dev environment cutover (canonical naming + InstanceName change)

- Provision `spaarke-bff-redis-dev` via `scripts/Deploy-RedisCache.ps1 -Environment dev`.
- Confirm Key Vault `spaarke-spekvcert/Redis-ConnectionString` (or appropriate env KV) is populated with new instance's connection string.
- Update `spaarke-bff-dev` App Settings:
  - `Redis__Enabled = true`
  - `Redis__InstanceName = spaarke:` (changed from `sdap:` per G10)
  - `ConnectionStrings__Redis = @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=Redis-ConnectionString)`
  - `Redis__AllowInMemoryFallback` NOT set (or set to `false` to be explicit)
- Restart BFF; verify `/healthz` 200 + log line `"Distributed cache: Redis enabled with instance name 'spaarke:'"` + no in-memory warning.
- Quick smoke test: create a chat session → verify a `spaarke:tenant:{tenantId}:session:{id}:v1` key appears via `az redis cli` or App Insights dependency telemetry. Verify the tenant prefix is present (G9 atomic-migration validation).
- Decommission legacy `spe-redis-dev-67e2xz` after **24-hr** verification window of green operation (shortened from 48 hr per Q3 — the legacy is empty, no data to verify, only post-cutover BFF stability needs verifying).
- Optionally rename the legacy resource to `spe-redis-dev-67e2xz-LEGACY-DELETE-2026-07-XX` during the verification window for clarity (NOT possible — Azure Redis can't be renamed in-place; instead, add a tag `decommission=2026-07-XX` to the legacy resource).

### Phase 4 — Observability

- Wire App Insights ↔ StackExchange.Redis dependency telemetry. Verify dependency calls appear in App Insights Live Metrics.
- Custom metrics emitted from the cache wrapper:
  - `cache.hits` (counter, dimension: `resource`)
  - `cache.misses` (counter, dimension: `resource`)
  - `cache.hit_rate` (gauge, computed)
  - `cache.redis_p95_ms` (histogram or computed from telemetry)
- Alert definitions:
  - Hit rate < 80% for 15 min → BFF cache key/version drift; investigate
  - Redis P95 > 100 ms for 5 min → network issue or SKU undersize
  - Memory > 80% of SKU limit → scale to next SKU
- Document the runbook for each alert.

### Phase 5 — Canonical docs (architecture + guide) + ADR-009 amendments + lessons learned + R7 backlog

**Per user request 2026-06-25**: This phase produces the comprehensive but concise (for AI consumption) Redis/Cache documentation set — analogous to the canonical doc work in sister project `spaarke-ai-azure-setup-dev-r1`. **Per Q-F (2026-06-25)**: all new docs use **kebab-case** (industry standard for documentation files).

- **UPDATE existing `docs/architecture/caching-architecture.md`** (kebab-case, ~100+ lines well-structured already; substantial additions, NOT a rewrite):
  - **Add Tenant Isolation section** — mandatory `tenant:{tenantId}:` prefix on all cache keys; system-level exceptions documented in wrapper allow-list (e.g., feature-flag cache, system config). Critical gap fix: current doc has key examples like `sdap:auth:access:{userId}:{resourceId}` with NO tenant prefix — multi-tenant isolation is NOT enforced today. G9 atomic migration fixes this.
  - **Add Multi-instance Behavior section** — Pub/Sub fan-out via Redis (membership invalidation, etc.); in-memory mode is single-instance only and does NOT support multi-instance scenarios (documented one-sentence limitation per Q-B)
  - **Add Cache Instance Registry section** — today = single "default" instance (`spaarke-bff-redis-{env}`); wrapper API designed for future named instances (Architecture 1 per Q-E with future-multi-Redis extensibility)
  - **Update Key Conventions section** — replace `sdap:` examples with `spaarke:` post-rename (Phase 1 changes `Redis:InstanceName`)
  - **Update Component table** — describe CacheModule changes from Phase 1 (fail-fast, AbortOnConnectFail=true, env-guarded AllowInMemoryFallback, Null-Object `IConnectionMultiplexer`)
  - **Add Failure Mode Catalog beyond "fail-open"** — Redis unreachable in deployed env = startup fail (G3); Pub/Sub degraded = stale-cache risk on multi-instance; SKU undersize = P95 latency degradation (alert threshold defined in Phase 4)
  - **Cross-link to new operational guide** + ADR-009 amendments
- **NEW: `docs/guides/redis-cache-azure-setup.md`** (kebab-case) — operational runbook:
  - Prerequisites (KV exists, MI permissions, App Service exists, App Settings template loaded)
  - Provision command + expected output (per environment)
  - Verification commands
  - Cutover protocol (dev: empty Redis = clean slate; staging/prod: G13 key-migration options)
  - Rollback procedure (revert App Setting to prior connection string + restart)
  - Secret rotation procedure (rotate primary key → re-upsert KV secret → BFF picks up automatically via KV reference; brief restart window only if needed)
  - Decommission procedure for old resource
  - Troubleshooting: connection failures, latency spikes, hit-rate degradation
  - Known limitation: in-memory fallback mode does NOT support multi-instance deployment (single-instance local dev only)
- **UPDATE: ADR-009 (concise + full, in lockstep)** with operational MUSTs (per G6):
  - SKU table (dev=Basic C0, staging=Standard C0+, prod=Standard C2+ or Premium P1+)
  - Connection string MUST come from Key Vault via `@Microsoft.KeyVault(SecretUri=...)` reference
  - Fail-fast at startup when Redis is configured but unreachable in deployed environments
  - Cache key MUST embed tenant ID with industry-standard format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}`
  - InstanceName MUST be `spaarke:` (canonical app prefix)
  - Pub/Sub MAY share Redis with cache in dev/staging; SHOULD be separated in prod
  - App Insights MUST capture Redis dependency calls; minimum custom metrics + alerts listed
  - Two-tier resource-naming rule (top-level env-suffixed, sub-resources env-agnostic)
- **UPDATE: `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md`** — add new §4.5 "Phase 1.5: Redis Cache" (before AI Search §4.6 from sister project) calling `Deploy-RedisCache.ps1`. Add to Appendix D Script Reference.
- **Document the secret rotation procedure** (rotate primary key via Azure portal → re-upsert Key Vault secret → BFF picks up via Key Vault reference; brief restart window if needed)
- **Capture lessons learned**: how the drift originated (Redis resource deleted, App Setting left at `false`, in-memory warning ignored for unknown duration), what guardrails would have prevented it (fail-fast in deployed envs, alert on in-memory warning, deployment checklist explicitly verifies Redis state)
- **Author the R7 backlog**: S1 (Entra ID auth), S2 (Pub/Sub separation), S3 (multi-region), S4 (other secrets migration deferred to sister project + future cleanup)

## 6. Reference material

### Key files in the spaarke repo

| Concern | File path |
|---|---|
| `CacheModule.cs` (the DI registration to modify) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` |
| ADR-009 concise (to amend) | `.claude/adr/ADR-009-redis-caching.md` |
| ADR-009 full (to amend in lockstep) | `docs/adr/ADR-009-caching-redis-first.md` |
| Appsettings template (current designed defaults) | `src/server/api/Sprk.Bff.Api/appsettings.template.json` |
| `IConnectionMultiplexer` consumer (Office jobs) | `src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs` |
| `IConnectionMultiplexer` consumer (chat session files cleanup) | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupJob.cs` |
| `IConnectionMultiplexer` consumer (chat context mapping) | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` |
| `IConnectionMultiplexer` consumer (membership invalidation Pub/Sub) | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipCacheInvalidator.cs`, `MembershipCacheInvalidationSubscriber.cs`, `MembershipCacheInvalidatorOptions.cs`, `IMembershipCacheInvalidator.cs` |
| Existing Null-Object pattern to mirror | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/NullMembershipCacheInvalidator.cs` |
| DI modules referencing `IConnectionMultiplexer` | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs`, `MembershipModule.cs`, `OfficeModule.cs` |
| **Existing Bicep module** (extend, do NOT duplicate) | `infrastructure/bicep/modules/redis.bicep` — confirmed to exist 2026-06-25 |
| **Existing Bicep param convention** (use `.bicepparam`) | `infrastructure/bicep/parameters/{dev,staging,prod,platform-prod,demo-customer,customer-template}.bicepparam` — 9 files use modern `.bicepparam`; ZERO `.parameters.json` in repo |
| **Existing Redis provisioning** (review + refactor per Q-E) | `scripts/Provision-Customer.ps1` lines 422-487 + 1437 — currently inlines Redis provisioning per customer (`spaarke-{customerId}-{env}-cache`). Phase 2 extracts to `Deploy-RedisCache.ps1` per Q-E resolution. |
| **Existing Redis validation harness** (leverage, do NOT rewrite) | `tests/manual/RedisValidationTests.ps1` — extend with tenant-isolation key-format check + fail-fast assertion if needed |
| **Sister project** (sequence + scope-split reference) | `projects/spaarke-ai-azure-setup-dev-r1/design.md` v0.2 + `spec.md` v0.1 — defines naming convention, KV reference pattern, env-suffix rule. Sister project's FR-08 and FR-15(Redis-parts) DELEGATED to this project. |
| BFF deploy skill | `.claude/skills/bff-deploy/SKILL.md` |
| Related ADRs | ADR-028 (Auth v2), ADR-029 (Publish Hygiene), ADR-032 (Null-Object Kill-Switch), ADR-010 (DI Minimalism) |
| `CLAUDE.md §10` BFF Hygiene binding rules | Root `CLAUDE.md` |
| `CLAUDE.md §11` Component Justification | Root `CLAUDE.md` |

### Live Azure state — verification commands (run these first, don't change anything until confirmed)

```bash
# 1. Inspect current Redis (legacy name) — does it exist? what SKU? what state?
az redis show -g spe-infrastructure-westus2 -n spe-redis-dev-67e2xz \
  --query "{name:name, host:hostName, sslPort:sslPort, sku:sku.name, capacity:sku.capacity, prov:provisioningState, version:redisVersion}" -o tsv

# 2. Inspect current BFF Redis App Settings (the drift)
az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev \
  --query "[?contains(name, 'Redis') || contains(name, 'edis')].{name:name, value:value}" -o tsv

# 3. Inspect Key Vault for existing Redis secret (and adjacent secrets surfaced in investigation)
az keyvault secret list --vault-name spaarke-spekvcert \
  --query "[?contains(name, 'edis') || contains(name, 'Redis')].name" -o tsv

# 4. BFF App Service exists + healthy
az webapp show -g rg-spaarke-dev -n spaarke-bff-dev --query "{state:state, host:defaultHostName}" -o tsv
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz

# 5. Confirm Managed Identity + Key Vault access policy / RBAC for the BFF App Service
az webapp identity show -g rg-spaarke-dev -n spaarke-bff-dev --query "{type:type, principalId:principalId, tenantId:tenantId}" -o tsv
az keyvault show --name spaarke-spekvcert --query "{name:name, rg:resourceGroup, rbacAuth:properties.enableRbacAuthorization}" -o tsv
```

### Codebase facts already discovered (paste into agent so it doesn't re-discover)

1. `Redis__Enabled = false` in dev BFF App Settings → `CacheModule` falls into `AddDistributedMemoryCache()` branch.
2. `IConnectionMultiplexer` is **NOT registered** in the in-memory fallback branch — services that depend on it can crash at resolve time. The dev BFF gets away with this only because those chains aren't exercised in common flows.
3. `NullMembershipCacheInvalidator` already exists as the Null-Object for `IMembershipCacheInvalidator` — pattern to mirror for `IConnectionMultiplexer`.
4. `appsettings.template.json` already has the right defaults (`Redis.Enabled = true`, `ConnectionStrings.Redis` = Key Vault reference). The dev BFF App Settings diverged from this template.
5. The legacy Redis resource (`spe-redis-dev-67e2xz`) was deleted earlier on 2026-06-25 (during demo+prod cost-reduction) and recreated the same afternoon. It is **operational** as of this design's authoring time, **empty** (no data), and **not in use** because of the `Redis__Enabled = false` drift.
6. `CacheModule.cs:31` sets `AbortOnConnectFail = false` — this swallows startup failures when Redis is unreachable, contradicting G3 fail-fast intent. **One-line fix** to flip to `true` is part of Phase 1.
7. **117 BFF files** use `IDistributedCache` (verified 2026-06-25 by `grep -rl IDistributedCache src/server/api/Sprk.Bff.Api/`) — significantly more than the original "77" estimate. This is the scope of G9 atomic migration.
8. `infrastructure/bicep/modules/redis.bicep` ALREADY EXISTS — Phase 2 extends, doesn't author new.
9. `.bicepparam` is the only param-file convention in repo — Phase 2 MUST use `.bicepparam` for new param files.
10. Adjacent concern `DocumentIntelligence__AiSearchKey` (plain admin key in BFF App Settings) — moved to sister project `spaarke-ai-azure-setup-dev-r1` per Q4 resolution. Out of scope here.
11. **No standalone `Deploy-RedisCache.ps1` exists.** Redis provisioning is currently inlined in `scripts/Provision-Customer.ps1` (lines 422-487 + 1437). Phase 2 extracts this logic to a standalone script (per Q-E Architecture 1 default) and refactors `Provision-Customer.ps1` to call it. Naming reconciliation also needed: existing uses `spaarke-{customerId}-{env}-cache`; canonical is `spaarke-bff-redis-{env}`.
12. **`tests/manual/RedisValidationTests.ps1` exists** — extend, do not rewrite. Phase 3/4 leverages it for cutover validation.

### Current `CacheModule.cs` else branch (the drift origin)

```csharp
else
{
    services.AddDistributedMemoryCache();
    // ⚠️ IConnectionMultiplexer NOT registered — services that inject it crash at resolve time
    logger.LogWarning("Distributed cache: Using in-memory cache (not distributed). This should ONLY be used in local development.");
}
```

### Current `appsettings.template.json` Redis defaults (what dev should have inherited)

```jsonc
"ConnectionStrings": {
  "Redis": "@Microsoft.KeyVault(SecretUri=#{KEY_VAULT_URL}#secrets/Redis-ConnectionString)"
},
"Redis": {
  "Enabled": true,
  "ConnectionString": null,
  "InstanceName": "#{REDIS_INSTANCE_NAME}#"
}
```

### Current dev BFF App Settings (the drift)

```
Redis__Enabled = false
ConnectionStrings__Redis = (not set)

# Adjacent concern (out of scope here; moved to sister project spaarke-ai-azure-setup-dev-r1 per Q4):
DocumentIntelligence__AiSearchKey = <REDACTED — plaintext admin key; the bug pattern we're fixing via KV references>
```

> **Security note**: Earlier draft of this design contained the actual admin key value in plaintext. Redacted 2026-06-25 after GitHub Push Protection correctly flagged it. The key was for the (deleted + recreated) `spaarke-search-dev` instance. If the recreated service has used the same key value (unlikely — Azure typically generates new keys on service recreation), rotate via `az search admin-key renew -g spe-infrastructure-westus2 --service-name spaarke-search-dev --key-kind primary`.

### ADR-009 current concise text — section to extend (lines 17–32)

```
### ✅ MUST
- MUST use IDistributedCache for cross-request caching
- MUST use RequestCache for within-request de-dupe
- MUST version cache keys (rowversion/etag)
- MUST use short TTLs for security data
- MUST document ADR-009 exception for any IMemoryCache use

### ❌ MUST NOT
- MUST NOT cache authorization decisions (cache data only)
- MUST NOT add L1 cache without profiling proof
- MUST NOT use IMemoryCache for non-metadata without justification
```

This must be extended with operational MUSTs from §3 Goals.

## 7. Anti-patterns to avoid

- **Silent in-memory fallback in deployed environments.** That's the original drift. Fail loudly.
- **Plain Redis connection strings in App Settings.** Always Key Vault references.
- **Sharing Redis across BFFs without a key prefix discriminator.** The `InstanceName` prefix (`spaarke:` after this project) exists for this reason.
- **Caching authorization decisions.** ADR-009 already says this; the new tenant-key-isolation helper should make it harder to accidentally violate.
- **Adding an L1 in-process cache layer on top of Redis** without ADR amendment + profiling proof.
- **Skipping the fail-fast at startup.** A BFF that boots without Redis when Redis was required is a hidden bug factory.
- **Asymmetric DI registration** (per ADR-032 — registering `IConnectionMultiplexer` only when a feature flag is on, leaving dependents broken). Always register a real or Null-Object impl.
- **Partial cache-key migration.** The CRITICAL bug class from Q-A: writing in new format while reading in old format orphans cache reads and produces silent cache misses. Either migrate ALL sites atomically OR keep both formats coexisting via a versioned reader. This project chooses atomic migration (G9).
- **Cache key collision via missing tenant prefix.** A cache call that omits the tenant prefix can return another tenant's data. The wrapper API requires tenantId; the code-review checklist enforces no direct `IDistributedCache` calls outside the wrapper.
- **Building per-environment cache-key formats.** Index/cache keys are env-agnostic (per Spaarke two-tier naming rule). Env is implicit in the parent Redis service URL. Don't suffix keys with `-dev` or similar.

## 8. Success criteria (project-wide)

1. Dev BFF startup log: `"Distributed cache: Redis enabled with instance name 'spaarke:'"` — in-memory warning gone.
2. Dev BFF App Settings reference Key Vault for the Redis connection string (no plain text).
3. ADR-009 amended in both `.claude/adr/` and `docs/adr/` with all the new operational MUSTs (per G6 + Phase 5).
4. `CacheModule.cs` fails startup with a clear error when Redis is configured but unreachable in a deployed environment. `AbortOnConnectFail = true` confirmed for deployed envs.
5. Bicep module `redis.bicep` extended (NOT duplicated); parameter files for dev / staging / prod committed using `.bicepparam` format; adjacent `.bicepparam` drift identified and fixed.
6. NEW `docs/guides/redis-cache-azure-setup.md` is the canonical operational reference (kebab-case per Q-F); a new operator can provision a new env Redis end-to-end in <30 min from the doc.
7. `docs/architecture/caching-architecture.md` UPDATED with tenant isolation, multi-instance behavior, instance registry, failure mode catalog, and Phase 1 CacheModule changes (per G12). NOT a new file — substantial additions to existing well-structured doc.
8. App Insights captures Redis dependency calls; cache hit rate metric visible in dashboards.
9. Tenant-key-isolation helper used in **ALL 117 BFF cache call sites** (atomic migration per G9); tests prove keys carry tenant scope; code-review checklist enforces wrapper-only access on future PRs (G14).
10. `Redis:InstanceName` changed from `sdap:` to `spaarke:` across appsettings, template, and Bicep params. Cache keys in production format: `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`.
11. R7 backlog captures S1–S4 deferred items.
12. The legacy `spe-redis-dev-67e2xz` resource is decommissioned (or tagged with `decommission=2026-07-XX`) after the **24-hr** verification window (shortened per Q3 since legacy is empty).
13. BFF publish size delta ≤+1 MB compressed (per ADR-029 + CLAUDE.md §10 binding rule).
14. `dotnet test` passes; new `CacheModule` tests cover all four scenarios (Redis-on, Redis-off-AllowFallback-Dev, Redis-off-AllowFallback-non-Dev throws, Redis-off-NoFallback throws).
15. `SPAARKE-DEPLOYMENT-GUIDE.md` §4.5 added with `Deploy-RedisCache.ps1` invocation; Appendix D updated.
16. Sister project handoff confirmed: `spaarke-ai-azure-setup-dev-r1` design+spec reference this project as prerequisite; this project's success criterion 1 is documented as the gate for sister-project Phase 3 start.

## 9. Owner Clarifications — RESOLVED 2026-06-25

All questions answered in pre-`/design-to-spec` discussion. Captured here for traceability.

- ~~**Q1**~~ — **RESOLVED**: Basic C0 for dev (cheapest, acceptable for dev). Staging Standard C0+. Prod deferred to finance review (Q10 below).
- ~~**Q2**~~ — **RESOLVED**: Azure Redis cannot be renamed in-place (confirmed). Approach: provision new `spaarke-bff-redis-dev` + cut over BFF + decommission legacy.
- ~~**Q3**~~ — **RESOLVED**: **24-hr** verification window (shortened from 48 hr because legacy Redis is empty — no data to verify, only post-cutover BFF stability needs verifying).
- ~~**Q4**~~ — **RESOLVED**: Adjacent secret (`DocumentIntelligence__AiSearchKey`) **OUT of scope here**; **moved to sister project** `spaarke-ai-azure-setup-dev-r1` per Q4. Keeps each project focused on its own concern.
- ~~**Q5**~~ — **RESOLVED**: `Redis:AllowInMemoryFallback` is an **App Setting with environment guard at startup** (`if (env.IsDevelopment())`). Cleaner than launch-profile-only — easier to test, impossible to misconfigure in non-Dev environments (startup throws if AllowFallback=true outside Development).
- ~~**Q6**~~ — **OPEN** (low priority): owner-direct vs PR review checklist for ADR-009 amendments. Default to PR review checklist; owner can override.
- ~~**Q7**~~ — **RESOLVED**: Phase 4 (observability) stays separate from Phase 3 (cutover). Observability hardening is independent and can be added incrementally post-cutover.
- ~~**Q8**~~ — **RESOLVED**: ALL 117 cache call sites migrated atomically (not "5 representative"). Per Q-A critical fix: read/write code must be consistent across entire codebase to prevent BFF from reading old-format keys while writing new-format. Audit/classify-first is overkill — just migrate all atomically.
- ~~**Q9**~~ — **RESOLVED**: `IConnectionMultiplexer` Null-Object lives in `Sprk.Bff.Api.Infrastructure.Cache.NullObjects` namespace.
- ~~**Q10**~~ — **OPEN** (deferred to finance review): prod Redis SKU + budget ceiling. Document range in operational guide; defer specific selection to finance review when prod provisioning is scoped (out of scope for this project per §4 Out of Scope).
- **NEW Q-A** — **RESOLVED**: Industry-standard cache key format adopted: `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` → with `InstanceName="spaarke:"`, final Redis key is `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`. Tenant prefix MANDATORY; system-level cache exceptions explicitly documented in the wrapper.
- **NEW Q-B** — **RESOLVED**: Multi-instance limitation of in-memory fallback DOCUMENTED in operational guide (one sentence) — NOT built/over-engineered. In-memory mode is for local dev with a single instance only; multi-instance scenarios MUST use Redis.
- **NEW Q-C** — **RESOLVED**: Dev BFF configured as production-like: `AbortOnConnectFail = true`, fail-fast enabled, no shortcuts. Even though Dev "could" use shortcuts, we build to production-like discipline to ensure protocols + procedures developed here apply to staging/prod cutovers.
- **NEW Q-D** — **RESOLVED**: `.bicepparam` (modern typed) format confirmed for ALL new param files. Phase 2 includes investigating adjacent `.bicepparam` files and addressing any drift identified.
- **NEW Q-E** — **RESOLVED 2026-06-25**: **Architecture 1 (Platform-only Redis)**. One Redis instance per environment (`spaarke-bff-redis-{env}`); tenant isolation via key prefix (per G9); wrapper API designed for future multi-Redis without major refactor (named-instance registry with single "default" registered today). `Provision-Customer.ps1` per-customer Redis provisioning is **DEPRECATED** (provisioned-but-unused; no current code references `spaarke-{customer}-{env}-cache`). If a customer ever needs dedicated Redis for data-residency or compliance, register their instance under a named key in the wrapper — small change, not redesign. See §5 Phase 2 for `Provision-Customer.ps1` refactor approach.
- **NEW Q-F** — **RESOLVED 2026-06-25**: File-naming convention is **kebab-case (lowercase-with-hyphens)** for ALL new docs in `docs/architecture/` and `docs/guides/`. Industry standard (Microsoft Docs, GitHub docs, MDN, GitLab, React/Vue/Next.js). Reserve UPPERCASE for top-level meta files only (`README.md`, `CHANGELOG.md`, `LICENSE.md`, etc.). Existing UPPERCASE files (`SPAARKE-DEPLOYMENT-GUIDE.md`, `AI-EMBEDDING-STRATEGY.md`, etc.) are referenced by their current names in this project; broader rename will happen in a future doc-cleanup project.

## 10. Pipeline notes

- This project follows the standard `/design-to-spec` → `/project-pipeline` → `/task-execute` workflow.
- Phases 0 (ADR amendment), 1 (CacheModule), 2 (Bicep), 4 (Observability) are largely parallel-safe. Phase 5 (canonical docs) can be drafted in parallel with Phases 1-4 but finalized after.
- Phase 0 (ADR amendment) touches `.claude/` so main-session only (per sub-agent write-boundary rule in root CLAUDE.md).
- Phase 3 (cutover) is a single-operator serial step; touches live Azure infra.
- **CRITICAL SEQUENCING**: This project's Phase 3 cutover (Redis live + canonically named + BFF using it) MUST complete BEFORE sister project `spaarke-ai-azure-setup-dev-r1` begins its Phase 3 (Deploy Infrastructure). The sister project's spec.md NFR-13 codifies this dependency.
- Phase 5 (canonical docs + ADR amendments + lessons + backlog) is the wrap-up.
- Estimated calendar: 1.5–2 weeks with one focused Claude Code session per phase. Phases 1+2 parallelizable; Phase 3 single-day; Phase 4 single-day; Phase 5 spans 2-3 days.
- Estimated POML task count: ~30–45 task files across the 6 phases + wrap-up.

---

## 11. Worktree + handoff plan

- Create worktree `c:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1` from master after design.md + spec.md are committed (post-`/design-to-spec`).
- Branch: `work/spaarke-redis-cache-remediation-r1`.
- Sister project's session at `c:\code_files\spaarke-wt-spaarke-ai-azure-setup-dev-r1` continues in parallel; its Phase 3 GATE is this project's Phase 3 completion.

---

*End of design.md v0.2.*
