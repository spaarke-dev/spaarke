# Project Plan: Spaarke Redis Cache Remediation (R1)

> **Last Updated**: 2026-06-25
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Close the BFF Redis/cache configuration drift (in-memory fallback active in a deployed env), harden ADR-009 with operational MUSTs, rename to canonical `spaarke-bff-redis-{env}`, and produce repeatable IaC + canonical docs so any environment provisions in <30 min from a runbook. This project is a **prerequisite** for `spaarke-ai-azure-setup-dev-r1` Phase 3.

**Scope**:
- Phase 1: `CacheModule` hardening + `ITenantCache` wrapper + atomic migration of ~199 cache call sites (single PR)
- Phase 2: Extend `redis.bicep`, new `.bicepparam` files, new `Deploy-RedisCache.ps1` extracted from `Provision-Customer.ps1`
- Phase 3: Dev cutover to `spaarke-bff-redis-dev` (Basic C0) via Key Vault reference; 24-hr window; legacy decommission
- Phase 4: App Insights Redis dependency telemetry + custom metrics + 3 alert definitions
- Phase 5: Canonical docs (`caching-architecture.md` update + new `redis-cache-azure-setup.md`) + ADR-009 lockstep amendment (concise + full) + lessons + R7 backlog

**Timeline**: Multi-week project across 5 sequential phases (Phase 1+2 partly parallel) | **Estimated Effort**: ~65 tasks; high rigor for BFF code + ADR amendments; medium for Bicep + scripts; low for doc updates

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-009 (Redis-First Caching)** — being **amended** by FR-20. Current MUSTs preserved (use `IDistributedCache`, version keys, short security TTLs, MUST NOT cache authz decisions, MUST NOT add L1 without profiling). New MUSTs added: SKU table, KV reference mandate, fail-fast in deployed envs, tenant prefix format, `InstanceName=spaarke:`, Pub/Sub guidance, App Insights mandate, two-tier resource-naming rule
- **ADR-010 (DI Minimalism)** — `ITenantCache` wrapper passes the new-interface justification (≥2 future implementations: default today + future named instances per NFR-12); CLAUDE.md §11 three-question template applied in PR description
- **ADR-013 (AI services bounded concurrency)** — `GraphTokenCache`, `EmbeddingCache` `SemaphoreSlim` rate-limit patterns MUST be preserved across migration
- **ADR-028 (Spaarke Auth v2)** — Redis connection string MUST come from Key Vault via `@Microsoft.KeyVault(VaultName=...;SecretName=...)`; App Service Managed Identity reads
- **ADR-029 (BFF Publish Hygiene)** — publish-size delta ≤+1 MB compressed per task (NFR-04); measure absolute + diff per BFF-touching task
- **ADR-032 (BFF Null-Object Kill-Switch)** — `IConnectionMultiplexer` Null-Object symmetric registration; P3 fail-fast tier rules; asymmetric-registration anti-pattern blocked

**From Spec**:
- ALL direct `IDistributedCache.` call sites in `Sprk.Bff.Api/` migrated atomically in single PR (NFR-07)
- Industry-standard key format `{InstanceName}tenant:{tenantId}:{resource}:{id}:v{version}` (FR-05 / Q-A)
- Canonical resource naming `spaarke-bff-redis-{env}` (top-level env-suffix); sub-resources env-agnostic (NFR-03, NFR-10)
- Production-like dev: `AbortOnConnectFail=true`, fail-fast, no shortcuts (Q-C)
- MUST NOT touch prod or demo environments during execution (NFR-05)
- MUST NOT introduce new BFF endpoints, services beyond the cache wrapper, DI registrations beyond CacheModule changes, or new packages (spec MUST NOT list)
- MUST extend existing `redis.bicep` (not duplicate); MUST leverage existing `RedisValidationTests.ps1`

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Architecture 1 (Platform-only Redis per env) | One Redis per env + mandatory tenant key prefix; wrapper future-extensible for multi-Redis without refactor | Per-customer Redis path in `Provision-Customer.ps1` deprecated (Q-E) |
| `ITenantCache` wrapper API | Mandatory `tenantId` parameter at every cache call → multi-tenant invariant enforced at compile time | All ~199 direct `IDistributedCache` calls migrated; system-level exceptions allow-listed with JSON comment |
| Symmetric Null-Object `IConnectionMultiplexer` registration (ADR-032) | Pub/Sub no-op in in-memory dev mode; `GetDatabase()` throws on accidental direct use | Single-instance limitation documented as known constraint in operational guide (Q-B) |
| `Redis:InstanceName` default `sdap:` → `spaarke:` | Drops deprecated brand; aligns with canonical naming | Dev cutover uses new value; dev Redis is empty so no migration cost (FR-07) |
| Atomic single-PR migration of all call sites (NFR-07) | Prevents read-old/write-new bug class across multi-tenant key space | Phase 1 tasks 010–017 split for review tractability but committed as ONE PR |
| Lockstep ADR-009 amendment (concise + full) | Both `.claude/adr/` and `docs/adr/` agents see the same MUSTs | Single PR updates BOTH files; Last Updated dates match (FR-20) |
| `redis-cache-azure-setup.md` is kebab-case (NEW); `SPAARKE-DEPLOYMENT-GUIDE.md` stays UPPERCASE | NEW files use kebab-case (industry standard); existing UPPERCASE files deferred to broader doc-cleanup (Q-F) | Mixed-case directory accepted in this project; future cleanup is out of scope |

