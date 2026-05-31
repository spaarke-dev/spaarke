using System.Diagnostics;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Indexing;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Indexes content into AI Search using a chunk → embed → delete-stale → upsert pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>History (Task 025 W3.5 refactor, 2026-05-28)</b>: this service was originally hard-coded to
/// the <c>spaarke-rag-references</c> index and the <see cref="KnowledgeDocument"/> schema. Per the Q5
/// duplication audit it has been parameterized so D-P4 (Precedent projection sync into
/// <c>spaarke-insights-index</c>) and D-P11 (Observation mirror) can reuse the same pipeline without
/// re-implementing chunking, embedding, idempotent delete, or batched upsert. Existing callers
/// (<c>AdminKnowledgeEndpoints</c> and the bulk path) continue to use the original convenience methods,
/// which now delegate to the generic <see cref="IndexIntoAsync{TDoc}"/> overload via
/// <see cref="KnowledgeDocumentSchemaMapper"/>. Behavior for the references index is unchanged.
/// </para>
/// <para>
/// Pipeline steps per source:
/// <list type="ordered">
///   <item>Delete existing documents for the source from the target index (idempotency).</item>
///   <item>Chunk content using the supplied <see cref="ChunkingOptions"/> (default: 512-token / 100-token overlap).</item>
///   <item>Generate 3072-dim embeddings via <see cref="IOpenAiClient"/>.</item>
///   <item>Build index documents via the supplied <see cref="ISchemaMapper{TDoc}"/>.</item>
///   <item>Merge-or-upload to the named index in batches of up to 1000.</item>
/// </list>
/// </para>
/// <para>
/// Constraints:
/// <list type="bullet">
///   <item>ADR-001: Called via admin endpoints — not wired to Service Bus.</item>
///   <item>ADR-010: Registered as concrete singleton in AiModule.cs.</item>
///   <item>Idempotent: deletes existing documents for the source before re-indexing.</item>
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

    // Maximum number of documents per AI Search upload batch.
    private const int UploadBatchSize = 1000;

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

    // -------------------------------------------------------------------------
    // Generic indexing API (Task 025 W3.5 refactor — added 2026-05-28)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Indexes <paramref name="content"/> into <paramref name="indexName"/> using <paramref name="schemaMapper"/>
    /// to translate chunks + embeddings into the target document type. Idempotent: deletes existing documents
    /// for <paramref name="sourceId"/> first.
    /// </summary>
    /// <typeparam name="TDoc">The AI Search document type for the target index (e.g., <see cref="KnowledgeDocument"/>, Observation row).</typeparam>
    /// <param name="indexName">Name of the AI Search index to upsert into.</param>
    /// <param name="sourceId">Stable source identifier; used to compose document IDs and for idempotent delete.</param>
    /// <param name="content">Text content to chunk, embed, and index.</param>
    /// <param name="schemaMapper">Mapper translating chunks + embeddings into <typeparamref name="TDoc"/> and supplying the source filter.</param>
    /// <param name="context">Optional display/metadata context (name, domain, tags).</param>
    /// <param name="chunkingOptions">Optional chunking configuration. Defaults to the reference profile (512-token / 100-token overlap).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Counts and duration for the operation.</returns>
    public async Task<ReferenceIndexingResult> IndexIntoAsync<TDoc>(
        string indexName,
        string sourceId,
        string content,
        ISchemaMapper<TDoc> schemaMapper,
        SchemaMappingContext? context = null,
        ChunkingOptions? chunkingOptions = null,
        CancellationToken ct = default)
        where TDoc : class
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentException.ThrowIfNullOrEmpty(content);
        ArgumentNullException.ThrowIfNull(schemaMapper);

        var stopwatch = Stopwatch.StartNew();
        var effectiveContext = context ?? SchemaMappingContext.Empty;
        var effectiveChunking = chunkingOptions ?? ReferenceChunkingOptions;

        _logger.LogInformation(
            "Starting indexing into {IndexName} for sourceId={SourceId}, mapper={Mapper}",
            indexName, sourceId, typeof(TDoc).Name);

        try
        {
            var searchClient = _searchIndexClient.GetSearchClient(indexName);

            // Step 1: Delete existing documents for this source (idempotency).
            var deletedCount = await DeleteForSourceAsync(searchClient, schemaMapper, sourceId, ct);

            _logger.LogDebug(
                "Deleted {DeletedCount} existing documents for sourceId={SourceId} in {IndexName}",
                deletedCount, sourceId, indexName);

            // Step 2: Chunk the content.
            var chunks = await _chunkingService.ChunkTextAsync(content, effectiveChunking, ct);

            if (chunks.Count == 0)
            {
                _logger.LogWarning(
                    "No chunks produced for sourceId={SourceId} in {IndexName} — content may be empty",
                    sourceId, indexName);

                return new ReferenceIndexingResult
                {
                    KnowledgeSourceId = sourceId,
                    ChunksIndexed = 0,
                    ChunksDeleted = deletedCount,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogDebug(
                "Chunked sourceId={SourceId}: {ChunkCount} chunks",
                sourceId, chunks.Count);

            // Step 3: Generate embeddings.
            var embeddings = await GenerateEmbeddingsInBatchesAsync(
                chunks.Select(c => c.Content).ToList(), ct);

            // Step 4: Build typed index documents via the mapper.
            var documents = schemaMapper.BuildDocuments(chunks, embeddings, sourceId, effectiveContext);

            // Step 5: Upload to the target index.
            var uploadedCount = await UploadDocumentsAsync(searchClient, documents, ct);

            stopwatch.Stop();

            _logger.LogInformation(
                "Indexing complete for sourceId={SourceId} in {IndexName}: {ChunksIndexed} documents indexed in {ElapsedMs}ms",
                sourceId, indexName, uploadedCount, stopwatch.ElapsedMilliseconds);

            LogReferenceIndexingCompleted(
                knowledgeSourceId: sourceId,
                name: effectiveContext.Name ?? sourceId,
                chunksIndexed: uploadedCount,
                elapsedMs: stopwatch.ElapsedMilliseconds);

            return new ReferenceIndexingResult
            {
                KnowledgeSourceId = sourceId,
                ChunksIndexed = uploadedCount,
                ChunksDeleted = deletedCount,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            LogReferenceIndexingFailed(
                knowledgeSourceId: sourceId,
                errorType: ex.GetType().Name);

            throw;
        }
    }

    /// <summary>
    /// Deletes all documents for <paramref name="sourceId"/> from <paramref name="indexName"/>
    /// using the source filter supplied by <paramref name="schemaMapper"/>.
    /// </summary>
    public async Task<int> DeleteFromAsync<TDoc>(
        string indexName,
        string sourceId,
        ISchemaMapper<TDoc> schemaMapper,
        CancellationToken ct = default)
        where TDoc : class
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentNullException.ThrowIfNull(schemaMapper);

        var searchClient = _searchIndexClient.GetSearchClient(indexName);
        var deletedCount = await DeleteForSourceAsync(searchClient, schemaMapper, sourceId, ct);

        _logger.LogInformation(
            "Deleted {DeletedCount} documents for sourceId={SourceId} from index {IndexName}",
            deletedCount, sourceId, indexName);

        return deletedCount;
    }

    // -------------------------------------------------------------------------
    // Convenience wrappers — preserve original spaarke-rag-references behavior
    // -------------------------------------------------------------------------

    /// <summary>
    /// Indexes a single knowledge source into the <c>spaarke-rag-references</c> index.
    /// Thin wrapper around <see cref="IndexIntoAsync{TDoc}"/> using <see cref="KnowledgeDocumentSchemaMapper"/>;
    /// behavior is identical to the pre-refactor implementation.
    /// </summary>
    /// <param name="knowledgeSourceId">The Dataverse knowledge source ID.</param>
    /// <param name="content">The text content to index.</param>
    /// <param name="name">Display name of the knowledge source.</param>
    /// <param name="domain">Domain classification (e.g., "legal", "finance").</param>
    /// <param name="tags">Tags for categorization and filtering.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result with chunk counts and elapsed time.</returns>
    public Task<ReferenceIndexingResult> IndexKnowledgeSourceAsync(
        string knowledgeSourceId,
        string content,
        string name,
        string domain,
        string[] tags,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(knowledgeSourceId);
        ArgumentException.ThrowIfNullOrEmpty(content);

        var context = new SchemaMappingContext
        {
            Name = name,
            Domain = domain,
            Tags = tags
        };

        return IndexIntoAsync(
            indexName: _aiSearchOptions.RagReferencesIndexName,
            sourceId: knowledgeSourceId,
            content: content,
            schemaMapper: KnowledgeDocumentSchemaMapper.Instance,
            context: context,
            chunkingOptions: ReferenceChunkingOptions,
            ct: ct);
    }

    /// <summary>
    /// Deletes all chunks for a knowledge source from the <c>spaarke-rag-references</c> index.
    /// Thin wrapper around <see cref="DeleteFromAsync{TDoc}"/>.
    /// </summary>
    /// <param name="knowledgeSourceId">The knowledge source ID whose chunks should be deleted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of chunks deleted.</returns>
    public Task<int> DeleteKnowledgeSourceAsync(string knowledgeSourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(knowledgeSourceId);

        return DeleteFromAsync(
            indexName: _aiSearchOptions.RagReferencesIndexName,
            sourceId: knowledgeSourceId,
            schemaMapper: KnowledgeDocumentSchemaMapper.Instance,
            ct: ct);
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
    /// Deletes all documents matching the schema mapper's source filter from <paramref name="searchClient"/>.
    /// </summary>
    private static async Task<int> DeleteForSourceAsync<TDoc>(
        SearchClient searchClient,
        ISchemaMapper<TDoc> schemaMapper,
        string sourceId,
        CancellationToken cancellationToken)
        where TDoc : class
    {
        var filter = schemaMapper.BuildSourceFilter(EscapeOData(sourceId));
        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = 1000,
            Select = { "id" }
        };

        // Use the SDK's SearchDocument projection for ID-only retrieval; this keeps the delete path
        // schema-agnostic (works for any index whose key field is named "id").
        var response = await searchClient.SearchAsync<SearchDocument>("*", searchOptions, cancellationToken);
        var idsToDelete = new List<string>();

        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document.TryGetValue("id", out var rawId) && rawId is string id && !string.IsNullOrEmpty(id))
            {
                idsToDelete.Add(id);
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
    /// Uploads documents to the target index in batches; returns the number successfully indexed.
    /// </summary>
    private static async Task<int> UploadDocumentsAsync<TDoc>(
        SearchClient searchClient,
        IReadOnlyList<TDoc> documents,
        CancellationToken cancellationToken)
        where TDoc : class
    {
        if (documents.Count == 0)
        {
            return 0;
        }

        var successCount = 0;

        for (int i = 0; i < documents.Count; i += UploadBatchSize)
        {
            var batch = documents.Skip(i).Take(UploadBatchSize).ToList();
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
    /// <summary>The source ID that was indexed (knowledge source, precedent, observation, etc.).</summary>
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
