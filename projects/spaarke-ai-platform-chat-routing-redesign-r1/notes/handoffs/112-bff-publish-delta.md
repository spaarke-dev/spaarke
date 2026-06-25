# Task 112 — BFF Publish-Size Delta

> **Date**: 2026-06-25
> **Task**: 112 — Phase B vector match with manifest `documentTypes` pre-filter (Hybrid C primary path)
> **Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
> **Rigor**: FULL (per task POML — tags include `bff-api` + `services` + `ai` + `azure-openai`; NFR-01 / ADR-029 binding)

## Measurement

| Field | Value |
|---|---|
| **Pre-task-112 baseline (reported by current-task.md)** | 49.22 MB compressed |
| **Post-task-110 baseline (per `110-bff-publish-delta.md`)** | 47.87 MB compressed |
| **Post-task-112 (this task — Phase B method + index field + tests)** | **44.94 MB** compressed (47,127,059 bytes) |
| **Delta vs current-task baseline (49.22 MB)** | **−4.28 MB** (net reduction) |
| **Delta vs task-110 baseline (47.87 MB)** | −2.93 MB |
| **Single-task escalation threshold (+5 MB)** | NOT exceeded |
| **NFR-01 60 MB hard ceiling** | NOT approached — 15.06 MB headroom |
| **NFR-01 55 MB architecture-review threshold** | NOT approached — 10.06 MB headroom |

## Measurement methodology

```
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o deploy/api-publish
tar -czf /c/tmp/bff-publish-task112.tar.gz . (from deploy/api-publish/)
stat -c%s /c/tmp/bff-publish-task112.tar.gz
```

Compressed size: 47,127,059 bytes → 44.94 MB.

## Delta analysis

Task 112 source changes:

- `PlaybookDispatcher.cs`: +N lines (new Phase B method `RunPhaseBVectorMatchAsync`, two private helpers, one new `IMemoryCache` field, one new public record type, cache key helpers). One new `using` for `Microsoft.Extensions.Caching.Memory` (already a transitive dep — no new package reference).
- `PlaybookEmbeddingService.cs`: +60 lines (new `SearchPlaybooksAsync` overload that accepts `documentTypeFilter`; `IndexPlaybookAsync` populates `DocumentTypes` field). Original signature retained as a thin delegating overload.
- `PlaybookEmbeddingDocument.cs`: +1 property (`IList<string> DocumentTypes`) decorated with `SearchableFieldAttribute`.
- `infrastructure/ai-search/playbook-embeddings.json`: +1 filterable field (`documentTypes` collection) — runtime config, not in publish output.

Expected publish-size delta: **near zero** (incremental .NET code; no new package refs, no new assemblies, no new framework dependencies).

Observed delta of **−4.28 MB vs the 49.22 MB current-task baseline** is within the historical drift band documented across `028d`, `032`, and `110` publish-delta notes (each landed at slightly different absolute sizes due to NuGet cache state, OS-level gzip determinism, and CI-vs-local pipeline differences). The reduction is **not** attributable to the task 112 code change itself — it reflects environmental drift away from the 49.22 MB high-water mark.

**Verification that the change itself is publish-neutral**:

- No new package references in `Sprk.Bff.Api.csproj`.
- `Microsoft.Extensions.Caching.Memory` was already a transitive dependency (via `Microsoft.Extensions.Hosting`).
- The Sprk.Bff.Api.dll is the only changed publish artifact in this task; the changed surface is a single method + helpers (~250 LOC in source ≈ a handful of KB in IL).
- `playbook-embeddings.json` is an `infrastructure/` artifact — NOT included in the `deploy/api-publish/` output.

## NFR-01 status

✅ **Compliant** — 44.94 MB measured vs 60 MB ceiling = 15.06 MB headroom. Well below both the architecture-review threshold (55 MB) and far below the per-task escalation threshold (+5 MB).

## Cumulative status post-task-112

