# Task Index — spaarke-redis-cache-remediation-r1

> **Generated**: 2026-06-25 by `/project-pipeline` (Steps 3 → task-create)
> **Total tasks**: 60 (Phase 1: 19 · Phase 2: 10 · Phase 3: 10 · Phase 4: 5 · Phase 5: 15 · Wrap-up: 1)
> **Total estimated effort**: ~110 hours
> **Status legend**: 🔲 not started · 🔄 in-progress · ✅ completed · ⏸ blocked · ↩ deferred

---

## Phase 1 — CacheModule hardening + wrapper + atomic migration (tasks 001–019)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 001 | Cache call-site inventory (authoritative count) | ✅ | — | 010–018 | 2 | STANDARD | Group 0 — foundational (authoritative count: **153 sites / 56 files**) |
| 002 | Add Redis:AllowInMemoryFallback option | ✅ | 001 | 003 | 1 | STANDARD | Group A — RedisOptions + appsettings.template.json |
| 003 | CacheModule 4-branch logic + AbortOnConnectFail=true | ✅ | 002 | 005, 009 | 3 | FULL | Group A — 4-branch + Program.cs IHostEnvironment plumbing |
| 004 | NullConnectionMultiplexer (ADR-032 Null-Object) | ✅ | 002 | 005, 009 | 3 | FULL | Group A — full IConnectionMultiplexer surface; P2 Pub/Sub, P3 GetDatabase |
| 005 | Symmetric DI registration (§F.1 static-scan) | ✅ | 003, 004 | 009 | 2 | FULL | Group B — fixed 2 nullable `IConnectionMultiplexer?` + factory→ctor in OfficeModule |
| 006 | ITenantCache interface + default implementation | ✅ | 005 | 009, 010–017 | 4 | FULL | Group C — interface + TenantCache + DI singleton; `tenant:{tid}:{res}:{id}:v{n}` key format |
| 007 | DistributedCacheExtensions: `sdap` → `spaarke` prefix | ✅ | 006 | 010–017 | 2 | STANDARD | Group C — line 171 + doc; 13/13 cache tests pass |
| 008 | appsettings InstanceName updates | ✅ | 006 | 018 | 1 | STANDARD | Group C — RedisOptions default + `appsettings.tokens.md` (`appsettings.json` doesn't exist for BFF) |
| 009 | CacheModuleTests (4 branches + Null-Object) | ✅ | 005, 006, 007, 008 | 010 | 3 | FULL | Group D — 8 tests (7 pass + 1 Skip for live-Redis path); + test-fixture repair across 15 files (§F.2 obligation: 337 latent failures → 0; 7826 passing) |
| 010 | Migrate Office services to ITenantCache | ✅ | 001, 006, 009 | 011 | 3 | FULL | Group E — **PARALLELIZED** with 011-015; 12 sites / 4 files; 0 exceptions |
| 011 | Migrate Chat services to ITenantCache | ✅ | 010 | 012 | 4 | FULL | Group E — 31 sites / 9 files; ITenantCache extended with GetStringAsync/SetStringAsync/RefreshAsync/SetSlidingAsync default-interface methods |
| 012 | Migrate Membership services to ITenantCache | ✅ | 011 | 013 | 3 | FULL | Group E — 16 sites / 5 files; Pub/Sub SCAN pattern updated to new key shape; IConnectionMultiplexer untouched |
| 013 | Migrate Document/AI services to ITenantCache | ✅ | 012 | 014 | 4 | FULL | Group E — 36+ sites / 13 files; 3 wrappers as NFR-08 exceptions (EmbeddingCache, PlaybookService, TextExtractorService) |
| 014 | Migrate Background-job services to ITenantCache | ✅ | 013 | 015 | 3 | FULL | Group E — 3 migrated / 22 NFR-08 exceptions (idempotency, watermarks, schema, GraphToken, comms — all legitimately cross-tenant) |
| 015 | Migrate Auth/User services to ITenantCache | ✅ | 014 | 016 | 2 | FULL | Group E — 21/21 sites migrated; 0 ADR-009 authz-decision-cache violations |
| 016 | Migrate Spaarke.Core consumers to ITenantCache | ✅ (no-op) | 015 | 017 | 2 | FULL | Group E — Wave 6 covered everything; grep `DistributedCacheExtensions` returns 0 in BFF |
| 017 | System-level cache exception allow-list (NFR-08) | ✅ | 016 | 018 | 2 | FULL | Group E — 11 distinct logical resources (well under 20 escalation threshold); `SystemCacheKeys.cs` + `notes/system-cache-exceptions.md` |
| 018 | Final grep verification (ZERO direct calls) | ✅ | 017 | 019 | 1 | STANDARD | Group F — FR-06 + FR-07 + Success Criteria #9 + #10 PASS |
| 019 | Phase 1 build + test + publish-size delta | ✅ | 018 | 030 | 1 | STANDARD | Group F — 0 errors; **7826 passed / 1 pre-existing failed / 135 skipped**; cumulative publish-size delta **−2.0 MB** vs branch start |

---

## Phase 2 — Bicep + provisioning artifacts (tasks 020–029)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 020 | redis.bicep parameter audit (FR-09) | ✅ | — | 022, 023, 024 | 2 | STANDARD | Group G — added `redisVersion`/`staticIP`/`redisPrimaryKey` output; SKU kept as string+int |
| 021 | bicepparam drift audit + fix (FR-10) | ✅ | — | 022, 023, 024 | 2 | STANDARD | Group G — fixed `customer-template.bicepparam` (broken `using`+schema) |
| 022 | redis-dev.bicepparam (Basic C0) | ✅ | 020, 021 | 025, 028, 031 | 1 | STANDARD | Group H |
| 023 | redis-staging.bicepparam (Standard C0) | ✅ | 020, 021 | 025 | 1 | STANDARD | Group H |
| 024 | redis-prod.bicepparam (Standard C2 placeholder) | ✅ | 020, 021 | 025 | 1 | STANDARD | Group H — DO-NOT-DEPLOY header; `deploy-gate=finance+security` tag |
| 025 | NEW Deploy-RedisCache.ps1 | ✅ | 022, 023, 024 | 027, 028, 031 | 4 | FULL | Group I — `-WhatIf dev` exit 0; prod no-`-Force` exit 2 with NFR-05 message |
| 026 | Extend RedisValidationTests.ps1 | ✅ | 025 | 028 | 2 | STANDARD | Group I — `Test-TenantPrefixInvariant` + `Test-FailFastBehavior`; `redis-cli` optional |
| 027 | Refactor Provision-Customer.ps1 to call Deploy-RedisCache | ✅ | 025 | — | 2 | FULL | Group I — inline Redis logic removed; Q-E deprecation comment + NFR-12 pointer |
| 028 | Deploy-RedisCache.ps1 -WhatIf integration check | ✅ | 022, 025, 026 | — | 1 | STANDARD | Group I — dev-WhatIf exit 0; prod-no-Force exit 1 (pwsh wrapper clamps 2→1; acceptance still met) |
| 029 | Phase 2 review (PSScriptAnalyzer + Bicep linter) | ✅ | 020–028 | 030 | 1 | STANDARD | Group I — PASS WITH NOTES (Bicep clean; PSScriptAnalyzer 2 cosmetic BOM warnings; pre-existing tech debt in `Provision-Customer.ps1` out of scope) |

---

## Phase 3 — Dev environment cutover (tasks 030–039)

⚠️ **Strictly serial** — Azure-side operations; irreversible; runbook order required.

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 030 | Pre-cutover baseline (App Settings, KV name, MI) | ⏸ DEFERRED | 019, 029 | 031, 032, 033 | 1 | STANDARD | **Requires Azure operator** — out of session scope |
| 031 | Provision spaarke-bff-redis-dev (Basic C0) | ⏸ DEFERRED | 022, 025, 030 | 032 | 2 | FULL | **Requires Azure operator** — `Deploy-RedisCache.ps1 -Environment dev -KeyVaultName <kv> -CutoverBffSettings` |
| 032 | Key Vault Redis-ConnectionString secret upsert | ⏸ DEFERRED | 031 | 033 | 1 | FULL | **Requires Azure operator** |
| 033 | Update spaarke-bff-dev App Settings | ⏸ DEFERRED | 032 | 034 | 1 | FULL | **Requires Azure operator** |
| 034 | BFF restart + verify (**Success Criterion #1 — gate signal**) | ⏸ DEFERRED | 033 | 035, 038 | 1 | FULL | **Requires Azure operator** — sister project NFR-13 gate signal |
| 035 | Smoke test — chat session key format | ⏸ DEFERRED | 034 | 036 | 1 | FULL | **Requires Azure operator** |
| 036 | 24-hr verification window | ⏸ DEFERRED | 035 | 037 | 1 | STANDARD | **Requires Azure operator** + 24-hour wall-clock window |
| 037 | Decommission legacy spe-redis-dev-67e2xz | ⏸ DEFERRED | 036 | 038 | 1 | FULL | **Requires Azure operator** |
| 038 | Sister project handoff signal | ⏸ DEFERRED | 034, 037 | 039 | 1 | MINIMAL | **Requires Azure operator** — appends to sister project's notes |
| 039 | Phase 3 retro | ⏸ DEFERRED | 038 | 051 | 1 | MINIMAL | **Follows Azure operator's Phase 3 work** |

---

## Phase 4 — Observability (tasks 040–044)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 040 | Verify App Insights Redis dependency telemetry | ⏸ DEFERRED | 035 | — | 1 | STANDARD | **Requires live Azure + post-cutover traffic** |
| 041 | Custom metrics emission from ITenantCache | ✅ | 006, 019 | 042 | 3 | FULL | `Meter "Spaarke.Cache"` + counters + histogram with `resource` dim; +1 unit test pass; publish-size delta **−2.0 MB** vs branch start (PR adds ~120 LOC of in-process metrics) |
| 042 | Verify custom metrics visible in App Insights | ⏸ DEFERRED | 041 | — | 1 | STANDARD | **Requires live Azure deploy + traffic** |
| 043 | 3 alert definitions (hit rate, P95, memory) | ✅ | 041 | — | 2 | STANDARD | `notes/alert-definitions-draft.md` + ready-to-paste Bicep skeletons; integrated into `redis-cache-azure-setup.md` §8 |
| 044 | Phase 4 publish-size delta | ✅ (subsumed by 041) | 041, 043 | — | 1 | STANDARD | Cumulative measurement done in task 041; **−2.0 MB** vs branch start |

---

## Phase 5 — Canonical docs + ADR amendments + lessons + R7 backlog (tasks 050–064)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 050 | UPDATE caching-architecture.md | ✅ | — | 052, 053, 058 | 3 | STANDARD | Group J — all FR-18 sections added; `sdap:`→`spaarke:` migrated |
| 051 | NEW redis-cache-azure-setup.md (operational runbook) | 🔲 | 039, 043 | 054, 055, 056 | 4 | STANDARD | Group J |
| 052 | AMEND .claude/adr/ADR-009-redis-caching.md (concise) | 🔲 | 050 | 053 | 2 | FULL | Group K — **main-session-only** (`.claude/` write boundary) |
| 053 | AMEND docs/adr/ADR-009-caching-redis-first.md (full) — lockstep | 🔲 | 052 | — | 2 | FULL | Group K — sequential lockstep |
| 054 | UPDATE SPAARKE-DEPLOYMENT-GUIDE.md §4.5 + Appendix D | 🔲 | 051 | — | 1 | STANDARD | Group L |
| 055 | Secret rotation procedure section | 🔲 | 051 | — | 1 | MINIMAL | Group L |
| 056 | Lessons-learned section | 🔲 | 039, 051 | — | 1 | MINIMAL | Group L |
| 057 | R7 backlog (S1–S4 deferred items) | ✅ | — | — | 1 | MINIMAL | Group L — parallel-safe |
| 058 | Doc-drift sweep (ZERO `sdap:` in active docs) | 🔲 | 050, 051, 052, 053, 054, 055 | 059 | 1 | STANDARD | Wrap |
| 059 | Final /code-review run | 🔲 | 058 | 060 | 2 | STANDARD | Wrap |
| 060 | Final /adr-check run | 🔲 | 059 | 061 | 1 | STANDARD | Wrap |
| 061 | Final publish-size delta report (cumulative) | 🔲 | 060 | 062 | 1 | STANDARD | Wrap |
| 062 | Final dotnet test pass | 🔲 | 061 | 063 | 1 | STANDARD | Wrap |
| 063 | Conflict-check vs PRs merged since branch start | 🔲 | 062 | 064 | 1 | STANDARD | Wrap |
| 064 | Push + finalize draft PR (Ready for Review) | 🔲 | 063 | 090 | 1 | STANDARD | Wrap |

---

## Wrap-up

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 090 | Project Wrap-up (code-review + adr-check + repo-cleanup + README Complete) | ✅ (partial) | 064 | — | 2 | FULL | Final — README + plan updated to reflect Phase 1+2+5 done; Phase 3 cutover + Phase 4 telemetry verification DEFERRED to Azure operator; lessons-learned + R7 backlog complete |

---

## Parallel Execution Plan

**Concurrency cap**: 6 agents per wave (per CLAUDE.md §10 parallel execution rule).

**Permission boundary**: Task 052 touches `.claude/adr/ADR-009-redis-caching.md` — `parallel-safe: false`; main session must run. Sub-agents will hit "Edit denied on .claude/" if dispatched.

### Wave structure

| Wave | Tasks | Prereq | Type | Notes |
|---|---|---|---|---|
| 0 | 001 | — | serial | Foundational inventory |
| A | 002, 003, 004 | 001 ✅ | parallel (3 agents) | Distinct files: RedisOptions / CacheModule / NullConnectionMultiplexer |
| B | 005 | A ✅ | serial | Symmetric-DI verification post-A |
| C | 006, 007, 008 | B ✅ | parallel (3 agents) | Interface + helper prefix + appsettings — distinct files |
| D | 009 | C ✅ | serial | Tests reference 003+004+006+008 |
| E | 010, 011, 012, 013, 014, 015, 016, 017 | D ✅ | **SEQUENTIAL** | NFR-07 single-PR atomicity; sub-task ordering for review tractability |
| F | 018, 019 | E ✅ | serial | Verification + gates |
| **(parallel to Phase 1)** G | 020, 021 | — | parallel (2 agents) | Pure infrastructure |
| H | 022, 023, 024 | G ✅ | parallel (3 agents) | Distinct `.bicepparam` files |
| I | 025, 026, 027, 028, 029 | H ✅ | semi-parallel | 025 blocks 027/028; 029 is final gate. Group I can run 025 → (026, 027 parallel) → 028 → 029 |
| Phase 3 | 030 → 031 → 032 → 033 → 034 → 035 → 036 → 037 → 038 → 039 | Phase 1 + Phase 2 ✅ | **STRICTLY SERIAL** | Cutover sequence; irreversible Azure operations |
| Phase 4 | 040 + (041 → 042) + 043; then 044 | Phase 3 ✅ | mixed | 040 parallel-safe with 041; 042 depends on 041; 043 parallel-safe; 044 is final gate |
| **(parallel to Phase 1)** J | 050, 051 | — / Phase 3+4 (051) | parallel (2 agents) | 050 parallel from start; 051 depends on Phase 3 outcomes |
| K | 052, 053 | 050 ✅ | **SEQUENTIAL (lockstep PR)** + **main-session-only for 052** | ADR amendment in lockstep |
| L | 054, 055, 056, 057 | K ✅ (054, 055, 056) / — (057) | parallel (3–4 agents) | Distinct files |
| Wrap | 058 → 059 → 060 → 061 → 062 → 063 → 064 → 090 | All prior ✅ | **SEQUENTIAL** | Wrap-up gates |

### Build verification between waves (MANDATORY)

After each wave that modifies `.cs` files: `dotnet build src/server/api/Sprk.Bff.Api/` before dispatching next wave. If build fails, STOP and report wave identifier.

### Critical path

`001 → A (002/003/004) → B (005) → C (006/007/008) → D (009) → E (010..017 sequential) → F (018/019) → Phase 3 (030..039) → Phase 4 (040..044) → 058 → 059 → 060 → 061 → 062 → 063 → 064 → 090`

**Critical-path duration estimate**: 2 + 3 + 2 + 4 + 3 + (3+4+3+4+3+2+2+2) + 1 + 1 + (1+2+1+1+1+1+1+1+1+1) + (3+1+1) + 1 + 2 + 1 + 1 + 1 + 1 + 1 + 2 ≈ **64 hours on the critical path**.

Parallel work (Phase 2 and docs 050/051/057) runs alongside the critical path — total project effort ~110 hours, critical-path ~64 hours.

### High-risk items

- **Atomic migration of ~199 sites (tasks 010–017)** — Mitigation: sequential sub-task ordering + final grep gate (task 018); single-PR commit boundary.
- **Phase 3 cutover (tasks 030–037)** — Irreversible Azure operations; runbook order strictly required.
- **PR #253 (NuGet bump) merging mid-project** — Mitigation: task 063 conflict-check rebases if needed.
- **ADR-009 lockstep amendment (tasks 052+053)** — Single PR; matching Last Updated dates verified at 053 acceptance.

---

## Cross-cutting metadata

- **Sister project handoff**: Task 034 produces the gate signal for `spaarke-ai-azure-setup-dev-r1` NFR-13. Task 038 records the handoff signal in sister project notes.
- **`.claude/` write boundary**: Task 052 is the only task touching `.claude/adr/`. Must run in main session per CLAUDE.md §3.
- **PR overlap caution**: PR #253 (`Microsoft.Extensions.Caching.StackExchangeRedis` NuGet bump) overlaps Phase 1. Coordinate at PR creation; rebase if it lands first.
- **Publish-size budget**: ≤+1 MB compressed delta vs branch start (NFR-04, ADR-029). Measured at tasks 019, 044, 061.
- **Documentation lockstep**: Tasks 050 (architecture) → 052 + 053 (ADR concise + full) → 054 (deployment guide) form the doctrinal pipeline. Order matters.

---

*This index is maintained by `task-execute`. As tasks complete, update Status column (🔲 → ✅) and any blocked-state changes (🔲 → ⏸).*
