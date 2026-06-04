namespace Sprk.Bff.Api.Services.Ai.Insights.Routing;

/// <summary>
/// Wave E2 / FR-05 intent classifier — examines a natural-language Insights query and
/// returns a routing decision: dispatch to the pre-authored playbook synthesis path
/// (<c>POST /api/insights/ask</c>) or to the open-ended RAG retrieval path
/// (<c>POST /api/insights/search</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Routing/</c>; freely
/// imports <see cref="IOpenAiClient"/> for the cheap LLM classification call. The
/// Spaarke Assistant (Wave E3 task 042) is the canonical consumer; the Wave E2
/// scaffold adds the <c>forceMode</c> wire field to <c>/ask</c> + <c>/search</c> so
/// callers that already know which path they want can declare it and bypass
/// classification entirely.
/// </para>
/// <para>
/// <b>Phase 1.5 mechanism</b> per design.md D-P15-06: LLM-based classification with a
/// short few-shot prompt and JSON-schema-constrained output. Embedding-based routing
/// (faster, model-free) is deferred to Phase 2 per POML scope. The interface is stable
/// across that future evolution — the impl swap will not touch consumers.
/// </para>
/// <para>
/// <b>Confidence + threshold</b>: every result carries a 0.0–1.0 confidence. Callers
/// MUST honor the <c>BelowThreshold</c> flag and fall back to RAG when set, matching
/// FR-05 "no false-positive playbook dispatch". The threshold itself lives in
/// <see cref="Configuration.InsightsIntentClassifierOptions.ConfidenceThreshold"/>;
/// the classifier returns the comparison result so callers don't need to import the
/// options type.
/// </para>
/// <para>
/// <b>Kill-switch behavior</b> per ADR-032 §F.1 + POML constraint: the classifier is
/// registered behind the compound Analysis/DocumentIntelligence gates. When OFF, a
/// <see cref="NullInsightsIntentClassifier"/> (P3 Fail-fast Null-Object) is registered
/// instead — its <see cref="ClassifyAsync"/> throws
/// <see cref="Configuration.FeatureDisabledException"/> with
/// <c>ErrorCode = "ai.intent-classification.disabled"</c>. P3 (NOT P2 Quiet) is binding
/// for query services per ADR-032: returning an empty/default routing decision would
/// silently mis-dispatch every query to the RAG path under disabled state, misleading
/// observability about the kill-switch state.
/// </para>
/// </remarks>
public interface IInsightsIntentClassifier
{
    /// <summary>
    /// Classify the supplied natural-language query and return a routing decision.
    /// </summary>
    /// <param name="query">The user's natural-language Insights query. Required, non-whitespace.</param>
    /// <param name="context">Optional caller-side context for the classifier (subject scheme,
    /// tenant, anything that helps disambiguate). Null/empty when no context is available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="IntentClassificationResult"/> carrying the routing decision +
    /// optional playbook id + confidence.</returns>
    /// <exception cref="ArgumentException">When <paramref name="query"/> is null/whitespace.</exception>
    /// <exception cref="Configuration.FeatureDisabledException">When the AI kill-switch is OFF
    /// (propagated unchanged from <see cref="NullInsightsIntentClassifier"/> per ADR-032 P3);
    /// callers convert this to 503 ProblemDetails via the shared
    /// <c>AsFeatureDisabled503()</c> helper.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled before the result is produced.</exception>
    Task<IntentClassificationResult> ClassifyAsync(
        string query,
        IntentClassificationContext? context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional caller-side context for the classifier. Currently carries the subject scheme
/// (matter / project / invoice) and tenant id so the classifier prompt can disambiguate
/// (e.g., "predict cost" on a matter routes to predict-matter-cost@v1 but on a project
/// routes to RAG because there is no project-cost playbook yet).
/// </summary>
/// <param name="SubjectScheme">Subject scheme from the caller's parsed subject (e.g.,
/// <c>"matter"</c>, <c>"project"</c>, <c>"invoice"</c>). Null if no subject was supplied.</param>
/// <param name="TenantId">Tenant id from the authenticated principal. Used only for
/// observability / telemetry attribution; NOT for tenant-specific prompt switching in
/// Phase 1.5.</param>
public sealed record IntentClassificationContext(
    string? SubjectScheme,
    string? TenantId);

/// <summary>
/// Outcome of <see cref="IInsightsIntentClassifier.ClassifyAsync"/>.
/// </summary>
/// <param name="Path">The chosen dispatch path: <see cref="IntentPath.Playbook"/> or
/// <see cref="IntentPath.Rag"/>. When <see cref="BelowThreshold"/> is true, callers MUST
/// route to <see cref="IntentPath.Rag"/> regardless of this field (the field carries the
/// classifier's hint for observability/tuning, but threshold safety takes precedence).</param>
/// <param name="PlaybookId">Suggested playbook canonical name when <see cref="Path"/> is
/// <see cref="IntentPath.Playbook"/> (e.g., <c>"predict-matter-cost@v1"</c>). Null on the
/// RAG path. The classifier returns the name; the dispatcher resolves the name → Guid via
/// <c>InsightsPlaybookNameMapOptions</c> the same way the <c>/api/insights/ask</c>
/// endpoint does.</param>
/// <param name="Confidence">Classifier's 0.0–1.0 confidence in the decision. Below the
/// threshold (see <see cref="BelowThreshold"/>), callers fall back to RAG.</param>
/// <param name="BelowThreshold">True when <see cref="Confidence"/> &lt; the configured
/// <see cref="Configuration.InsightsIntentClassifierOptions.ConfidenceThreshold"/>. Callers
/// MUST fall back to RAG when set. The classifier returns this so callers don't need to
/// import the options type.</param>
/// <param name="Reason">Brief classifier-supplied rationale (one short sentence) for the
/// decision, captured for observability / SME prompt tuning. May be empty if the LLM omitted
/// it; never null.</param>
/// <param name="CacheHit">True when the result was served from the per-query cache rather
/// than a fresh LLM call. Carried for observability; callers may surface as a response
/// header for SME tuning.</param>
public sealed record IntentClassificationResult(
    IntentPath Path,
    string? PlaybookId,
    double Confidence,
    bool BelowThreshold,
    string Reason,
    bool CacheHit);

/// <summary>
/// Routing-decision categories returned by <see cref="IInsightsIntentClassifier"/>.
/// </summary>
public enum IntentPath
{
    /// <summary>
    /// Dispatch to <c>POST /api/insights/ask</c> with the playbook canonical name in
    /// <see cref="IntentClassificationResult.PlaybookId"/>. Use when the query maps cleanly
    /// to a pre-authored Insights-mode playbook (typed output + evidence-sufficiency gate).
    /// </summary>
    Playbook = 1,

    /// <summary>
    /// Dispatch to <c>POST /api/insights/search</c> (RAG path). Use for open-ended
    /// natural-language questions that don't map to a pre-authored playbook, OR when the
    /// classifier's confidence is below the configured threshold (FR-05 safety fall-back).
    /// </summary>
    Rag = 2
}
