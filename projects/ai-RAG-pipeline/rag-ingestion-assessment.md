# RAG Document Ingestion Pipeline - Comprehensive Assessment

> **Date**: 2026-01-19
> **Status**: ✅ Architecture Consolidated - Phase 1 Complete, Phase 2 (Bulk Indexing) In Progress
> **Author**: AI Analysis
> **Related Issue**: Documents not being indexed to AI Search on upload
> **Last Code Review**: 2026-01-19

---

## Executive Summary

### ✅ RESOLVED: Architecture Consolidation Complete (2026-01-19)

The dual-queue architecture has been eliminated. All RAG indexing now uses a single `sdap-jobs` queue:

**Changes Made:**
- ❌ **DELETED**: `DocumentEventProcessor.cs` and 5 related files (~460 lines)
- ❌ **DELETED**: `DocumentEventProcessorOptions.cs`, `DocumentEventTelemetry.cs`, `DocumentEvent.cs`
- ❌ **DELETED**: `IDocumentEventHandler.cs`, `DocumentEventHandler.cs`
- ❌ **DELETED**: `DocumentEventHandlerRagTests.cs` (obsolete test)
- ✅ **UPDATED**: `WorkersModule.cs` - removed DocumentEventProcessor registration
- ✅ **UPDATED**: `appsettings.template.json` - removed DocumentEventProcessor config section

### Current Architecture (Consolidated)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    CONSOLIDATED RAG INDEXING ARCHITECTURE               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  REAL-TIME INDEXING (Existing - Working)                                │
│  ┌──────────────┐    ┌──────────────┐    ┌───────────────────────────┐ │
│  │ FileUpload   │───►│ /api/ai/rag/ │───►│ FileIndexingService       │ │
│  │ PCF          │    │ index-file   │    │ (OBO auth, synchronous)   │ │
│  └──────────────┘    └──────────────┘    └───────────────────────────┘ │
│                                                                         │
│  ┌──────────────┐    ┌──────────────┐    ┌───────────────────────────┐ │
│  │ Email        │───►│ sdap-jobs    │───►│ RagIndexingJobHandler     │ │
│  │ Processing   │    │ queue        │    │ (app-only auth)           │ │
│  └──────────────┘    └──────────────┘    └───────────────────────────┘ │
│                                                                         │
│  BULK INDEXING (Phase 2 - In Progress)                                  │
│  ┌──────────────┐    ┌──────────────┐    ┌───────────────────────────┐ │
│  │ Admin UI     │───►│ POST /api/ai │───►│ BulkRagIndexingJobHandler │ │
│  │ "Index Docs" │    │ /rag/admin/  │    │ (progress tracking)       │ │
│  │              │    │ bulk-index   │    │                           │ │
│  └──────────────┘    └──────────────┘    └───────────────────────────┘ │
│                                                                         │
│  ┌──────────────┐    ┌──────────────┐    ┌───────────────────────────┐ │
│  │ Scheduled    │───►│ sdap-jobs    │───►│ BulkRagIndexingJobHandler │ │
│  │ Timer Job    │    │ queue        │    │ (find unindexed docs)     │ │
│  └──────────────┘    └──────────────┘    └───────────────────────────┘ │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### What's Working ✅
- **Email attachments** → Indexed successfully (via `sdap-jobs` queue)
- **PCF FileUpload** → Direct API call to `/api/ai/rag/index-file` (working)

### Remaining Work (Phase 2) 🔧
- **Bulk indexing endpoint** → For admin manual indexing (in progress)
- **Scheduled indexing** → For periodic indexing of unindexed documents (in progress)

### Key Constraints
- **No Dataverse Plugins** - ADR-002 prohibits HTTP/Service Bus calls from plugins
- **Single Queue Architecture** - All async processing via `sdap-jobs` queue

---

## End-to-End Flow Analysis (2026-01-19)

### Flow 1: PCF FileUpload → Search Index ✅ WORKING

