using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// RAG (Retrieval-Augmented Generation) endpoints for knowledge base operations.
/// Follows ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// Provides endpoints for:
/// - Hybrid search (keyword + vector + semantic ranking)
/// - Document indexing
/// - Document deletion
///
/// Multi-tenant support via tenantId in request/options.
/// </remarks>
public static class RagEndpoints
{
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/rag")
            .RequireAuthorization()
            .WithTags("AI RAG");

        // POST /api/ai/rag/search - Hybrid search
        group.MapPost("/search", Search)
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagSearch")
            .WithSummary("Search knowledge base using hybrid search")
            .WithDescription("Executes hybrid search combining keyword, vector, and semantic ranking for optimal relevance.")
            .Produces<RagSearchResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/rag/index - Index a document
        group.MapPost("/index", IndexDocument)
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagIndexDocument")
            .WithSummary("Index a document chunk into the knowledge base")
            .WithDescription("Generates embedding and indexes the document chunk for RAG retrieval.")
            .Produces<KnowledgeDocument>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/rag/index/batch - Batch index documents
        group.MapPost("/index/batch", IndexDocumentsBatch)
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagIndexDocumentsBatch")
            .WithSummary("Batch index multiple document chunks")
            .WithDescription("Generates embeddings and indexes multiple document chunks efficiently.")
            .Produces<IReadOnlyList<IndexResult>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // DELETE /api/ai/rag/{documentId} - Delete a document
        group.MapDelete("/{documentId}", DeleteDocument)
            .AddTenantAuthorizationFilter()
            .WithName("RagDeleteDocument")
            .WithSummary("Delete a document chunk from the knowledge base")
            .Produces<RagDeleteResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // DELETE /api/ai/rag/source/{sourceDocumentId} - Delete all chunks for a source document
        group.MapDelete("/source/{sourceDocumentId}", DeleteBySourceDocument)
            .AddTenantAuthorizationFilter()
            .WithName("RagDeleteBySourceDocument")
            .WithSummary("Delete all chunks for a source document")
            .Produces<RagDeleteResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // GET /api/ai/rag/embedding - Generate embedding for text (utility endpoint)
        group.MapPost("/embedding", GetEmbedding)
            .RequireRateLimiting("ai-batch")
            .WithName("RagGetEmbedding")
            .WithSummary("Generate embedding for text content")
            .WithDescription("Generates a vector embedding for the provided text. Uses caching when available.")
            .Produces<EmbeddingResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/rag/index-file - Index a file via unified pipeline
        group.MapPost("/index-file", IndexFile)
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagIndexFile")
            .WithSummary("Index a file into the knowledge base via unified pipeline")
            .WithDescription("Downloads file via OBO authentication, extracts text, chunks, generates embeddings, and indexes to Azure AI Search.")
            .Produces<FileIndexingResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/rag/enqueue-indexing - Enqueue a file for background RAG indexing
        // Uses AllowAnonymous + API key header validation (consistent with email webhook pattern)
        // Security: Validates X-Api-Key header before enqueueing job
        // Job handler uses app-only auth (Pattern 6) for SPE file access
        group.MapPost("/enqueue-indexing", EnqueueIndexing)
            .AllowAnonymous()
            .RequireRateLimiting("ai-batch")
            .WithName("RagEnqueueIndexing")
            .WithSummary("Enqueue a file for background RAG indexing")
            .WithDescription("Validates API key header and enqueues file for async indexing via job handler. Used for background jobs, scheduled indexing, bulk operations, and automated testing.")
            .Produces<EnqueueIndexingResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // ═══════════════════════════════════════════════════════════════════════════
        // Admin Bulk Indexing Endpoints
        // ═══════════════════════════════════════════════════════════════════════════

        var adminGroup = group.MapGroup("/admin")
            .RequireAuthorization("SystemAdmin")
            .WithTags("AI RAG Admin");

