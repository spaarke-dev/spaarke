# Office Workers Implementation Plan

> **Created**: 2026-01-26
> **Status**: Active Implementation
> **Purpose**: Complete the Office add-in email save → document processing pipeline

---

## Executive Summary

The Office add-in save flow is partially implemented. The API endpoint and initial SPE upload work, but the background worker pipeline is incomplete. This document provides the roadmap to complete the full end-to-end flow using **existing components** wherever possible.

**Current Blocker**: Service Bus queue `office-upload-finalization` does not exist in Azure.

---

## Architecture Overview

### Intended Processing Pipeline

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           USER ACTION                                        │
│  Outlook Add-in → "Save to Spaarke" → Select entity association             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  POST /office/save (OfficeEndpoints.cs)                                      │
│  - Validates request                                                         │
│  - Creates ProcessingJob in Dataverse                                        │
│  - Uploads .eml to SPE via SpeFileStore                                      │
│  - Queues message to Service Bus                                             │
│  - Returns 202 Accepted + jobId                                              │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                    Queue: office-upload-finalization
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  UploadFinalizationWorker (EXISTING - fully implemented)                     │
│  - Creates Document record in Dataverse                                      │
│  - Creates EmailArtifact record                                              │
│  - Extracts attachments → creates child Documents                            │
│  - Updates job status via IOfficeJobStatusService                            │
│  - Queues next stage (Profile or Indexing)                                   │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
              ┌─────────────────────┴─────────────────────┐
              │                                           │
    Queue: office-profile                      Queue: office-indexing
    (if ProfileSummary enabled)                (if RagIndex enabled)
              │                                           │
              ▼                                           ▼
┌─────────────────────────────┐         ┌─────────────────────────────────────┐
│  ProfileSummaryWorker       │         │  IndexingWorker (EXISTING - code    │
│  (TO BE CREATED - but       │         │  exists, NOT registered in DI)      │
│  should REUSE existing      │         │  - Uses IFileIndexingService        │
│  ProfileSummaryJobHandler)  │         │  - Chunks → Embeds → Indexes        │
│                             │         │  - Updates job status               │
└─────────────────────────────┘         └─────────────────────────────────────┘
              │                                           │
              └─────────────────────┬─────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  Job Complete → SSE notifies client → User sees "Document Saved"             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Critical: Existing Components to REUSE

### DO NOT CREATE NEW VERSIONS OF THESE

| Component | Location | Purpose | Reuse Strategy |
|-----------|----------|---------|----------------|
| **IFileIndexingService** | `Services/Ai/IFileIndexingService.cs` | RAG indexing pipeline | IndexingWorker already uses `IndexFileAppOnlyAsync` |
| **FileIndexingService** | `Services/Ai/FileIndexingService.cs` | Implementation | Already registered in DI |
| **ProfileSummaryJobHandler** | `Services/Jobs/Handlers/ProfileSummaryJobHandler.cs` | AI document profiling | Uses `IAppOnlyAnalysisService` - consider routing profile jobs through this |
| **IAppOnlyAnalysisService** | `Services/Ai/IAppOnlyAnalysisService.cs` | Document Intelligence + OpenAI | Used by ProfileSummaryJobHandler |
| **IOfficeJobStatusService** | `Workers/Office/IOfficeJobStatusService.cs` | SSE job status updates | Already exists, use for status broadcasts |
| **OfficeJobStatusService** | `Workers/Office/OfficeJobStatusService.cs` | Implementation | Already registered |
| **IDataverseService** | `Spaarke.Dataverse/IDataverseService.cs` | Dataverse CRUD | Already used by UploadFinalizationWorker |
| **SpeFileStore** | `Infrastructure/Graph/SpeFileStore.cs` | SPE operations | Already used by UploadFinalizationWorker |
| **AttachmentFilterService** | `Services/Email/AttachmentFilterService.cs` | Filter noise attachments | Already used by UploadFinalizationWorker |
| **IEmailToEmlConverter** | `Services/Email/IEmailToEmlConverter.cs` | .eml parsing | Already used by UploadFinalizationWorker |

---

## Current State Assessment

### Code Status

