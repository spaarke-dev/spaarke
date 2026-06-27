namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for <see cref="Services.Ai.Insights.Routing.IInsightsIntentClassifier"/>
/// (Wave E task 041 / FR-05). Bound from <c>Insights:IntentClassifier</c> in
/// <c>appsettings.{env}.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threshold tunability</b> per POML step 4: <see cref="ConfidenceThreshold"/> is
/// SME-tunable without a code deploy. Below the threshold the classifier instructs
/// callers to fall back to the open-ended RAG path (preserves the "no false-positive
/// playbook dispatch" invariant of FR-05).
/// </para>
/// <para>
/// <b>Caching</b> per POML step 3: SHA-256 of the normalized query is the cache key;
/// <see cref="CacheTtlMinutes"/> controls the sliding TTL. Matches
/// <c>InsightsActionRouter</c>'s 15-min sliding window default — balances Dataverse-style
/// hot-path absorption with SME prompt-iteration latency.
/// </para>
/// <para>
/// <b>Model choice</b> per POML goal: classification is intentionally cheap. The default
/// (null) defers to <c>IOpenAiClient</c>'s configured <c>SummarizeModel</c>. Operators
/// that want a different deployment for routing (e.g., gpt-4o-mini vs gpt-4o) override
/// here without touching production code.
/// </para>
/// </remarks>
public sealed class InsightsIntentClassifierOptions
{
    /// <summary>Configuration section name binding.</summary>
    public const string SectionName = "Insights:IntentClassifier";

    /// <summary>
    /// Confidence below which classification result is treated as "low-confidence" and the
    /// caller MUST fall back to the open-ended RAG path. Range (0.0, 1.0]; default 0.7
    /// per POML Step 4. Below-threshold returns still carry the original path/playbook
    /// hint so observability can flag near-misses for prompt tuning.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Sliding-window TTL for the per-query classification cache. Default 15 minutes
    /// (matches <c>InsightsActionRouter</c>). Set 0 to disable caching for diagnostics.
    /// </summary>
    public int CacheTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Optional deployment-name override for the classification LLM call. Null defers
    /// to <c>IOpenAiClient</c>'s configured <c>SummarizeModel</c>. Production
    /// recommendation: gpt-4o-mini (cheap, low-latency; the classifier prompt is short
    /// and the JSON schema constrains output tokens).
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Per-call max output tokens for the classification LLM call. Default 80 — the
    /// JSON schema is small ({path, playbookId?, confidence, reason}); the prompt
    /// instructs the model to be terse. Keeps p95 well under the 500ms FR-05 budget.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 80;

    /// <summary>
    /// Fine-grained opt-out independent of the compound Analysis / DocumentIntelligence
    /// gates. When false, the registered classifier is the <c>NullInsightsIntentClassifier</c>
    /// (ADR-032 P3 Fail-fast) regardless of whether the compound AI gate is on. Useful for
    /// staged rollout — operators can ship the classifier code without enabling it.
    /// Default true (classifier is on whenever the compound AI gate is on).
    /// </summary>
    public bool Enabled { get; set; } = true;
}
