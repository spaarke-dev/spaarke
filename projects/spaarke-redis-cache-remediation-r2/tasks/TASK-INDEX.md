# Task Index — spaarke-redis-cache-remediation-r2

> **Generated**: 2026-06-26 by `/project-pipeline` (Steps 3 → task-create)
> **Total tasks**: 17 (Phase 1: 6 · Phase 2: 5 · Phase 3: 3 · Phase 4: 3)
> **Total estimated effort**: ~30 hours (~3-5 days walltime including review)
> **Status legend**: 🔲 not started · 🔄 in-progress · ✅ completed · ⏸ blocked · ↩ deferred

---

## Phase 1 — Theme A: Cache observability hardening (tasks 001-006)

All BFF code (`bff-api` tag) → **FULL rigor**.

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 001 | `cache.failures` Counter + try/catch + `ClassifyException` | ✅ | — | 030 | 2 | FULL | Group 0 — foundational, serial. **Done 2026-06-26**: Added `FailuresCounter` to TenantCache.cs (same Meter); added try/catch + `RecordFailure` + `ClassifyException` switch (5 outcomes) to MetricsDistributedCache.cs all 8 public methods. Build clean (0 errors, 0 new warnings). RedisTimeoutException covered by TimeoutException arm (derives from it). |
| 002 | Meter consolidation — single canonical `CacheMetrics` static class | ✅ | — | 003, 005 | 3 | FULL | Group A — **Done 2026-06-26**: promoted CacheMetrics to static class owning the single Meter + 5 instruments; removed Meter+Counter fields from TenantCache; switched MetricsDistributedCache + 6 consumers (EmbeddingCache, GraphTokenCache, GraphMetadataCache, CachedAccessDataSource, AnalysisRagProcessor, TextExtractorService) from instance ctor injection to static method calls; removed `AddSingleton<CacheMetrics>` from DocumentsModule; updated 2 test files. Build clean. |
| 003 | `cache.hits.by_resource` + `cache.misses.by_resource` at TenantCache | 🔲 | 002 | 030 | 2 | FULL | Group B — depends on 002 ✅ (uses canonical static class) |
| 004 | NEW `infrastructure/bicep/alerts.bicep` (3 cache alerts) | ✅ | — | 014, 030 | 3 | STANDARD | Group A — **Done 2026-06-26**: NEW alerts.bicep with 3 resources (memory metricAlert + hit-rate scheduledQueryRules + P95 scheduledQueryRules); EXTENDED Deploy-RedisCache.ps1 with -DeployAlerts/-AppInsightsName/-ActionGroupResourceId params + env-default App Insights name. `bicep build` succeeds; `-WhatIf` plan shows 3 alerts. |
| 005 | Decorator regression integration test (`MetricsDistributedCacheRegistrationTests`) | 🔲 | 002 | 030 | 2 | FULL (TEST-MODIFYING override) | Group B — depends on 002 ✅ (asserts Meter count = 1) |
| 006 | `UseAzureMonitor()` fails-open guard in `Program.cs` | ✅ | — | 030 | 1 | FULL | Group A — **Done 2026-06-26**: extracted to `Infrastructure/Startup/AzureMonitorGuard.cs` (4-branch shape mirroring CacheModule); Program.cs uses `AzureMonitorGuard.ShouldWireExporter()`; 9 unit tests (Production-throw + Development-pass + case-insensitive env name). Build clean. |

---

## Phase 2 — Theme B: Redis key rotation automation (tasks 010-014)

Mixed scripting + IaC + docs. Parallel-safe with Phase 3.

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 010 | NEW `scripts/Rotate-RedisKey.ps1` (safe-window rotation algorithm) | 🔲 | — | 011, 013, 030 | 4 | STANDARD | Group C — parallel-safe with Phase 3 |
| 011 | NEW `.github/workflows/redis-key-rotation.yml` (3 cron jobs + `workflow_dispatch`) | 🔲 | 010 | 030 | 2 | STANDARD | Group C — depends on script existence |
| 012 | Document per-env OIDC SP isolation procedure | 🔲 | — | 013 | 1 | MINIMAL | Group C — docs-only |
| 013 | Runbook §6 update — automated as primary; manual as emergency | 🔲 | 010, 012 | 030 | 1 | MINIMAL | Group C — depends on 010 + 012 |
| 014 | Missed-rotation alert (>100 days) added to `alerts.bicep` | 🔲 | 004 | 030 | 1 | STANDARD | Group C — depends on 004 (extends same Bicep) |

---

## Phase 3 — Theme C: R1 implementation gap closure (tasks 020-022)

