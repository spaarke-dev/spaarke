using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Production implementation of <see cref="ITenantBudgetPolicy"/>. Reads tenant identity from
/// <see cref="AiMeteringContext.Current"/>, resolves the per-tenant budget entry from
/// <see cref="TenantBudgetOptions"/>, and compares against the running total in
/// <see cref="ITenantTokenLedger"/>. See <see cref="ITenantBudgetPolicy"/> XML for the full
/// design rationale.
/// </summary>
/// <remarks>
/// Singleton lifetime (stateless; reads immutable options snapshot per call via
/// <see cref="IOptionsMonitor{T}"/> so app-settings changes surface without restart).
/// </remarks>
public sealed class TenantBudgetPolicy : ITenantBudgetPolicy
{
    private readonly IOptionsMonitor<TenantBudgetOptions> _options;
    private readonly ITenantTokenLedger _ledger;
    private readonly ILogger<TenantBudgetPolicy> _logger;

    public TenantBudgetPolicy(
        IOptionsMonitor<TenantBudgetOptions> options,
        ITenantTokenLedger ledger,
        ILogger<TenantBudgetPolicy> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void EnsureUnderBudget()
    {
        var opts = _options.CurrentValue;

        // Master kill: platform-wide disable of enforcement (Model 2 fleet-wide behavior).
        if (!opts.Enabled)
        {
            return;
        }

        // Tenant identity must come from the ambient AiMeteringContext scope set at entry seams.
        // If absent (defensive), cannot attribute → cannot gate → allow through. The observability
        // path handles the same case identically (dimension omitted, not sentinel — per NFR-07).
        var tenantId = AiMeteringContext.Current?.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        // Look up the per-tenant entry. Absence = Model 2 default (no gate).
        if (!opts.Tenants.TryGetValue(tenantId, out var entry) || entry is null)
        {
            return;
        }

        // Model 2 tenants are observability-only — the entry exists (for future evolution) but
        // does not gate. Only Model1Gated entries produce 429s per spec.md FR-13 §M2.
        if (entry.TenancyMode != TenantBudgetTenancyMode.Model1Gated)
        {
            return;
        }

        // A zero-or-negative cap disables the gate defensively (§11: fail-open on missing config).
        if (entry.MonthlyBudgetUsd <= 0m)
        {
            return;
        }

        // Compare current month-to-date spend to the configured cap.
        var observed = _ledger.GetMonthToDateSpendUsd(tenantId);
        if (observed >= entry.MonthlyBudgetUsd)
        {
            _logger.LogWarning(
                "Tenant {TenantIdHash} exceeded monthly AI budget: observed ${Observed:F2} vs cap ${Cap:F2}. Returning 429.",
                HashForLog(tenantId), observed, entry.MonthlyBudgetUsd);

            throw new TenantBudgetExceededException(tenantId, entry.MonthlyBudgetUsd, observed);
        }
    }

    /// <summary>
    /// Redact opaque AAD tenant GUID to a short hash for log correlation without leaking full
    /// identity into structured logs (identifiers-only discipline per NFR-07 / ADR-015).
    /// The full <c>tenant.id</c> extension is still present on the 429 ProblemDetails response
    /// for operator debug — this log-line hash is for correlation across log entries only.
    /// </summary>
    private static string HashForLog(string tenantId) =>
        tenantId.Length <= 8 ? tenantId : tenantId[..8] + "...";
}
