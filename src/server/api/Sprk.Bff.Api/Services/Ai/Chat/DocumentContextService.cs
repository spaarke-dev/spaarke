using System.Numerics.Tensors;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Injects source document content into SprkChat conversations within a 30K token budget
/// using conversation-aware semantic chunking.
///
/// <b>Factory-instantiated</b> (NOT DI-registered) per ADR-010. Created by
/// <see cref="SprkChatAgentFactory"/> with dependencies resolved from the scoped service provider.
///
/// <b>Pipeline</b>:
/// <list type="ordered">
///   <item>Retrieve document metadata from <see cref="IDocumentDataverseService"/> to get SPE IDs.</item>
///   <item>Download document binary from SPE via <see cref="ISpeFileOperations"/> facade (ADR-007).</item>
///   <item>Extract text via <see cref="ITextExtractor"/>.</item>
///   <item>Chunk text into ~500-token segments using plain text splitting (no Doc Intel layout needed
///         for chat context — layout-aware chunking is already done by <see cref="SemanticDocumentChunker"/>
///         for RAG indexing).</item>
///   <item>If total tokens &lt;= 30K: inject all chunks.</item>
///   <item>If total tokens &gt; 30K: embed latest user message, compute cosine similarity against
///         chunk embeddings, select top-N chunks within budget (conversation-aware re-selection).</item>
/// </list>
///
/// <b>Data governance (ADR-015)</b>: MUST NOT log document text content. Only metadata
/// (document ID, chunk counts, token budget) is logged.
/// </summary>
public sealed class DocumentContextService
{
    /// <summary>
    /// Maximum token budget for document context injection.
    /// Part of the overall 128K context window: 8K playbook + 30K document + ~40K history + ~50K response.
    /// </summary>
    internal const int MaxTokenBudget = 30_000;

    /// <summary>
    /// Maximum number of documents that can be processed concurrently during
    /// multi-document context injection (ADR-016: bounded concurrency).
    /// </summary>
    private const int MaxConcurrentDocuments = 5;

    /// <summary>
    /// Maximum number of documents supported in a single multi-document request.
    /// Beyond this count, per-document budget becomes too small for meaningful context.
    /// </summary>
    private const int MaxDocumentCount = 20;

    /// <summary>
    /// Target token size for each chunk when splitting plain text.
    /// Uses ~500 tokens per chunk for good granularity during re-selection.
    /// </summary>
    private const int ChunkTargetTokens = 500;

    private readonly IDocumentDataverseService _documentService;
    private readonly ISpeFileOperations _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DocumentContextService"/>.
    /// </summary>
    /// <param name="documentService">Dataverse document lookup (for SPE IDs and metadata).</param>
    /// <param name="speFileStore">SPE facade for file download (ADR-007).</param>
    /// <param name="textExtractor">Text extraction from file streams.</param>
    /// <param name="openAiClient">OpenAI client for embedding generation (conversation-aware re-selection).</param>
    /// <param name="logger">Logger for diagnostic metadata output (no content per ADR-015).</param>
    public DocumentContextService(
        IDocumentDataverseService documentService,
        ISpeFileOperations speFileStore,
        ITextExtractor textExtractor,
        IOpenAiClient openAiClient,
        ILogger logger)
    {
        _documentService = documentService;
        _speFileStore = speFileStore;
        _textExtractor = textExtractor;
        _openAiClient = openAiClient;
        _logger = logger;
    }

    /// <summary>
    /// Extracts document text, chunks it, and selects chunks within the 30K token budget.
    ///
    /// When <paramref name="latestUserMessage"/> is provided and the document exceeds the budget,
    /// chunks are ranked by embedding similarity to the user's message (conversation-aware FR-03).
    /// When null or when the document fits within budget, chunks are selected by position
    /// (beginning of document preferred).
    /// </summary>
    /// <param name="documentId">Dataverse sprk_document ID.</param>
    /// <param name="httpContext">
    /// HTTP context for OBO authentication. Required for SPE file download.
    /// May be null for background processing scenarios (falls back to app-only download).
    /// </param>
    /// <param name="latestUserMessage">
    /// The most recent user message for conversation-aware chunk re-selection.
    /// Null on initial session creation (position-based selection).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DocumentContextResult"/> with selected chunks, or an empty result
    /// on failure (soft failure — document context is enhancing, not required).
    /// </returns>
    public async Task<DocumentContextResult> InjectDocumentContextAsync(
        string documentId,
        HttpContext? httpContext,
        string? latestUserMessage = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Loading document context for {DocumentId}, hasUserMessage={HasUserMessage}",
            documentId, latestUserMessage != null);

