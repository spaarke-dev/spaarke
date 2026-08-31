namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// Month-to-date USD spend ledger per tenant. Consumed by <see cref="TenantBudgetPolicy"/> to
/// answer the pre-call question "would this tenant's next AI call exceed its monthly budget?".
/// </summary>
/// <remarks>
/// <para>
/// The r1 MVP implementation (<see cref="InMemoryTenantTokenLedger"/>) is a per-process
/// <c>ConcurrentDictionary&lt;(tenantId, yyyy-MM), decimal&gt;</c> — sufficient for a single-slot
/// BFF, resets on cold-start. This is intentionally simple: the authoritative source of truth
/// is still Application Insights (via the existing <c>ai.metering.tokens</c> counter shipped by
/// task 054); the ledger is a low-latency PRE-call best-effort estimate. If the process restarts,
/// the ledger starts at zero and the operator's KQL-backed dashboard remains authoritative.
/// </para>
/// <para>
/// Future evolution (deferred, not in r1 scope): back with Redis (per ADR-009) for cross-slot
/// accuracy, OR query App Insights at ledger-warmup for a boot-time backfill. Neither is required
/// for SC #13 acceptance — the gate exists to STOP runaway consumption, not to be
/// millisecond-accurate against the platform bill.
/// </para>
/// </remarks>
public interface ITenantTokenLedger
{
    /// <summary>
    /// Return the month-to-date estimated USD spend for the given tenant in the current billing
    /// month (UTC). Returns <c>0m</c> for tenants with no observed spend this month (including
    /// after a cold-start reset).
    /// </summary>
    /// <param name="tenantId">Opaque AAD tenant id. Case-insensitive match.</param>
    /// <returns>Estimated USD spend month-to-date. Non-negative.</returns>
    decimal GetMonthToDateSpendUsd(string tenantId);

    /// <summary>
    /// Add an observed USD cost to the tenant's month-to-date total. Called by the post-call
    /// observability path when an OpenAI call reports usage; the pre-call gate reads the running
    /// total to decide whether to allow the NEXT call.
    /// </summary>
    /// <param name="tenantId">Opaque AAD tenant id. Case-insensitive match.</param>
    /// <param name="deltaUsd">Cost of the just-completed call, in USD. Zero and negative values are ignored (defensive).</param>
    void AddSpend(string tenantId, decimal deltaUsd);
}