### Discovered Resources

**Applicable ADRs** (loaded for all tasks via `adr-aware` skill):
- [`.claude/adr/ADR-009-redis-caching.md`](../../.claude/adr/ADR-009-redis-caching.md) — concise; **being amended** by FR-20
- [`docs/adr/ADR-009-caching-redis-first.md`](../../docs/adr/ADR-009-caching-redis-first.md) — full; **being amended** by FR-20
- [`.claude/adr/ADR-010-di-minimalism.md`](../../.claude/adr/ADR-010-di-minimalism.md) — `ITenantCache` justification
- [`.claude/adr/ADR-013-*.md`](../../.claude/adr/) — AI services bounded concurrency
- [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Key Vault reference syntax
- [`.claude/adr/ADR-029-bff-publish-hygiene.md`](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — publish-size delta rule
- [`.claude/adr/ADR-032-bff-nullobject-kill-switch.md`](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object symmetric registration

**Applicable Constraints**:
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — §F.1 Asymmetric-Registration Tier 1.5; §F.2 Fixture-Config-FIRST; §F.3 Empirical-Reproduction-FIRST; test-update obligation
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — KV reference, idempotent deploy, publish-size per-task verification

**Applicable Skills** (verified present):
- Code work: `task-execute`, `code-review`, `adr-check`, `spaarke-conventions` (auto), `adr-aware` (auto), `script-aware` (auto)
- Infrastructure: `azure-deploy`, `bff-deploy`
- Docs: `docs-architecture`, `docs-guide`, `docs-procedures`, `docs-standards`
- Lifecycle: `push-to-github`, `worktree-sync`, `merge-to-master`, `repo-cleanup`, `context-handoff`

**Knowledge / patterns**:
- [`.claude/patterns/caching/distributed-cache.md`](../../.claude/patterns/caching/distributed-cache.md) — current pattern (will need update from `sdap:` to `spaarke:`)
- [`.claude/patterns/auth/managed-identity-resource-rbac.md`](../../.claude/patterns/auth/managed-identity-resource-rbac.md) — App Service MI → KV secret read
- `docs/architecture/caching-architecture.md` — to UPDATE (FR-18); current sections: Overview, Component Structure, Cache Types, TTL Tiers, Key Conventions, Invalidation Patterns, Data Flow
- `docs/standards/CODING-STANDARDS.md` + `ANTI-PATTERNS.md` — `IDistributedCache` for cross-request caching; no `.Result`/`.Wait()`; versioned keys

**Canonical reuse references** (tasks point at these — do NOT recreate):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/NullMembershipCacheInvalidator.cs` — Null-Object template to mirror for `NullConnectionMultiplexer`
- `infrastructure/ai-search/deploy-session-files-index.ps1` — post-deploy invariant verification pattern
- `spaarke-bff-prod` App Settings — `@Microsoft.KeyVault(...)` reference pattern
- `scripts/Provision-Customer.ps1:422-492` — source code to extract into `Deploy-RedisCache.ps1`
- `tests/manual/RedisValidationTests.ps1` — extend with tenant + fail-fast checks; do NOT rewrite

**Reality vs. spec — material findings from Phase 1 exploration**:
- **Direct `IDistributedCache.` call sites**: spec says "117"; exploration counts **~199 direct calls across 62 files** in `Sprk.Bff.Api/`. Task 001 produces authoritative inventory; atomic-migration rule (NFR-07) still holds for the actual count.
- **`appsettings.json` `InstanceName`**: spec assumes `sdap:`; reality is `sdap-dev:` (hyphenated dev variant). Find/replace task includes both.
- **`RedisOptions.AllowInMemoryFallback`**: confirmed missing — Phase 1 task adds.
- **`CacheModuleTests.cs`**: confirmed does NOT exist (directory `Infrastructure/DI/` exists but file absent) — Phase 1 task creates from scratch.
- **`redis.bicep` default SKU**: currently Premium (not Basic C0 as spec assumes for dev). `redis-dev.bicepparam` will override; module default unchanged to preserve Premium for prod.
- **`Provision-Customer.ps1` Redis block**: confirmed at lines 422–492 (one line longer than spec's 422–487).
- **`scripts/Deploy-RedisCache.ps1`**: confirmed does NOT exist — Phase 2 task creates.
- **`docs/guides/redis-cache-azure-setup.md`**: confirmed does NOT exist — Phase 5 task creates.
- **`SPAARKE-DEPLOYMENT-GUIDE.md` §4.5**: confirmed not present — Phase 5 task inserts.
- **Legacy `spe-redis-dev-67e2xz` string**: not hardcoded anywhere in committed code (runtime-only state) — decommission is Azure-side only.

### Placement Justification (per CLAUDE.md §10 BFF Hygiene)

**Three-question template applied to the NEW `ITenantCache` interface**:

1. **Existing** — What does this overlap with? The only existing wrapper near this surface is `Spaarke.Core/Cache/DistributedCacheExtensions.cs` (a static helper, not a tenant-scoping abstraction). `IMembershipCacheInvalidator` exists but is Pub/Sub-only. There is no existing tenant-scoping cache contract.
2. **Extension** — Can I extend the existing instead? `DistributedCacheExtensions` could be extended with tenant-scoped overloads, but it's a static class with no DI seam — adding tenant scoping there spreads the multi-tenant invariant across static helpers vs. a single injectable. A DI-resolvable `ITenantCache` makes the invariant testable (mock at the seam) and lets us emit metrics (FR-16) at the wrapper boundary in one place.
3. **Cost-of-doing-nothing** — Concrete failure modes without `ITenantCache`: (a) any new cache call could omit the `tenant:` prefix and corrupt the multi-tenant key space — the read-old/write-new bug class NFR-07 explicitly names; (b) no central seam for App Insights custom metrics (`cache.hits`, `cache.misses`, `cache.hit_rate`, `cache.redis_p95_ms`) per FR-16; (c) the future multi-Redis pattern (NFR-12) has no `cacheInstance` parameter to add to.

Conclusion: `ITenantCache` is justified. ≥2 future implementations satisfy ADR-010 (default today + future named instances per NFR-12). PR description repeats this justification verbatim.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: CacheModule hardening + wrapper + atomic call-site migration (tasks 001–019)
└─ One-line AbortOnConnectFail fix + fail-fast + env-guarded fallback
└─ Null-Object IConnectionMultiplexer + symmetric DI
└─ ITenantCache wrapper + atomic migration of ~199 call sites (single PR)

Phase 2: Bicep + provisioning artifacts (tasks 020–029)
└─ Extend redis.bicep + new .bicepparam (dev/staging/prod)
└─ NEW Deploy-RedisCache.ps1 extracted from Provision-Customer.ps1:422-492
└─ Extend RedisValidationTests.ps1 with tenant + fail-fast assertions

Phase 3: Dev cutover (tasks 030–039)
└─ Provision spaarke-bff-redis-dev (Basic C0)
└─ KV secret upsert + App Settings update + restart + verify
└─ 24-hr window + legacy decommission + sister-project handoff

Phase 4: Observability (tasks 040–044)
└─ App Insights Redis dependency telemetry
└─ Custom metrics emission from ITenantCache wrapper
└─ 3 alert definitions

Phase 5: Docs + ADR amendments + lessons + R7 backlog (tasks 050–065)
└─ UPDATE caching-architecture.md (Tenant Isolation, Multi-instance, Failure Mode Catalog)
└─ NEW docs/guides/redis-cache-azure-setup.md operational runbook
└─ Lockstep ADR-009 amendment (concise + full)
└─ SPAARKE-DEPLOYMENT-GUIDE.md §4.5; secret rotation; lessons-learned; R7 backlog
```

### Critical Path

**Blocking Dependencies:**
- Phase 3 BLOCKED BY Phase 1 (cutover requires hardened CacheModule)
- Phase 3 BLOCKED BY Phase 2 (cutover requires `Deploy-RedisCache.ps1`)
- Phase 4 BLOCKED BY Phase 3 (cannot verify dependency telemetry without real Redis traffic)
- Sister project `spaarke-ai-azure-setup-dev-r1` Phase 3 BLOCKED BY this project's Phase 3 cutover (NFR-11)
- Task 005 (symmetric DI registration) BLOCKED BY 003 (CacheModule update) + 004 (NullConnectionMultiplexer creation)
- Tasks 010–017 (atomic call-site migration) BLOCKED BY 006 (`ITenantCache` interface available)
- Tasks 052 + 053 (ADR-009 lockstep amendment) BLOCKED BY 050 (architecture doc update establishes vocabulary)

**Parallelism opportunities**:
- Phase 2 (tasks 020–029) can largely run in parallel with Phase 1 — pure infrastructure work, no BFF code dep until task 025 references `Deploy-RedisCache.ps1` output
- Phase 5 doc tasks (050, 051) can start in parallel with Phase 1 — they document the design, not the implementation outcome
- Tasks 040 + 043 in Phase 4 are parallel-safe (telemetry verification vs. alert definition authoring)

**High-Risk Items:**
- PR #253 (Redis NuGet package bump) overlapping — Mitigation: pin Phase 1 to current version range; rebase Phase 1 PR after #253 merges
- Atomic migration of ~199 call sites (review tractability) — Mitigation: tasks 010–017 split by sub-area for diff readability but committed as ONE PR; final `grep` verification gate (task 018)
- Asymmetric DI registration anti-pattern slipping past code review — Mitigation: `bff-extensions.md` §F.1 static-scan recipe in task 005 + ADR-032 P3 enforcement in code-review skill
- Dev KV name ambiguity (`spaarke-spekvcert` vs `sprkspaarkedev-aif-kv`) — Mitigation: task 030 captures actual dev KV in cutover baseline before task 032 secret upsert

---

## 4. Phase Breakdown

### Phase 1: CacheModule hardening + wrapper + atomic migration (tasks 001–019)

**Objectives:**
1. Make dev BFF production-like for caching (AbortOnConnectFail=true; fail-fast; env-guarded fallback)
2. Introduce tenant-key-isolation wrapper that compile-time-enforces multi-tenant invariant
3. Atomically migrate every direct `IDistributedCache` call in `Sprk.Bff.Api/` to the wrapper

**Deliverables:**
- [ ] Task 001: `notes/cache-call-site-inventory.md` — authoritative count + grouping of ~199 sites by file
- [ ] Task 002: `RedisOptions.AllowInMemoryFallback` property + template schema
- [ ] Task 003: `CacheModule.cs:31` fix + 4-branch logic (FR-01..03)
- [ ] Task 004: `Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs` (FR-04)
- [ ] Task 005: Symmetric DI registration verified via §F.1 static scan
- [ ] Task 006: `Infrastructure/Cache/ITenantCache.cs` + default implementation (FR-05)
- [ ] Task 007: `Spaarke.Core/Cache/DistributedCacheExtensions.cs` prefix update (FR-07)
- [ ] Task 008: `appsettings.json` + `appsettings.template.json` `InstanceName` updates (FR-07)
- [ ] Task 009: `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs` — 4 branches + Null-Object pub/sub + GetDatabase throws (FR-08)
- [ ] Tasks 010–017: Atomic call-site migration (Office, Chat, Membership, Document/AI, Background jobs, Auth/User, Shared, system-exception allow-list) — ONE PR (NFR-07)
- [ ] Task 018: Final `grep` verification — ZERO direct calls outside wrapper + tests (FR-06)
- [ ] Task 019: Phase 1 `dotnet build` + `dotnet test` + publish-size delta report (ADR-029)

**Critical Tasks:** Task 001 (inventory) MUST BE FIRST — its output sizes tasks 010–017.

**Inputs**: `spec.md`, ADR-009, ADR-010, ADR-013, ADR-029, ADR-032, `bff-extensions.md`, existing source under `src/server/api/Sprk.Bff.Api/`.

**Outputs**: PR with all Phase 1 changes; `notes/cache-call-site-inventory.md`; `notes/phase-1-publish-size-delta.md`.

### Phase 2: Bicep + provisioning artifacts (tasks 020–029)

**Objectives:**
1. Make `redis.bicep` parameterization complete + audited for drift across env-specific files
2. Extract `Deploy-RedisCache.ps1` from `Provision-Customer.ps1`; deprecate per-customer Redis
3. Make post-deploy verification leverage existing `RedisValidationTests.ps1` (extend; do not rewrite)

**Deliverables:**
- [ ] Task 020: `redis.bicep` parameter audit (FR-09)
- [ ] Task 021: `infrastructure/bicep/parameters/` drift audit + fix (FR-10)
- [ ] Tasks 022–024: `redis-dev.bicepparam` (Basic C0), `redis-staging.bicepparam` (Standard C0), `redis-prod.bicepparam` (Standard C2+ / Premium)
- [ ] Task 025: NEW `scripts/Deploy-RedisCache.ps1` — idempotent, `-Environment`, `-WhatIf`, `-VerifyOnly`, `-CutoverBffSettings`, KV upsert (FR-11, NFR-01, NFR-06)
- [ ] Task 026: Extend `tests/manual/RedisValidationTests.ps1` — tenant key-format check + fail-fast assertion (NFR-02)
- [ ] Task 027: Refactor `scripts/Provision-Customer.ps1:422-492` to call `Deploy-RedisCache.ps1` (FR-12)
- [ ] Task 028: `Deploy-RedisCache.ps1 -Environment dev -WhatIf` integration check
- [ ] Task 029: Phase 2 review (PSScriptAnalyzer if configured; no BFF code → skip publish-size)

**Critical Tasks:** Task 025 BLOCKS tasks 027, 028, and all of Phase 3.

**Inputs**: existing `redis.bicep`, `Provision-Customer.ps1`, `RedisValidationTests.ps1`, `infrastructure/ai-search/deploy-session-files-index.ps1` as reference pattern.

**Outputs**: 3 new `.bicepparam` files, `Deploy-RedisCache.ps1`, extended `RedisValidationTests.ps1`, refactored `Provision-Customer.ps1`.

### Phase 3: Dev environment cutover (tasks 030–039)

**Objectives:**
1. Provision `spaarke-bff-redis-dev` (Basic C0) and cut dev BFF over to it via Key Vault reference
2. Verify production-like behavior in dev (startup log, smoke test, no in-memory warning)
3. Decommission legacy after 24-hr window; signal sister project that gate is open

**Deliverables:**
- [ ] Task 030: `notes/dev-cutover-baseline.md` (current App Settings, KV name confirmation, MI permissions)
- [ ] Task 031: Provision `spaarke-bff-redis-dev` via `Deploy-RedisCache.ps1 -Environment dev` (FR-13)
- [ ] Task 032: KV `Redis-ConnectionString` secret upsert (FR-14)
- [ ] Task 033: Update `spaarke-bff-dev` App Settings (`Redis__Enabled=true`, `InstanceName=spaarke:`, KV ref, `AllowInMemoryFallback=false`) (FR-14)
- [ ] Task 034: BFF restart + `/healthz` + startup log verification — **Success Criterion #1** + sister-project gate signal (FR-14)
- [ ] Task 035: Smoke test — `spaarke:tenant:{tenantId}:session:{id}:v1` key observed (FR-14)
- [ ] Task 036: 24-hr verification window — zero errors, telemetry green
- [ ] Task 037: Legacy `spe-redis-dev-67e2xz` decommission (FR-15)
- [ ] Task 038: Sister project handoff — append cutover record to sister project notes
- [ ] Task 039: Phase 3 retro — runbook deviations documented for Phase 5 lessons-learned

**Critical Tasks:** Task 034 is the **gate signal** for sister project NFR-13. Task 037 cannot start before 24-hr window completes (FR-15).

**Inputs**: Phase 1 PR merged, `Deploy-RedisCache.ps1`, Azure CLI logged in with appropriate permissions.

**Outputs**: Running `spaarke-bff-redis-dev`; dev BFF cut over; `notes/dev-cutover-baseline.md` + `notes/phase-3-retro.md`; sister project handoff signal.

### Phase 4: Observability (tasks 040–044)

**Objectives:**
1. Verify App Insights captures Redis dependency calls
2. Emit custom metrics from `ITenantCache` wrapper
3. Document + (where applicable) deploy 3 alert definitions

**Deliverables:**
- [ ] Task 040: App Insights Redis dependency telemetry verification in Live Metrics (FR-16)
- [ ] Task 041: Custom metrics emission (`cache.hits`, `cache.misses`, `cache.hit_rate`, `cache.redis_p95_ms` with `resource` dimension) from wrapper (FR-16)
- [ ] Task 042: Metrics visible in App Insights metrics explorer; reference workbook section
- [ ] Task 043: 3 alert definitions documented in `redis-cache-azure-setup.md`; deployed via Bicep `alerts.bicep` or App Insights workbook if applicable (FR-17)
- [ ] Task 044: Phase 4 publish-size delta report (added metrics emission code)

**Critical Tasks:** Task 041 BLOCKS task 042 (cannot verify metrics visibility without emission); tasks 040 + 043 parallel-safe.

**Inputs**: Phase 3 complete, App Insights connected, `ITenantCache` wrapper in production code.

**Outputs**: PR with metrics emission code; alert definitions section in operational guide.

### Phase 5: Canonical docs + ADR amendments + lessons + R7 backlog (tasks 050–065)

**Objectives:**
1. Update `caching-architecture.md` with tenant isolation, multi-instance behavior, instance registry, failure mode catalog
2. Author NEW `redis-cache-azure-setup.md` operational runbook (<30-min provision target)
3. Amend ADR-009 in lockstep (concise + full); update deployment guide; secret rotation; lessons; R7 backlog

**Deliverables:**
- [ ] Task 050: UPDATE `docs/architecture/caching-architecture.md` (FR-18)
- [ ] Task 051: NEW `docs/guides/redis-cache-azure-setup.md` (FR-19)
- [ ] Task 052: UPDATE `.claude/adr/ADR-009-redis-caching.md` (concise) — operational MUSTs (FR-20)
- [ ] Task 053: UPDATE `docs/adr/ADR-009-caching-redis-first.md` (full) — lockstep with task 052 (FR-20)
- [ ] Task 054: UPDATE `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` — §4.5 + Appendix D entry (FR-21)
- [ ] Task 055: Secret-rotation procedure section in `redis-cache-azure-setup.md` (FR-22)
- [ ] Task 056: Lessons-learned section — drift origin + guardrails (FR-22)
- [ ] Task 057: `notes/r7-backlog.md` — S1–S4 deferred items (FR-22)
- [ ] Task 058: Doc-drift sweep — `grep -r "sdap:" docs/` returns ZERO post-update
- [ ] Tasks 059–064: Pre-merge gates — final `code-review`, `adr-check`, publish-size final report, `dotnet test`, conflict-check, push-to-github update
- [ ] Task 065: Project wrap-up (`090-project-wrap-up.poml`) — update README to Complete, finalize lessons, archive

**Critical Tasks:** Tasks 052 + 053 lockstep — single PR; do not merge separately. Task 052 touches `.claude/` — main-session-only (sub-agent write boundary).

**Inputs**: Phase 1–4 outcomes (vocabulary + measurements + decisions feed docs + lessons).

**Outputs**: Updated docs + amended ADRs + R7 backlog + project Complete status.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure Cache for Redis (Basic SKU available in `westus2`) | GA | Low | Falls back to nearest region if quota issue surfaces |
| `spaarke-bff-dev` App Service (P1v3) | Running (confirmed 2026-06-25) | Low | If down, Phase 3 paused until restored |
| `spaarke-spekvcert` (or alternate) Key Vault | Assumed accessible | Low | Task 030 verifies actual KV in cutover baseline |
| App Service Managed Identity → KV secret read | To verify in Phase 3 | Low | Grant role if missing (small task in Phase 3) |
| App Insights connection on dev BFF (`APPLICATIONINSIGHTS_CONNECTION_STRING`) | Assumed configured | Low | Phase 4 task verifies; provisions App Insights if missing |
| Azure CLI logged in with: Redis create/delete, KV secret modify, App Service config modify, `spe-infrastructure-westus2` + `rg-spaarke-dev` | Required | Low | Phase 0 / 3 pre-flight |
| PR #253 (Redis NuGet bump) | Open | Med | Coordinate; rebase Phase 1 PR if #253 merges first |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `infrastructure/bicep/modules/redis.bicep` | Verified exists | Current (to EXTEND) |
| `scripts/Provision-Customer.ps1:422-492` | Verified exists | Current (to REFACTOR — extract to `Deploy-RedisCache.ps1`) |
| `tests/manual/RedisValidationTests.ps1` | Verified exists | Current (to EXTEND) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CacheModule.cs` | Verified exists | Current (to MODIFY) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/NullMembershipCacheInvalidator.cs` | Verified exists | Reference pattern for Null-Object |
| ADR-009 (both concise + full) | Verified exists | Current (Last Updated 2025-12-18 / 2025-12-04) — to AMEND |
| `docs/architecture/caching-architecture.md` | Verified exists, Last 2026-04-05 | Current (to UPDATE) |
| `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md` | Verified exists | Current (to UPDATE §4.5) |
| Sister project `spaarke-ai-azure-setup-dev-r1/spec.md` | Verified exists | Codifies NFR-13 sequencing dependency on this project |

---

## 6. Testing Strategy

**Unit Tests** (target: cover all four `CacheModule` branches + Null-Object semantics):
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs` — Redis-on, Redis-off-AllowFallback-Dev, Redis-off-AllowFallback-non-Dev throws, Redis-off-NoFallback throws (FR-08, NFR-13)
- Null-Object `IConnectionMultiplexer`: `GetSubscriber().Publish` is no-op, `GetSubscriber().Subscribe` is no-op, `GetDatabase()` throws `NotSupportedException`
- `ITenantCache` wrapper: every operation produces a key matching `spaarke:tenant:{tenantId}:{resource}:{id}:v{version}`
- Existing AI service caches (`GraphTokenCache`, `EmbeddingCache`): preserve `SemaphoreSlim` rate-limit invariants after migration

**Integration Tests**:
- Simulated unreachable Redis at startup in deployed env config → assert startup failure with correct error message (per Success Criterion #4)
- Dev cutover smoke test (task 035): chat session creation produces expected key in Redis (verified via `az redis cli` or App Insights dependency)

**E2E / Manual Verification**:
- Dev BFF post-cutover: `/healthz` 200, startup log line, 24-hr error-free window (task 036)
- `Deploy-RedisCache.ps1 -Environment dev -WhatIf` produces accurate plan output (task 028)
- Operator dry-run of `redis-cache-azure-setup.md` (post-project) — <30-min provision target (Success Criterion #6)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] `CacheModule.cs:31` `AbortOnConnectFail = true`
- [ ] All four CacheModule branches covered by unit tests; `dotnet test` passes
- [ ] `grep -r "IDistributedCache\." src/server/api/Sprk.Bff.Api/` returns ZERO outside wrapper + tests
- [ ] `grep -r "sdap:" src/server/api/Sprk.Bff.Api/` returns ZERO
- [ ] Publish-size delta ≤+1 MB compressed (vs. baseline at branch start)
- [ ] PR description includes three-question justification for `ITenantCache` per CLAUDE.md §11

**Phase 2:**
- [ ] `redis.bicep` parameters complete (FR-09 list); module file unchanged in identity (extend, not duplicate)
- [ ] `Deploy-RedisCache.ps1 -Environment dev -WhatIf` produces a plan; `-VerifyOnly` returns non-zero exit on missing/bad instance
- [ ] `Provision-Customer.ps1` Redis block replaced with call to `Deploy-RedisCache.ps1`
- [ ] PSScriptAnalyzer (if configured) passes on new scripts

**Phase 3:**
- [ ] `az redis show -g spe-infrastructure-westus2 -n spaarke-bff-redis-dev` returns running Basic C0
- [ ] Dev BFF startup log line: `"Distributed cache: Redis enabled with instance name 'spaarke:'"` — NO in-memory warning
- [ ] App Settings show `@Microsoft.KeyVault(...)` for `ConnectionStrings__Redis`
- [ ] Smoke test produces expected tenant-prefixed key
- [ ] 24-hr window: zero errors in App Insights
- [ ] Legacy resource decommissioned / tagged

**Phase 4:**
- [ ] App Insights Live Metrics shows Redis dependency calls
- [ ] Custom metrics visible in App Insights metrics explorer
- [ ] 3 alert definitions documented; deployed if applicable

**Phase 5:**
- [ ] `caching-architecture.md` includes Tenant Isolation, Multi-instance Behavior, Cache Instance Registry, Failure Mode Catalog
- [ ] `redis-cache-azure-setup.md` exists at `docs/guides/`
- [ ] ADR-009 amended in BOTH files with matching Last Updated dates
- [ ] `SPAARKE-DEPLOYMENT-GUIDE.md` §4.5 present; Appendix D updated
- [ ] R7 backlog file exists with S1–S4 entries

### Business Acceptance

- [ ] Sister project `spaarke-ai-azure-setup-dev-r1` can begin its Phase 3 (gate signal cleared)
- [ ] An operator unfamiliar with this project can provision a new env Redis end-to-end from `redis-cache-azure-setup.md` in <30 min

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Call-site count larger than spec (199 vs 117) inflates Phase 1 effort | High (confirmed) | Med | Task 001 produces authoritative inventory; tasks 010–017 sized from real count |
| R2 | PR #253 (Redis NuGet bump) merges mid-Phase 1 | Med | Med | Coordinate; rebase Phase 1 PR if needed; pin to current version range until then |
| R3 | Asymmetric DI registration leaks past code review | Low | High (runtime null-ref) | §F.1 static-scan recipe in task 005; ADR-032 P3 enforcement; CI builds in subsequent waves catch breakage early |
| R4 | Hidden non-tenant-scoped system keys complicate atomic migration | Med | Med | Task 001 inventory flags system keys; NFR-08 allow-list explicit with JSON comment per call site |
| R5 | Dev KV name differs from assumption (`spaarke-spekvcert`) | Low | Low | Task 030 captures actual KV in cutover baseline before secret upsert |
| R6 | Publish-size delta exceeds NFR-04 +1 MB | Low | Low | Tasks 019, 044 measure; expected +0.1 MB |
| R7 | App Insights connection missing on dev BFF | Low | Med | Phase 4 task verifies; provisions App Insights if absent (small additive) |
| R8 | Sister project Phase 3 timeline slips waiting on this project | Med (cross-project) | Med | Phase 1+2 partially parallel; Phase 3 dev-only (fast); sister project can do Phase 1+2 in parallel |
| R9 | Doc drift remains after FR-07 (stray `sdap:` references) | Low | Low | Task 058 doc-drift sweep grep verifies |
| R10 | Failed Bicep deploy leaves dev in half-state | Low | High | `Deploy-RedisCache.ps1` is idempotent (NFR-01); `-VerifyOnly` for diagnostic; rollback procedure in `redis-cache-azure-setup.md` |

---

## 9. Next Steps

1. **Review this plan.md** + spec.md for accuracy and discovered-resources completeness
2. **Run** `/task-create spaarke-redis-cache-remediation-r1` to generate ~65 POML task files + `TASK-INDEX.md`
3. **Commit + push** the generated artifacts on `work/spaarke-redis-cache-remediation-r1`
4. **Begin** Phase 1 with task 001 (inventory) — read-only; no code changes — via `/task-execute`

---

**Status**: Ready for `task-create`.
**Next Action**: Invoke `task-create` to decompose this plan into executable POML task files.

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. The Phase Breakdown section (§4) is the authoritative WBS for task generation.*
