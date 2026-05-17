using System.ComponentModel;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing document vector search capabilities to the SprkChatAgent.
///
/// Exposes two search methods:
///   - <see cref="SearchDocumentsAsync"/> — targeted search against the knowledge index,
///     scoped to the playbook's knowledge sources when available
///   - <see cref="SearchDiscoveryAsync"/> — broad discovery search across all documents
///     (intentionally tenant-wide, not knowledge-scoped)
///
/// Both methods enforce ADR-014 tenant isolation via a tenant ID captured at construction
/// time and passed to <see cref="IRagService.SearchAsync(string, RagSearchOptions, System.Threading.CancellationToken)"/>
/// as a required filter. The tenant ID is NOT exposed as an LLM tool parameter — it is
/// injected by <see cref="SprkChatAgentFactory"/> from the authenticated session context.
///
/// Each method populates the shared <see cref="CitationContext"/> with source metadata
/// (chunk IDs, source names, excerpts) so the AI can reference sources via citation markers
/// [N] and the frontend can render citation footnotes.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
/// </summary>
public sealed class DocumentSearchTools
{
    private readonly IRagService _ragService;
    private readonly string _tenantId;
    private readonly IReadOnlyList<string>? _knowledgeSourceIds;
    private readonly string? _parentEntityType;
    private readonly string? _parentEntityId;
    private readonly CitationContext? _citationContext;
    private readonly Func<ChatSseEvent, CancellationToken, Task>? _sseWriter;