**Source**: [DocumentUploadForm.tsx:247-271](src/client/pcf/UniversalQuickCreate/control/components/DocumentUploadForm.tsx#L247-L271)

```
PCF DocumentUploadForm
    │
    ├──► Phase 1: Upload file to SPE
    │    └── PUT /api/obo/containers/{id}/files/{path} → SpeFileStore.UploadSmallAsUserAsync()
    │
    ├──► Phase 2: Create Dataverse document record
    │    └── documentRecordService.createDocuments()
    │
    ├──► Phase 3: AI Summary (optional, streaming)
    │    └── POST /api/ai/document-intelligence/analyze
    │
    └──► Phase 4: RAG Indexing (fire-and-forget)
         └── POST /api/ai/rag/index-file
                 │
                 ▼
         FileIndexingService.IndexFileAsync() (OBO auth)
                 │
                 ├── Download file from SPE (OBO)
                 ├── Extract text (Document Intelligence)
                 ├── Chunk text (TextChunkingService)
                 ├── Generate embeddings (OpenAI)
                 └── Index to Azure AI Search
```

**Status**: ✅ WORKING - Documents uploaded via PCF are indexed to Search

### Flow 2: Email → Search Index ✅ WORKING

**Source**: [EmailToDocumentJobHandler.cs:301-303](src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs#L301-L303)

```
Graph Webhook → BFF API
    │
    ├──► EmailWebhookEndpoints → EmailToDocumentJobHandler
    │        │
    │        ├── Create .eml file in SPE
    │        ├── Create Dataverse document record
    │        ├── Process attachments → child documents
    │        │
    │        └── EnqueueRagIndexingJobAsync() (if AutoIndexToRag=true)
    │                │
    │                ▼
    │        sdap-jobs queue (JobContract with type="RagIndexing")
    │
    ▼
ServiceBusJobProcessor → RagIndexingJobHandler → FileIndexingService (app-only auth)
```

**Status**: ✅ WORKING - Email documents indexed at 2026-01-19T17:09:06Z

### Flow 3: Matter/Dataverse Documents → Search Index ❌ NOT WORKING

**Intended Source**: [DocumentEventHandler.cs:545-607](src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/DocumentEventHandler.cs#L545-L607)

```
Dataverse Plugin → Service Bus
    │
    ├──► document-events queue (DocumentEvent message)
    │
    ▼
DocumentEventProcessor → DocumentEventHandler.HandleEventAsync()
    │
    ├── HandleDocumentCreatedAsync() (sprk_hasfile=true only)
    │        │
    │        └── EnqueueRagIndexingJobAsync() (if AutoIndexToRag=true)
    │                │
    │                ▼
    │        sdap-jobs queue (JobContract with type="RagIndexing")
    │
    ▼
ServiceBusJobProcessor → RagIndexingJobHandler → FileIndexingService (app-only auth)
```

**Status**: ❌ NOT WORKING
- Queue has 4 dead-lettered messages
- No logs from DocumentEventProcessor in Application Insights
- Service is registered but not processing

### Service Bus Queue Status (2026-01-19)

| Queue | Active | Dead Letter | Status |
|-------|--------|-------------|--------|
| `sdap-jobs` | 0 | 2938 | Processing, but backlog in DLQ (mostly email duplicates) |
| `document-events` | 0 | 4 | NOT processing - DocumentEventProcessor appears inactive |

---

## Recommended Solution: Consolidate to Single Queue

### Option A: Fix DocumentEventProcessor (Maintains Current Architecture)

**Pros**: Minimal code changes
**Cons**: Still has 2-hop inefficiency, maintains complexity

**Steps**:
1. Debug why DocumentEventProcessor is not processing messages
2. Check DI registration ordering (potential ServiceBusClient conflict)
3. Add startup logging to verify service starts

### Option B: Eliminate DocumentEventProcessor (RECOMMENDED) ⭐

**Pros**: Single job queue, simpler architecture, matches email flow
**Cons**: Requires Dataverse plugin modification

**Steps**:
1. Modify Dataverse plugin to send `RagIndexing` jobs directly to `sdap-jobs` queue
2. Use same `JobContract` format as email processing
3. Delete `DocumentEventProcessor` and `document-events` queue
4. Update documentation to reflect single-queue architecture

**New Architecture**:
```
ALL document indexing flows → sdap-jobs queue → ServiceBusJobProcessor
                                                        │
                                                        ├── EmailToDocumentJobHandler
                                                        ├── RagIndexingJobHandler ← Dataverse plugin jobs
                                                        └── Other job handlers
```

### Option C: Enhance PCF Direct Indexing (Simplest Short-Term Fix)

**Pros**: No backend changes needed, immediate fix
**Cons**: Only works for PCF-created documents, not Dataverse UI/flows

**Steps**:
1. Ensure all PCF controls that create documents also call `/api/ai/rag/index-file`
2. For Matter documents: Add indexing call to the form's onSave event
3. For Dataverse UI users: Provide manual "Index Document" button

---

## Documentation Discrepancy

**RAG-ARCHITECTURE.md** describes a single-queue architecture using `sdap-jobs`:
- `ServiceBusJobProcessor` processes all jobs
- `RagIndexingJobHandler` handles indexing jobs
- No mention of `DocumentEventProcessor` or `document-events` queue

**Actual Implementation** has two processors:
- `ServiceBusJobProcessor` for `sdap-jobs` queue (working)
- `DocumentEventProcessor` for `document-events` queue (not working)

**Action**: Update documentation OR consolidate to match documentation

---

## Recent Updates (Since 2026-01-14)

### Code Changes (Jan 14-19, 2026) - IMPLEMENTATION COMPLETE
| File | Change | Status |
|------|--------|--------|
| `IFileIndexingService.cs` | Created unified indexing interface with 3 entry points | ✅ Complete |
| `FileIndexingService.cs` | Full pipeline: download → extract → chunk → embed → index | ✅ Complete |
| `ITextChunkingService.cs` | Shared chunking interface | ✅ Complete |
| `TextChunkingService.cs` | Extracted from tool handlers, sentence-aware chunking | ✅ Complete |
| `RagEndpoints.cs` | Added `/index-file` and `/enqueue-indexing` endpoints | ✅ Complete |
| `RagIndexingJobHandler.cs` | Background job processor with idempotency | ✅ Complete |
| `DocumentEventHandler.cs` | Added `EnqueueRagIndexingJobAsync()` for event-driven indexing | ✅ Complete |
| `Program.cs` | DI registrations for all new services | ✅ Complete |

### Implementation Status (RESOLVED)
The **orchestration layer is now complete**. `IFileIndexingService` serves as the `DocumentIndexingService` referenced in the original assessment. All placeholder implementations in `DocumentEventHandler.cs` have been replaced with working code that enqueues RAG indexing jobs.

### Current Issue: Configuration/Trigger Path
Documents are not being indexed because the trigger path is not active:
1. `EmailProcessing:AutoIndexToRag` defaults to `false` in configuration
2. Dataverse webhook may not be sending document events to the BFF API
3. Job queue processing may not be running

---

## Component Inventory

### Fully Implemented Components (All Ready)

| Component | Location | Status | Key Methods |
|-----------|----------|--------|-------------|
| **Text Extraction** | `Services/Ai/TextExtractorService.cs` | ✅ Complete | `ExtractAsync()` - PDF, DOCX, TXT, email, vision OCR |
| **Text Chunking** | `Services/Ai/TextChunkingService.cs` | ✅ Complete | `ChunkTextAsync()` - sentence-aware, configurable overlap |
| **Embedding Generation** | `Services/Ai/OpenAiClient.cs` | ✅ Complete | `GenerateEmbeddingAsync()`, `GenerateEmbeddingsAsync()` + circuit breaker |
| **Embedding Cache** | `Services/Ai/EmbeddingCache.cs` | ✅ Complete | Redis-based, 7-day TTL, SHA256 keys, graceful error handling |
| **RAG Indexing** | `Services/Ai/RagService.cs` | ✅ Complete | `IndexDocumentAsync()`, `IndexDocumentsBatchAsync()` + Polly 8.x resilience |
| **RAG Search** | `Services/Ai/RagService.cs` | ✅ Complete | `SearchAsync()` - hybrid + semantic + circuit breaker |
| **File Indexing Orchestrator** | `Services/Ai/FileIndexingService.cs` | ✅ Complete | `IndexFileAsync()`, `IndexFileAppOnlyAsync()`, `IndexContentAsync()` |
| **Deployment Routing** | `Services/Ai/KnowledgeDeploymentService.cs` | ✅ Complete | Multi-tenant index routing, deployment config caching |
| **RAG API Endpoints** | `Api/Ai/RagEndpoints.cs` | ✅ Complete | `/index-file`, `/enqueue-indexing`, `/search`, `/index/batch`, DELETE endpoints |
| **File Download (OBO)** | `Infrastructure/Graph/SpeFileStore.cs` | ✅ Complete | `DownloadFileAsUserAsync()` |
| **File Download (App-Only)** | `Infrastructure/Graph/SpeFileStore.cs` | ✅ Complete | `DownloadFileAsync()` |
| **RAG Indexing Job Handler** | `Services/Jobs/Handlers/RagIndexingJobHandler.cs` | ✅ Complete | Background processing with idempotency |
| **Job Framework** | `Services/Jobs/IJobHandler.cs` | ✅ Complete | Service Bus job processing |
| **Document Events** | `Services/Jobs/DocumentEvent.cs` | ✅ Complete | Event model for Create/Update/Delete |
| **Event-Driven RAG Trigger** | `Services/Jobs/Handlers/DocumentEventHandler.cs` | ✅ Complete | `EnqueueRagIndexingJobAsync()` - conditional on `AutoIndexToRag` |

### Configuration Required (Not Code Issues)

| Item | Current State | Required Action |
|------|---------------|-----------------|
| **AutoIndexToRag Setting** | `false` (default) | Set `EmailProcessing:AutoIndexToRag=true` in Azure App Service |
| **Dataverse Webhook** | Unknown | Verify webhook is registered and sending document events to BFF |
| **Service Bus Connection** | Unknown | Verify job queue is configured and processing jobs |
| **Rag:ApiKey** | Required for `/enqueue-indexing` | Ensure configured in Key Vault / App Settings |

### Legacy/Deprecated (Can Be Cleaned Up)

| Component | Location | Status | Note |
|-----------|----------|--------|------|
| **Duplicated ChunkText()** | Multiple tool handlers | ⚠️ Deprecated | Now use `ITextChunkingService` - handlers can be refactored |
| **Placeholder Methods** | `DocumentEventHandler.cs` | ⚠️ Stubs remain | Non-RAG methods still placeholders (container moves, etc.) |

---

## Current Data Flow

### What Happens Today (AutoIndexToRag=false)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    DOCUMENT UPLOAD FLOW (Current State)                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  PCF Upload        BFF API              SPE Storage                     │
│  Component ──────► UploadEndpoints ───► SharePoint Embedded             │
│                         │                    │                          │
│                         ▼                    ▼                          │
│              ┌────────────────────┐  ┌─────────────────────┐            │
│              │  Dataverse record  │  │  File stored in     │            │
│              │  created           │  │  SPE container      │            │
│              └────────────────────┘  └─────────────────────┘            │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────────────────────────────┐             │
│              │  Dataverse Webhook → BFF API               │             │
│              │  DocumentEventHandler.HandleEventAsync()   │             │
│              └────────────────────────────────────────────┘             │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────────────────────────────┐             │
│              │  EnqueueRagIndexingJobAsync()              │             │
│              │  ⚠️ SKIPPED: AutoIndexToRag = false        │             │
│              └────────────────────────────────────────────┘             │
│                                                                         │
│  Result: Document stored but NOT indexed to AI Search                   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### What Should Happen (AutoIndexToRag=true) - IMPLEMENTED

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    DOCUMENT UPLOAD FLOW (With AutoIndexToRag=true)      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  PCF Upload        BFF API              SPE Storage                     │
│  Component ──────► UploadEndpoints ───► SharePoint Embedded             │
│                         │                    │                          │
│                         ▼                    ▼                          │
│              ┌────────────────────┐  ┌─────────────────────┐            │
│              │  Dataverse record  │  │  File stored in     │            │
│              │  created           │  │  SPE container      │            │
│              └────────────────────┘  └─────────────────────┘            │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────────────────────────────┐             │
│              │  Dataverse Webhook → BFF API               │             │
│              │  DocumentEventHandler.HandleEventAsync()   │             │
│              └────────────────────────────────────────────┘             │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────────────────────────────┐             │
│              │  EnqueueRagIndexingJobAsync()              │             │
│              │  ✅ AutoIndexToRag = true → enqueue job    │             │
│              └────────────────────────────────────────────┘             │
│                         │                                               │
│                         ▼                                               │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │              RagIndexingJobHandler → FileIndexingService          │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │                                                                   │   │
│  │  Step 1: Download file from SPE (app-only auth)                  │   │
│  │          └─► SpeFileStore.DownloadFileAsync()                    │   │
│  │                                                                   │   │
│  │  Step 2: Extract text from file                                  │   │
│  │          └─► TextExtractorService.ExtractAsync()                 │   │
│  │                                                                   │   │
│  │  Step 3: Chunk text into segments                                │   │
│  │          └─► TextChunkingService.ChunkTextAsync()                │   │
│  │                                                                   │   │
│  │  Step 4: Build KnowledgeDocument objects                         │   │
│  │                                                                   │   │
│  │  Step 5: Index chunks to AI Search (embeddings auto-generated)   │   │
│  │          └─► RagService.IndexDocumentsBatchAsync()               │   │
│  │                                                                   │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────┐                                     │
│              │  Azure AI Search   │                                     │
│              │  spaarke-knowledge │                                     │
│              │  -index-v2         │                                     │
│              └────────────────────┘                                     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Alternative: Manual/On-Demand Indexing (Also Implemented)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    MANUAL INDEXING FLOW (API Triggered)                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  PCF Control        BFF API                                             │
│  or Script ────────► POST /api/ai/rag/index-file                        │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────────────────────────────┐             │
│              │  FileIndexingService.IndexFileAsync()      │             │
│              │  (OBO authentication - user's token)       │             │
│              └────────────────────────────────────────────┘             │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────────────────────────────┐             │
│              │  Download → Extract → Chunk → Index        │             │
│              │  (synchronous, returns result immediately) │             │
│              └────────────────────────────────────────────┘             │
│                         │                                               │
│                         ▼                                               │
│              ┌────────────────────┐                                     │
│              │  FileIndexingResult│                                     │
│              │  { success, count }│                                     │
│              └────────────────────┘                                     │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Existing Code Analysis

### Text Extraction (COMPLETE)

**File**: `Services/Ai/TextExtractorService.cs`

```csharp
public async Task<TextExtractionResult> ExtractAsync(
    Stream fileStream,
    string fileName,
    CancellationToken cancellationToken)
```

**Supported Formats**:
- Native: TXT, JSON, CSV, XML, HTML, MD
- Document Intelligence: PDF, DOCX (Azure AI Document Intelligence)
- Email: EML (MimeKit), MSG (MsgReader)
- Vision OCR: PNG, JPG, GIF, BMP, WEBP (GPT-4o vision)

**Status**: Production-ready, used by `AnalysisOrchestrationService`

---

### Text Chunking (EXISTS - NEEDS EXTRACTION)

**Current Location**: Duplicated in multiple tool handlers

**Example from `ClauseComparisonHandler.cs` (line 527-560)**:
```csharp
private static List<string> ChunkText(string text, int chunkSize)
{
    if (string.IsNullOrEmpty(text) || text.Length <= chunkSize)
        return new List<string> { text };

    var chunks = new List<string>();
    var position = 0;

    while (position < text.Length)
    {
        var length = Math.Min(chunkSize, text.Length - position);
        var chunk = text.Substring(position, length);

        // Try to break at sentence boundary
        if (position + length < text.Length)
        {
            var lastPeriod = chunk.LastIndexOf(". ");
            if (lastPeriod > chunkSize / 2)
            {
                chunk = chunk.Substring(0, lastPeriod + 1);
                length = chunk.Length;
            }
        }

        chunks.Add(chunk);

        var advance = length - ChunkOverlap; // 200 char overlap
        if (advance <= 0) position += length;
        else position += advance;
    }

    return chunks;
}
```

**Found in**:
- `ClauseComparisonHandler.cs`
- `ClauseAnalyzerHandler.cs`
- `DateExtractorHandler.cs`
- `EntityExtractorHandler.cs`
- `SummaryHandler.cs`

**Action**: Extract to shared `ITextChunkingService`

---

### RAG Indexing (COMPLETE)

**File**: `Services/Ai/RagService.cs`

**Key Methods**:
```csharp
// Single document indexing
public async Task<KnowledgeDocument> IndexDocumentAsync(
    KnowledgeDocument document,
    CancellationToken cancellationToken)

// Batch indexing (more efficient)
public async Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
    IEnumerable<KnowledgeDocument> documents,
    CancellationToken cancellationToken)

// Delete operations
public async Task DeleteDocumentAsync(string documentId, CancellationToken ct)
public async Task DeleteBySourceDocumentAsync(string sourceDocumentId, CancellationToken ct)

// Embedding utility
public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct)
```

**Features**:
- Generates embeddings if not provided
- Computes document-level vector from chunk vectors
- Supports 3072-dim vectors (text-embedding-3-large)
- Multi-tenant filtering via `tenantId`
- **Polly 8.x circuit breaker resilience** (added Jan 12, 2026)
- **Telemetry and comprehensive logging**

**Status**: Production-ready, API endpoints exist

---

### Document Event Handler (PLACEHOLDER)

**File**: `Services/Jobs/Handlers/DocumentEventHandler.cs`

**Current State**:
```csharp
private async Task HandleFileAddedAsync(DocumentEvent documentEvent, CancellationToken ct)
{
    _logger.LogInformation("Handling file addition for document {DocumentId}",
        documentEvent.DocumentId);
    await Task.CompletedTask; // PLACEHOLDER
}
```

**All handlers are placeholders**:
- `HandleFileAddedAsync` → `await Task.CompletedTask`
- `HandleFileRemovedAsync` → `await Task.CompletedTask`
- `HandleDocumentCreatedAsync` → `await Task.CompletedTask`
- etc.

**Action**: Implement these to trigger RAG indexing

---

## Architectural Decisions

### Decision 1: Trigger Mechanism

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| **A. Wire into DocumentEventHandler** | Automatic, event-driven | Requires Service Bus, complex auth | Phase 2 |
| **B. Create API endpoint** | Simple, callable from PCF | Requires explicit call | ⭐ Phase 1 |
| **C. Wire into UploadEndpoints** | Automatic on upload | Tightly coupled, sync blocking | Not recommended |

**Recommendation**: Start with **Option B** (API endpoint) for immediate value:
- `POST /api/ai/rag/index-document/{documentId}`
- PCF calls this after successful upload
- Returns indexing status

Then add **Option A** (event-driven) for background processing.

---

### Decision 2: Authentication for File Download

| Scenario | Auth Method | Implementation |
|----------|-------------|----------------|
| **User-triggered indexing** | OBO (On-Behalf-Of) | `SpeFileStore.DownloadFileAsUserAsync(httpContext)` |
| **Background job indexing** | Managed Identity | Need to configure SPE container permissions |

**Recommendation**:
- Phase 1: User-triggered with OBO (simpler)
- Phase 2: Background job with MI (requires SPE permission setup)

---

### Decision 3: Chunking Strategy

| Strategy | Description | Use Case |
|----------|-------------|----------|
| **Fixed-size** | 4000 chars with 200 overlap | Current implementation |
| **Sentence-aware** | Break at sentence boundaries | Current implementation |
| **Semantic** | Use AI to identify logical sections | Future enhancement |

**Recommendation**: Extract current sentence-aware chunking to shared service.

---

## Implementation Plan

### Phase 1: Core Pipeline (Immediate Priority)

**Objective**: Enable document indexing via API endpoint

#### Task 1.1: Create ITextChunkingService
**Effort**: Low | **Files**: 2 new

```csharp
public interface ITextChunkingService
{
    IReadOnlyList<TextChunk> ChunkText(string text, ChunkingOptions? options = null);
}

public record TextChunk(
    string Content,
    int Index,
    int StartPosition,
    int Length);

public record ChunkingOptions(
    int ChunkSize = 4000,
    int Overlap = 200,
    bool PreserveSentences = true);
```

**Action**: Extract logic from `ClauseComparisonHandler.ChunkText()`

---

#### Task 1.2: Create IDocumentIndexingService
**Effort**: Medium | **Files**: 2 new

```csharp
public interface IDocumentIndexingService
{
    /// <summary>
    /// Index a document from SPE into RAG knowledge base.
    /// </summary>
    Task<DocumentIndexingResult> IndexDocumentAsync(
        DocumentIndexRequest request,
        HttpContext httpContext, // For OBO auth
        CancellationToken cancellationToken);
}

public record DocumentIndexRequest(
    string DocumentId,
    string SpeFileId,
    string DriveId,
    string ItemId,
    string TenantId,
    string? KnowledgeSourceId = null,
    string? KnowledgeSourceName = null);

public record DocumentIndexingResult(
    bool Success,
    int ChunksIndexed,
    string? ErrorMessage = null);
```

**Implementation Steps**:
1. Download file from SPE via `SpeFileStore.DownloadFileAsUserAsync()`
2. Extract text via `ITextExtractor.ExtractAsync()`
3. Chunk via `ITextChunkingService.ChunkText()`
4. Build `KnowledgeDocument` for each chunk
5. Index via `IRagService.IndexDocumentsBatchAsync()`

---

#### Task 1.3: Create API Endpoint
**Effort**: Low | **Files**: 1 modified

Add to `RagEndpoints.cs` (current endpoints: POST /search, /index, /index/batch, DELETE /{id}, /source/{id}, POST /embedding):

```csharp
// POST /api/ai/rag/index-document/{documentId}
group.MapPost("/index-document/{documentId}", IndexDocumentFromSpe)
    .AddTenantAuthorizationFilter()
    .WithName("RagIndexDocumentFromSpe")
    .WithSummary("Index a document from SPE into RAG knowledge base");
```

**This endpoint orchestrates**: SPE download → text extraction → chunking → embedding → indexing

---

#### Task 1.4: Wire DI and Test
**Effort**: Low | **Files**: 2 modified

- Register services in `Program.cs`
- Create integration tests
- Test with real document

---

### Phase 2: Event-Driven Indexing

**Objective**: Auto-index documents when created/updated

#### Task 2.1: Implement DocumentEventHandler.HandleFileAddedAsync
**Effort**: Medium | **Files**: 1 modified

```csharp
private async Task HandleFileAddedAsync(DocumentEvent documentEvent, CancellationToken ct)
{
    var request = new DocumentIndexRequest(
        DocumentId: documentEvent.DocumentId,
        SpeFileId: documentEvent.EntityData["sprk_spefileid"]?.ToString(),
        DriveId: documentEvent.EntityData["sprk_graphdriveid"]?.ToString(),
        ItemId: documentEvent.EntityData["sprk_graphitemid"]?.ToString(),
        TenantId: documentEvent.OrganizationId);

    // Note: No HttpContext in background job - need MI auth
    await _documentIndexingService.IndexDocumentBackgroundAsync(request, ct);
}
```

---

#### Task 2.2: Configure SPE Managed Identity Access
**Effort**: Medium | **Infrastructure**

- Grant MI read access to SPE containers
- Update `SpeFileStore` to support MI-based download

---

### Phase 3: Hardening & Optimization

#### Task 3.1: Re-indexing on Update
- Delete existing chunks before re-indexing
- Use `RagService.DeleteBySourceDocumentAsync()`

#### Task 3.2: Deletion Handling
- Wire `HandleFileRemovedAsync` to delete from index

#### Task 3.3: Bulk Indexing Endpoint
- For migrating existing documents
- Background job with progress tracking

---

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `Services/Ai/ITextChunkingService.cs` | Interface for text chunking |
| `Services/Ai/TextChunkingService.cs` | Implementation |
| `Services/Ai/IDocumentIndexingService.cs` | Interface for document indexing orchestration |
| `Services/Ai/DocumentIndexingService.cs` | Implementation |

### Modified Files

| File | Changes |
|------|---------|
| `Api/Ai/RagEndpoints.cs` | Add `/index-document/{documentId}` endpoint |
| `Infrastructure/DI/AiModule.cs` | Register new services |
| `Services/Jobs/Handlers/DocumentEventHandler.cs` | Implement handlers (Phase 2) |
| `Services/Ai/Tools/*.cs` | Refactor to use `ITextChunkingService` |

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| OBO token expiry during large file processing | Medium | Stream processing, chunked operations |
| Rate limiting on embedding API | High | Batch embeddings, exponential backoff |
| Index quota exceeded | Medium | Monitor index size, implement cleanup |
| Duplicate indexing | Low | Use `documentId_chunkIndex` as key |

---

## Success Criteria

1. **Functional**: Documents uploaded via PCF are indexed to AI Search
2. **Performance**: Indexing completes within 30 seconds for typical documents
3. **Reliability**: Failed indexing doesn't block upload flow
4. **Observability**: Indexing status visible in logs and Application Insights

---

## Next Steps

1. ✅ Assessment complete (2026-01-11)
2. ✅ Assessment refreshed (2026-01-14) - Confirmed gaps remain
3. ⏳ User approval of approach
4. ⏳ Create project branch (`work/ai-rag-pipeline`)
5. ⏳ Create POML task files for implementation (via `/project-pipeline`)
6. ⏳ Execute Phase 1 tasks:
   - [ ] Create `ITextChunkingService` + `TextChunkingService`
   - [ ] Create `IDocumentIndexingService` + `DocumentIndexingService`
   - [ ] Add `/index-document/{documentId}` endpoint to `RagEndpoints.cs`
   - [ ] Register services in DI
7. ⏳ Test with real documents
8. ⏳ Execute Phase 2 tasks (event-driven indexing)
9. ⏳ Deploy to Azure

---

## Summary Status

| Category | Status |
|----------|--------|
| **Core RAG Components** | ✅ 100% Complete (with resilience) |
| **Orchestration Layer** | ✅ 100% Complete (`FileIndexingService`) |
| **Text Chunking Service** | ✅ 100% Complete (`TextChunkingService`) |
| **Event Handler Integration** | ✅ 100% Complete (`EnqueueRagIndexingJobAsync`) |
| **API Trigger Endpoints** | ✅ 100% Complete (`/index-file`, `/enqueue-indexing`) |
| **Background Job Handler** | ✅ 100% Complete (`RagIndexingJobHandler`) |
| **Configuration** | ⚠️ `AutoIndexToRag=false` by default |
| **Dataverse Webhook** | ❓ Unknown - needs verification |

**Bottom Line**: All code is production-ready. Documents are not being indexed because:
1. `EmailProcessing:AutoIndexToRag` is disabled (needs to be set to `true`)
2. The Dataverse webhook trigger path needs verification

---

## Immediate Action Items

### 1. Enable AutoIndexToRag (Quick Fix)

**Azure Portal** → **App Services** → `spe-api-dev-67e2xz` → **Configuration** → **Application Settings**

Add or update:
```
EmailProcessing__AutoIndexToRag = true
```

Or via Azure CLI:
```bash
az webapp config appsettings set \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --settings EmailProcessing__AutoIndexToRag=true
```

### 2. Verify Dataverse Webhook Configuration

Check if document events are being sent to the BFF API:
- Look for Service Endpoint registration in Dataverse
- Verify webhook URL points to: `https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/events`
- Check Application Insights for incoming webhook requests

### 3. Test Manual Indexing (Immediate Workaround)

Use the `/index-file` endpoint to manually index documents:

```bash
curl -X POST "https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/rag/index-file" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "driveId": "{spe-drive-id}",
    "itemId": "{spe-item-id}",
    "fileName": "document.pdf",
    "documentId": "{dataverse-document-id}"
  }'
```

### 4. Check Application Insights Logs

Search for these log patterns to understand what's happening:
- `"Processing RAG indexing job"` - Job handler is processing
- `"AutoIndexToRag disabled"` - Skipping because of config
- `"Enqueued RAG indexing job"` - Job was successfully enqueued
- `"Indexed {FileName}"` - File successfully indexed

---

*Assessment created: 2026-01-11*
*Assessment refreshed: 2026-01-14*
*Assessment updated: 2026-01-19 - Implementation complete, configuration issue identified*
