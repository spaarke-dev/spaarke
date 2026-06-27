# Indexer Routing Fix — Deployed; Operator Verification Steps

> **Date**: 2026-06-08
> **Commit**: `60bbc413` (Tier 3 indexer routing)
> **Status**: BFF + DocumentUploadWizard deployed to SPAARKE DEV 1.

---

## Quick verification (5 min)

### 1. Hard-refresh browser

Ctrl+Shift+R on SPAARKE DEV 1 → any page hosting the DocumentUploadWizard (e.g. a Matter form).

### 2. Upload a test file

Open the DocumentUploadWizard via "Upload Documents" action on a Matter under the "Spaarke" BU (the one whose `sprk_searchindexname = spaarke-file-index`). Upload any PDF/DOCX.

### 3. Pull Application Insights — verbose write logging

In Azure Portal → Application Insights resource for `spaarke-bff-dev` → Logs:

```kql
traces
| where timestamp > ago(10m)
| where message contains "Indexing batch:"
| project timestamp, message, customDimensions
| order by timestamp desc
```

**Expected log line for a file uploaded under the "Spaarke" BU**:
```
Indexing batch: TenantId=<your-tenant-id> SearchIndexName=spaarke-file-index ResolvedEndpoint=https://...search.windows.net BatchSize=N
```

**`SearchIndexName=spaarke-file-index`** is the audit confirmation. Anything else (e.g. `(tenant-default)`) means the value didn't make it through and we have a regression.

### 4. Query the AI Search index directly

In Azure Portal → Azure AI Search service → `spaarke-file-index` → Search explorer → run:
```json
{ "search": "*", "filter": "documentId eq '<documentId from the upload>'" }
```

You should see N chunks for the uploaded file in `spaarke-file-index`.

### 5. Negative test (default fallback)

Upload a file under a Matter where the BU has NO `sprk_searchindexname` (Spaarke Dev 1 / Test 1). The log should show `SearchIndexName=(tenant-default)` and chunks should land in whatever `appsettings.AiSearch.KnowledgeIndexName` points to (legacy default). This proves NFR-02 — no routing breakage when the value is empty.

---

## If something doesn't work

| Symptom | Diagnosis | Fix |
|---|---|---|
| `SearchIndexName=(tenant-default)` even when uploading under "Spaarke" BU | The wizard's `resolveSearchIndexNameForRecord` returned empty OR `triggerRagIndexing` didn't pick up the value | Check browser console for `[UploadOrchestrator] Resolved sprk_searchindexname:` log line; if it shows the right value but the BFF log shows `(tenant-default)`, the POST body isn't carrying it — re-deploy `sprk_documentuploadwizard` |
| BFF returns 400 INDEX_NOT_ALLOWED | The Dataverse `sprk_searchindexname` value isn't in `appsettings.AiSearch.AllowedIndexes` | Either fix the BU/Document value OR add the index to the allow-list + redeploy BFF |
| Job handler logs WARN "rejected by allow-list ... Falling back to tenant default" | A stale Dataverse value referenced an index no longer in the allow-list (e.g., deprovisioned) | Expected behavior — default-fallback per design. Optionally clean up the stale Dataverse value |
| No "Indexing batch:" log line at all | The `/api/ai/rag/index-file` call failed (auth, network) | Search App Insights for `RAG indexing returned` or `RAG indexing trigger failed` — the wizard's catch logs it |

---

## What was deployed

### BFF (commit `60bbc413`, deployed via `Deploy-BffApi.ps1`)
- NEW `SearchIndexNameResolver` service (server-side 3-step chain)
- `IRagService.IndexDocumentsBatchAsync` 3-arg overload (NFR-02-safe; old 2-arg delegates)
- `RagService` impl: uses 3-arg `GetSearchClientAsync(tenantId, indexName, ct)` when present
- Verbose write logging: `Indexing batch: TenantId={} SearchIndexName={} ResolvedEndpoint={} BatchSize={}`
- `FileIndexRequest` + `ContentIndexRequest`: `SearchIndexName` field added
- `FileIndexingService` (3 entry points): threads request value through
- `RagIndexingJobPayload`: `SearchIndexName` field added
- 3 job-handler consumers: payload-first → resolver fallback → INDEX_NOT_ALLOWED → tenant default
- `RagEndpoints.IndexDocumentsBatch`: accepts `[FromQuery] string? searchIndexName`

### DocumentUploadWizard (`sprk_documentuploadwizard` web resource 1075 KB)
- `uploadOrchestrator.kickOffBackgroundTasks` accepts `searchIndexName`
- `triggerRagIndexing` POST body now includes `searchIndexName`
- Value comes from Phase 2's `resolvedSearchIndexName` (already resolved by the post-UAT fix)

### Tests
- 6140 / 0 / 109 (baseline 6124; +16 new tests)
- New: `SearchIndexNameResolverTests.cs` (7 chain tests)
- Updated: `RagServiceTests.cs` + `FileIndexingServiceTests.cs` + `RagIndexingJobHandlerTests.cs`

---

## What's still out of scope (separate follow-up)

1. **Email-to-Document + Office Add-in routes**: the handler-side resolver fallback covers these defensively, but the enqueueing sites don't proactively set `SearchIndexName` on the job payload. Could be improved by reading from `sprk_document.sprk_searchindexname` at enqueue time (requires extending `IDocumentDataverseService.GetDocumentAsync` to return the field).
2. **Drift audit script `sprk_name` bug** (documented in `053-backfill-dryrun.md`).
3. **Backfill script parameter naming** (`-Environment` vs `-EnvironmentUrl` — cosmetic).
