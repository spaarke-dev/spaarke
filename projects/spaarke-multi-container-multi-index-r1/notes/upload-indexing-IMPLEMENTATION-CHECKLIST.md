# Upload-Indexing Centralization — Implementation Checklist

> **Created**: 2026-06-08
> **Spec**: [`upload-indexing-centralization-design.md`](./upload-indexing-centralization-design.md) (read this first; it has all rationale + edge cases)
> **Scope extension to**: `spaarke-multi-container-multi-index-r1` (Tier 3 indexer routing was incomplete — wizard-side only; this fix centralizes server-side)
> **Branch**: `work/spaarke-multi-container-multi-index-r1`

This is the **running tracker** — update as work completes. The design doc is the spec; this is the punch list.

---

## Pre-work (done before this checklist)

- [x] BFF DI fix deployed (commit `2c9b9e73`, BFF healthy, CORS works)
- [x] CreateMatterWizard rebuilt + redeployed (sprk_creatematterwizard 1047 KB, 2026-06-08 09:43) — Issue 1 (stale bundle) fixed
- [x] Design doc complete (4 phases, 11 fail-protections, 5 endpoints incl. SprkChat persist gap, External SPA + Office add-in analysis)

---

## Phase 1 — Helper + DI registration + tests (no behavior change)

### Code
- [ ] Create `src/server/api/Sprk.Bff.Api/Services/Ai/IPostUploadIndexingEnqueuer.cs` (interface + `PostUploadIndexingRequest` record)
- [ ] Create `src/server/api/Sprk.Bff.Api/Services/Ai/PostUploadIndexingEnqueuer.cs` (implementation extracting canonical pattern from `UploadFinalizationWorker.EnqueueRagIndexingAsync` lines 1280-1330)
  - [ ] Feature-flag check (`Indexing:PostUploadEnqueueEnabled`, default true)
  - [ ] Empty-file skip
  - [ ] Missing-tenant guard (ERROR log, no enqueue)
  - [ ] Content-type skip-list (§4.3) — but ensure `.msg/.eml` NOT skipped (per §8.5.2 caveat)
  - [ ] Size-cap skip (default 200 MB, configurable)
  - [ ] Null `SearchIndexName` still enqueues (handler fallback)
  - [ ] Idempotency key `rag-index-{driveId}-{itemId}`
  - [ ] Non-fatal try/catch around `SubmitJobAsync`
  - [ ] Observability log line (§4.9)
- [ ] DI registration in `Infrastructure/DI/AnalysisServicesModule.cs` at TOP of `AddAnalysisServicesModule` (above conditionals — same lesson as `ISearchIndexNameResolver`). Lifetime: scoped.

### Config
- [ ] Add `Indexing:PostUploadEnqueueEnabled = true` to `appsettings.json` + dev/prod overrides

### Tests (`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PostUploadIndexingEnqueuerTests.cs`)
- [ ] `HappyPath_SubmitsJob`
- [ ] `EmptyFile_SkipsEnqueue`
- [ ] `SkippableContentType_SkipsEnqueue` (test .zip, .exe; verify .msg/.pdf NOT skipped)
- [ ] `LargeFile_SkipsEnqueue`
- [ ] `NullSearchIndexName_StillEnqueues`
- [ ] `MissingTenantId_SkipsAndLogsError`
- [ ] `FeatureFlagOff_SkipsEnqueue`
- [ ] `SubmitThrows_LogsWarningDoesNotPropagate`

### Verification
- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors
- [ ] `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → all new tests pass, no regression in existing 6140
- [ ] **Commit + push**: `feat(multi-container-multi-index-r1): Phase 1 — IPostUploadIndexingEnqueuer + DI + tests (no behavior change)`

---

## Phase 2 — Refactor existing enqueue sites to use helper

Replace inline `RagIndexingJobPayload` + `SubmitJobAsync` with `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync` calls. **Pure deduplication — zero behavior change.**

### Sites to refactor (5 files; preserve existing behavior exactly)
- [ ] `Workers/Office/UploadFinalizationWorker.cs` lines 1280-1330 — `EnqueueRagIndexingAsync` → helper call
- [ ] `Services/Communication/IncomingCommunicationProcessor.cs` line 753 — Email-to-Document enqueue → helper call
- [ ] `Services/Ai/AnalysisResultPersistence.cs` line 257 — AI workflow enqueue → helper call
- [ ] `Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs` line 163 — AI node enqueue → helper call
- [ ] `Api/Ai/KnowledgeBaseEndpoints.cs` line 385 — manual KB ingest enqueue → helper call
- [ ] `Api/Ai/RagEndpoints.cs` line 833 — `/api/ai/rag/index-file` enqueue → helper call (DocumentUploadWizard still calls this endpoint; helper preserves identical behavior)

### Regression tests
- [ ] Integration test: Office Add-in upload still enqueues (mock `IJobSubmissionService` records 1 call per upload)
- [ ] Integration test: Email-to-Document still enqueues
- [ ] Integration test: `/api/ai/rag/index-file` still enqueues (DocumentUploadWizard path)
- [ ] All existing test suite still passes (6140 baseline)

### Verification
- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors
- [ ] `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → green
- [ ] **Commit + push**: `refactor(multi-container-multi-index-r1): Phase 2 — collapse 6 inline enqueue sites onto IPostUploadIndexingEnqueuer`

