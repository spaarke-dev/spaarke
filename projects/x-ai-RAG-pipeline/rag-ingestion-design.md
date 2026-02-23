# RAG Document Ingestion - Design Document

> **Date**: 2026-01-12
> **Status**: READY FOR IMPLEMENTATION
> **Author**: AI Analysis + Human Review
> **Component Inventory**: VERIFIED - No duplicates found

---

## Executive Summary

This document defines the design for wiring document ingestion into the RAG (Retrieval-Augmented Generation) knowledge index. The solution extends the existing BFF API to provide a simple, direct pipeline from file upload to RAG indexing.

**Key Principle**: Clear separation between **writing to RAG** (this feature) and **reading from RAG** (existing playbook/analysis system).

---

## Architecture Overview

### Two Distinct Flows

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     WRITE PATH (This Feature)                           │
│                     ─────────────────────────                           │
│                                                                         │
│   File Upload → SPE Storage → Index to RAG                              │
│                                                                         │
│   ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────────────┐ │
│   │  Upload  │───►│   SPE    │───►│  Extract │───►│  Azure AI Search │ │
│   │  File    │    │  Storage │    │  + Index │    │  (RAG Index)     │ │
│   └──────────┘    └──────────┘    └──────────┘    └──────────────────┘ │
│                                                                         │
│   - Simple, direct pipeline                                             │
│   - No playbooks involved                                               │
│   - Works for Documents AND orphan files                                │
│   - Single API endpoint                                                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                     READ PATH (Existing System)                         │
│                     ───────────────────────────                         │
│                                                                         │
│   Analysis Request → Query RAG → Augment LLM Prompt → Generate Response │
│                                                                         │
│   ┌──────────┐    ┌──────────────────┐    ┌──────────┐    ┌──────────┐ │
│   │ Playbook │───►│ Knowledge Source │───►│   LLM    │───►│ Analysis │ │
│   │ Execution│    │ (RAG Query)      │    │  Prompt  │    │  Output  │ │
│   └──────────┘    └──────────────────┘    └──────────┘    └──────────┘ │
│                                                                         │
│   - Playbooks CONSUME RAG (read only)                                   │
│   - KnowledgeType.RagIndex queries the index                            │
│   - Provides context to LLM for analysis                                │
│   - Playbooks do NOT write to RAG                                       │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Separation of Concerns

| Concern | Write Path | Read Path |
|---------|------------|-----------|
| **Purpose** | Populate RAG index | Use RAG for analysis |
| **Trigger** | File upload | Analysis/playbook execution |
| **Components** | TextExtractor → Chunking → RagService | ProcessRagKnowledgeAsync → LLM |
| **Playbook involvement** | None | Yes - Knowledge sources |
| **LLM calls** | No (only embeddings) | Yes (completions) |

---

## Comprehensive Component Inventory

> **Purpose**: This section documents ALL existing components with their exact locations, interfaces, and capabilities to ensure NO duplicate or parallel components are created during implementation.

### 1. RagService (REUSE - No Changes)

**Location**: `Services/Ai/RagService.cs` (784 lines)
**Interface**: `Services/Ai/IRagService.cs`

**Capabilities**:
```csharp
// Already implemented - USE THESE METHODS:
Task<KnowledgeDocument> IndexDocumentAsync(KnowledgeDocument document, CancellationToken ct);
Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct);
Task<RagSearchResponse> SearchAsync(string query, RagSearchOptions options, CancellationToken ct);
Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string text, CancellationToken ct);
Task<bool> DeleteDocumentAsync(string documentId, string tenantId, CancellationToken ct);
Task<int> DeleteBySourceDocumentAsync(string sourceDocumentId, string tenantId, CancellationToken ct);
```

**Key Features Already Implemented**:
- ✅ Generates embeddings for documents without vectors (line 280-284)
- ✅ Computes `documentVector` by averaging chunk `contentVector`s (line 374-404)
- ✅ Handles orphan files (groups by `DocumentId ?? SpeFileId`) (line 377-378)
- ✅ Batch indexing up to 1000 docs per batch (line 421-437)
- ✅ Populates `fileType` from `fileName` automatically (line 689-718)
- ✅ Multi-tenant index routing via `KnowledgeDeploymentService`
- ✅ Circuit breaker and resilience patterns via `IResilientSearchClient`

