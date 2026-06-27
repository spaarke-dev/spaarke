# Indexer Routing Fix — Design Doc

> **Date**: 2026-06-08
> **Trigger**: UAT discovered files land in SPE but are NOT being indexed into the correct AI Search index per `sprk_searchindexname`.
> **Root cause**: The canonical RAG write boundary `RagService.IndexDocumentsBatchAsync` calls the 2-arg `_deploymentService.GetSearchClientAsync(tenantId, ct)` overload — it never receives nor uses the per-record `searchIndexName` value.
> **Status**: Design proposal — awaiting scope approval before implementation.

---

## 1. The architecture that exists today

Per `docs/architecture/sdap-document-processing-architecture.md` §"AI Processing Pipeline":

```
            ┌──── Route 1: File Upload (PCF)
            │       └─→ POST /api/ai/rag/index-file (OBO)
            │           └─→ FileIndexingService.IndexFileAsync
            │
3 routes ───┤    ┌── Route 2: Email-to-Document
converge    │    │     └─→ Service Bus RagIndexing job
on the      │    │         └─→ RagIndexingJobHandler.ProcessAsync
SAME RAG    ├────┤             └─→ FileIndexingService.IndexFileAppOnlyAsync
pipeline    │    │                  (also: BulkRagIndexingJobHandler for bulk)
            │    │
            │    └── Route 3: Office Add-in
            │           └─→ UploadFinalization → enqueues RagIndexing job
            │               └─→ RagIndexingJobHandler.ProcessAsync
            │                   └─→ FileIndexingService.IndexFileAppOnlyAsync
            │                       OR FileIndexingService.IndexContentAsync (pre-extracted)
            │
            └──→ ALL THREE → IRagService.IndexDocumentsBatchAsync (THE write boundary)
                                  ↓
                       deploymentService.GetSearchClientAsync(tenantId, ct)  ← bug here
                                  ↓
                       searchClient.MergeOrUploadDocumentsAsync(batch)
```

`GetSearchClientAsync(tenantId, ct)` (2-arg) resolves to the tenant-default index — the per-record `sprk_searchindexname` is ignored.

This project's Phase B (tasks 010-018) added a 3-arg overload `GetSearchClientAsync(tenantId, indexName, ct)` with allow-list validation. **All search READ paths** use it. **No write path uses it.**

---

## 2. Audit — every indexing entry point + write site

### A. OBO entry points (user-initiated)

| Caller | File:Line | Calls | Currently passes searchIndexName? |
|---|---|---|---|
| `RagEndpoints.IndexFile` (POST `/api/ai/rag/index-file`) | `RagEndpoints.cs:524` | `FileIndexingService.IndexFileAsync` | ❌ no |
| `RagEndpoints` (other endpoint, line 677) | `RagEndpoints.cs:677` | `FileIndexingService.IndexFileAsync` | ❌ no |
| `RagEndpoints.IndexDocumentsBatch` (POST `/api/ai/rag/index-documents`) | `RagEndpoints.cs:332` | `IRagService.IndexDocumentsBatchAsync` directly | ❌ no |

### B. App-only entry points (background jobs)

| Caller | File:Line | Calls | Currently passes searchIndexName? |
|---|---|---|---|
| `RagIndexingJobHandler.ProcessAsync` | `Services/Jobs/Handlers/RagIndexingJobHandler.cs:131` | `FileIndexingService.IndexFileAppOnlyAsync` | ❌ no |
| `BulkRagIndexingJobHandler.ProcessAsync` | `Services/Ai/Jobs/BulkRagIndexingJobHandler.cs:299` | `FileIndexingService.IndexFileAppOnlyAsync` | ❌ no |
| `IndexingWorkerHostedService` (Office) | `Workers/Office/IndexingWorkerHostedService.cs:168` | `FileIndexingService.IndexFileAppOnlyAsync` | ❌ no |

### C. Pre-extracted content path

| Caller | Calls | Currently passes searchIndexName? |
|---|---|---|
| (Email content extraction → indexing) | `FileIndexingService.IndexContentAsync` | ❌ no |

### D. The canonical write boundary

| Method | File:Line | What it does |
|---|---|---|
| `RagService.IndexDocumentsBatchAsync(documents, ct)` | `RagService.cs:491` | Acquires search client via `GetSearchClientAsync(tenantId, ct)` 2-arg, then `MergeOrUploadDocumentsAsync` |

### E. Out-of-scope write sites (separate features, different indexes)

