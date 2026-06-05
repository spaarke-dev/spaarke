using System.Diagnostics;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using AzureIndexingResult = Azure.Search.Documents.Models.IndexingResult;
// Disambiguate between our pipeline result type and the Azure SDK indexing result type.
using PipelineIndexingResult = Sprk.Bff.Api.Models.Ai.IndexingResult;

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

    /// <summary>
    /// R5 (spec.md FR-09 / §4.2 + ADR-014): runs the indexing pipeline for a single file
    /// uploaded to a chat session, writing chunks to ONLY the <c>spaarke-session-files</c>
    /// index (NOT the knowledge or discovery indexes). Every emitted document carries BOTH
    /// <paramref name="tenantId"/> AND <paramref name="sessionId"/> per ADR-014 isolation
    /// invariant — enforced at compile time by the required parameter (cannot be elided)
    /// and at runtime by argument validation (cannot be null/empty).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method REUSES the existing pipeline's chunking (knowledge granularity only —
    /// session-files index uses the 2048-char / 200-overlap profile, NOT the discovery
    /// 4096-char profile), embedding generation, and document-build helpers. It introduces
    /// only the session-files-specific delete + upload helpers (mirroring the existing
    /// discovery-index helpers). Per R5 reuse mandate (CLAUDE.md §3.1), this is an
    /// additive extension to the existing pipeline — NOT a parallel
    /// <c>SessionFilesIndexingPipeline</c>.
    /// </para>
    /// <para>
    /// Idempotency (ADR-004): re-indexing the same
    /// <c>(documentId, tenantId, sessionId)</c> triple deletes any prior session-files
    /// chunks before uploading the new chunks, matching the existing
    /// <see cref="IndexDocumentAsync"/> contract.
    /// </para>
    /// <para>
    /// Constraints:
    /// <list type="bullet">
    ///   <item>ADR-014: every emitted document MUST carry <c>tenantId</c> AND <c>sessionId</c>.</item>
    ///   <item>R5 NFR-02: files &lt; 500 tokens skip chunking — produces single chunk.</item>
    ///   <item>R5 spec.md §3.2 / ADR-018: NO new feature flag — target index is the
    ///     call-site method choice (<c>IndexSessionFileAsync</c> vs <c>IndexDocumentAsync</c>),
    ///     NOT a configuration toggle.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="document">Parsed document produced by <see cref="DocumentParserRouter"/>.</param>
    /// <param name="documentId">Stable identifier for the document (used as partition key + filter target in AI Search).</param>
    /// <param name="tenantId">Tenant identifier stored on every AI Search document (ADR-014).</param>
    /// <param name="sessionId">Chat-session identifier stored on every emitted document (ADR-014 + R5 FR-09 isolation). REQUIRED — null/empty throws.</param>
    /// <param name="fileName">Display name of the source file (stored for search result presentation).</param>
    /// <param name="speFileId">SharePoint Embedded file identifier (required by the KnowledgeDocument schema).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PipelineIndexingResult"/> with the session-files chunk count reported via
    /// <see cref="PipelineIndexingResult.KnowledgeChunksIndexed"/> (session-files uses the
    /// knowledge-granularity chunking profile) and <c>DiscoveryChunksIndexed = 0</c>
    /// (this path never writes to the discovery index).
    /// </returns>
    /// <exception cref="ArgumentNullException">When <paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="documentId"/>, <paramref name="tenantId"/>, <paramref name="sessionId"/>, or <paramref name="speFileId"/> is null or empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<PipelineIndexingResult> IndexSessionFileAsync(
        ParsedDocument document,
        string documentId,
        string tenantId,
        string sessionId,
        string fileName,
        string speFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        // ADR-014 + R5 FR-09 contract: sessionId is required for session-files writes.
        // Enforced at runtime in addition to compile-time positional requirement.
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentException.ThrowIfNullOrEmpty(speFileId);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting session-files indexing for documentId={DocumentId} tenantId={TenantId} sessionId={SessionId} fileName={FileName}",
            documentId, tenantId, sessionId, fileName);

        try
        {
            // Step 1: Chunk at knowledge granularity ONLY (single-granularity index per design.md §4.2).
            // NFR-02: ITextChunkingService produces a single chunk when input text is below
            // the chunk-size threshold, so files < 500 tokens naturally fall through as one chunk.
            var chunks = await _chunkingService.ChunkTextAsync(
                document.Text, KnowledgeChunkingOptions, cancellationToken);

            _logger.LogDebug(
                "Chunked session file documentId={DocumentId} sessionId={SessionId}: {ChunkCount} chunks",
                documentId, sessionId, chunks.Count);

            // Step 2: Generate embeddings for all chunks (reuses existing batching + semaphore).
            var embeddings = await GenerateEmbeddingsInBatchesAsync(
                chunks.Select(c => c.Content).ToList(), cancellationToken);

            // Step 3: Resolve the session-files SearchClient.
            var sessionFilesClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.SessionFilesIndexName);

            // Step 4: Delete stale chunks for (documentId, tenantId, sessionId) — ADR-004 idempotency.
            // NOTE: this path does NOT touch the knowledge index (_ragService.DeleteBySourceDocumentAsync)
            // and does NOT touch the discovery index (DeleteDiscoveryChunksAsync). Isolation verified by
            // RagIndexingPipelineTests.IndexSessionFile_does_not_call_knowledge_or_discovery_indexes.
            var deleted = await DeleteSessionFileChunksAsync(
                sessionFilesClient, documentId, tenantId, sessionId, cancellationToken);

            _logger.LogDebug(
                "Deleted stale session-files chunks for documentId={DocumentId} sessionId={SessionId}: {Deleted} chunks",
                documentId, sessionId, deleted);

            // Step 5: Build KnowledgeDocument[] with sessionId populated (chunk-id suffix "s" for "session").
            // BuildKnowledgeDocuments was extended with an optional trailing sessionId parameter
            // so existing customer-corpus call signatures remain unchanged (back-compat).
            var sessionDocs = BuildKnowledgeDocuments(
                chunks, embeddings, documentId, tenantId, fileName, speFileId,
                chunkIdSuffix: "s",
                sessionId: sessionId);

            // Step 6: Upload to the session-files index (mirrors UploadDiscoveryDocumentsAsync).
            var indexed = await UploadSessionFileDocumentsAsync(
                sessionFilesClient, sessionDocs, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Session-files indexing complete for documentId={DocumentId} sessionId={SessionId}: " +
                "{ChunksIndexed} chunks in {ElapsedMs}ms",
                documentId, sessionId, indexed, stopwatch.ElapsedMilliseconds);

            // Structured log event for the evaluation harness — distinct EventId from the
            // customer-corpus path so logs can be split per write target.
            LogSessionFileIndexingCompleted(
                tenantId: tenantId,
                sessionId: sessionId,
                documentId: documentId,
                chunksIndexed: indexed,
                elapsedMs: stopwatch.ElapsedMilliseconds);

            return new PipelineIndexingResult
            {
                DocumentId = documentId,
                KnowledgeChunksIndexed = indexed,
                DiscoveryChunksIndexed = 0,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            LogSessionFileIndexingFailed(
                tenantId: tenantId,
                sessionId: sessionId,
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
    /// <remarks>
    /// R5 (task 003): added optional trailing <paramref name="sessionId"/> parameter for the
    /// session-files write path (R5 spec §4.2 / FR-09 / ADR-014). Default-null preserves the
    /// existing customer-corpus call signatures exactly — the knowledge / discovery / references
    /// index writes pass no <paramref name="sessionId"/> and the JSON serializer omits the
    /// field per the <c>JsonIgnoreCondition.WhenWritingNull</c> attribute on
    /// <see cref="KnowledgeDocument.SessionId"/>, so existing index payloads are byte-for-byte
    /// unchanged.
    /// </remarks>
    private static IReadOnlyList<KnowledgeDocument> BuildKnowledgeDocuments(
        IReadOnlyList<TextChunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> embeddings,
        string documentId,
        string tenantId,
        string fileName,
        string speFileId,
        string chunkIdSuffix = "k",
        string? sessionId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var docs = new List<KnowledgeDocument>(chunks.Count);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            docs.Add(new KnowledgeDocument
            {
                // Format: {documentId}_{suffix}_{chunkIndex} — unique per index type
                // ("k" knowledge, "d" discovery, "s" session-files per R5 task 003).
                Id = $"{documentId}_{chunkIdSuffix}_{chunk.Index}",
                TenantId = tenantId,
                SessionId = sessionId,  // R5 — null on customer-corpus paths; set on session-files path.
                DocumentId = documentId,
                SpeFileId = speFileId,
                FileName = fileName ?? string.Empty,
                Content = chunk.Content,
                ChunkIndex = chunk.Index,
                ChunkCount = chunks.Count,
                ContentVector = i < embeddings.Count ? embeddings[i] : ReadOnlyMemory<float>.Empty,
                // Initialize collection fields to empty (NOT null). Azure Search rejects
                // null values for Collection(Edm.String) fields with default Nullable=False
                // semantics — observed as a 400 on the spaarke-session-files index upload
                // during R5 SC-18 walkthrough 2026-06-05. Existing customer-corpus indexes
                // (knowledge/discovery) accept empty lists fine; this keeps the same
                // behavior across all three target indexes.
                Tags = Array.Empty<string>(),
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
    /// R5 (spec §4.2 / FR-09 / ADR-014): deletes all existing session-files-index chunks
    /// for the <c>(documentId, tenantId, sessionId)</c> triple by searching for them with
    /// an OData filter then calling <see cref="SearchClient.DeleteDocumentsAsync"/>.
    /// Mirrors <see cref="DeleteDiscoveryChunksAsync"/> but adds the <c>sessionId</c>
    /// predicate to enforce session isolation.
    /// </summary>
    /// <remarks>
    /// The filter shape is <c>documentId eq '...' and tenantId eq '...' and sessionId eq '...'</c>
    /// — all three predicates are required per ADR-014 (tenantId) + R5 FR-09 (sessionId)
    /// + ADR-004 idempotency (documentId). Verified by
    /// <c>IndexSessionFile_idempotency_deletes_before_upload</c>.
    /// </remarks>
    private async Task<int> DeleteSessionFileChunksAsync(
        SearchClient sessionFilesClient,
        string documentId,
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var filter = $"documentId eq '{EscapeOData(documentId)}' and tenantId eq '{EscapeOData(tenantId)}' and sessionId eq '{EscapeOData(sessionId)}'";
        var searchOptions = new SearchOptions
        {
            Filter = filter,
            Size = 1000,
            Select = { "id" }
        };

        var response = await sessionFilesClient.SearchAsync<KnowledgeDocument>("*", searchOptions, cancellationToken);
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

        var deleteResponse = await sessionFilesClient.DeleteDocumentsAsync(
            "id", idsToDelete, cancellationToken: cancellationToken);
        return deleteResponse.Value.Results.Count(r => r.Succeeded);
    }

    /// <summary>
    /// R5 (spec §4.2 / FR-09): uploads session-files-index documents in batches; returns
    /// the number successfully indexed. Mirrors <see cref="UploadDiscoveryDocumentsAsync"/>
    /// but targets the session-files client. Same 1000-doc batch size as the existing helpers.
    /// </summary>
    private static async Task<int> UploadSessionFileDocumentsAsync(
        SearchClient sessionFilesClient,
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
            var response = await sessionFilesClient.MergeOrUploadDocumentsAsync(
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

    /// <summary>
    /// R5 (task 003): logged on successful session-files indexing completion. Distinct
    /// EventId from the customer-corpus path (5200) so log queries can split per write
    /// target. Includes both <c>tenantId</c> and <c>sessionId</c> per ADR-014 + R5 FR-09.
    /// </summary>
    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Information,
        Message = "SessionFileIndexingCompleted tenant={TenantId} sessionId={SessionId} documentId={DocumentId} chunksIndexed={ChunksIndexed} elapsedMs={ElapsedMs}")]
    private partial void LogSessionFileIndexingCompleted(string tenantId, string sessionId, string documentId, int chunksIndexed, long elapsedMs);

    /// <summary>
    /// R5 (task 003): logged when the session-files indexing path fails. Captures error
    /// type only — no stack trace, no document content. Distinct EventId from the
    /// customer-corpus failure path (5201).
    /// </summary>
    [LoggerMessage(
        EventId = 5203,
        Level = LogLevel.Error,
        Message = "SessionFileIndexingFailed tenant={TenantId} sessionId={SessionId} documentId={DocumentId} errorType={ErrorType}")]
    private partial void LogSessionFileIndexingFailed(string tenantId, string sessionId, string documentId, string errorType);
}
