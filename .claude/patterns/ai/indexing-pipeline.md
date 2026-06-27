# RAG Indexing Pipeline Pattern

> **Last Reviewed**: 2026-05-22
> **Reviewed By**: post-mortem of orphan-chunk + GUID-case regression
> **Status**: Verified

## When
Use when adding or modifying any code path that writes documents to `spaarke-files-index` (or its 1024-token sibling `spaarke-discovery-index`) — new upload surfaces, "Send to Index" variants, background indexing jobs, bulk re-indexers, or analysis result chunkers.

## Read These Files (in order)
1. `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs` — the three entry points (`/api/ai/rag/index-file`, `/api/ai/rag/send-to-index`, `/api/ai/rag/enqueue-indexing`) and the FileIndexRequest contract they expect.
2. `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` — the **single convergence point** (`IndexTextInternalAsync`) where all entry points meet. This is where ID normalization, KnowledgeDocument construction, and the batch call to RagService live.
3. `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` (`IndexDocumentsBatchAsync`) — deployment resolution, per-chunk Azure Search write, telemetry.
4. `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts` (`triggerRagIndexing`) — the wizard's client-side contract: must pass `documentId`, `parentEntity`, `tenantId`, `driveId`, `itemId`, `fileName`.
5. `src/client/webresources/js/sprk_DocumentOperations.js` (`sendToIndex`) — the ribbon's client-side contract; same fields, normalized at source.

## Constraints
- **ADR-013**: AI features extend BFF; no separate indexing service.
- **ADR-016 (AI security)**: tenant isolation enforced via `tenantId` field + filter; never index without it.
- **ADR-015**: never log document content; metrics only.

## Key Rules — MUST follow

- **MUST pass `documentId`** (the Dataverse `sprk_document` GUID) on every user-initiated indexing call. Without it, chunks land as orphans and the BFF cannot write the `sprk_searchindexed` / `sprk_searchindexedon` / `sprk_searchindexname` tracking fields on the Dataverse record.
- **MUST normalize GUIDs to lowercase** before they reach Azure Search. `Edm.String` filters are case-sensitive; `Xrm.Page.data.entity.getId()` returns `{UPPER}`, the Dataverse Web API client returns `lowercase`. `FileIndexingService.IndexTextInternalAsync` normalizes defensively, but the client should also lowercase at source.
- **MUST pass `parentEntity` (entityType / entityId / entityName)** when the document is scoped to a matter / project / invoice. `entityType` is the short form (`matter`, not `sprk_matter`) — strip the `sprk_` prefix.
- **MUST treat a 200 response with `ChunksIndexed=0` as failure**. The BFF guard now returns `Success=false` in this case, but clients should not assume `response.ok` means "indexed."
- **MUST log non-2xx response status and body** on the client. Fire-and-forget patterns that swallow errors hide regressions for months — see [`.claude/FAILURE-MODES.md`](../../FAILURE-MODES.md) AP-2.
- **MUST deploy via `scripts/ai-search/Deploy-AllIndexes.ps1`** (FR-07 canonical deployer) against the canonical schemas `infrastructure/ai-search/spaarke-files-index.json` + `spaarke-discovery-index.json` — these declare `privilege_group_ids` with `filterable: true` so the privilege filter in `RagService.cs` works correctly. The retired `spaarke-knowledge-index-v2` field-flag legacy issue (CRITICAL #1 in `.claude/FAILURE-MODES.md` historical AP-2) is resolved structurally by the canonical schemas + the Deploy-AllIndexes post-deploy invariant verifier (NFR-02) which asserts canonical field flags per AI-SEARCH-INDEX-CATALOG.md §4.

## Observability
On every batch write, `RagService` logs (Information level):
- `Resolved deployment for tenant {TenantId}: Model=... IndexName=... Endpoint=... BatchSize=...`
- `Batch indexed {Success}/{Total} documents for tenant {TenantId} to {IndexName}`
- Per-chunk: `Azure Search rejected chunk {Key} in {IndexName}: status=... error=...` (warning)

These signatures are diagnostic gold — search App Insights `traces` for them when investigating "the file isn't indexed" reports.
