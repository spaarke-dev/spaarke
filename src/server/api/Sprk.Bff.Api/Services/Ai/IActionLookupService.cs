using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for AI actions using portable alternate keys.
/// Minimizes Dataverse queries for action resolution in playbook workflows.
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (action configs rarely change)
/// - Memory usage: ~1KB per cached action (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public interface IActionLookupService
{
    /// <summary>
    /// Get action by portable code (alternate key).
    /// Results are cached for 1 hour to minimize Dataverse queries.
    /// </summary>
    /// <param name="actionCode">Portable action code (e.g., "ACT-001")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Action entity with ID and metadata</returns>
    Task<ActionResponse> GetByCodeAsync(string actionCode, CancellationToken ct = default);

    /// <summary>
    /// Clear cached action for a specific code.
    /// </summary>
    void ClearCache(string actionCode);

    /// <summary>
    /// Clear all cached actions.
    /// </summary>
    void ClearAllCache();
}

/// <summary>
/// Action entity response.
/// </summary>
public record ActionResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ActionCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int StatusCode { get; init; }
}
