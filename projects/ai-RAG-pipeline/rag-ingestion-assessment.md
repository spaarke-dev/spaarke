# RAG Document Ingestion Pipeline - Comprehensive Assessment

> **Date**: 2026-01-14 (Updated from 2026-01-11)
> **Status**: Assessment Refreshed - Implementation Gaps Remain
> **Author**: AI Analysis
> **Related Issue**: Documents not being indexed to AI Search on upload
> **Last Code Review**: 2026-01-14

---

## Executive Summary

The Spaarke platform has **all core RAG components implemented** but lacks the **orchestration layer** that connects document upload to RAG indexing. The visualization control's 500 error occurs because documents are stored in SPE (SharePoint Embedded) but never indexed into Azure AI Search.

**Root Cause**: No trigger mechanism exists to invoke the indexing pipeline when documents are uploaded or created.

**Solution Path**: Create a `DocumentIndexingService` that orchestrates existing components, and wire it to document lifecycle events.

---

## Recent Updates (Since 2026-01-11)

### Code Changes (Jan 12, 2026)
| File | Change |
|------|--------|
| `RagService.cs` | Added Polly 8.x circuit breaker resilience (Task 072) |
| `OpenAiClient.cs` | Added circuit breaker for embedding API calls |
| `IRagService.cs` | Interface updated to support resilience patterns |
| `IOpenAiClient.cs` | Interface updated for circuit breaker support |
| `KnowledgeDeploymentService.cs` | Deployment config caching improvements |

### Implementation Status (Unchanged)
The core **orchestration gap remains**: no `IDocumentIndexingService` has been created to connect document upload to RAG indexing. All placeholder implementations in `DocumentEventHandler.cs` are still `await Task.CompletedTask`.

---

## Component Inventory

### Fully Implemented Components (Ready to Use)

| Component | Location | Status | Key Methods |
|-----------|----------|--------|-------------|
| **Text Extraction** | `Services/Ai/TextExtractorService.cs` | ✅ Complete | `ExtractAsync()` - PDF, DOCX, TXT, email, vision OCR |
| **Embedding Generation** | `Services/Ai/OpenAiClient.cs` | ✅ Complete | `GenerateEmbeddingAsync()`, `GenerateEmbeddingsAsync()` + circuit breaker |
| **Embedding Cache** | `Services/Ai/EmbeddingCache.cs` | ✅ Complete | Redis-based, 7-day TTL, SHA256 keys, graceful error handling |
| **RAG Indexing** | `Services/Ai/RagService.cs` | ✅ Complete | `IndexDocumentAsync()`, `IndexDocumentsBatchAsync()` + Polly 8.x resilience |
| **RAG Search** | `Services/Ai/RagService.cs` | ✅ Complete | `SearchAsync()` - hybrid + semantic + circuit breaker |
| **Deployment Routing** | `Services/Ai/KnowledgeDeploymentService.cs` | ✅ Complete | Multi-tenant index routing, deployment config caching |
| **RAG API Endpoints** | `Api/Ai/RagEndpoints.cs` | ✅ Complete | POST /index, /index/batch, /search, DELETE /{id}, /source/{id} |
| **File Download (OBO)** | `Infrastructure/Graph/SpeFileStore.cs` | ✅ Complete | `DownloadFileAsUserAsync()` |
| **Job Framework** | `Services/Jobs/IJobHandler.cs` | ✅ Complete | Service Bus job processing |
| **Document Events** | `Services/Jobs/DocumentEvent.cs` | ✅ Complete | Event model for Create/Update/Delete |

### Partially Implemented (Needs Work)

| Component | Location | Status | Issue |
|-----------|----------|--------|-------|
| **Text Chunking** | Multiple tool handlers | ⚠️ Duplicated | Same `ChunkText()` in 5+ handlers (ClauseComparisonHandler, ClauseAnalyzerHandler, DateExtractorHandler, EntityExtractorHandler, SummaryHandler) |
| **Document Event Handler** | `Services/Jobs/Handlers/DocumentEventHandler.cs` | ⚠️ Placeholder | All critical methods are `await Task.CompletedTask` stubs |
| **Document Processing Job** | `Services/Jobs/Handlers/DocumentProcessingJobHandler.cs` | ⚠️ Sample | Placeholder implementation |