        // 1. Load document metadata from Dataverse
        DocumentEntity? document;
        try
        {
            document = await _documentService.GetDocumentAsync(documentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load document metadata for {DocumentId}; returning empty context",
                documentId);
            return DocumentContextResult.Empty(documentId);
        }

        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found in Dataverse", documentId);
            return DocumentContextResult.Empty(documentId);
        }

        // 2. Check for valid SPE file reference
        if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has no SPE file reference (DriveId={DriveId}, ItemId={ItemId})",
                documentId, document.GraphDriveId, document.GraphItemId);
            return DocumentContextResult.Empty(documentId, document.Name);
        }

        // 3. Extract text from document
        string? extractedText;
        try
        {
            extractedText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to extract text from document {DocumentId}; returning empty context",
                documentId);
            return DocumentContextResult.Empty(documentId, document.Name);
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            _logger.LogInformation(
                "Document {DocumentId} yielded no extractable text", documentId);
            return DocumentContextResult.Empty(documentId, document.Name);
        }

        // 4. Chunk the extracted text
        var allChunks = ChunkPlainText(extractedText);

        _logger.LogInformation(
            "Document {DocumentId} chunked into {ChunkCount} chunks, total tokens ~{TotalTokens}",
            documentId, allChunks.Count, allChunks.Sum(c => c.TokenCount));

        var totalTokens = allChunks.Sum(c => c.TokenCount);

        // 5. Select chunks within budget
        if (totalTokens <= MaxTokenBudget)
        {
            // All chunks fit — include everything
            _logger.LogInformation(
                "Document {DocumentId} fits within budget ({TotalTokens} <= {Budget}); including all {Count} chunks",
                documentId, totalTokens, MaxTokenBudget, allChunks.Count);

            return new DocumentContextResult(
                DocumentId: documentId,
                DocumentName: document.Name,
                SelectedChunks: allChunks,
                TotalChunks: allChunks.Count,
                TotalTokensUsed: totalTokens,
                WasTruncated: false);
        }

        // 6. Document exceeds budget — conversation-aware re-selection
        var selected = await SelectChunksByRelevanceAsync(
            allChunks, latestUserMessage, cancellationToken);

        _logger.LogInformation(
            "Document {DocumentId} truncated: selected {SelectedCount}/{TotalCount} chunks, " +
            "using {TokensUsed}/{Budget} tokens",
            documentId, selected.Count, allChunks.Count,
            selected.Sum(c => c.TokenCount), MaxTokenBudget);

        return new DocumentContextResult(
            DocumentId: documentId,
            DocumentName: document.Name,
            SelectedChunks: selected,
            TotalChunks: allChunks.Count,
            TotalTokensUsed: selected.Sum(c => c.TokenCount),
            WasTruncated: true,
            TruncationReason: $"Document has ~{totalTokens} tokens; selected {selected.Count} of {allChunks.Count} chunks within {MaxTokenBudget} token budget");
    }

    /// <summary>
    /// Re-selects document chunks based on relevance to the latest user message.
    ///
    /// Called on each conversation turn when the document exceeds the 30K budget.
    /// Embeds the latest user message, computes cosine similarity against cached
    /// chunk embeddings, and returns the top-N chunks within budget.
    ///
    /// This enables FR-03: asking about different document sections in sequence
    /// surfaces the relevant section content each time.
    /// </summary>
    /// <param name="existingResult">
    /// The previous <see cref="DocumentContextResult"/>. The <see cref="DocumentContextResult.TotalChunks"/>
    /// indicates whether re-selection is needed (if &gt; selected chunks count).
    /// </param>
    /// <param name="allChunks">
    /// All document chunks (not just the previously selected subset). Must be provided
    /// for re-ranking since the previous result only contains the selected subset.
    /// </param>
    /// <param name="latestUserMessage">
    /// The most recent user message to rank chunks against.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated <see cref="DocumentContextResult"/> with re-ranked chunk selection.</returns>
    public async Task<DocumentContextResult> RefineContextAsync(
        DocumentContextResult existingResult,
        IReadOnlyList<DocumentChunk> allChunks,
        string latestUserMessage,
        CancellationToken cancellationToken = default)
    {
        if (!existingResult.WasTruncated || string.IsNullOrWhiteSpace(latestUserMessage))
        {
            // No re-selection needed if document fits in budget or no user message
            return existingResult;
        }

        _logger.LogInformation(
            "Refining document context for {DocumentId} based on user message",
            existingResult.DocumentId);

        var selected = await SelectChunksByRelevanceAsync(
            allChunks, latestUserMessage, cancellationToken);

        _logger.LogInformation(
            "Refined document {DocumentId}: selected {SelectedCount}/{TotalCount} chunks, " +
            "using {TokensUsed}/{Budget} tokens",
            existingResult.DocumentId, selected.Count, allChunks.Count,
            selected.Sum(c => c.TokenCount), MaxTokenBudget);

        return existingResult with
        {
            SelectedChunks = selected,
            TotalTokensUsed = selected.Sum(c => c.TokenCount),
            TruncationReason = $"Re-selected {selected.Count} of {allChunks.Count} chunks by relevance to latest message"
        };
    }

    // -------------------------------------------------------------------------
    // Multi-document context injection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts and selects content from multiple documents within a shared 30K token budget.
    ///
    /// <b>Budget allocation strategy</b>:
    /// <list type="ordered">
    ///   <item>Equal proportional allocation: each document gets <c>30K / N</c> tokens initially.</item>
    ///   <item>Parallel retrieval bounded by <see cref="SemaphoreSlim"/> (max 5 concurrent, ADR-016).</item>
    ///   <item>Leftover reallocation: documents smaller than their share donate unused tokens proportionally.</item>
    ///   <item>Conversation-aware chunk selection per document using embedding similarity.</item>
    ///   <item>Cross-document chunk interleaving: merge selected chunks sorted by relevance score.</item>
    /// </list>
    ///
    /// Supports FR-12: multi-document analysis where users compare and cross-reference up to 5 documents.
    /// </summary>
    /// <param name="documentIds">Dataverse sprk_document IDs to include in context.</param>
    /// <param name="httpContext">HTTP context for OBO authentication (SPE file download).</param>
    /// <param name="latestUserMessage">
    /// The most recent user message for conversation-aware chunk re-selection.
    /// Null on initial session creation (position-based selection).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="MultiDocumentContextResult"/> with per-document groups and cross-document
    /// interleaved chunks within the shared 30K token budget.
    /// </returns>
    public async Task<MultiDocumentContextResult> InjectMultiDocumentContextAsync(
        IReadOnlyList<string> documentIds,
        HttpContext? httpContext,
        string? latestUserMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
        {
            _logger.LogInformation("No document IDs provided for multi-document context");
            return MultiDocumentContextResult.Empty();
        }

        if (documentIds.Count > MaxDocumentCount)
        {
            _logger.LogWarning(
                "Multi-document request exceeds maximum ({Count} > {Max}); truncating to first {Max} documents",
                documentIds.Count, MaxDocumentCount, MaxDocumentCount);
            documentIds = documentIds.Take(MaxDocumentCount).ToList();
        }

        _logger.LogInformation(
            "Loading multi-document context for {DocumentCount} documents, hasUserMessage={HasUserMessage}",
            documentIds.Count, latestUserMessage != null);

        // 1. Initial proportional allocation
        var perDocBudget = MaxTokenBudget / documentIds.Count;

        // 2. Retrieve and chunk all documents in parallel (bounded concurrency — ADR-016)
        var semaphore = new SemaphoreSlim(MaxConcurrentDocuments);
        var documentResults = new (string DocumentId, string? DocumentName, IReadOnlyList<DocumentChunk> AllChunks, int TotalTokens)[documentIds.Count];

        await Task.WhenAll(documentIds.Select(async (docId, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await RetrieveAndChunkDocumentAsync(docId, httpContext, cancellationToken);
                documentResults[index] = result;
            }
            finally
            {
                semaphore.Release();
            }
        }));

        // 3. Leftover reallocation — documents smaller than their share donate unused tokens
        var allocations = ComputeBudgetAllocations(documentResults, perDocBudget);

        // Log per-document allocations (ADR-015: no content logging)
        for (var i = 0; i < documentResults.Length; i++)
        {
            _logger.LogInformation(
                "Document {DocumentId}: {TotalTokens} total tokens, allocated {AllocatedTokens} tokens, " +
                "{ChunkCount} chunks",
                documentResults[i].DocumentId,
                documentResults[i].TotalTokens,
                allocations[i],
                documentResults[i].AllChunks.Count);
        }

        // 4. Apply conversation-aware chunk selection per document within allocated budgets
        var allScoredChunks = new List<ScoredChunk>();
        var documentGroups = new List<DocumentChunkGroup>();

        // Generate user message embedding once (shared across all documents)
        ReadOnlyMemory<float>? messageEmbedding = null;
        if (!string.IsNullOrWhiteSpace(latestUserMessage))
        {
            try
            {
                messageEmbedding = await _openAiClient.GenerateEmbeddingAsync(
                    latestUserMessage, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate embedding for user message; falling back to position-based selection");
            }
        }

        for (var docIndex = 0; docIndex < documentResults.Length; docIndex++)
        {
            var (docId, docName, allChunks, totalTokens) = documentResults[docIndex];
            var docBudget = allocations[docIndex];

            if (allChunks.Count == 0)
            {
                documentGroups.Add(new DocumentChunkGroup(
                    DocumentId: docId,
                    DocumentName: docName,
                    SelectedChunks: Array.Empty<DocumentChunk>(),
                    TotalChunks: 0,
                    TokensAllocated: docBudget,
                    TokensUsed: 0,
                    WasTruncated: false));
                continue;
            }

            // Select chunks within this document's allocated budget
            var (selectedChunks, scoredChunks) = await SelectChunksWithScoresAsync(
                allChunks, docBudget, messageEmbedding, docIndex, docName, cancellationToken);

            allScoredChunks.AddRange(scoredChunks);

            var tokensUsed = selectedChunks.Sum(c => c.TokenCount);
            documentGroups.Add(new DocumentChunkGroup(
                DocumentId: docId,
                DocumentName: docName,
                SelectedChunks: selectedChunks,
                TotalChunks: allChunks.Count,
                TokensAllocated: docBudget,
                TokensUsed: tokensUsed,
                WasTruncated: totalTokens > docBudget));
        }

        // 5. Cross-document chunk interleaving: sort all scored chunks by relevance, enforce total budget
        var mergedChunks = InterleaveCrossDocument(allScoredChunks);

        var totalTokensUsed = mergedChunks.Sum(c => c.TokenCount);
        var anyTruncated = documentGroups.Any(g => g.WasTruncated);

        _logger.LogInformation(
            "Multi-document context complete: {DocumentCount} documents, {MergedChunkCount} merged chunks, " +
            "{TotalTokensUsed}/{Budget} tokens, anyTruncated={AnyTruncated}",
            documentGroups.Count, mergedChunks.Count, totalTokensUsed, MaxTokenBudget, anyTruncated);

        return new MultiDocumentContextResult(
            DocumentGroups: documentGroups.AsReadOnly(),
            MergedChunks: mergedChunks,
            TotalTokensUsed: totalTokensUsed,
            AnyTruncated: anyTruncated);
    }

    /// <summary>
    /// Retrieves a single document's metadata, downloads, extracts text, and chunks it.
    /// Returns the raw chunks without budget selection (that happens in the caller).
    /// </summary>
    private async Task<(string DocumentId, string? DocumentName, IReadOnlyList<DocumentChunk> AllChunks, int TotalTokens)>
        RetrieveAndChunkDocumentAsync(
            string documentId,
            HttpContext? httpContext,
            CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentService.GetDocumentAsync(documentId, cancellationToken);
            if (document == null)
            {
                _logger.LogWarning("Document {DocumentId} not found in Dataverse", documentId);
                return (documentId, null, Array.Empty<DocumentChunk>(), 0);
            }

            if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
            {
                _logger.LogWarning(
                    "Document {DocumentId} has no SPE file reference", documentId);
                return (documentId, document.Name, Array.Empty<DocumentChunk>(), 0);
            }

            var extractedText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogInformation(
                    "Document {DocumentId} yielded no extractable text", documentId);
                return (documentId, document.Name, Array.Empty<DocumentChunk>(), 0);
            }

            var allChunks = ChunkPlainText(extractedText);
            var totalTokens = allChunks.Sum(c => c.TokenCount);

            _logger.LogInformation(
                "Document {DocumentId} chunked into {ChunkCount} chunks, total tokens ~{TotalTokens}",
                documentId, allChunks.Count, totalTokens);

            return (documentId, document.Name, allChunks, totalTokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to retrieve/chunk document {DocumentId}; returning empty",
                documentId);
            return (documentId, null, Array.Empty<DocumentChunk>(), 0);
        }
    }

    /// <summary>
    /// Computes final budget allocations with leftover reallocation.
    /// Documents smaller than their proportional share donate unused tokens
    /// to documents that need more budget.
    /// </summary>
    private static int[] ComputeBudgetAllocations(
        (string DocumentId, string? DocumentName, IReadOnlyList<DocumentChunk> AllChunks, int TotalTokens)[] documentResults,
        int initialPerDocBudget)
    {
        var allocations = new int[documentResults.Length];
        var leftover = 0;
        var needsMore = new List<int>();

        // First pass: identify small documents and collect leftover
        for (var i = 0; i < documentResults.Length; i++)
        {
            var totalTokens = documentResults[i].TotalTokens;

            if (totalTokens <= initialPerDocBudget)
            {
                // Document fits within its share — allocate exactly what it needs
                allocations[i] = totalTokens;
                leftover += initialPerDocBudget - totalTokens;
            }
            else
            {
                // Document needs more than its share — mark for reallocation
                allocations[i] = initialPerDocBudget;
                needsMore.Add(i);
            }
        }

        // Second pass: redistribute leftover proportionally to documents that need more
        if (leftover > 0 && needsMore.Count > 0)
        {
            var extraPerDoc = leftover / needsMore.Count;
            var remainder = leftover % needsMore.Count;

            for (var j = 0; j < needsMore.Count; j++)
            {
                var idx = needsMore[j];
                var extra = extraPerDoc + (j < remainder ? 1 : 0);
                allocations[idx] += extra;

                // Cap at document's actual size (don't over-allocate)
                if (allocations[idx] > documentResults[idx].TotalTokens)
                    allocations[idx] = documentResults[idx].TotalTokens;
            }
        }

        // Enforce hard cap: total allocations must not exceed MaxTokenBudget
        var totalAllocated = allocations.Sum();
        if (totalAllocated > MaxTokenBudget)
        {
            // Scale down proportionally
            var scale = (double)MaxTokenBudget / totalAllocated;
            for (var i = 0; i < allocations.Length; i++)
            {
                allocations[i] = (int)(allocations[i] * scale);
            }
        }

        return allocations;
    }

    /// <summary>
    /// Selects chunks for a single document within its allocated budget, returning both
    /// the selected chunks and their scored representations for cross-document interleaving.
    /// </summary>
    private async Task<(IReadOnlyList<DocumentChunk> Selected, IReadOnlyList<ScoredChunk> Scored)>
        SelectChunksWithScoresAsync(
            IReadOnlyList<DocumentChunk> allChunks,
            int budgetTokens,
            ReadOnlyMemory<float>? messageEmbedding,
            int documentIndex,
            string? documentName,
            CancellationToken cancellationToken)
    {
        if (messageEmbedding == null)
        {
            // Position-based selection (no user message or embedding failed)
            var selected = new List<DocumentChunk>();
            var scored = new List<ScoredChunk>();
            var tokensUsed = 0;

            for (var i = 0; i < allChunks.Count; i++)
            {
                var chunk = allChunks[i];
                if (tokensUsed + chunk.TokenCount > budgetTokens)
                    break;

                selected.Add(chunk);
                // Assign decreasing score by position so early chunks rank higher
                scored.Add(new ScoredChunk(chunk, 1.0 - (i * 0.001), documentIndex, documentName));
                tokensUsed += chunk.TokenCount;
            }

            return (selected.AsReadOnly(), scored.AsReadOnly());
        }

        try
        {
            // Generate embeddings for all chunks
            var chunkTexts = allChunks.Select(c => c.Content).ToList();
            var chunkEmbeddings = await _openAiClient.GenerateEmbeddingsAsync(
                chunkTexts, cancellationToken: cancellationToken);

            // Score each chunk by cosine similarity
            var scoredChunks = new List<ScoredChunk>();
            for (var i = 0; i < allChunks.Count; i++)
            {
                var similarity = CosineSimilarity(
                    messageEmbedding.Value.Span, chunkEmbeddings[i].Span);
                scoredChunks.Add(new ScoredChunk(allChunks[i], similarity, documentIndex, documentName));
            }

            // Sort by score descending, select greedily within budget
            scoredChunks.Sort((a, b) => b.RelevanceScore.CompareTo(a.RelevanceScore));

            var selectedChunks = new List<DocumentChunk>();
            var selectedScored = new List<ScoredChunk>();
            var tokensUsed = 0;

            foreach (var scored in scoredChunks)
            {
                if (tokensUsed + scored.Chunk.TokenCount > budgetTokens)
                    continue;

                selectedChunks.Add(scored.Chunk);
                selectedScored.Add(scored);
                tokensUsed += scored.Chunk.TokenCount;
            }

            return (selectedChunks.AsReadOnly(), selectedScored.AsReadOnly());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Embedding-based selection failed for document index {DocumentIndex}; using position-based fallback",
                documentIndex);

            // Fallback to position-based
            var selected = new List<DocumentChunk>();
            var scored = new List<ScoredChunk>();
            var tokensUsed = 0;

            for (var i = 0; i < allChunks.Count; i++)
            {
                var chunk = allChunks[i];
                if (tokensUsed + chunk.TokenCount > budgetTokens)
                    break;

                selected.Add(chunk);
                scored.Add(new ScoredChunk(chunk, 1.0 - (i * 0.001), documentIndex, documentName));
                tokensUsed += chunk.TokenCount;
            }

            return (selected.AsReadOnly(), scored.AsReadOnly());
        }
    }

    /// <summary>
    /// Interleaves scored chunks from all documents sorted by relevance score,
    /// enforcing the total 30K token budget cap.
    /// </summary>
    private static IReadOnlyList<DocumentChunk> InterleaveCrossDocument(
        List<ScoredChunk> allScoredChunks)
    {
        if (allScoredChunks.Count == 0)
            return Array.Empty<DocumentChunk>();

        // Sort all chunks across all documents by relevance score (highest first)
        allScoredChunks.Sort((a, b) => b.RelevanceScore.CompareTo(a.RelevanceScore));

        var merged = new List<DocumentChunk>();
        var tokensUsed = 0;

        foreach (var scored in allScoredChunks)
        {
            if (tokensUsed + scored.Chunk.TokenCount > MaxTokenBudget)
                continue;

            merged.Add(scored.Chunk);
            tokensUsed += scored.Chunk.TokenCount;
        }

        return merged.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // Private: text extraction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Downloads and extracts text from a document using SpeFileStore (ADR-007)
    /// and TextExtractor.
    /// </summary>
    private async Task<string?> ExtractDocumentTextAsync(
        DocumentEntity document,
        HttpContext? httpContext,
        CancellationToken cancellationToken)
    {
        var fileName = document.FileName ?? "document";

        Stream? fileStream;
        if (httpContext != null)
        {
            // OBO authentication (preferred — user-delegated permissions)
            fileStream = await _speFileStore.DownloadFileAsUserAsync(
                httpContext,
                document.GraphDriveId!,
                document.GraphItemId!,
                cancellationToken);
        }
        else
        {
            // App-only fallback (background processing scenarios)
            fileStream = await _speFileStore.DownloadFileAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                cancellationToken);
        }

        if (fileStream == null)
        {
            _logger.LogWarning(
                "Failed to download document {DocumentId} from SPE — stream is null",
                document.Id);
            return null;
        }

        using (fileStream)
        {
            var result = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Text extraction failed for document {DocumentId}: {Error}",
                    document.Id, result.ErrorMessage);
                return null;
            }

            // ADR-015: Do NOT log text content — only metadata
            _logger.LogDebug(
                "Extracted text from document {DocumentId}: {CharCount} characters, method={Method}",
                document.Id, result.Text?.Length ?? 0, result.Method);

            return result.Text;
        }
    }

    // -------------------------------------------------------------------------
    // Private: chunking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Splits plain text into chunks of approximately <see cref="ChunkTargetTokens"/> tokens.
    /// Uses paragraph boundaries when possible, falling back to sentence/word boundaries.
    /// </summary>
    /// <remarks>
    /// This is a simplified chunker for chat context injection. The full
    /// <see cref="SemanticDocumentChunker"/> operates on Document Intelligence
    /// <c>AnalyzeResult</c> and is used for RAG indexing. Here we work with
    /// already-extracted plain text and use simpler boundary detection.
    /// </remarks>
    private static IReadOnlyList<DocumentChunk> ChunkPlainText(string text)
    {
        var chunks = new List<DocumentChunk>();
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        var buffer = new System.Text.StringBuilder();
        var chunkIndex = 0;

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var paragraphTokens = EstimateTokens(trimmed);

            // If adding this paragraph would exceed the chunk target and we already have content,
            // flush the buffer as a chunk.
            if (buffer.Length > 0 && EstimateTokens(buffer.ToString()) + paragraphTokens > ChunkTargetTokens)
            {
                var content = buffer.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    chunks.Add(new DocumentChunk(
                        Content: content,
                        SectionTitle: null,
                        PageNumber: 1,
                        ChunkIndex: chunkIndex,
                        TokenCount: EstimateTokens(content)));
                    chunkIndex++;
                }
                buffer.Clear();
            }

            // If a single paragraph exceeds the chunk target, split it further
            if (paragraphTokens > ChunkTargetTokens && buffer.Length == 0)
            {
                var subChunks = SplitLargeParagraph(trimmed, chunkIndex);
                chunks.AddRange(subChunks);
                chunkIndex += subChunks.Count;
                continue;
            }

            if (buffer.Length > 0)
                buffer.Append("\n\n");

            buffer.Append(trimmed);
        }

        // Flush remaining content
        if (buffer.Length > 0)
        {
            var content = buffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                chunks.Add(new DocumentChunk(
                    Content: content,
                    SectionTitle: null,
                    PageNumber: 1,
                    ChunkIndex: chunkIndex,
                    TokenCount: EstimateTokens(content)));
            }
        }

        return chunks.AsReadOnly();
    }

    /// <summary>
    /// Splits a large paragraph into chunks at sentence boundaries.
    /// </summary>
    private static IReadOnlyList<DocumentChunk> SplitLargeParagraph(string text, int startIndex)
    {
        var chunks = new List<DocumentChunk>();
        // Split on sentence boundaries (period, question mark, exclamation followed by space)
        var sentences = text.Split(new[] { ". ", "? ", "! " }, StringSplitOptions.RemoveEmptyEntries);

        var buffer = new System.Text.StringBuilder();
        var chunkIndex = startIndex;

        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (buffer.Length > 0 && EstimateTokens(buffer.ToString()) + EstimateTokens(trimmed) > ChunkTargetTokens)
            {
                var content = buffer.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    chunks.Add(new DocumentChunk(
                        Content: content,
                        SectionTitle: null,
                        PageNumber: 1,
                        ChunkIndex: chunkIndex,
                        TokenCount: EstimateTokens(content)));
                    chunkIndex++;
                }
                buffer.Clear();
            }

            if (buffer.Length > 0)
                buffer.Append(". ");

            buffer.Append(trimmed);
        }

        if (buffer.Length > 0)
        {
            var content = buffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                chunks.Add(new DocumentChunk(
                    Content: content,
                    SectionTitle: null,
                    PageNumber: 1,
                    ChunkIndex: chunkIndex,
                    TokenCount: EstimateTokens(content)));
            }
        }

        return chunks;
    }

    // -------------------------------------------------------------------------
    // Private: conversation-aware chunk selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selects chunks by embedding similarity to the latest user message.
    /// Falls back to position-based selection when no user message is provided
    /// or embedding generation fails.
    /// </summary>
    private async Task<IReadOnlyList<DocumentChunk>> SelectChunksByRelevanceAsync(
        IReadOnlyList<DocumentChunk> allChunks,
        string? latestUserMessage,
        CancellationToken cancellationToken)
    {
        // When no user message, use position-based selection (prefer beginning of document)
        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return SelectByPosition(allChunks);
        }

        try
        {
            // Generate embedding for the user's message
            var messageEmbedding = await _openAiClient.GenerateEmbeddingAsync(
                latestUserMessage, cancellationToken: cancellationToken);

            // Generate embeddings for all chunks (batch for efficiency)
            var chunkTexts = allChunks.Select(c => c.Content).ToList();
            var chunkEmbeddings = await _openAiClient.GenerateEmbeddingsAsync(
                chunkTexts, cancellationToken: cancellationToken);

            // Score each chunk by cosine similarity to the user message
            var scoredChunks = new List<(DocumentChunk Chunk, double Score)>();
            for (var i = 0; i < allChunks.Count; i++)
            {
                var similarity = CosineSimilarity(
                    messageEmbedding.Span, chunkEmbeddings[i].Span);
                scoredChunks.Add((allChunks[i], similarity));
            }

            // Sort by similarity descending, then select greedily within budget
            scoredChunks.Sort((a, b) => b.Score.CompareTo(a.Score));

            var selected = new List<DocumentChunk>();
            var tokensUsed = 0;

            foreach (var (chunk, _) in scoredChunks)
            {
                if (tokensUsed + chunk.TokenCount > MaxTokenBudget)
                    continue; // Skip chunks that would exceed budget

                selected.Add(chunk);
                tokensUsed += chunk.TokenCount;
            }

            // Re-sort selected chunks by original position for coherent reading order
            selected.Sort((a, b) => a.ChunkIndex.CompareTo(b.ChunkIndex));

            return selected.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Embedding-based chunk selection failed; falling back to position-based selection");
            return SelectByPosition(allChunks);
        }
    }

    /// <summary>
    /// Selects chunks by position (beginning of document preferred) within the token budget.
    /// Used as initial selection and as fallback when embedding generation fails.
    /// </summary>
    private static IReadOnlyList<DocumentChunk> SelectByPosition(IReadOnlyList<DocumentChunk> allChunks)
    {
        var selected = new List<DocumentChunk>();
        var tokensUsed = 0;

        foreach (var chunk in allChunks)
        {
            if (tokensUsed + chunk.TokenCount > MaxTokenBudget)
                break;

            selected.Add(chunk);
            tokensUsed += chunk.TokenCount;
        }

        return selected.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Estimates token count using the standard chars / 4 approximation.
    /// Consistent with <see cref="SemanticDocumentChunker.TokenCount"/>.
    /// </summary>
    internal static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 4);

    /// <summary>
    /// Computes cosine similarity between two embedding vectors.
    /// Returns a value between -1 and 1 (1 = most similar).
    /// </summary>
    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        return TensorPrimitives.CosineSimilarity(a, b);
    }
}
