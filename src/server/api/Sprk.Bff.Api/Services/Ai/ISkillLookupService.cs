using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Cached lookup service for AI skills using portable alternate keys.
/// Minimizes Dataverse queries for skill resolution in playbook workflows.
/// </summary>
/// <remarks>
/// Performance characteristics:
/// - First lookup: ~50-100ms (Dataverse query + cache write)
/// - Cached lookups: &lt;1ms (in-memory)
/// - Cache TTL: 1 hour (skill configs rarely change)
/// - Memory usage: ~1KB per cached skill (negligible)
///
/// SaaS multi-environment support:
/// Same code works in DEV/QA/PROD without config changes.
/// Alternate keys travel with solution imports, GUIDs regenerate.
/// </remarks>
public interface ISkillLookupService
{
    /// <summary>
    /// Get skill by portable code (alternate key).
    /// Results are cached for 1 hour to minimize Dataverse queries.
    /// </summary>
    /// <param name="skillCode">Portable skill code (e.g., "SKILL-001")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Skill entity with ID and metadata</returns>
    Task<SkillResponse> GetByCodeAsync(string skillCode, CancellationToken ct = default);

    /// <summary>
    /// Clear cached skill for a specific code.
    /// </summary>
    void ClearCache(string skillCode);

    /// <summary>
    /// Clear all cached skills.
    /// </summary>
    void ClearAllCache();
}

/// <summary>
/// Skill entity response.
/// </summary>
public record SkillResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SkillCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int StatusCode { get; init; }
}
