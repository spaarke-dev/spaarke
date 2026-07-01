using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Null-Object subclass of <see cref="DailyBriefingCollector"/> registered when the
/// compound AI kill switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-030 + ADR-032 §F.1 — mirrors
/// <see cref="NullDailyBriefingNarrator"/>. Although the collector itself only depends on
/// CRUD-only services (<see cref="IGenericEntityService"/>, <see cref="IMembershipResolverService"/>),
/// it is registered alongside the narrator inside the compound-AI gate because the two are
/// always consumed as a pair by the <c>/api/ai/daily-briefing/render</c> endpoint. Keeping
/// the kill-switch envelope identical avoids partial-availability surprises.
/// </para>
/// <para>
/// <see cref="CollectAsync"/> throws <see cref="FeatureDisabledException"/> on first call.
/// The consuming endpoint (<c>HandleRender</c>) wraps the call in a generic try/catch that
/// returns 500 ProblemDetails. Live callers receive a clear failure mode rather than a DI
/// startup crash.
/// </para>
/// <para>
/// Construction: uses the protected base ctor that only requires <see cref="ILogger{T}"/> —
/// no dependencies are resolved. Registered via
/// <see cref="Infrastructure.DI.AnalysisServicesModule.AddNullObjectsForCompoundOff"/>.
/// </para>
/// </remarks>
public sealed class NullDailyBriefingCollector : DailyBriefingCollector
{
    private const string ErrorCode = "ai.briefing.collect.disabled";
    private const string DetailMessage =
        "Daily briefing collection requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<DailyBriefingCollector> _logger;

    public NullDailyBriefingCollector(ILogger<DailyBriefingCollector> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task<DailyBriefingNarrateRequest> CollectAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "NullDailyBriefingCollector.CollectAsync invoked while AI Daily Briefing feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);

        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
