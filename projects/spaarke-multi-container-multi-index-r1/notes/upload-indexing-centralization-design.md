# Centralized Post-Upload RAG Indexing — Design

> **Date**: 2026-06-08
> **Author**: Claude (session continuation from indexer-routing-fix work)
> **Status**: Draft (awaiting approval)
> **Scope extension to**: `spaarke-multi-container-multi-index-r1` (Tier 3 indexer routing was incomplete — only DocumentUploadWizard's `/api/ai/rag/index-file` path was wired; the 4 Create* wizards upload to SPE but never enqueue indexing)
> **Supersedes the wizard-side trigger model**: `uploadOrchestrator.kickOffBackgroundTasks` becomes redundant; deleted after rollout.

---

## 1. Problem statement

Files uploaded via the Create* wizards (Matter, Project, WorkAssignment, Event) land in SharePoint Embedded but **never reach the AI Search index**. UAT confirmed: file present in SPE container, zero chunks in `spaarke-file-index`. Documents created by these wizards are invisible to RAG.

Root cause: only one of the BFF upload entry points enqueues RAG indexing today. The 4 Create* wizards route through `POST /api/spe/containers/{id}/items/upload` (via `EntityCreationService.uploadFilesToSpe`), and that endpoint has **no indexing enqueue**.

This is a long-standing architectural gap, not a regression. The Tier 3 indexer-routing fix scoped only DocumentUploadWizard's call site. The other wizards were never instrumented.

---

## 2. Current state map

### 2.1 SPE upload entry points (server-side)

| Endpoint | Used by | Enqueues RAG? |
|---|---|---|
| `POST /api/spe/containers/{id}/items/upload` (small files) | 4 Create* wizards via `EntityCreationService.uploadFilesToSpe` | ❌ **MISSING** |
| `POST /api/obo/drives/{driveId}/upload-session` + `/chunk` (large files) | Chunked client uploads | ❌ **MISSING** |
| `POST /api/ai/chat/sessions/{id}/documents` | Chat attachment upload | ❌ (intentionally — chat scratch) |
| `POST /api/ai/rag/index-file` (explicit indexing) | DocumentUploadWizard `triggerRagIndexing` | ✅ |
| `UploadFinalizationWorker` (Office Add-in finalize) | Office Add-in | ✅ |
| `EmailToDocumentJobHandler` | Email-to-Document Service Bus job | ✅ |
| `KnowledgeBaseEndpoints` (manual KB ingest) | Admin/automation | ✅ |
| `AnalysisResultPersistence` + `DeliverToIndexNodeExecutor` | Internal AI workflows | ✅ |

### 2.2 Existing canonical pattern (DO NOT reinvent)

The Office Add-in worker contains the canonical reference implementation:

[`UploadFinalizationWorker.EnqueueRagIndexingAsync`](../../src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs#L1280-L1330) — wraps:

- `JobContract` construction with `RagIndexingJobHandler.JobTypeName`
- `JobSubmissionService.SubmitJobAsync` (Service Bus enqueue)
- Idempotency key `rag-index-{driveId}-{itemId}`
- `MaxAttempts = 3`
- **Try/catch — enqueue failure does not fail the upload** (RAG is non-critical to upload success)

The `RagIndexingJobPayload` already carries `SearchIndexName` (added by Tier 3). The downstream handler runs the resolver chain: payload value → `ISearchIndexNameResolver` (parent record → BU cascade) → INDEX_NOT_ALLOWED → tenant default. **Nothing in the resolver or handler needs to change.**

### 2.3 What the architecture demands

> "Whenever a file is uploaded — through any mechanism — if the file goes to SPE it has to go to the search index that corresponds to either the creating user's Business Unit OR to the search index of the parent record."

The decision belongs at the **single seam where SPE writes complete server-side** — not scattered across N clients. Every existing or future upload path goes through one of two endpoints (small / chunked). Wiring indexing there means:

- Create* wizards: automatic
- DocumentUploadWizard: still works through its current `/api/ai/rag/index-file` route (will be decommissioned in Phase 3)
- Office Add-in: already correct (independent post-upload finalization path)
- Email-to-Document: already correct (independent job path)
- Any future surface (mobile, Power Automate, API consumer): automatic — no new code

---

## 3. Proposed architecture

### 3.1 Single shared helper

Add **one** server-side helper that every upload endpoint calls after successful SPE write:

```csharp
// src/server/api/Sprk.Bff.Api/Services/Ai/PostUploadIndexingEnqueuer.cs (new)
public interface IPostUploadIndexingEnqueuer
{
    Task EnqueueIfApplicableAsync(
        PostUploadIndexingRequest request,
        CancellationToken ct);
}

public sealed record PostUploadIndexingRequest(
    string TenantId,
    string DriveId,
    string ItemId,
    string FileName,
    long FileSizeBytes,
    string? ContentType,
    Guid? DocumentId,                  // null when no sprk_document created yet
    ParentEntityContext? ParentEntity, // sprk_matter / sprk_project / sprk_workassignment / sprk_event
    string Source,                     // "SpeContainerUpload" | "OboUploadSession" | "OfficeAddin" | etc.
    string CorrelationId);
```

Default implementation (`PostUploadIndexingEnqueuer`) collapses the duplicated logic from `UploadFinalizationWorker` and `EmailToDocumentJobHandler` into one place. Both existing call sites refactor to consume the helper (Phase 2, low risk — pure deduplication).

### 3.2 Wiring sequence at upload endpoints

```
Client → BFF upload endpoint
         ↓
         SpeFileStore.UploadSmallAsync / CreateUploadSessionAsync + UploadChunkAsync
         ↓ (success path; throws propagate to client unchanged)
         IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync(...)
         ↓
         JobSubmissionService.SubmitJobAsync(JobContract)
         ↓
         Service Bus → RagIndexingJobHandler → ISearchIndexNameResolver chain → AI Search
```

- The enqueue helper runs **after** the response body is built but **inside** the request-scope `try`. Failures are logged and swallowed (non-fatal).
- For chunked uploads, enqueue fires on **session-complete** (final chunk PUT returns success with item ID) — not per chunk.

### 3.3 Files changed

| File | Change | Lines | Risk |
|---|---|---|---|
| `Services/Ai/IPostUploadIndexingEnqueuer.cs` | NEW | ~30 | Low |
| `Services/Ai/PostUploadIndexingEnqueuer.cs` | NEW (extracted from UploadFinalizationWorker pattern) | ~150 | Low |
| `Infrastructure/DI/AnalysisServicesModule.cs` | Register `IPostUploadIndexingEnqueuer` as scoped (top-level, unconditional — same lesson as ISearchIndexNameResolver) | +1 | Low |
| `Api/ContainerItemEndpoints.cs` (`UploadContainerItem`) | After `_speFileStore.UploadSmallAsync` succeeds, build request + call `enqueuer.EnqueueIfApplicableAsync` | +20 | Med |
| `Api/OBOEndpoints.cs` (`upload-session` complete path) | Same as above on session completion | +20 | Med |
| `Workers/Office/UploadFinalizationWorker.cs` | Replace inline enqueue with helper call (deduplication) | -50 / +5 | Low |
| `Services/Communication/IncomingCommunicationProcessor.cs` | Replace inline enqueue with helper call | -50 / +5 | Low |
| Tests | Add unit tests for `PostUploadIndexingEnqueuer` (success path + each fail-protection branch) | +100 | — |

**No changes** to: `RagIndexingJobHandler`, `ISearchIndexNameResolver`, `RagIndexingJobPayload`, `JobSubmissionService`, `RagService.IndexDocumentsBatchAsync`, the resolver chain. Those are already correct.

---

## 4. Edge / fail-protection features

### 4.1 Non-fatal enqueue (MUST)

Every call to `EnqueueIfApplicableAsync` is wrapped in try/catch. **Enqueue failures never fail the SPE upload.** Logged at WARN with correlation ID. Operator can re-trigger manually via `POST /api/ai/rag/index-file`.

Rationale: SPE upload is the contract with the user — the file is safely stored. Indexing is best-effort; transient Service Bus outage shouldn't roll the upload back.

### 4.2 Idempotency (MUST)

Idempotency key: `rag-index-{driveId}-{itemId}`. If the same file is uploaded twice (e.g., user retries after network blip), Service Bus deduplicates. The handler is also idempotent — re-running on the same chunks produces the same AI Search documents (same chunk IDs).

### 4.3 Content-type filtering (SHOULD)

Skip enqueue for content types that have no extractable text:

```
SKIP if ContentType matches any of:
  video/*, audio/*, application/zip, application/x-zip-compressed,
  application/x-rar-compressed, application/x-7z-compressed,
  application/octet-stream (when FileName extension is in skip-list:
  .exe, .dll, .bin, .iso, .dmg, .img)
```

Log at INFO: `"Skipping RAG enqueue for {FileName}: ContentType {ContentType} not indexable"`.

Rationale: enqueueing these wastes a Service Bus message + a handler invocation that will fail extraction. The handler already handles this gracefully (returns "no text found"), but avoiding the round-trip is cheaper.

**Note**: PDFs of scanned images may have no extractable text but should still be enqueued — the handler's OCR path may handle them. Don't preemptively skip PDFs.

### 4.4 Size cap (SHOULD)

Skip enqueue if `FileSizeBytes > MAX_INDEXABLE_BYTES` (default 200 MB, configurable).

Rationale: very large files (full DVDs, datasets) overwhelm the chunker + embedding API costs. Operator can manually trigger if needed via `/api/ai/rag/index-file`.

### 4.5 Empty file (MUST)

Skip enqueue if `FileSizeBytes == 0`. Log at INFO.

### 4.6 SearchIndexName resolution failure → still enqueue (MUST)

If the parent-record + BU cascade can't determine `SearchIndexName`, **enqueue with `SearchIndexName = null`**. The handler's default-fallback path (Tier 3) handles it: write to tenant default + log WARN.

Rationale: never fail-closed on routing. A file in the tenant-default index is recoverable (operator can re-index after fixing the Dataverse value). A file in NO index is invisible.

### 4.7 Tenant context required (MUST)

Skip enqueue if `TenantId` is empty/missing — log at ERROR with full request context. This indicates a misconfigured upload path (SpeAdmin? anonymous endpoint?) that should not be silently indexing.

### 4.8 Bulk upload handling (CONSIDER)

For N files uploaded in one batch by EntityCreationService (e.g., user drops 50 files into the wizard), each enqueue is independent. Service Bus + the handler can absorb this — no batching at the enqueue layer required for normal volumes (1-100 files).

For higher volumes, `BulkRagIndexingJobPayload` exists and can be wired in a follow-up. Out of scope for this fix.

### 4.9 Observability (MUST)

Every enqueue logs:

```
[PostUploadIndexingEnqueuer] Enqueued RAG indexing job {JobId} for {FileName}
  (DriveId={DriveId} ItemId={ItemId} DocumentId={DocumentId}
   SearchIndexName={SearchIndexName ?? "(resolver-pending)"}
   Source={Source} TenantId={TenantId} CorrelationId={CorrelationId})
```

Every skip logs at INFO with reason. Every enqueue failure logs at WARN with the inner exception.

The Tier 3 verbose log line at the indexing step (`"Indexing batch: SearchIndexName=..."`) is preserved — operator can correlate enqueue → handler → final index via correlation ID.

### 4.10 Feature flag (MUST)

Add `Indexing:PostUploadEnqueueEnabled` (default `true`) to config. When `false`, helper short-circuits with INFO log. Allows operator to disable indexing wholesale during incidents (Service Bus saturation, AI Search outage) without redeploying.

### 4.11 Disaster recovery / backfill (REFERENCE)

If post-upload indexing is dropped for an extended period (Service Bus outage, feature flag off), operators can backfill via:

```powershell
# Re-trigger indexing for all sprk_document records created since {date}
# (calls /api/ai/rag/index-file for each — idempotent)
.\scripts\Reindex-DocumentsSince.ps1 -SinceDate "2026-06-08T00:00:00Z" -DryRun
```

(This script doesn't exist yet — listed here as a follow-up follow-on, NOT scoped for this fix.)

---

## 5. Test plan

### 5.1 Unit tests (`Sprk.Bff.Api.Tests`)

| Test | Asserts |
|---|---|
| `EnqueueIfApplicableAsync_HappyPath_SubmitsJob` | `IJobSubmissionService.SubmitJobAsync` called once with correct payload |
| `EnqueueIfApplicableAsync_EmptyFile_SkipsEnqueue` | No job submitted; INFO log present |
| `EnqueueIfApplicableAsync_SkippableContentType_SkipsEnqueue` | No job submitted; INFO log with content-type reason |
| `EnqueueIfApplicableAsync_LargeFile_SkipsEnqueue` | Above-cap file → no job; INFO log |
| `EnqueueIfApplicableAsync_NullSearchIndexName_StillEnqueues` | Job submitted with `SearchIndexName = null` (handler fallback) |
| `EnqueueIfApplicableAsync_MissingTenantId_SkipsAndLogsError` | No job submitted; ERROR log |
| `EnqueueIfApplicableAsync_FeatureFlagOff_SkipsEnqueue` | No job submitted; INFO log with feature-flag reason |
| `EnqueueIfApplicableAsync_SubmitThrows_LogsWarningDoesNotPropagate` | Caller sees no exception; WARN log |

### 5.2 Integration tests (`Sprk.Bff.Api.IntegrationTests`)

| Test | Asserts |
|---|---|
| `UploadContainerItem_Success_EnqueuesIndexingJob` | After 200 OK from upload, mocked `IJobSubmissionService` shows 1 call |
| `OboUploadSession_Complete_EnqueuesIndexingJob` | After session-complete PUT chunk, enqueue fires once |
| `UploadFinalizationWorker_PostHelperRefactor_StillEnqueues` | Office Add-in path unchanged behavior (regression guard) |

### 5.3 UAT after deploy

1. **Matter wizard** — upload via Create New Matter under "Spaarke" BU:
   - SPE: file appears in container ✅
   - Dataverse: `sprk_document` record with `sprk_searchindexname = spaarke-file-index` ✅
   - App Insights: `[PostUploadIndexingEnqueuer] Enqueued RAG indexing job ...` ✅
   - App Insights: `Indexing batch: SearchIndexName=spaarke-file-index` (downstream handler) ✅
   - AI Search: chunks present in `spaarke-file-index` for the file's documentId ✅
2. **Project / WorkAssignment / Event wizards** — same checks (regression guard for shared code path)
3. **DocumentUploadWizard** — should continue working unchanged (uses its own `/api/ai/rag/index-file` path); after Phase 3 cleanup, it converges with the wizards
4. **Office Add-in** — regression guard; existing path still works
5. **Negative test** — upload a `.zip` file: enqueue skipped, INFO log present
6. **Negative test** — Service Bus simulated outage (set namespace to wrong creds in dev): upload still succeeds, WARN log present, file in SPE but no chunks in AI Search until Service Bus restored

---

## 6. Rollout plan

### Phase 1: Helper + DI registration + tests (no behavior change)

- New `IPostUploadIndexingEnqueuer` + impl + tests (all passing)
- DI registration at top of `AddAnalysisServicesModule` (unconditional — same lesson as ISearchIndexNameResolver)
- **Build clean, all tests green, no production behavior change yet**
- Commit + push

### Phase 2: Refactor existing enqueue sites to use helper

- `UploadFinalizationWorker.EnqueueRagIndexingAsync` → call helper
- `EmailToDocumentJobHandler.EnqueueRagIndexingJobAsync` → call helper
- Regression tests pass (Office Add-in + Email-to-Document still enqueue)
- **No new enqueue sites yet, no production behavior change**
- Commit + push

### Phase 3: Wire helper into all 5 missing endpoints

- `UploadContainerItem` (`/api/spe/containers/{id}/items/upload`) → call helper after `UploadSmallAsync` succeeds
- `/api/obo/upload-session/chunk` complete path → call helper after final chunk PUT
- `/api/containers/{containerId}/upload` (alternative small-upload route) → call helper after `UploadSmallAsync` succeeds
- `/api/upload-session/chunk` (alternative chunked route) → call helper after final chunk PUT
- **`PersistDocumentAsync` (`/api/ai/chat/sessions/{id}/documents/{docId}/persist`)** → call helper after `UploadSmallAsUserAsync` succeeds (NEW per §8.5.5)
- Tests pass (including per-endpoint integration test)
- **Production behavior change: Create* wizards + External SPA + SprkChat persist all now trigger indexing**
- Deploy BFF via `Deploy-BffApi.ps1` (hash-verify + healthz)
- UAT per Section 5.3 + add SprkChat persist test case (upload to chat → click "Save to Spaarke" → verify chunks land in `spaarke-file-index` for the parent Matter)

### Phase 4: Decommission wizard-side trigger (cleanup)

- DocumentUploadWizard's `uploadOrchestrator.kickOffBackgroundTasks` + `triggerRagIndexing` deleted (no longer needed — server-side helper handles it via the same `UploadContainerItem` endpoint)
- `/api/ai/rag/index-file` endpoint retained for manual operator re-triggers (DR / backfill)
- Wizard rebuilt + redeployed
- Final UAT

---

## 7. Rollback plan

Each phase is independently revertable:

- **Phase 1 rollback**: revert the commit. Helper unused; zero impact.
- **Phase 2 rollback**: revert. Existing call sites go back to their inline enqueue (regression-tested).
- **Phase 3 rollback**: revert. Create* wizards stop enqueueing — files in SPE but not in AI Search (the state we're fixing). Recovery via manual `/api/ai/rag/index-file` re-trigger script (Section 4.11).
- **Phase 4 rollback**: revert wizard deploy. DocumentUploadWizard goes back to dual-path (server enqueues + wizard enqueues — idempotency key dedupes via Service Bus). Safe.

**Fast-stop**: set `Indexing:PostUploadEnqueueEnabled = false` in App Service config + restart. No new indexing enqueues fire. Files continue to upload normally. (Section 4.10.)

---

## 8. Open questions

1. **Chat attachment upload** (`/api/ai/chat/sessions/{id}/documents`): should this enqueue indexing? Current behavior: no (chat attachments are conversation-scoped scratch; they don't need to appear in tenant-wide RAG search). **Recommendation**: leave as-is. Out of scope.
2. **Container provisioning** (initial empty-container creation by SpeAdmin): no files to index. **Not applicable**.
3. **File copy/move** between containers (if such a path exists): would need separate enqueue logic. **Recommendation**: out of scope for this fix; document as follow-up.
4. **Additional small/chunked endpoints discovered**: `Api/UploadEndpoints.cs` defines `POST /api/containers/{containerId}/upload` and `PUT /api/upload-session/chunk` — these are alternative routes (non-`/spe/` prefix) that also go through `SpeFileStore`. Phase 3 must wire the helper into BOTH endpoint families (`/api/spe/containers/{id}/items/upload` AND `/api/containers/{containerId}/upload`; `/api/obo/upload-session/chunk` AND `/api/upload-session/chunk`). Treat as 4 endpoints, same helper call.
5. **SprkChat persist** (`POST /api/ai/chat/sessions/{id}/documents/{docId}/persist`) — discovered during scope review: this promotes a chat-session Redis attachment to permanent SPE storage via `SpeFileStore.UploadSmallAsUserAsync` but does **not** enqueue tenant RAG indexing. Same architectural gap as Create* wizards. **Phase 3 wires this as the 5th endpoint.** See §8.5.5 below.

---

## 8.5.5 SprkChat / SpaarkeAI conversation surface

### Two distinct flows, only one needs the helper

#### Flow A: Session-scoped attachment (`POST /api/ai/chat/sessions/{id}/documents`) — already correct, intentionally segregated

- Client uploads a file as conversation context for the AI session
- Binary stored in **Redis only** (transient — not SPE)
- Text extracted via DocumentIntelligence
- **Already indexes** to a **separate** `spaarke-session-files` AI Search index via `RagIndexingPipeline.IndexSessionFileAsync`
- Carries `tenantId + sessionId` partition keys per ADR-014 / R5 FR-09
- **Intentionally NOT in the tenant RAG index** — session attachments are conversation-scoped scratch and should not appear in tenant-wide search results
- **Helper does NOT touch this endpoint.** Confirmed correct.

#### Flow B: Persist to SPE (`POST /api/ai/chat/sessions/{id}/documents/{docId}/persist`) — **NEW gap to fix**

- User clicks "Save to Spaarke" in chat to keep a session attachment permanently (e.g., associate to a Matter)
- Retrieves binary from Redis (idempotent — checks `doc-persist:{sessionId}:{documentId}` marker)
- **Uploads to SPE** via `SpeFileStore.UploadSmallAsUserAsync` ([`ChatDocumentEndpoints.cs:716`](../../src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs#L716))
- Creates `sprk_document` record linked to a parent entity (Matter/Project/etc.)
- **Does NOT enqueue tenant RAG indexing** — verified by greppting the post-upload code path; no `EnqueueRagIndexing` / `SubmitJobAsync` call exists
- **Result**: chat-promoted documents land in SPE + Dataverse but are invisible to `spaarke-file-index` tenant search
- **Severity**: equivalent to the Create* wizard gap. A user-asked-AI-to-summarize-then-promoted-to-Matter file should be tenant-searchable. Today it isn't.

**Fix**: Phase 3 wires `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync` immediately after the successful `UploadSmallAsUserAsync` in `PersistDocumentAsync`. The `ParentEntity` context is already available (the persist payload identifies the target Matter/Project/etc.) — feed it into the resolver chain so the file lands in the parent's preferred index.

**Idempotency note**: PersistDocumentAsync already has an idempotency marker (`doc-persist:{sessionId}:{documentId}`). The helper's own idempotency key (`rag-index-{driveId}-{itemId}`) is independent — duplicate persist calls dedupe via the persist marker (no second SPE upload) AND the indexing job dedupes via the rag-index key (no second indexing job).

### SpaarkeAi (LegalWorkspace) `ConversationPane`

- Uses Flow A (session-scoped). Already correct via `RagIndexingPipeline.IndexSessionFileAsync` to `spaarke-session-files`. No change.

---

## 8.5.6 External SPA (B2B guest portal — `src/client/external-spa/`)

- B2B guest contacts (Secure Project access) upload via `DocumentUploadPage.tsx` + `DocumentLibrary.tsx`
- Client uses `createBffUploadService` from `@spaarke/auth` — the same shared upload service backing internal callers
- That routes through one of the 4 standard BFF upload endpoints from §3.3
- **Net effect**: when Phase 3 wires the helper into those endpoints, External SPA gets indexing automatically
- **No External-SPA-specific code change required**

This validates the single-seam thesis: a completely separate auth surface (sessionStorage MSAL per ADR-028, per-tab guest isolation) inherits the fix for free.

**Access-control note**: Indexing is independent of access enforcement. AI Search documents are indexed regardless of who uploaded them; access filtering happens at query time via the BFF's authorization layer. External-SPA-uploaded documents being in `spaarke-file-index` doesn't expose them to internal users beyond what the existing access rules already allow.

---

## 8.5.7 Surfaces summary (revised)

| Surface | Indexing today | After fix | Helper touches | Client change? |
|---|---|---|---|---|
| Create Matter / Project / WorkAssignment / Event wizards | ❌ NONE | ✅ tenant index | Endpoints #1, #3 | None |
| DocumentUploadWizard | ✅ via wizard-side `triggerRagIndexing` | ✅ tenant index (via server-side helper) | Endpoints #1 / #3 | Phase 4 cleanup |
| Outlook Add-in (save attachment) | ✅ via `UploadFinalizationWorker` | ✅ tenant index (via helper) | (worker refactor Phase 2) | None |
| Word Add-in (save document) | ✅ via `UploadFinalizationWorker` | ✅ tenant index (via helper) | (worker refactor Phase 2) | None |
| Email-to-Document Service Bus | ✅ already enqueues | ✅ tenant index (via helper) | (refactor Phase 2) | None |
| **SprkChat session upload (Flow A)** | ✅ `spaarke-session-files` (intentional) | ✅ unchanged — session index, NOT helper | **Not wired** | None |
| **SprkChat persist to SPE (Flow B)** | ❌ NONE (NEW GAP) | ✅ tenant index | **Endpoint #5 (NEW)** | None |
| External SPA (B2B portal) | ❌ (rides on endpoints #1/#3) | ✅ tenant index (via helper) | None special — endpoints #1/#3 | None |
| Future Teams app | N/A | ✅ when built | None special — endpoints #1/#3 | Spec must route through BFF |
| Future surfaces | N/A | ✅ when built | None special | None (single seam) |

---

## 8.5. Office / Outlook / Teams add-in implications

### 8.5.1 Office Add-ins (Word, Outlook taskpane) — already correct, will benefit

**Current state**:
- Client side: `src/client/office-addins/shared/services/ApiClient.ts` exposes `uploadFile(endpoint, file, fileName)` — used by both Word + Outlook taskpanes
- Server side: uploads route through standard `SpeFileStore` endpoints
- **Post-upload finalization**: `UploadFinalizationWorker` (Service Bus background worker) runs and **already** calls `EnqueueRagIndexingAsync` — the canonical pattern this design is consolidating
- Result: Office Add-in uploads **already** end up in AI Search via the correct path

**Impact of this fix**:
- **Phase 2 refactor**: `UploadFinalizationWorker.EnqueueRagIndexingAsync` (`UploadFinalizationWorker.cs:1280-1330`) is replaced by a call to `IPostUploadIndexingEnqueuer`. **Pure deduplication — zero behavior change**.
- Regression test (Section 5.2): integration test pins the existing enqueue behavior. If Phase 2 breaks Office Add-in indexing, the test fails immediately.
- Client code (Word/Outlook taskpane): **no change required**.

**Net effect for Office Add-ins**: more resilient (single helper to maintain), zero user-visible change.

### 8.5.2 Outlook add-in — save-attachment-to-SPE flow

**Current state**:
- `OutlookHostAdapter.ts` reads email attachments via Office.js → calls `ApiClient.uploadFile` → BFF upload endpoint
- Same `UploadFinalizationWorker` finalization path applies
- Attachments saved to SPE → already indexed

**Impact**: same as Office Add-ins above. The centralized helper will handle this path automatically post-Phase 3 (when the underlying BFF upload endpoints all use the helper).

**One caveat**: Outlook attachments often have `application/octet-stream` content-type with arbitrary extensions. The skip-list in §4.3 must NOT inadvertently skip legitimate email attachments. **Test case**: save an Outlook .msg / .eml attachment → should be indexed. Save a .exe attachment → should be skipped (security + indexability).

### 8.5.3 Teams app — not yet implemented, design enables it

**Current state**:
- `projects/sdap-teams-app/` contains spec.md + design.md (status: "Ready for Implementation")
- No deployed Teams code today
- Planned surfaces: Personal App, configurable Tabs, Messaging Extension, **Message Action ("Save attachment to Spaarke")**

**Impact**: when the Teams app ships, the "Save attachment to Spaarke" Message Action will POST to one of the centralized BFF upload endpoints. Because the helper is wired there, **Teams uploads will automatically index — no Teams-specific code needed**.

**Design recommendation for the Teams project**: the Teams app MUST route all SPE saves through one of the four canonical BFF endpoints (`/api/spe/containers/.../upload`, `/api/containers/.../upload`, or the two `upload-session` paths). It MUST NOT call Graph directly to upload to SPE — that would bypass the BFF (and therefore the indexing helper). Add this requirement to `projects/sdap-teams-app/design.md` post-Phase 3.

### 8.5.4 Summary table

| Surface | Today | After this fix | Surface-specific code change? |
|---|---|---|---|
| Create Matter / Project / WorkAssignment / Event wizards | ❌ Files in SPE, not in AI Search | ✅ Auto-indexed | **None** (shared lib EntityCreationService unchanged) |
| DocumentUploadWizard | ✅ Works (via `/api/ai/rag/index-file`) | ✅ Still works; Phase 4 cleans up the redundant client-side trigger | Phase 4 only (delete `triggerRagIndexing`) |
| Outlook Add-in (save email attachment) | ✅ Works (via `UploadFinalizationWorker`) | ✅ Works (now via shared helper) | **None** |
| Word Add-in (save document) | ✅ Works (via `UploadFinalizationWorker`) | ✅ Works (now via shared helper) | **None** |
| Email-to-Document Service Bus | ✅ Works | ✅ Works (now via shared helper) | **None** |
| Future Teams app | N/A (not built) | ✅ Auto-indexed when shipped | Teams design must route through BFF (documented above) |
| Future surfaces (mobile, Power Automate, third-party API consumers) | N/A | ✅ Auto-indexed when they call BFF | **None** — single seam absorbs them |

---

## 8.6. Documentation update plan (REQUIRED — part of this work)

This architectural change touches multiple docs. After Phase 3 deploy + UAT, update:

### Architecture docs

| File | Update |
|---|---|
| `docs/architecture/sdap-document-processing-architecture.md` | Add new section "Centralized Post-Upload Indexing" describing the helper, its position in the upload pipeline, and the canonical fail-protection patterns from §4. Replace any text describing the wizard-side trigger as authoritative. |
| `docs/architecture/AI-ARCHITECTURE.md` | Update the RAG indexing pipeline diagram: show `IPostUploadIndexingEnqueuer` as the unified entry point, with all upload surfaces converging on it. |
| `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` | Cross-reference: when documents are uploaded via any workspace widget, they index automatically. |
| `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` | Note: `@spaarke/ui-components` upload services (`EntityCreationService`, `MultiFileUploadService`) no longer need to call `triggerRagIndexing` after Phase 4 cleanup. |

### ADR updates

| File | Update |
|---|---|
| `docs/adr/ADR-007-spe-file-store.md` (SpeFileStore facade) | Append clarification: post-upload indexing is now mandated for every BFF upload endpoint via `IPostUploadIndexingEnqueuer`. SpeFileStore remains the SPE write boundary; the helper is the indexing trigger boundary. |
| `docs/adr/ADR-013-ai-architecture.md` (or wherever AI architecture ADR lives) | Note the centralization. |

### Constraints (`.claude/constraints/`)

| File | Update |
|---|---|
| `.claude/constraints/bff-extensions.md` | Add binding rule: every new BFF endpoint that writes to SPE MUST call `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync` after success. PR template + reviewer checklist updated to enforce. |

### Patterns (`.claude/patterns/`)

| File | Update |
|---|---|
| **NEW**: `.claude/patterns/ai/post-upload-indexing.md` | 25-line pointer file → links to canonical helper implementation + the 4 wired endpoints. Future BFF authors land here when adding new upload paths. |

### Guides (`docs/guides/`)

| File | Update |
|---|---|
| **NEW**: `docs/guides/POST-UPLOAD-INDEXING-OPERATOR-GUIDE.md` | Operator runbook: how to verify indexing fired (App Insights query), how to manually re-index (script from §4.11), how to disable via feature flag, how to inspect Service Bus DLQ if indexing fails persistently. |
| `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` | Update Section "What happens when a file is uploaded" with the centralized flow. |

### Project artifacts

| File | Update |
|---|---|
| `projects/spaarke-multi-container-multi-index-r1/README.md` | Add changelog entry; mark new graduation criterion: "5. Files uploaded via Create* wizards land in correct AI Search index". |
| `projects/spaarke-multi-container-multi-index-r1/notes/lessons-learned.md` | Append section: why Tier 3 scope missed this; how single-seam thinking would have caught it earlier; recommendation for future projects touching upload code paths. |
| `projects/sdap-teams-app/design.md` | Add binding rule (per §8.5.3): Teams Message Action MUST route through BFF upload endpoints. |

**Doc work is non-optional** — it ships in the same PR as the code. Reviewers reject PRs that change the architecture without updating the docs.

---

## 9. Acceptance criteria

- [ ] Build clean: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors
- [ ] Tests: all new unit tests pass; existing tests unchanged (NFR-02)
- [ ] BFF publish-size delta: < 1 MB (helper is ~150 lines; well under R1 NFR-01 ceiling 60 MB)
- [ ] Hash-verify + `/healthz` 200 after deploy
- [ ] UAT Section 5.3 all 6 checks pass
- [ ] App Insights shows enqueue log line for every Matter/Project/WorkAssignment/Event wizard upload
- [ ] AI Search `spaarke-file-index` contains chunks for the operator's test file
- [ ] No regression in DocumentUploadWizard, Office Add-in, or Email-to-Document paths

---

## 10. References

- Tier 3 indexer routing design: [`indexer-routing-fix-design.md`](./indexer-routing-fix-design.md)
- DI fix restart point (BFF startup crash): [`handoffs/RESTART-bff-down-DI-fix-applied.md`](./handoffs/RESTART-bff-down-DI-fix-applied.md)
- Canonical enqueue pattern: [`UploadFinalizationWorker.cs:1280-1330`](../../src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs#L1280-L1330)
- Job submission contract: [`JobSubmissionService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs)
- RAG payload + handler: [`RagIndexingJobHandler.cs`](../../src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs)
- ADR-007 (SpeFileStore facade): [`docs/adr/ADR-007-spe-file-store.md`](../../docs/adr/ADR-007-spe-file-store.md)
- ADR-013 (AI architecture): [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md)
- ADR-030 (Null-Object kill-switch — relevant to DI lifetime decision): [`.claude/adr/ADR-030-bff-nullobject-kill-switch.md`](../../.claude/adr/ADR-030-bff-nullobject-kill-switch.md)
