using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Knowledge base management endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides CRUD operations for indexed documents, a test-search endpoint for RAG quality validation,
/// and a health endpoint returning document counts and last-updated timestamps.
/// </summary>
/// <remarks>
/// All endpoints are registered under /api/ai/knowledge and require AiAuthorizationFilter (ADR-008).
/// Search is scoped to tenantId from claims or header per ADR-014.
/// Registered in Program.cs as: app.MapKnowledgeBaseEndpoints()
/// </remarks>
public static class KnowledgeBaseEndpoints
{
    // Header name used for tenantId when not resolvable from claims (matches existing API convention)
    private const string TenantIdHeader = "X-Tenant-Id";

    public static IEndpointRouteBuilder MapKnowledgeBaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/knowledge")
            .RequireAuthorization()
            .WithTags("AI Knowledge Base");

        // GET /api/ai/knowledge/indexes/health — index health: doc counts and last-updated
        group.MapGet("/indexes/health", GetIndexHealth)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .RequireRateLimiting("ai-batch")
            .WithName("KnowledgeBaseHealth")
            .WithSummary("Get knowledge base index health")
            .WithDescription("Returns document counts for knowledge and discovery indexes and last-updated timestamps.")
            .Produces<KnowledgeIndexHealthResult>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // GET /api/ai/knowledge/indexes/{indexName}/documents — paged document list for tenant
        group.MapGet("/indexes/{indexName}/documents", GetIndexedDocuments)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .RequireRateLimiting("ai-batch")
            .WithName("KnowledgeBaseListDocuments")
            .WithSummary("List indexed documents in an index")
            .WithDescription("Returns a paged list of document metadata indexed for the requesting tenant in the specified index.")
            .Produces<KnowledgeIndexedDocumentsResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId} — remove all chunks for a document
        group.MapDelete("/indexes/{indexName}/documents/{documentId}", DeleteIndexedDocument)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .WithName("KnowledgeBaseDeleteDocument")
            .WithSummary("Delete a document from an index")
            .WithDescription("Removes all chunks for the specified document from the named index, scoped to the requesting tenant.")
            .Produces<KnowledgeDeleteResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // POST /api/ai/knowledge/indexes/reindex/{documentId} — trigger re-indexing job
        group.MapPost("/indexes/reindex/{documentId}", ReindexDocument)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .RequireRateLimiting("ai-batch")
            .WithName("KnowledgeBaseReindexDocument")
            .WithSummary("Trigger re-indexing for a document")
            .WithDescription("Enqueues a re-indexing job for the specified document. The job handler fetches the document from SPE and reindexes it.")
            .Produces<KnowledgeReindexResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        // POST /api/ai/knowledge/test-search — raw RAG results for evaluation/admin
        group.MapPost("/test-search", TestSearch)
            .AddEndpointFilter<AiAuthorizationFilter>()
            .RequireRateLimiting("ai-batch")
            .WithName("KnowledgeBaseTestSearch")
            .WithSummary("Test-search the knowledge base")
            .WithDescription("Executes a raw RAG search query and returns results for evaluation or admin validation. Scoped to the requesting tenant (ADR-014).")
            .Produces<KnowledgeTestSearchResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    // -------------------------------------------------------------------------
    // Endpoint handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// GET /api/ai/knowledge/indexes/health
    /// Returns document counts in the knowledge and discovery indexes for the tenant, plus last-updated timestamp.
    /// </summary>
    private static async Task<IResult> GetIndexHealth(
        HttpContext httpContext,
        IRagService ragService,
        IOptions<AiSearchOptions> aiSearchOptions,
        SearchIndexClient searchIndexClient,
        ILogger<RagIndexingPipeline> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "TenantId could not be resolved from claims or header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogDebug("Knowledge base health check requested for tenant {TenantId}", tenantId);

        try
        {
            var options = aiSearchOptions.Value;
            var knowledgeFilter = $"tenantId eq '{EscapeOData(tenantId)}'";

            // Query both indexes in parallel for document counts
            var knowledgeClient = searchIndexClient.GetSearchClient(options.KnowledgeIndexName);
            var discoveryClient = searchIndexClient.GetSearchClient(options.DiscoveryIndexName);

            var knowledgeCountTask = GetTenantDocumentCountAsync(knowledgeClient, knowledgeFilter, cancellationToken);
            var discoveryCountTask = GetTenantDocumentCountAsync(discoveryClient, knowledgeFilter, cancellationToken);

            await Task.WhenAll(knowledgeCountTask, discoveryCountTask);

            var result = new KnowledgeIndexHealthResult
            {
                KnowledgeDocCount = knowledgeCountTask.Result,
                DiscoveryDocCount = discoveryCountTask.Result,
                LastUpdated = DateTimeOffset.UtcNow,
                KnowledgeIndexName = options.KnowledgeIndexName,
                DiscoveryIndexName = options.DiscoveryIndexName
            };

            logger.LogInformation(
                "Knowledge base health: tenant={TenantId} knowledgeDocs={KnowledgeCount} discoveryDocs={DiscoveryCount}",
                tenantId, result.KnowledgeDocCount, result.DiscoveryDocCount);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching knowledge base health for tenant {TenantId}", tenantId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to retrieve knowledge base health.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// GET /api/ai/knowledge/indexes/{indexName}/documents
    /// Returns a paged list of unique document IDs indexed for the tenant in the named index.
    /// </summary>
    private static async Task<IResult> GetIndexedDocuments(
        string indexName,
        HttpContext httpContext,
        IOptions<AiSearchOptions> aiSearchOptions,
        SearchIndexClient searchIndexClient,
        ILogger<RagIndexingPipeline> logger,
        CancellationToken cancellationToken,
        [Microsoft.AspNetCore.Mvc.FromQuery] int page = 1,
        [Microsoft.AspNetCore.Mvc.FromQuery] int pageSize = 50)
    {
        var tenantId = ResolveTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "TenantId could not be resolved from claims or header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var options = aiSearchOptions.Value;
        var validIndexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            options.KnowledgeIndexName,
            options.DiscoveryIndexName
        };

        if (!validIndexNames.Contains(indexName))
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Index '{indexName}' not found. Valid indexes: {string.Join(", ", validIndexNames)}",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        logger.LogDebug(
            "Listing indexed documents for tenant {TenantId} in index {IndexName} (page={Page}, size={Size})",
            tenantId, indexName, page, pageSize);

        try
        {
            var searchClient = searchIndexClient.GetSearchClient(indexName);
            var filter = $"tenantId eq '{EscapeOData(tenantId)}'";
            var skip = (page - 1) * pageSize;

            var searchOptions = new SearchOptions
            {
                Filter = filter,
                Size = pageSize,
                Skip = skip,
                Select = { "id", "documentId", "fileName", "createdAt", "updatedAt" },
                IncludeTotalCount = true
            };

            var response = await searchClient.SearchAsync<KnowledgeDocument>("*", searchOptions, cancellationToken);
            var results = new List<KnowledgeDocumentSummary>();

            await foreach (var item in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
            {
                if (item.Document != null)
                {
                    results.Add(new KnowledgeDocumentSummary
                    {
                        ChunkId = item.Document.Id,
                        DocumentId = item.Document.DocumentId,
                        FileName = item.Document.FileName,
                        CreatedAt = item.Document.CreatedAt,
                        UpdatedAt = item.Document.UpdatedAt
                    });
                }
            }

            var result = new KnowledgeIndexedDocumentsResult
            {
                IndexName = indexName,
                Documents = results,
                Page = page,
                PageSize = pageSize,
                TotalCount = response.Value.TotalCount ?? 0
            };

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error listing indexed documents for tenant {TenantId} in index {IndexName}",
                tenantId, indexName);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to retrieve indexed documents.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// DELETE /api/ai/knowledge/indexes/{indexName}/documents/{documentId}
    /// Removes all chunks for the document from the specified index, scoped to tenant.
    /// </summary>
    private static async Task<IResult> DeleteIndexedDocument(
        string indexName,
        string documentId,
        HttpContext httpContext,
        IOptions<AiSearchOptions> aiSearchOptions,
        SearchIndexClient searchIndexClient,
        IRagService ragService,
        ILogger<RagIndexingPipeline> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "TenantId could not be resolved from claims or header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "documentId is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var options = aiSearchOptions.Value;
        var validIndexNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            options.KnowledgeIndexName,
            options.DiscoveryIndexName
        };

        if (!validIndexNames.Contains(indexName))
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Index '{indexName}' not found. Valid indexes: {string.Join(", ", validIndexNames)}",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        logger.LogInformation(
            "Deleting document {DocumentId} from index {IndexName} for tenant {TenantId}",
            documentId, indexName, tenantId);

        try
        {
            int chunksDeleted;

            // Route to appropriate deletion method based on index
            if (indexName.Equals(options.KnowledgeIndexName, StringComparison.OrdinalIgnoreCase))
            {
                // Use IRagService for knowledge index — it manages the primary index
                chunksDeleted = await ragService.DeleteBySourceDocumentAsync(documentId, tenantId, cancellationToken);
            }
            else
            {
                // Delete directly from the discovery index using SearchClient
                var searchClient = searchIndexClient.GetSearchClient(indexName);
                chunksDeleted = await DeleteChunksFromIndexAsync(
                    searchClient, documentId, tenantId, cancellationToken);
            }

            if (chunksDeleted == 0)
            {
                return Results.NotFound(new KnowledgeDeleteResult
                {
                    DocumentId = documentId,
                    IndexName = indexName,
                    ChunksDeleted = 0,
                    Message = $"No chunks found for document '{documentId}' in index '{indexName}'."
                });
            }

            logger.LogInformation(
                "Deleted {ChunksDeleted} chunks for document {DocumentId} from index {IndexName} (tenant={TenantId})",
                chunksDeleted, documentId, indexName, tenantId);

            return Results.Ok(new KnowledgeDeleteResult
            {
                DocumentId = documentId,
                IndexName = indexName,
                ChunksDeleted = chunksDeleted,
                Message = $"Deleted {chunksDeleted} chunk(s) for document '{documentId}'."
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error deleting document {DocumentId} from index {IndexName} for tenant {TenantId}",
                documentId, indexName, tenantId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to delete document from index.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// POST /api/ai/knowledge/indexes/reindex/{documentId}
    /// Triggers a background re-indexing job for the specified document.
    /// Returns 202 Accepted with job tracking info.
    /// </summary>
    private static async Task<IResult> ReindexDocument(
        string documentId,
        KnowledgeReindexRequest request,
        HttpContext httpContext,
        JobSubmissionService jobSubmissionService,
        ILogger<RagIndexingPipeline> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "TenantId could not be resolved from claims or header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "documentId is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation(
            "Reindex requested for document {DocumentId}, tenant {TenantId}",
            documentId, tenantId);

        try
        {
            var jobId = Guid.NewGuid();
            var idempotencyKey = $"rag-reindex-{documentId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var jobPayload = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new RagIndexingJobPayload
                {
                    TenantId = tenantId,
                    DriveId = request.DriveId ?? string.Empty,
                    ItemId = documentId,
                    DocumentId = documentId,
                    FileName = request.FileName ?? string.Empty
                }));

            var job = new JobContract
            {
                JobId = jobId,
                JobType = "RagIndexing",
                IdempotencyKey = idempotencyKey,
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = jobPayload
            };

            await jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            logger.LogInformation(
                "Reindex job {JobId} submitted for document {DocumentId} (tenant={TenantId})",
                jobId, documentId, tenantId);

            return Results.Accepted(
                $"/api/ai/knowledge/indexes/reindex/{documentId}",
                new KnowledgeReindexResult
                {
                    DocumentId = documentId,
                    JobId = jobId,
                    Status = "Queued",
                    Message = "Reindex job submitted successfully. Processing will begin shortly."
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error submitting reindex job for document {DocumentId} (tenant={TenantId})",
                documentId, tenantId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to submit reindex job.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// POST /api/ai/knowledge/test-search
    /// Executes a raw RAG search query and returns results. For evaluation/admin use.
    /// Scoped to the requesting tenant per ADR-014.
    /// </summary>
    private static async Task<IResult> TestSearch(
        KnowledgeTestSearchRequest request,
        HttpContext httpContext,
        IRagService ragService,
        ILogger<RagIndexingPipeline> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "TenantId could not be resolved from claims or header.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Query is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var top = Math.Clamp(request.Top ?? 5, 1, 20);

        logger.LogInformation(
            "Test-search requested: query={Query}, indexName={IndexName}, top={Top}, tenant={TenantId}",
            request.Query, request.IndexName ?? "default", top, tenantId);

        try
        {
            var searchOptions = new RagSearchOptions
            {
                TenantId = tenantId,
                TopK = top,
                UseSemanticRanking = true,
                UseVectorSearch = true,
                UseKeywordSearch = true
            };

            var response = await ragService.SearchAsync(request.Query, searchOptions, cancellationToken);

            var result = new KnowledgeTestSearchResult
            {
                Query = request.Query,
                IndexName = request.IndexName ?? "knowledge-index",
                TenantId = tenantId,
                ResultCount = response.Results.Count,
                SearchDurationMs = response.SearchDurationMs,
                EmbeddingDurationMs = response.EmbeddingDurationMs,
                EmbeddingCacheHit = response.EmbeddingCacheHit,
                Results = response.Results.Select(r => new KnowledgeTestSearchResultItem
                {
                    Id = r.Id,
                    DocumentId = r.DocumentId,
                    DocumentName = r.DocumentName,
                    Content = r.Content,
                    Score = r.Score,
                    SemanticScore = r.SemanticScore,
                    ChunkIndex = r.ChunkIndex,
                    ChunkCount = r.ChunkCount
                }).ToList()
            };

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error executing test-search for tenant {TenantId}, query={Query}",
                tenantId, request.Query);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to execute test search.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the tenantId from JWT claims (tid or oid) or from the X-Tenant-Id header (ADR-014).
    /// </summary>
    private static string? ResolveTenantId(HttpContext httpContext)
    {
        // Prefer the 'tid' (tenant) claim from Azure AD JWT
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        // Fallback to X-Tenant-Id header (admin/service-to-service scenarios)
        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers[TenantIdHeader].FirstOrDefault();
        }

        return tenantId;
    }

    /// <summary>
    /// Gets the count of documents in an index that match the given OData filter.
    /// </summary>
    private static async Task<long> GetTenantDocumentCountAsync(
        SearchClient searchClient,
        string filter,
        CancellationToken cancellationToken)
    {
        var options = new SearchOptions
        {
            Filter = filter,
            Size = 0,
            IncludeTotalCount = true
        };

        var response = await searchClient.SearchAsync<KnowledgeDocument>("*", options, cancellationToken);
        return response.Value.TotalCount ?? 0;
    }

    /// <summary>
    /// Deletes all chunks for <paramref name="documentId"/> from the specified search client,
    /// scoped to <paramref name="tenantId"/>. Returns count of deleted chunks.
    /// </summary>
    private static async Task<int> DeleteChunksFromIndexAsync(
        SearchClient searchClient,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var filter = $"documentId eq '{EscapeOData(documentId)}' and tenantId eq '{EscapeOData(tenantId)}'";
        var options = new SearchOptions
        {
            Filter = filter,
            Size = 1000,
            Select = { "id" }
        };

        var response = await searchClient.SearchAsync<KnowledgeDocument>("*", options, cancellationToken);
        var idsToDelete = new List<string>();

        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(result.Document?.Id))
            {
                idsToDelete.Add(result.Document.Id);
            }
        }

        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        var deleteResponse = await searchClient.DeleteDocumentsAsync(
            "id", idsToDelete, cancellationToken: cancellationToken);
        return deleteResponse.Value.Results.Count(r => r.Succeeded);
    }

    /// <summary>
    /// Escapes single quotes in OData filter values (single-quote doubling per OData spec).
    /// </summary>
    private static string EscapeOData(string value) => value.Replace("'", "''");
}

// -------------------------------------------------------------------------
// Request / Response models for KnowledgeBaseEndpoints
// -------------------------------------------------------------------------

/// <summary>
/// Health summary for the knowledge base indexes.
/// </summary>
public record KnowledgeIndexHealthResult
{
    /// <summary>Total chunk count in the knowledge index for the requesting tenant.</summary>
    public long KnowledgeDocCount { get; init; }

    /// <summary>Total chunk count in the discovery index for the requesting tenant.</summary>
    public long DiscoveryDocCount { get; init; }

    /// <summary>Timestamp when this health check was performed.</summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>Name of the knowledge index being queried.</summary>
    public string KnowledgeIndexName { get; init; } = string.Empty;

    /// <summary>Name of the discovery index being queried.</summary>
    public string DiscoveryIndexName { get; init; } = string.Empty;
}

/// <summary>
/// Paged list of document chunk summaries for a given index and tenant.
/// </summary>
public record KnowledgeIndexedDocumentsResult
{
    /// <summary>Name of the index queried.</summary>
    public string IndexName { get; init; } = string.Empty;

    /// <summary>Page of documents returned.</summary>
    public List<KnowledgeDocumentSummary> Documents { get; init; } = [];

    /// <summary>Current page number (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Page size used.</summary>
    public int PageSize { get; init; }

    /// <summary>Total count of matching chunks in the index for this tenant.</summary>
    public long TotalCount { get; init; }
}

/// <summary>
/// Summary of a single indexed document chunk.
/// </summary>
public record KnowledgeDocumentSummary
{
    /// <summary>The chunk ID (format: {documentId}_{suffix}_{chunkIndex}).</summary>
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>The source document ID.</summary>
    public string? DocumentId { get; init; }

    /// <summary>The source document file name.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>When this chunk was indexed.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When this chunk was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Result of a delete-document-from-index operation.
/// </summary>
public record KnowledgeDeleteResult
{
    /// <summary>The document ID that was targeted.</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>The index from which chunks were deleted.</summary>
    public string IndexName { get; init; } = string.Empty;

    /// <summary>Number of chunks deleted.</summary>
    public int ChunksDeleted { get; init; }

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request body for POST /api/ai/knowledge/indexes/reindex/{documentId}.
/// </summary>
public record KnowledgeReindexRequest
{
    /// <summary>SharePoint Embedded drive ID (required for RagIndexingJobHandler to fetch the file).</summary>
    public string? DriveId { get; init; }

    /// <summary>File name — stored on the indexed chunks for display purposes.</summary>
    public string? FileName { get; init; }
}

/// <summary>
/// Result of a reindex-document operation (202 Accepted).
/// </summary>
public record KnowledgeReindexResult
{
    /// <summary>The document ID that will be re-indexed.</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>The background job ID for tracking.</summary>
    public Guid JobId { get; init; }

    /// <summary>Job status (e.g., "Queued").</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Human-readable summary message.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request body for POST /api/ai/knowledge/test-search.
/// </summary>
public record KnowledgeTestSearchRequest
{
    /// <summary>The search query text.</summary>
    public required string Query { get; init; }

    /// <summary>Target index name. Defaults to the knowledge index when not specified.</summary>
    public string? IndexName { get; init; }

    /// <summary>Number of results to return (1–20, default 5).</summary>
    public int? Top { get; init; }
}

/// <summary>
/// Raw RAG search results for evaluation/admin purposes.
/// </summary>
public record KnowledgeTestSearchResult
{
    /// <summary>The original query.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Index that was searched.</summary>
    public string IndexName { get; init; } = string.Empty;

    /// <summary>Tenant that the search was scoped to (ADR-014).</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Number of results returned.</summary>
    public int ResultCount { get; init; }

    /// <summary>Time taken for the search in milliseconds.</summary>
    public long SearchDurationMs { get; init; }

    /// <summary>Time taken for embedding generation in milliseconds.</summary>
    public long EmbeddingDurationMs { get; init; }

    /// <summary>Whether the embedding was served from cache.</summary>
    public bool EmbeddingCacheHit { get; init; }

    /// <summary>The search result items.</summary>
    public List<KnowledgeTestSearchResultItem> Results { get; init; } = [];
}

/// <summary>
/// A single result item from a test-search operation.
/// </summary>
public record KnowledgeTestSearchResultItem
{
    /// <summary>Chunk ID.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Source document ID.</summary>
    public string? DocumentId { get; init; }

    /// <summary>Source document name.</summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>Chunk content text.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Relevance score (0.0–1.0).</summary>
    public double Score { get; init; }

    /// <summary>Semantic ranking score (when semantic ranking was applied).</summary>
    public double? SemanticScore { get; init; }

    /// <summary>Chunk index within the source document.</summary>
    public int ChunkIndex { get; init; }

    /// <summary>Total chunks in the source document.</summary>
    public int ChunkCount { get; init; }
}
