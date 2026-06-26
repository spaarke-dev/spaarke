# Current Task State — spaarke-redis-cache-remediation-r2

> **Last Updated**: 2026-06-26
> **Status**: ✅ Task 001 complete · 🔲 Next: Task 002 (Meter consolidation) — Group A wave

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarke-redis-cache-remediation-r2 |
| **Active task** | Group A wave (002 + 004 + 006 — parallel-safe) |
| **Status** | Group 0 ✅ done; Group A ready to dispatch |
| **Next action** | Dispatch tasks 002, 004, 006 in parallel via 3 task-execute invocations in ONE message |

---

## Task 001 — Completed 2026-06-26

- **Title**: `cache.failures` Counter + try/catch + `ClassifyException` (FR-01)
- **Rigor**: FULL
- **Files modified**:
  - `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs` — added `FailuresCounter` static field (uses same Meter `Sprk.Bff.Api.Cache`; no new Meter instance)
  - `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/MetricsDistributedCache.cs` — wrapped all 8 public methods (Get, GetAsync, Set, SetAsync, Refresh, RefreshAsync, Remove, RemoveAsync) in try/catch; added `RecordFailure` + `ClassifyException` (switch over 5 outcomes: canceled, timeout, connection, serialization, other)
- **Build verification**: `dotnet build src/server/api/Sprk.Bff.Api/` returned 0 errors, 18 pre-existing warnings (0 new)
- **Note**: `RedisTimeoutException` derives from `TimeoutException` so the `RedisTimeoutException` arm was unreachable; `RedisServerException` similarly redundant — both removed with inline comment.
- **Deferred verification**:
  - KQL `customMetrics | where name == 'cache.failures'` — verified post-deploy in task 030
  - Integration test asserting Meter count = 1 — explicit verification in task 005 after task 002 lands

---

## Active Group — Group A (tasks 002, 004, 006)

All three are parallel-safe (distinct files: Telemetry/CacheMetrics.cs + TenantCache.cs vs alerts.bicep vs Program.cs).

| Task | Title | Files | Rigor |
|---|---|---|---|
| 002 | Meter consolidation — promote CacheMetrics to static; remove TenantCache static fields | Telemetry/CacheMetrics.cs, Infrastructure/Cache/TenantCache.cs, EmbeddingCache, GraphTokenCache, CacheModule.cs | FULL |
| 004 | NEW infrastructure/bicep/alerts.bicep (3 cache alerts) + Deploy-RedisCache.ps1 `-DeployAlerts` flag | infrastructure/bicep/alerts.bicep (NEW), scripts/Deploy-RedisCache.ps1 | STANDARD |
| 006 | UseAzureMonitor() fails-open guard in Program.cs | src/server/api/Sprk.Bff.Api/Program.cs + new unit test | FULL |

**Note for task 002**: my task 001 added `FailuresCounter` to TenantCache. When task 002 promotes CacheMetrics to canonical static class, that Counter should move with the Hits/Misses/Histogram instruments.

---

## Resume Protocol

If context is reset / new session:
1. Read this file
2. Read `tasks/TASK-INDEX.md` for overall status
3. Dispatch Group A: ONE message with 3 Skill tool invocations (task-execute, one per POML)
