using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Null-Object subclass of <see cref="DailyBriefingNarrator"/> registered when the
/// compound AI kill switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-030 + ADR-032 §F.1 — mirrors
/// <see cref="Chat.NullSessionSummarizeOrchestrator"/>. The Daily Briefing endpoints
/// (<c>POST /api/ai/daily-briefing/render</c> and <c>POST /api/ai/daily-briefing/narrate</c>,
/// mapped unconditionally by <see cref="Infrastructure.DI.EndpointMappingExtensions"/>) inject
/// <see cref="DailyBriefingNarrator"/> directly. Without a Null subclass registered on the
/// compound-OFF branch, minimal-API parameter inference fails at host startup ("Failure to
/// infer one or more parameters") because the real narrator's DI graph
/// (<see cref="AnalysisActionService"/> typed HttpClient) is unresolvable when the compound
/// AI gate is off. Observed in PR #520 CI (run 28482755126).
/// </para>
/// <para>
/// <see cref="NarrateAsync"/> throws <see cref="FeatureDisabledException"/> on first call.
/// Both consuming endpoints (<c>HandleRender</c>, <c>HandleNarrate</c>) wrap the call in a
/// generic try/catch that returns 500 ProblemDetails — sufficient because tests never invoke
/// these endpoints under compound-OFF (the kill switch is the contract). Live callers receive
/// a clear failure mode rather than a DI startup crash.
/// </para>
/// <para>
/// Construction: uses the protected base ctor that only requires <see cref="ILogger{T}"/> —
/// none of the AI dependencies are resolved, which keeps the DI graph valid when those
/// services are absent. Registered via
/// <see cref="Infrastructure.DI.AnalysisServicesModule.AddNullObjectsForCompoundOff"/>.
/// </para>
/// </remarks>
public sealed class NullDailyBriefingNarrator : DailyBriefingNarrator
{
    private const string ErrorCode = "ai.briefing.narrate.disabled";
    private const string DetailMessage =
        "Daily briefing narration requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<DailyBriefingNarrator> _logger;

    public NullDailyBriefingNarrator(ILogger<DailyBriefingNarrator> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task<DailyBriefingNarrateResponse> NarrateAsync(
        DailyBriefingNarrateRequest req,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "NullDailyBriefingNarrator.NarrateAsync invoked while AI Daily Briefing feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);

        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