| Component | File Exists | Registered in DI | Functional | Notes |
|-----------|-------------|------------------|------------|-------|
| UploadFinalizationWorker | ✅ | ✅ | ❌ (queue missing) | Fully implemented |
| IndexingWorker | ✅ | ❌ | ❌ | Code exists, not registered |
| ProfileSummaryWorker | ❌ | ❌ | ❌ | Not created |
| OfficeWorkersModule | ✅ | ✅ | ✅ | Only registers UploadFinalizationWorker |
| IOfficeJobHandler | ✅ | ✅ | ✅ | Interface exists |
| IOfficeJobStatusService | ✅ | ✅ | ✅ | Implementation exists |

### Azure Infrastructure Status

> **Updated 2026-01-26**: Queues created in `SharePointEmbedded` resource group (not `spe-infrastructure-westus2`).

| Resource | Exists | Used By | Action Required |
|----------|--------|---------|-----------------|
| `office-upload-finalization` queue | ✅ | UploadFinalizationWorker | CREATED (Task 067) |
| `office-profile` queue | ✅ | ProfileSummaryWorker (planned) | CREATED (Task 067) |
| `office-indexing` queue | ✅ | IndexingWorker | CREATED (Task 067) |
| `office-jobs` queue | ✅ | **NOTHING** (unused) | DELETE (Task 069c) |
| `sdap-jobs` queue | ✅ | ServiceBusJobProcessor | Keep (existing system) |
| `document-events` queue | ✅ | Unknown | Keep (investigate) |

**Service Bus Details**:
- Namespace: `spaarke-servicebus-dev`
- Resource Group: `SharePointEmbedded` (not `spe-infrastructure-westus2`)

---

## Design Decision: Profile Worker Strategy

### Two Options

**Option A: New ProfileSummaryWorker (Office Worker Pattern)**
- Create new `ProfileSummaryWorker.cs` implementing `IOfficeJobHandler`
- Listens on `office-profile` queue
- Calls `IAppOnlyAnalysisService.AnalyzeDocumentAsync`
- Consistent with UploadFinalizationWorker and IndexingWorker patterns

**Option B: Reuse ProfileSummaryJobHandler (Generic Job Pattern)**
- Route profile jobs through existing `sdap-jobs` queue
- ProfileSummaryJobHandler already exists and works
- Less code to write, but mixes two job patterns

### Recommendation: Option A (New ProfileSummaryWorker)

Reasoning:
- Keeps Office pipeline self-contained in `Workers/Office/`
- Consistent queue naming (`office-*`)
- ProfileSummaryJobHandler uses different payload format (JobContract vs OfficeJobMessage)
- Easier to trace/debug the Office flow

However, ProfileSummaryWorker should **delegate to IAppOnlyAnalysisService** (not duplicate logic):

```csharp
// ProfileSummaryWorker.cs - REUSES existing analysis service
public async Task<JobOutcome> ProcessAsync(OfficeJobMessage message, CancellationToken ct)
{
    // 1. Check if profile option enabled
    // 2. Call IAppOnlyAnalysisService.AnalyzeDocumentAsync (EXISTING)
    // 3. Update job status via IOfficeJobStatusService (EXISTING)
    // 4. Queue to office-indexing if needed
}
```

---

## Implementation Phases

### Phase 1: Basic Save Flow (Critical Path)

**Goal**: Email save → Document created in Dataverse

**Steps**:
1. Create Service Bus queue: `office-upload-finalization`
2. Deploy BFF API with updated configuration
3. Test end-to-end: Outlook → Save → SPE upload → Worker → Document created
4. Verify job status updates reach SSE client

**Files Involved**:
- No code changes required (UploadFinalizationWorker already complete)
- Azure: Create queue via portal or CLI

**Test Criteria**:
- Email saves without error
- Document record appears in Dataverse with correct association
- EmailArtifact record created
- Attachments extracted as child Documents (if present)
- Job status shows "Completed"

---

### Phase 2: RAG Indexing Stage

**Goal**: Documents indexed in Azure AI Search after save

**Steps**:
1. Create Service Bus queue: `office-indexing`
2. Register IndexingWorker in OfficeWorkersModule.cs
3. Deploy updated API
4. Test: Save with RagIndex=true → Document indexed

**Files to Modify**:
- `Workers/Office/OfficeWorkersModule.cs` - Add IndexingWorker registration

**Code Change**:
```csharp
// OfficeWorkersModule.cs - ADD this registration
services.AddSingleton<IOfficeJobHandler, IndexingWorker>();
services.AddHostedService<IndexingWorker>(sp =>
{
    var handlers = sp.GetServices<IOfficeJobHandler>();
    return handlers.OfType<IndexingWorker>().First();
});
```

