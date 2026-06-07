using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
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

        // POST /api/ai/rag/send-to-index - Index documents by ID (for Dataverse ribbon button)
        // Uses user OBO authentication to access files
        // Updates Dataverse sprk_searchindexed fields after successful indexing
        group.MapPost("/send-to-index", SendToIndex)
            .AddTenantAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("RagSendToIndex")
            .WithSummary("Index documents by ID for semantic search")
            .WithDescription("Indexes one or more documents by DocumentId. Gets file details from Dataverse, indexes via OBO auth, and updates Dataverse tracking fields. Designed for Dataverse ribbon button integration.")
            .Produces<SendToIndexResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/rag/enqueue-indexing - Enqueue a file for background RAG indexing
        // Auth: Named RagApiKey scheme (task AUTHV2-045). Mapped on `app` (not `group`) so
        // the group's `.RequireAuthorization()` (JWT default) does NOT compose with the API
        // key policy — API key callers do not present a JWT, so AND-composition would 401 them.
        // Job handler uses app-only auth (Pattern 6) for SPE file access.
        app.MapPost("/api/ai/rag/enqueue-indexing", EnqueueIndexing)
            .RequireAuthorization(AuthPolicies.RagApiKey)
            // Task AUTHV2-049 — Use api-key-rag (300/min per API-key scheme) instead of ai-batch
            // (which keys on user oid). API-key callers have no oid claim, so ai-batch would have
            // bucketed all callers into the same partition under the "unknown" fallback key.
            .RequireRateLimiting("api-key-rag")
            .WithName("RagEnqueueIndexing")
            .WithTags("AI RAG")
            .WithSummary("Enqueue a file for background RAG indexing")
            .WithDescription("Validates API key (X-Api-Key) via the named RagApiKey scheme and enqueues file for async indexing. Used for background jobs, scheduled indexing, bulk operations, and automated testing.")
            .Produces<EnqueueIndexingResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
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

        // multi-container-multi-index-r1 FR-BFF-07 (task 016 — final wiring): the
        // client supplies `searchIndexName` at the top level of `RagSearchRequest`
        // (per the DTO contract in task 011), but `IRagService.SearchAsync(query,
        // options, ct)` consumes the value from `RagSearchOptions.SearchIndexName`
        // (added in task 014). Bridge the two here without mutating the caller's
        // request DTO. When the caller did NOT set `request.SearchIndexName`,
        // pass `request.Options` verbatim — preserves byte-for-byte backward-compat
        // (NFR-02) for existing callers and the unmodified service-level test
        // suite. ProblemDetails 400 (ADR-019) for INDEX_NOT_ALLOWED propagates
        // from the resolver via the generic 500 catch — see Step 9.5 notes.
        var effectiveOptions = !string.IsNullOrWhiteSpace(request.SearchIndexName)
            ? request.Options with { SearchIndexName = request.SearchIndexName }
            : request.Options;

        try
        {
            var response = await ragService.SearchAsync(request.Query, effectiveOptions, cancellationToken);
            return Results.Ok(response);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException)
        {
            // multi-container-multi-index-r1 FR-BFF-07 (task 016) — rethrow so the
            // global `UseExceptionHandler` middleware (MiddlewarePipelineExtensions)
            // renders the canonical ProblemDetails JSON per ADR-019. Without this,
            // the generic `catch (Exception)` below would convert
            // `INDEX_NOT_ALLOWED` (statusCode 400) into a 500 response — breaking
            // NFR-08 (rejected index name MUST surface as ProblemDetails 400).
            throw;
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 B7): NullRagService surfaced.
            return ex.AsFeatureDisabled503();
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
    /// <remarks>
    /// When DocumentId is provided, Dataverse tracking fields (sprk_searchindexed,
    /// sprk_searchindexedon, sprk_searchindexname) are updated after successful indexing.
    /// </remarks>
    private static async Task<IResult> IndexFile(
        FileIndexRequest request,
        IFileIndexingService fileIndexingService,
        IDocumentDataverseService dataverseService,
        IOptions<AnalysisOptions> analysisOptions,
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

            // Update Dataverse tracking fields when DocumentId is provided
            if (!string.IsNullOrEmpty(request.DocumentId))
            {
                var indexName = analysisOptions.Value.SharedIndexName;
                var updateRequest = new UpdateDocumentRequest
                {
                    SearchIndexed = true,
                    SearchIndexName = indexName,
                    SearchIndexedOn = DateTime.UtcNow
                };

                await dataverseService.UpdateDocumentAsync(request.DocumentId, updateRequest, cancellationToken);
            }

            return Results.Ok(result);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 1.5 round 4 (D-02 cluster exception): NullFileIndexingService surfaced.
            return ex.AsFeatureDisabled503();
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
    /// Index documents by DocumentId for semantic search.
    /// Designed for Dataverse ribbon button integration.
    /// </summary>
    /// <remarks>
    /// - Gets document details from Dataverse (including parent entity lookups)
    /// - Indexes file via OBO authentication
    /// - Updates Dataverse with search index tracking fields (sprk_searchindexed, etc.)
    /// - Returns results for each document processed
    /// </remarks>
    private static async Task<IResult> SendToIndex(
        [FromBody] SendToIndexRequest request,
        IFileIndexingService fileIndexingService,
        IDocumentDataverseService dataverseService,
        IOptions<AnalysisOptions> analysisOptions,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");

        // Validate request
        if (request.DocumentIds == null || request.DocumentIds.Count == 0)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "DocumentIds is required and must contain at least one document ID",
                Status = 400
            });
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Request",
                Detail = "TenantId is required",
                Status = 400
            });
        }

        var results = new List<SendToIndexDocumentResult>();
        var indexName = analysisOptions.Value.SharedIndexName;

        foreach (var documentId in request.DocumentIds)
        {
            try
            {
                // Step 1: Get document from Dataverse
                var document = await dataverseService.GetDocumentAsync(documentId, cancellationToken);
                if (document == null)
                {
                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = false,
                        ErrorMessage = "Document not found"
                    });
                    continue;
                }

                // Step 2: Validate document has file
                if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
                {
                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = false,
                        ErrorMessage = "Document does not have an associated file (missing DriveId or ItemId)"
                    });
                    continue;
                }

                // Step 3: Build parent entity context from document lookups
                ParentEntityContext? parentEntity = null;
                if (!string.IsNullOrEmpty(document.MatterId))
                {
                    parentEntity = new ParentEntityContext(
                        EntityType: "matter",
                        EntityId: document.MatterId,
                        EntityName: document.MatterName ?? "Unknown Matter"
                    );
                }
                else if (!string.IsNullOrEmpty(document.ProjectId))
                {
                    parentEntity = new ParentEntityContext(
                        EntityType: "project",
                        EntityId: document.ProjectId,
                        EntityName: document.ProjectName ?? "Unknown Project"
                    );
                }
                else if (!string.IsNullOrEmpty(document.InvoiceId))
                {
                    parentEntity = new ParentEntityContext(
                        EntityType: "invoice",
                        EntityId: document.InvoiceId,
                        EntityName: document.InvoiceName ?? "Unknown Invoice"
                    );
                }

                // Step 4: Build file index request
                var indexRequest = new FileIndexRequest
                {
                    TenantId = request.TenantId,
                    DriveId = document.GraphDriveId,
                    ItemId = document.GraphItemId,
                    FileName = document.FileName ?? document.Name,
                    DocumentId = documentId,
                    ParentEntity = parentEntity
                };

                // Step 5: Index via OBO authentication
                var indexResult = await fileIndexingService.IndexFileAsync(indexRequest, httpContext, cancellationToken);

                if (indexResult.Success)
                {
                    // Step 6: Update Dataverse with search index fields
                    var updateRequest = new UpdateDocumentRequest
                    {
                        SearchIndexed = true,
                        SearchIndexName = indexName,
                        SearchIndexedOn = DateTime.UtcNow
                    };

                    await dataverseService.UpdateDocumentAsync(documentId, updateRequest, cancellationToken);

                    logger.LogInformation(
                        "Document {DocumentId} indexed successfully: {ChunksIndexed} chunks to {IndexName}",
                        documentId, indexResult.ChunksIndexed, indexName);

                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = true,
                        ChunksIndexed = indexResult.ChunksIndexed,
                        IndexName = indexName,
                        ParentEntityType = parentEntity?.EntityType,
                        ParentEntityId = parentEntity?.EntityId
                    });
                }
                else
                {
                    logger.LogWarning(
                        "Document {DocumentId} indexing failed: {Error}",
                        documentId, indexResult.ErrorMessage);

                    results.Add(new SendToIndexDocumentResult
                    {
                        DocumentId = documentId,
                        Success = false,
                        ErrorMessage = indexResult.ErrorMessage
                    });
                }
            }
            catch (FeatureDisabledException ex)
            {
                // Task 011 Phase 1b Tier 1.5 round 4 (D-02 cluster exception): NullFileIndexingService surfaced.
                // Kill-switch state is request-global, not per-document — short-circuit the whole batch
                // with a 503 ProblemDetails rather than recording per-document failures that would mislead
                // operators into chasing N "indexing failed" entries when the root cause is one kill switch.
                return ex.AsFeatureDisabled503();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing document {DocumentId} for indexing", documentId);
                results.Add(new SendToIndexDocumentResult
                {
                    DocumentId = documentId,
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return Results.Ok(new SendToIndexResponse
        {
            TotalRequested = request.DocumentIds.Count,
            SuccessCount = results.Count(r => r.Success),
            FailedCount = results.Count(r => !r.Success),
            Results = results
        });
    }

    /// <summary>
    /// Enqueue a file for background RAG indexing via job queue.
    /// Authorization enforced upstream by the <see cref="AuthPolicies.RagApiKey"/> policy
    /// (task AUTHV2-045 — named API key scheme). The job handler
    /// (RagIndexingJobHandler) uses Pattern 6 (app-only auth) for SPE file access.
    /// </summary>
    /// <remarks>
    /// Returns 202 Accepted with job tracking information for async processing.
    /// </remarks>
    private static async Task<IResult> EnqueueIndexing(
        [FromBody] FileIndexRequest request,
        HttpRequest httpRequest,
        JobSubmissionService jobSubmissionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RagEndpoints");
        var traceId = httpRequest.HttpContext.TraceIdentifier;
        var correlationId = httpRequest.Headers["X-Correlation-Id"].FirstOrDefault() ?? traceId;

        // API key validation is performed by ApiKeyAuthenticationHandler (RagApiKey scheme)
        // bound via .RequireAuthorization(AuthPolicies.RagApiKey) on the endpoint registration.

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
                ParentEntity = request.ParentEntity,
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

    /// <summary>
    /// Optional explicit Azure AI Search index name to target for this request. When
    /// provided (non-null and non-empty), the BFF resolver MUST use this index in place
    /// of the Dataverse / appsettings fallback chain — subject to the allow-list in
    /// <c>appsettings.AiSearch.AllowedIndexes</c>. When omitted (null or empty), the
    /// existing 2-tier resolver chain (<c>sprk_aiknowledgedeployment</c> Dataverse entity
    /// then <c>appsettings.AiSearch.KnowledgeIndexName</c>) is used unchanged.
    /// JSON deserialization is forward-compatible: requests without this field continue
    /// to work as today (FR-BFF-05, NFR-02).
    /// </summary>
    public string? SearchIndexName { get; init; }
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

/// <summary>
/// Request model for send-to-index endpoint (Dataverse ribbon button integration).
/// </summary>
public record SendToIndexRequest
{
    /// <summary>
    /// List of Dataverse document IDs to index.
    /// </summary>
    public required List<string> DocumentIds { get; init; }

    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public required string TenantId { get; init; }
}

/// <summary>
/// Response from send-to-index endpoint.
/// </summary>
public record SendToIndexResponse
{
    /// <summary>
    /// Total number of documents requested for indexing.
    /// </summary>
    public int TotalRequested { get; init; }

    /// <summary>
    /// Number of documents successfully indexed.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of documents that failed to index.
    /// </summary>
    public int FailedCount { get; init; }

    /// <summary>
    /// Per-document results.
    /// </summary>
    public List<SendToIndexDocumentResult> Results { get; init; } = [];
}

/// <summary>
/// Result for a single document in send-to-index operation.
/// </summary>
public record SendToIndexDocumentResult
{
    /// <summary>
    /// The document ID that was processed.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Whether the indexing succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of chunks indexed (if successful).
    /// </summary>
    public int ChunksIndexed { get; init; }

    /// <summary>
    /// Name of the search index used.
    /// </summary>
    public string? IndexName { get; init; }

    /// <summary>
    /// Parent entity type if document was associated with an entity.
    /// </summary>
    public string? ParentEntityType { get; init; }

    /// <summary>
    /// Parent entity ID if document was associated with an entity.
    /// </summary>
    public string? ParentEntityId { get; init; }

    /// <summary>
    /// Error message if indexing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
