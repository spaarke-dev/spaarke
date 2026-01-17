using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
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
        FileIndexRequest request,
        HttpRequest httpRequest,
        JobSubmissionService jobSubmissionService,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
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
