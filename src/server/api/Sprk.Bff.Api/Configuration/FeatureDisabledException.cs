namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Thrown by Null-Object service implementations when a feature is disabled via a kill-switch
/// flag (e.g., <c>Analysis:Enabled=false</c>, <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// Endpoints catch this exception and convert it to a 503 ProblemDetails response per ADR-018
/// (kill switches) and ADR-019 (ProblemDetails). The <see cref="ErrorCode"/> property carries
/// a stable feature-key identifier (e.g., <c>ai.briefing.disabled</c>, <c>ai.rag.disabled</c>)
/// that the client can switch on to render feature-specific UX.
/// </para>
/// <para>
/// Introduced 2026-06-01 by <c>sdap.bff.api-test-suite-repair-r2</c> task 011 Phase 1b Tier 2
/// per design <c>projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md</c> §3.
/// Use <see cref="FeatureDisabledResults.AsFeatureDisabled503"/> to produce the canonical
/// 503 ProblemDetails response from a caught instance.
/// </para>
/// </remarks>
public sealed class FeatureDisabledException : InvalidOperationException
{
    /// <summary>
    /// Stable feature-key identifier (e.g., <c>ai.briefing.disabled</c>). Included in the
    /// 503 ProblemDetails response as the <c>errorCode</c> extension. Never null.
    /// </summary>
    public string ErrorCode { get; }

    public FeatureDisabledException(string errorCode, string detail)
        : base(detail)
    {
        ErrorCode = errorCode ?? throw new ArgumentNullException(nameof(errorCode));
    }
}