        // POST /api/ai/rag/admin/bulk-index - Submit a bulk RAG indexing job
        adminGroup.MapPost("/bulk-index", SubmitBulkIndexingJob)
            .WithName("RagSubmitBulkIndexing")
            .WithSummary("Submit a bulk RAG indexing job")
            .WithDescription("Queries documents matching criteria and indexes them in bulk with progress tracking. Returns job ID for status monitoring.")
            .Produces<BulkIndexingResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // GET /api/ai/rag/admin/bulk-index/{jobId}/status - Get bulk job status
        adminGroup.MapGet("/bulk-index/{jobId}/status", GetBulkIndexingJobStatus)
            .WithName("RagGetBulkIndexingStatus")
            .WithSummary("Get bulk indexing job status")
            .WithDescription("Returns progress and status of a bulk indexing job including processed count, errors, and estimated time remaining.")
            .Produces<BulkIndexingStatusResponse>()
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Search knowledge base using hybrid search.
    /// </summary>
    private static async Task<IResult> Search(
        RagSearchRequest request,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Query is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.Options?.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required in options",
                Status = 400
            });
        }

        try
        {
            var response = await ragService.SearchAsync(request.Query, request.Options, cancellationToken);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Search Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Index a document chunk into the knowledge base.
    /// </summary>
    private static async Task<IResult> IndexDocument(
        KnowledgeDocument document,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.Id))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Document ID is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(document.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(document.Content))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Content is required",
                Status = 400
            });
        }

        try
        {
            var indexed = await ragService.IndexDocumentAsync(document, cancellationToken);
            return Results.Ok(indexed);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Indexing Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Batch index multiple document chunks.
    /// </summary>
    private static async Task<IResult> IndexDocumentsBatch(
        IEnumerable<KnowledgeDocument> documents,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        var docList = documents.ToList();

        if (docList.Count == 0)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "At least one document is required",
                Status = 400
            });
        }

        try
        {
            var results = await ragService.IndexDocumentsBatchAsync(docList, cancellationToken);
            return Results.Ok(results);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Batch Indexing Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Delete a document chunk from the knowledge base.
    /// </summary>
    private static async Task<IResult> DeleteDocument(
        string documentId,
        [AsParameters] DeleteDocumentQuery query,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId query parameter is required",
                Status = 400
            });
        }

        try
        {
            var deleted = await ragService.DeleteDocumentAsync(documentId, query.TenantId, cancellationToken);
            return Results.Ok(new RagDeleteResult { Deleted = deleted, Count = deleted ? 1 : 0 });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Delete Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Delete all chunks for a source document.
    /// </summary>
    private static async Task<IResult> DeleteBySourceDocument(
        string sourceDocumentId,
        [AsParameters] DeleteDocumentQuery query,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId query parameter is required",
                Status = 400
            });
        }

        try
        {
            var count = await ragService.DeleteBySourceDocumentAsync(sourceDocumentId, query.TenantId, cancellationToken);
            return Results.Ok(new RagDeleteResult { Deleted = count > 0, Count = count });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Delete Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Generate embedding for text content.
    /// </summary>
    private static async Task<IResult> GetEmbedding(
        EmbeddingRequest request,
        IRagService ragService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Text is required",
                Status = 400
            });
        }

        try
        {
            var embedding = await ragService.GetEmbeddingAsync(request.Text, cancellationToken);
            return Results.Ok(new EmbeddingResult
            {
                Embedding = embedding,
                Dimensions = embedding.Length
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Embedding Generation Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Index a file into the knowledge base via unified pipeline.
    /// Uses OBO authentication to access user's files.
    /// </summary>
    private static async Task<IResult> IndexFile(
        FileIndexRequest request,
        IFileIndexingService fileIndexingService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.DriveId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DriveId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.ItemId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "ItemId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "FileName is required",
                Status = 400
            });
        }

        try
        {
            var result = await fileIndexingService.IndexFileAsync(request, httpContext, cancellationToken);

            if (!result.Success)
            {
                return Results.Problem(
                    title: "Indexing Failed",
                    detail: result.ErrorMessage,
                    statusCode: 500);
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                title: "Indexing Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Enqueue a file for background RAG indexing via job queue.
    /// Uses API key header validation for security (consistent with email webhook pattern).
    /// </summary>
    /// <remarks>
    /// This endpoint validates an API key header before enqueueing the job.
    /// The job handler (RagIndexingJobHandler) uses Pattern 6 (app-only auth) for SPE file access.
    /// Returns 202 Accepted with job tracking information for async processing.
    /// </remarks>
    private static async Task<IResult> EnqueueIndexing(
        [FromBody] FileIndexRequest request,
        HttpRequest httpRequest,
        JobSubmissionService jobSubmissionService,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");
        var traceId = httpRequest.HttpContext.TraceIdentifier;
        var correlationId = httpRequest.Headers["X-Correlation-Id"].FirstOrDefault() ?? traceId;

        // Step 1: Validate API key header
        var apiKey = httpRequest.Headers["X-Api-Key"].FirstOrDefault();
        var expectedApiKey = configuration["Rag:ApiKey"];

        if (string.IsNullOrEmpty(expectedApiKey))
        {
            logger.LogWarning("RAG API key not configured - rejecting request");
            return Results.Problem(
                title: "Configuration Error",
                detail: "RAG API key not configured on server",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
        {
            logger.LogWarning("Invalid or missing RAG API key for request {TraceId}", traceId);
            return Results.Problem(
                title: "Unauthorized",
                detail: "Invalid or missing API key",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Step 2: Validate required fields
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.DriveId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DriveId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.ItemId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "ItemId is required",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "FileName is required",
                Status = 400
            });
        }

        try
        {
            // Step 3: Build job payload
            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new RagIndexingJobPayload
            {
                TenantId = request.TenantId,
                DriveId = request.DriveId,
                ItemId = request.ItemId,
                FileName = request.FileName,
                DocumentId = request.DocumentId,
                KnowledgeSourceId = request.KnowledgeSourceId,
                KnowledgeSourceName = request.KnowledgeSourceName,
                Metadata = request.Metadata,
                Source = "EnqueueEndpoint",
                EnqueuedAt = DateTimeOffset.UtcNow
            }));

            // Step 4: Create and submit job
            var idempotencyKey = $"rag-index-{request.DriveId}-{request.ItemId}";
            var job = new JobContract
            {
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = request.ItemId,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
                Payload = jobPayload,
                MaxAttempts = 3
            };

            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            logger.LogInformation(
                "Enqueued RAG indexing job {JobId} for file {FileName} (DriveId: {DriveId}, ItemId: {ItemId})",
                job.JobId, request.FileName, request.DriveId, request.ItemId);

            return Results.Accepted(
                value: new EnqueueIndexingResponse
                {
                    Accepted = true,
                    JobId = job.JobId,
                    CorrelationId = correlationId,
                    IdempotencyKey = idempotencyKey,
                    Message = $"File {request.FileName} queued for RAG indexing"
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue RAG indexing job for {FileName}", request.FileName);
            return Results.Problem(
                title: "Enqueue Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Submit a bulk RAG indexing job.
    /// Admin endpoint that queries documents matching criteria and enqueues them for indexing.
    /// </summary>
    private static async Task<IResult> SubmitBulkIndexingJob(
        [FromBody] BulkIndexingRequest request,
        HttpRequest httpRequest,
        JobSubmissionService jobSubmissionService,
        BatchJobStatusStore statusStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");
        var correlationId = httpRequest.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpRequest.HttpContext.TraceIdentifier;

        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        try
        {
            // Build job payload from request
            var payload = new BulkRagIndexingPayload
            {
                TenantId = request.TenantId,
                Filter = request.Filter,
                MatterId = request.MatterId,
                CreatedAfter = request.CreatedAfter,
                CreatedBefore = request.CreatedBefore,
                DocumentType = request.DocumentType,
                MaxDocuments = request.MaxDocuments,
                MaxConcurrency = request.MaxConcurrency,
                ForceReindex = request.ForceReindex,
                Source = "Admin"
            };

            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(payload));

            // Create job contract
            var job = new JobContract
            {
                JobType = BulkRagIndexingJobHandler.JobTypeName,
                SubjectId = request.TenantId,
                CorrelationId = correlationId,
                IdempotencyKey = $"bulk-rag-{request.TenantId}-{DateTimeOffset.UtcNow.Ticks}",
                Payload = jobPayload,
                MaxAttempts = 1 // Bulk jobs should not auto-retry
            };

            // Create initial job status in cache (estimated total is 0 until job starts)
            // Note: BatchFiltersApplied is reused from email batch - we map RAG filters to compatible fields
            await statusStore.CreateJobStatusAsync(
                job.JobId.ToString(),
                new BatchFiltersApplied
                {
                    StartDate = request.CreatedAfter ?? DateTime.MinValue,
                    EndDate = request.CreatedBefore ?? DateTime.MaxValue,
                    StatusFilter = request.Filter // "unindexed", "all", etc.
                },
                estimatedTotalEmails: 0, // Actual count determined when job starts
                cancellationToken);

            // Submit job to queue
            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            logger.LogInformation(
                "Submitted bulk RAG indexing job {JobId} for tenant {TenantId}, filter={Filter}, maxDocs={MaxDocs}",
                job.JobId, request.TenantId, request.Filter, request.MaxDocuments);

            var statusUrl = $"/api/ai/rag/admin/bulk-index/{job.JobId}/status";

            return Results.Accepted(
                value: new BulkIndexingResponse
                {
                    JobId = job.JobId,
                    Status = "Pending",
                    Message = $"Bulk indexing job submitted. Use status endpoint to monitor progress.",
                    StatusUrl = statusUrl
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to submit bulk RAG indexing job for tenant {TenantId}", request.TenantId);
            return Results.Problem(
                title: "Submit Failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get status of a bulk RAG indexing job.
    /// </summary>
    private static async Task<IResult> GetBulkIndexingJobStatus(
        string jobId,
        BatchJobStatusStore statusStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "Job ID is required",
                Status = 400
            });
        }

        var status = await statusStore.GetJobStatusAsync(jobId, cancellationToken);

        if (status == null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Bulk indexing job '{jobId}' not found",
                Status = 404
            });
        }

        // Map BatchJobStatusResponse to BulkIndexingStatusResponse
        var response = new BulkIndexingStatusResponse
        {
            JobId = status.JobId,
            Status = status.Status.ToString(),
            TotalDocuments = status.TotalEmails, // TotalEmails is reused for documents
            ProcessedCount = status.ProcessedCount,
            ErrorCount = status.ErrorCount,
            SkippedCount = status.SkippedCount,
            PercentComplete = status.ProgressPercent,
            StartedAt = status.StartedAt.HasValue ? new DateTimeOffset(status.StartedAt.Value, TimeSpan.Zero) : null,
            CompletedAt = status.CompletedAt.HasValue ? new DateTimeOffset(status.CompletedAt.Value, TimeSpan.Zero) : null,
            RecentErrors = status.RecentErrors.Select(e => e.Message).ToList()
        };

        return Results.Ok(response);
    }
}

/// <summary>
/// Request model for RAG search.
/// </summary>
public record RagSearchRequest
{
    /// <summary>
    /// The search query text.
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Search options including tenant, filters, and limits.
    /// </summary>
    public required RagSearchOptions Options { get; init; }
}

/// <summary>
/// Query parameters for delete operations.
/// </summary>
public class DeleteDocumentQuery
{
    /// <summary>
    /// Tenant ID for routing to correct index.
    /// </summary>
    public string? TenantId { get; init; }
}

/// <summary>
/// Result of a delete operation.
/// </summary>
public record RagDeleteResult
{
    /// <summary>
    /// Whether any documents were deleted.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Number of documents deleted.
    /// </summary>
    public int Count { get; init; }
}

/// <summary>
/// Request model for embedding generation.
/// </summary>
public record EmbeddingRequest
{
    /// <summary>
    /// The text to generate an embedding for.
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// Result of embedding generation.
/// </summary>
public record EmbeddingResult
{
    /// <summary>
    /// The generated embedding vector.
    /// </summary>
    public ReadOnlyMemory<float> Embedding { get; init; }

    /// <summary>
    /// Number of dimensions in the embedding.
    /// </summary>
    public int Dimensions { get; init; }
}

/// <summary>
/// Response from enqueue indexing endpoint.
/// </summary>
public record EnqueueIndexingResponse
{
    /// <summary>
    /// Whether the job was accepted for processing.
    /// </summary>
    public bool Accepted { get; init; }

    /// <summary>
    /// The unique job identifier for tracking.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key for deduplication.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request model for bulk RAG indexing.
/// </summary>
public record BulkIndexingRequest
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Filter type: "unindexed" (default), "all".
    /// </summary>
    public string Filter { get; init; } = "unindexed";

    /// <summary>
    /// Optional Matter ID to filter documents.
    /// </summary>
    public string? MatterId { get; init; }

    /// <summary>
    /// Optional: Only index documents created after this date.
    /// </summary>
    public DateTime? CreatedAfter { get; init; }

    /// <summary>
    /// Optional: Only index documents created before this date.
    /// </summary>
    public DateTime? CreatedBefore { get; init; }

    /// <summary>
    /// Optional document type filter (e.g., ".pdf", ".docx").
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Maximum number of documents to process (default: 1000).
    /// </summary>
    public int MaxDocuments { get; init; } = 1000;

    /// <summary>
    /// Maximum concurrent document processing (default: 5).
    /// </summary>
    public int MaxConcurrency { get; init; } = 5;

    /// <summary>
    /// If true, reindex documents even if they have been indexed before.
    /// </summary>
    public bool ForceReindex { get; init; } = false;
}

/// <summary>
/// Response from bulk indexing submission.
/// </summary>
public record BulkIndexingResponse
{
    /// <summary>
    /// The unique job identifier for tracking.
    /// </summary>
    public Guid JobId { get; init; }

    /// <summary>
    /// Status of the job (Pending, InProgress, etc.).
    /// </summary>
    public string Status { get; init; } = "Pending";

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// URL to poll for status updates.
    /// </summary>
    public string StatusUrl { get; init; } = string.Empty;
}

/// <summary>
/// Response for bulk indexing job status.
/// </summary>
public record BulkIndexingStatusResponse
{
    /// <summary>
    /// The unique job identifier.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Current job status (Pending, InProgress, Completed, PartiallyCompleted, Failed).
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Total number of documents to process.
    /// </summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Number of documents successfully processed.
    /// </summary>
    public int ProcessedCount { get; init; }

    /// <summary>
    /// Number of documents that failed to process.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Number of documents skipped (already indexed).
    /// </summary>
    public int SkippedCount { get; init; }

    /// <summary>
    /// Percentage of completion (0-100).
    /// </summary>
    public double PercentComplete { get; init; }

    /// <summary>
    /// When the job was started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// When the job was completed (if finished).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Last few errors encountered (for debugging).
    /// </summary>
    public List<string> RecentErrors { get; init; } = [];
}
