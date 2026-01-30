# Office Workers Implementation Plan

> **Created**: 2026-01-26
> **Updated**: 2026-01-27
> **Status**: Active Implementation - Workers Running but Stubs
> **Purpose**: Complete the Office add-in email save → document processing pipeline

---

## Executive Summary

The Office add-in save flow infrastructure is complete and deployed. All Service Bus queues exist, all workers are registered and listening. However, **ProfileSummaryWorker and IndexingWorkerHostedService are stub implementations** that skip actual AI processing.

**Current Status**:
- ✅ Service Bus queues created in Azure
- ✅ ServiceBusClient registered in DI (deployed 2026-01-27)
- ✅ All workers listening to their queues
- ✅ Email body fetching added to SaveView
- ⚠️ ProfileSummaryWorker is a stub (skips AI profile generation)
- ⚠️ IndexingWorkerHostedService is a stub (skips RAG indexing)

**Current Blockers**: Need to integrate existing AI services into stub workers.

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

## Current State Assessment (Updated 2026-01-27)

### ✅ What's Working

| Component | Status | Notes |
|-----------|--------|-------|
| Email Save Flow | ✅ Working | UI, validation, job creation |
| Document Creation | ✅ Working | Dataverse record created |
| SPE File Upload | ✅ Working | .eml file with body + attachments |
| Service Bus Connectivity | ✅ Working | All workers connected to queues |
| UploadFinalizationWorker | ✅ Working | Creates records, queues downstream jobs |
| Job Status Updates | ✅ Working | Redis + SSE working |
| Job Completion | ✅ Working | UI receives completion status |

### ⚠️ What's NOT Working (Stub Implementations)

| Component | Issue | Impact |
|-----------|-------|--------|
| **ProfileSummaryWorker** | Stub implementation (line 150-155) | No AI summary, keywords, or metadata |
| **IndexingWorkerHostedService** | Stub implementation (line 129-133) | Documents not indexed in Azure AI Search |

### Code Status

| Component | File Exists | Registered in DI | Functional | Notes |
|-----------|-------------|------------------|------------|-------|
| UploadFinalizationWorker | ✅ | ✅ | ✅ | Fully implemented |
| IndexingWorkerHostedService | ✅ | ✅ | ⚠️ Stub only | Needs FileIndexingService integration |
| ProfileSummaryWorker | ✅ | ✅ | ⚠️ Stub only | Needs Playbook/Analysis integration |
| OfficeWorkersModule | ✅ | ✅ | ✅ | All workers registered |
| ServiceBusClient | ✅ | ✅ | ✅ | Added via AddOfficeServiceBus() |
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

## Remaining Work Items

### 1. Integrate RAG Indexing (IndexingWorkerHostedService)

**File**: `src/server/api/Sprk.Bff.Api/Workers/Office/IndexingWorkerHostedService.cs`

**Current Code** (lines 129-133):
```csharp
// TODO: Integrate with actual IndexingWorker.ProcessAsync() when ready
// For now, mark job as complete immediately (indexing is optional per spec)
_logger.LogInformation(
    "Indexing skipped for job {JobId} (stub implementation), marking complete",
    message.JobId);
```

**Required Changes**:
1. Inject `IFileIndexingService` into constructor
2. Replace stub with call to `IndexFileAppOnlyAsync()`
3. Handle indexing errors gracefully (indexing is optional per spec)
4. Update job status to "Indexed" stage after success

**Existing Service to Use**:
- `IFileIndexingService.IndexFileAppOnlyAsync()` - Already exists and registered in DI
- Used by existing `ScheduledRagIndexingService` - proven pattern

### 2. Integrate AI Profile Generation (ProfileSummaryWorker)

**File**: `src/server/api/Sprk.Bff.Api/Workers/Office/ProfileSummaryWorker.cs`

**Current Code** (lines 150-155):
```csharp
// Step 4: TODO - Generate AI profile summary using existing AI services
// For now, this is a stub that skips actual AI processing
_logger.LogInformation(
    "AI profile generation skipped (not yet implemented) for job {JobId}, document {DocumentId}",
    message.JobId,
    payload.DocumentId);
```

**Required Changes**:
1. Determine correct AI service to use:
   - Option A: `IPlaybookOrchestrationService` (if email profile playbook exists)
   - Option B: `IAppOnlyAnalysisService.AnalyzeDocumentAsync()` (existing document analysis)
2. Inject selected service into constructor
3. Call AI service with document details
4. Update Dataverse Document record with AI-generated metadata:
   - `sprk_keywords`
   - `sprk_summary`
   - `sprk_documenttype` (if classified)
   - Other AI-generated fields
5. Handle errors gracefully (AI processing is optional)

**Decision Required**: Which AI service should process email documents?
- Email-specific playbook? (needs investigation)
- General document analysis? (already exists)

### 3. Test End-to-End Flow

**For Outlook**:
1. Save email from Outlook Web App
2. Verify Document created with all metadata
3. Verify AI summary populated (if enabled)
4. Verify document indexed in Azure AI Search (if enabled)
5. Verify attachments extracted and saved

**For Word**:
1. Test Word add-in save flow
2. Verify Document + DOCX file saved correctly
3. Verify same AI processing applies to Word documents

