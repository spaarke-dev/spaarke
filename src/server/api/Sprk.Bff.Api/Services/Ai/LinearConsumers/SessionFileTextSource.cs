using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Fetches the extracted text for a set of chat-session-uploaded files from the
/// <c>spaarke-session-files</c> Azure AI Search index and concatenates it into a
/// single string suitable for a Linear AI Consumer (e.g., <see cref="FileSummarizeService"/>).
/// </summary>
/// <remarks>
/// R7 Wave 12.3 (2026-07-02). The chat-summarize consumer needs text from files
/// uploaded into a chat session — the source of truth is the session-files RAG
/// index (see architecture §5.2.1). This mirrors the retrieval pattern in
/// <see cref="Sprk.Bff.Api.Services.Ai.Handlers.RecallSessionFileHandler"/> but
/// pulls ALL chunks for the target files rather than a top-K subset.
/// </remarks>
public interface ISessionFileTextSource
{
    /// <summary>
    /// Retrieves + concatenates the extracted text for the specified session files.
    /// </summary>
    /// <param name="tenantId">Tenant claim (ADR-014).</param>
    /// <param name="sessionId">Chat session id.</param>
    /// <param name="files">The session-file records to summarize. Each carries a
    /// <see cref="ChatSessionFile.SearchDocumentIdsCsv"/> enumerating the chunk ids
    /// that own its content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Concatenated text (chunks ordered by fileId → chunkIndex) plus a display
    /// name suitable for logging.
    /// </returns>
    Task<SessionFileText> FetchAsync(
        string tenantId,
        string sessionId,
        IReadOnlyList<ChatSessionFile> files,
        CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="ISessionFileTextSource.FetchAsync"/>.</summary>
public sealed record SessionFileText
{
    /// <summary>Concatenated extracted text. Empty when no chunks resolved.</summary>
    public string ExtractedText { get; init; } = string.Empty;

    /// <summary>
    /// Display label — first file name when a single file is present, or
    /// "session-files-combined" for multi-file input. Passed to
    /// <c>FileSummarizeService</c> for logging + prompt substitution.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Total chunks resolved across all files (observability).</summary>
    public int ChunkCount { get; init; }
}

/// <summary>
/// <see cref="ISessionFileTextSource"/> implementation backed by
/// <see cref="IRagService"/> session-scoped search.
/// </summary>
public sealed class SessionFileTextSource : ISessionFileTextSource
{
    /// <summary>
    /// Per-file <c>TopK</c> upper bound. Session files are typically ≤50 chunks
    /// (per <c>RagIndexingPipeline</c> chunking config); 200 covers the tail while
    /// keeping the search cost bounded.
    /// </summary>
    private const int MaxChunksPerFile = 200;

    /// <summary>
    /// Broad wildcard-style query. Session-scoped searches AND with
    /// <c>sessionId eq '...'</c>, so this returns every chunk for the session
    /// regardless of textual match. Using a common word ("the") + all-toggles
    /// yields consistent recall across languages without hitting the semantic
    /// ranker's "no matches" degenerate path.
    /// </summary>
    private const string RetrieveAllQuery = "the";

    private readonly IRagService _ragService;
    private readonly ILogger<SessionFileTextSource> _logger;

    public SessionFileTextSource(IRagService ragService, ILogger<SessionFileTextSource> logger)
    {
        _ragService = ragService;
        _logger = logger;
    }

    public async Task<SessionFileText> FetchAsync(
        string tenantId,
        string sessionId,
        IReadOnlyList<ChatSessionFile> files,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return new SessionFileText { DisplayName = "session-files-empty" };
        }

        // Build allowed-chunk-id set from every target file's SearchDocumentIdsCsv.
        var allowedChunkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var chunkId in (file.SearchDocumentIdsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                allowedChunkIds.Add(chunkId);
            }
        }

        if (allowedChunkIds.Count == 0)
        {
            _logger.LogWarning(
                "SessionFileTextSource: session {SessionId} had {FileCount} files but no chunk ids in SearchDocumentIdsCsv — nothing to fetch.",
                sessionId, files.Count);
            return new SessionFileText { DisplayName = files[0].FileName };
        }

        // Session-scoped RAG search. TopK sized to cover the union of file chunks
        // (each file ≤ MaxChunksPerFile). MinScore=0 disables score filtering so
        // low-relevance chunks (e.g., appendices) still surface.
        var topK = Math.Min(MaxChunksPerFile * files.Count, 500);
        var options = new RagSearchOptions
        {
            TenantId = tenantId,
            SessionId = sessionId,
            TopK = topK,
            MinScore = 0f,
            UseSemanticRanking = false,
            UseVectorSearch = false,
            UseKeywordSearch = true,
        };

        RagSearchResponse response;
        try
        {
            response = await _ragService
                .SearchAsync(RetrieveAllQuery, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "SessionFileTextSource: session-scoped RAG search failed. TenantId={TenantId} SessionId={SessionId}",
                tenantId, sessionId);
            throw new InvalidOperationException(
                "Session-file text retrieval failed. See inner exception.", ex);
        }

        // Post-filter to just the allowed chunk ids, group by DocumentId (each
        // file maps to a distinct DocumentId in the session-files index), then
        // sort within each group by ChunkIndex to preserve document order.
        var chunks = response.Results
            .Where(r => allowedChunkIds.Contains(r.Id))
            .GroupBy(r => r.DocumentId ?? r.DocumentName ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .SelectMany(g => g.OrderBy(r => r.ChunkIndex))
            .ToList();

        if (chunks.Count == 0)
        {
            _logger.LogWarning(
                "SessionFileTextSource: session-scoped search returned {ResultCount} chunks but none matched the allowed chunk id set (TenantId={TenantId} SessionId={SessionId}).",
                response.Results.Count, tenantId, sessionId);
            return new SessionFileText { DisplayName = files[0].FileName };
        }

        // Concatenate. For multi-file, prefix each file's block with a header so
        // the LLM can reference file boundaries in the summary.
        var text = files.Count == 1
            ? string.Join("\n\n", chunks.Select(c => c.Content))
            : BuildMultiFileText(chunks, files);

        var displayName = files.Count == 1
            ? files[0].FileName
            : "session-files-combined";

        _logger.LogInformation(
            "SessionFileTextSource: retrieved {ChunkCount} chunks across {FileCount} file(s) ({TextChars} chars). SessionId={SessionId}",
            chunks.Count, files.Count, text.Length, sessionId);

        return new SessionFileText
        {
            ExtractedText = text,
            DisplayName = displayName,
            ChunkCount = chunks.Count,
        };
    }

    /// <summary>
    /// Concatenate chunks with per-file headers so the LLM can distinguish file
    /// boundaries in its summary (satisfies the SUM-CHAT@v1 prompt's
    /// "references file names when distinguishing multi-file content" rule).
    /// </summary>
    private static string BuildMultiFileText(
        IReadOnlyList<RagSearchResult> chunks,
        IReadOnlyList<ChatSessionFile> files)
    {
        // Map DocumentId (or DocumentName) → user-facing file name from the manifest.
        var fileNameByDocId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var chunkId in (file.SearchDocumentIdsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // The chunk id pattern is "{documentId}_s_{index}" (see
                // ChatDocumentEndpoints §upload + RagIndexingPipeline). Strip the
                // suffix to recover the DocumentId key.
                var underscoreIdx = chunkId.IndexOf("_s_", StringComparison.Ordinal);
                if (underscoreIdx > 0)
                {
                    var docId = chunkId[..underscoreIdx];
                    fileNameByDocId[docId] = file.FileName;
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        string? currentGroupKey = null;
        foreach (var chunk in chunks)
        {
            var key = chunk.DocumentId ?? chunk.DocumentName ?? string.Empty;
            if (key != currentGroupKey)
            {
                if (sb.Length > 0) sb.AppendLine();
                var displayName = fileNameByDocId.TryGetValue(key, out var name)
                    ? name
                    : chunk.DocumentName ?? "unknown-file";
                sb.AppendLine($"=== File: {displayName} ===");
                currentGroupKey = key;
            }
            sb.AppendLine(chunk.Content ?? string.Empty);
        }

        return sb.ToString().TrimEnd();
    }
}
