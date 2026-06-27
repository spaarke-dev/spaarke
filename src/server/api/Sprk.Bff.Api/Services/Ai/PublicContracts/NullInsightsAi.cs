using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IInsightsAi"/> registered when the compound
/// AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032 + D-09 §2 L1. Throws <see cref="FeatureDisabledException"/>
/// on every public method so that consumer endpoints (<c>/api/insights/ask</c>,
/// <c>/api/insights/search</c>, <c>/api/insights/assistant/query</c>, the D-P8 SPE-upload
/// consumer, the D-P4 Precedent projection sync) convert to 503 ProblemDetails per ADR-018 +
/// ADR-019. Returning empty results would silently mask the kill-switch state and mislead
/// operators / observability — fail-fast is the correct semantic.
/// </para>
/// <para>
/// <b>Stream method pre-stream invariant</b>: <see cref="AssistantQueryStreamAsync"/> throws
/// the exception synchronously before returning the <see cref="IAsyncEnumerable{T}"/>, NOT
/// as the first iterated chunk. The endpoint sees the exception before negotiating SSE
/// headers and returns 503 ProblemDetails with NO SSE body, matching ADR-032 kill-switch
/// ordering invariant + the contract documented on <see cref="IInsightsAi.AssistantQueryStreamAsync"/>.
/// </para>
/// <para>
/// Logger is injected for telemetry on disabled-feature invocation attempts; logged at
/// <c>Debug</c> level only because hitting a kill-switched feature is expected behavior
/// when test fixtures or operations set the gate OFF.
/// </para>
/// <para>
/// Introduced 2026-06-04 by <c>bff-ai-architecture-audit-r1</c> Phase 4 Migration PR #1
/// (LATENT BUG #1 remediation per W4 §4.5 + DR-003). Closes the asymmetric-registration
/// gap where <see cref="IInsightsAi"/> was registered unconditionally in
/// <c>InsightsFacadeModule</c> but its transitive ctor deps were conditional — producing
/// a runtime 500 <see cref="InvalidOperationException"/> under compound-AI-OFF instead of
/// the contract-specified 503 <see cref="FeatureDisabledException"/>.
/// </para>
/// </remarks>
public sealed class NullInsightsAi : IInsightsAi
{
    private const string ErrorCode = "ai.insights.disabled";
    private const string DetailMessage =
        "Spaarke Insights Engine requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullInsightsAi> _logger;

    public NullInsightsAi(ILogger<NullInsightsAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<InsightsAgentResult> AnswerQuestionAsync(
        InsightsAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(AnswerQuestionAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<InsightsIngestResult> RunIngestAsync(
        InsightsIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(RunIngestAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<ReadOnlyMemory<float>> EmbedTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(EmbedTextAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<InsightsSearchFacadeResult> SearchAsync(
        InsightsSearchFacadeRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(SearchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<AssistantQueryFacadeResult> AssistantQueryAsync(
        AssistantQueryFacadeRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(AssistantQueryAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public IAsyncEnumerable<AssistantQueryChunk> AssistantQueryStreamAsync(
        AssistantQueryFacadeRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(AssistantQueryStreamAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullInsightsAi.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