---

## Phase 3 — Wire helper into 5 missing endpoints (the real fix)

This is the production behavior change. Files in SPE now reach the tenant AI Search index.

### Endpoints to wire
- [ ] `Api/ContainerItemEndpoints.cs` `UploadContainerItem` (`/api/spe/containers/{id}/items/upload`) — after `_speFileStore.UploadSmallAsync` success
- [ ] `Api/OBOEndpoints.cs` `/api/obo/upload-session/chunk` final-chunk path — after success returning item ID
- [ ] `Api/UploadEndpoints.cs` `/api/containers/{containerId}/upload` — after `UploadSmallAsync` success
- [ ] `Api/UploadEndpoints.cs` `/api/upload-session/chunk` final-chunk path — after success returning item ID
- [ ] `Api/Ai/ChatDocumentEndpoints.cs` `PersistDocumentAsync` (`/api/ai/chat/sessions/{id}/documents/{docId}/persist`) — after `_speFileStore.UploadSmallAsUserAsync` success (line ~716)

### Each endpoint wiring includes
- Inject `IPostUploadIndexingEnqueuer` (scoped)
- Build `PostUploadIndexingRequest` from upload result (DriveId, ItemId, FileName, FileSizeBytes, ContentType, DocumentId if available, ParentEntity if available, Source label distinguishing the path, CorrelationId)
- `await enqueuer.EnqueueIfApplicableAsync(request, ct)` inside a try/catch (defense in depth — helper is already non-fatal, but belt-and-braces)
- Source labels: `"SpeContainerUpload"`, `"OboUploadSession"`, `"DirectContainerUpload"`, `"DirectUploadSession"`, `"ChatPersist"`

### Tests
- [ ] Per-endpoint integration test: success path → mock `IJobSubmissionService` shows 1 call with correct payload
- [ ] Per-endpoint integration test: skip path (empty file) → no enqueue
- [ ] Per-endpoint integration test: enqueue failure → upload still returns 200/201

### Build + deploy
- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors
- [ ] `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → all tests pass
- [ ] Verify publish size delta < 1 MB (helper is ~150 LOC; safely under R1 NFR-01 ceiling 60 MB)
- [ ] **Commit + push**: `feat(multi-container-multi-index-r1): Phase 3 — wire IPostUploadIndexingEnqueuer into 5 BFF upload endpoints`
- [ ] **Deploy BFF**: `pwsh -ExecutionPolicy Bypass -File "C:\code_files\spaarke-wt-spaarke-multi-container-multi-index-r1\scripts\Deploy-BffApi.ps1"` (must use absolute path)
  - [ ] Hash-verify: all 4 critical files match
  - [ ] `/healthz` returns 200 (allow up to 120s on Linux cold-start per bff-deploy skill)

### UAT (run after deploy)
- [ ] Hard-refresh browser; upload file via **Create New Matter** under "Spaarke" BU
  - [ ] SPE: file appears in container ✅
  - [ ] Dataverse: `sprk_document` with `sprk_searchindexname = spaarke-file-index` ✅
  - [ ] App Insights: `[PostUploadIndexingEnqueuer] Enqueued RAG indexing job ...` log line present ✅
  - [ ] App Insights: `Indexing batch: SearchIndexName=spaarke-file-index` (downstream handler) ✅
  - [ ] AI Search: chunks present in `spaarke-file-index` for the file's documentId ✅
- [ ] Repeat for **Create New Project / WorkAssignment / Event** wizards (regression-guard the shared code path)
- [ ] **DocumentUploadWizard** — must still work unchanged (uses `/api/ai/rag/index-file` route; helper preserves behavior)
- [ ] **Office Add-in** — regression guard; existing flow unaffected
- [ ] **SprkChat "Save to Spaarke"** — upload to chat session → click persist → verify chunks in `spaarke-file-index` (NEW — was broken before this fix)
- [ ] Negative: upload a `.zip` → skip log present, no chunks ✅
- [ ] Negative (optional, dev env only): simulate Service Bus auth failure → upload still 200, WARN log present, no crash

---

## Phase 4 — Decommission wizard-side trigger (cleanup)

After UAT confirms server-side path works.

- [ ] Delete `uploadOrchestrator.kickOffBackgroundTasks` in `src/solutions/DocumentUploadWizard/src/services/uploadOrchestrator.ts`
- [ ] Delete `triggerRagIndexing` function
- [ ] Delete `searchIndexName` plumbing (the wizard no longer needs to resolve it — server does)
- [ ] **Keep** `/api/ai/rag/index-file` BFF endpoint (operators use for manual re-trigger / DR per §4.11)
- [ ] Clean-rebuild shared lib + DocumentUploadWizard:
  - [ ] `cd C:\code_files\spaarke-wt-spaarke-multi-container-multi-index-r1\src\client\shared\Spaarke.UI.Components && npm run build`
  - [ ] `cd C:\code_files\spaarke-wt-spaarke-multi-container-multi-index-r1\src\client\shared\Spaarke.Auth && npm run build`
  - [ ] `rm -rf src/solutions/DocumentUploadWizard/dist src/solutions/DocumentUploadWizard/node_modules/.vite src/solutions/DocumentUploadWizard/.vite`
  - [ ] `cd C:\code_files\spaarke-wt-spaarke-multi-container-multi-index-r1\src\solutions\DocumentUploadWizard && npm run build`
- [ ] Verify post-build: `grep -c "triggerRagIndexing" src/solutions/DocumentUploadWizard/dist/index.html` → 0
- [ ] Deploy: `pwsh -ExecutionPolicy Bypass -File "C:\code_files\spaarke-wt-spaarke-multi-container-multi-index-r1\scripts\Deploy-WizardCodePages.ps1" -DataverseUrl "https://spaarkedev1.crm.dynamics.com"`
- [ ] UAT: DocumentUploadWizard still works (now via server-side path)
- [ ] **Commit + push**: `refactor(multi-container-multi-index-r1): Phase 4 — remove redundant wizard-side RAG trigger (server now handles centrally)`

---

## Documentation updates (Phase D — runs alongside Phase 3 commit)

Doc work is non-optional and ships in the same PR as code per §8.6.

### Architecture docs
- [ ] `docs/architecture/sdap-document-processing-architecture.md` — add "Centralized Post-Upload Indexing" section
- [ ] `docs/architecture/AI-ARCHITECTURE.md` — update RAG pipeline diagram (helper as unified entry)
- [ ] `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md` — cross-reference auto-indexing
- [ ] `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — note `EntityCreationService` no longer triggers indexing

