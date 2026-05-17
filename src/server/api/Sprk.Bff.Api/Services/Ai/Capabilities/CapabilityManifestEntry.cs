namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Immutable record describing a single AI capability known to the system.
///
/// Populated by <see cref="ICapabilityManifestLoader"/> from the <c>sprk_aicapability</c>
/// Dataverse table and held in memory by <see cref="CapabilityManifest"/>.
///
/// Thread-safety: record is immutable; safe to share across threads.
/// </summary>
/// <param name="CapabilityName">
/// Unique logical name matching the Dataverse <c>sprk_name</c> column
/// (e.g. "web_search", "legal_research", "write_back").
/// </param>
/// <param name="Description">
/// One-line human-readable description used in LLM system prompts and admin UIs.
/// Must be concise (≤120 chars) so the capability index stays under ~500 tokens.
/// </param>
/// <param name="KeywordHints">
/// Short keyword phrases the router uses to match incoming user messages to this capability.
/// Not used by the LLM directly — router Layer 1 pattern matching only.
/// </param>
/// <param name="PlaybookId">
/// Optional ID of the Dataverse <c>sprk_analysisplaybook</c> record that owns this capability.
/// Null for global/cross-playbook capabilities.
/// </param>
/// <param name="ToolNames">
/// Names of <see cref="Microsoft.Extensions.AI.AIFunction"/> tools that implement this capability
/// (e.g. ["SearchWeb"], ["ResearchLegal", "LookupCase"]).
/// </param>
/// <param name="IsEnabled">
/// When false the capability is inactive and excluded from <see cref="ICapabilityManifest.GetAll"/>.
/// Allows soft-disabling without deletion.
/// </param>
/// <param name="TenantRestrictions">
/// Optional list of tenant IDs allowed to use this capability.
/// Empty list means unrestricted (available to all tenants).
/// </param>
public sealed record CapabilityManifestEntry(
    string CapabilityName,
    string Description,
    IReadOnlyList<string> KeywordHints,
    Guid? PlaybookId,
    IReadOnlyList<string> ToolNames,
    bool IsEnabled,
    IReadOnlyList<string> TenantRestrictions);
