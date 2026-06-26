# Task Index — spaarke-redis-cache-remediation-r1

> **Generated**: 2026-06-25 by `/project-pipeline` (Steps 3 → task-create)
> **Total tasks**: 60 (Phase 1: 19 · Phase 2: 10 · Phase 3: 10 · Phase 4: 5 · Phase 5: 15 · Wrap-up: 1)
> **Total estimated effort**: ~110 hours
> **Status legend**: 🔲 not started · 🔄 in-progress · ✅ completed · ⏸ blocked · ↩ deferred

---

## Phase 1 — CacheModule hardening + wrapper + atomic migration (tasks 001–019)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 001 | Cache call-site inventory (authoritative count) | 🔲 | — | 010–018 | 2 | STANDARD | Group 0 — foundational |
| 002 | Add Redis:AllowInMemoryFallback option | 🔲 | 001 | 003 | 1 | STANDARD | Group A |
| 003 | CacheModule 4-branch logic + AbortOnConnectFail=true | 🔲 | 002 | 005, 009 | 3 | FULL | Group A |
| 004 | NullConnectionMultiplexer (ADR-032 Null-Object) | 🔲 | 002 | 005, 009 | 3 | FULL | Group A |
| 005 | Symmetric DI registration (§F.1 static-scan) | 🔲 | 003, 004 | 009 | 2 | FULL | Group B |
| 006 | ITenantCache interface + default implementation | 🔲 | 005 | 009, 010–017 | 4 | FULL | Group C |
| 007 | DistributedCacheExtensions: `sdap` → `spaarke` prefix | 🔲 | 006 | 010–017 | 2 | STANDARD | Group C |
| 008 | appsettings InstanceName updates | 🔲 | 006 | 018 | 1 | STANDARD | Group C |
| 009 | CacheModuleTests (4 branches + Null-Object) | 🔲 | 005, 006, 007, 008 | 010 | 3 | FULL | Group D |
| 010 | Migrate Office services to ITenantCache | 🔲 | 001, 006, 009 | 011 | 3 | FULL | Group E *(sequential — NFR-07)* |
| 011 | Migrate Chat services to ITenantCache | 🔲 | 010 | 012 | 4 | FULL | Group E |
| 012 | Migrate Membership services to ITenantCache | 🔲 | 011 | 013 | 3 | FULL | Group E |
| 013 | Migrate Document/AI services to ITenantCache | 🔲 | 012 | 014 | 4 | FULL | Group E |
| 014 | Migrate Background-job services to ITenantCache | 🔲 | 013 | 015 | 3 | FULL | Group E |
| 015 | Migrate Auth/User services to ITenantCache | 🔲 | 014 | 016 | 2 | FULL | Group E |
| 016 | Migrate Spaarke.Core consumers to ITenantCache | 🔲 | 015 | 017 | 2 | FULL | Group E |
| 017 | System-level cache exception allow-list (NFR-08) | 🔲 | 016 | 018 | 2 | FULL | Group E |
| 018 | Final grep verification (ZERO direct calls) | 🔲 | 017 | 019 | 1 | STANDARD | Group F |
| 019 | Phase 1 build + test + publish-size delta | 🔲 | 018 | 030 | 1 | STANDARD | Group F |

---

## Phase 2 — Bicep + provisioning artifacts (tasks 020–029)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 020 | redis.bicep parameter audit (FR-09) | 🔲 | — | 022, 023, 024 | 2 | STANDARD | Group G — parallel to Phase 1 |
| 021 | bicepparam drift audit + fix (FR-10) | 🔲 | — | 022, 023, 024 | 2 | STANDARD | Group G |
| 022 | redis-dev.bicepparam (Basic C0) | 🔲 | 020, 021 | 025, 028, 031 | 1 | STANDARD | Group H |
| 023 | redis-staging.bicepparam (Standard C0) | 🔲 | 020, 021 | 025 | 1 | STANDARD | Group H |
| 024 | redis-prod.bicepparam (Standard C2 placeholder) | 🔲 | 020, 021 | 025 | 1 | STANDARD | Group H |
| 025 | NEW Deploy-RedisCache.ps1 | 🔲 | 022, 023, 024 | 027, 028, 031 | 4 | FULL | Group I |
| 026 | Extend RedisValidationTests.ps1 | 🔲 | 025 | 028 | 2 | STANDARD | Group I |
| 027 | Refactor Provision-Customer.ps1 to call Deploy-RedisCache | 🔲 | 025 | — | 2 | FULL | Group I |
| 028 | Deploy-RedisCache.ps1 -WhatIf integration check | 🔲 | 022, 025, 026 | — | 1 | STANDARD | Group I |
| 029 | Phase 2 review (PSScriptAnalyzer + Bicep linter) | 🔲 | 020–028 | 030 | 1 | STANDARD | Group I — final gate |

