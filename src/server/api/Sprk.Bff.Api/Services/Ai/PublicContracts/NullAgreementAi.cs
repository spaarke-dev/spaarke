using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IAgreementAi"/>, registered when the compound AI kill switch is OFF
/// (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// P3 Fail-Fast per ADR-032: every method throws <see cref="FeatureDisabledException"/> so the intake orchestrator holds the
/// run for admin attention instead of reading an empty classification or a fake plan. Returning either would send a
/// document down the wrong harness or out for approval with no review behind it.
/// </remarks>
public sealed class NullAgreementAi : IAgreementAi
{
    /// <summary>The error code a caller maps to a 503 or a held run.</summary>
    public const string ErrorCode = "ai.agreement.disabled";

    private const string DetailMessage =
        "Agreement AI (document classification and NDA edit plans) requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullAgreementAi> _logger;

    public NullAgreementAi(ILogger<NullAgreementAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<AgreementClassifyResult> ClassifyAsync(
        AgreementClassifyRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ClassifyAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<AgreementEditPlan> BuildEditPlanAsync(
        AgreementEditPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(BuildEditPlanAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullAgreementAi.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