IaC + docs cleanup. Parallel-safe with Phase 2.

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 020 | Remove Redis from `customer.bicep` (module call + params + var + outputs); `what-if` verify | 🔲 | — | 021, 030 | 2 | STANDARD | Group D — parallel-safe with Phase 2 |
| 021 | Drop `redisSku` / `redisCapacity` from `customer-template.bicepparam` | 🔲 | 020 | 030 | 1 | STANDARD | Group D — depends on 020 |
| 022 | `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 strikethrough/footnote cleanup | 🔲 | 020 | 030 | 1 | MINIMAL | Group D — docs-only |

---

## Phase 4 — Deploy + verify + wrap-up (tasks 030-032)

Strictly serial.

| ID | Title | Status | Dependencies | Blocks | Hours | Rigor | Parallel |
|---|---|---|---|---|---|---|---|
| 030 | Deploy `alerts.bicep` to dev + KQL verification (NFR-07) + publish-size delta (NFR-04) | 🔲 | All Phase 1-3 | 031 | 2 | FULL | serial |
| 031 | Close GitHub Issues #462 / DEF-007 / DEF-008 / DEF-009 + flip R1 `defer-issues.md` | 🔲 | 030 | 032 | 1 | MINIMAL | serial |
| 032 | Project wrap-up (code-review + adr-check + repo-cleanup + README → Complete + lessons-learned) | 🔲 | 031 | — | 2 | FULL | serial |

---

## Parallel Execution Plan

**Concurrency cap**: 6 agents per wave (per CLAUDE.md §10 + project-pipeline Step 5 rule).

### Wave structure

| Wave | Tasks | Prereq | Type | Notes |
|---|---|---|---|---|
| 0 | 001 | — | serial | Foundational (`cache.failures` Counter is the most-broadly-cited new instrument) |
| A | 002, 004, 006 | — (start parallel-to-001 if context allows; safer: after 001 ✅) | parallel (3 agents) | Distinct files: Telemetry/CacheMetrics.cs vs alerts.bicep vs Program.cs |
| B | 003, 005 | A ✅ (002 specifically) | parallel (2 agents) | Both depend on canonical static class from 002 |
| C (parallel to Phases 1-3) | 010, 012 | — | parallel (2 agents) | Script + SP docs are independent |
| C-cont | 011, 013, 014 | 010 ✅, 012 ✅, 004 ✅ | parallel (3 agents) | Workflow + runbook + alert extension; distinct files |
| D (parallel to Phases 1-2) | 020 | — | serial | Bicep `what-if` verification gate |
| D-cont | 021, 022 | 020 ✅ | parallel (2 agents) | bicepparam + deployment guide; distinct files |
| Phase 4 | 030 → 031 → 032 | All prior ✅ | **STRICTLY SERIAL** | Deploy → close issues → wrap-up |

### Optimal parallel-execution timeline

```
T=0:        Wave 0 (001) + Wave C-start (010, 012) + Wave D-start (020) in parallel
T=T1:       Wave A (002, 004, 006) + Wave C-cont (011, 013, 014) + Wave D-cont (021, 022)
T=T2:       Wave B (003, 005)
T=T3:       Task 030 (deploy + verify)
T=T4:       Task 031 (issue closures)
T=T5:       Task 032 (wrap-up)
```

### Build verification between waves (MANDATORY)

After each wave that modifies `.cs` files: `dotnet build src/server/api/Sprk.Bff.Api/` before dispatching next wave. If build fails, STOP and report wave identifier.

Specifically:
- After Wave 0 (task 001): build BFF
- After Wave A (task 002 + 006): build BFF
- After Wave B (task 003): build BFF
- After task 005: `dotnet test` integration test suite

### Critical path

`001 → 002 → 003 ∥ 005 → 030 → 031 → 032`

**Critical-path duration estimate**: 2 + 3 + max(2,2) + 2 + 1 + 2 = **12 hours on the critical path**.

Total project effort ~30 hours; parallel work (Phases 2-3 + 004 + 006) runs alongside critical path.

### High-risk items

- **Task 002 Meter consolidation** — touches `TenantCache.cs` static fields + `Telemetry/CacheMetrics.cs` + 3-5 consumer files (`EmbeddingCache`, `GraphTokenCache`, others). Grep-audit critical. Risk: half-removed instance class breaks DI.
  - Mitigation: §F.2 Fixture-Config-FIRST applies if WAF-based test fixtures regress.
- **Task 020 `customer.bicep` Redis removal** — irreversible IaC change. `what-if` MUST run before commit; if any live customer relies on Redis module path, escalate.
- **Task 030 deploy + KQL verification** — depends on Azure dev environment Redis + App Insights connection; operator must confirm `spaarke-bff-redis-dev` and KV `spaarke-spekvcert` are reachable.
- **PR overlap** — see `projects/INDEX.md` Hot-Path BFF section (13 active BFF-touching projects). R2 specifically overlaps R1's `MetricsDistributedCache` + `TenantCache` + `CacheMetrics` files. Mitigation: rebase if R1 work surfaces follow-up PRs.

---

## Cross-cutting metadata

- **Hot-path declaration** (per CLAUDE.md §10 / R1 CICD-062): BFF=Y, SpaarkeAi=N, ci-workflows=Y, skill-directives=N, root-CLAUDE.md=N. Block lives in `design.md`.
- **PR strategy** (NFR-01): ONE combined PR for Theme A + B + C. Opens after task 030 succeeds.
- **Publish-size budget** (NFR-04): ≤+0.5 MB compressed delta vs branch start. Measured at task 030 via `Deploy-BffApi.ps1`.
- **No ADR changes** (NFR-08): R2 operationalizes ADR-009's R1 amendment. MUST NOT modify `.claude/adr/` or `docs/adr/` for ADR-009.
- **`.claude/` write boundary** (CLAUDE.md §3): no R2 task touches `.claude/`. All sub-agents are write-safe.

---

*This index is maintained by `task-execute`. As tasks complete, update Status column (🔲 → ✅).*