---

## Phase 3 — Dev environment cutover (tasks 030–039)

⚠️ **Strictly serial** — Azure-side operations; irreversible; runbook order required.

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 030 | Pre-cutover baseline (App Settings, KV name, MI) | 🔲 | 019, 029 | 031, 032, 033 | 1 | STANDARD | Phase3 — serial |
| 031 | Provision spaarke-bff-redis-dev (Basic C0) | 🔲 | 022, 025, 030 | 032 | 2 | FULL | Phase3 |
| 032 | Key Vault Redis-ConnectionString secret upsert | 🔲 | 031 | 033 | 1 | FULL | Phase3 |
| 033 | Update spaarke-bff-dev App Settings | 🔲 | 032 | 034 | 1 | FULL | Phase3 |
| 034 | BFF restart + verify (**Success Criterion #1 — gate signal**) | 🔲 | 033 | 035, 038 | 1 | FULL | Phase3 |
| 035 | Smoke test — chat session key format | 🔲 | 034 | 036 | 1 | FULL | Phase3 |
| 036 | 24-hr verification window | 🔲 | 035 | 037 | 1 | STANDARD | Phase3 |
| 037 | Decommission legacy spe-redis-dev-67e2xz | 🔲 | 036 | 038 | 1 | FULL | Phase3 |
| 038 | Sister project handoff signal | 🔲 | 034, 037 | 039 | 1 | MINIMAL | Phase3 |
| 039 | Phase 3 retro | 🔲 | 038 | 051 | 1 | MINIMAL | Phase3 |

---

## Phase 4 — Observability (tasks 040–044)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 040 | Verify App Insights Redis dependency telemetry | 🔲 | 035 | — | 1 | STANDARD | Phase4 — parallel-safe |
| 041 | Custom metrics emission from ITenantCache | 🔲 | 006, 019 | 042 | 3 | FULL | Phase4 |
| 042 | Verify custom metrics visible in App Insights | 🔲 | 041 | — | 1 | STANDARD | Phase4 |
| 043 | 3 alert definitions (hit rate, P95, memory) | 🔲 | 041 | — | 2 | STANDARD | Phase4 — parallel-safe |
| 044 | Phase 4 publish-size delta | 🔲 | 041, 043 | — | 1 | STANDARD | Phase4 — final gate |

---

## Phase 5 — Canonical docs + ADR amendments + lessons + R7 backlog (tasks 050–064)

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 050 | UPDATE caching-architecture.md | 🔲 | — | 052, 053, 058 | 3 | STANDARD | Group J — parallel to Phase 1+2 |
| 051 | NEW redis-cache-azure-setup.md (operational runbook) | 🔲 | 039, 043 | 054, 055, 056 | 4 | STANDARD | Group J |
| 052 | AMEND .claude/adr/ADR-009-redis-caching.md (concise) | 🔲 | 050 | 053 | 2 | FULL | Group K — **main-session-only** (`.claude/` write boundary) |
| 053 | AMEND docs/adr/ADR-009-caching-redis-first.md (full) — lockstep | 🔲 | 052 | — | 2 | FULL | Group K — sequential lockstep |
| 054 | UPDATE SPAARKE-DEPLOYMENT-GUIDE.md §4.5 + Appendix D | 🔲 | 051 | — | 1 | STANDARD | Group L |
| 055 | Secret rotation procedure section | 🔲 | 051 | — | 1 | MINIMAL | Group L |
| 056 | Lessons-learned section | 🔲 | 039, 051 | — | 1 | MINIMAL | Group L |
| 057 | R7 backlog (S1–S4 deferred items) | 🔲 | — | — | 1 | MINIMAL | Group L — parallel-safe |
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
| 090 | Project Wrap-up (code-review + adr-check + repo-cleanup + README Complete) | 🔲 | 064 | — | 2 | FULL | Final |

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