### ADR updates
- [ ] `docs/adr/ADR-007-spe-file-store.md` — clarify post-upload indexing mandate
- [ ] `docs/adr/ADR-013-ai-architecture.md` — note centralization

### Constraints
- [ ] `.claude/constraints/bff-extensions.md` — binding rule: new BFF upload endpoints MUST call `IPostUploadIndexingEnqueuer`

### Patterns (new file)
- [ ] `.claude/patterns/ai/post-upload-indexing.md` (NEW) — 25-line pointer to canonical helper + 5 wired endpoints

### Operator guides
- [ ] `docs/guides/POST-UPLOAD-INDEXING-OPERATOR-GUIDE.md` (NEW) — verification + re-trigger + DLQ inspection
- [ ] `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` — update upload-flow section

### Project artifacts
- [ ] `projects/spaarke-multi-container-multi-index-r1/README.md` — changelog + new graduation criterion #5
- [ ] `projects/spaarke-multi-container-multi-index-r1/notes/lessons-learned.md` — append section (why Tier 3 scope missed this; single-seam thinking)
- [ ] `projects/sdap-teams-app/design.md` — add binding rule per §8.5.3

**11 files total.**

---

## Coordination + safety

- [ ] **r6 parallel project conflict check**: before final merge to master, run `git diff origin/master..HEAD -- src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` to identify conflict points (per [`RESTART-bff-down-DI-fix-applied.md`](./handoffs/RESTART-bff-down-DI-fix-applied.md))
- [ ] **Fast-stop available**: set `Indexing:PostUploadEnqueueEnabled = false` in App Service config + restart if Service Bus / AI Search outage requires emergency disable
- [ ] **Backout per phase**: each phase commit is independently revertable (see design §7)

---

## Resume-from-compaction protocol

If session compacts mid-work:

1. Read this file — checkboxes show exact state
2. Read [`upload-indexing-centralization-design.md`](./upload-indexing-centralization-design.md) for full spec + edge cases
3. Read [`current-task.md`](../current-task.md) for the immediate active phase/step
4. Resume at the first unchecked box

Key facts to anchor:
- Branch: `work/spaarke-multi-container-multi-index-r1`
- This is a scope extension to `spaarke-multi-container-multi-index-r1` (Tier 3 indexer routing fix proved incomplete)
- Matter wizard stale-bundle fix already deployed (commit context); the architectural fix is what's running now
- BFF healthy as of session start (DI fix `2c9b9e73`); deploys via `Deploy-BffApi.ps1` with absolute path
