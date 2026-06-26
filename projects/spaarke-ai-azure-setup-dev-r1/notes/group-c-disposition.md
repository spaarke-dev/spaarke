# Group C (Tasks 031-037) — Disposition Note

> **Phase**: 4 — Code + Config Refactor
> **Group**: C (BFF refactor)
> **Date**: 2026-06-26
> **Status**: 6/7 complete; task 031 BLOCKED pending user decision on DiscoveryIndexName structural refactor

---

## Summary

Group C was originally scoped as 7 parallel mechanical refactors (replace `spaarke-knowledge-index-v2` with `spaarke-files-index` across BFF services). On audit, the actual scope diverged significantly from the POML expectations:

| Task | Original Scope | Actual State | Disposition |
|---|---|---|---|
| 031 | Refactor RagService.cs, RagIndexingPipeline.cs, IndexRetrieveNode.cs | Zero `spaarke-knowledge-index-v2` matches BUT structural `DiscoveryIndexName` consumers remain (5 CS0618 warnings from task 030 [Obsolete]) | **BLOCKED — user decision needed** (see § Task 031 Escalation) |
| 032 | Refactor FileIndexingService.cs, ReferenceIndexingService.cs, ReferenceRetrievalService.cs, SessionFilesCleanupJob.cs | Zero matches — already canonical (refactored in R5 work) | ✅ Verified no-op |
| 033 | Refactor 4 PlaybookEmbedding services | Zero matches — task 013 (Phase 2 atomic rename) covered the code side | ✅ Verified no-op |
| 034 | Refactor KnowledgeDeploymentService + IKnowledgeDeploymentService + KnowledgeBaseEndpoints | 4 live references (3 in IKnowledgeDeploymentService default value + doc-comments, 1 in KnowledgeBaseEndpoints fallback) | ✅ 4 edits applied |
| 035 | Refactor 3 job handlers + InvoiceSearchService | Zero matches — task 014 (Phase 2 atomic rename) covered the code side | ✅ Verified no-op |
| 036 | Wire tenantId on records-index writer + reader; update Sync-RecordsToIndex.ps1 | C# writer + reader already done in task 015 (Phase 2); PS script needed tenantId param + population | ✅ PS script updated; C# verified |
| 037 | 5 doc-comment edits (KnowledgeDocument + AiAnalysisNodeExecutor + appsettings.tokens.md) | All 5 live references | ✅ 5 edits applied |

**Net work delivered** (this commit):
- 4 BFF C# files edited (KnowledgeBaseEndpoints, IKnowledgeDeploymentService, KnowledgeDocument, AiAnalysisNodeExecutor)
- 1 BFF doc file edited (appsettings.tokens.md)
- 1 PS script updated (Sync-RecordsToIndex.ps1 — tenantId parameter + population)
- 6 task POMLs marked ✅
- 1 task POML (031) marked 🟡 blocked

**Net work NOT delivered**:
- Task 031: `DiscoveryIndexName` consumer refactor in RagService/RagIndexingPipeline/IRagService (5 CS0618 warning sites)

---

## Why Most Group C Tasks Were No-ops

The Group C POMLs were authored before the Phase 2 atomic-rename commits (tasks 010-016, see git log). Those Phase 2 commits, particularly:

- **Task 013** (playbook-embeddings rename — commit `6daaab5f1`): refactored `PlaybookEmbeddingService.cs:46` constant to `spaarke-playbook-embeddings`
- **Task 014** (invoices-index rename — commit `10dba1592`): refactored `InvoiceIndexingJobHandler.cs:40` + `InvoiceSearchService.cs:45` to `spaarke-invoices-index`
- **Task 015** (records tenantId — commit `e9aea584f`): added tenantId field + wired DataverseIndexSyncService writer + RecordSyncJob writer + RecordSearchAuthorizationFilter reader (preserved as auth gate) + RecordSearchService.BuildRecordFilter (added OData filter)

…together with the earlier R5 Spaarke AI Platform Unification work that introduced the Options pattern for BFF AI services, eliminated the hardcoded `spaarke-knowledge-index-v2` strings in services 032, 033, 035 ahead of this project.

The audit grep `spaarke-knowledge-index-v2` (excluding tests + .test.ts fixtures) returned only 11 hits in production BFF — all in the 4 files this commit addresses (tasks 034 + 037).

