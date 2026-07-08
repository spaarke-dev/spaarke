using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IPlaybookLookupService"/> registered when the
/// compound AI kill-switch is OFF (<c>Analysis:Enabled=false</c> or
/// <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032 for the query path: silently returning a fabricated
/// playbook (or a not-found error) would mislead consumers about the kill-switch state.
/// <see cref="GetByIdAsync"/> throws <see cref="FeatureDisabledException"/>; endpoint catch
/// sites convert to 503 ProblemDetails via <c>FeatureDisabledResults.AsFeatureDisabled503</c>
/// (ADR-018 + ADR-019).
/// </para>
/// <para>
/// The cache-clear methods are quiet no-ops (P2 semantics per ADR-032): with the feature
/// off nothing is cached, so "nothing happened" is the truthful outcome and admin cache-flush
/// flows need not fail.
/// </para>
/// <para>
/// Introduced 2026-07-05 by ai-architecture-redesign-r1 task 006 (FR-P0-05) — the real
/// registration moved from FinanceModule to AnalysisServicesModule.AddPlaybookServices.
/// </para>
/// </remarks>
public sealed class NullPlaybookLookupService : IPlaybookLookupService
{
    /// <summary>Stable errorCode carried in the 503 ProblemDetails — clients switch on this string.</summary>
    public const string ErrorCode = "ai.playbook.lookup.disabled";

    private const string DetailMessage =
        "Playbook lookup requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullPlaybookLookupService> _logger;

    public NullPlaybookLookupService(ILogger<NullPlaybookLookupService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PlaybookResponse> GetByIdAsync(string playbookId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "NullPlaybookLookupService.GetByIdAsync invoked while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public void ClearCache(string playbookId)
    {
        // Quiet no-op: nothing is cached when the feature is off.
        _logger.LogDebug(
            "NullPlaybookLookupService.ClearCache no-op while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
    }

    public void ClearAllCache()
    {
        // Quiet no-op: nothing is cached when the feature is off.
        _logger.LogDebug(
            "NullPlaybookLookupService.ClearAllCache no-op while AI feature is disabled (errorCode={ErrorCode}).",
            ErrorCode);
    }
}
