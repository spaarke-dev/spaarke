# Current Task State — spaarke-redis-cache-remediation-r2

> **Last Updated**: 2026-06-26
> **Status**: ✅ Group 0 + Group A done (4 of 17 tasks) · 🔲 Next: Group B (003 + 005)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarke-redis-cache-remediation-r2 |
| **Active task** | Group B wave (003 + 005 — parallel-safe) |
| **Status** | Group A ✅ done; Group B ready to dispatch |
| **Next action** | Dispatch tasks 003, 005 in parallel via 2 sub-agents |

---

## Completed Tasks (4 of 17)

| # | Task | Files | Commit |
|---|---|---|---|
| 001 | `cache.failures` Counter + try/catch + ClassifyException (FR-01) | TenantCache.cs (Counter add) + MetricsDistributedCache.cs (try/catch + ClassifyException) | `73b79857c` |
| 002 | Meter consolidation — canonical static CacheMetrics class (FR-02) | CacheMetrics.cs (static) + TenantCache.cs (fields removed) + MetricsDistributedCache.cs (refs switched) + 6 consumers (EmbeddingCache, GraphTokenCache, GraphMetadataCache, CachedAccessDataSource, AnalysisRagProcessor, TextExtractorService) + DocumentsModule.cs (DI removal) + SpaarkeCore.cs (factory cleanup) + 2 test files | Group A commit (pending) |
| 004 | NEW alerts.bicep + Deploy-RedisCache.ps1 `-DeployAlerts` flag (FR-04) | infrastructure/bicep/alerts.bicep (NEW) + scripts/Deploy-RedisCache.ps1 (extended) | Group A commit (pending) |
| 006 | UseAzureMonitor() fails-open guard (FR-06) | Infrastructure/Startup/AzureMonitorGuard.cs (NEW) + Program.cs (call site) + tests/Startup/AzureMonitorGuardTests.cs (9 tests) | Group A commit (pending) |

**Build state after Group A**: `dotnet build src/server/api/Sprk.Bff.Api/` returns 0 errors, 18 pre-existing warnings (0 new). Test project also builds clean.

---

## Active Group — Group B (tasks 003 + 005)

Both depend on task 002 ✅. Parallel-safe (distinct files: TenantCache.cs vs new integration test file).

| Task | Title | Files | Rigor |
|---|---|---|---|
| 003 | `cache.hits.by_resource` + `cache.misses.by_resource` Counters at TenantCache layer | Infrastructure/Cache/TenantCache.cs + Telemetry/CacheMetrics.cs (extend with 2 new Counters) | FULL |
| 005 | Decorator regression integration test (`MetricsDistributedCacheRegistrationTests`) | tests/integration/Sprk.Bff.Api.Tests.Integration/Cache/MetricsDistributedCacheRegistrationTests.cs (NEW) | FULL (TEST-MODIFYING) |

**Coordination note for task 003**: CacheMetrics is now a static class (post task 002). Add 2 new Counters as static fields/properties on CacheMetrics: `HitsByResourceCounter` + `MissesByResourceCounter`. TenantCache calls them with the `resource` parameter as a dimension on every Get/Set/Remove path.

**Coordination note for task 005**: locate the integration test project (find via `glob tests/integration/**/CustomWebAppFactory*` or similar). The test asserts (a) `IDistributedCache` resolves to `MetricsDistributedCache` wrapping the expected inner type; (b) exactly one `Meter("Sprk.Bff.Api.Cache")` instance exists at runtime via `MeterListener` enumeration.

---

## Resume Protocol

If context is reset / new session:
1. Read this file
2. Read `tasks/TASK-INDEX.md` for overall status
3. Dispatch Group B: ONE message with 2 Agent tool calls (or run sequentially if context tight)
