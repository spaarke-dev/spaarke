using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for looking up playbooks by portable logical identifiers (alternate keys).
/// Provides caching to minimize Dataverse queries in high-volume scenarios.
/// </summary>
/// <remarks>
/// Purpose: Enable SaaS multi-environment deployments without hardcoded GUIDs.
///
/// Why needed: Primary key GUIDs change when solutions deploy to new environments.
/// Alternate keys (sprk_playbookcode) remain stable across DEV/QA/PROD.
///
/// Example:
/// - DEV: Playbook "Invoice Analysis" → GUID 1e657651-9308-f111-8407-7c1e520aa4df
/// - QA:  Same playbook → GUID 9a8b7c6d-1234-5678-abcd-ef0123456789
/// - PROD: Same playbook → GUID 4f5e6d7c-8901-2345-bcde-123456789abc
///
/// Using alternate key "PB-013" works in all environments without code changes.
/// </remarks>
public interface IPlaybookLookupService
{
    /// <summary>
    /// Get playbook by portable code (alternate key).
    /// Results are cached for 1 hour to minimize Dataverse queries.
    /// </summary>
    /// <param name="playbookCode">Playbook code (e.g., "PB-013" for Invoice Analysis)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Playbook response</returns>
    /// <exception cref="PlaybookNotFoundException">Thrown when playbook with specified code is not found</exception>
    /// <example>
    /// var playbook = await _playbookLookup.GetByCodeAsync("PB-013", ct);
    /// // In DEV: Returns playbook with GUID 1e657651-9308-...
    /// // In QA:  Returns playbook with GUID 9a8b7c6d-1234-...
    /// // In PROD: Returns playbook with GUID 4f5e6d7c-8901-...
    /// // Code works everywhere without changes!
    /// </example>
    Task<PlaybookResponse> GetByCodeAsync(string playbookCode, CancellationToken ct = default);

    /// <summary>
    /// Clear cached playbook for a specific code.
    /// Use when playbook configuration changes and cache needs invalidation.
    /// </summary>
    /// <param name="playbookCode">Playbook code to clear from cache</param>
    void ClearCache(string playbookCode);

    /// <summary>
    /// Clear all cached playbooks.
    /// Use for administrative cache flush operations.
    /// </summary>
    void ClearAllCache();
}