### 4. Verify Word Add-in Integration

**Status**: Unknown - needs testing

**Files to Check**:
- `src/client/office-addins/word/` - Word adapter implementation
- `SaveView.tsx` - Should work for both Outlook and Word
- `useSaveFlow.ts` - Content type detection for Word documents

---

## Implementation Phases

### ✅ Phase 1: Basic Save Flow (COMPLETE)

**Goal**: Email save → Document created in Dataverse

**Status**: ✅ **COMPLETE** (2026-01-27)

**Completed**:
- ✅ Service Bus queues created (`office-upload-finalization`, `office-profile`, `office-indexing`)
- ✅ ServiceBusClient registered in Program.cs via `AddOfficeServiceBus()`
- ✅ All workers registered in OfficeWorkersModule
- ✅ Email body fetching added to SaveView
- ✅ BFF API deployed with workers running
- ✅ End-to-end save flow working
- ✅ Document record created in Dataverse
- ✅ EmailArtifact record created
- ✅ Job status updates working (Redis + SSE)

**Test Results**:
- ✅ Email saves without error
- ✅ Document record appears in Dataverse with correct association
- ✅ EmailArtifact record created
- ✅ .eml file includes body and attachments
- ✅ Job completes (though AI stages are stubs)

---

### 🚧 Phase 2: RAG Indexing Integration (IN PROGRESS)

**Goal**: Documents indexed in Azure AI Search after save

**Status**: ⚠️ Worker exists but is a stub

**Completed**:
- ✅ `office-indexing` queue exists
- ✅ IndexingWorkerHostedService registered and running
- ✅ IFileIndexingService exists and is registered

**Remaining Work**:
1. Replace stub in `IndexingWorkerHostedService.cs` (line 129-133)
2. Inject `IFileIndexingService` into constructor
3. Call `IndexFileAppOnlyAsync()` with document details
4. Update Document record with indexing metadata
5. Test: Save with RagIndex=true → Document indexed
6. Verify duplicate handling (idempotency)

**Files to Modify**:
- `Workers/Office/IndexingWorkerHostedService.cs` - Replace stub with real implementation

**Existing Components to Use**:
- `IFileIndexingService.IndexFileAppOnlyAsync()` - Already exists
- Pattern reference: `ScheduledRagIndexingService.cs` (existing RAG indexing pattern)

**Test Criteria**:
- Document appears in Azure AI Search index
- Vector embeddings generated
- Job status shows "Indexed" stage
- Duplicate saves don't re-index (idempotency)

---

### 🚧 Phase 3: AI Profile Integration (IN PROGRESS)

**Goal**: Documents get AI-generated profile/summary after save

**Status**: ⚠️ Worker exists but is a stub

**Completed**:
- ✅ `office-profile` queue exists
- ✅ ProfileSummaryWorker registered and running

**Remaining Work**:
1. **Decision**: Determine which AI service to use:
   - Option A: Email-specific playbook (if exists)
   - Option B: `IAppOnlyAnalysisService.AnalyzeDocumentAsync()` (general document analysis)
2. Replace stub in `ProfileSummaryWorker.cs` (line 150-155)
3. Inject selected AI service into constructor
4. Call AI service with document details
5. Update Document record with AI metadata:
   - `sprk_keywords`
   - `sprk_summary`
   - `sprk_documenttype`
   - Other AI-generated fields
6. Test: Save with ProfileSummary=true → Document gets AI metadata
7. Verify graceful error handling

**Files to Modify**:
- `Workers/Office/ProfileSummaryWorker.cs` - Replace stub with real implementation

**Existing Components to Use**:
- `IAppOnlyAnalysisService.AnalyzeDocumentAsync()` - Already exists
- OR `IPlaybookOrchestrationService` - If email playbook exists
- Pattern reference: `ProfileSummaryJobHandler.cs` (existing profile generation pattern)

**Test Criteria**:
- Document metadata updated with AI-generated summary
- Keywords extracted and saved
- Job status shows "ProfileSummary" stage
- Errors in profile stage don't fail entire job

---

### 🚧 Phase 4: Word Add-in Support (VERIFICATION NEEDED)

**Goal**: Word documents save → Document created (same as Outlook)

**Status**: ⚠️ Code exists, needs testing

**Investigation Required**:
1. Test Word add-in save flow
2. Verify WordAdapter implementation
3. Verify DOCX file upload works
4. Verify AI processing applies to Word documents

**Files to Check**:
- `src/client/office-addins/word/` - Word adapter
- `src/client/office-addins/shared/adapters/WordAdapter.ts` - Implementation
- `useSaveFlow.ts` - Word document handling

**Test Criteria**:
- Word document saves from Word Online
- Document record created in Dataverse
- DOCX file uploaded to SPE
- Same AI processing pipeline applies

---

### Phase 5: Cleanup and Documentation

**Status**: ⏳ Pending completion of Phases 2-4

**Steps**:
1. Delete unused `office-jobs` queue (Task 069c)
2. Update TASK-INDEX.md with final status
3. Document deployment procedure for customer
4. Create Azure resource deployment script/checklist

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