**Confirmation**: No new indexing service needed - `IndexDocumentsBatchAsync` does everything.

---

### 2. TextExtractorService (REUSE - No Changes)

**Location**: `Services/Ai/TextExtractorService.cs` (628 lines)
**Interface**: `Services/Ai/ITextExtractor.cs`

**Capabilities**:
```csharp
Task<TextExtractionResult> ExtractAsync(Stream fileStream, string fileName, CancellationToken ct);
bool IsSupported(string extension);
ExtractionMethod? GetMethod(string extension);
```

**Supported Formats**:
| Format | Method | Notes |
|--------|--------|-------|
| PDF, DOCX, DOC, XLSX, XLS, PPTX, PPT | Document Intelligence | Azure AI prebuilt-read model |
| TXT, MD, JSON, CSV, XML, HTML | Native | Direct StreamReader with BOM detection |
| EML | Email (MimeKit) | Full email metadata extraction |
| MSG | Email (MsgReader) | Outlook format support |
| Images (PNG, JPG, etc.) | VisionOCR | Returns `RequiresVision()` for vision model |

**Confirmation**: Fully production-ready, no modifications needed.

---

### 3. EmbeddingCache (REUSE - No Changes)

**Location**: `Services/Ai/EmbeddingCache.cs` (197 lines)
**Interface**: `Services/Ai/IEmbeddingCache.cs`

**Capabilities**:
```csharp
string ComputeContentHash(string content);  // SHA256 hash
Task<ReadOnlyMemory<float>?> GetEmbeddingAsync(string contentHash, CancellationToken ct);
Task SetEmbeddingAsync(string contentHash, ReadOnlyMemory<float> embedding, CancellationToken ct);
Task<ReadOnlyMemory<float>?> GetEmbeddingForContentAsync(string content, CancellationToken ct);
Task SetEmbeddingForContentAsync(string content, ReadOnlyMemory<float> embedding, CancellationToken ct);
```

