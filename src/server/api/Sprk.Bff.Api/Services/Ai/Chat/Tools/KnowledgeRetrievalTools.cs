using System.ComponentModel;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing knowledge base retrieval capabilities for the SprkChatAgent.
///
/// Exposes two methods:
///   - <see cref="GetKnowledgeSourceAsync"/> — retrieves all indexed content for a specific
///     knowledge source ID (queries the knowledge index filtered by knowledgeSourceId)
///   - <see cref="SearchKnowledgeBaseAsync"/> — semantic search scoped to the playbook's
///     knowledge sources, or across all sources for the tenant when no scope is configured
///
/// Both methods enforce ADR-014 tenant isolation by passing tenantId
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
    private readonly IReadOnlyList<string>? _knowledgeSourceIds;

    public KnowledgeRetrievalTools(IRagService ragService, ChatKnowledgeScope? knowledgeScope = null)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _knowledgeSourceIds = knowledgeScope?.RagKnowledgeSourceIds is { Count: > 0 }
            ? knowledgeScope.RagKnowledgeSourceIds
            : null;
    }

    /// <summary>
    /// Retrieves all indexed content for a specific knowledge source by its ID.
    /// Use this when the user wants to see what's in a particular knowledge base or reference material.
    /// The knowledge source ID should be the GUID of a sprk_content record.
    /// </summary>
    /// <param name="knowledgeSourceId">Retrieve a specific knowledge source by ID</param>
    /// <param name="tenantId">Tenant identifier for index routing (ADR-014 — required).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of all content chunks for the specified knowledge source.</returns>
    public async Task<string> GetKnowledgeSourceAsync(
        [Description("Retrieve a specific knowledge source by ID")] string knowledgeSourceId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(knowledgeSourceId, nameof(knowledgeSourceId));
        ArgumentException.ThrowIfNullOrEmpty(tenantId, nameof(tenantId));

        var options = new RagSearchOptions
        {
            TenantId = tenantId,
            KnowledgeSourceId = knowledgeSourceId,
            TopK = 10,
            MinScore = 0.0f, // Return all content for this source
            UseSemanticRanking = false,
            UseVectorSearch = false,
            UseKeywordSearch = true
        };

        var response = await _ragService.SearchAsync("*", options, cancellationToken);

        if (response.Results.Count == 0)
        {
            return $"No content found for knowledge source '{knowledgeSourceId}'.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Knowledge source '{knowledgeSourceId}' contains {response.Results.Count} chunk(s):");
        sb.AppendLine();

        foreach (var (result, idx) in response.Results.Select((r, i) => (r, i + 1)))
        {
            sb.AppendLine($"[{idx}] {result.DocumentName} (Chunk {result.ChunkIndex + 1}/{result.ChunkCount})");
            sb.AppendLine($"    {result.Content}");
            sb.AppendLine();
        }

        if (response.TotalCount > response.Results.Count)
        {
            sb.AppendLine($"Note: Showing {response.Results.Count} of {response.TotalCount} total chunks.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Searches the knowledge base for reference information relevant to the query.
    /// Use this to find policies, procedures, standards, or reference materials
    /// that apply to the user's question. Results are scoped to the playbook's knowledge
    /// sources when configured, or across all sources for the tenant otherwise.
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
            KnowledgeSourceIds = _knowledgeSourceIds,
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
