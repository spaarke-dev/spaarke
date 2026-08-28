using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Thrown by <see cref="ITenantBudgetPolicy"/> when a Model 1 (shared trial/SMB) tenant exceeds
/// its configured monthly USD budget for AI token spend (spec.md FR-13 §M1 + SC #13). Endpoints
/// convert to 429 ProblemDetails via <see cref="TenantBudgetResults.AsTenantBudgetExceeded429"/>.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="FeatureDisabledException"/> (503 for kill-switch-disabled features):
/// budget-exceeded is 429 (client can retry after the monthly reset OR after the operator raises
/// the budget) — semantically closer to rate limiting than feature-disabled.
/// </para>
/// <para>
/// Model 2 tenants (dedicated stamp) never see this exception; enforcement is Model-1-only per
/// spec.md FR-13 §M1/M2. The exception is intentionally derived from <see cref="InvalidOperationException"/>
/// mirroring <see cref="FeatureDisabledException"/> so existing endpoint catch chains treat it
/// as a soft/expected failure rather than falling through to 500 (per ADR-019).
/// </para>
/// </remarks>
public sealed class TenantBudgetExceededException : InvalidOperationException
{
    /// <summary>Stable error code — <c>ai.tenant.budget_exceeded</c>. Included as the 429 <c>errorCode</c> extension.</summary>
    public const string StableErrorCode = "ai.tenant.budget_exceeded";

    /// <summary>
    /// Opaque AAD tenant id whose budget was exceeded. Included as the 429 <c>tenant.id</c>
    /// extension for operator diagnostics (identifier only — no content — per NFR-07 / ADR-015).
    /// </summary>
    public string TenantId { get; }

    /// <summary>Monthly USD budget cap that was exceeded.</summary>
    public decimal MonthlyBudgetUsd { get; }

    /// <summary>Month-to-date USD spend at time of check.</summary>
    public decimal ObservedSpendUsd { get; }

    public TenantBudgetExceededException(string tenantId, decimal monthlyBudgetUsd, decimal observedSpendUsd)
        : base(BuildMessage(tenantId, monthlyBudgetUsd, observedSpendUsd))
    {
        TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        MonthlyBudgetUsd = monthlyBudgetUsd;
        ObservedSpendUsd = observedSpendUsd;
    }

    private static string BuildMessage(string tenantId, decimal monthlyBudgetUsd, decimal observedSpendUsd) =>
        $"Tenant '{tenantId}' has exceeded its monthly AI token budget " +
        $"(observed: ${observedSpendUsd:F2}, cap: ${monthlyBudgetUsd:F2}). " +
        "Contact your administrator to raise the budget or wait until the next monthly reset. " +
        "See spec.md FR-13 §M1 + SC #13.";
}
