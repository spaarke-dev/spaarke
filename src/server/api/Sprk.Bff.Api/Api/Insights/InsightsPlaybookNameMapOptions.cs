namespace Sprk.Bff.Api.Api.Insights;

/// <summary>
/// Maps canonical Insights-mode playbook names (e.g., <c>"predict-matter-cost@v1"</c>)
/// to their environment-specific Dataverse <c>sprk_analysisplaybook</c> Guid. Lets
/// callers (PCFs, code pages, external clients) reference playbooks by stable name
/// across Dev / Test / Prod where each environment has a different Guid for the same
/// logically-published playbook.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bound at</b>: <c>Insights:Playbooks</c> in <c>appsettings.json</c> / App Service
/// configuration / Key Vault. Per-environment override holds the actual Guids; the
/// default <c>appsettings.json</c> ships an empty map.
/// </para>
/// <para>
/// <b>Endpoint behavior</b>: <see cref="InsightEndpoints"/>'s ask handler accepts
/// <c>request.Question</c> as either a Guid (advanced/direct path — original Phase 1
/// contract) OR a canonical name registered in <see cref="Map"/>. If neither resolves,
/// the request fails 400 with a helpful error listing the configured names. Existing
/// Guid-based callers continue to work unchanged.
/// </para>
/// <para>
/// <b>Config shape</b> (appsettings.json):
/// <code>
/// {
///   "Insights": {
///     "Playbooks": {
///       "Map": {
///         "predict-matter-cost@v1": "63b80630-975b-f111-a825-3833c5d9bcab"
///       }
///     }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// <b>Case sensitivity</b>: lookup is case-insensitive (per <see cref="StringComparer.OrdinalIgnoreCase"/>).
/// </para>
/// <para>
/// <b>Map key naming — App Service env-var constraint (Linux POSIX)</b>: Linux App
/// Service inherits POSIX env-var naming rules — keys must match
/// <c>[A-Za-z_][A-Za-z0-9_]*</c>. That means BOTH <c>@</c> AND <c>-</c> are rejected
/// when these settings are configured via App Service Application Settings or
/// Key Vault references. The API-facing playbook key MUST use snake_case_only form
/// (e.g., <c>predict_matter_cost_v1</c>) — not <c>predict-matter-cost@v1</c> and
/// not <c>predict-matter-cost-v1</c>. The Dataverse <c>sprk_analysisplaybook.sprk_name</c>
/// is unconstrained and may still use <c>@vN</c>; the map key is just whatever name
/// the API caller will send as <c>InsightAskRequest.Question</c>. File-source bindings
/// (appsettings.json) accept any string, but stick to snake_case_only form so the
/// API contract is identical across Dev / Test / Prod regardless of binding source.
/// </para>
/// <para>
/// <b>Why this layer exists</b>: Dataverse generates a fresh Guid for the
/// <c>sprk_analysisplaybook</c> row in each environment, so hard-coding the Guid in
/// caller code is non-portable. The friendly-name catalog was flagged as a Phase 1.5
/// enhancement in task 061; surfaced as a deploy-time problem before the first env
/// promotion. This is the minimal config-driven resolver.
/// </para>
/// </remarks>
public sealed class InsightsPlaybookNameMapOptions
{
    /// <summary>Configuration section name to bind to.</summary>
    public const string SectionName = "Insights:Playbooks";

    /// <summary>
    /// Map of canonical playbook name → Dataverse <c>sprk_analysisplaybook</c> Guid.
    /// Empty by default; populated per-environment.
    /// </summary>
    /// <remarks>
    /// Backing field uses <see cref="StringComparer.OrdinalIgnoreCase"/> so lookups are
    /// case-insensitive. The IOptions binder populates this via property setter, so the
    /// post-bind dictionary preserves the binder's default (case-sensitive) ordinal
    /// comparer unless we re-wrap it. <see cref="ResolveOrDefault(string)"/> handles the
    /// case-insensitive lookup explicitly to be safe across both code paths.
    /// </remarks>
    public Dictionary<string, Guid> Map { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Look up the Guid for a canonical playbook name. Returns <see cref="Guid.Empty"/>
    /// when the name is not registered (caller treats as "not found"). Case-insensitive.
    /// </summary>
    /// <param name="name">The canonical playbook name (e.g., <c>"predict-matter-cost@v1"</c>).</param>
    /// <returns>The mapped Guid, or <see cref="Guid.Empty"/> when unmapped.</returns>
    public Guid ResolveOrDefault(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || Map.Count == 0)
        {
            return Guid.Empty;
        }

        // Linear case-insensitive scan defends against the IConfigurationBinder
        // populating Map with a case-sensitive comparer. Map sizes are tiny (a handful
        // of playbooks per environment); cost is negligible vs the protection.
        foreach (var kvp in Map)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return Guid.Empty;
    }
}
