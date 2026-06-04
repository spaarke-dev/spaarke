using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Insights.Routing;

/// <summary>
/// ADR-032 P3 Fail-fast Null-Object for <see cref="IInsightsIntentClassifier"/>. Registered
/// when (a) the compound Analysis / DocumentIntelligence AI gate is OFF, OR (b) the
/// fine-grained <see cref="InsightsIntentClassifierOptions.Enabled"/> kill-switch is false.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why P3 (Fail-fast) not P2 (Quiet no-op)</b> per Wave E2 POML constraint + ADR-032:
/// the classifier is a query/computation service. A P2 quiet Null-Object that returned a
/// default routing decision (e.g., "always RAG") would silently mis-attribute every query
/// to the RAG path under disabled state and mislead observability about the kill-switch
/// status. P3 makes the disabled state observable: callers (the future Spaarke Assistant
/// in Wave E3, and any other consumers) catch <see cref="FeatureDisabledException"/> with
/// <c>ErrorCode = "ai.intent-classification.disabled"</c> and surface 503 ProblemDetails
/// via <see cref="FeatureDisabledResults.AsFeatureDisabled503"/>.
/// </para>
/// <para>
/// <b>Asymmetric-registration §F.1 compliance</b>: this Null-Object is registered alongside
/// the real <see cref="InsightsIntentClassifier"/> in
/// <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c> (compound-AI-OFF branches) so
/// any unconditionally-mapped endpoint or background service that injects
/// <see cref="IInsightsIntentClassifier"/> still resolves under the kill-switch. The
/// Wave E2 scaffold does NOT yet have such an unconditional consumer (the
/// <c>/api/insights/ask</c> + <c>/api/insights/search</c> endpoints accept a
/// <c>forceMode</c> wire field but do not invoke the classifier directly in E2 — the
/// classifier dispatch path is reserved for E3 Assistant integration). The Null-Object is
/// still registered for forward-compat with E3 to satisfy §F.1's binding rule (every new
/// conditional service in a <c>*Module.cs</c> <c>if (flag)</c> block requires either
/// promotion-to-unconditional or a Null-Object in the <c>else</c> branch).
/// </para>
/// <para>
/// <b>Constructor footprint</b>: only <see cref="ILogger{T}"/>. Keeps the Null-Object
/// safe to register even when AI deps (<see cref="IOpenAiClient"/>,
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/>,
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>) are unavailable
/// — per ADR-032 §MUST "Null-Object constructors MUST be minimal".
/// </para>
/// </remarks>
public sealed class NullInsightsIntentClassifier : IInsightsIntentClassifier
{
    /// <summary>Stable error code surfaced in the 503 ProblemDetails extensions.</summary>
    internal const string ErrorCode = "ai.intent-classification.disabled";

    /// <summary>Detail message for the 503 ProblemDetails body.</summary>
    internal const string DetailMessage =
        "Insights intent classification requires Insights feature enabled (Analysis:Enabled + DocumentIntelligence:Enabled + Insights:IntentClassifier:Enabled).";

    private readonly ILogger<InsightsIntentClassifier> _logger;

    public NullInsightsIntentClassifier(ILogger<InsightsIntentClassifier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IntentClassificationResult> ClassifyAsync(
        string query,
        IntentClassificationContext? context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullInsightsIntentClassifier.ClassifyAsync invoked while Insights intent classification is disabled (errorCode={ErrorCode}).",
            ErrorCode);

        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
