using System.Diagnostics;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

// Disambiguate between our pipeline result type and the Azure SDK indexing result type.
using PipelineIndexingResult = Sprk.Bff.Api.Models.Ai.IndexingResult;
using AzureIndexingResult = Azure.Search.Documents.Models.IndexingResult;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates the RAG indexing pipeline: chunk → embed → index for a parsed document.
/// Writes chunks to both the knowledge index (512-token) and the discovery index (1024-token)
/// in parallel, and deletes stale chunks before re-indexing to ensure idempotency (ADR-004).
/// </summary>
/// <remarks>
/// <para>
/// Pipeline steps:
/// <list type="ordered">
///   <item>Chunk the parsed document text at two granularities using <see cref="ITextChunkingService"/>.</item>
///   <item>Generate embeddings for all chunks in parallel batches (max 16 concurrent).</item>
///   <item>Delete existing chunks for the document from both indexes (ADR-004 idempotency).</item>
///   <item>Upload new chunks to the knowledge index and the discovery index in parallel.</item>
/// </list>
/// </para>
/// <para>
/// Performance target: completes within 60 000 ms per document (NFR-11).
/// Embeddings are batched via <see cref="IOpenAiClient.GenerateEmbeddingsAsync"/> to stay
/// within Azure OpenAI rate limits while maximising throughput.
/// </para>
/// <para>
/// Constraints:
/// <list type="bullet">
///   <item>ADR-001: Pipeline called by RagIndexingJobHandler via Service Bus — not wired here.</item>
///   <item>ADR-004: Same documentId always replaces previous chunks (idempotent).</item>
///   <item>ADR-009: No chunk embeddings cached in Redis — index directly to AI Search.</item>
///   <item>ADR-010: Registered as concrete singleton in AiModule.cs, not Program.cs inline.</item>
///   <item>ADR-014: Every AI Search document carries <c>tenantId</c> for query-time isolation.</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class RagIndexingPipeline
{
    private readonly ITextChunkingService _chunkingService;
    private readonly IRagService _ragService;
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly AiSearchOptions _aiSearchOptions;
    private readonly ILogger<RagIndexingPipeline> _logger;

    // Maximum number of concurrent embedding API calls (prevents rate-limit errors).
    private const int MaxConcurrentEmbeddings = 16;

    // Chunk size settings matching ChunkOptions token targets (tokens ≈ chars / 4).
    // Knowledge index: 512 tokens → 2048 chars, 50-token overlap → 200 chars.
    // Discovery index: 1024 tokens → 4096 chars, 100-token overlap → 400 chars.
    private static readonly ChunkingOptions KnowledgeChunkingOptions = new()
    {
        ChunkSize = 2048,
        Overlap = 200,
        PreserveSentenceBoundaries = true
    };

    private static readonly ChunkingOptions DiscoveryChunkingOptions = new()
    {
        ChunkSize = 4096,
        Overlap = 400,
        PreserveSentenceBoundaries = true
    };

    /// <summary>
    /// Initialises a new <see cref="RagIndexingPipeline"/>.
    /// </summary>
    /// <param name="chunkingService">Text chunking service for splitting document text.</param>
    /// <param name="ragService">RAG service for knowledge-index operations.</param>
    /// <param name="searchIndexClient">Azure AI Search index client for discovery-index operations.</param>
    /// <param name="openAiClient">OpenAI client for embedding generation.</param>
    /// <param name="aiSearchOptions">AI Search configuration (index names).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RagIndexingPipeline(
        ITextChunkingService chunkingService,
        IRagService ragService,
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IOptions<AiSearchOptions> aiSearchOptions,
        ILogger<RagIndexingPipeline> logger)
    {
        _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService));
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _aiSearchOptions = aiSearchOptions?.Value ?? throw new ArgumentNullException(nameof(aiSearchOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the full indexing pipeline for a single document.
    /// </summary>
    /// <param name="document">Parsed document produced by <see cref="DocumentParserRouter"/>.</param>
    /// <param name="documentId">Stable identifier for the document (used as partition key in AI Search).</param>
    /// <param name="tenantId">Tenant identifier stored on every AI Search document (ADR-014).</param>
    /// <param name="fileName">Display name of the source file (stored for search result presentation).</param>
    /// <param name="speFileId">SharePoint Embedded file identifier (required by the KnowledgeDocument schema).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PipelineIndexingResult"/> with chunk counts and elapsed wall-clock time.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<PipelineIndexingResult> IndexDocumentAsync(
        ParsedDocument document,
        string documentId,
        string tenantId,
        string fileName,
        string speFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(speFileId);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting RAG indexing pipeline for document {DocumentId}, tenant {TenantId}, fileName={FileName}",
            documentId, tenantId, fileName);

        try
        {
            // Step 1: Chunk at both granularities.
            var knowledgeChunksTask = _chunkingService.ChunkTextAsync(document.Text, KnowledgeChunkingOptions, cancellationToken);
            var discoveryChunksTask = _chunkingService.ChunkTextAsync(document.Text, DiscoveryChunkingOptions, cancellationToken);

            await Task.WhenAll(knowledgeChunksTask, discoveryChunksTask);

            var knowledgeChunks = knowledgeChunksTask.Result;
            var discoveryChunks = discoveryChunksTask.Result;

            _logger.LogDebug(
                "Chunked document {DocumentId}: {KnowledgeCount} knowledge chunks, {DiscoveryCount} discovery chunks",
                documentId, knowledgeChunks.Count, discoveryChunks.Count);

            // Step 2: Generate embeddings for all chunks.
            // Knowledge and discovery chunks are embedded independently so each index has
            // vectors appropriate to its own chunk boundaries.
            var knowledgeEmbeddings = await GenerateEmbeddingsInBatchesAsync(
                knowledgeChunks.Select(c => c.Content).ToList(), cancellationToken);
            var discoveryEmbeddings = await GenerateEmbeddingsInBatchesAsync(
                discoveryChunks.Select(c => c.Content).ToList(), cancellationToken);

            // Step 3: Delete stale chunks from both indexes (ADR-004 idempotency).
            // Delete in parallel — knowledge index via IRagService, discovery index directly.
            var discoverySearchClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.DiscoveryIndexName);

            var deleteKnowledgeTask = _ragService.DeleteBySourceDocumentAsync(documentId, tenantId, cancellationToken);
            var deleteDiscoveryTask = DeleteDiscoveryChunksAsync(discoverySearchClient, documentId, tenantId, cancellationToken);

            await Task.WhenAll(deleteKnowledgeTask, deleteDiscoveryTask);

            _logger.LogDebug(
                "Deleted stale chunks for document {DocumentId}: {KnowledgeDeleted} knowledge, {DiscoveryDeleted} discovery",
                documentId, deleteKnowledgeTask.Result, deleteDiscoveryTask.Result);

            // Step 4: Build and upload new KnowledgeDocument objects to both indexes.
            var knowledgeDocs = BuildKnowledgeDocuments(
                knowledgeChunks, knowledgeEmbeddings, documentId, tenantId, fileName, speFileId);
            var discoveryDocs = BuildKnowledgeDocuments(
                discoveryChunks, discoveryEmbeddings, documentId, tenantId, fileName, speFileId,
                chunkIdSuffix: "d");

            var uploadKnowledgeTask = _ragService.IndexDocumentsBatchAsync(knowledgeDocs, cancellationToken);
            var uploadDiscoveryTask = UploadDiscoveryDocumentsAsync(
                discoverySearchClient, discoveryDocs, cancellationToken);

            await Task.WhenAll(uploadKnowledgeTask, uploadDiscoveryTask);

            var knowledgeResults = uploadKnowledgeTask.Result;
            var discoveryResults = uploadDiscoveryTask.Result;

            var knowledgeIndexed = knowledgeResults.Count(r => r.Succeeded);
            var discoveryIndexed = discoveryResults;

            stopwatch.Stop();

            _logger.LogInformation(
                "RAG indexing pipeline complete for document {DocumentId}: " +
                "{KnowledgeIndexed} knowledge chunks, {DiscoveryIndexed} discovery chunks indexed in {ElapsedMs}ms",
                documentId, knowledgeIndexed, discoveryIndexed, stopwatch.ElapsedMilliseconds);

            // Log structured IndexingCompleted event for the evaluation harness (AIPL-071).
            LogIndexingCompleted(
                tenantId: tenantId,
                documentId: documentId,
                knowledgeChunks: knowledgeIndexed,
                discoveryChunks: discoveryIndexed,
                elapsedMs: stopwatch.ElapsedMilliseconds);

            return new PipelineIndexingResult
            {
                DocumentId = documentId,
                KnowledgeChunksIndexed = knowledgeIndexed,
                DiscoveryChunksIndexed = discoveryIndexed,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            // Log structured IndexingFailed event. ErrorType only — no stack trace, no document content.
            LogIndexingFailed(
                tenantId: tenantId,
                documentId: documentId,
                errorType: ex.GetType().Name);

            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates embeddings for a list of texts in parallel batches.
    /// Uses a <see cref="SemaphoreSlim"/> to cap concurrent OpenAI API calls at
    /// <see cref="MaxConcurrentEmbeddings"/> to avoid rate-limit errors.
    /// </summary>
    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsInBatchesAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<ReadOnlyMemory<float>>();
        }

        var results = new ReadOnlyMemory<float>[texts.Count];
        var semaphore = new SemaphoreSlim(MaxConcurrentEmbeddings, MaxConcurrentEmbeddings);

        var tasks = texts.Select(async (text, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var embedding = await _openAiClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
                results[index] = embedding;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// Builds <see cref="KnowledgeDocument"/> objects from text chunks and embeddings.
    /// </summary>
    private static IReadOnlyList<KnowledgeDocument> BuildKnowledgeDocuments(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> embeddings,
        string documentId,
        string tenantId,
        string fileName,
        string speFileId,
        string chunkIdSuffix = "k")
    {
        var now = DateTimeOffset.UtcNow;
        var docs = new List<KnowledgeDocument>(chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            docs.Add(new KnowledgeDocument
            {
                // Format: {documentId}_{suffix}_{chunkIndex} — unique per index type
                Id = $"{documentId}_{chunkIdSuffix}_{chunk.Index}",
                TenantId = tenantId,
                DocumentId = documentId,
                SpeFileId = speFileId,
                FileName = fileName ?? string.Empty,
                Content = chunk.Content,
                ChunkIndex = chunk.Index,
                ChunkCount = chunks.Count,
                ContentVector = i < embeddings.Count ? embeddings[i] : ReadOnlyMemory<float>.Empty,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return docs;
    }

    /// <summary>
    /// Deletes all existing discovery-index chunks for <paramref name="documentId"/> by
    /// searching for them with an OData filter then calling
    /// <see cref="SearchClient.DeleteDocumentsAsync"/>.
    /// </summary>
    private async Task<int> DeleteDiscoveryChunksAsync(
        SearchClient discoveryClient,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var filter = $"documentId eq '{EscapeOData(documentId)}' and tenantId eq '{EscapeOData(tenantId)}'";
        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = 1000,
            Select = { "id" }
        };

        var response = await discoveryClient.SearchAsync<KnowledgeDocument>("*", searchOptions, cancellationToken);
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

        var deleteResponse = await discoveryClient.DeleteDocumentsAsync(
            "id", idsToDelete, cancellationToken: cancellationToken);
        return deleteResponse.Value.Results.Count(r => r.Succeeded);
    }

    /// <summary>
    /// Uploads discovery-index documents in batches, returns the number successfully indexed.
    /// </summary>
    private static async Task<int> UploadDiscoveryDocumentsAsync(
        SearchClient discoveryClient,
        IReadOnlyList<KnowledgeDocument> documents,
        CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return 0;
        }

        const int batchSize = 1000;
        var successCount = 0;

        for (int i = 0; i < documents.Count; i += batchSize)
        {
            var batch = documents.Skip(i).Take(batchSize).ToList();
            var response = await discoveryClient.MergeOrUploadDocumentsAsync(
                batch, cancellationToken: cancellationToken);

            successCount += response.Value.Results.Count(r => r.Succeeded);
        }

        return successCount;
    }

    /// <summary>
    /// Escapes a string for use in an OData filter expression (single-quote doubling).
    /// </summary>
    private static string EscapeOData(string value) => value.Replace("'", "''");

    // -------------------------------------------------------------------------
    // High-performance structured log events (source-generated)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Logged on successful pipeline completion. Includes chunk counts and elapsed time
    /// for the evaluation harness (AIPL-071). No document content or vectors are included.
    /// </summary>
    [LoggerMessage(
        EventId = 5200,
        Level = LogLevel.Information,
        Message = "IndexingCompleted tenant={TenantId} documentId={DocumentId} knowledgeChunks={KnowledgeChunks} discoveryChunks={DiscoveryChunks} elapsedMs={ElapsedMs}")]
    private partial void LogIndexingCompleted(string tenantId, string documentId, int knowledgeChunks, int discoveryChunks, long elapsedMs);

    /// <summary>
    /// Logged when the pipeline fails. Captures error type only — no stack trace, no document content.
    /// </summary>
    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Error,
        Message = "IndexingFailed tenant={TenantId} documentId={DocumentId} errorType={ErrorType}")]
    private partial void LogIndexingFailed(string tenantId, string documentId, string errorType);
}
