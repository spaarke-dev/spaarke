# Task 032 Evidence — Wire ChatDocumentEndpoints upload into R5 session-files pipeline

> **Status**: ✅ COMPLETE
> **Date**: 2026-06-04
> **Branch**: `worktree-agent-a1af03ee7b7758060` (forked from `work/spaarke-ai-platform-unification-r5` @ `1ba5160b`)
> **Estimated effort**: 1.5h | **Actual effort**: ~1.5h

---

## 1. Files modified / created

| File | Action | Lines changed (approx) |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs` | Modified — added cap check + RAG indexing + manifest persist | +130 |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/ChatDocumentEndpointsTests.cs` | Created — 5 new tests (4 required + 1 manifest-append edge case) | +468 (new) |
| `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` | Row 032 status 🔲 → ✅ 2026-06-04 | +1 / -1 |
| `projects/spaarke-ai-platform-unification-r5/tasks/032-upload-endpoint-wire-session-files.poml` | Status + completed date | +5 / -5 |
| `projects/spaarke-ai-platform-unification-r5/notes/task-032-evidence.md` | This file | new |

---

## 2. Implementation summary

### 2.1 Cap check (defense-in-depth)

Inserted between session-fetch and multipart-form-read (~line 174 of `ChatDocumentEndpoints.cs`). Returns 400 ProblemDetails with stable errorCode `summarize.too-many-files` BEFORE any Redis writes or text extraction. Matches `SummarizeSessionEndpoint`'s contract for the 21st-file scenario.

### 2.2 R5 wiring block (inserted between Step 10 metadata cache and Step 11 202 response)

