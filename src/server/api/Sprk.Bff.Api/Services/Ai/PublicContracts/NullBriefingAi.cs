using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Null-Object implementation of <see cref="IBriefingAi"/> registered when the compound
/// AI kill-switch is OFF (<c>Analysis:Enabled=false</c> OR <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 L1. Throws <see cref="FeatureDisabledException"/> on every
/// public method so that consumer endpoints can convert to 503 ProblemDetails per ADR-018 + ADR-019.
/// Returning empty narrative text would silently render "no briefing available" in the UI,
/// masking the kill-switch state and misleading operators — fail-fast is the correct semantic.
/// </para>
/// <para>
/// Logger is injected (not just for telemetry on disabled-feature invocation attempts) because
/// hitting a kill-switched feature is expected behavior when test fixtures or operations set
/// <c>DocumentIntelligence:Enabled=false</c>; logged at <c>Debug</c> level only.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 2.</para>
/// </remarks>
public sealed class NullBriefingAi : IBriefingAi
{
    private const string ErrorCode = "ai.briefing.disabled";
    private const string DetailMessage =
        "AI briefing requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullBriefingAi> _logger;

    public NullBriefingAi(ILogger<NullBriefingAi> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<string> GenerateNarrativeAsync(
        string prompt,
        int? maxOutputTokens = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "NullBriefingAi.GenerateNarrativeAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}
