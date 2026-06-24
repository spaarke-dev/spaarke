using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for looking up playbooks by the stable-ID alternate key (<c>sprk_playbookid</c>)
/// per Q&amp;A 2026-06-22 Q1. Provides caching to minimize Dataverse queries in
/// high-volume scenarios.
/// </summary>
/// <remarks>
/// Purpose: Enable SaaS multi-environment deployments without hardcoded GUIDs.
///
/// Why needed: Primary key GUIDs change when solutions deploy to new environments.
/// The stable-ID alt-key column <c>sprk_playbookid</c> (whose value mirrors the row's
/// <c>sprk_analysisplaybookid</c> PK at seed time) remains stable across DEV/QA/PROD.
///
/// NOTE: The admin-facing descriptive slug column <c>sprk_playbookcode</c> (e.g.,
/// <c>PB-002</c>, <c>PB-008</c>) is NOT used by code per Q&amp;A 2026-06-22 Q1; it exists
/// for administrators / makers and is unrelated to the runtime lookup path.
///
/// Example:
/// - DEV: Playbook "Invoice Analysis" → PK GUID 1e657651-9308-f111-8407-7c1e520aa4df
///   (sprk_playbookid alt-key mirrors the PK)
/// - QA:  Same playbook → PK GUID 9a8b7c6d-1234-5678-abcd-ef0123456789
///   (sprk_playbookid alt-key mirrors the PK in each environment)
/// </remarks>
public interface IPlaybookLookupService
{
    /// <summary>
    /// Get playbook by stable-ID alternate key (<c>sprk_playbookid</c>).
    /// Results are cached for 1 hour to minimize Dataverse queries.
    /// </summary>
    /// <param name="playbookId">Stable-ID value (GUID format, mirrors row's <c>sprk_analysisplaybookid</c> PK).</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Playbook response</returns>
    /// <exception cref="PlaybookNotFoundException">Thrown when playbook with specified id is not found</exception>
    /// <example>
    /// var playbook = await _playbookLookup.GetByIdAsync("1e657651-9308-f111-8407-7c1e520aa4df", ct);
    /// </example>
    Task<PlaybookResponse> GetByIdAsync(string playbookId, CancellationToken ct = default);

    /// <summary>
    /// Clear cached playbook for a specific id.
    /// Use when playbook configuration changes and cache needs invalidation.
    /// </summary>
    /// <param name="playbookId">Playbook id (stable-ID alt-key value) to clear from cache</param>
    void ClearCache(string playbookId);

    /// <summary>
    /// Clear all cached playbooks.
    /// Use for administrative cache flush operations.
    /// </summary>
    void ClearAllCache();
}
