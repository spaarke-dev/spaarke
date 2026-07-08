using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// Null-Object subclass of <see cref="DailyBriefingCompositeService"/> registered when the
/// compound AI kill switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// P3 Fail-Fast pattern per ADR-032 §F.1 — mirrors <see cref="NullDailyBriefingNarrator"/>.
/// The Daily Briefing endpoints (<c>/render</c>, <c>/narrate</c>, <c>/email</c>) are mapped
/// unconditionally and inject the concrete <see cref="DailyBriefingCompositeService"/>;
/// without this mirror, minimal-API parameter inference fails at host startup when the
/// compound gate is OFF. Every method throws <see cref="FeatureDisabledException"/>;
/// the endpoints map it to the canonical 503 ProblemDetails.
/// </remarks>
public sealed class NullDailyBriefingCompositeService : DailyBriefingCompositeService
{
    private const string ErrorCode = "ai.briefing.disabled";
    private const string DetailMessage =
        "Daily briefing requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<DailyBriefingCompositeService> _logger;

    public NullDailyBriefingCompositeService(ILogger<DailyBriefingCompositeService> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task<DailyBriefingNarrateResponse> RenderAsync(
        Guid systemUserId, string tenantId, CancellationToken cancellationToken) => Throw();

    public override Task<DailyBriefingNarrateResponse> NarrateAsync(
        DailyBriefingNarrateRequest request, string tenantId, CancellationToken cancellationToken) => Throw();

    public override Task<DailyBriefingNarrateResponse> EmailAsync(
        Guid systemUserId, string tenantId, string recipientEmail, CancellationToken cancellationToken) => Throw();

    private Task<DailyBriefingNarrateResponse> Throw()
    {
        _logger.LogDebug(
            "NullDailyBriefingCompositeService invoked while AI Daily Briefing feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