    public DocumentSearchTools(
        IRagService ragService,
        string tenantId,
        ChatKnowledgeScope? knowledgeScope = null,
        CitationContext? citationContext = null,
        Func<ChatSseEvent, CancellationToken, Task>? sseWriter = null)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _knowledgeSourceIds = knowledgeScope?.RagKnowledgeSourceIds is { Count: > 0 }
            ? knowledgeScope.RagKnowledgeSourceIds
            : null;
        _parentEntityType = knowledgeScope?.ParentEntityType;
        _parentEntityId = knowledgeScope?.ParentEntityId;
        _citationContext = citationContext;
        _sseWriter = sseWriter;
    }

    /// <summary>
    /// Searches the knowledge index for document content relevant to the user's query.
    /// Use this when the user asks about specific topics, clauses, or information within documents.
    /// </summary>
    /// <param name="query">Search query for document knowledge</param>
    /// <param name="topK">Maximum number of results to return (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of matching document excerpts with relevance scores.</returns>
    public async Task<string> SearchDocumentsAsync(
        [Description("Search query for document knowledge")] string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query, nameof(query));

        var options = new RagSearchOptions
        {
            TenantId = _tenantId,
            TopK = Math.Clamp(topK, 1, 20),
            KnowledgeSourceIds = _knowledgeSourceIds,
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true
        };

        var response = await _ragService.SearchAsync(query, options, cancellationToken);

        if (response.Results.Count == 0)
        {
            return "No relevant documents found for the given query.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {response.Results.Count} relevant document(s) for query: \"{query}\"");
        sb.AppendLine();

        // Build widget data items in parallel with the text response for the output_pane SSE event.
        var widgetItems = new List<object>(response.Results.Count);

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];
            var citationId = _citationContext?.AddCitation(
                result.Id,
                result.DocumentName,
                pageNumber: null, // Page number not available in search index
                result.Content);

            var marker = citationId.HasValue ? $"[{citationId.Value}]" : $"[{i + 1}]";

            sb.AppendLine($"Source {marker}: {result.DocumentName} (Relevance: {result.Score:P0})");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeSourceName))
            {
                sb.AppendLine($"    Knowledge Source: {result.KnowledgeSourceName}");
            }
            sb.AppendLine($"    Chunk: {result.ChunkIndex + 1}/{result.ChunkCount}, ID: {result.Id}");
            sb.AppendLine($"    {result.Content}");
            sb.AppendLine();

            widgetItems.Add(new
            {
                chunkId = result.Id,
                documentId = result.DocumentId,
                documentName = result.DocumentName,
                knowledgeSourceName = result.KnowledgeSourceName,
                score = result.Score,
                chunkIndex = result.ChunkIndex,
                chunkCount = result.ChunkCount,
                excerpt = result.Content.Length > 400 ? result.Content[..400] + "…" : result.Content,
                citationMarker = marker
            });
        }

        // Emit output_pane SSE event so the frontend SearchResults widget renders structured cards.
        // This fires alongside the text response — the chat gets the formatted text AND
        // the output pane gets the structured widget data (Gap 1 fix).
        if (_sseWriter != null)
        {
            var outputPaneEvent = ChatSseEventFactory.CreateOutputPaneEvent(
                "SearchResults",
                new { query, results = widgetItems });
            await _sseWriter(outputPaneEvent, cancellationToken);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Performs a broad discovery search across all indexed documents for the tenant.
    /// Use this when exploring what documents are available or finding related content
    /// across the entire document corpus — not limited to a specific knowledge source.
    /// </summary>
    /// <param name="query">Broad discovery search across all documents</param>
    /// <param name="topK">Maximum number of results to return (default: 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of discovered document excerpts with relevance scores.</returns>
    public async Task<string> SearchDiscoveryAsync(
        [Description("Broad discovery search across all documents")] string query,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query, nameof(query));

        var options = new RagSearchOptions
        {
            TenantId = _tenantId,
            TopK = Math.Clamp(topK, 1, 20),
            MinScore = 0.5f, // Lower threshold for discovery to cast a wider net
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true,
            // Entity scope: when HostContext is set, constrain discovery to the parent entity boundary.
            // When null, discovery remains tenant-wide (backward compatible).
            ParentEntityType = _parentEntityType,
            ParentEntityId = _parentEntityId
        };

        var response = await _ragService.SearchAsync(query, options, cancellationToken);

        if (response.Results.Count == 0)
        {
            return "No documents discovered matching the given query.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Discovery search found {response.Results.Count} document(s) for: \"{query}\"");
        sb.AppendLine();

        // Build widget data items in parallel with the text response for the output_pane SSE event.
        var widgetItems = new List<object>(response.Results.Count);

        for (var i = 0; i < response.Results.Count; i++)
        {
            var result = response.Results[i];
            // Truncate content for discovery (show preview only)
            var preview = result.Content.Length > 300 ? result.Content[..300] + "..." : result.Content;

            var citationId = _citationContext?.AddCitation(
                result.Id,
                result.DocumentName,
                pageNumber: null, // Page number not available in search index
                preview);

            var marker = citationId.HasValue ? $"[{citationId.Value}]" : $"[{i + 1}]";

            sb.AppendLine($"Source {marker}: {result.DocumentName} (Score: {result.Score:F2})");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeSourceName))
            {
                sb.AppendLine($"    Collection: {result.KnowledgeSourceName}");
            }
            sb.AppendLine($"    Chunk: {result.ChunkIndex + 1}/{result.ChunkCount}, ID: {result.Id}");
            sb.AppendLine($"    {preview}");
            sb.AppendLine();

            widgetItems.Add(new
            {
                chunkId = result.Id,
                documentId = result.DocumentId,
                documentName = result.DocumentName,
                knowledgeSourceName = result.KnowledgeSourceName,
                score = result.Score,
                chunkIndex = result.ChunkIndex,
                chunkCount = result.ChunkCount,
                excerpt = preview,
                citationMarker = marker
            });
        }

        // Emit output_pane SSE event so the frontend SearchResults widget renders structured cards.
        // Discovery results use the same SearchResults widget type as targeted search,
        // distinguished by the isDiscovery flag in widgetData (Gap 1 fix).
        if (_sseWriter != null)
        {
            var outputPaneEvent = ChatSseEventFactory.CreateOutputPaneEvent(
                "SearchResults",
                new { query, results = widgetItems, isDiscovery = true });
            await _sseWriter(outputPaneEvent, cancellationToken);
        }

        return sb.ToString().TrimEnd();
    }
}