**Existing Components Used**:
- `IFileIndexingService.IndexFileAppOnlyAsync` (already called by IndexingWorker)
- `IIdempotencyService` (already used)
- `RagTelemetry` (already used)

**Test Criteria**:
- Document appears in Azure AI Search index
- Job status shows "Indexed"
- Duplicate saves don't re-index (idempotency)

---

### Phase 3: AI Profile Stage

**Goal**: Documents get AI-generated profile/summary after save

**Steps**:
1. Create Service Bus queue: `office-profile`
2. Create ProfileSummaryWorker.cs (delegates to IAppOnlyAnalysisService)
3. Register ProfileSummaryWorker in OfficeWorkersModule.cs
4. Update UploadFinalizationWorker to queue to profile stage
5. Deploy and test

**Files to Create**:
- `Workers/Office/ProfileSummaryWorker.cs`

**Files to Modify**:
- `Workers/Office/OfficeWorkersModule.cs` - Add ProfileSummaryWorker registration

**Existing Components Used**:
- `IAppOnlyAnalysisService.AnalyzeDocumentAsync` (DO NOT DUPLICATE)
- `IOfficeJobStatusService` (for status updates)
- `IIdempotencyService` (for duplicate prevention)

**Test Criteria**:
- Document metadata updated with AI-generated summary
- Job status shows "Profiled"
- Errors in profile stage don't fail entire job

---

### Phase 4: Cleanup

**Steps**:
1. Delete unused `office-jobs` queue
2. Update TASK-INDEX.md to reflect true completion status
3. Document deployment in notes/deployment-log.md

---

## File Reference Index

### Existing Worker Files

| File | Purpose | Status |
|------|---------|--------|
| [UploadFinalizationWorker.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs) | Stage 1: File upload + record creation | ✅ Complete |
| [IndexingWorker.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/IndexingWorker.cs) | Stage 3: RAG indexing | ✅ Code complete, needs DI registration |
| [OfficeWorkersModule.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/OfficeWorkersModule.cs) | DI registration | ⚠️ Only registers UploadFinalizationWorker |
| [IOfficeJobHandler.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/IOfficeJobHandler.cs) | Handler interface | ✅ Complete |
| [IOfficeJobStatusService.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/IOfficeJobStatusService.cs) | Status broadcast interface | ✅ Complete |
| [OfficeJobStatusService.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/OfficeJobStatusService.cs) | Status broadcast implementation | ✅ Complete |
| [Messages/OfficeJobMessage.cs](../../src/server/api/Sprk.Bff.Api/Workers/Office/Messages/) | Message DTOs | ✅ Complete |

### Existing AI Services to REUSE

| File | Purpose | Do Not Duplicate |
|------|---------|------------------|
| [IFileIndexingService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IFileIndexingService.cs) | RAG indexing interface | IndexFileAppOnlyAsync |
| [FileIndexingService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs) | RAG indexing implementation | Already registered |
| [IAppOnlyAnalysisService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAppOnlyAnalysisService.cs) | Document analysis interface | AnalyzeDocumentAsync |
| [AppOnlyAnalysisService.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/AppOnlyAnalysisService.cs) | Document analysis implementation | Already registered |
| [ProfileSummaryJobHandler.cs](../../src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/ProfileSummaryJobHandler.cs) | Reference pattern | Shows how to use IAppOnlyAnalysisService |

### API Endpoint Files

| File | Purpose |
|------|---------|
| [OfficeEndpoints.cs](../../src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs) | POST /office/save entry point |
| [OfficeService.cs](../../src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs) | SaveAsync implementation |

### Configuration Files

| File | Purpose |
|------|---------|
| [appsettings.json](../../src/server/api/Sprk.Bff.Api/appsettings.json) | Service Bus connection string |
| [ServiceBusOptions.cs](../../src/server/api/Sprk.Bff.Api/Configuration/ServiceBusOptions.cs) | Configuration binding |

---

## Azure Resources

### Service Bus Namespace

| Property | Value |
|----------|-------|
| Name | `spaarke-servicebus-dev` |
| Resource Group | `spe-infrastructure-westus2` |
| Connection String Secret | `ServiceBus:ConnectionString` in Key Vault |

### Queues to Create

