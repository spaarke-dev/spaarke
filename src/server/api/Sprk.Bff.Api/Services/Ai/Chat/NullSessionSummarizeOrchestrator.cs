using System.Runtime.CompilerServices;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Null-Object subclass of <see cref="SessionSummarizeOrchestrator"/> registered when the
/// compound AI kill switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-030 + D-09 §2 — mirrors <see cref="NullSprkChatAgentFactory"/>
/// (B2) and <see cref="NullPendingPlanManager"/> (B3). The R5 Summarize endpoint
/// (<c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>, mapped unconditionally by
/// <c>EndpointMappingExtensions</c>) injects <see cref="SessionSummarizeOrchestrator"/>
/// directly. Without a Null subclass registered on the compound-OFF branch, minimal-API
/// parameter inference fails at host startup ("Failure to infer one or more parameters")
/// because the real orchestrator's DI graph (<c>IRagService</c> + <c>IOpenAiClient</c> +
/// <c>IGenericEntityService</c>) is unresolvable when the compound AI gate is off.
/// </para>
/// <para>
/// <see cref="SummarizeSessionFilesAsync"/> throws <see cref="FeatureDisabledException"/>
/// at the first <c>MoveNextAsync()</c> (the iterator's preamble). The endpoint
/// (<c>SummarizeSessionEndpoint.SummarizeAsync</c>) probes the orchestrator with a single
/// <c>MoveNextAsync()</c> inside a try/catch BEFORE setting SSE headers, recognizes
/// <see cref="FeatureDisabledException"/>, and emits a 503 ProblemDetails via
/// <c>FeatureDisabledResults.AsFeatureDisabled503</c> (canonical ADR-018 / ADR-019 shape).
/// </para>
/// <para>
/// Construction: uses the protected base ctor that only requires <c>ILogger</c> — none of
/// the AI dependencies are resolved, which keeps the DI graph valid when those services
/// are absent. Registered via <c>AnalysisServicesModule.AddNullObjectsForCompoundOff</c>.
/// </para>
/// </remarks>
public sealed class NullSessionSummarizeOrchestrator : SessionSummarizeOrchestrator
{
    private const string ErrorCode = "ai.summarize.disabled";
    private const string DetailMessage =
        "AI Summarize requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<SessionSummarizeOrchestrator> _logger;

    public NullSessionSummarizeOrchestrator(ILogger<SessionSummarizeOrchestrator> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async IAsyncEnumerable<AnalysisChunk> SummarizeSessionFilesAsync(
        SummarizeSessionFilesRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullSessionSummarizeOrchestrator.SummarizeSessionFilesAsync invoked while AI Summarize feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);

        // Throwing inside an async iterator surfaces synchronously on the FIRST MoveNextAsync()
        // — exactly what SummarizeSessionEndpoint.SummarizeAsync probes for in its
        // try/catch BEFORE setting SSE headers. The endpoint maps FeatureDisabledException
        // to a 503 ProblemDetails per ADR-018 + ADR-019.
        throw new FeatureDisabledException(ErrorCode, DetailMessage);

#pragma warning disable CS0162 // unreachable — required to make this a valid iterator method
        yield break;
#pragma warning restore CS0162
    }
}
