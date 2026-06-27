using System.Diagnostics;
using System.Security.Claims;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Security;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Implements hybrid RAG search combining keyword search, vector search, and semantic ranking.
/// Integrates with IKnowledgeDeploymentService for multi-tenant index routing.
/// </summary>
/// <remarks>
/// Hybrid search pipeline:
/// 1. Generate embedding for query (with optional caching)
/// 2. Build hybrid query combining:
///    - Full-text keyword search on content field
///    - Vector search on contentVector field (cosine similarity)
/// 3. Apply semantic ranking for result re-ordering
/// 4. Filter by tenant and other criteria
/// 5. Return ranked results with telemetry
///
/// Performance targets:
/// - P95 latency: <500ms
/// - Embedding generation: ~50ms (cached), ~150ms (uncached)
/// - Search execution: ~100-300ms depending on index size
/// </remarks>
public partial class RagService : IRagService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IPrivilegeGroupResolver _privilegeGroupResolver;
    private readonly IResilientSearchClient? _resilientClient;
    private readonly AnalysisOptions _analysisOptions;
    private readonly ILogger<RagService> _logger;
    private readonly AiTelemetry? _telemetry;
    /// <summary>
    /// R6 Pillar 6c (FR-37 / task 063) — optional context.* event emitter for
    /// <c>context.knowledge_retrieved</c> emission after each RAG search completes.
    /// ADR-015 audit: per-result emission carries deterministic source IDs + relevance score
    /// + numeric resultCount ONLY. Never document content, never chunk text. Optional so
    /// existing test fixtures and AI-OFF paths continue to construct cleanly.
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter? _contextEventEmitter;
    // B8 (task 011 Phase 1b Tier 3, D-09 §2 B8): direct Azure SDK access for knowledge-base
    // index administration. Used only by GetIndexHealthAsync / GetIndexedDocumentsAsync /
    // DeleteIndexedDocumentAsync which absorb the calls previously made by KnowledgeBaseEndpoints.
    private readonly SearchIndexClient _searchIndexClient;
    private readonly AiSearchOptions _aiSearchOptions;

    // Semantic configuration name from the index definition
    private const string SemanticConfigurationName = "knowledge-semantic-config";

    // Maximum query text length for structured logging (PII/compliance — do not log full query)
    private const int MaxQueryTextLength = 200;

    // Vector field name and dimensions (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector3072";
    private const int VectorDimensions = 3072;

    // Search field for keyword queries
    private static readonly string[] SearchFields = ["content", "fileName", "knowledgeSourceName"];

    // Supported file extensions for type extraction
    private static readonly HashSet<string> SupportedFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "docx", "doc", "xlsx", "xls", "pptx", "ppt", "msg", "eml", "txt", "html", "htm", "rtf", "csv"
    };

    public RagService(
        IKnowledgeDeploymentService deploymentService,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        IPrivilegeGroupResolver privilegeGroupResolver,
        IOptions<AnalysisOptions> analysisOptions,
        SearchIndexClient searchIndexClient,
        IOptions<AiSearchOptions> aiSearchOptions,
        ILogger<RagService> logger,
        IResilientSearchClient? resilientClient = null,
        AiTelemetry? telemetry = null,
        Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter? contextEventEmitter = null)
    {
        _deploymentService = deploymentService;
        _openAiClient = openAiClient;
        _embeddingCache = embeddingCache;
        _privilegeGroupResolver = privilegeGroupResolver ?? throw new ArgumentNullException(nameof(privilegeGroupResolver));
        _resilientClient = resilientClient;
        _analysisOptions = analysisOptions.Value;
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _aiSearchOptions = (aiSearchOptions ?? throw new ArgumentNullException(nameof(aiSearchOptions))).Value;
        _logger = logger;
        _telemetry = telemetry;
        _contextEventEmitter = contextEventEmitter;
    }

    /// <inheritdoc />
    public async Task<RagSearchResponse> SearchAsync(
        string query,
        RagSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        return await SearchAsync(query, options, user: null, cancellationToken);
    }

    /// <summary>
    /// Search with an explicit <see cref="ClaimsPrincipal"/> for privilege-aware filtering.
    /// When <paramref name="user"/> is null, the method attempts to resolve the principal
    /// from <see cref="IHttpContextAccessor"/> (available in request-scoped scenarios).
    /// </summary>
    internal async Task<RagSearchResponse> SearchAsync(
        string query,
        RagSearchOptions options,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.TenantId);

        var totalStopwatch = Stopwatch.StartNew();
        var embeddingStopwatch = new Stopwatch();
        var searchStopwatch = new Stopwatch();
        var embeddingCacheHit = false;

        _logger.LogDebug(
            "Starting RAG search for tenant {TenantId}, query length={QueryLength}, TopK={TopK}",
            options.TenantId, query.Length, options.TopK);

        try
        {
            // Step 0: Resolve privilege groups before issuing any search (AIPU2-027 — fail-closed).
            // If group resolution fails, PrivilegeGroupResolver returns an empty list and logs the error.
            // RagService then returns empty results rather than falling back to unfiltered search.
            var principal = user ?? options.CallerPrincipal;
            IReadOnlyList<string> userGroupIds;

            if (principal != null)
            {
                userGroupIds = await _privilegeGroupResolver.ResolveGroupIdsAsync(principal, cancellationToken);

                // Fail-closed: if the principal has no OID (malformed token) the resolver returns empty.
                // We still proceed — BuildSearchOptions will add "not privilege_group_ids/any()"
                // which returns only public documents, which is the correct safe default.
                _telemetry?.RecordPrivilegeFilterApplied(groupCount: userGroupIds.Count);

                if (userGroupIds.Count == 0)
                {
                    _logger.LogInformation(
                        "RAG search: user has zero resolved groups for tenant {TenantId} — only public documents will be returned",
                        options.TenantId);
                    _telemetry?.RecordPrivilegeFilterEmptyResult();
                }
            }
            else
            {
                // No principal available — system/background call. Apply public-only filter.
                userGroupIds = Array.Empty<string>();
                _logger.LogDebug(
                    "RAG search: no caller principal available for tenant {TenantId} — applying public-only privilege filter",
                    options.TenantId);
                _telemetry?.RecordPrivilegeFilterApplied(groupCount: 0);
            }

            // Step 1: Get embedding for query (with caching)
            ReadOnlyMemory<float> queryEmbedding = default;
            if (options.UseVectorSearch)
            {
                embeddingStopwatch.Start();

                // Try cache first
                var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(query, cancellationToken);
                if (cachedEmbedding.HasValue)
                {
                    queryEmbedding = cachedEmbedding.Value;
                    embeddingCacheHit = true;
                }
                else
                {
                    // Cache miss - generate and cache
                    queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
                    await _embeddingCache.SetEmbeddingForContentAsync(query, queryEmbedding, cancellationToken);
                }

                embeddingStopwatch.Stop();

                _logger.LogDebug("Query embedding in {ElapsedMs}ms, cacheHit={CacheHit}",
                    embeddingStopwatch.ElapsedMilliseconds, embeddingCacheHit);
            }

            // Step 2: Get SearchClient — session-scoped routing wins when SessionId is set
            // (R5 task 002 / FR-09); else preserve pre-R5 deployment-routing behavior.
            // Branch order is most-specific-first: SessionId > DeploymentId > default tenant.
            // The session-files index is a SHARED session-scoped index (not per-tenant), so
            // we resolve the SearchClient directly via the injected SearchIndexClient rather
            // than via IKnowledgeDeploymentService (which routes per-tenant for the knowledge
            // index family). Tenant isolation is preserved via the unconditional
            // `tenantId eq '...'` filter in BuildSearchOptions (ADR-014 invariant).
            //
            // multi-container-multi-index-r1 FR-BFF-07 (task 014) — when SessionId is NOT set
            // AND no explicit DeploymentId is provided, but SearchIndexName IS provided,
            // route via the 3-arg resolver overload to apply allow-list validation
            // (FR-BFF-02) and bind the SearchClient to the explicit index (FR-BFF-03).
            // When SearchIndexName is null / whitespace, preserve the original 2-arg call
            // site verbatim so the existing test suite passes UNMODIFIED (FR-BFF-04 /
            // NFR-02 backward-compat). Validation logic is intentionally NOT replicated
            // here — it lives in one place inside KnowledgeDeploymentService per the
            // resolver's contract.
            searchStopwatch.Start();
            SearchClient searchClient;
            if (!string.IsNullOrEmpty(options.SessionId))
            {
                // ────────────────────────────────────────────────────────────────────
                // chat-routing-redesign-r1 task 100 — Architecture §5.2.1 binding-NEGATIVE
                // guard (FR-36). Session-scoped chat-memory retrieval MUST NOT target the
                // spaarke-insights-index. The Insights index holds derived knowledge
                // artifacts (Observations / Precedents) owned by the Insights subsystem.
                // Forcing chat memory through it would:
                //   (a) pollute Insights with chat-specific records (artifact-type bloat),
                //   (b) confuse retrieval ranking (mixing diagnostic findings w/ memory),
                //   (c) couple two unrelated domains,
                //   (d) force the Insights team to support chat-memory queries.
                // Per architecture §4.5 + §7.3, allowed chat-memory indexes are exclusively
                // spaarke-session-files (T2 session recall), spaarke-files-index
                // (T3 matter-scoped), and spaarke-rag-references (T4 org-scoped knowledge).
                // This guard is defense-in-depth against misconfiguration of
                // AiSearchOptions.SessionFilesIndexName (which is operator-settable).
                // ────────────────────────────────────────────────────────────────────
                EnsureNotInsightsIndex(_aiSearchOptions.SessionFilesIndexName);

                searchClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.SessionFilesIndexName);
                _logger.LogDebug(
                    "RAG search routing to session-files index {IndexName} for tenant {TenantId} session {SessionId}",
                    _aiSearchOptions.SessionFilesIndexName, options.TenantId, options.SessionId);
            }
            else if (options.DeploymentId.HasValue)
            {
                searchClient = await _deploymentService.GetSearchClientByDeploymentAsync(options.DeploymentId.Value, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(options.SearchIndexName))
            {
                // Explicit caller-supplied index name — route via the 3-arg overload, which
                // applies allow-list validation in KnowledgeDeploymentService (FR-BFF-02).
                searchClient = await _deploymentService.GetSearchClientAsync(options.TenantId, options.SearchIndexName, cancellationToken);
            }
            else
            {
                // No explicit index — preserve the original 2-arg call site verbatim so the
                // existing test suite passes UNMODIFIED (NFR-02). The 2-arg overload is the
                // contract-preserved entry point per IKnowledgeDeploymentService XML doc.
                searchClient = await _deploymentService.GetSearchClientAsync(options.TenantId, cancellationToken);
            }

            // Step 3: Build hybrid search options with privilege filter applied
            var searchOptions = BuildSearchOptions(options, queryEmbedding, userGroupIds);

            // Step 4: Execute search (with optional resilience)
            var searchText = options.UseKeywordSearch ? query : "*";

            // Log structured RetrievalQuery event before executing search.
            // QueryText is truncated to MaxQueryTextLength — never log full content (PII/compliance).
            LogRetrievalQuery(
                tenantId: options.TenantId,
                indexName: searchClient.IndexName,
                queryText: query.Length > MaxQueryTextLength ? query[..MaxQueryTextLength] : query,
                topK: options.TopK);
            SearchResults<KnowledgeDocument> searchResults;

            if (_resilientClient != null)
            {
                searchResults = await _resilientClient.SearchAsync<KnowledgeDocument>(
                    searchClient, searchText, searchOptions, cancellationToken);
            }
            else
            {
                var response = await searchClient.SearchAsync<KnowledgeDocument>(
                    searchText, searchOptions, cancellationToken);
                searchResults = response.Value;
            }
            searchStopwatch.Stop();

            // Step 5: Process results
            var results = new List<RagSearchResult>();
            long totalCount = 0;

            await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
            {
                if (result.Document == null) continue;

                // Apply minimum score filter
                var score = result.Score ?? 0;
                var semanticScore = result.SemanticSearch?.RerankerScore;

                // Use semantic score if available, otherwise use search score
                var effectiveScore = semanticScore ?? score;
                if (effectiveScore < options.MinScore) continue;

                results.Add(new RagSearchResult
                {
                    Id = result.Document.Id,
                    DocumentId = result.Document.DocumentId,
                    DocumentName = result.Document.FileName,
                    Content = result.Document.Content,
                    KnowledgeSourceName = result.Document.KnowledgeSourceName,
                    Score = effectiveScore,
                    SemanticScore = semanticScore,
                    ChunkIndex = result.Document.ChunkIndex ?? 0,
                    ChunkCount = result.Document.ChunkCount,
                    Highlights = GetHighlights(result),
                    Metadata = result.Document.Metadata,
                    Tags = result.Document.Tags
                });
            }

            // Get total count from response
            totalCount = searchResults.TotalCount ?? results.Count;

            totalStopwatch.Stop();

            // Log structured RetrievalResults event — IDs and scores for Recall@K / nDCG@K evaluation.
            // Document content and embeddings are intentionally excluded from this event.
            LogRetrievalResults(
                tenantId: options.TenantId,
                resultCount: results.Count,
                documentIds: string.Join(",", results.Select(r => r.DocumentId ?? r.Id)),
                scores: string.Join(",", results.Select(r => r.Score.ToString("F4"))),
                elapsedMs: totalStopwatch.ElapsedMilliseconds);

            // R6 Pillar 6c (FR-37 / task 063) — context.knowledge_retrieved emission.
            // ADR-015 audit per emission site (lines below): payload carries
            //   - DocumentId / Id (deterministic identifier of the chunk; Tier 1 safe)
            //   - Score (numeric metric)
            //   - resultCount (numeric metric)
            //   - tenantId (deterministic identifier)
            // It DOES NOT carry: result.Content (chunk text), result.Highlights (excerpt text),
            // result.Metadata (free-form), result.DocumentName (filename — could leak via
            // ADR-015 amendment Tier 2; intentionally excluded from this event surface to keep
            // it Tier 1 only). The IContextEventEmitter.KnowledgeRetrieved signature is
            // structurally constrained — no string/object parameters accept user content.
            if (_contextEventEmitter is not null)
            {
                foreach (var r in results)
                {
                    _contextEventEmitter.KnowledgeRetrieved(
                        knowledgeSourceId: r.DocumentId ?? r.Id ?? string.Empty,
                        relevanceScore: r.Score,
                        resultCount: results.Count,
                        sessionId: null, // R6 task 063: chat session id not threaded into RagService.SearchAsync today; downstream Pillar 6c trace widget correlates by tenantId + timestamp ordering.
                        tenantId: options.TenantId);
                }
            }

            _logger.LogInformation(
                "RAG search completed for tenant {TenantId}: {ResultCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms)",
                options.TenantId, results.Count, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds);

            // Record telemetry
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: results.Count,
                success: true,
                embeddingCacheHit: embeddingCacheHit);

            return new RagSearchResponse
            {
                Query = query,
                Results = results,
                TotalCount = totalCount,
                SearchDurationMs = searchStopwatch.ElapsedMilliseconds,
                EmbeddingDurationMs = embeddingStopwatch.ElapsedMilliseconds,
                EmbeddingCacheHit = embeddingCacheHit
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: 0,
                success: false,
                errorCode: "search_failed");
            throw;
        }
        catch (OpenAiCircuitBrokenException)
        {
            _logger.LogWarning("OpenAI circuit breaker open during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: 0,
                resultCount: 0,
                success: false,
                errorCode: "openai_circuit_breaker_open");
            throw;
        }
        catch (AiSearchCircuitBrokenException)
        {
            _logger.LogWarning("Azure AI Search circuit breaker open during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: 0,
                success: false,
                errorCode: "search_circuit_breaker_open");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: 0,
                success: false,
                errorCode: "unexpected_error");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RagSearchResponse> SearchAsync(
        RagQuery ragQuery,
        Guid? deploymentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ragQuery);
        ArgumentException.ThrowIfNullOrEmpty(ragQuery.SearchText);
        ArgumentException.ThrowIfNullOrEmpty(ragQuery.FilterExpression);

        // Extract tenantId from the filter expression for logging purposes.
        // The filter already contains the full OData expression including tenantId.
        _logger.LogDebug(
            "Starting RagQuery search, searchText length={SearchTextLength}, filter={Filter}, Top={Top}",
            ragQuery.SearchText.Length, ragQuery.FilterExpression, ragQuery.Top);

        // Translate RagQuery into RagSearchOptions.
        // The FilterExpression from RagQuery is applied via the DocumentType field
        // to pass through to BuildSearchOptions. However, since the existing RagSearchOptions
        // does not support arbitrary OData expressions, we use a delegating approach:
        // extract the tenant and document type from the filter and map to RagSearchOptions fields.
        var (tenantId, documentType) = ParseFilterExpression(ragQuery.FilterExpression);

        var options = new RagSearchOptions
        {
            TenantId = tenantId,
            DeploymentId = deploymentId,
            TopK = ragQuery.Top,
            MinScore = (float)ragQuery.MinRelevanceScore,
            DocumentType = documentType,
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true
        };

        return await SearchAsync(ragQuery.SearchText, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument> IndexDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(document.TenantId);

        // Validate speFileId is populated (required for all documents including orphans)
        ArgumentException.ThrowIfNullOrEmpty(document.SpeFileId, nameof(document.SpeFileId));

        _logger.LogDebug("Indexing document {DocumentId} speFileId {SpeFileId} chunk {ChunkIndex} for tenant {TenantId}",
            document.DocumentId ?? "(orphan)", document.SpeFileId, document.ChunkIndex, document.TenantId);

        // Ensure file metadata is populated
        PopulateFileMetadata(document);

        // Generate embedding if not provided
        if (document.ContentVector.Length == 0 && !string.IsNullOrEmpty(document.Content))
        {
            var embedding = await _openAiClient.GenerateEmbeddingAsync(document.Content, cancellationToken: cancellationToken);
            document.ContentVector = embedding;
        }

        // For single-chunk documents, documentVector equals contentVector.
        // For multi-chunk documents indexed individually, use IndexDocumentsBatchAsync to compute documentVector
        // by averaging all chunk contentVectors, or run DocumentVectorBackfillService afterward.
        if (document.DocumentVector.Length == 0 && document.ContentVector.Length > 0 && document.ChunkCount == 1)
        {
            document.DocumentVector = document.ContentVector;
            _logger.LogDebug("Set documentVector = contentVector for single-chunk document speFileId {SpeFileId}", document.SpeFileId);
        }

        // Ensure timestamps are set
        var now = DateTimeOffset.UtcNow;
        if (document.CreatedAt == default)
        {
            document.CreatedAt = now;
        }
        document.UpdatedAt = now;

        // Get SearchClient and index document (with optional resilience)
        var searchClient = await _deploymentService.GetSearchClientAsync(document.TenantId, cancellationToken);
        IndexDocumentsResult indexResult;

        if (_resilientClient != null)
        {
            indexResult = await _resilientClient.MergeOrUploadDocumentsAsync(
                searchClient, new[] { document }, cancellationToken);
        }
        else
        {
            var response = await searchClient.MergeOrUploadDocumentsAsync(
                new[] { document }, cancellationToken: cancellationToken);
            indexResult = response.Value;
        }

        var result = indexResult.Results.FirstOrDefault();
        if (result?.Succeeded != true)
        {
            throw new InvalidOperationException($"Failed to index document: {result?.ErrorMessage}");
        }

        _logger.LogInformation("Indexed document {DocumentId} chunk {ChunkIndex} for tenant {TenantId}",
            document.DocumentId, document.ChunkIndex, document.TenantId);

        return document;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default)
        => IndexDocumentsBatchAsync(documents, searchIndexName: null, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        string? searchIndexName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var documentList = documents.ToList();
        if (documentList.Count == 0) return [];

        var tenantId = documentList[0].TenantId;
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        // Validate all documents have speFileId (required)
        foreach (var doc in documentList)
        {
            ArgumentException.ThrowIfNullOrEmpty(doc.SpeFileId, nameof(doc.SpeFileId));
        }

        _logger.LogDebug("Batch indexing {Count} documents for tenant {TenantId}", documentList.Count, tenantId);

        // Populate file metadata for all documents
        foreach (var doc in documentList)
        {
            PopulateFileMetadata(doc);
        }

        // Generate embeddings for documents without vectors
        var documentsNeedingEmbeddings = documentList
            .Where(d => d.ContentVector.Length == 0 && !string.IsNullOrEmpty(d.Content))
            .ToList();

        if (documentsNeedingEmbeddings.Count > 0)
        {
            var texts = documentsNeedingEmbeddings.Select(d => d.Content).ToList();
            var embeddings = await _openAiClient.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

            for (int i = 0; i < documentsNeedingEmbeddings.Count; i++)
            {
                documentsNeedingEmbeddings[i].ContentVector = embeddings[i];
            }
        }

        // Compute documentVector for each unique file by averaging chunk contentVectors.
        // For documents with DocumentId, group by DocumentId; for orphan files, group by SpeFileId.
        // This enables document similarity visualization for the AI Azure Search Module.
        var documentGroups = documentList
            .GroupBy(d => d.DocumentId ?? d.SpeFileId)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        foreach (var group in documentGroups)
        {
            var chunkVectors = group
                .Select(d => d.ContentVector)
                .Where(v => v.Length > 0)
                .ToList();

            if (chunkVectors.Count > 0)
            {
                var documentVector = ComputeDocumentVector(chunkVectors);
                if (documentVector.Length > 0)
                {
                    foreach (var doc in group)
                    {
                        doc.DocumentVector = documentVector;
                    }

                    var firstDoc = group.First();
                    _logger.LogDebug(
                        "Computed documentVector for {Identifier} from {ChunkCount} chunks (isOrphan={IsOrphan})",
                        group.Key, chunkVectors.Count, firstDoc.DocumentId == null);
                }
            }
        }

        // Set timestamps
        var now = DateTimeOffset.UtcNow;
        foreach (var doc in documentList)
        {
            if (doc.CreatedAt == default)
            {
                doc.CreatedAt = now;
            }
            doc.UpdatedAt = now;
        }

        // Index in batches (Azure AI Search limit is 1000 per batch)
        var deploymentConfig = await _deploymentService.GetDeploymentConfigAsync(tenantId, cancellationToken);

        // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — when a caller supplies an
        // explicit searchIndexName, route via the 3-arg GetSearchClientAsync(tenantId, indexName, ct)
        // overload (Phase B / FR-BFF-01..04). That overload validates against
        // AiSearchOptions.AllowedIndexes and throws SdapProblemException(INDEX_NOT_ALLOWED, 400)
        // on miss (FR-BFF-02 / NFR-08). When null/whitespace, fall through to the 2-arg overload
        // which preserves byte-for-byte NFR-02 backward-compat (existing 2-tier chain).
        var searchClient = string.IsNullOrWhiteSpace(searchIndexName)
            ? await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken)
            : await _deploymentService.GetSearchClientAsync(tenantId, searchIndexName, cancellationToken);

        // Observability: surface the write destination for every batch — without this,
        // a misconfigured deployment (wrong index name, wrong model) is invisible to operators.
        _logger.LogInformation(
            "Resolved deployment for tenant {TenantId}: Model={Model}, IndexName={IndexName}, Endpoint={Endpoint}, BatchSize={BatchSize}",
            tenantId, deploymentConfig.Model, deploymentConfig.IndexName, searchClient.Endpoint, documentList.Count);

        // multi-container-multi-index-r1 indexer-routing-fix (Tier 3, verbose write logging) —
        // emit a structured-log line at INFO per write batch surfacing the resolved searchIndexName
        // (the per-record sprk_searchindexname value, or "(tenant-default)" when null) alongside
        // the Azure Search endpoint. Lets operators audit which physical index each batch landed in
        // — critical for the UAT scenario where "files in SPE but not in the expected index" was
        // root-caused by per-record routing not being threaded end-to-end (bff-extensions.md §F.1).
        _logger.LogInformation(
            "Indexing batch: TenantId={TenantId} SearchIndexName={SearchIndexName} ResolvedEndpoint={Endpoint} BatchSize={BatchSize}",
            tenantId, searchIndexName ?? "(tenant-default)", searchClient.Endpoint, documentList.Count);

        var results = new List<IndexResult>();

        const int batchSize = 1000;
        for (int i = 0; i < documentList.Count; i += batchSize)
        {
            var batch = documentList.Skip(i).Take(batchSize).ToList();
            IndexDocumentsResult indexResult;

            if (_resilientClient != null)
            {
                indexResult = await _resilientClient.MergeOrUploadDocumentsAsync(
                    searchClient, batch, cancellationToken);
            }
            else
            {
                var response = await searchClient.MergeOrUploadDocumentsAsync(
                    batch, cancellationToken: cancellationToken);
                indexResult = response.Value;
            }

            foreach (var result in indexResult.Results)
            {
                if (result.Succeeded)
                {
                    results.Add(IndexResult.Success(result.Key));
                }
                else
                {
                    // Observability: surface per-document Azure Search rejection reasons.
                    // Aggregate batch logging hides which chunks failed and why.
                    _logger.LogWarning(
                        "Azure Search rejected chunk {ChunkKey} in {IndexName}: status={Status} error={ErrorMessage}",
                        result.Key, deploymentConfig.IndexName, result.Status, result.ErrorMessage ?? "(no message)");
                    results.Add(IndexResult.Failure(result.Key, result.ErrorMessage ?? "Unknown error"));
                }
            }
        }

        var successCount = results.Count(r => r.Succeeded);
        _logger.LogInformation(
            "Batch indexed {SuccessCount}/{TotalCount} documents for tenant {TenantId} to {IndexName}",
            successCount, documentList.Count, tenantId, deploymentConfig.IndexName);

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentAsync(
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        _logger.LogDebug("Deleting document {DocumentId} for tenant {TenantId}", documentId, tenantId);

        var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);
        IndexDocumentsResult deleteResult;

        if (_resilientClient != null)
        {
            deleteResult = await _resilientClient.DeleteDocumentsAsync(
                searchClient, "id", new[] { documentId }, cancellationToken);
        }
        else
        {
            var response = await searchClient.DeleteDocumentsAsync(
                "id", new[] { documentId }, cancellationToken: cancellationToken);
            deleteResult = response.Value;
        }

        var result = deleteResult.Results.FirstOrDefault();
        var succeeded = result?.Succeeded ?? false;

        if (succeeded)
        {
            _logger.LogInformation("Deleted document {DocumentId} for tenant {TenantId}", documentId, tenantId);
        }
        else
        {
            _logger.LogWarning("Failed to delete document {DocumentId} for tenant {TenantId}: {Error}",
                documentId, tenantId, result?.ErrorMessage);
        }

        return succeeded;
    }

    /// <inheritdoc />
    public async Task<int> DeleteBySourceDocumentAsync(
        string sourceDocumentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDocumentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        _logger.LogDebug("Deleting all chunks for source document {SourceDocumentId} for tenant {TenantId}",
            sourceDocumentId, tenantId);

        var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

        // First, find all chunk IDs for this source document
        var searchOptions = new SearchOptions
        {
            Filter = $"documentId eq '{EscapeFilterValue(sourceDocumentId)}' and tenantId eq '{EscapeFilterValue(tenantId)}'",
            Size = 1000,
            Select = { "id" }
        };

        SearchResults<KnowledgeDocument> searchResults;
        if (_resilientClient != null)
        {
            searchResults = await _resilientClient.SearchAsync<KnowledgeDocument>(
                searchClient, "*", searchOptions, cancellationToken);
        }
        else
        {
            var response = await searchClient.SearchAsync<KnowledgeDocument>(
                "*", searchOptions, cancellationToken);
            searchResults = response.Value;
        }

        var idsToDelete = new List<string>();
        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document?.Id != null)
            {
                idsToDelete.Add(result.Document.Id);
            }
        }

        if (idsToDelete.Count == 0)
        {
            _logger.LogDebug("No chunks found for source document {SourceDocumentId}", sourceDocumentId);
            return 0;
        }

        // Delete all found chunks (with optional resilience)
        IndexDocumentsResult deleteResult;
        if (_resilientClient != null)
        {
            deleteResult = await _resilientClient.DeleteDocumentsAsync(
                searchClient, "id", idsToDelete, cancellationToken);
        }
        else
        {
            var deleteResponse = await searchClient.DeleteDocumentsAsync(
                "id", idsToDelete, cancellationToken: cancellationToken);
            deleteResult = deleteResponse.Value;
        }
        var deletedCount = deleteResult.Results.Count(r => r.Succeeded);

        _logger.LogInformation("Deleted {DeletedCount}/{TotalCount} chunks for source document {SourceDocumentId}",
            deletedCount, idsToDelete.Count, sourceDocumentId);

        return deletedCount;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        // Try cache first
        var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(text, cancellationToken);
        if (cachedEmbedding.HasValue)
        {
            return cachedEmbedding.Value;
        }

        // Cache miss - generate and cache
        var embedding = await _openAiClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        await _embeddingCache.SetEmbeddingForContentAsync(text, embedding, cancellationToken);

        return embedding;
    }

    // ── B8: Knowledge-base index administration (task 011 Phase 1b Tier 3, D-09 §2 B8) ────
    // The 3 methods below absorb the direct SearchIndexClient calls that
    // KnowledgeBaseEndpoints (GetIndexHealth, GetIndexedDocuments, DeleteIndexedDocument)
    // previously made. Behavior is preserved 1:1 (verbatim move) per D-09 §8 Risks; this
    // is a facade refactor (ADR-007), not a redesign. The Null-Object path is
    // NullRagService which throws FeatureDisabledException for these methods.

    /// <inheritdoc />
    public async Task<KnowledgeIndexHealth> GetIndexHealthAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var knowledgeFilter = $"tenantId eq '{EscapeFilterValue(tenantId)}'";

        // Query both indexes in parallel for document counts
        var knowledgeClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.KnowledgeIndexName);
        var discoveryClient = _searchIndexClient.GetSearchClient(_aiSearchOptions.DiscoveryIndexName);

        var knowledgeCountTask = GetTenantDocumentCountAsync(knowledgeClient, knowledgeFilter, cancellationToken);
        var discoveryCountTask = GetTenantDocumentCountAsync(discoveryClient, knowledgeFilter, cancellationToken);

        await Task.WhenAll(knowledgeCountTask, discoveryCountTask);

        var health = new KnowledgeIndexHealth(
            KnowledgeDocCount: knowledgeCountTask.Result,
            DiscoveryDocCount: discoveryCountTask.Result,
            LastUpdated: DateTimeOffset.UtcNow,
            KnowledgeIndexName: _aiSearchOptions.KnowledgeIndexName,
            DiscoveryIndexName: _aiSearchOptions.DiscoveryIndexName);

        _logger.LogInformation(
            "Knowledge base health: tenant={TenantId} knowledgeDocs={KnowledgeCount} discoveryDocs={DiscoveryCount}",
            tenantId, health.KnowledgeDocCount, health.DiscoveryDocCount);

        return health;
    }

    /// <inheritdoc />
    public async Task<IndexedDocumentsPage> GetIndexedDocumentsAsync(
        string indexName,
        string tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        EnsureKnownIndex(indexName);

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var searchClient = _searchIndexClient.GetSearchClient(indexName);
        var filter = $"tenantId eq '{EscapeFilterValue(tenantId)}'";
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
        var results = new List<IndexedDocumentSummary>();

        await foreach (var item in response.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (item.Document != null)
            {
                results.Add(new IndexedDocumentSummary(
                    ChunkId: item.Document.Id,
                    DocumentId: item.Document.DocumentId,
                    FileName: item.Document.FileName,
                    CreatedAt: item.Document.CreatedAt,
                    UpdatedAt: item.Document.UpdatedAt));
            }
        }

        return new IndexedDocumentsPage(
            IndexName: indexName,
            Documents: results,
            Page: page,
            PageSize: pageSize,
            TotalCount: response.Value.TotalCount ?? 0);
    }

    /// <inheritdoc />
    public async Task<int> DeleteIndexedDocumentAsync(
        string indexName,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexName);
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        EnsureKnownIndex(indexName);

        _logger.LogInformation(
            "Deleting document {DocumentId} from index {IndexName} for tenant {TenantId}",
            documentId, indexName, tenantId);

        int chunksDeleted;

        // Route to appropriate deletion method based on index
        if (indexName.Equals(_aiSearchOptions.KnowledgeIndexName, StringComparison.OrdinalIgnoreCase))
        {
            // Use the existing knowledge-index deletion path (handles deployment routing)
            chunksDeleted = await DeleteBySourceDocumentAsync(documentId, tenantId, cancellationToken);
        }
        else
        {
            // Delete directly from the discovery index using the named SearchClient
            var searchClient = _searchIndexClient.GetSearchClient(indexName);
            chunksDeleted = await DeleteChunksFromIndexAsync(
                searchClient, documentId, tenantId, cancellationToken);
        }

        return chunksDeleted;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="indexName"/> is not one of
    /// the two admin-allowed indexes (knowledge or discovery). Used by B8 endpoints to surface
    /// a 400 / 404 ProblemDetails when an unknown index is targeted.
    /// </summary>
    private void EnsureKnownIndex(string indexName)
    {
        if (!string.Equals(indexName, _aiSearchOptions.KnowledgeIndexName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(indexName, _aiSearchOptions.DiscoveryIndexName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Index '{indexName}' is not recognized. Valid indexes: " +
                $"{_aiSearchOptions.KnowledgeIndexName}, {_aiSearchOptions.DiscoveryIndexName}",
                nameof(indexName));
        }
    }

    /// <summary>
    /// Gets the count of documents in an index that match the given OData filter.
    /// Moved verbatim from <c>KnowledgeBaseEndpoints.GetTenantDocumentCountAsync</c> per
    /// D-09 §2 B8 (task 011 Phase 1b Tier 3).
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
    /// scoped to <paramref name="tenantId"/>. Moved verbatim from
    /// <c>KnowledgeBaseEndpoints.DeleteChunksFromIndexAsync</c> per D-09 §2 B8.
    /// </summary>
    private static async Task<int> DeleteChunksFromIndexAsync(
        SearchClient searchClient,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var filter = $"documentId eq '{EscapeFilterValue(documentId)}' and tenantId eq '{EscapeFilterValue(tenantId)}'";
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

    #region Private Methods

    private SearchOptions BuildSearchOptions(
        RagSearchOptions options,
        ReadOnlyMemory<float> queryEmbedding,
        IReadOnlyList<string>? userGroupIds = null)
    {
        // R5 task 002 / FR-09 — under session-scoped routing, use the session-files
        // semantic configuration (the knowledge index's `knowledge-semantic-config` does
        // not exist on the session-files index). The session-files schema has its own
        // titleField=fileName + prioritizedContentFields=content per task 001 schema.
        var isSessionScoped = !string.IsNullOrEmpty(options.SessionId);
        var semanticConfigName = isSessionScoped
            ? _aiSearchOptions.SessionFilesSemanticConfigName
            : SemanticConfigurationName;

        var searchOptions = new SearchOptions
        {
            Size = options.TopK,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = semanticConfigName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.None)
            }
        };

        // Add vector search if enabled — both indexes use the same `contentVector3072`
        // field name (3072-dim text-embedding-3-large) per task 001 schema.
        if (options.UseVectorSearch && queryEmbedding.Length > 0)
        {
            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = options.TopK * 2, // Retrieve more for hybrid fusion
                        Fields = { VectorFieldName }
                    }
                }
            };
        }

        // Build filter expression
        var filters = new List<string>();

        // Always filter by tenant for security (ADR-014) — applied for BOTH the
        // knowledge-index and session-files-index code paths.
        filters.Add($"tenantId eq '{EscapeFilterValue(options.TenantId)}'");

        if (isSessionScoped)
        {
            // R5 task 002 / FR-09 — session isolation invariant per ADR-014: when
            // SessionId is set, both `tenantId` AND `sessionId` MUST match (ANDed
            // with the unconditional tenant filter above). Never OR — cross-tenant
            // session leaks would otherwise be possible.
            filters.Add($"sessionId eq '{EscapeFilterValue(options.SessionId!)}'");

            // The session-files schema (task 001) carries only `tenantId` + `sessionId`
            // + chunk + tags + timestamps columns. It does NOT carry
            // `knowledgeSourceId` / `parentEntityType` / `parentEntityId` /
            // `privilege_group_ids` / `documentType` filterable fields. Apply tags only;
            // skip the rest with a debug log so a misuse is visible in diagnostics.
            if (options.KnowledgeSourceId is not null
                || options.KnowledgeSourceIds is { Count: > 0 }
                || options.ExcludeKnowledgeSourceIds is { Count: > 0 }
                || !string.IsNullOrEmpty(options.DocumentType)
                || !string.IsNullOrEmpty(options.ParentEntityType)
                || !string.IsNullOrEmpty(options.ParentEntityId))
            {
                _logger.LogDebug(
                    "RAG search session-scoped routing for tenant {TenantId} session {SessionId} — " +
                    "knowledge-source / document-type / parent-entity filters are ignored " +
                    "(session-files schema does not carry those columns; see R5 task 002 + task 001 schema).",
                    options.TenantId, options.SessionId);
            }

            // Tags ARE supported on the session-files schema (Collection(Edm.String),
            // filterable). Preserve the same OR / AND / NOT semantics as the knowledge path.
            if (options.Tags?.Count > 0)
            {
                var tagFilters = options.Tags.Select(t => $"tags/any(tag: tag eq '{EscapeFilterValue(t)}')");
                filters.Add($"({string.Join(" or ", tagFilters)})");
            }
            if (options.RequiredTags is { Count: > 0 })
            {
                foreach (var tag in options.RequiredTags)
                {
                    filters.Add($"tags/any(tag: tag eq '{EscapeFilterValue(tag)}')");
                }
            }
            if (options.ExcludeTags is { Count: > 0 })
            {
                foreach (var tag in options.ExcludeTags)
                {
                    filters.Add($"not tags/any(tag: tag eq '{EscapeFilterValue(tag)}')");
                }
            }

            // Privilege filter is SKIPPED under session-scoped routing — the session-files
            // schema does not carry the `privilege_group_ids` column, and session isolation
            // is enforced by the `sessionId eq '...'` clause itself (the chat session owner
            // already passed authorization to upload the file). See R5 design.md §2.11 +
            // task 002 POML constraint "privilege-group filter (AIPU2-027)".
        }
        else
        {
            // Knowledge-index path — preserved byte-for-byte from pre-R5 (NFR-10
            // back-compat). All filter logic below this point is identical to the
            // historical implementation; only the indentation changes.

            // Include knowledge sources — KnowledgeSourceIds (plural) takes precedence over singular.
            // Use search.in() for lists > 10 items to avoid OData clause limits; OR chain below 10 for log readability.
            if (options.KnowledgeSourceIds is { Count: > 0 })
            {
                if (options.KnowledgeSourceIds.Count > 10)
                {
                    var escaped = string.Join(",", options.KnowledgeSourceIds.Select(EscapeFilterValue));
                    filters.Add($"search.in(knowledgeSourceId, '{escaped}', ',')");
                }
                else
                {
                    var sourceFilters = options.KnowledgeSourceIds
                        .Select(id => $"knowledgeSourceId eq '{EscapeFilterValue(id)}'");
                    filters.Add($"({string.Join(" or ", sourceFilters)})");
                }
            }
            else if (!string.IsNullOrEmpty(options.KnowledgeSourceId))
            {
                filters.Add($"knowledgeSourceId eq '{EscapeFilterValue(options.KnowledgeSourceId)}'");
            }

            // Exclude knowledge sources (NOT filter)
            if (options.ExcludeKnowledgeSourceIds is { Count: > 0 })
            {
                if (options.ExcludeKnowledgeSourceIds.Count > 10)
                {
                    var escaped = string.Join(",", options.ExcludeKnowledgeSourceIds.Select(EscapeFilterValue));
                    filters.Add($"not search.in(knowledgeSourceId, '{escaped}', ',')");
                }
                else
                {
                    var excludeFilters = options.ExcludeKnowledgeSourceIds
                        .Select(id => $"knowledgeSourceId eq '{EscapeFilterValue(id)}'");
                    filters.Add($"not ({string.Join(" or ", excludeFilters)})");
                }
            }

            if (!string.IsNullOrEmpty(options.DocumentType))
            {
                filters.Add($"documentType eq '{EscapeFilterValue(options.DocumentType)}'");
            }

            // Tags — OR semantics (existing): documents matching ANY of the specified tags
            if (options.Tags?.Count > 0)
            {
                var tagFilters = options.Tags.Select(t => $"tags/any(tag: tag eq '{EscapeFilterValue(t)}')");
                filters.Add($"({string.Join(" or ", tagFilters)})");
            }

            // Required tags — AND semantics: documents must have ALL of the specified tags
            if (options.RequiredTags is { Count: > 0 })
            {
                foreach (var tag in options.RequiredTags)
                {
                    filters.Add($"tags/any(tag: tag eq '{EscapeFilterValue(tag)}')");
                }
            }

            // Exclude tags — NOT semantics: exclude documents with any of the specified tags
            if (options.ExcludeTags is { Count: > 0 })
            {
                foreach (var tag in options.ExcludeTags)
                {
                    filters.Add($"not tags/any(tag: tag eq '{EscapeFilterValue(tag)}')");
                }
            }

            // Entity scope — filter by parent entity type and ID (both required)
            if (!string.IsNullOrEmpty(options.ParentEntityType) && !string.IsNullOrEmpty(options.ParentEntityId))
            {
                filters.Add($"parentEntityType eq '{EscapeFilterValue(options.ParentEntityType)}'");
                filters.Add($"parentEntityId eq '{EscapeFilterValue(options.ParentEntityId)}'");
            }

            // Privilege filter — ALWAYS applied (AIPU2-027 security requirement).
            // Ensures only documents the user's groups are authorised to view are returned.
            // Null userGroupIds means system/background call: treat as public-only.
            var privilegeFilter = PrivilegeFilterBuilder.BuildFilter(userGroupIds ?? Array.Empty<string>());
            filters.Add(privilegeFilter);
        }

        searchOptions.Filter = string.Join(" and ", filters);

        // Set search fields for keyword search — both schemas expose `content` + `fileName`
        // as searchable text fields. `knowledgeSourceName` is knowledge-only; safe to add
        // because Azure AI Search ignores unknown searchFields rather than erroring, but
        // we trim under session-routing to keep the query plan tight.
        if (options.UseKeywordSearch)
        {
            if (isSessionScoped)
            {
                searchOptions.SearchFields.Add("content");
                searchOptions.SearchFields.Add("fileName");
            }
            else
            {
                foreach (var field in SearchFields)
                {
                    searchOptions.SearchFields.Add(field);
                }
            }
        }

        // Enable highlighting
        searchOptions.HighlightFields.Add("content");

        return searchOptions;
    }

    private static IReadOnlyList<string>? GetHighlights(SearchResult<KnowledgeDocument> result)
    {
        if (result.Highlights == null || !result.Highlights.TryGetValue("content", out var highlights))
        {
            // Try semantic captions if available
            if (result.SemanticSearch?.Captions?.Count > 0)
            {
                return result.SemanticSearch.Captions
                    .Select(c => c.Highlights ?? c.Text)
                    .Where(h => !string.IsNullOrEmpty(h))
                    .ToList()!;
            }
            return null;
        }

        return highlights.ToList();
    }

    private static string EscapeFilterValue(string value)
    {
        // Escape single quotes for OData filter expressions
        return value.Replace("'", "''");
    }

    /// <summary>
    /// chat-routing-redesign-r1 task 100 — Architecture §5.2.1 binding-NEGATIVE enforcement
    /// + spec FR-36. Throws <see cref="InvalidOperationException"/> if a chat-memory
    /// retrieval path attempts to route through the Insights-domain
    /// <c>spaarke-insights-index</c>. Called from the session-scoped routing branch in
    /// <see cref="SearchAsync(string, RagSearchOptions, ClaimsPrincipal?, CancellationToken)"/>
    /// to defend against operator misconfiguration of
    /// <see cref="AiSearchOptions.SessionFilesIndexName"/>.
    /// </summary>
    /// <remarks>
    /// This is a deliberately literal name compare (case-insensitive) — the Insights index
    /// name is a stable, well-known string per architecture §7.3. Per §5.2.1 the chat-memory
    /// allowed index family is { spaarke-session-files, spaarke-files-index,
    /// spaarke-rag-references } exclusively; the Insights index is reserved for the Insights
    /// subsystem's own services. Use of the Insights index from chat-memory paths would be
    /// a categorical-mismatch design violation, not merely a misconfiguration.
    /// </remarks>
    /// <param name="indexName">The Azure AI Search index name about to be resolved.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="indexName"/> equals <c>"spaarke-insights-index"</c>.
    /// </exception>
    private static void EnsureNotInsightsIndex(string indexName)
    {
        if (string.Equals(indexName, "spaarke-insights-index", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "BINDING VIOLATION (architecture §5.2.1 + spec FR-36): chat-memory retrieval " +
                "cannot target spaarke-insights-index. The Insights index holds derived knowledge " +
                "artifacts owned by the Insights subsystem; using it from chat-memory paths would " +
                "pollute Insights records, confuse retrieval ranking, and couple two unrelated " +
                "domains. Configure AiSearchOptions.SessionFilesIndexName to one of the allowed " +
                "chat-domain indexes: spaarke-session-files (T2 session-scoped recall), " +
                "spaarke-files-index (T3 matter-scoped), or spaarke-rag-references (T4 org-scoped " +
                "knowledge). If you need Insights-domain retrieval, use the Insights subsystem's " +
                "own services (InsightsOrchestrator / IndexRetrieveNode).");
        }
    }

    /// <summary>
    /// Parses a RagQuery OData filter expression to extract tenantId and optional documentType.
    /// Expected format: "tenantId eq 'value' [and documentType eq 'value']"
    /// </summary>
    /// <param name="filterExpression">OData filter expression from RagQuery.</param>
    /// <returns>Tuple of (tenantId, documentType). DocumentType is null if not present.</returns>
    private static (string TenantId, string? DocumentType) ParseFilterExpression(string filterExpression)
    {
        var tenantId = string.Empty;
        string? documentType = null;

        // Parse "tenantId eq 'value'" clause
        var tenantMatch = System.Text.RegularExpressions.Regex.Match(
            filterExpression,
            @"tenantId eq '([^']*(?:''[^']*)*)'");

        if (tenantMatch.Success)
        {
            // Unescape single quotes (OData '' → ')
            tenantId = tenantMatch.Groups[1].Value.Replace("''", "'");
        }

        // Parse optional "documentType eq 'value'" clause
        var docTypeMatch = System.Text.RegularExpressions.Regex.Match(
            filterExpression,
            @"documentType eq '([^']*(?:''[^']*)*)'");

        if (docTypeMatch.Success)
        {
            documentType = docTypeMatch.Groups[1].Value.Replace("''", "'");
        }

        return (tenantId, documentType);
    }

    /// <summary>
    /// Populates file metadata fields (fileType) from fileName if not already set.
    /// </summary>
    /// <param name="document">The document to populate metadata for.</param>
    private static void PopulateFileMetadata(KnowledgeDocument document)
    {
        // Extract FileType from FileName if not already set
        if (string.IsNullOrEmpty(document.FileType))
        {
            document.FileType = ExtractFileType(document.FileName);
        }
    }

    /// <summary>
    /// Extracts the file type (extension without dot) from a file name.
    /// Returns "unknown" if the extension cannot be determined or is not supported.
    /// </summary>
    /// <param name="fileName">The file name to extract the type from.</param>
    /// <returns>Lowercase file extension (e.g., "pdf", "docx") or "unknown".</returns>
    private static string ExtractFileType(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "unknown";
        }

        var lastDotIndex = fileName.LastIndexOf('.');
        if (lastDotIndex < 0 || lastDotIndex == fileName.Length - 1)
        {
            return "unknown";
        }

        var extension = fileName[(lastDotIndex + 1)..].ToLowerInvariant();
        return SupportedFileTypes.Contains(extension) ? extension : "unknown";
    }

    /// <summary>
    /// Compute document-level embedding by averaging chunk contentVectors with L2 normalization.
    /// This enables document similarity visualization per the AI Azure Search Module design.
    /// </summary>
    /// <param name="chunkVectors">Content vectors from all chunks of a document.</param>
    /// <returns>Normalized averaged document vector, or empty if no valid vectors.</returns>
    private static ReadOnlyMemory<float> ComputeDocumentVector(IReadOnlyList<ReadOnlyMemory<float>> chunkVectors)
    {
        if (chunkVectors.Count == 0)
        {
            return ReadOnlyMemory<float>.Empty;
        }

        // Filter valid vectors (non-empty with correct dimensions)
        var validVectors = chunkVectors.Where(v => v.Length == VectorDimensions).ToList();
        if (validVectors.Count == 0)
        {
            return ReadOnlyMemory<float>.Empty;
        }

        // Single vector - return as-is (already normalized by OpenAI)
        if (validVectors.Count == 1)
        {
            return validVectors[0];
        }

        // Compute average of all chunk vectors
        var result = new float[VectorDimensions];
        foreach (var vector in validVectors)
        {
            var span = vector.Span;
            for (int i = 0; i < VectorDimensions; i++)
            {
                result[i] += span[i];
            }
        }

        var count = validVectors.Count;
        for (int i = 0; i < VectorDimensions; i++)
        {
            result[i] /= count;
        }

        // L2 normalization for cosine similarity
        var magnitude = 0f;
        for (int i = 0; i < VectorDimensions; i++)
        {
            magnitude += result[i] * result[i];
        }
        magnitude = MathF.Sqrt(magnitude);

        if (magnitude > 0)
        {
            for (int i = 0; i < VectorDimensions; i++)
            {
                result[i] /= magnitude;
            }
        }

        return result;
    }

    #endregion

    #region High-Performance Structured Log Events

    /// <summary>
    /// Logged before each retrieval search. QueryText is truncated to 200 chars — never log full content.
    /// </summary>
    [LoggerMessage(
        EventId = 5100,
        Level = LogLevel.Information,
        Message = "RetrievalQuery tenant={TenantId} index={IndexName} queryText={QueryText} topK={TopK}")]
    private partial void LogRetrievalQuery(string tenantId, string indexName, string queryText, int topK);

    /// <summary>
    /// Logged after each retrieval search. Contains the IDs and scores needed by the evaluation harness (AIPL-071).
    /// Vectors and document content are intentionally excluded.
    /// </summary>
    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Information,
        Message = "RetrievalResults tenant={TenantId} resultCount={ResultCount} documentIds={DocumentIds} scores={Scores} elapsedMs={ElapsedMs}")]
    private partial void LogRetrievalResults(string tenantId, int resultCount, string documentIds, string scores, long elapsedMs);

    #endregion
}
