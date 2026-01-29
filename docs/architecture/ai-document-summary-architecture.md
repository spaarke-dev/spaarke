# AI Document Summary Architecture

> **Last Updated**: January 28, 2026
> **Purpose**: Comprehensive architecture documentation for all Document creation processes with AI analysis pipelines

---

## Table of Contents

1. [File Upload via UniversalDocumentUpload PCF Control](#1-file-upload-via-universaldocumentupload-pcf-control)
2. [Email-to-Document Automation Process](#2-email-to-document-automation-process)
3. [Outlook Add-in Save Process](#3-outlook-add-in-save-process)
4. [Word Add-in Save Process](#4-word-add-in-save-process)
5. [Comparison Summary](#5-comparison-summary)

---

## 1. File Upload via UniversalDocumentUpload PCF Control

### Overview

User-initiated file upload through PCF control in Model-Driven Apps, Custom Pages, or Canvas Apps. Files are uploaded to SharePoint Embedded (SPE) and Document records created in Dataverse with optional AI analysis.

### Process Flow Diagram

```
┌──────────────────────────────────────────────────────────────┐
│ USER ACTION: Select Files → Enter Metadata → Click Upload   │
└────────────────────────┬─────────────────────────────────────┘
                         │
                         ▼
        ┌────────────────────────────────────────┐
        │  PHASE 1: FILE UPLOAD TO SPE           │
        ├────────────────────────────────────────┤
        │ Component: MultiFileUploadService.ts   │
        │ Endpoint:  PUT /api/containers/{id}/   │
        │            files/{*path}                │
        │ Handler:   UploadEndpoints.cs           │
        │ Service:   SpeFileStore.cs              │
        │ Result:    Graph DriveItem              │
        │            (id, webUrl, size)           │
        └────────────────┬───────────────────────┘
                         │ Returns: driveId, itemId, fileName
                         ▼
        ┌────────────────────────────────────────┐
        │  PHASE 2: CREATE DOCUMENT RECORD       │
        ├────────────────────────────────────────┤
        │ Component: DocumentRecordService.ts    │
        │ API:       context.webAPI.createRecord │
        │ Entity:    sprk_document               │
        │ Fields:    GraphDriveId, GraphItemId,  │
        │            FileName, FileSize, etc.    │
        │ Lookup:    Matter/Project/Invoice/etc. │
        │ Result:    Document GUID               │
        └────────────────┬───────────────────────┘
                         │ documentId
            ┌────────────┴────────────┐
            │                         │
            ▼                         ▼
    ┌─────────────────┐      ┌─────────────────┐
    │  PHASE 3: AI    │      │  PHASE 4: RAG   │
    │  SUMMARY        │      │  INDEXING       │
    │  (Optional)     │      │  (Optional)     │
    ├─────────────────┤      ├─────────────────┤
    │ Component:      │      │ Component:      │
    │   useAiSummary  │      │   RagService    │
    │ Endpoint:       │      │ Endpoint:       │
    │   POST /api/ai/ │      │   POST /api/ai/ │
    │   summary       │      │   rag/index-    │
    │                 │      │   file          │
    │ Concurrent:     │      │                 │
    │   3 files max   │      │ Handler:        │
    │                 │      │   FileIndexing  │
    │ Non-blocking:   │      │   Service       │
    │   Failures don't│      │                 │
    │   fail upload   │      │ Result:         │
    │                 │      │   Chunks indexed│
    │ Result:         │      │   to Azure AI   │
    │   Summary text  │      │   Search        │
    │   stored in     │      │                 │
    │   sprk_file     │      │ Non-blocking:   │
    │   summary field │      │   Fire-and-     │
    └─────────────────┘      │   forget        │
                             └─────────────────┘
```

### Code Components

#### Client-Side Components

| Component | File Path | Purpose |
|-----------|-----------|---------|
| **DocumentUploadForm** | `src/client/pcf/UniversalQuickCreate/control/components/DocumentUploadForm.tsx` | Main upload UI with file selection, metadata input, AI options |
| **FileSelectionField** | `src/client/pcf/UniversalQuickCreate/control/components/FileSelectionField.tsx` | Drag-and-drop file picker with validation (max 10 files, 10MB each, 100MB total) |
| **MultiFileUploadService** | `src/client/pcf/UniversalQuickCreate/control/services/MultiFileUploadService.ts` | Orchestrates multi-file upload, sequential processing, progress tracking |
| **DocumentRecordService** | `src/client/pcf/UniversalQuickCreate/control/services/DocumentRecordService.ts` | Creates Dataverse Document records with OData @odata.bind lookups |
| **useAiSummary** | `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts` | Manages AI summary queue, concurrent processing (3 max), error handling |

#### API Endpoints

| Endpoint | Handler | Auth | Purpose |
|----------|---------|------|---------|
| `PUT /api/containers/{containerId}/files/{*path}` | `UploadEndpoints.UploadFileAsync()` | OBO | Upload file to SPE |
| `POST /api/containers/{containerId}/upload` | `UploadEndpoints.CreateUploadSessionAsync()` | OBO | Create resumable upload session (large files) |
| `PUT /api/upload-session/chunk` | `UploadEndpoints.UploadChunkAsync()` | OBO | Upload chunk for large files |
| `POST /api/ai/summary` | `AiEndpoints.GenerateSummaryAsync()` | OBO | Generate AI document summary |
| `POST /api/ai/rag/index-file` | `RagEndpoints.IndexFileAsync()` | OBO | Index document for semantic search |

#### Server-Side Services

| Service | File Path | Purpose |
|---------|-----------|---------|
| **SpeFileStore** | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` | Facade for SPE operations (ADR-007), wraps Graph SDK v5.99.0 |
| **IDataverseService** | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | Dataverse operations (create/update/query) |
| **FileIndexingService** | `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` | Text extraction, chunking, embedding, Azure AI Search indexing |

### Azure Resources

| Resource | Purpose | Configuration |
|----------|---------|---------------|
| **SharePoint Embedded** | File storage | Container ID from config, files at `/{containerId}/{fileName}` |
| **Dataverse** | Document metadata | `sprk_document` entity with SPE pointers |
| **Azure OpenAI** | Text embeddings | `spaarke-openai-dev`, text-embedding-ada-002 model |
| **Azure AI Search** | RAG vector store | `spaarke-search-dev`, documents indexed with metadata |
| **MSAL** | Authentication | OBO token flow for user context |

### Data Flow

#### Document Record Structure

```json
{
  "sprk_documentname": "file.pdf",
  "sprk_filename": "file.pdf",
  "sprk_filesize": 1024000,
  "sprk_graphitemid": "01MZGZ6JZRNQQVGXQTK4KTPVBKQHGZFGZI",
  "sprk_graphdriveid": "b!xqQ8...",
  "sprk_filepath": "https://...",
  "sprk_filesummary": "AI-generated summary text",
  "sprk_filesummarydate": "2026-01-28T14:30:00Z",
  "sprk_matter@odata.bind": "/sprk_matters(guid)"
}
```

#### AI Summary Flow

1. User checks "Run AI Summary after upload"
2. After Document creation, `useAiSummary.addDocuments()` queues documents
3. Concurrent processing (3 files max) calls `/api/ai/summary` per file
4. On completion, updates `sprk_filesummary` field in Dataverse
5. Non-blocking: failures logged as warnings, don't fail upload

#### RAG Indexing Flow

1. Fire-and-forget call to `/api/ai/rag/index-file` after Document creation
2. Server downloads file from SPE using OBO authentication
3. Text extracted (PDF, DOCX, etc.) via `TextExtractor`
4. Text chunked semantically via `TextChunkingService`
5. Embeddings generated via Azure OpenAI
6. Indexed to Azure AI Search with metadata

### Error Handling

| Error Type | Handler | Retry | User Impact |
|------------|---------|-------|-------------|
| File too large | Client validation | No | Error shown before upload |
| SPE upload fails | Log, show error | No | Upload fails, user retries |
| Document creation fails | Log, show error | No | Upload succeeds, record creation fails |
| AI summary fails | Log warning | No | Document created, no summary |
| RAG indexing fails | Log warning | No | Document created, not searchable |

### Configuration

```json
{
  "FileUpload": {
    "MaxFiles": 10,
    "MaxFileSize": 10485760,
    "MaxTotalSize": 104857600
  },
  "Ai": {
    "EnableSummary": true,
    "EnableRagIndexing": false,
    "ConcurrentSummaries": 3
  }
}
```

---

## 2. Email-to-Document Automation Process

### Overview

Automated conversion of Dataverse email activities to Document records with .eml files stored in SPE. Triggered by webhook on email creation, processes email + attachments, and queues AI analysis.

### Process Flow Diagram

```
┌────────────────────────────────────────────────────────┐
│  EMAIL ARRIVAL IN DATAVERSE                            │
│  (User creates email activity OR webhook from Outlook) │
└──────────────────────────┬─────────────────────────────┘
                           │
            ┌──────────────┴──────────────┐
            │                             │
            ▼                             ▼
    ┌───────────────┐          ┌────────────────────┐
    │  PRIMARY:     │          │  BACKUP:           │
    │  WEBHOOK      │          │  POLLING SERVICE   │
    │  TRIGGER      │          │  (5-min intervals) │
    ├───────────────┤          ├────────────────────┤
    │ POST /api/v1/ │          │ Queries unprocessed│
    │ emails/       │          │ emails (24hr       │
    │ webhook-      │          │ lookback)          │
    │ trigger       │          │                    │
    │               │          │ Max 100 per cycle  │
    │ Validates     │          │                    │
    │ signature     │          │ Filters by         │
    │               │          │ sprk_document      │
    │ Extracts      │          │ processingstatus   │
    │ emailId       │          │ = null             │
    └───────┬───────┘          └────────┬───────────┘
            │                           │
            └──────────┬────────────────┘
                       │ Both enqueue to Service Bus
                       ▼
        ┌──────────────────────────────────────┐
        │  SERVICE BUS: sdap-jobs QUEUE        │
        ├──────────────────────────────────────┤
        │ Message: JobContract                 │
        │ JobType: "ProcessEmailToDocument"    │
        │ SubjectId: {emailId}                 │
        │ Payload: { EmailId, TriggerSource }  │
        │ IdempotencyKey: "Email:{id}:Archive" │
        └──────────────┬───────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────┐
        │  JOB HANDLER:                        │
        │  EmailToDocumentJobHandler           │
        ├──────────────────────────────────────┤
        │ 1. Check Idempotency (Redis)         │
        │ 2. Acquire Processing Lock           │
        │ 3. Circuit Breaker: Mark InProgress  │
        │                                      │
        │ 4. Convert Email to .eml             │
        │    └─ EmailToEmlConverter            │
        │       ├─ Fetch email from Dataverse │
        │       ├─ Fetch attachments (retry)  │
        │       ├─ Build MimeMessage (MimeKit)│
        │       └─ Write to MemoryStream      │
        │                                      │
        │ 5. Upload .eml to SPE                │
        │    └─ SpeFileStore.UploadSmallAsync │
        │       Path: /emails/{date}_{subj}.eml│
        │                                      │
        │ 6. Create Document Record            │
        │    └─ IDataverseService.Create       │
        │       DocumentAsync                  │
        │       Type: Email (100000006)        │
        │       Source: EmailArchive (659490003)│
        │                                      │
        │ 7. Update with Email Metadata        │
        │    └─ EmailSubject, EmailFrom,       │
        │       EmailTo, EmailDate, etc.       │
        │                                      │
        │ 8. Process Attachments               │
        │    └─ For each attachment:           │
        │       ├─ Filter (signatures, etc.)   │
        │       ├─ Upload to SPE (subpath)     │
        │       └─ Create child Document       │
        │          ParentDocumentLookup        │
        │                                      │
        │ 9. Enqueue AI Analysis (if enabled)  │
        │    └─ AppOnlyDocumentAnalysis job    │
        │       Queue: sdap-jobs               │
        │       Playbook: "Document Profile"   │
        │                                      │
        │ 10. Enqueue RAG Indexing (if enabled)│
        │     └─ RagIndexingJobHandler job     │
        │                                      │
        │ 11. Mark Processed (Redis, 7d TTL)   │
        │ 12. Circuit Breaker: Mark Completed  │
        └──────────────┬───────────────────────┘
                       │
            ┌──────────┴──────────┐
            │                     │
            ▼                     ▼
    ┌──────────────┐      ┌─────────────────┐
    │  AI ANALYSIS │      │  RAG INDEXING   │
    │  (Parallel)  │      │  (Parallel)     │
    ├──────────────┤      ├─────────────────┤
    │ AppOnlyDocu  │      │ RagIndexing     │
    │ mentAnalysis │      │ JobHandler      │
    │ JobHandler   │      │                 │
    │              │      │ Extract text    │
    │ Analyze doc  │      │ Chunk text      │
    │ Generate     │      │ Generate embed  │
    │ profile      │      │ Index to Azure  │
    │ summary      │      │ AI Search       │
    │              │      │                 │
    │ Store in     │      │ Result: Chunks  │
    │ sprk_analy   │      │ indexed         │
    │ sis entity   │      │                 │
    └──────────────┘      └─────────────────┘
```

### Code Components

#### Trigger Components

| Component | File Path | Purpose |
|-----------|-----------|---------|
| **WebhookTrigger** | `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` (lines 167-318) | Receives Dataverse webhook, validates signature, enqueues job |
| **EmailPollingBackupService** | `src/server/api/Sprk.Bff.Api/Services/Email/EmailPollingBackupService.cs` | Backup polling service for missed webhooks |

#### Job Handlers

| Component | File Path | Purpose |
|-----------|-----------|---------|
| **EmailToDocumentJobHandler** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` (966 lines) | Main handler: .eml conversion, Document creation, attachment processing |
| **AppOnlyDocumentAnalysisJobHandler** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` | AI analysis for Documents (app-only auth) |
| **EmailAnalysisJobHandler** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailAnalysisJobHandler.cs` | AI analysis for email activities (combined email + attachments) |
| **RagIndexingJobHandler** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` | Indexes documents to Azure AI Search |

#### Services

| Service | File Path | Purpose |
|---------|-----------|---------|
| **EmailToEmlConverter** | `src/server/api/Sprk.Bff.Api/Services/Email/EmailToEmlConverter.cs` (932 lines) | Converts Dataverse email activity to RFC 5322 compliant .eml using MimeKit |
| **AttachmentFilterService** | `src/server/api/Sprk.Bff.Api/Services/Email/AttachmentFilterService.cs` | Filters out noise: signatures, tracking pixels, small images, blocked extensions |
| **IAppOnlyAnalysisService** | `src/server/api/Sprk.Bff.Api/Services/Ai/IAppOnlyAnalysisService.cs` | App-only AI analysis (no user context required) |

### Azure Resources

| Resource | Purpose | Configuration |
|----------|---------|---------------|
| **Service Bus** | Job queue | Queue: `sdap-jobs`, 5 concurrent handlers |
| **SharePoint Embedded** | File storage | Path: `/emails/{date}_{subject}.eml`, `/emails/attachments/{parentDocId}/{fileName}` |
| **Dataverse** | Email activities, Documents | `email` entity, `sprk_document` entity, `activitymimeattachment` entity |
| **Redis Cache** | Idempotency & locking | Keys: `email:processed:{idempotencyKey}`, TTL: 7 days |
| **Azure OpenAI** | AI analysis | Playbook: "Document Profile" |
| **Azure AI Search** | RAG indexing | Vector store for semantic search |
| **Key Vault** | Secrets | `Dataverse:ClientSecret`, `ai-openai-key` |

### Data Flow

#### JobContract Structure

```json
{
  "jobId": "guid",
  "jobType": "ProcessEmailToDocument",
  "subjectId": "email-activity-id",
  "correlationId": "correlation-guid",
  "payload": {
    "EmailId": "guid",
    "TriggerSource": "Webhook" | "Polling"
  },
  "idempotencyKey": "Email:{emailId}:Archive",
  "attempt": 1,
  "maxAttempts": 3,
  "createdAt": "2026-01-28T14:30:00Z"
}
```

#### .eml Document Record

```json
{
  "sprk_documentname": "2026-01-28_Meeting Notes.eml",
  "sprk_documenttype": 100000006,
  "sprk_sourcetype": 659490003,
  "sprk_graphitemid": "spe-item-id",
  "sprk_graphdriveid": "spe-drive-id",
  "sprk_emailsubject": "Meeting Notes",
  "sprk_emailfrom": "sender@example.com",
  "sprk_emailto": "[\"recipient@example.com\"]",
  "sprk_emaildate": "2026-01-28T10:00:00Z",
  "sprk_emailbody": "Email body preview...",
  "sprk_emailmessageid": "<message-id@example.com>",
  "sprk_emailconversationindex": "thread-id",
  "sprk_email@odata.bind": "/emails(email-activity-guid)"
}
```

#### Attachment Document Record

```json
{
  "sprk_documentname": "attachment.pdf",
  "sprk_documenttype": 100000007,
  "sprk_relationshiptype": 100000000,
  "sprk_sourcetype": 659490004,
  "sprk_parentdocumentlookup@odata.bind": "/sprk_documents(parent-doc-guid)",
  "sprk_emailconversationindex": "thread-id",
  "sprk_graphitemid": "attachment-item-id"
}
```

### Processing Logic

#### Idempotency Check

```csharp
// Check Redis cache
var key = $"email:processed:{idempotencyKey}";
var exists = await _cache.GetStringAsync(key);
if (exists != null)
{
    // Already processed, skip
    return JobOutcome.Success(jobId, JobType, elapsed);
}
```

#### Processing Lock

```csharp
// Acquire distributed lock
var lockKey = $"email:lock:{emailId}";
var lockAcquired = await _cache.SetAsync(lockKey, "locked", new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
});
if (!lockAcquired)
{
    // Another instance processing, skip
    return JobOutcome.Failure(jobId, JobType, "Lock not acquired", retryable: true);
}
```

#### Circuit Breaker

```csharp
// Mark email as InProgress
await _dataverse.UpdateAsync("email", emailId, new Dictionary<string, object>
{
    ["sprk_documentprocessingstatus"] = 100000000 // InProgress
});

try
{
    // ... processing ...

    // Mark as Completed
    await _dataverse.UpdateAsync("email", emailId, new Dictionary<string, object>
    {
        ["sprk_documentprocessingstatus"] = 100000001 // Completed
    });
}
catch (Exception ex)
{
    // Mark as Failed
    await _dataverse.UpdateAsync("email", emailId, new Dictionary<string, object>
    {
        ["sprk_documentprocessingstatus"] = 100000002 // Failed
    });
    throw;
}
```

#### Attachment Filtering

```csharp
// AttachmentFilterService.FilterAttachments()
var filtered = attachments
    .Where(a => !IsBlockedExtension(a.FileName))
    .Where(a => !IsSignatureImage(a.FileName, a.SizeBytes))
    .Where(a => !IsSmallImage(a.SizeBytes, a.MimeType))
    .Where(a => !IsInline(a.ContentId) || includeInline)
    .ToList();
```

Blocked extensions: `.exe`, `.dll`, `.bat`, `.ps1`, `.vbs`, `.cmd`, `.com`, `.scr`, `.pif`, `.msi`

Signature patterns: `image***.png`, `spacer.gif`, `logo*.png`, `signature*.png`

Small image threshold: 5KB

### Error Handling

| Error Type | Handler | Retry | Dead Letter |
|------------|---------|-------|-------------|
| Email not found | Return Poisoned | No | Yes |
| Idempotency duplicate | Return Success | No | No |
| SPE upload fails | Return Failure | Yes (max 3) | After 3 attempts |
| Document creation fails | Return Failure | Yes (max 3) | After 3 attempts |
| Attachment processing fails | Log warning, continue | No | No |
| AI analysis fails (transient) | Return Failure | Yes (max 3) | After 3 attempts |
| AI analysis fails (permanent) | Return Poisoned | No | Yes |

### Configuration

```json
{
  "EmailProcessing": {
    "DefaultContainerId": "spe-container-guid",
    "ProcessInbound": true,
    "ProcessOutbound": true,
    "AutoEnqueueAi": true,
    "AutoIndexToRag": false,
    "MaxAttachmentSizeMB": 25,
    "BlockedAttachmentExtensions": [".exe", ".dll", ".bat", ".ps1"],
    "SignatureImagePatterns": ["image***.png", "spacer.gif", "logo*.png"],
    "MinImageSizeKB": 5,
    "PollingIntervalMinutes": 5,
    "PollingLookbackHours": 24,
    "PollingBatchSize": 100
  },
  "Jobs": {
    "ServiceBus": {
      "ConnectionString": "Endpoint=...",
      "QueueName": "sdap-jobs",
      "MaxConcurrentCalls": 5
    }
  }
}
```

---

## 3. Outlook Add-in Save Process

### Overview

User-initiated save from Outlook add-in taskpane. Uploads email/attachment to SPE, creates Document + artifact records, processes attachments, and queues AI analysis via Service Bus workers.

### Process Flow Diagram

```
┌───────────────────────────────────────────────────────┐
│  USER ACTION: Click "Save to Spaarke" in Taskpane    │
│  (Email or Attachment selected, Target Entity chosen) │
└──────────────────────────┬────────────────────────────┘
                           │
                           ▼
        ┌──────────────────────────────────────────┐
        │  CLIENT: SaveFlow Component              │
        ├──────────────────────────────────────────┤
        │ useSaveFlow.startSave()                  │
        │ ├─ Build SaveRequest with metadata       │
        │ ├─ Compute SHA-256 idempotency key       │
        │ └─ POST /office/save                     │
        └──────────────┬───────────────────────────┘
                       │ Authorization: Bearer {token}
                       ▼
        ┌──────────────────────────────────────────┐
        │  API: POST /office/save                  │
        ├──────────────────────────────────────────┤
        │ Handler: OfficeEndpoints.SaveAsync()     │
        │ Service: OfficeService.SaveAsync()       │
        │                                          │
        │ 1. Idempotency Check                     │
        │    └─ Query ProcessingJob by key        │
        │       └─ If exists: Return duplicate     │
        │                                          │
        │ 2. Create ProcessingJob Record           │
        │    └─ IDataverseService.Create           │
        │       ProcessingJobAsync                 │
        │       Status: Queued                     │
        │                                          │
        │ 3. Enrich Email from Graph (if needed)   │
        │    └─ Fetch email body + attachments    │
        │       via Microsoft Graph API (OBO)     │
        │                                          │
        │ 4. Build .eml or Attachment Stream       │
        │    └─ Email: MimeKit serialization      │
        │    └─ Attachment: Base64 decode         │
        │                                          │
        │ 5. Upload to SPE                         │
        │    └─ SpeFileStore.UploadSmallAsync()   │
        │       Result: driveId, itemId, webUrl   │
        │                                          │
        │ 6. Create Document Record                │
        │    └─ IDataverseService.Create           │
        │       DocumentAsync                      │
        │       └─ Base record created             │
        │                                          │
        │ 7. Update with SPE Metadata              │
        │    └─ IDataverseService.Update           │
        │       DocumentAsync                      │
        │       └─ GraphItemId, GraphDriveId, etc.│
        │                                          │
        │ 8. Queue UploadFinalization Job          │
        │    └─ Send OfficeJobMessage to          │
        │       Service Bus:                       │
        │       "office-upload-finalization"       │
        │       Payload: {                         │
        │         TempFileLocation: "spe://...",   │
        │         DocumentId: {created-doc-id},    │
        │         TriggerAiProcessing: true,       │
        │         AiOptions: { ... }               │
        │       }                                  │
        │                                          │
        │ 9. Return 202 Accepted                   │
        │    └─ { jobId, statusUrl, streamUrl }   │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  SERVICE BUS:                            │
        │  office-upload-finalization QUEUE        │
        ├──────────────────────────────────────────┤
        │ Message: OfficeJobMessage                │
        │ JobType: "UploadFinalization"            │
        │ Payload: UploadFinalizationPayload       │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  BACKGROUND WORKER:                      │
        │  UploadFinalizationWorker                │
        ├──────────────────────────────────────────┤
        │ BackgroundService (ADR-001)              │
        │ MaxConcurrentCalls: 5                    │
        │ Retry: Exponential backoff (3 attempts)  │
        │                                          │
        │ ProcessAsync(OfficeJobMessage)           │
        │                                          │
        │ 1. Check Idempotency (Redis)             │
        │    └─ Key: office:upload:processed:{key} │
        │       TTL: 7 days                        │
        │                                          │
        │ 2. Deserialize Payload                   │
        │                                          │
        │ 3. File Location Check                   │
        │    └─ If TempFileLocation = "spe://":   │
        │       File already in SPE, use existing  │
        │       Document ID from payload           │
        │                                          │
        │ 4. Create Artifact Records               │
        │    ├─ If Email: CreateEmailArtifact     │
        │    │   └─ sprk_emailartifact entity     │
        │    │      {Sender, Recipients, Subject,  │
        │    │       MessageId, ConversationId}    │
        │    │                                     │
        │    └─ If Attachment: Create              │
        │       AttachmentArtifact                 │
        │       └─ sprk_attachmentartifact         │
        │          {OriginalFilename, ContentType, │
        │           Size, IsInline}                │
        │                                          │
        │ 5. Process Email Attachments             │
        │    └─ Download .eml from SPE             │
        │    └─ Extract attachments (MimeKit)      │
        │    └─ Filter (signatures, etc.)          │
        │    └─ Respect user's attachment          │
        │       selection (SelectedAttachmentFile  │
        │       Names)                             │
        │    └─ For each selected attachment:      │
        │       ├─ Upload to SPE                   │
        │       │  Path: /emails/attachments/      │
        │       │        {parentDocId}/{fileName}  │
        │       ├─ Create child Document record    │
        │       ├─ Set ParentDocumentLookup        │
        │       ├─ Copy ConversationIndex          │
        │       └─ Copy EmailParentId              │
        │                                          │
        │ 6. Mark as Processed (Redis)             │
        │                                          │
        │ 7. Queue AI Processing (if enabled)      │
        │    └─ Create AppOnlyDocumentAnalysis job │
        │       Queue: sdap-jobs                   │
        │       JobType: "AppOnlyDocumentAnalysis" │
        │       Payload: {                         │
        │         DocumentId: {doc-guid},          │
        │         Source: "OfficeAddin",           │
        │         EnqueuedAt: {timestamp}          │
        │       }                                  │
        │       IdempotencyKey:                    │
        │         "analysis-{docId}-documentprofile"│
        │                                          │
        │ 8. Mark Job Complete                     │
        │    └─ Update ProcessingJob:              │
        │       Status = Completed, Progress = 100 │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  SERVICE BUS: sdap-jobs QUEUE            │
        ├──────────────────────────────────────────┤
        │ Message: JobContract                     │
        │ JobType: "AppOnlyDocumentAnalysis"       │
        │ SubjectId: {documentId}                  │
        │ Payload: { DocumentId, Source, ... }     │
        └──────────────┬───────────────────────────┘
                       │
                       ▼
        ┌──────────────────────────────────────────┐
        │  JOB HANDLER:                            │
        │  AppOnlyDocumentAnalysisJobHandler       │
        ├──────────────────────────────────────────┤
        │ 1. Check Idempotency                     │
        │ 2. Acquire Processing Lock               │
        │ 3. Call IAppOnlyAnalysisService          │
        │    .AnalyzeDocumentAsync()               │
        │    ├─ Load playbook ("Document Profile")│
        │    ├─ Resolve playbook scopes            │
        │    │  └─ ResolvePlaybookScopesAsync()    │
        │    │     └─ Query N:N relationship       │
        │    │        sprk_playbook_tool           │
        │    │     └─ Load AnalysisTool entities   │
        │    ├─ Fetch document + text from SPE     │
        │    ├─ Execute AI analysis                │
        │    │  └─ Azure OpenAI with Prompt Flow   │
        │    └─ Store results in sprk_analysis     │
        │       entity                             │
        │                                          │
        │ 4. Mark as Processed                     │
        │ 5. Release Lock                          │
        └──────────────────────────────────────────┘
```

### Code Components

#### Client-Side Components

| Component | File Path | Purpose |
|-----------|-----------|---------|
| **SaveFlow** | `src/client/office-addins/shared/taskpane/components/SaveFlow.tsx` | Main save orchestration UI |
| **useSaveFlow** | `src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts` | Save state management, API calls, job polling |
| **SaveView** | `src/client/office-addins/shared/taskpane/components/views/SaveView.tsx` | Entity picker, attachment selector, processing options |
| **EntityPicker** | `src/client/office-addins/shared/taskpane/components/EntityPicker.tsx` | Target entity selection (Matter, Project, Invoice, etc.) |
| **AttachmentSelector** | `src/client/office-addins/shared/taskpane/components/AttachmentSelector.tsx` | Checkbox list for attachment selection |

#### API Endpoints

| Endpoint | Handler | Auth | Purpose |
|----------|---------|------|---------|
| `POST /office/save` | `OfficeEndpoints.SaveAsync()` | OAuth | Save email/attachment to Spaarke |
| `GET /office/jobs/{jobId}` | `OfficeEndpoints.GetJobStatusAsync()` | OAuth | Poll job status |
| `GET /office/jobs/{jobId}/stream` | `OfficeEndpoints.StreamJobStatusAsync()` | OAuth | SSE streaming job status |

#### Server-Side Services

| Service | File Path | Purpose |
|---------|-----------|---------|
| **OfficeService** | `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs` | Main orchestration: ProcessingJob creation, Graph API enrichment, SPE upload |
| **UploadFinalizationWorker** | `src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs` | Background worker for artifact creation, attachment processing |
| **IEmailToEmlConverter** | `src/server/api/Sprk.Bff.Api/Services/Email/IEmailToEmlConverter.cs` | Email to .eml conversion (MimeKit) |
| **AppOnlyDocumentAnalysisJobHandler** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` | AI analysis handler (app-only auth) |
| **IAppOnlyAnalysisService** | `src/server/api/Sprk.Bff.Api/Services/Ai/IAppOnlyAnalysisService.cs` | AI analysis service |
| **IScopeResolverService** | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Resolves playbook scopes (Skills, Knowledge, Tools) from N:N relationships |

### Azure Resources

| Resource | Purpose | Configuration |
|----------|---------|---------------|
| **Service Bus** | Job queues | Queues: `office-upload-finalization`, `sdap-jobs` |
| **SharePoint Embedded** | File storage | Paths: `/{fileName}.eml`, `/emails/attachments/{parentDocId}/{fileName}` |
| **Dataverse** | Records | `sprk_document`, `sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_processingjob`, `sprk_analysis` |
| **Redis Cache** | Idempotency & locking | Keys: `office:upload:processed:{key}`, TTL: 7 days |
| **Microsoft Graph API** | Email enrichment | Fetch email body + attachments via OBO token |
| **Azure OpenAI** | AI analysis | Playbook execution |
| **Key Vault** | Secrets | `ServiceBusConnectionString`, `Dataverse:ClientSecret` |

### Data Flow

#### SaveRequest Structure

```json
{
  "contentType": "Email" | "Attachment" | "Document",
  "targetEntity": {
    "entityType": "matter",
    "entityId": "guid",
    "displayName": "Matter Name"
  },
  "email": {
    "subject": "Meeting Notes",
    "senderEmail": "sender@example.com",
    "recipients": ["recipient@example.com"],
    "sentDate": "2026-01-28T10:00:00Z",
    "conversationId": "thread-id",
    "internetMessageId": "<message-id@example.com>",
    "body": "Email body...",
    "isBodyHtml": true,
    "attachments": [
      {
        "fileName": "attachment.pdf",
        "contentType": "application/pdf",
        "size": 1024000
      }
    ],
    "selectedAttachmentFileNames": ["attachment.pdf"]
  },
  "triggerAiProcessing": true,
  "aiOptions": {
    "profileSummary": true,
    "ragIndex": true,
    "deepAnalysis": false
  },
  "idempotencyKey": "sha256-hash"
}
```

#### OfficeJobMessage Structure

```json
{
  "jobId": "guid",
  "jobType": "UploadFinalization",
  "userId": "user-guid",
  "correlationId": "correlation-guid",
  "idempotencyKey": "sha256-hash",
  "attempt": 1,
  "maxAttempts": 3,
  "payload": {
    "contentType": "Email",
    "fileName": "email.eml",
    "fileSize": 1024000,
    "mimeType": "message/rfc822",
    "tempFileLocation": "spe://drive-id/item-id",
    "containerId": "spe-container-guid",
    "folderPath": "/emails/2024-01",
    "associationType": "matter",
    "associationId": "matter-guid",
    "triggerAiProcessing": true,
    "aiOptions": {
      "profileSummary": true,
      "ragIndex": true,
      "deepAnalysis": false
    },
    "emailMetadata": { ... },
    "attachmentMetadata": { ... },
    "documentId": "created-doc-guid"
  }
}
```

#### JobStatusResponse Structure

```json
{
  "jobId": "guid",
  "status": "Queued" | "Running" | "Completed" | "Failed",
  "jobType": "EmailSave",
  "progress": 0-100,
  "currentPhase": "RecordsCreated" | "FileUploaded" | "ProfileSummary" | "Indexed",
  "completedPhases": [
    {
      "name": "RecordsCreated",
      "completedAt": "2026-01-28T14:30:45Z",
      "durationMs": 1000
    }
  ],
  "createdAt": "2026-01-28T14:30:00Z",
  "completedAt": "2026-01-28T14:35:00Z",
  "result": {
    "artifact": {
      "type": "Document",
      "id": "document-guid",
      "webUrl": "https://...",
      "speFileId": "item-id"
    }
  },
  "error": null | {
    "code": "OFFICE_012",
    "message": "Failed to upload",
    "retryable": true
  }
}
```

### Processing Logic

#### Idempotency (Duplicate Detection)

```csharp
// Check Dataverse for existing ProcessingJob
var existingJob = await _dataverse.QueryAsync(
    "sprk_processingjobs",
    $"?$filter=sprk_idempotencykey eq '{idempotencyKey}'&$top=1"
);

if (existingJob != null)
{
    // Duplicate detected
    return new SaveResponse
    {
        Duplicate = true,
        DocumentId = existingJob.DocumentId,
        Message = "Document already saved"
    };
}
```

#### Attachment Selection Handling

```csharp
// In UploadFinalizationWorker.ProcessAsync()
var selectedAttachmentFileNames = payload.EmailMetadata?.SelectedAttachmentFileNames;

// Filter attachments based on user selection
var attachmentsToProcess = allAttachments.Where(a =>
{
    if (selectedAttachmentFileNames == null || selectedAttachmentFileNames.Length == 0)
    {
        // null/undefined: Process all (backward compatible)
        // empty array: Process none (user deselected all)
        return selectedAttachmentFileNames == null;
    }

    // Process only selected
    return selectedAttachmentFileNames.Contains(a.FileName);
}).ToList();
```

#### Child Document Creation (Attachments)

```csharp
// Create child Document for each attachment
var childDocument = new CreateDocumentRequest
{
    Name = attachment.FileName,
    FileName = attachment.FileName,
    FileSize = attachment.SizeBytes,
    GraphItemId = attachmentItemId,
    GraphDriveId = driveId,
    FilePath = attachmentWebUrl,
    DocumentType = 100000007, // Email attachment
    ParentDocumentLookup = parentDocumentId,
    ParentFileName = parentFileName,
    ParentGraphItemId = parentItemId,
    RelationshipType = 100000000, // Email attachment relationship
    SourceType = 659490004, // Email attachment source
    EmailConversationIndex = parentConversationId,
    EmailParentId = parentInternetMessageId
};

await _dataverse.CreateDocumentAsync(childDocument);
```

### Error Handling

| Error Type | Handler | Retry | User Impact |
|------------|---------|-------|-------------|
| Duplicate save | Return 200 with duplicate flag | No | User sees "Already saved" message |
| Invalid association | Return 400 validation error | No | User corrects and retries |
| SPE upload fails | Queue retry | Yes (3 max) | Background retry, user notified on final failure |
| Document creation fails | Queue retry | Yes (3 max) | Background retry |
| Attachment processing fails | Log warning, continue | No | Email saved, attachments skipped |
| AI analysis fails (transient) | Queue retry | Yes (3 max) | Background retry |
| AI analysis fails (permanent) | Dead letter | No | Document saved, no analysis |

### Configuration

```json
{
  "Office": {
    "RateLimitOptions": {
      "SaveRequestsPerMinute": 10,
      "JobStatusRequestsPerMinute": 60
    },
    "DefaultContainerId": "spe-container-guid"
  },
  "ServiceBus": {
    "ConnectionString": "Endpoint=...",
    "Queues": {
      "UploadFinalization": "office-upload-finalization",
      "Jobs": "sdap-jobs"
    }
  },
  "EmailProcessing": {
    "MaxAttachmentSizeMB": 25,
    "BlockedAttachmentExtensions": [".exe", ".dll", ".bat", ".ps1"]
  }
}
```

---

## 4. Word Add-in Save Process

### Overview

The Word add-in save process **shares the same architecture** as the Outlook add-in with minor differences in content type and metadata.

### Shared Architecture

| Component | Shared with Outlook | Notes |
|-----------|---------------------|-------|
| **SaveFlow Component** | ✅ Yes | Same UI component |
| **useSaveFlow Hook** | ✅ Yes | Same state management |
| **API Endpoint** | ✅ Yes | POST /office/save |
| **OfficeService** | ✅ Yes | Same orchestration logic |
| **UploadFinalizationWorker** | ✅ Yes | Same background worker |
| **Service Bus Queues** | ✅ Yes | office-upload-finalization, sdap-jobs |
| **AI Analysis Pipeline** | ✅ Yes | AppOnlyDocumentAnalysisJobHandler |

### Differences from Outlook Add-in

#### Content Type

**Outlook:**
- `contentType: "Email"` - Email messages
- `contentType: "Attachment"` - Email attachments

**Word:**
- `contentType: "Document"` - Word documents

#### Metadata

**Word-Specific Fields:**
```json
{
  "document": {
    "fileName": "contract.docx",
    "title": "Contract Agreement",
    "contentType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    "size": 1024000,
    "contentBase64": "base64-encoded-content"
  }
}
```

**Outlook-Specific Fields:**
```json
{
  "email": {
    "subject": "...",
    "senderEmail": "...",
    "recipients": [...],
    "attachments": [...]
  }
}
```

#### Document Record Differences

**Word Document:**
```json
{
  "sprk_documenttype": 100000000, // Word document (not email)
  "sprk_sourcetype": 659490000, // Office add-in
  "sprk_filename": "contract.docx",
  "sprk_mimetype": "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
}
```

**Email Document:**
```json
{
  "sprk_documenttype": 100000006, // Email
  "sprk_sourcetype": 659490003, // Email archive
  "sprk_filename": "email.eml",
  "sprk_mimetype": "message/rfc822",
  "sprk_emailsubject": "...",
  "sprk_emailfrom": "..."
}
```

### Word-Specific Processing

#### No Attachment Processing

Unlike Outlook emails, Word documents don't have child attachments, so:
- UploadFinalizationWorker skips attachment extraction
- No child Document records created
- No EmailArtifact/AttachmentArtifact records

#### AI Analysis

Same pipeline as Outlook:
1. Document uploaded to SPE
2. AppOnlyDocumentAnalysisJobHandler processes
3. "Document Profile" playbook executed
4. Results stored in sprk_analysis entity

#### RAG Indexing

Same indexing pipeline:
- Text extracted from .docx
- Chunked semantically
- Embedded via Azure OpenAI
- Indexed to Azure AI Search

### Flow Diagram (Word-Specific)

```
┌──────────────────────────────────────────────────┐
│  USER ACTION: Click "Save to Spaarke" in Word   │
│  (Document open, Target Entity chosen)           │
└─────────────────────┬────────────────────────────┘
                      │
                      ▼
        ┌─────────────────────────────────┐
        │  SaveFlow → useSaveFlow         │
        │  POST /office/save              │
        │  {                              │
        │    contentType: "Document",     │
        │    document: { ... },           │
        │    targetEntity: { ... }        │
        │  }                              │
        └─────────────┬───────────────────┘
                      │
                      ▼
        ┌─────────────────────────────────┐
        │  OfficeService.SaveAsync()      │
        │  1. Idempotency check           │
        │  2. Create ProcessingJob        │
        │  3. Decode base64 content       │
        │  4. Upload to SPE               │
        │  5. Create Document record      │
        │  6. Queue UploadFinalization    │
        │  7. Return 202 + jobId          │
        └─────────────┬───────────────────┘
                      │
                      ▼
        ┌─────────────────────────────────┐
        │  UploadFinalizationWorker       │
        │  1. Check idempotency           │
        │  2. Skip artifact creation      │
        │     (No EmailArtifact)          │
        │  3. Skip attachment processing  │
        │     (No child attachments)      │
        │  4. Queue AI analysis           │
        │  5. Mark job complete           │
        └─────────────┬───────────────────┘
                      │
                      ▼
        ┌─────────────────────────────────┐
        │  AppOnlyDocumentAnalysis        │
        │  (Same as Outlook add-in)       │
        │  1. Load "Document Profile"     │
        │  2. Analyze .docx content       │
        │  3. Store results               │
        └─────────────────────────────────┘
```

### Configuration

Same configuration as Outlook add-in (see [Section 3 Configuration](#configuration-2))

---

## 5. Comparison Summary

### Architecture Overview

| Process | Trigger | SPE Upload | Document Creation | Artifact Creation | Attachment Processing | AI Analysis Queue |
|---------|---------|------------|-------------------|-------------------|----------------------|-------------------|
| **File Upload (PCF)** | User click | Client → API → SPE | Client → Dataverse | None | N/A | Optional (fire-and-forget) |
| **Email-to-Document** | Webhook/Polling | Job handler → SPE | Job handler → Dataverse | None | Yes (child Documents) | Queued to sdap-jobs |
| **Outlook Add-in** | User click | API → SPE → Worker updates | API → Dataverse | Worker → EmailArtifact/AttachmentArtifact | Yes (child Documents) | Queued to sdap-jobs |
| **Word Add-in** | User click | API → SPE → Worker updates | API → Dataverse | None | No | Queued to sdap-jobs |

### Service Bus Queues

| Queue | Used By | Purpose |
|-------|---------|---------|
| `sdap-jobs` | All processes | Main job queue for AI analysis (AppOnlyDocumentAnalysisJobHandler, RagIndexingJobHandler, etc.) |
| `office-upload-finalization` | Outlook/Word add-in only | Finalize upload, create artifacts, process attachments |

### AI Analysis Triggers

| Process | Trigger Method | Job Type | Playbook |
|---------|----------------|----------|----------|
| **File Upload (PCF)** | Client calls `/api/ai/summary` (fire-and-forget) | Direct API call | Default |
| **Email-to-Document** | Handler queues `AppOnlyDocumentAnalysis` to `sdap-jobs` | Job queue | "Document Profile" |
| **Outlook Add-in** | Worker queues `AppOnlyDocumentAnalysis` to `sdap-jobs` | Job queue | "Document Profile" |
| **Word Add-in** | Worker queues `AppOnlyDocumentAnalysis` to `sdap-jobs` | Job queue | "Document Profile" |

### Key Differences

#### File Upload (PCF)
- **Synchronous** upload flow (user waits for completion)
- **No Service Bus** - direct API calls
- **No artifact records** - just Document entity
- **Optional AI** - user checkbox controls
- **Fire-and-forget AI** - non-blocking, client-initiated

#### Email-to-Document
- **Asynchronous** automation (webhook-triggered)
- **Service Bus** - `sdap-jobs` queue
- **No artifact records** - just Document + child Documents for attachments
- **Automatic AI** - always queued if `AutoEnqueueAi` enabled
- **Background workers** - EmailToDocumentJobHandler, AppOnlyDocumentAnalysisJobHandler

#### Outlook Add-in
- **Asynchronous** user-initiated (returns 202 immediately)
- **Service Bus** - `office-upload-finalization`, then `sdap-jobs`
- **Artifact records** - EmailArtifact/AttachmentArtifact + Document
- **Configurable AI** - user checkboxes control options
- **Background workers** - UploadFinalizationWorker, AppOnlyDocumentAnalysisJobHandler
- **Attachment processing** - Downloads .eml, extracts attachments, creates child Documents

#### Word Add-in
- **Same as Outlook** except:
  - No EmailArtifact/AttachmentArtifact
  - No attachment extraction
  - contentType: "Document" instead of "Email"

### Common Infrastructure

All processes share:
- **SharePoint Embedded (SPE)** for file storage
- **Dataverse** for metadata (Document entity)
- **AppOnlyDocumentAnalysisJobHandler** for AI analysis
- **IAppOnlyAnalysisService** for AI orchestration
- **IScopeResolverService** for playbook scope resolution
- **Azure OpenAI** for AI analysis
- **Azure AI Search** for RAG indexing (optional)

### Idempotency Mechanisms

| Process | Mechanism | Storage |
|---------|-----------|---------|
| **File Upload (PCF)** | None (client-side only) | N/A |
| **Email-to-Document** | IdempotencyKey in Redis | `email:processed:{key}` (7d TTL) |
| **Outlook Add-in** | IdempotencyKey in Dataverse + Redis | ProcessingJob entity + `office:upload:processed:{key}` (7d TTL) |
| **Word Add-in** | Same as Outlook | Same as Outlook |

### Error Handling

| Process | Retry Logic | Dead Letter | User Notification |
|---------|-------------|-------------|-------------------|
| **File Upload (PCF)** | None | N/A | Immediate error shown |
| **Email-to-Document** | Exponential backoff (3 max) | Yes | Admin only (email status field) |
| **Outlook Add-in** | Exponential backoff (3 max) | Yes | Job status polling, error in UI |
| **Word Add-in** | Same as Outlook | Yes | Same as Outlook |

---

## Appendix A: Common Dataverse Entities

### sprk_document

Primary entity for all file metadata across all processes.

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_documentname` | String | User-visible name |
| `sprk_filename` | String | Original filename |
| `sprk_filesize` | Integer | File size in bytes |
| `sprk_graphitemid` | String | SPE Graph DriveItem ID |
| `sprk_graphdriveid` | String | SPE Graph Drive/Container ID |
| `sprk_filepath` | String | SharePoint URL |
| `sprk_documenttype` | Choice | Email (100000006), EmailAttachment (100000007), Document (100000000), etc. |
| `sprk_sourcetype` | Choice | EmailArchive (659490003), EmailAttachment (659490004), OfficeAddin (659490000), etc. |
| `sprk_filesummary` | Memo | AI-generated summary text |
| `sprk_filesummarydate` | DateTime | When summary generated |
| `sprk_emailsubject` | String | Email subject (if email) |
| `sprk_emailfrom` | String | Sender email (if email) |
| `sprk_emailto` | String | Recipients JSON (if email) |
| `sprk_emaildate` | DateTime | Sent date (if email) |
| `sprk_emailbody` | Memo | Email body preview (if email) |
| `sprk_emailmessageid` | String | Internet Message ID (if email) |
| `sprk_emailconversationindex` | String | Conversation/thread ID (if email) |
| `sprk_parentdocumentlookup` | Lookup | Parent document (if attachment) |
| `sprk_matter` | Lookup | Associated matter |
| `sprk_project` | Lookup | Associated project |
| `sprk_invoice` | Lookup | Associated invoice |

### sprk_emailartifact

Created by Outlook add-in only (not email-to-document).

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_name` | String | "{Subject} - {SentDate}" |
| `sprk_sender` | String | Sender email |
| `sprk_recipients` | Memo | Recipients JSON |
| `sprk_sentdate` | DateTime | Sent date |
| `sprk_receiveddate` | DateTime | Received date |
| `sprk_messageid` | String | Internet Message ID |
| `sprk_conversationid` | String | Conversation/thread ID |
| `sprk_bodypreview` | Memo | Email body preview |
| `sprk_hasattachments` | Boolean | Has attachments flag |
| `sprk_priority` | Choice | Important (100000001), Normal (100000002), Low (100000003) |
| `sprk_documentid` | Lookup | Associated Document |

### sprk_attachmentartifact

Created by Outlook add-in only (not email-to-document).

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_name` | String | Original filename |
| `sprk_originalfilename` | String | Original filename |
| `sprk_contenttype` | String | MIME type |
| `sprk_size` | Integer | Size in bytes |
| `sprk_isinline` | Boolean | Inline attachment flag |
| `sprk_emailartifactid` | Lookup | Parent EmailArtifact (if from email) |
| `sprk_documentid` | Lookup | Associated Document |

### sprk_processingjob

Created by Outlook/Word add-in only.

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_name` | String | "Email Save - {timestamp}" |
| `sprk_jobtype` | Choice | EmailSave (100000001), DocumentSave (100000002), etc. |
| `sprk_status` | Choice | Queued (0), Running (1), Completed (2), Failed (3), Cancelled (4) |
| `sprk_progress` | Integer | 0-100 |
| `sprk_currentstage` | String | "RecordsCreated", "FileUploaded", "ProfileSummary", etc. |
| `sprk_idempotencykey` | String | SHA-256 hash for duplicate detection |
| `sprk_correlationid` | String | Correlation GUID for tracing |
| `sprk_payload` | Memo | JSON payload for replay |
| `sprk_completeddate` | DateTime | Completion timestamp |
| `sprk_errorcode` | String | Error code (if failed) |
| `sprk_errormessage` | Memo | Error message (if failed) |

### sprk_analysis

Created by AI analysis pipeline (all processes).

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_name` | String | "{DocumentName} - Analysis" |
| `sprk_documentid` | Lookup | Associated Document |
| `sprk_playbookid` | Lookup | Playbook used |
| `sprk_status` | Choice | Queued, Running, Completed, Failed |
| `sprk_result` | Memo | JSON analysis results |
| `sprk_createddate` | DateTime | Analysis start |
| `sprk_completeddate` | DateTime | Analysis completion |

---

## Appendix B: Service Bus Job Types

| Job Type | Handler | Queue | Purpose |
|----------|---------|-------|---------|
| `ProcessEmailToDocument` | EmailToDocumentJobHandler | sdap-jobs | Convert email to .eml + attachments |
| `AppOnlyDocumentAnalysis` | AppOnlyDocumentAnalysisJobHandler | sdap-jobs | AI analysis for Documents |
| `EmailAnalysis` | EmailAnalysisJobHandler | sdap-jobs | AI analysis for email activities |
| `RagIndexingJobHandler` | RagIndexingJobHandler | sdap-jobs | Index documents to Azure AI Search |
| `UploadFinalization` | UploadFinalizationWorker | office-upload-finalization | Finalize Office add-in uploads |

---

## Appendix C: Azure OpenAI Playbooks

| Playbook | Purpose | Used By |
|----------|---------|---------|
| **Document Profile** | Extract key entities, categories, risk assessment from documents | All AI analysis processes |
| **Email Analysis** | Analyze email content + attachments combined | EmailAnalysisJobHandler |
| **Deep Analysis** | Comprehensive document analysis (optional) | User-requested via add-in |

Each playbook has N:N relationships to:
- **Skills** (`sprk_playbook_skill`) - AI capabilities
- **Knowledge** (`sprk_playbook_knowledge`) - Reference data
- **Tools** (`sprk_playbook_tool`) - Analysis tools

---

*End of Document*
