namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Configuration for the Linear AI Consumer path.
/// Binds from <c>LinearConsumers</c> section of appsettings.
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 (2026-07-02). Config-driven Action + Playbook id maps let us
/// keep the dispatch simple for tonight's Doc Upload migration without
/// forcing per-tenant routing table changes. Later consumers (File Summarize,
/// Prefills) will add entries here; if we later need environment-aware or
/// per-tenant routing, we can promote this to
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IConsumerRoutingService"/>
/// -driven resolution without churning consumer service code — they'll still
/// call <see cref="IActionResolver.ResolveAsync"/>.
/// </para>
/// <para>
/// Config example (see <c>appsettings.json</c>):
/// <code>
/// {
///   "LinearConsumers": {
///     "ActionIds": {
///       "document-profile": "bb356968-ebe9-f011-8406-7ced8d1dc988"
///     },
///     "PlaybookIds": {
///       "document-profile": "18cf3cc8-02ec-f011-8406-7c1e520aa4df"
///     }
///   }
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class LinearConsumersOptions
{
    public const string SectionName = "LinearConsumers";

    /// <summary>
    /// Map of consumer-type key → <c>sprk_analysisaction</c> row id. Used by
    /// <see cref="IActionResolver"/> to look up the {SystemPrompt +
    /// OutputSchemaJson + Temperature} triple that drives the LLM call.
    /// </summary>
    public Dictionary<string, Guid> ActionIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Map of consumer-type key → <c>sprk_analysisplaybook</c> row id. Used by
    /// <c>AnalysisEndpoints.ExecuteAnalysis</c> to dispatch: an incoming
    /// request whose <c>PlaybookId</c> matches an entry here routes through
    /// the Linear consumer service; otherwise it falls through to the engine
    /// path. The mapping is retained even after the playbook rows are
    /// deactivated because clients still submit the old playbookId.
    /// </summary>
    public Dictionary<string, Guid> PlaybookIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional Azure OpenAI deployment override per consumer-type. Falls
    /// back to the client's configured default when absent. Rarely needed;
    /// most consumers should use the default.
    /// </summary>
    public Dictionary<string, string> ModelDeployments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reverse-lookup: given a playbookId (as sent by the client), return the
    /// consumer-type key that owns it — or null if none. Used by the endpoint
    /// dispatch.
    /// </summary>
    public string? GetConsumerTypeForPlaybookId(Guid playbookId)
    {
        foreach (var kvp in PlaybookIds)
        {
            if (kvp.Value == playbookId)
                return kvp.Key;
        }
        return null;
    }
}
