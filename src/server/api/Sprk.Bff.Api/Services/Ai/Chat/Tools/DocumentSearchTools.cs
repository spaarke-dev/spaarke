using System.ComponentModel;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;

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

    public DocumentSearchTools(IRagService ragService, string tenantId, ChatKnowledgeScope? knowledgeScope = null)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _knowledgeSourceIds = knowledgeScope?.RagKnowledgeSourceIds is { Count: > 0 }
            ? knowledgeScope.RagKnowledgeSourceIds
            : null;
        _parentEntityType = knowledgeScope?.ParentEntityType;
        _parentEntityId = knowledgeScope?.ParentEntityId;
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

        foreach (var (result, idx) in response.Results.Select((r, i) => (r, i + 1)))
        {
            sb.AppendLine($"[{idx}] {result.DocumentName} (Relevance: {result.Score:P0})");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeSourceName))
            {
                sb.AppendLine($"    Source: {result.KnowledgeSourceName}");
            }
            sb.AppendLine($"    {result.Content}");
            sb.AppendLine();
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

        foreach (var (result, idx) in response.Results.Select((r, i) => (r, i + 1)))
        {
            sb.AppendLine($"[{idx}] {result.DocumentName} (Score: {result.Score:F2})");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeSourceName))
            {
                sb.AppendLine($"    Collection: {result.KnowledgeSourceName}");
            }
            // Truncate content for discovery (show preview only)
            var preview = result.Content.Length > 300 ? result.Content[..300] + "..." : result.Content;
            sb.AppendLine($"    {preview}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
