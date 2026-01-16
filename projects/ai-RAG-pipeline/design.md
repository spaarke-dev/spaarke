# RAG Document Ingestion Pipeline - Implementation Design

> **Date**: 2026-01-14 (Updated)
> **Status**: READY FOR IMPLEMENTATION
> **Author**: AI Analysis + Human Review
> **Supersedes**: rag-ingestion-assessment.md (2026-01-11), rag-ingestion-design.md (2026-01-12)
> **Coordinates With**: email-to-document-automation-r2 project

---

## Executive Summary

This document defines the complete design for RAG document ingestion, supporting **three entry points** that converge to a **single shared pipeline**:

1. **OBO (On-Behalf-Of)**: User-triggered indexing via API endpoint
2. **App-Only (File Download)**: Background process indexing with file download
3. **App-Only (Pre-Extracted)**: Background process with text already extracted (optimization)

**Root Cause** (from assessment): No trigger mechanism exists to invoke the indexing pipeline when documents are uploaded or created.

**Solution**: Create `IFileIndexingService` as the **single source of truth** for RAG indexing, with:
- API endpoint for user-triggered indexing (OBO)
- Job handler for background indexing (App-Only)
- Both paths use identical chunking, embedding, and indexing logic

---

## Architecture Overview

### Core Principle: Single Service, Multiple Entry Points

**All indexing paths converge to the same internal pipeline.** The only difference is how text is obtained.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     UNIFIED RAG INDEXING ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────┐         ┌─────────────────┐         ┌─────────────┐  │
│   │  User Upload    │         │  Email-to-Doc   │         │  Batch Job  │  │
│   │  (PCF/API)      │         │  (Background)   │         │  (Future)   │  │
│   └────────┬────────┘         └────────┬────────┘         └──────┬──────┘  │
│            │                           │                         │         │
│            ▼                           ▼                         ▼         │
│   ┌─────────────────┐         ┌─────────────────┐         ┌─────────────┐  │
│   │ POST /api/ai/   │         │ RagIndexingJob  │         │ RagIndexing │  │
│   │ rag/index-file  │         │ Handler         │         │ JobHandler  │  │
│   └────────┬────────┘         └────────┬────────┘         └──────┬──────┘  │
│            │                           │                         │         │
│            │ OBO Auth                  │ App-Only Auth           │         │
│            │                           │                         │         │
│            ▼                           ▼                         ▼         │
│   ┌─────────────────────────────────────────────────────────────────────┐  │
│   │                     FileIndexingService                              │  │
│   │  ┌───────────────────────────────────────────────────────────────┐  │  │
│   │  │  IndexFileAsync(request, httpContext)     ← User upload (OBO) │  │  │
│   │  │  IndexFileAppOnlyAsync(request)           ← Download + extract│  │  │
│   │  │  IndexContentAsync(request)               ← Pre-extracted     │  │  │
│   │  └───────────────────────────────────────────────────────────────┘  │  │
│   │                              │                                       │  │
│   │                              ▼                                       │  │
│   │  ┌───────────────────────────────────────────────────────────────┐  │  │
│   │  │          SHARED PIPELINE (identical for all paths)            │  │  │
│   │  │                                                               │  │  │
│   │  │  ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌──────────────┐  │  │  │
│   │  │  │  Chunk  │ → │  Build  │ → │  Embed  │ → │ Index to     │  │  │  │
│   │  │  │  Text   │   │  Docs   │   │ (OpenAI)│   │ AI Search    │  │  │  │
│   │  │  └─────────┘   └─────────┘   └─────────┘   └──────────────┘  │  │  │
│   │  │       ↑                                                       │  │  │
│   │  │  ITextChunkingService (shared)                                │  │  │
│   │  └───────────────────────────────────────────────────────────────┘  │  │
│   └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Three Entry Points, One Pipeline

| Entry Point | Trigger | Auth | Text Source | Use Case |
|-------------|---------|------|-------------|----------|
| `IndexFileAsync()` | API endpoint | OBO | Download via OBO | User uploads via PCF |
| `IndexFileAppOnlyAsync()` | Job handler | App-only | Download via app | Document events, fallback |
| `IndexContentAsync()` | Job handler | App-only | Pre-extracted | Email processing (optimized) |

**Why this matters**: Regardless of how a file enters the system (user upload, email automation, batch import), the chunking, embedding, and indexing logic is **identical**. This ensures consistent behavior and searchability.

---

## Coordination with email-to-document-automation-r2

