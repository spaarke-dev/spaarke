using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Rag;

/// <summary>
/// Implementation of IKnowledgeDeploymentService for Model 1 (Shared Index with Tenant Filtering).
/// Handles document chunking, embedding, and indexing into Azure AI Search.
/// </summary>
public class KnowledgeDeploymentService : IKnowledgeDeploymentService
{
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly IRagService _ragService;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<KnowledgeDeploymentService> _logger;

    // Default chunking settings (overridable per deployment)
    private const int DefaultChunkSize = 1000;
    private const int DefaultChunkOverlap = 200;

    public KnowledgeDeploymentService(
        IRagService ragService,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<KnowledgeDeploymentService> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.AiSearchEndpoint))
            throw new InvalidOperationException("AiSearchEndpoint is not configured.");
        if (string.IsNullOrWhiteSpace(_options.AiSearchKey))
            throw new InvalidOperationException("AiSearchKey is not configured.");

        var searchUri = new Uri(_options.AiSearchEndpoint);
        var credential = new AzureKeyCredential(_options.AiSearchKey);

        // Use knowledge index (not records index)
        var knowledgeIndexName = _options.KnowledgeIndexName ?? "spaarke-knowledge-shared";
        _searchClient = new SearchClient(searchUri, knowledgeIndexName, credential);
        _indexClient = new SearchIndexClient(searchUri, credential);
    }

    public Task<KnowledgeDeployment?> GetDeploymentAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        // For Model 1, return a default shared deployment configuration
        // In production, this would load from sprk_knowledgedeployment entity in Dataverse
        var deployment = new KnowledgeDeployment
        {
            Id = Guid.Empty,
            Name = "Shared Knowledge Index",
            CustomerId = customerId,
            Model = DeploymentModel.Shared,
            IndexName = _options.KnowledgeIndexName ?? "spaarke-knowledge-shared",
            TenantFilterField = "customerId",
            IsActive = true,
            ChunkSize = DefaultChunkSize,
            ChunkOverlap = DefaultChunkOverlap
        };

        return Task.FromResult<KnowledgeDeployment?>(deployment);
    }

    public async Task<IndexDocumentResult> IndexDocumentAsync(
        IndexDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Indexing document {DocumentId} for customer {CustomerId}, content length {Length}",
            request.DocumentId, request.CustomerId, request.DocumentContent.Length);

        try
        {
            // Get deployment config
            var deployment = await GetDeploymentAsync(request.CustomerId, cancellationToken);
            if (deployment == null || !deployment.IsActive)
            {
                return IndexDocumentResult.Fail("No active knowledge deployment found for customer");
            }

            // Chunk the document
            var chunks = ChunkDocument(
                request.DocumentContent,
                deployment.ChunkSize,
                deployment.ChunkOverlap);

            _logger.LogDebug(
                "Document {DocumentId} split into {ChunkCount} chunks",
                request.DocumentId, chunks.Count);

            // Generate embeddings and build index documents
            var indexDocuments = new List<KnowledgeIndexDocument>();
            var failedChunks = 0;

            for (var i = 0; i < chunks.Count; i++)
            {
                try
                {
                    var embedding = await _ragService.GenerateEmbeddingAsync(chunks[i], cancellationToken);

                    var indexDoc = new KnowledgeIndexDocument
                    {
                        Id = $"{request.DocumentId}_{i}",
                        CustomerId = request.CustomerId.ToString(),
                        KnowledgeSourceId = request.KnowledgeSourceId.ToString(),
                        DocumentId = request.DocumentId,
                        ChunkIndex = i,
                        ChunkContent = chunks[i],
                        ContentVector = embedding,
                        DocumentTitle = request.DocumentTitle,
                        DocumentFileName = request.DocumentFileName,
                        KnowledgeType = request.KnowledgeType,
                        Category = request.Category,
                        Tags = request.Tags,
                        IsPublic = request.IsPublic,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                        SourceMetadata = request.SourceMetadata
                    };

                    indexDocuments.Add(indexDoc);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to generate embedding for chunk {ChunkIndex} of document {DocumentId}",
                        i, request.DocumentId);
                    failedChunks++;
                }
            }

            if (indexDocuments.Count == 0)
            {
                return IndexDocumentResult.Fail(
                    "Failed to process any document chunks",
                    failedChunks,
                    stopwatch.ElapsedMilliseconds);
            }

            // Upload to Azure AI Search
            var response = await _searchClient.UploadDocumentsAsync(indexDocuments, cancellationToken: cancellationToken);

            var successCount = response.Value.Results.Count(r => r.Succeeded);
            var errorCount = response.Value.Results.Count(r => !r.Succeeded);

            stopwatch.Stop();

            _logger.LogInformation(
                "Indexed document {DocumentId}: {SuccessCount} chunks uploaded, {ErrorCount} failed in {DurationMs}ms",
                request.DocumentId, successCount, errorCount, stopwatch.ElapsedMilliseconds);

            if (errorCount > 0)
            {
                var errors = response.Value.Results
                    .Where(r => !r.Succeeded)
                    .Select(r => r.ErrorMessage)
                    .Take(3);
                _logger.LogWarning(
                    "Some chunks failed to index for {DocumentId}: {Errors}",
                    request.DocumentId, string.Join("; ", errors));
            }

            return IndexDocumentResult.Ok(successCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Failed to index document {DocumentId}: {Error}",
                request.DocumentId, ex.Message);

            return IndexDocumentResult.Fail(
                $"Indexing failed: {ex.Message}",
                durationMs: stopwatch.ElapsedMilliseconds);
        }
    }

    public async Task<RemoveDocumentResult> RemoveDocumentAsync(
        Guid customerId,
        string documentId,
        Guid? knowledgeSourceId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Removing document {DocumentId} for customer {CustomerId}",
            documentId, customerId);

        try
        {
            // Build filter to find all chunks for this document
            var filter = $"customerId eq '{customerId}' and documentId eq '{documentId}'";
            if (knowledgeSourceId.HasValue)
            {
                filter += $" and knowledgeSourceId eq '{knowledgeSourceId}'";
            }

            // Search for all chunks
            var searchOptions = new SearchOptions
            {
                Filter = filter,
                Size = 1000, // Max batch size
                Select = { "id" }
            };

            var response = await _searchClient.SearchAsync<KnowledgeIndexDocument>("*", searchOptions, cancellationToken);

            var idsToDelete = new List<string>();
            await foreach (var result in response.Value.GetResultsAsync())
            {
                idsToDelete.Add(result.Document.Id);
            }

            if (idsToDelete.Count == 0)
            {
                _logger.LogDebug("No chunks found to delete for document {DocumentId}", documentId);
                return RemoveDocumentResult.Ok(0);
            }

            // Delete chunks
            var deleteResponse = await _searchClient.DeleteDocumentsAsync(
                "id",
                idsToDelete,
                cancellationToken: cancellationToken);

            var deletedCount = deleteResponse.Value.Results.Count(r => r.Succeeded);

            _logger.LogInformation(
                "Removed {Count} chunks for document {DocumentId}",
                deletedCount, documentId);

            return RemoveDocumentResult.Ok(deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to remove document {DocumentId}: {Error}",
                documentId, ex.Message);

            return RemoveDocumentResult.Fail($"Removal failed: {ex.Message}");
        }
    }

    public Task<ReindexResult> ReindexKnowledgeSourceAsync(
        Guid knowledgeSourceId,
        CancellationToken cancellationToken = default)
    {
        // This would:
        // 1. Get all documents from the knowledge source
        // 2. Remove existing chunks
        // 3. Re-chunk and re-index all documents
        // For now, return not implemented
        _logger.LogWarning("ReindexKnowledgeSourceAsync not yet implemented");

        return Task.FromResult(new ReindexResult
        {
            Success = false,
            ErrorMessage = "Reindexing not yet implemented"
        });
    }

    public async Task<IndexStatistics> GetIndexStatisticsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Count documents and chunks for this customer
            var filter = $"customerId eq '{customerId}'";
            var searchOptions = new SearchOptions
            {
                Filter = filter,
                Size = 0,
                IncludeTotalCount = true
            };

            var response = await _searchClient.SearchAsync<KnowledgeIndexDocument>("*", searchOptions, cancellationToken);

            // Get unique document count
            var docSearchOptions = new SearchOptions
            {
                Filter = filter,
                Size = 0,
                Facets = { "documentId" }
            };

            var docResponse = await _searchClient.SearchAsync<KnowledgeIndexDocument>("*", docSearchOptions, cancellationToken);

            var totalChunks = response.Value.TotalCount ?? 0;
            var totalDocs = docResponse.Value.Facets.TryGetValue("documentId", out var facets)
                ? facets.Count
                : 0;

            return new IndexStatistics
            {
                CustomerId = customerId,
                Model = DeploymentModel.Shared,
                IndexName = _options.KnowledgeIndexName ?? "spaarke-knowledge-shared",
                TotalDocuments = totalDocs,
                TotalChunks = (int)totalChunks,
                LastIndexedAt = null // Would need to track separately
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get index statistics for customer {CustomerId}", customerId);

            return new IndexStatistics
            {
                CustomerId = customerId,
                Model = DeploymentModel.Shared,
                IndexName = _options.KnowledgeIndexName ?? "spaarke-knowledge-shared"
            };
        }
    }

    /// <summary>
    /// Split document text into overlapping chunks.
    /// </summary>
    private static List<string> ChunkDocument(string content, int chunkSize, int overlap)
    {
        var chunks = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
            return chunks;

        // Normalize whitespace
        content = NormalizeWhitespace(content);

        if (content.Length <= chunkSize)
        {
            chunks.Add(content);
            return chunks;
        }

        var position = 0;
        while (position < content.Length)
        {
            var endPosition = Math.Min(position + chunkSize, content.Length);

            // Try to break at sentence boundary if possible
            if (endPosition < content.Length)
            {
                var lastSentenceEnd = FindLastSentenceBoundary(content, position, endPosition);
                if (lastSentenceEnd > position + chunkSize / 2)
                {
                    endPosition = lastSentenceEnd;
                }
            }

            var chunk = content[position..endPosition].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk);
            }

            // Move position with overlap
            position = endPosition - overlap;
            if (position >= content.Length - overlap)
            {
                break;
            }
        }

        return chunks;
    }

    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasWhitespace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasWhitespace)
                {
                    sb.Append(' ');
                    lastWasWhitespace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasWhitespace = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static int FindLastSentenceBoundary(string text, int start, int end)
    {
        var sentenceEnders = new[] { '.', '!', '?', '\n' };

        for (var i = end - 1; i > start; i--)
        {
            if (sentenceEnders.Contains(text[i]))
            {
                return i + 1;
            }
        }

        // Fall back to word boundary
        for (var i = end - 1; i > start; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return end;
    }
}

/// <summary>
/// Index document structure for Azure AI Search.
/// </summary>
internal class KnowledgeIndexDocument
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string KnowledgeSourceId { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string ChunkContent { get; set; } = string.Empty;
    public float[] ContentVector { get; set; } = [];
    public string? DocumentTitle { get; set; }
    public string? DocumentFileName { get; set; }
    public string? KnowledgeType { get; set; }
    public string? Category { get; set; }
    public string[]? Tags { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? SourceMetadata { get; set; }
}