**Key Features**:
- ✅ Redis-based (IDistributedCache) following ADR-009
- ✅ SHA256 content hashing for cache keys
- ✅ 7-day TTL (embeddings are deterministic for same model)
- ✅ Graceful error handling (cache failures don't break embedding generation)
- ✅ OpenTelemetry metrics via CacheMetrics

**Note**: RagService already uses EmbeddingCache internally - no direct usage needed.

---

### 4. OpenAiClient (REUSE - No Changes)

**Location**: `Services/Ai/OpenAiClient.cs`
**Interface**: `Services/Ai/IOpenAiClient.cs`

**Capabilities**:
```csharp
Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken ct);
Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken ct);
Task<string> GetCompletionAsync(string prompt, CancellationToken ct);
IAsyncEnumerable<string> GetCompletionStreamingAsync(string prompt, CancellationToken ct);
```

**Key Features**:
- ✅ text-embedding-3-large (3072 dimensions) by default
- ✅ Batch embedding generation
- ✅ Circuit breaker pattern (`OpenAiCircuitBrokenException`)

**Note**: RagService calls OpenAiClient internally - no direct usage needed for indexing.

---

### 5. SpeFileStore (REUSE - No Changes)

**Location**: `Infrastructure/Graph/SpeFileStore.cs`
**Interface**: `Infrastructure/Graph/ISpeFileOperations.cs`

**Capabilities**:
```csharp
// For user-context downloads (OBO authentication):
Task<Stream?> DownloadFileAsUserAsync(HttpContext ctx, string driveId, string itemId, CancellationToken ct);
Task<FileHandleDto?> GetFileMetadataAsUserAsync(HttpContext ctx, string driveId, string itemId, CancellationToken ct);

// For app-context downloads:
Task<Stream?> DownloadFileAsync(string driveId, string itemId, CancellationToken ct);
Task<FileHandleDto?> GetFileMetadataAsync(string driveId, string itemId, CancellationToken ct);
```

**Architecture**: Facade delegating to specialized operation classes:
- `DriveItemOperations` - File download/metadata
- `ContainerOperations` - SPE container management
- `UploadSessionManager` - File uploads
- `UserOperations` - User operations

**Confirmation**: Use `DownloadFileAsUserAsync` for OBO-authenticated file download.

---

### 6. KnowledgeDocument Model (REUSE - No Changes)

**Location**: `Models/Ai/KnowledgeDocument.cs` (185 lines)

**Fields Available**:
```csharp
string Id                    // {documentId}_{chunkIndex} or {itemId}_{chunkIndex}
string TenantId              // Multi-tenant isolation
string? DeploymentId         // For dedicated/customer-owned indexes
string? KnowledgeSourceId    // Source knowledge record
string? KnowledgeSourceName  // Display name
string? DocumentId           // Dataverse document ID (null for orphans)
string? SpeFileId            // SharePoint Embedded file ID (always populated)
string FileName              // File display name
string? FileType             // Extension (auto-populated by RagService)
int ChunkIndex               // Zero-based chunk index
int ChunkCount               // Total chunks for document
string Content               // Text content
ReadOnlyMemory<float> ContentVector    // 3072-dim chunk embedding
ReadOnlyMemory<float> DocumentVector   // 3072-dim document embedding (averaged)
string? Metadata             // JSON metadata
IList<string>? Tags          // Categorization tags
DateTimeOffset CreatedAt     // Indexed timestamp
DateTimeOffset UpdatedAt     // Last update timestamp
```

**Confirmation**: Model supports orphan files (`DocumentId = null`, `SpeFileId = required`).

---

### 7. KnowledgeDeploymentService (REUSE - No Changes)

**Location**: `Services/Ai/KnowledgeDeploymentService.cs`
**Interface**: `Services/Ai/IKnowledgeDeploymentService.cs`

**Capabilities**:
```csharp
Task<SearchClient> GetSearchClientAsync(string tenantId, CancellationToken ct);
Task<SearchClient> GetSearchClientByDeploymentAsync(Guid deploymentId, CancellationToken ct);
```

**Deployment Models**:
- `Shared` - Multi-tenant index filtered by tenantId (default)
- `Dedicated` - Per-tenant dedicated index
- `CustomerOwned` - Customer's own Azure subscription

**Note**: RagService uses this internally for index routing.

---

### 8. RagEndpoints (MODIFY - Add Endpoint)

**Location**: `Api/Ai/RagEndpoints.cs` (399 lines)

**Existing Endpoints**:
| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/ai/rag/search` | Hybrid search |
| POST | `/api/ai/rag/index` | Index single chunk |
| POST | `/api/ai/rag/index/batch` | Batch index chunks |
| DELETE | `/api/ai/rag/{documentId}` | Delete chunk |
| DELETE | `/api/ai/rag/source/{sourceDocumentId}` | Delete all chunks |
| POST | `/api/ai/rag/embedding` | Generate embedding |

**New Endpoint to Add**:
| Method | Route | Purpose |
|--------|-------|---------|
| POST | `/api/ai/rag/index-file` | **Full file indexing pipeline** |

---

### 9. Text Chunking (EXTRACT - Consolidate Duplicates)

**Current Locations** (duplicated code):
- `Services/Ai/Tools/SummaryHandler.cs` (line 479-517)
- `Services/Ai/Tools/ClauseComparisonHandler.cs`
- `Services/Ai/Tools/ClauseAnalyzerHandler.cs`
- `Services/Ai/Tools/EntityExtractorHandler.cs`
- `Services/Ai/Tools/DateExtractorHandler.cs`
- `Services/Ai/Tools/RiskDetectorHandler.cs`
- `Services/Ai/Tools/FinancialCalculatorHandler.cs`

**Existing Logic** (from SummaryHandler):
```csharp
private static List<string> ChunkText(string text, int chunkSize)
{
    // Sentence-boundary aware splitting
    // Breaks at ". " when possible
    // Uses ChunkOverlap = 200 characters
    // Default ChunkSize = 8000 (varies by handler)
}
```

**Action**: Extract to shared `ITextChunkingService` to eliminate duplication.

---

## Deprecation and Cleanup Plan

> **Purpose**: Explicitly document components to be deprecated and removed to avoid technical debt.

### Components to DEPRECATE and REMOVE

| File | Method | Action | Reason |
|------|--------|--------|--------|
| `Services/Ai/Tools/SummaryHandler.cs` | `ChunkText()` (line 479-517) | **REMOVE** | Replace with `ITextChunkingService` |
| `Services/Ai/Tools/ClauseComparisonHandler.cs` | `ChunkText()` | **REMOVE** | Replace with `ITextChunkingService` |
| `Services/Ai/Tools/ClauseAnalyzerHandler.cs` | `ChunkText()` | **REMOVE** | Replace with `ITextChunkingService` |
| `Services/Ai/Tools/EntityExtractorHandler.cs` | `ChunkText()` | **REMOVE** | Replace with `ITextChunkingService` |
| `Services/Ai/Tools/DateExtractorHandler.cs` | `ChunkText()` | **REMOVE** | Replace with `ITextChunkingService` |
| `Services/Ai/Tools/RiskDetectorHandler.cs` | `ChunkText()` | **REMOVE** | Replace with `ITextChunkingService` |
| `Services/Ai/Tools/FinancialCalculatorHandler.cs` | `ChunkText()` | **REMOVE** | Replace with `ITextChunkingService` |

### Refactoring Required for Tool Handlers

Each tool handler with `ChunkText()` must be refactored to:

1. **Inject** `ITextChunkingService` via constructor
2. **Replace** private `ChunkText()` calls with service calls
3. **Remove** private `ChunkText()` method and `ChunkOverlap` constant

**Before** (current duplicated pattern):
```csharp
public sealed class SummaryHandler : IAnalysisToolHandler
{
    private const int DefaultChunkSize = 8000;
    private const int ChunkOverlap = 200;

    // ... other dependencies ...

    public async Task<ToolResult> ExecuteAsync(...)
    {
        var chunks = ChunkText(documentText, DefaultChunkSize);
        // ...
    }

    private static List<string> ChunkText(string text, int chunkSize)
    {
        // 40+ lines of duplicated chunking logic
    }
}
```

**After** (using shared service):
```csharp
public sealed class SummaryHandler : IAnalysisToolHandler
{
    private readonly ITextChunkingService _chunkingService;
    // ... other dependencies ...

    public SummaryHandler(ITextChunkingService chunkingService, ...)
    {
        _chunkingService = chunkingService;
    }

    public async Task<ToolResult> ExecuteAsync(...)
    {
        var chunks = _chunkingService.ChunkText(documentText,
            new ChunkingOptions(ChunkSize: 8000, Overlap: 200));
        // ...
    }

    // NO private ChunkText() method - removed
}
```

### Cleanup Verification Checklist

After implementation, verify:
- [ ] No `private static List<string> ChunkText` methods remain in tool handlers
- [ ] No `ChunkOverlap` constants remain in tool handlers (moved to ChunkingOptions defaults)
- [ ] All 7 tool handlers inject `ITextChunkingService`
- [ ] All existing tool handler unit tests still pass
- [ ] No dead code left behind

### Why This Cleanup is Mandatory (Not Optional)

1. **DRY Principle**: 7 copies of the same 40+ line method violates Don't Repeat Yourself
2. **Bug Risk**: Fixing chunking bugs requires changes in 7 places
3. **Inconsistency Risk**: Each handler has slightly different chunk sizes (4000, 8000) - shared service makes this explicit
4. **Test Coverage**: Single shared service is easier to test comprehensively
5. **ADR Alignment**: Follows existing patterns of extracting shared functionality (e.g., `EmbeddingCache`, `TextExtractorService`)

---

## Duplication Analysis Summary

| Component | Status | Action |
|-----------|--------|--------|
| RagService | ✅ Complete | REUSE - Has all indexing capabilities |
| TextExtractorService | ✅ Complete | REUSE - Handles all file formats |
| EmbeddingCache | ✅ Complete | REUSE - Used by RagService internally |
| OpenAiClient | ✅ Complete | REUSE - Used by RagService internally |
| SpeFileStore | ✅ Complete | REUSE - Has OBO file download |
| KnowledgeDocument | ✅ Complete | REUSE - Supports orphans |
| KnowledgeDeploymentService | ✅ Complete | REUSE - Multi-tenant routing |
| RagEndpoints | Needs extension | ADD `/index-file` endpoint |
| Text Chunking | Duplicated | EXTRACT to shared service |

**Conclusion**: No parallel or duplicate components needed. Implementation requires:
1. **New**: `ITextChunkingService` (extract duplicated logic)
2. **New**: `IFileIndexingService` (orchestrate pipeline)
3. **Modify**: `RagEndpoints.cs` (add `/index-file` endpoint)

---

## New Components

### 1. ITextChunkingService

Extract existing chunking logic to shared service:

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
    bool PreserveSentenceBoundaries = true);
```

**File**: `Services/Ai/TextChunkingService.cs`

---

### 2. File Indexing Method

Add to existing service (AnalysisOrchestrationService or new lightweight service):

```csharp
public interface IFileIndexingService
{
    /// <summary>
    /// Index a file from SPE to the RAG knowledge index.
    /// Works for both Documents (with documentId) and orphan files (without).
    /// </summary>
    Task<FileIndexingResult> IndexFileAsync(
        FileIndexRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public record FileIndexRequest
{
    public required string DriveId { get; init; }
    public required string ItemId { get; init; }
    public required string FileName { get; init; }
    public required string TenantId { get; init; }
    public string? DocumentId { get; init; }           // null for orphan files
    public string? KnowledgeSourceId { get; init; }    // for future multi-index
    public string? KnowledgeSourceName { get; init; }  // display name
}

public record FileIndexingResult
{
    public bool Success { get; init; }
    public int ChunksIndexed { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
```

---

### 3. API Endpoint

```csharp
// POST /api/ai/rag/index-file
group.MapPost("/index-file", IndexFile)
    .AddTenantAuthorizationFilter()
    .RequireRateLimiting("ai-batch")
    .WithName("RagIndexFile")
    .WithSummary("Index a file from SPE to RAG knowledge base")
    .WithDescription("Extracts text, chunks, generates embeddings, and indexes to Azure AI Search.")
    .Produces<FileIndexingResult>()
    .ProducesProblem(400)
    .ProducesProblem(401)
    .ProducesProblem(500);
```

---

## Implementation Flow

### Index File Pipeline

```
POST /api/ai/rag/index-file
{
  "driveId": "b!abc...",
  "itemId": "01ABC...",
  "fileName": "contract.pdf",
  "tenantId": "a221a95e-...",
  "documentId": "ab07176b-...",      // optional
  "knowledgeSourceId": null          // optional, for future use
}
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  Step 1: Download File from SPE                               │
│  ─────────────────────────────                                │
│  await _speFileStore.DownloadFileAsUserAsync(                 │
│      httpContext, driveId, itemId, ct);                       │
│                                                               │
│  - Uses OBO (On-Behalf-Of) authentication                     │
│  - Returns file stream                                        │
└───────────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  Step 2: Extract Text                                         │
│  ────────────────────                                         │
│  var result = await _textExtractor.ExtractAsync(              │
│      stream, fileName, ct);                                   │
│                                                               │
│  - PDF/DOCX via Azure Document Intelligence                   │
│  - Native text formats (TXT, JSON, CSV, XML, HTML, MD)        │
│  - Email formats (EML, MSG)                                   │
│  - Returns extracted text string                              │
└───────────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  Step 3: Chunk Text                                           │
│  ───────────────────                                          │
│  var chunks = _chunkingService.ChunkText(result.Text,         │
│      new ChunkingOptions(ChunkSize: 4000, Overlap: 200));     │
│                                                               │
│  - Sentence-boundary aware splitting                          │
│  - Returns list of TextChunk with index and position          │
└───────────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  Step 4: Build Knowledge Documents                            │
│  ─────────────────────────────────                            │
│  var documents = chunks.Select((chunk, i) => new KnowledgeDoc │
│  {                                                            │
│      Id = $"{documentId ?? itemId}_{i}",                      │
│      DocumentId = documentId,        // null for orphans      │
│      SpeFileId = itemId,                                      │
│      TenantId = tenantId,                                     │
│      FileName = fileName,                                     │
│      Content = chunk.Content,                                 │
│      ChunkIndex = i,                                          │
│      ChunkCount = chunks.Count,                               │
│      KnowledgeSourceId = knowledgeSourceId,                   │
│      KnowledgeSourceName = knowledgeSourceName                │
│  });                                                          │
└───────────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  Step 5: Index to RAG (includes embedding generation)         │
│  ─────────────────────────────────────────────────────────    │
│  var results = await _ragService.IndexDocumentsBatchAsync(    │
│      documents, ct);                                          │
│                                                               │
│  - RagService generates embeddings for each chunk             │
│  - Embeddings cached in Redis (7-day TTL)                     │
│  - Computes documentVector from chunk average                 │
│  - Indexes to Azure AI Search                                 │
└───────────────────────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────────────────────┐
│  Step 6: Return Result                                        │
│  ─────────────────────                                        │
│  return new FileIndexingResult                                │
│  {                                                            │
│      Success = results.All(r => r.Succeeded),                 │
│      ChunksIndexed = results.Count(r => r.Succeeded),         │
│      Duration = stopwatch.Elapsed                             │
│  };                                                           │
└───────────────────────────────────────────────────────────────┘
```

---

## Document vs Orphan File Handling

| Aspect | Document | Orphan File |
|--------|----------|-------------|
| `documentId` | Dataverse GUID | `null` |
| `speFileId` | Graph item ID | Graph item ID |
| Chunk ID format | `{documentId}_{chunkIndex}` | `{itemId}_{chunkIndex}` |
| Visualization | Shows in DocumentRelationshipViewer | Shows as orphan node |
| Dataverse record | Yes (sprk_document) | No |

---

## Integration Points

### PCF Integration

After file upload completes, PCF calls the indexing endpoint:

```typescript
// In PCF after successful upload
const indexResult = await fetch('/api/ai/rag/index-file', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
    },
    body: JSON.stringify({
        driveId: uploadResult.parentReference.driveId,
        itemId: uploadResult.id,
        fileName: uploadResult.name,
        tenantId: context.userSettings.tenantId,
        documentId: documentRecord?.id  // null if orphan
    })
});
```

### Playbook Integration (Read Path)

Playbooks continue to use RAG via Knowledge sources (no changes needed):

```csharp
// Existing: ProcessRagKnowledgeAsync queries the index
var ragSources = knowledge.Where(k => k.Type == KnowledgeType.RagIndex);
foreach (var source in ragSources)
{
    var searchResults = await _ragService.SearchAsync(
        searchQuery,
        new RagSearchOptions { TenantId = tenantId, TopK = 5 },
        ct);
    // Results added to LLM context
}
```

---

## Multi-Index Support (Future)

The design supports future multiple RAG indexes:

```csharp
public record FileIndexRequest
{
    // ... existing fields ...

    // Future: specify target index
    public string? KnowledgeSourceId { get; init; }
    public string? KnowledgeSourceName { get; init; }

    // Future: could also support
    // public string? TargetIndexName { get; init; }
    // public RagDeploymentModel? DeploymentModel { get; init; }
}
```

The existing `KnowledgeDeploymentService` already supports:
- **Shared**: Multi-tenant shared index
- **Dedicated**: Per-tenant index
- **CustomerOwned**: Customer's own Azure AI Search

---

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `Services/Ai/ITextChunkingService.cs` | Interface |
| `Services/Ai/TextChunkingService.cs` | Shared chunking implementation |
| `Services/Ai/IFileIndexingService.cs` | Interface (optional - could be in orchestration service) |
| `Services/Ai/FileIndexingService.cs` | Implementation |

### Modified Files

| File | Changes |
|------|---------|
| `Api/Ai/RagEndpoints.cs` | Add `/index-file` endpoint |
| `Infrastructure/DI/AiModule.cs` | Register new services |
| `Services/Ai/Tools/*.cs` | Refactor to use `ITextChunkingService` (cleanup) |

---

## Error Handling

| Error | Response | Recovery |
|-------|----------|----------|
| File not found in SPE | 404 | Verify driveId/itemId |
| Extraction failed | 500 with details | Check file format support |
| Embedding generation failed | 500 + circuit breaker | Retry after 30s |
| Index write failed | 500 with partial results | Retry failed chunks |
| Tenant not authorized | 403 | Verify tenant access |

---

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| Small file (<1MB) | < 5 seconds | Mostly extraction time |
| Medium file (1-10MB) | < 15 seconds | PDF/DOCX processing |
| Large file (10-50MB) | < 60 seconds | Chunking + batch embedding |
| Embedding generation | ~50ms cached, ~150ms uncached | Per chunk |

---

## Testing Strategy

| Test Type | Scope |
|-----------|-------|
| Unit tests | TextChunkingService, document building |
| Integration tests | Full pipeline with test files |
| E2E tests | PCF upload → index → visualization |

---

## Success Criteria

1. [ ] `POST /api/ai/rag/index-file` indexes documents to AI Search
2. [ ] Works for Documents (with documentId)
3. [ ] Works for orphan files (without documentId)
4. [ ] Indexed documents appear in DocumentRelationshipViewer
5. [ ] Existing playbooks can query indexed content via Knowledge sources
6. [ ] Performance within targets
7. [ ] All existing tests continue to pass

---

## Summary

This design provides a clean, simple solution:

- **Write path**: Direct pipeline (upload → extract → chunk → index)
- **Read path**: Unchanged (playbooks query RAG via Knowledge sources)
- **No playbook involvement** in indexing
- **Reuses all existing components**
- **Works for Documents and orphan files**
- **Supports future multi-index scenarios**

---

## Implementation Verification Checklist

### Existing Components - CONFIRMED COMPLETE ✅

| Component | Verified | Notes |
|-----------|----------|-------|
| `IRagService.IndexDocumentsBatchAsync()` | ✅ | Generates embeddings, computes documentVector |
| `ITextExtractor.ExtractAsync()` | ✅ | PDF, DOCX, EML, MSG, native text |
| `ISpeFileOperations.DownloadFileAsUserAsync()` | ✅ | OBO authentication |
| `KnowledgeDocument` model | ✅ | Supports orphan files (DocumentId = null) |
| `IKnowledgeDeploymentService` | ✅ | Multi-tenant index routing |
| `IEmbeddingCache` | ✅ | Redis-based, 7-day TTL |

### New Components Required

| Component | Type | Purpose |
|-----------|------|---------|
| `ITextChunkingService` | Interface + Implementation | Extract duplicated chunking logic |
| `IFileIndexingService` | Interface + Implementation | Orchestrate file → RAG pipeline |
| `/api/ai/rag/index-file` | API Endpoint | New endpoint in RagEndpoints.cs |
| DI Registration | Configuration | Register new services |

### Files to Create

```
src/server/api/Sprk.Bff.Api/
├── Services/Ai/
│   ├── ITextChunkingService.cs     # NEW
│   ├── TextChunkingService.cs      # NEW
│   ├── IFileIndexingService.cs     # NEW
│   └── FileIndexingService.cs      # NEW
```

### Files to Modify

```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/RagEndpoints.cs          # ADD /index-file endpoint
├── Infrastructure/DI/AiModule.cs   # ADD service registrations
└── Services/Ai/Tools/              # MANDATORY: Refactor all 7 handlers
    ├── SummaryHandler.cs           # REMOVE ChunkText(), inject ITextChunkingService
    ├── ClauseComparisonHandler.cs  # REMOVE ChunkText(), inject ITextChunkingService
    ├── ClauseAnalyzerHandler.cs    # REMOVE ChunkText(), inject ITextChunkingService
    ├── EntityExtractorHandler.cs   # REMOVE ChunkText(), inject ITextChunkingService
    ├── DateExtractorHandler.cs     # REMOVE ChunkText(), inject ITextChunkingService
    ├── RiskDetectorHandler.cs      # REMOVE ChunkText(), inject ITextChunkingService
    └── FinancialCalculatorHandler.cs # REMOVE ChunkText(), inject ITextChunkingService
```

### Confirmation: No Duplicate Components

After comprehensive code review:
- ✅ No existing `ITextChunkingService` or similar interface
- ✅ No existing `IFileIndexingService` or similar interface
- ✅ No existing `/index-file` endpoint
- ✅ Chunking logic is duplicated across 7 tool handlers (extraction beneficial)
- ✅ All required infrastructure (RagService, TextExtractor, SpeFileStore) exists and is production-ready

---

*Design document finalized: 2026-01-12*
*Component inventory verified: 2026-01-12*
