using System.Diagnostics;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Indexes golden reference knowledge sources into the <c>spaarke-rag-references</c> AI Search index.
/// Follows the same chunk → embed → index pattern as <see cref="RagIndexingPipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline steps per knowledge source:
/// <list type="ordered">
///   <item>Delete existing chunks for the source from spaarke-rag-references (idempotency).</item>
///   <item>Chunk content at 512-token granularity with 100-token overlap.</item>
///   <item>Generate 3072-dim embeddings via <see cref="IOpenAiClient"/>.</item>
///   <item>Build index documents with all schema fields (knowledgeSourceId, tags, domain).</item>
///   <item>Upload to spaarke-rag-references index.</item>
/// </list>
/// </para>
/// <para>
/// Constraints:
/// <list type="bullet">
///   <item>ADR-001: Called via admin endpoints — not wired to Service Bus.</item>
///   <item>ADR-010: Registered as concrete singleton in AiModule.cs.</item>
///   <item>Idempotent: deletes existing chunks before re-indexing.</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class ReferenceIndexingService
{
    private readonly ITextChunkingService _chunkingService;
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly IScopeResolverService _scopeResolverService;
    private readonly AiSearchOptions _aiSearchOptions;
    private readonly ILogger<ReferenceIndexingService> _logger;

    // Maximum number of concurrent embedding API calls (prevents rate-limit errors).
    private const int MaxConcurrentEmbeddings = 16;

    // Reference index: 512 tokens → 2048 chars, 100-token overlap → 400 chars.
    private static readonly ChunkingOptions ReferenceChunkingOptions = new()
    {
        ChunkSize = 2048,
        Overlap = 400,
        PreserveSentenceBoundaries = true
    };

    /// <summary>
    /// Initialises a new <see cref="ReferenceIndexingService"/>.
    /// </summary>
    public ReferenceIndexingService(
        ITextChunkingService chunkingService,
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IScopeResolverService scopeResolverService,
        IOptions<AiSearchOptions> aiSearchOptions,
        ILogger<ReferenceIndexingService> logger)
    {
        _chunkingService = chunkingService ?? throw new ArgumentNullException(nameof(chunkingService));
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _scopeResolverService = scopeResolverService ?? throw new ArgumentNullException(nameof(scopeResolverService));
        _aiSearchOptions = aiSearchOptions?.Value ?? throw new ArgumentNullException(nameof(aiSearchOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Indexes a single knowledge source into the spaarke-rag-references index.
    /// Deletes existing chunks first for idempotency, then chunks, embeds, and uploads.
    /// </summary>
    /// <param name="knowledgeSourceId">The Dataverse knowledge source ID.</param>
    /// <param name="content">The text content to index.</param>
    /// <param name="name">Display name of the knowledge source.</param>
    /// <param name="domain">Domain classification (e.g., "legal", "finance").</param>
    /// <param name="tags">Tags for categorization and filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result with chunk counts and elapsed time.</returns>
    public async Task<ReferenceIndexingResult> IndexKnowledgeSourceAsync(
        string knowledgeSourceId,
        string content,
        string name,
        string domain,
        string[] tags,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(knowledgeSourceId);
        ArgumentException.ThrowIfNullOrEmpty(content);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting reference indexing for knowledge source {KnowledgeSourceId}, name={Name}, domain={Domain}",
            knowledgeSourceId, name, domain);

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.RagReferencesIndexName);

            // Step 1: Delete existing chunks for this source (idempotency).
            var deletedCount = await DeleteChunksForSourceAsync(searchClient, knowledgeSourceId, ct);

            _logger.LogDebug(
                "Deleted {DeletedCount} existing chunks for knowledge source {KnowledgeSourceId}",
                deletedCount, knowledgeSourceId);

            // Step 2: Chunk the content (512 tokens, 100-token overlap).
            var chunks = await _chunkingService.ChunkTextAsync(content, ReferenceChunkingOptions, ct);

            if (chunks.Count == 0)
            {
                _logger.LogWarning(
                    "No chunks produced for knowledge source {KnowledgeSourceId} — content may be empty",
                    knowledgeSourceId);

                return new ReferenceIndexingResult
                {
                    KnowledgeSourceId = knowledgeSourceId,
                    ChunksIndexed = 0,
                    ChunksDeleted = deletedCount,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogDebug(
                "Chunked knowledge source {KnowledgeSourceId}: {ChunkCount} chunks",
                knowledgeSourceId, chunks.Count);

            // Step 3: Generate 3072-dim embeddings.
            var embeddings = await GenerateEmbeddingsInBatchesAsync(
                chunks.Select(c => c.Content).ToList(), ct);

            // Step 4: Build index documents with all schema fields.
            var documents = BuildReferenceDocuments(
                chunks, embeddings, knowledgeSourceId, name, domain, tags);

            // Step 5: Upload to spaarke-rag-references index.
            var uploadedCount = await UploadDocumentsAsync(searchClient, documents, ct);

            stopwatch.Stop();

            _logger.LogInformation(
                "Reference indexing complete for {KnowledgeSourceId}: {ChunksIndexed} chunks indexed in {ElapsedMs}ms",
                knowledgeSourceId, uploadedCount, stopwatch.ElapsedMilliseconds);

            LogReferenceIndexingCompleted(
                knowledgeSourceId: knowledgeSourceId,
                name: name,
                chunksIndexed: uploadedCount,
                elapsedMs: stopwatch.ElapsedMilliseconds);

            return new ReferenceIndexingResult
            {
                KnowledgeSourceId = knowledgeSourceId,
                ChunksIndexed = uploadedCount,
                ChunksDeleted = deletedCount,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            LogReferenceIndexingFailed(
                knowledgeSourceId: knowledgeSourceId,
                errorType: ex.GetType().Name);

            throw;
        }
    }

    /// <summary>
    /// Deletes all chunks for a knowledge source from the spaarke-rag-references index.
    /// </summary>
    /// <param name="knowledgeSourceId">The knowledge source ID whose chunks should be deleted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of chunks deleted.</returns>
    public async Task<int> DeleteKnowledgeSourceAsync(string knowledgeSourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(knowledgeSourceId);

        var searchClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.RagReferencesIndexName);
        var deletedCount = await DeleteChunksForSourceAsync(searchClient, knowledgeSourceId, ct);

        _logger.LogInformation(
            "Deleted {DeletedCount} chunks for knowledge source {KnowledgeSourceId} from references index",
            deletedCount, knowledgeSourceId);

        return deletedCount;
    }

    /// <summary>
    /// Queries Dataverse for all system knowledge sources and indexes each into spaarke-rag-references.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregate result with totals across all sources.</returns>
    public async Task<ReferenceIndexingBatchResult> IndexAllReferencesAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting bulk reference indexing — querying all knowledge sources from Dataverse");

        // Paginate through all knowledge sources from Dataverse.
        var allSources = new List<AnalysisKnowledge>();
        var page = 1;
        const int pageSize = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _scopeResolverService.ListKnowledgeAsync(
                new ScopeListOptions { Page = page, PageSize = pageSize },
                ct);

            allSources.AddRange(result.Items);

            if (result.Items.Length < pageSize || allSources.Count >= result.TotalCount)
                break;

            page++;
        }

        _logger.LogInformation("Found {SourceCount} knowledge sources to index", allSources.Count);

        var totalChunksIndexed = 0;
        var totalChunksDeleted = 0;
        var successCount = 0;
        var failedCount = 0;
        var skippedCount = 0;
        var errors = new List<ReferenceIndexingError>();

        foreach (var source in allSources)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(source.Content))
            {
                _logger.LogDebug(
                    "Skipping knowledge source {SourceId} ({Name}) — no content",
                    source.Id, source.Name);
                skippedCount++;
                continue;
            }

            try
            {
                var indexResult = await IndexKnowledgeSourceAsync(
                    source.Id.ToString(),
                    source.Content,
                    source.Name,
                    source.Type.ToString(),
                    Array.Empty<string>(),
                    ct);

                totalChunksIndexed += indexResult.ChunksIndexed;
                totalChunksDeleted += indexResult.ChunksDeleted;
                successCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to index knowledge source {SourceId} ({Name})",
                    source.Id, source.Name);

                failedCount++;
                errors.Add(new ReferenceIndexingError
                {
                    KnowledgeSourceId = source.Id.ToString(),
                    Name = source.Name,
                    ErrorMessage = ex.Message
                });
            }
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Bulk reference indexing complete: {SuccessCount} succeeded, {FailedCount} failed, " +
            "{SkippedCount} skipped, {TotalChunks} total chunks indexed in {ElapsedMs}ms",
            successCount, failedCount, skippedCount, totalChunksIndexed, stopwatch.ElapsedMilliseconds);

        return new ReferenceIndexingBatchResult
        {
            TotalSources = allSources.Count,
            SuccessCount = successCount,
            FailedCount = failedCount,
            SkippedCount = skippedCount,
            TotalChunksIndexed = totalChunksIndexed,
            TotalChunksDeleted = totalChunksDeleted,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Errors = errors
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates embeddings for a list of texts in parallel batches.
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
    /// Builds <see cref="KnowledgeDocument"/> objects for the reference index from text chunks and embeddings.
    /// </summary>
    private static IReadOnlyList<KnowledgeDocument> BuildReferenceDocuments(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> embeddings,
        string knowledgeSourceId,
        string name,
        string domain,
        string[] tags)
    {
        var now = DateTimeOffset.UtcNow;
        var docs = new List<KnowledgeDocument>(chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            docs.Add(new KnowledgeDocument
            {
                // Format: {knowledgeSourceId}_ref_{chunkIndex}
                Id = $"{knowledgeSourceId}_ref_{chunk.Index}",
                TenantId = "system",
                KnowledgeSourceId = knowledgeSourceId,
                KnowledgeSourceName = name,
                DocumentType = domain,
                Content = chunk.Content,
                ChunkIndex = chunk.Index,
                ChunkCount = chunks.Count,
                ContentVector = i < embeddings.Count ? embeddings[i] : ReadOnlyMemory<float>.Empty,
                Tags = tags.Length > 0 ? tags.ToList() : null,
                FileName = name,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return docs;
    }

    /// <summary>
    /// Deletes all existing chunks for <paramref name="knowledgeSourceId"/> from the reference index.
    /// </summary>
    private static async Task<int> DeleteChunksForSourceAsync(
        SearchClient searchClient,
        string knowledgeSourceId,
        CancellationToken cancellationToken)
    {
        var filter = $"knowledgeSourceId eq '{EscapeOData(knowledgeSourceId)}'";
        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = 1000,
            Select = { "id" }
        };

        var response = await searchClient.SearchAsync<KnowledgeDocument>("*", searchOptions, cancellationToken);
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
    /// Uploads documents to the reference index in batches, returns the number successfully indexed.
    /// </summary>
    private static async Task<int> UploadDocumentsAsync(
        SearchClient searchClient,
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
            var response = await searchClient.MergeOrUploadDocumentsAsync(
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

    [LoggerMessage(
        EventId = 5300,
        Level = LogLevel.Information,
        Message = "ReferenceIndexingCompleted knowledgeSourceId={KnowledgeSourceId} name={Name} chunksIndexed={ChunksIndexed} elapsedMs={ElapsedMs}")]
    private partial void LogReferenceIndexingCompleted(string knowledgeSourceId, string name, int chunksIndexed, long elapsedMs);

    [LoggerMessage(
        EventId = 5301,
        Level = LogLevel.Error,
        Message = "ReferenceIndexingFailed knowledgeSourceId={KnowledgeSourceId} errorType={ErrorType}")]
    private partial void LogReferenceIndexingFailed(string knowledgeSourceId, string errorType);
}

// -------------------------------------------------------------------------
// Result models for ReferenceIndexingService
// -------------------------------------------------------------------------

/// <summary>
/// Result of indexing a single knowledge source into the references index.
/// </summary>
public record ReferenceIndexingResult
{
    /// <summary>The knowledge source ID that was indexed.</summary>
    public string KnowledgeSourceId { get; init; } = string.Empty;

    /// <summary>Number of chunks successfully indexed.</summary>
    public int ChunksIndexed { get; init; }

    /// <summary>Number of stale chunks deleted before re-indexing.</summary>
    public int ChunksDeleted { get; init; }

    /// <summary>Total elapsed time in milliseconds.</summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// Result of bulk-indexing all knowledge sources.
/// </summary>
public record ReferenceIndexingBatchResult
{
    /// <summary>Total number of knowledge sources found in Dataverse.</summary>
    public int TotalSources { get; init; }

    /// <summary>Number of sources successfully indexed.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of sources that failed to index.</summary>
    public int FailedCount { get; init; }

    /// <summary>Number of sources skipped (no content).</summary>
    public int SkippedCount { get; init; }

    /// <summary>Total chunks indexed across all sources.</summary>
    public int TotalChunksIndexed { get; init; }

    /// <summary>Total stale chunks deleted across all sources.</summary>
    public int TotalChunksDeleted { get; init; }

    /// <summary>Total elapsed time in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>Details of any indexing failures.</summary>
    public List<ReferenceIndexingError> Errors { get; init; } = [];
}

/// <summary>
/// Details of a failed knowledge source indexing operation.
/// </summary>
public record ReferenceIndexingError
{
    /// <summary>The knowledge source ID that failed.</summary>
    public string KnowledgeSourceId { get; init; } = string.Empty;

    /// <summary>Name of the knowledge source.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Error message describing the failure.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}