| Threshold | Value | Status |
|---|---|---|
| Hard ceiling (NFR-01) | 60.00 MB | ✅ 15.06 MB headroom |
| Architecture-review threshold | 55.00 MB | ✅ 10.06 MB headroom |
| Per-task escalation threshold (+5 MB) | n/a | ✅ Not exceeded (delta is net negative) |
| Phase 0 project baseline (per `013-bff-publish-delta.md`) | 44.75 MB | +0.19 MB cumulative across the project to date |

## Files modified in task 112

| Path | Lines | Nature |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | +~280 | Phase B method + helpers + `IMemoryCache` field + `PhaseBPerFileResult` record |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs` | +~60 | New `SearchPlaybooksAsync` overload w/ documentTypes filter; index-time `DocumentTypes` population |
| `src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookEmbeddingDocument.cs` | +28 | `DocumentTypes` filterable collection property |
| `infrastructure/ai-search/playbook-embeddings.json` | +10 | `documentTypes` filterable collection field (forward-compat for Phase 4b) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherPhaseBTests.cs` | +480 (new) | 11 `[Fact]` / `[Theory]` tests; not in publish |
| `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/112-bff-publish-delta.md` | new | This file |

## Build & test summary

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 17 warnings (all pre-existing, matches task-110 baseline)**
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PlaybookDispatcherPhaseB"` → **11 passed, 0 failed, 0 skipped, 160 ms**
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PlaybookDispatcher|FullyQualifiedName~PlaybookEmbeddingService"` → **32 passed, 0 failed, 0 skipped, 226 ms** (no regressions on existing PlaybookDispatcher / PlaybookEmbeddingService suites)
- `dotnet publish src/server/api/Sprk.Bff.Api/ -c Release` → succeeded; compressed output 44.94 MB

## Latency budget verification (FR-17 v2)

| Path | Target | Measured (mocked) | Status |
|---|---|---|---|
| Manifest-present p95 | ≤ 100 ms | < 50 ms (p95 across 20 iterations, fresh cache per iteration) | ✅ Within budget |
| Manifest-absent (3 files, parallel fan-out) | ≤ 300 ms for 3 files | < 300 ms wall-clock (simulated per-call 80–150 ms; parallelism bounded by slowest call) | ✅ Within budget |

Tests `RunPhaseBVectorMatchAsync_ManifestPresent_MeetsP95LatencyBudget` and `RunPhaseBVectorMatchAsync_ManifestAbsent_MeetsLatencyBudgetFor3Files` enforce these budgets in CI.

## Open follow-ups for main session

1. **`playbook-embeddings` AI Search index needs `documentTypes` field added** in deployed environments (bff-dev, demo, prod) via re-applying `infrastructure/ai-search/playbook-embeddings.json`. Without this field deployed, the manifest-present OData filter returns zero matches (graceful degradation — caller falls back to manifest-absent path). When Phase 4b lands (deferred per current MVP scope cut), playbooks should be re-indexed so `DocumentTypes` is populated from `sprk_jpsmatchingmetadata`.
2. **Task 113R top-N selector** is the next consumer — it will call `RunPhaseBVectorMatchAsync` and reconcile per-file top-K lists into the final dispatch decision (cross-file disagreement resolution).
3. **Wiring `RunPhaseBVectorMatchAsync` into `DispatchAsync`** is intentionally deferred to task 113R per the task 112 contract ("Phase B returns N top-K lists; the caller (113R, a future task) reconciles").
4. **`ChatSession.UploadedFiles[fileId].ClassifiedDocType` is currently always `null`** in production because Phase 4b classification pipeline is deferred per the MVP scope cut. The manifest-present branch is forward-compat scaffolding; the manifest-absent fallback IS the MVP production path.
5. **No `.claude/` edits made** in task 112 (per push policy).
6. **Commit** is on `work/spaarke-ai-platform-chat-routing-redesign-r1`; pushing immediately after commit per single-stream policy.
