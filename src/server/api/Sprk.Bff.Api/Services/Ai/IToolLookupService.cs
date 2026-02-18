using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for AI tools using portable alternate keys.
/// Minimizes Dataverse queries for tool resolution in playbook workflows.
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (tool configs rarely change)
/// - Memory usage: ~1KB per cached tool (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public interface IToolLookupService
{
    /// <summary>
    /// Get tool by portable code (alternate key).
    /// Results are cached for 1 hour to minimize Dataverse queries.
    /// </summary>
    /// <param name="toolCode">Portable tool code (e.g., "TL-001")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Tool entity with ID and metadata</returns>
    Task<ToolResponse> GetByCodeAsync(string toolCode, CancellationToken ct = default);

    /// <summary>
    /// Clear cached tool for a specific code.
    /// </summary>
    void ClearCache(string toolCode);

    /// <summary>
    /// Clear all cached tools.
    /// </summary>
    void ClearAllCache();
}

/// <summary>
/// Tool entity response.
/// </summary>
public record ToolResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ToolCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int StatusCode { get; init; }
}