These also write to AI Search but for **different indexes / feature surfaces**. They are NOT in scope for this fix:

- `DataverseIndexSyncService` — record-matching index (`Services/RecordMatching/`)
- `InvoiceIndexingJobHandler` — invoice classification index
- `VisualizationService` — visualization data index
- `ObservationIndexUpserter` — insights observation index
- `PlaybookEmbeddingService` — playbook embedding index
- `ReferenceIndexingService` — RAG reference index (`spaarke-rag-references`)
- `EmbeddingMigrationService` — embedding migration
- `DocumentVectorBackfillService` — embeddings backfill
- `RagIndexingPipeline` (`Services/Ai/RagIndexingPipeline.cs`) — discovery + sessionfiles indexes (intentional separate routing)
- `PrecedentProjectionSync` — insights precedents

**Scope of this fix is only the document-indexing path**: chunks of user-uploaded files written by `RagService.IndexDocumentsBatchAsync` going into `sprk_searchindexname`-routed indexes (per `appsettings.AiSearch.AllowedIndexes`).

---

## 3. Proposed design — robust + shared

### Design principles

1. **The write boundary is the contract** — `IRagService.IndexDocumentsBatchAsync` is the single chokepoint. All routing must converge here.
2. **NFR-02-safe** — same pattern as Phase B: add a NEW overload accepting `searchIndexName`, keep the existing overload as a no-arg pass-through. Existing tests don't change.
3. **Per-batch routing, not per-chunk** — `searchIndexName` parameterizes the batch (one `searchClient` per batch). This matches the actual write model (one `searchClient.MergeOrUploadDocumentsAsync` per batch) and avoids per-chunk grouping complexity.
4. **Auto-resolve when caller doesn't know** — for background jobs that have a `documentId` but no `searchIndexName`, the BFF should look up `sprk_document.sprk_searchindexname` from Dataverse before indexing. A shared helper service.
5. **Allow-list validation already in place** — the 3-arg `GetSearchClientAsync` (Phase B) validates against `appsettings.AiSearch.AllowedIndexes`. Reuse it.

### Component layers

```
┌─ NEW: SearchIndexNameResolver (shared service) ─────────────────────────┐
│   Purpose: Server-side equivalent of the wizard's resolveSearchIndexNameForRecord.
│   API: Task<string?> ResolveAsync(string? documentId, string? parentEntityType, 
│                                    string? parentEntityId, CancellationToken ct)
│   Chain: documentId → sprk_document.sprk_searchindexname (1st)
│          parentEntityType+Id → parent.sprk_searchindexname (2nd)
│          parent's owning BU → businessunit.sprk_searchindexname (3rd)
│          null (4th — server-side BFF tenant-default takes over via existing 2-tier chain)
│   Used by: RagIndexingJobHandler, BulkRagIndexingJobHandler, IndexingWorkerHostedService
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─ EXTEND: FileIndexRequest + ContentIndexRequest ────────────────────────┐
│   Add: public string? SearchIndexName { get; init; }                    │
│   (Optional — when omitted, falls through to existing tenant chain.)    │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─ EXTEND: IFileIndexingService all 3 methods ────────────────────────────┐
│   No signature change — they consume the request DTOs above.            │
│   Internal pipeline reads request.SearchIndexName and threads through.  │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─ EXTEND: IRagService.IndexDocumentsBatchAsync — TWO-OVERLOAD PATTERN ───┐
│   1. Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(         │
│        IEnumerable<KnowledgeDocument> documents,                         │
│        CancellationToken ct = default)                                   │
│      ← existing signature — NFR-02 preserved.                            │
│                                                                          │
│   2. Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(         │
│        IEnumerable<KnowledgeDocument> documents,                         │
│        string? searchIndexName,                                          │
│        CancellationToken ct = default)                                   │
│      ← NEW — when searchIndexName non-null, uses the 3-arg               │
│      GetSearchClientAsync(tenantId, indexName, ct) overload              │
│      → allow-list validation kicks in.                                   │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─ EXTEND: RagIndexingJobPayload ─────────────────────────────────────────┐
│   Add: public string? SearchIndexName { get; init; }                    │
│   Email + Office Add-in callers set it from sprk_document.sprk_*        │
│   When null, RagIndexingJobHandler calls SearchIndexNameResolver        │
│   as a defensive fallback.                                              │
└─────────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─ EXTEND: Wizard uploadOrchestrator.triggerRagIndexing ──────────────────┐
│   Body adds: searchIndexName (resolved client-side already — Phase A   │
│              fix wave wired this for buildRecordPayload; reuse here)    │
└─────────────────────────────────────────────────────────────────────────┘
```