```bash
# Create queues using Azure CLI
az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name spaarke-servicebus-dev \
  --name office-upload-finalization \
  --max-size 1024 \
  --default-message-time-to-live P14D

az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name spaarke-servicebus-dev \
  --name office-profile \
  --max-size 1024 \
  --default-message-time-to-live P14D

az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name spaarke-servicebus-dev \
  --name office-indexing \
  --max-size 1024 \
  --default-message-time-to-live P14D

# Delete unused queue
az servicebus queue delete \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name spaarke-servicebus-dev \
  --name office-jobs
```

---

## Spec References

### From [spec.md](../spec.md)

**Section: Background Processing** (lines 110-115):
- Upload finalization worker
- Profile summary worker
- Indexing worker
- Deep analysis worker (optional - future)

**Section: NFR-01** (line 167):
- API returns jobId within 3 seconds; heavy processing async

**Section: NFR-04** (line 170):
- Job status updates: SSE primary; polling fallback at 3 seconds

---

## ADR References

| ADR | Relevance |
|-----|-----------|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api-and-workers.md) | BackgroundService pattern - no Azure Functions |
| [ADR-004](../../.claude/adr/ADR-004-async-job-contract.md) | Job contract and idempotency patterns |
| [ADR-007](../../.claude/adr/ADR-007-spefilestore-facade.md) | SpeFileStore usage for SPE operations |

---

## Task File References

| Task | File | Status (Actual) |
|------|------|-----------------|
| 060 | [060-create-worker-project-structure.poml](../tasks/060-create-worker-project-structure.poml) | ✅ Actually complete |
| 061 | [061-implement-upload-worker.poml](../tasks/061-implement-upload-worker.poml) | ✅ Actually complete |
| 062 | [062-implement-profile-worker.poml](../tasks/062-implement-profile-worker.poml) | ❌ NOT complete - code missing |
| 063 | [063-implement-indexing-worker.poml](../tasks/063-implement-indexing-worker.poml) | ⚠️ Code exists but not registered |
| 064 | [064-implement-job-status-service.poml](../tasks/064-implement-job-status-service.poml) | ✅ Actually complete |
| 066 | [066-deploy-workers.poml](../tasks/066-deploy-workers.poml) | ❌ Queues not created |

---

## Success Criteria

### Phase 1 Complete When:
- [ ] `office-upload-finalization` queue exists
- [ ] Email save from Outlook creates Document in Dataverse
- [ ] EmailArtifact record created with correct metadata
- [ ] Attachments extracted as child Documents
- [ ] Job status reaches "Completed"
- [ ] SSE client receives status updates

### Phase 2 Complete When:
- [ ] `office-indexing` queue exists
- [ ] IndexingWorker registered in DI
- [ ] Documents with RagIndex=true are indexed in AI Search
- [ ] Job status shows "Indexed" stage complete

### Phase 3 Complete When:
- [ ] `office-profile` queue exists
- [ ] ProfileSummaryWorker created and registered
- [ ] Documents with ProfileSummary=true get AI metadata
- [ ] Job status shows "Profiled" stage complete

### Full Pipeline Complete When:
- [ ] All three phases work end-to-end
- [ ] Job status SSE shows all stage transitions
- [ ] Error handling works (retries, DLQ)
- [ ] Unused `office-jobs` queue deleted

---

## Anti-Patterns to Avoid

1. **DO NOT** create a new file indexing service - use `IFileIndexingService`
2. **DO NOT** create a new analysis service - use `IAppOnlyAnalysisService`
3. **DO NOT** duplicate idempotency logic - use `IIdempotencyService`
4. **DO NOT** create new telemetry classes - use existing `RagTelemetry`, `DocumentTelemetry`
5. **DO NOT** create new Dataverse access - use `IDataverseService`
6. **DO NOT** bypass `SpeFileStore` for SPE operations
7. **DO NOT** create new SSE mechanisms - use `IOfficeJobStatusService`

---

## Next Steps

**Immediate Action**: Create `office-upload-finalization` queue to unblock Phase 1 testing.

```bash
az servicebus queue create \
  --resource-group spe-infrastructure-westus2 \
  --namespace-name spaarke-servicebus-dev \
  --name office-upload-finalization \
  --max-size 1024 \
  --default-message-time-to-live P14D
```

Then test the basic save flow before proceeding to Phase 2.

---

*Document created during debugging session. Update as implementation progresses.*