### Current State (as of 2026-01-14)

The email-to-document-automation-r2 project has implemented:
- `EmailToDocumentJobHandler` - processes emails, creates Documents
- `AppOnlyDocumentAnalysisJobHandler` - background AI analysis jobs
- `EnqueueAiAnalysisJobAsync()` - enqueues AI jobs after document creation

### Integration Pattern

RAG indexing will follow the **same job-based pattern** as AI analysis:

```csharp
// EmailToDocumentJobHandler.cs - Current (Task 022)
await EnqueueAiAnalysisJobAsync(documentId, "Email", ct);

// EmailToDocumentJobHandler.cs - Future (RAG integration)
await EnqueueRagIndexingJobAsync(documentId, driveId, itemId, fileName, extractedText, ct);
```

### Sequencing

| Phase | Project | Description |
|-------|---------|-------------|
| **Now** | email-to-document-r2 | Complete tasks 023-029 (unit tests, deployment) |
| **Next** | ai-RAG-pipeline | Implement FileIndexingService, RagIndexingJobHandler |
| **Then** | ai-RAG-pipeline | Add RAG integration to EmailToDocumentJobHandler |

This sequencing ensures email-to-document is stable before adding RAG capabilities.

---

## Component Inventory (Verified 2026-01-14)

### Fully Implemented Components (Ready to Use)

| Component | Location | Status | Notes |
|-----------|----------|--------|-------|
| **Text Extraction** | `Services/Ai/TextExtractorService.cs` | ✅ Complete | PDF, DOCX, TXT, email, vision OCR |
| **Embedding Generation** | `Services/Ai/OpenAiClient.cs` | ✅ Complete | Polly 8.x circuit breaker (Jan 12) |
| **Embedding Cache** | `Services/Ai/EmbeddingCache.cs` | ✅ Complete | Redis, 7-day TTL, graceful errors |
| **RAG Indexing** | `Services/Ai/RagService.cs` | ✅ Complete | Batch indexing, document vectors |
| **RAG Search** | `Services/Ai/RagService.cs` | ✅ Complete | Hybrid + semantic + circuit breaker |
| **Deployment Routing** | `Services/Ai/KnowledgeDeploymentService.cs` | ✅ Complete | Multi-tenant, config caching |
| **RAG API Endpoints** | `Api/Ai/RagEndpoints.cs` | ✅ Complete | POST /index, /batch, /search, DELETE |
| **File Download (OBO)** | `Infrastructure/Graph/SpeFileStore.cs` | ✅ Complete | `DownloadFileAsUserAsync()` |
| **File Download (App)** | `Infrastructure/Graph/SpeFileStore.cs` | ✅ Complete | `DownloadFileAsync()` |
| **Email Processing** | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | ✅ Complete | App-only auth, AI job enqueueing |
| **Graph Factory** | `Infrastructure/Graph/GraphClientFactory.cs` | ✅ Complete | OBO + App-only support |
| **Job Submission** | `Services/Jobs/JobSubmissionService.cs` | ✅ Complete | Service Bus integration |

### Missing Components (To Implement)

| Component | Purpose | Priority |
|-----------|---------|----------|
| **ITextChunkingService** | Shared chunking logic (extract from tool handlers) | HIGH |
| **IFileIndexingService** | Orchestrate: text → chunk → embed → index | HIGH |
| **RagIndexingJobHandler** | Background job for RAG indexing | HIGH |
| **API Endpoint** | `POST /api/ai/rag/index-file` | HIGH |
| **Email Integration** | Add `EnqueueRagIndexingJobAsync` to EmailToDocumentJobHandler | HIGH |

### Partially Implemented (Needs Completion)

| Component | Location | Status | Issue |
|-----------|----------|--------|-------|
| **Text Chunking** | Multiple tool handlers | ⚠️ Duplicated | Same code in 7 handlers |
| **Document Event Handler** | `Services/Jobs/Handlers/DocumentEventHandler.cs` | ⚠️ Placeholder | All methods are stubs |

---

## New Components Design

### 1. ITextChunkingService

Extract existing chunking logic from tool handlers to ensure consistent chunking across all use cases:

