using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

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
            .WithName("RagDeleteDocument")
            .WithSummary("Delete a document chunk from the knowledge base")
            .Produces<RagDeleteResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // DELETE /api/ai/rag/source/{sourceDocumentId} - Delete all chunks for a source document
        group.MapDelete("/source/{sourceDocumentId}", DeleteBySourceDocument)
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