### File-level change list (BFF + wizard)

**BFF (~7 files modified, 1 new)**:

1. `src/server/api/Sprk.Bff.Api/Services/Ai/IFileIndexingService.cs` — add `SearchIndexName` to `FileIndexRequest` + `ContentIndexRequest` records.
2. `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` — thread `request.SearchIndexName` from the 3 entry methods into the shared internal pipeline → pass to `IRagService.IndexDocumentsBatchAsync` new overload.
3. **NEW**: `src/server/api/Sprk.Bff.Api/Services/Ai/SearchIndexNameResolver.cs` — shared resolver with 3-step chain (document → parent → BU). Used by job handlers.
4. `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` + `RagService.cs` — add 3-arg `IndexDocumentsBatchAsync` overload accepting `string? searchIndexName`; internally calls `GetSearchClientAsync(tenantId, indexName, ct)`.
5. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobPayloads/RagIndexingJobPayload.cs` — add `SearchIndexName` property.
6. `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` — read payload `SearchIndexName`; if null, call `SearchIndexNameResolver`; pass to `FileIndexRequest`.
7. `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs` — same pattern.
8. `src/server/api/Sprk.Bff.Api/Workers/Office/IndexingWorkerHostedService.cs` — same pattern.
9. (Optional) `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` — line 332 `IndexDocumentsBatch` endpoint: if direct caller provides KnowledgeDocument list, accept a query/body parameter `searchIndexName` and pass to new overload.

**Job-enqueueing sites (need to look up sprk_searchindexname from Dataverse before enqueueing)**:

10. `EmailAnalysisJobHandler` or wherever it enqueues `RagIndexing` jobs — look up Document's `sprk_searchindexname` and set on payload.
11. `UploadFinalizationWorker` (Office Add-in) — same.

**Wizard side (1 file modified — small change)**:

12. `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts` — `triggerRagIndexing` already has the resolved value from our post-UAT fix; just add `searchIndexName: resolvedSearchIndexName` to the POST body.

**Tests**:

13. `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/RagServiceTests.cs` — add tests for new overload (both branches: with searchIndexName → 3-arg called; without → 2-arg called for backward compat).
14. `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/FileIndexingServiceTests.cs` — add tests for thread-through.
15. `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/SearchIndexNameResolverTests.cs` (new) — 3-step chain tests.
16. `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/Handlers/RagIndexingJobHandlerTests.cs` — payload threading + fallback resolver.

**Deploy**:

17. Redeploy BFF (`/bff-deploy`).
18. Redeploy DocumentUploadWizard (`/code-page-deploy`).

---

## 4. What about the "files in SPE but not indexed at all" symptom?

The UAT finding was that files land in SPE (`sprk_graphdriveid` populated) but don't appear in AI Search at all. **Two possible explanations**:

### Hypothesis A: The wizard call to `/api/ai/rag/index-file` is failing silently
The wizard's `triggerRagIndexing` is `Promise.allSettled`-wrapped (fire-and-forget). If the BFF endpoint returns 4xx or 5xx, the wizard logs and moves on — UAT-invisible. **Operator check**: pull Application Insights logs for the wizard's upload time window; look for `/api/ai/rag/index-file` 4xx/5xx.

### Hypothesis B: The endpoint succeeds but indexes into the WRONG index
With our fix above, the wizard's Document has `sprk_searchindexname = "spaarke-file-index"` (from BU cascade). Without our fix, the BFF currently writes to the **tenant-default** (`appsettings.AiSearch.KnowledgeIndexName` — probably `sprk-knowledge-shared` per the existing arch doc). So the chunks ARE going somewhere — just not the index UAT was checking.

**Most likely**: Hypothesis B. The UAT was looking at `spaarke-file-index` (or `spaarke-knowledge-index-v2`) and finding nothing; the chunks went into `sprk-knowledge-shared` (the legacy default).

**The fix above resolves both**: by threading `searchIndexName`, chunks land in the **correct** per-record index, and operators can verify by querying the named index directly.

---

## 5. Risks

| Risk | Mitigation |
|---|---|
| Email + Office Add-in routes also need to set `SearchIndexName` in their job payloads — **wider blast radius than just the wizard fix** | Audit + update those enqueueing sites in the same PR. The shared `SearchIndexNameResolver` is the fallback when the enqueueing site forgets. |
| `IndexContentAsync` (email pre-extracted) doesn't have a Document or parent context easily available | The `ContentIndexRequest` already has `DocumentId` — `SearchIndexNameResolver` chain handles it. |
| Tenant-default chain still applies when `searchIndexName` is null/empty — same behavior as the search path. Backward-compat preserved. | NFR-02 verified by leaving 2-arg overload untouched. |
| Allow-list rejection on a stale `sprk_searchindexname` value (e.g., index deprovisioned in Azure but Dataverse still has the value) | The 3-arg resolver throws `SdapProblemException(INDEX_NOT_ALLOWED, 400)`. The job handler should catch this and either fail loudly OR fall back to the default index. **Decision needed**: hard fail vs fallback. Recommendation: **fall back** to default (write a warning log) — failing background jobs on stale config would surprise operators. The wizard path (OBO, synchronous) SHOULD hard-fail so the user knows. |
| `DocumentVectorBackfillService` and similar maintenance jobs may also write to the document chunks index — do they need the routing? | Out of scope for this fix (those are migration tools, single-shot operations). If they need per-record routing later, they'd use the same `IRagService.IndexDocumentsBatchAsync` new overload. |

---

## 6. Recommended scope tiers

### Tier 1 — Minimum viable fix (wizard path only)
- Change list items 1, 2, 4, 12, 13 (5 file changes)
- Tests: 13 only
- Deploys: BFF + DocumentUploadWizard
- **Effort**: ~1 hour
- **Coverage**: Fixes Route 1 (PCF File Upload Wizard) only. Email + Office Add-in routes still route to tenant default.

### Tier 2 — Wizard + shared resolver + job payload extension (Recommended)
- Tier 1 PLUS items 3, 5, 6, 7, 8, 10, 11
- Tests: 13, 14, 15, 16
- Deploys: BFF + DocumentUploadWizard
- **Effort**: ~2-3 hours
- **Coverage**: All three document-creation routes route correctly. Shared `SearchIndexNameResolver` is the future-proof point for ALL background indexing.

### Tier 3 — Tier 2 + `IndexDocumentsBatch` direct API extension + operator log audit
- Tier 2 PLUS item 9
- Plus: enable verbose logging around the `MergeOrUploadDocumentsAsync` call to surface index name + chunk count per write (helps operators audit which index a given upload ended up in)
- **Effort**: ~3-4 hours
- **Coverage**: Most robust. Direct `IndexDocumentsBatch` callers also gain the routing.

---

## 7. Open questions for the operator

1. **Allow-list rejection during background job — hard fail or default-fallback?** (Section 5, Risk 4 above.) Default recommendation: **default-fallback with warning log** for job handlers (background); **hard fail** for OBO API.
2. **Should the existing fallback chain on the BFF (`sprk_aiknowledgedeployment` Dataverse entity → `appsettings.AiSearch.KnowledgeIndexName`) be deprecated** now that per-record routing exists? It's the "tenant-default" when no record-level value is set. Recommendation: KEEP — it's the safety net for Spaarke Dev 1 / Test 1 BUs that intentionally have no `sprk_searchindexname`.
3. **Email + Office Add-in routes — do they need to look up `sprk_searchindexname` at enqueue time, or accept the slight performance cost of letting the job handler resolve it via `SearchIndexNameResolver`?** Recommendation: **let the handler resolve** — keeps enqueueing sites simple; small Dataverse call inside the job is acceptable.

---

## 8. Acceptance criteria for the fix

- [ ] **A new Matter Document** (uploaded via wizard) chunks appear in **the index named by the Matter's BU's `sprk_searchindexname`** (verified by querying that specific index).
- [ ] **A Document with explicit `sprk_searchindexname = "spaarke-file-index"`** (Protected Matter) chunks appear in `spaarke-file-index` ONLY, not in the default.
- [ ] **A Document with NULL `sprk_searchindexname`** continues to land in the tenant default — backward-compat preserved (NFR-02).
- [ ] **400 ProblemDetails `INDEX_NOT_ALLOWED`** returned when the wizard sends a `searchIndexName` not in the allow-list (Phase B contract preserved).
- [ ] Email-to-Document and Office Add-in flows ALSO route correctly to the per-record index.
- [ ] Existing BFF test suite passes unmodified (NFR-02). New tests cover the routing branches.