```csharp
// Services/Ai/ITextChunkingService.cs
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

**Implementation**: Extract from `SummaryHandler.ChunkText()` (line 479-517)

---

### 2. IFileIndexingService (Unified Pipeline)

The **single service** for all RAG indexing, with three entry points:

```csharp
// Services/Ai/IFileIndexingService.cs
public interface IFileIndexingService
{
    /// <summary>
    /// Index a file using OBO authentication (user-triggered).
    /// Downloads file, extracts text, then uses shared pipeline.
    /// </summary>
    Task<FileIndexingResult> IndexFileAsync(
        FileIndexRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Index a file using app-only authentication (background job).
    /// Downloads file, extracts text, then uses shared pipeline.
    /// </summary>
    Task<FileIndexingResult> IndexFileAppOnlyAsync(
        FileIndexRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Index pre-extracted content directly (most efficient).
    /// Skips download and extraction, goes straight to shared pipeline.
    /// Use when text is already available (e.g., email processing).
    /// </summary>
    Task<FileIndexingResult> IndexContentAsync(
        ContentIndexRequest request,
        CancellationToken cancellationToken);
}

public record FileIndexRequest
{
    public required string DriveId { get; init; }
    public required string ItemId { get; init; }
    public required string FileName { get; init; }
    public required string TenantId { get; init; }
    public string? DocumentId { get; init; }           // null for orphan files
    public string? KnowledgeSourceId { get; init; }
    public string? KnowledgeSourceName { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public record ContentIndexRequest
{
    public required string Content { get; init; }
    public required string FileName { get; init; }
    public required string TenantId { get; init; }
    public required string SpeFileId { get; init; }    // Graph item ID
    public string? DocumentId { get; init; }
    public string? KnowledgeSourceId { get; init; }
    public string? KnowledgeSourceName { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public record FileIndexingResult
{
    public bool Success { get; init; }
    public int ChunksIndexed { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
    public string? DocumentId { get; init; }
    public string? SpeFileId { get; init; }

    public static FileIndexingResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
```

---

### 3. FileIndexingService Implementation

```csharp
// Services/Ai/FileIndexingService.cs
public sealed class FileIndexingService : IFileIndexingService
{
    private readonly ISpeFileOperations _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly ITextChunkingService _chunkingService;
    private readonly IRagService _ragService;
    private readonly ILogger<FileIndexingService> _logger;

    public FileIndexingService(
        ISpeFileOperations speFileStore,
        ITextExtractor textExtractor,
        ITextChunkingService chunkingService,
        IRagService ragService,
        ILogger<FileIndexingService> logger)
    {
        _speFileStore = speFileStore;
        _textExtractor = textExtractor;
        _chunkingService = chunkingService;
        _ragService = ragService;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Entry Point 1: User Upload (OBO)
    // ═══════════════════════════════════════════════════════════════════
    public async Task<FileIndexingResult> IndexFileAsync(
        FileIndexRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Download via OBO
            await using var stream = await _speFileStore.DownloadFileAsUserAsync(
                httpContext, request.DriveId, request.ItemId, cancellationToken);

            if (stream is null)
                return FileIndexingResult.Failed($"File not found: {request.DriveId}/{request.ItemId}");

            // Extract text
            var extraction = await _textExtractor.ExtractAsync(stream, request.FileName, cancellationToken);
            if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
                return FileIndexingResult.Failed(extraction.Error ?? "Text extraction failed");

            // → SHARED PIPELINE
            return await IndexTextInternalAsync(extraction.Text, request, stopwatch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FileName} (OBO)", request.FileName);
            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Entry Point 2: Background Job (App-Only) - downloads file
    // ═══════════════════════════════════════════════════════════════════
    public async Task<FileIndexingResult> IndexFileAppOnlyAsync(
        FileIndexRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Download via App-Only
            await using var stream = await _speFileStore.DownloadFileAsync(
                request.DriveId, request.ItemId, cancellationToken);

            if (stream is null)
                return FileIndexingResult.Failed($"File not found: {request.DriveId}/{request.ItemId}");

            // Extract text
            var extraction = await _textExtractor.ExtractAsync(stream, request.FileName, cancellationToken);
            if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
                return FileIndexingResult.Failed(extraction.Error ?? "Text extraction failed");

            // → SHARED PIPELINE
            return await IndexTextInternalAsync(extraction.Text, request, stopwatch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file {FileName} (App-Only)", request.FileName);
            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Entry Point 3: Pre-Extracted Content (most efficient)
    // ═══════════════════════════════════════════════════════════════════
    public async Task<FileIndexingResult> IndexContentAsync(
        ContentIndexRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Convert to common request format
            var fileRequest = new FileIndexRequest
            {
                DriveId = string.Empty,  // Not needed - content provided
                ItemId = request.SpeFileId,
                FileName = request.FileName,
                TenantId = request.TenantId,
                DocumentId = request.DocumentId,
                KnowledgeSourceId = request.KnowledgeSourceId,
                KnowledgeSourceName = request.KnowledgeSourceName,
                Metadata = request.Metadata
            };

            // → SHARED PIPELINE directly (no download or extraction)
            return await IndexTextInternalAsync(request.Content, fileRequest, stopwatch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index content for {FileName}", request.FileName);
            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SHARED PIPELINE - All entry points converge here
    // ═══════════════════════════════════════════════════════════════════
    private async Task<FileIndexingResult> IndexTextInternalAsync(
        string text,
        FileIndexRequest request,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // Step 1: Chunk text (same for all sources)
        var chunks = _chunkingService.ChunkText(text);

        if (chunks.Count == 0)
        {
            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = "No chunks generated from content",
                Duration = stopwatch.Elapsed
            };
        }

        // Step 2: Build KnowledgeDocuments (same schema for all)
        var chunkIdBase = request.DocumentId ?? request.ItemId;
        var documents = chunks.Select((chunk, i) => new KnowledgeDocument
        {
            Id = $"{chunkIdBase}_{i}",
            TenantId = request.TenantId,
            DocumentId = request.DocumentId,
            SpeFileId = request.ItemId,
            FileName = request.FileName,
            Content = chunk.Content,
            ChunkIndex = i,
            ChunkCount = chunks.Count,
            KnowledgeSourceId = request.KnowledgeSourceId,
            KnowledgeSourceName = request.KnowledgeSourceName,
            Metadata = request.Metadata is not null
                ? JsonSerializer.Serialize(request.Metadata)
                : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }).ToList();

        // Step 3: Index batch (embeddings generated inside RagService)
        var results = await _ragService.IndexDocumentsBatchAsync(documents, cancellationToken);

        var successCount = results.Count(r => r.Succeeded);
        var allSucceeded = successCount == results.Count;

        _logger.LogInformation(
            "Indexed {FileName}: {Success}/{Total} chunks in {Duration}ms",
            request.FileName, successCount, results.Count, stopwatch.ElapsedMilliseconds);

        return new FileIndexingResult
        {
            Success = allSucceeded,
            ChunksIndexed = successCount,
            ErrorMessage = allSucceeded ? null : $"Failed to index {results.Count - successCount} chunks",
            Duration = stopwatch.Elapsed,
            DocumentId = request.DocumentId,
            SpeFileId = request.ItemId
        };
    }
}
```

---

### 4. RagIndexingJobHandler (Background Processing)

A **thin wrapper** that delegates to FileIndexingService, following the same pattern as AppOnlyDocumentAnalysisJobHandler:

```csharp
// Services/Jobs/Handlers/RagIndexingJobHandler.cs
public class RagIndexingJobHandler : IJobHandler
{
    public const string JobTypeName = "RagIndexing";

    private readonly IFileIndexingService _fileIndexingService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly RagTelemetry _telemetry;
    private readonly ILogger<RagIndexingJobHandler> _logger;

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var payload = ParsePayload<RagIndexingPayload>(job.Payload);

            _logger.LogInformation(
                "Processing RAG indexing job {JobId} for document {DocumentId}",
                job.JobId, payload.DocumentId);

            // Idempotency check
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
                idempotencyKey = $"rag-index-{payload.DocumentId}";

            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation("Document {DocumentId} already indexed to RAG", payload.DocumentId);
                _telemetry.RecordJobSkippedDuplicate();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            FileIndexingResult result;

            // Choose entry point based on available data
            if (!string.IsNullOrEmpty(payload.ExtractedText))
            {
                // Pre-extracted text available → most efficient path
                _logger.LogDebug("Using pre-extracted text ({CharCount} chars)", payload.ExtractedText.Length);

                var contentRequest = new ContentIndexRequest
                {
                    Content = payload.ExtractedText,
                    FileName = payload.FileName,
                    TenantId = payload.TenantId,
                    SpeFileId = payload.ItemId,
                    DocumentId = payload.DocumentId?.ToString(),
                    Metadata = payload.Metadata
                };

                result = await _fileIndexingService.IndexContentAsync(contentRequest, ct);
            }
            else
            {
                // No pre-extracted text → download and extract
                _logger.LogDebug("No pre-extracted text, downloading from SPE");

                var fileRequest = new FileIndexRequest
                {
                    DriveId = payload.DriveId!,
                    ItemId = payload.ItemId,
                    FileName = payload.FileName,
                    TenantId = payload.TenantId,
                    DocumentId = payload.DocumentId?.ToString(),
                    Metadata = payload.Metadata
                };

                result = await _fileIndexingService.IndexFileAppOnlyAsync(fileRequest, ct);
            }

            if (result.Success)
            {
                await _idempotencyService.MarkEventAsProcessedAsync(idempotencyKey, TimeSpan.FromDays(7), ct);
                _telemetry.RecordJobSuccess(stopwatch, result.ChunksIndexed);

                _logger.LogInformation(
                    "RAG indexing job {JobId} completed: {ChunksIndexed} chunks in {Duration}ms",
                    job.JobId, result.ChunksIndexed, stopwatch.ElapsedMilliseconds);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _telemetry.RecordJobFailure(result.ErrorMessage ?? "unknown");
            return JobOutcome.Failure(job.JobId, JobType, result.ErrorMessage ?? "Indexing failed", job.Attempt, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG indexing job {JobId} failed", job.JobId);
            _telemetry.RecordJobFailure("exception");
            return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private static T? ParsePayload<T>(JsonDocument? payload) where T : class
    {
        if (payload == null) return null;
        return JsonSerializer.Deserialize<T>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

/// <summary>
/// Payload for RAG indexing jobs.
/// </summary>
public class RagIndexingPayload
{
    public Guid? DocumentId { get; set; }
    public string? DriveId { get; set; }
    public required string ItemId { get; set; }
    public required string FileName { get; set; }
    public required string TenantId { get; set; }

    /// <summary>
    /// Pre-extracted text (optimization - avoids re-downloading and re-extracting).
    /// </summary>
    public string? ExtractedText { get; set; }

    /// <summary>
    /// Additional metadata to store with indexed chunks.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Source of the indexing request.
    /// </summary>
    public string? Source { get; set; }

    public DateTimeOffset? EnqueuedAt { get; set; }
}
```

---

## Integration Points

### 1. API Endpoint (User-Triggered, OBO)

Add to `RagEndpoints.cs`:

```csharp
// POST /api/ai/rag/index-file
group.MapPost("/index-file", IndexFile)
    .AddTenantAuthorizationFilter()
    .RequireRateLimiting("ai-batch")
    .WithName("RagIndexFile")
    .WithSummary("Index a file from SPE to RAG knowledge base")
    .WithDescription("Downloads file, extracts text, chunks, generates embeddings, and indexes to Azure AI Search.")
    .Produces<FileIndexingResult>()
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

private static async Task<IResult> IndexFile(
    [FromBody] FileIndexRequest request,
    HttpContext httpContext,
    IFileIndexingService fileIndexingService,
    CancellationToken cancellationToken)
{
    var result = await fileIndexingService.IndexFileAsync(request, httpContext, cancellationToken);

    return result.Success
        ? Results.Ok(result)
        : Results.Problem(
            title: "Indexing failed",
            detail: result.ErrorMessage,
            statusCode: StatusCodes.Status500InternalServerError);
}
```

### 2. Email-to-Document Integration (Job-Based, App-Only)

Add to `EmailToDocumentJobHandler.cs` - follows same pattern as AI analysis enqueueing:

**Configuration** (add to `EmailProcessingOptions.cs`):

```csharp
/// <summary>
/// Whether to automatically index email documents to RAG.
/// Default: false (enable after RAG pipeline deployed and tested)
/// </summary>
public bool AutoIndexToRag { get; set; } = false;
```

**Enqueueing method** (add to `EmailToDocumentJobHandler.cs`):

```csharp
/// <summary>
/// Enqueues a RAG indexing job for a document if AutoIndexToRag is enabled.
/// Uses try/catch to ensure enqueueing failures don't fail the main processing.
/// </summary>
private async Task EnqueueRagIndexingJobAsync(
    Guid documentId,
    string driveId,
    string itemId,
    string fileName,
    string? extractedText,
    Dictionary<string, string>? metadata,
    CancellationToken ct)
{
    if (!_options.AutoIndexToRag)
    {
        _logger.LogDebug(
            "AutoIndexToRag disabled, skipping RAG indexing for document {DocumentId}",
            documentId);
        return;
    }

    try
    {
        var ragJob = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = RagIndexingJobHandler.JobTypeName,
            SubjectId = documentId.ToString(),
            CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString(),
            IdempotencyKey = $"rag-index-{documentId}",
            Attempt = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = JsonDocument.Parse(JsonSerializer.Serialize(new RagIndexingPayload
            {
                DocumentId = documentId,
                DriveId = driveId,
                ItemId = itemId,
                FileName = fileName,
                TenantId = "app-only",  // Or extract from context
                ExtractedText = extractedText,
                Metadata = metadata,
                Source = "email-archive",
                EnqueuedAt = DateTimeOffset.UtcNow
            }))
        };

        await _jobSubmissionService.SubmitJobAsync(ragJob, ct);

        _telemetry.RecordRagJobEnqueued("email");  // Add telemetry method

        _logger.LogInformation(
            "Enqueued RAG indexing job {JobId} for document {DocumentId}",
            ragJob.JobId, documentId);
    }
    catch (Exception ex)
    {
        _telemetry.RecordRagJobEnqueueFailure("email", "enqueue_error");

        _logger.LogWarning(ex,
            "Failed to enqueue RAG indexing job for document {DocumentId}: {Error}. Email processing will continue.",
            documentId, ex.Message);
    }
}
```

**Call site** (after document creation, alongside AI analysis):

```csharp
// Enqueue AI analysis (existing from Task 022)
await EnqueueAiAnalysisJobAsync(documentId, "Email", ct);

// Enqueue RAG indexing (new)
await EnqueueRagIndexingJobAsync(
    documentId,
    driveId,
    fileHandle.Id,
    fileName,
    null,  // extractedText - see optimization note below
    new Dictionary<string, string>
    {
        ["emailSubject"] = email.Subject ?? "",
        ["emailFrom"] = email.From ?? "",
        ["source"] = "email-archive"
    },
    ct);
```

### 3. Pre-Extraction Optimization (Optional Enhancement)

For maximum efficiency, extract text once and pass to both jobs:

```csharp
// Optional: Extract text once for both AI and RAG
string? extractedText = null;
if (_options.AutoEnqueueAi || _options.AutoIndexToRag)
{
    emlStream.Position = 0;
    var extraction = await _textExtractor.ExtractAsync(emlStream, fileName, ct);
    if (extraction.Success)
        extractedText = extraction.Text;
    emlStream.Position = 0;
}

// Pass to both jobs
await EnqueueAiAnalysisJobAsync(documentId, "Email", extractedText, fileName, ct);
await EnqueueRagIndexingJobAsync(documentId, driveId, itemId, fileName, extractedText, metadata, ct);
```

This optimization can be implemented after initial RAG integration is working.

### 4. Document Event Handler (Future, App-Only)

For documents created outside email flow:

```csharp
private async Task HandleDocumentCreatedAsync(DocumentEvent documentEvent, CancellationToken ct)
{
    // ... extract document info ...

    var request = new FileIndexRequest
    {
        DriveId = driveId,
        ItemId = itemId,
        FileName = fileName,
        TenantId = tenantId,
        DocumentId = documentId
    };

    var result = await _fileIndexingService.IndexFileAppOnlyAsync(request, ct);

    if (result.Success)
    {
        await _idempotencyService.MarkEventAsProcessedAsync($"rag-index-{documentId}", TimeSpan.FromDays(7), ct);
    }
}
```

---

## PCF Integration

After file upload completes, PCF calls the indexing endpoint:

```typescript
// In PCF after successful upload to SPE
async function indexDocumentToRag(
    uploadResult: DriveItem,
    documentId: string | null,
    tenantId: string
): Promise<FileIndexingResult> {
    const response = await fetch('/api/ai/rag/index-file', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${await getAccessToken()}`
        },
        body: JSON.stringify({
            driveId: uploadResult.parentReference?.driveId,
            itemId: uploadResult.id,
            fileName: uploadResult.name,
            tenantId: tenantId,
            documentId: documentId  // null if orphan file
        })
    });

    if (!response.ok) {
        console.warn('RAG indexing failed:', await response.text());
        // Don't block upload flow - indexing is async enhancement
    }

    return await response.json();
}
```

---

## Implementation Tasks

### Phase 0: Analysis Workflow Alignment (Priority: CRITICAL - Prerequisite)

**Issue Identified (2026-01-14)**: `AppOnlyAnalysisService` does NOT create `sprk_analysis` or `sprk_analysisoutput` records like `AnalysisOrchestrationService`. This creates inconsistency in how analysis results are stored.

**Current State**:
| Service | Creates `sprk_analysis` | Creates `sprk_analysisoutput` | Updates Document Fields |
|---------|------------------------|-------------------------------|------------------------|
| `AnalysisOrchestrationService` (OBO) | ✅ Yes | ✅ Yes | ✅ Yes |
| `AppOnlyAnalysisService` (Background) | ❌ No | ❌ No | ✅ Yes |

**Target State**: Both services should create Analysis records for consistency.

| Task | Description | Files |
|------|-------------|-------|
| 0.1 | Add `IDataverseService` dependency to `AppOnlyAnalysisService` | 1 modified |
| 0.2 | Call `CreateAnalysisAsync()` before running playbook tools | 1 modified |
| 0.3 | Call `CreateAnalysisOutputAsync()` for each tool output | 1 modified |
| 0.4 | Ensure dual-write: outputs to `sprk_analysisoutput` AND Document fields | 1 modified |
| 0.5 | Update `AppOnlyDocumentAnalysisJobHandler` to pass AnalysisId to telemetry | 1 modified |
| 0.6 | Unit tests for Analysis record creation in app-only flow | 1 new |

**Files Modified**:
- `Services/Ai/AppOnlyAnalysisService.cs` - Add Analysis record creation
- `Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` - Pass AnalysisId
- `Telemetry/DocumentTelemetry.cs` - Track AnalysisId

**Why This Matters**:
1. Analysis records provide audit trail of all document analyses
2. Enables "Analysis Workspace" to show background analyses
3. Consistent behavior regardless of trigger source (user vs automation)
4. Required before RAG integration since both need unified approach

---

### Phase 1: Core Pipeline (Priority: HIGH)

| Task | Description | Files |
|------|-------------|-------|
| 1.1 | Create `ITextChunkingService` + `TextChunkingService` | 2 new |
| 1.2 | Create `IFileIndexingService` + `FileIndexingService` | 2 new |
| 1.3 | Create `RagIndexingJobHandler` | 1 new |
| 1.4 | Add `POST /api/ai/rag/index-file` endpoint | 1 modified |
| 1.5 | Register services and job handler in DI | 1 modified |
| 1.6 | Unit tests for chunking and indexing services | 2 new |

### Phase 2: Email Integration (Priority: HIGH)

| Task | Description | Files |
|------|-------------|-------|
| 2.1 | Add `AutoIndexToRag` to `EmailProcessingOptions` | 1 modified |
| 2.2 | Add `EnqueueRagIndexingJobAsync` to `EmailToDocumentJobHandler` | 1 modified |
| 2.3 | Add RAG telemetry to `EmailTelemetry` | 1 modified |
| 2.4 | Integration tests for email-to-RAG flow | 1 new |

### Phase 3: Cleanup (Priority: MEDIUM)

| Task | Description | Files |
|------|-------------|-------|
| 3.1 | Refactor tool handlers to use `ITextChunkingService` | 7 modified |
| 3.2 | Verify no duplicate `ChunkText()` methods remain | Verification |

### Phase 4: Event-Driven (Priority: LOW)

| Task | Description | Files |
|------|-------------|-------|
| 4.1 | Implement `DocumentEventHandler.HandleDocumentCreatedAsync()` | 1 modified |
| 4.2 | E2E tests for document event → RAG flow | 1 new |

---

## Verification: Same Pipeline for All Paths

| Step | User Upload (OBO) | Email Automation | Document Event | Same Code? |
|------|-------------------|------------------|----------------|------------|
| 1. Get text | `DownloadFileAsUserAsync` | Pre-extracted or download | `DownloadFileAsync` | Entry varies |
| 2. Chunk | `_chunkingService.ChunkText()` | `_chunkingService.ChunkText()` | `_chunkingService.ChunkText()` | ✅ Same |
| 3. Build docs | `new KnowledgeDocument {...}` | `new KnowledgeDocument {...}` | `new KnowledgeDocument {...}` | ✅ Same |
| 4. Embed | `_ragService.IndexDocumentsBatchAsync()` | `_ragService.IndexDocumentsBatchAsync()` | `_ragService.IndexDocumentsBatchAsync()` | ✅ Same |
| 5. Index | Azure AI Search | Azure AI Search | Azure AI Search | ✅ Same |
| **Result** | Identical chunks, embeddings, index entries | Identical | Identical | ✅ Guaranteed |

---

## Files Summary

### New Files

```
src/server/api/Sprk.Bff.Api/
├── Services/Ai/
│   ├── ITextChunkingService.cs         # Interface
│   ├── TextChunkingService.cs          # Implementation
│   ├── IFileIndexingService.cs         # Interface (unified pipeline)
│   └── FileIndexingService.cs          # Implementation
├── Services/Jobs/Handlers/
│   └── RagIndexingJobHandler.cs        # Background job handler
└── Telemetry/
    └── RagTelemetry.cs                 # RAG-specific telemetry
```

### Modified Files

```
src/server/api/Sprk.Bff.Api/
├── Api/Ai/RagEndpoints.cs                           # Add /index-file endpoint
├── Configuration/EmailProcessingOptions.cs          # Add AutoIndexToRag
├── Infrastructure/DI/AiModule.cs                    # Register services
├── Services/Jobs/Handlers/
│   ├── EmailToDocumentJobHandler.cs                 # Add EnqueueRagIndexingJobAsync
│   └── DocumentEventHandler.cs                      # Implement handlers (Phase 4)
├── Telemetry/EmailTelemetry.cs                      # Add RAG telemetry methods
└── Services/Ai/Tools/
    ├── SummaryHandler.cs                            # Refactor to use ITextChunkingService
    └── (6 other handlers)                           # Refactor chunking
```

---

## Error Handling

| Error | Response | Recovery |
|-------|----------|----------|
| File not found in SPE | 404 | Verify driveId/itemId |
| Extraction failed | 500 with details | Check file format support |
| Embedding generation failed | 500 + circuit breaker | Retry after 30s |
| Index write failed | 500 with partial results | Retry failed chunks |
| Tenant not authorized | 403 | Verify tenant access |
| App-only auth failure | 500 | Check client credentials |

---

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| Small file (<1MB) | < 5 seconds | Mostly extraction time |
| Medium file (1-10MB) | < 15 seconds | PDF/DOCX processing |
| Large file (10-50MB) | < 60 seconds | Chunking + batch embedding |
| Embedding generation | ~50ms cached, ~150ms uncached | Per chunk |
| Email archive indexing | < 10 seconds | Most emails < 1MB |

---

## Success Criteria

### Phase 0: Analysis Alignment
1. [ ] `AppOnlyAnalysisService` creates `sprk_analysis` records
2. [ ] `AppOnlyAnalysisService` creates `sprk_analysisoutput` records for each tool
3. [ ] Analysis records are visible in Dataverse for background-processed documents
4. [ ] Document fields AND Analysis records updated consistently

### Phase 1-4: RAG Pipeline
5. [ ] `POST /api/ai/rag/index-file` indexes documents via OBO auth
6. [ ] `RagIndexingJobHandler` indexes documents via app-only auth
7. [ ] Both paths produce **identical** indexed results
8. [ ] Email archives are automatically indexed when `AutoIndexToRag=true`
9. [ ] Works for Documents (with documentId) and orphan files (without)
10. [ ] Indexed documents are searchable via `/api/ai/rag/search`
11. [ ] Performance within targets
12. [ ] All existing tests continue to pass
13. [ ] Duplicate `ChunkText()` methods removed from tool handlers

---

## Summary

| Category | Status | Notes |
|----------|--------|-------|
| **Analysis Workflow Alignment** | ❌ Phase 0 | AppOnlyAnalysisService must create sprk_analysis records |
| **Core RAG Components** | ✅ Complete | RagService, TextExtractor, EmbeddingCache |
| **Text Chunking Service** | ❌ To Create | Extract from tool handlers |
| **File Indexing Service** | ❌ To Create | Unified pipeline with 3 entry points |
| **RAG Job Handler** | ❌ To Create | Background processing |
| **API Endpoint** | ❌ To Create | `POST /api/ai/rag/index-file` |
| **Email Integration** | ❌ To Wire | Add after email-to-document-r2 complete |
| **Event Handler** | ⚠️ Phase 4 | Placeholder stubs exist |

**Bottom Line**:
1. **Phase 0 first**: Align AppOnlyAnalysisService to create Analysis records (consistency)
2. All indexing paths converge to `FileIndexingService.IndexTextInternalAsync()`
3. Same chunking, embedding, and indexing regardless of source
4. Job-based approach for email aligns with existing AI analysis pattern
5. Complete email-to-document-r2 first, then implement RAG pipeline

---

*Design document created: 2026-01-14*
*Updated: 2026-01-14 - Unified pipeline architecture, job handler pattern, sequencing guidance*
*Supersedes: rag-ingestion-assessment.md (2026-01-11), rag-ingestion-design.md (2026-01-12)*