**Lesson**: a comprehensive grep-audit at the START of a Phase 4 multi-task BFF refactor would identify which tasks are no-ops and which need real work, sparing the project the overhead of 7 sub-agent dispatches for what was effectively 5 targeted edits. Recommend adding "Phase 4 pre-audit grep" as a generic project-pipeline step in future projects.

---

## Task 031 Escalation: DiscoveryIndexName Structural Refactor

The remaining work — task 031 — surfaced as significantly larger than POML implied. The 5 CS0618 warnings flag a STRUCTURAL pattern (not a string rename):

### What `DiscoveryIndexName` Is

`AiSearchOptions.DiscoveryIndexName` (marked `[Obsolete(FR-14: ...)]` in task 030) drives a **dual-index code path** in the BFF AI services:

1. **`RagIndexingPipeline.IndexDocumentAsync`** (line 156): each ingest writes to BOTH `KnowledgeIndex` AND `DiscoveryIndex` with separate chunk strategies + separate embeddings (the "knowledge chunks" and "discovery chunks" abstractions).

2. **`RagService.GetIndexHealthAsync`** (lines 862-874): queries BOTH indexes for document counts and returns `KnowledgeIndexHealth(KnowledgeDocCount, DiscoveryDocCount, KnowledgeIndexName, DiscoveryIndexName)`.

3. **`RagService.EnsureKnownIndex`** (lines 977-986): validates that admin-supplied `indexName` is either `KnowledgeIndexName` or `DiscoveryIndexName`; rejects with `ArgumentException` otherwise.

4. **`IRagService.KnowledgeIndexHealth`** (line 231): the API contract record exposing both index names.

5. **`KnowledgeBaseEndpoints` health endpoint** (line 138): maps `KnowledgeIndexHealth.DiscoveryIndexName` into the public `KnowledgeIndexHealthResult` (line 575) returned to BuilderAdmin clients.

### The Tension

Per [`docs/architecture/AI-SEARCH-INDEX-CATALOG.md`](../../../docs/architecture/AI-SEARCH-INDEX-CATALOG.md) §5 Retired Indexes Appendix:

> **`discovery-index`** — Deleted 2026-06-25. Why retired: "Provisioned by AIPL-016 but never wired into runtime; no live writer or query path found in the 2026-06-25 audit. Schema cost without runtime benefit."

But the BFF code I just inspected SHOWS the dual-index pipeline + health surface IS live (compiles, would execute if invoked). Either:

- (a) The 2026-06-25 audit missed these code paths, OR
- (b) The code paths are dead at runtime (no consumer calls `RagIndexingPipeline.IndexDocumentAsync` with `discoveryChunks`; no consumer hits the dual-index health endpoint)

Either way, the catalog says `discovery-index` is retired — so the dual-index code path is dead-or-misleading. Three options for resolving:

### Three Resolution Options

**Option A — Full structural refactor (correct + breaking)**:
- Remove `DiscoveryIndexName` consumer paths entirely:
  - `RagIndexingPipeline` ingests to ONE index (`FilesIndexName`); drop `discoveryChunks`/`discoveryEmbeddings` arrays
  - `RagService.GetIndexHealthAsync` returns just `KnowledgeIndex*` fields (or rename to just `IndexDocCount`/`IndexName`)
  - `IRagService.KnowledgeIndexHealth` record drops `DiscoveryDocCount` + `DiscoveryIndexName` fields → **breaking change to the BFF API surface**
  - `KnowledgeBaseEndpoints` returns a smaller `KnowledgeIndexHealthResult` → **breaking change to BuilderAdmin clients**
  - `RagService.EnsureKnownIndex` validates against `KnowledgeIndexName` + `FilesIndexName` (or just one)
  - Task 030 [Obsolete] property + task 046 grep cleanup naturally remove the property