- Resolves `RagIndexingPipeline` defensively via `httpContext.RequestServices.GetService<>()` — preserves existing AI-off endpoint behavior (returns 503 via `ITextExtractor`'s catch path BEFORE reaching the indexing code; the defensive fetch protects against the unlikely "ITextExtractor real but pipeline missing" anomaly without crashing).
- Builds a `ParsedDocument` from `extractionResult.Text` and calls `IndexSessionFileAsync(parsedDocument, documentId, tenantId, sessionId, fileName, documentId, ct)` — passing `documentId` as both `documentId` and `speFileId` (per task POML §3.1 — chat-document GUID acts as the link for future SPE persistence).
- Reconstructs chunk IDs from the deterministic naming pattern (`{documentId}_s_{index}`, per `RagIndexingPipeline.BuildKnowledgeDocuments` line 448) since `PipelineIndexingResult` only returns count.
- Builds `ChatSessionFile` (six fields per `ChatSession.cs:134`) with `SearchDocumentIdsCsv` = comma-joined chunk IDs.
- Appends to `session.UploadedFiles` via `with` expression (immutable record).
- Persists via `sessionManager.UpdateSessionCacheAsync(updatedSession, ct)` (internal virtual; same-assembly access; refreshes Redis hot tier + fire-and-forget Cosmos write-through per D-06).
- Catches `FeatureDisabledException` → 503 (mirror of step 8a pattern).
- Other exceptions logged but non-fatal — legacy Redis writes already succeeded; the file is at least discoverable via the R3 doc-upload Redis path for back-compat.

### 2.3 Tests added (5 — 4 required + 1 edge case)

| # | Test | Verifies |
|---|---|---|
| 1 | `Upload_Success_CallsIndexSessionFileAsync_WithExpectedArgs` | `MergeOrUploadDocumentsAsync` invoked on session-files SearchClient with `(TenantId, SessionId, FileName)` propagated (observed via `SearchIndexClient` mock). |
| 2 | `Upload_Success_PersistsUpdatedSession_WithNewUploadedFileEntry` | `UpdateSessionCacheAsync` invoked with a session whose `UploadedFiles` gained one new entry; verified the entry's `FileName`, `ContentType`, `SizeBytes`, `SearchDocumentIdsCsv` all populated. |
| 3 | `Upload_Success_AppendsToExistingUploadedFiles` | Manifest with 2 existing files grows to 3 (append semantics, not replace). |
| 4 | `Upload_AtCap_Returns400_TooManyFiles_NoMutation` | 21st upload to a 20-file session → 400 + `summarize.too-many-files`; `MergeOrUploadDocumentsAsync` NEVER called; `PersistedSession` is null (no manifest mutation). |
| 5 | `Upload_Success_StillWritesLegacyRedisKeys_ForBackCompat` | `doc-upload:`, `doc-binary:`, `doc-upload-meta:` Redis keys all populated after a successful upload (no R3 consumer left behind). |

All 5 tests pass.

---

## 3. Build + test results

| Metric | Value |
|---|---|
| `dotnet build -c Release src/server/api/Sprk.Bff.Api/` | ✅ Build succeeded, 0 errors, 16 warnings (all pre-existing) |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "ChatDocumentEndpointsTests"` | ✅ 5 passed, 0 failed, 0 skipped (38 ms) |
| Full unit suite | ✅ **6228 passed, 0 failed, 111 skipped** (1m13s) — 5 new tests added; baseline was 6223 |
| Zero regressions | ✅ Verified |

---

## 4. BFF publish size verification (CLAUDE.md §10)

| Metric | Value |
|---|---|
| Command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| Result | ✅ Publish succeeded |
| Uncompressed (`du -sh`) | 139 MB |
| **Compressed (tar.gz)** | **44.16 MB** (46,308,758 bytes) |
| Baseline (2026-05-26) | ~45.65 MB |
| **Delta** | **−1.49 MB** (slightly smaller than baseline) |
| R5 per-task budget (CLAUDE.md §3.6) | ≤ +1 MB → ✅ well within budget |
| Absolute ceiling (NFR-01) | ≤ 60 MB compressed → ✅ well under |

The slight decrease is incidental — likely from incremental publish artifact reduction or normalization across runs; the delta is within measurement noise. No CVE-introducing packages added.

---

## 5. ADR compliance

| ADR | Compliance |
|---|---|
| **ADR-010** (DI minimalism) | ✅ Used existing `RagIndexingPipeline` concrete; ZERO new top-level Program.cs lines; defensive `RequestServices.GetService` avoids new registration |
| **ADR-014** (tenant isolation) | ✅ `IndexSessionFileAsync` receives both `tenantId` AND `sessionId`; index entries carry both partition keys |
| **ADR-018** (Flag scope discipline) | ✅ No new feature flag; inherits compound AI gate via existing `ITextExtractor` Null-Object fallback |
| **ADR-019** (ProblemDetails) | ✅ Stable errorCode `summarize.too-many-files` matches `SummarizeSessionEndpoint`'s constant |
| **ADR-015** (no content logging) | ✅ Only metadata in logs (DocumentId, ChunkCount, DurationMs, ManifestSize) — no text or binary content |
| **D-06** (ChatSessionManager) | ✅ Persistence via `UpdateSessionCacheAsync` seam — Redis + fire-and-forget Cosmos |
| **CLAUDE.md §10 F.1** (asymmetric registration) | ✅ Defensive resolution preserves the existing endpoint-tolerance contract; no new `if (flag) { ... }` block introduced |

---

## 6. Live-patched tid-claim fix preservation

Lines 151-153 (`UploadDocumentAsync`) and lines 443-445 (`PersistDocumentAsync`) — the schema URL fallback applied to Dev on 2026-06-04 — are PRESERVED in this commit. Both endpoints check:

```csharp
var tenantId = httpContext.User.FindFirst("tid")?.Value
    ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
    ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
```

This is the second R5 file with the same pattern; once promoted to master via the planned follow-up PR (per `summarize-vertical-remediation-plan.md` §4), the live-deploy/master drift closes.

---

## 7. Out-of-scope items (intentionally NOT touched)

Per task POML scope boundary:
- `ChatContext` model — task 033 territory
- `PlaybookChatContextProvider` — task 033 territory
- `SprkChatAgentFactory` — task 033 territory
- Frontend code — task 034 territory
- Anything under `.claude/` — sub-agent permission boundary

---

## 8. Deviations from task POML

None substantive. Two minor decisions worth noting:

1. **Defensive resolution via `RequestServices.GetService<>()` instead of constructor parameter injection.** The task POML proposed adding `IRagIndexingPipeline` as a hard handler parameter. I deviated to use defensive resolution because:
   - `RagIndexingPipeline` is conditionally registered (gated on `DocumentIntelligence:Enabled`); injecting as a hard param would cause DI parameter-inference failure at endpoint mapping time when AI is off, and the existing `EndpointMappingExtensions.MapChatDocumentEndpoints()` try/catch would log + drop the entire endpoint. That's a regression — the endpoint currently mounts unconditionally and returns 503 via the `ITextExtractor` Null-Object at first call.
   - The class is `sealed` so an `IRagIndexingPipeline` interface doesn't exist (per ADR-010 "interface-for-testability-alone is forbidden"); creating one would have been a scope expansion.
   - The defensive pattern mirrors `WorkspaceLayoutAuthorizationFilter` and other filters that use `httpContext.RequestServices.GetService<T>()` for late-binding optional deps.

2. **No new `AppendUploadedFileAsync` helper on `ChatSessionManager`.** The task POML left this as optional ("only if it makes endpoint code materially cleaner"). The inline `session with { UploadedFiles = ... }` followed by `UpdateSessionCacheAsync(...)` is already concise (4 lines) and self-documenting. Adding a helper would have introduced a new public method with marginal benefit and would have required a corresponding test.

Both deviations preserve the acceptance criteria; neither alters the contract.

---

## 9. Next steps

- **Coordinated push**: main session will push this commit + task 033's commit together after both complete and main does build verification.
- **Deploy**: after merge, deploy via `Deploy-BffApi.ps1` to Spaarke Dev.
- **Task 034**: frontend auto-trigger (requires 032 + 033 deployed first).
- **Task 035**: re-run SC-18 walkthrough end-to-end; capture solo-SME signoff.
