namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Configuration for the Linear AI Consumer path.
/// Binds from <c>LinearConsumers</c> section of appsettings.
/// </summary>
/// <remarks>
/// <para>
/// R7 Wave 12 (2026-07-02) + Wave 12.3 (2026-07-02).
/// </para>
/// <para>
/// <b>ActionId lookup was retired in Wave 12.3</b> in favor of the
/// <c>sprk_playbookconsumer.sprk_action</c> routing-table column
/// (see <see cref="IActionResolver"/> +
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IConsumerRoutingService.ResolveActionAsync"/>).
/// The remaining maps here (<see cref="PlaybookIds"/>,
/// <see cref="ModelDeployments"/>, <see cref="MaxOutputTokens"/>) serve narrow
/// purposes that don't fit the routing-table primitive:
/// </para>
/// <list type="bullet">
///   <item><see cref="PlaybookIds"/>: reverse-lookup for
///         <c>AnalysisEndpoints.ExecuteAnalysis</c> — clients still submit the
///         legacy playbookId, and the endpoint needs to know which consumer
///         owns it BEFORE it can resolve an Action.</item>
///   <item><see cref="ModelDeployments"/>: per-consumer LLM deployment
///         override; environment-scoped concern, config-appropriate.</item>
///   <item><see cref="MaxOutputTokens"/>: per-consumer output cap; production
///         tuning concern, config-appropriate.</item>
/// </list>
/// <para>
/// Config example (see <c>appsettings.json</c>):
/// <code>
/// {
///   "LinearConsumers": {
///     "PlaybookIds": {
///       "document-profile": "18cf3cc8-02ec-f011-8406-7c1e520aa4df"
///     },
///     "MaxOutputTokens": {
///       "summarize-file": 4000
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
    /// Optional per-consumer max output token override. Falls back to
    /// <c>DocumentIntelligence:MaxOutputTokens</c> (default 500) when absent.
    /// Needed for consumers whose output is a large structured payload
    /// (File Summary produces up to ~2500 tokens; Doc Profile fits in ~800).
    /// Range enforced by <see cref="Sprk.Bff.Api.Services.Ai.OpenAiClient"/>
    /// implementation.
    /// </summary>
    public Dictionary<string, int> MaxOutputTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reverse-lookup: given a playbookId (as sent by the client), return the
    /// consumer-type key that owns it — always normalized to the hyphen form
    /// (matches <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.ConsumerTypes"/>
    /// constants). Returns null if no map entry matches.
    /// </summary>
    /// <remarks>
    /// Key normalization: App Service env-var rules forbid hyphens in the last
    /// segment of a hierarchical setting name, so operators configure entries
    /// as e.g. <c>LinearConsumers__PlaybookIds__document_profile</c>. Source-
    /// controlled appsettings can use the hyphen form <c>document-profile</c>.
    /// This method converts underscores → hyphens so both configurations map
    /// to the same <c>ConsumerTypes.*</c> constant.
    /// </remarks>
    public string? GetConsumerTypeForPlaybookId(Guid playbookId)
    {
        foreach (var kvp in PlaybookIds)
        {
            if (kvp.Value == playbookId)
                return kvp.Key.Replace('_', '-');
        }
        return null;
    }

    /// <summary>
    /// Look up an optional model-deployment override with the same hyphen/
    /// underscore normalization semantics as <see cref="GetConsumerTypeForPlaybookId"/>.
    /// </summary>
    public bool TryGetModelDeployment(string consumerType, out string? deployment)
    {
        deployment = null;
        if (ModelDeployments.TryGetValue(consumerType, out deployment) && !string.IsNullOrWhiteSpace(deployment))
            return true;

        var underscoreForm = consumerType.Replace('-', '_');
        if (ModelDeployments.TryGetValue(underscoreForm, out deployment) && !string.IsNullOrWhiteSpace(deployment))
            return true;

        var hyphenForm = consumerType.Replace('_', '-');
        if (ModelDeployments.TryGetValue(hyphenForm, out deployment) && !string.IsNullOrWhiteSpace(deployment))
            return true;

        deployment = null;
        return false;
    }

    /// <summary>
    /// Look up an optional max-output-tokens override with the same hyphen/
    /// underscore normalization semantics as <see cref="GetConsumerTypeForPlaybookId"/>.
    /// </summary>
    public bool TryGetMaxOutputTokens(string consumerType, out int maxTokens)
    {
        maxTokens = 0;
        if (MaxOutputTokens.TryGetValue(consumerType, out maxTokens) && maxTokens > 0)
            return true;

        var underscoreForm = consumerType.Replace('-', '_');
        if (MaxOutputTokens.TryGetValue(underscoreForm, out maxTokens) && maxTokens > 0)
            return true;

        var hyphenForm = consumerType.Replace('_', '-');
        if (MaxOutputTokens.TryGetValue(hyphenForm, out maxTokens) && maxTokens > 0)
            return true;

        maxTokens = 0;
        return false;
    }
}