- Estimated effort: ~4 hours including test updates (8 test files reference DiscoveryIndexName); requires BuilderAdmin coordination
- **Risk**: breaking API contract; mid-project scope expansion (this project's FR-14 scope is "remove DiscoveryIndexName property" — silent on consumer-side semantic preservation)

**Option B — Point DiscoveryIndex at FilesIndex (degraded; non-breaking)**:
- Keep all dual-index code paths intact
- DiscoveryIndexName default already = `spaarke-files-index` (from task 030)
- Functional behavior: dual-index writes/reads against the SAME index twice — write returns `mergeOrUpload` ok (idempotent); health endpoint reports the same doc count twice; `EnsureKnownIndex` accepts both names but both resolve to the same physical index
- Task 030 [Obsolete] property + task 046 grep cleanup will not be possible (consumers still use it)
- **Risk**: dead code shipped to prod; misleading health metrics (DiscoveryDocCount == KnowledgeDocCount always); deferred technical debt
- Estimated effort: 0 (already done by task 030)

**Option C — Skeleton refactor (compile-clean; semantic-preserve)**:
- Refactor consumers to NO-OP the discovery branch where safe:
  - `RagService.GetIndexHealthAsync` returns `DiscoveryDocCount = 0` + `DiscoveryIndexName = ""` (or `null`)
  - `RagIndexingPipeline` skips the discovery write entirely (don't generate discovery chunks, don't embed them, don't write them — just early-return after knowledge write)
  - `RagService.EnsureKnownIndex` rejects DiscoveryIndexName name (only KnowledgeIndexName accepted)
  - `IRagService.KnowledgeIndexHealth` record keeps the fields (no API break) but they're now informational/zero
- Public API contract preserved; consumers ignore the now-zero discovery fields
- Task 046 grep cleanup can remove the [Obsolete] property cleanly since consumers no longer touch it (they hardcode "" / 0)
- Estimated effort: ~2 hours including the 8 test-file updates
- **Risk**: lingering dead fields in the API contract; clients depending on DiscoveryDocCount get always-zero (functional change)

### Recommendation

**Option C** is the pragmatic middle — preserves API contract for BuilderAdmin clients, eliminates the dead-code runtime path (saves embeddings cost + write IOPS), enables clean task 046 cleanup, and keeps the FR-14 spirit (no live consumer of the retired discovery-index).

**Option A** is the "correct" answer but is scope expansion + breaking — should be a follow-up project unless the user wants to fold it in here.

**Option B** is the minimum-effort fallback — ship the degraded state, file follow-up tech-debt task.

User decision needed before task 031 proceeds.

---

## Files Modified This Commit (Group C partial)

| File | Task | Change |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Configuration/AiSearchOptions.cs` | 030 | (already committed in earlier task 030 commit) |
| `src/server/api/Sprk.Bff.Api/Configuration/AnalysisOptions.cs` | 030 | (already committed) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/IKnowledgeDeploymentService.cs` | 034 | 3 lines: doc-comments + default value → `spaarke-files-index` |
| `src/server/api/Sprk.Bff.Api/Api/Ai/KnowledgeBaseEndpoints.cs` | 034 | 1 line: fallback default `spaarke-knowledge-index-v2` → `spaarke-files-index` |
| `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs` | 037 | 2 doc-comment lines |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | 037 | 1 doc-comment line |
| `src/server/api/Sprk.Bff.Api/appsettings.tokens.md` | 037 | 2 token-doc lines |
| `scripts/ai-search/Sync-RecordsToIndex.ps1` | 036 | tenantId param + auto-resolve from az session + populate on every record (FR-12) |
| `projects/spaarke-ai-azure-setup-dev-r1/tasks/TASK-INDEX.md` | — | 7 task statuses updated (6 ✅, 1 🟡) |
| `projects/spaarke-ai-azure-setup-dev-r1/tasks/03[1-7]-*.poml` | — | 6 status `not-started` → `completed`; 1 status `not-started` → `blocked-pending-user-decision` |

Build verified: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors, 23 warnings (5 CS0618 on DiscoveryIndexName consumers — pending task 031 decision).

---

## Next Steps

1. **User decides Option A / B / C for task 031** (this is the blocker)
2. **Proceed to Group D** (tasks 038-039) — independent of task 031
3. **Task 040** (.claude/ doc updates) — main-session-only
4. **Task 041** (KV-ref migration) — security-sensitive sequential gate
5. **Task 042** (NFR-14 test fixture sweep) — Redis project hit 337 failures from analogous DI tightening; this is the largest single Phase 4 risk
6. **Tasks 045 + 046** — verification gates (publish-size delta + grep cleanup including DiscoveryIndexName property removal pending task 031)

Phase 5 (deploy + verify) cannot start until Phase 4 completes.
