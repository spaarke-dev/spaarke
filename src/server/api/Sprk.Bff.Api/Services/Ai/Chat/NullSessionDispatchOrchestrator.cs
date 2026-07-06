using System.Runtime.CompilerServices;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Null-Object subclass of <see cref="SessionDispatchOrchestrator"/> registered when the
/// compound AI kill switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-030 + ADR-032 §F.1 — mirrors
/// <see cref="NullSessionSummarizeOrchestrator"/> exactly. The Click dispatch endpoint
/// (<c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c>, mapped unconditionally by
/// <c>EndpointMappingExtensions</c>) injects <see cref="SessionDispatchOrchestrator"/>
/// directly; without a Null subclass on the compound-OFF branch, minimal-API parameter
/// inference fails at host startup because the real orchestrator's DI graph
/// (<c>IActionRunner</c> + <c>IScopeResolverService</c> + <c>ISessionFileTextSource</c>
/// + <c>IOutputRouter</c>) is unresolvable when the compound AI gate is off.
/// </para>
/// <para>
/// <see cref="DispatchAsync"/> throws <see cref="FeatureDisabledException"/> at the
/// first <c>MoveNextAsync()</c> (the iterator's preamble). The endpoint probes the
/// orchestrator with a single <c>MoveNextAsync()</c> BEFORE setting SSE headers,
/// recognizes <see cref="FeatureDisabledException"/>, and emits the canonical 503
/// ProblemDetails via <c>FeatureDisabledResults.AsFeatureDisabled503</c>
/// (ADR-018 / ADR-019 shape).
/// </para>
/// </remarks>
public sealed class NullSessionDispatchOrchestrator : SessionDispatchOrchestrator
{
    private const string ErrorCode = "ai.dispatch.disabled";
    private const string DetailMessage =
        "AI capability dispatch requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<SessionDispatchOrchestrator> _logger;

    public NullSessionDispatchOrchestrator(ILogger<SessionDispatchOrchestrator> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async IAsyncEnumerable<AnalysisChunk> DispatchAsync(
        SessionDispatchRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullSessionDispatchOrchestrator.DispatchAsync invoked while AI dispatch is disabled (errorCode={ErrorCode}).",
            ErrorCode);

        // Throwing inside an async iterator surfaces synchronously on the FIRST
        // MoveNextAsync() — exactly what DispatchSessionEndpoint probes for in its
        // try/catch BEFORE setting SSE headers (503 ProblemDetails per ADR-018 + ADR-019).
        throw new FeatureDisabledException(ErrorCode, DetailMessage);

#pragma warning disable CS0162 // unreachable — required to make this a valid iterator method
        yield break;
#pragma warning restore CS0162
    }
}