**DocumentEventHandler Placeholder Methods** (all stubs):
- `InitializeDocumentForFileOperationsAsync()`
- `ProcessInitialFileUploadAsync()`
- `SyncDocumentNameToSpeAsync()`
- `HandleContainerChangeAsync()`
- `HandleFileStatusChangeAsync()`
- `HandleFileAddedAsync()` ← **Critical for RAG ingestion trigger**
- `HandleFileRemovedAsync()`
- `DeleteAssociatedFileAsync()`
- All status-specific handlers (`HandleDocumentActivationAsync()`, etc.)

### Missing Components

| Component | Purpose | Priority |
|-----------|---------|----------|
| **IDocumentIndexingService** | Orchestrate download → extract → chunk → embed → index | HIGH |
| **ITextChunkingService** | Shared chunking logic (extract from tool handlers) | MEDIUM |
| **Trigger Integration** | Wire upload/create events to indexing | HIGH |

---

## Current Data Flow

### What Happens Today (Broken)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        DOCUMENT UPLOAD FLOW                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  PCF Upload        BFF API              SPE Storage                     │
│  Component ──────► UploadEndpoints ───► SharePoint Embedded             │
│                                              │                          │
│                                              ▼                          │
│                    ┌─────────────────────────────────────────┐          │
│                    │  ✅ File stored in SPE container        │          │
│                    │  ✅ Dataverse record created             │          │
│                    │  ❌ NO RAG INDEXING TRIGGERED            │          │
│                    └─────────────────────────────────────────┘          │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                    VISUALIZATION REQUEST (FAILS)                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  PCF Control       BFF API              Azure AI Search                 │
│  Viewer ─────────► /api/ai/visualization ──► Query Index                │
│                         │                         │                     │
│                         │                         ▼                     │
│                         │              ┌─────────────────────┐          │
│                         │              │  Document NOT found  │          │
│                         │              │  (never indexed)     │          │
│                         │              └─────────────────────┘          │
│                         │                         │                     │
│                         ◄─────────────────────────┘                     │
│                         │                                               │
│                    500 Internal Server Error                            │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### What Should Happen (Target State)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        DOCUMENT UPLOAD FLOW (FIXED)                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  PCF Upload        BFF API              SPE Storage                     │
│  Component ──────► UploadEndpoints ───► SharePoint Embedded             │
│                         │                    │                          │
│                         │                    ▼                          │
│                         │         ┌─────────────────────┐               │
│                         │         │  File stored in SPE │               │
│                         │         └─────────────────────┘               │
│                         │                    │                          │
│                         ▼                    │                          │
│              ┌────────────────────┐          │                          │
│              │  Trigger Indexing  │◄─────────┘                          │
│              │  (Event or API)    │                                     │
│              └────────────────────┘                                     │
│                         │                                               │
│                         ▼                                               │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │              DocumentIndexingService (NEW)                        │   │
│  ├──────────────────────────────────────────────────────────────────┤   │
│  │                                                                   │   │
│  │  Step 1: Download file from SPE                                  │   │
│  │          └─► SpeFileStore.DownloadFileAsUserAsync() [EXISTING]   │   │
│  │                                                                   │   │
│  │  Step 2: Extract text from file                                  │   │
│  │          └─► TextExtractorService.ExtractAsync() [EXISTING]      │   │
│  │                                                                   │   │
│  │  Step 3: Chunk text into segments                                │   │
│  │          └─► TextChunkingService.ChunkText() [NEW - extract]     │   │
│  │                                                                   │   │
│  │  Step 4: Generate embeddings (batch)                             │   │
│  │          └─► OpenAiClient.GenerateEmbeddingsAsync() [EXISTING]   │   │
│  │                                                                   │   │
│  │  Step 5: Index chunks to AI Search                               │   │
│  │          └─► RagService.IndexDocumentsBatchAsync() [EXISTING]    │   │
│  │                                                                   │   │
│  │  Step 6: Update Dataverse record status                          │   │
│  │          └─► DataverseService.UpdateDocumentAsync() [EXISTING]   │   │
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
| **Orchestration Layer** | ❌ 0% Complete (blocking gap) |
| **Text Chunking Service** | ⚠️ Logic exists, needs extraction |
| **Event Handler Integration** | ⚠️ Structure exists, needs implementation |
| **API Trigger Endpoint** | ❌ Not yet created |

**Bottom Line**: All building blocks are production-ready. The project needs the `DocumentIndexingService` orchestrator and `/index-document/{documentId}` endpoint to close the gap.

---

*Assessment created: 2026-01-11*
*Assessment refreshed: 2026-01-14*
