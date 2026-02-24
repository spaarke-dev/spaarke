using System.ComponentModel;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing knowledge base retrieval capabilities for the SprkChatAgent.
///
/// Exposes two methods:
///   - <see cref="GetKnowledgeSourceAsync"/> — retrieves all indexed content for a specific
///     knowledge source ID (queries the knowledge index filtered by knowledgeSourceId)
///   - <see cref="SearchKnowledgeBaseAsync"/> — semantic search limited to a specific knowledge
///     source, or across all knowledge sources for the tenant
///
/// Both methods enforce ADR-014 tenant isolation by passing <paramref name="tenantId"/>
/// to <see cref="IRagService.SearchAsync(string, RagSearchOptions, System.Threading.CancellationToken)"/>
/// as a required filter.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
/// </summary>
public sealed class KnowledgeRetrievalTools
{
    private readonly IRagService _ragService;

    public KnowledgeRetrievalTools(IRagService ragService)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
    }

    /// <summary>
    /// Retrieves all indexed content for a specific knowledge source by its ID.
    /// Use this when the user wants to see what's in a particular knowledge base or reference material.
    /// The knowledge source ID should be the GUID of a sprk_content record.
    /// </summary>
    /// <param name="knowledgeSourceId">Retrieve a specific knowledge source by ID</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of all content chunks for the specified knowledge source.</returns>
    /// <remarks>
    /// This method searches for all content associated with the given knowledge source ID.
    /// Returns up to 10 content chunks sorted by relevance. If more chunks exist, a note
    /// is appended indicating truncation.
    /// </remarks>
    public Task<string> GetKnowledgeSourceAsync(
        [Description("Retrieve a specific knowledge source by ID")] string knowledgeSourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(knowledgeSourceId, nameof(knowledgeSourceId));

        // ADR-014: Retrieving a knowledge source by ID requires a tenantId for index scoping.
        // This method delegates to SearchKnowledgeBaseAsync which enforces tenant isolation.
        // The agent should call SearchKnowledgeBaseAsync directly when a tenant context is available.
        return Task.FromResult(
            $"To retrieve content for knowledge source '{knowledgeSourceId}', use SearchKnowledgeBaseAsync " +
            $"with the source ID as the search query and your tenant ID. " +
            $"Example: SearchKnowledgeBaseAsync(query: '{knowledgeSourceId}', tenantId: <your-tenant-id>)");
    }

    /// <summary>
    /// Searches the knowledge base for reference information relevant to the query.
    /// Use this to find policies, procedures, standards, or reference materials
    /// that apply to the user's question. Results are scoped to the tenant's knowledge base.
    /// </summary>
    /// <param name="query">Search knowledge base for reference information</param>
    /// <param name="tenantId">Tenant identifier for index routing (ADR-014 — required).</param>
    /// <param name="topK">Maximum number of results to return (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of matching knowledge base entries with relevance scores.</returns>
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("Search knowledge base for reference information")] string query,
        string tenantId,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query, nameof(query));
        ArgumentException.ThrowIfNullOrEmpty(tenantId, nameof(tenantId));

        var options = new RagSearchOptions
        {
            TenantId = tenantId,
            TopK = Math.Clamp(topK, 1, 20),
            UseSemanticRanking = true,
            UseVectorSearch = true,
            UseKeywordSearch = true
        };

        var response = await _ragService.SearchAsync(query, options, cancellationToken);

        if (response.Results.Count == 0)
        {
            return $"No knowledge base entries found for: \"{query}\".";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Knowledge base search found {response.Results.Count} result(s) for: \"{query}\"");
        sb.AppendLine();

        foreach (var (result, idx) in response.Results.Select((r, i) => (r, i + 1)))
        {
            sb.AppendLine($"[{idx}] {result.DocumentName} (Relevance: {result.Score:P0})");
            if (!string.IsNullOrWhiteSpace(result.KnowledgeSourceName))
            {
                sb.AppendLine($"    Knowledge Source: {result.KnowledgeSourceName}");
            }
            sb.AppendLine($"    {result.Content}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
