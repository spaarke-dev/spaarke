namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Outcome of an idempotent ACS membership mutation (add/remove) on one thread (design §8.4). Returned by
/// <see cref="Acs.IAcsThreadService.AddParticipantsAsync"/> and
/// <see cref="Acs.IAcsThreadService.RemoveParticipantAsync"/> so the membership-reconcile job (task 041)
/// can observe how many participants were actually applied vs no-oped, and how many underlying ACS calls
/// (batches) were issued — the throttle unit for staying inside the ACS 10-mutations/10s + 30/min
/// per-thread budget.
/// </summary>
/// <remarks>
/// Server-side model only (ADR-045) — no <c>Azure.Communication.*</c> type is exposed. Mutations are
/// idempotent: adding a participant already in the thread and removing one already absent are both counted
/// as <see cref="NoOped"/> (safe no-ops), never errors.
/// </remarks>
public sealed record AcsMembershipChange
{
    /// <summary>The ACS <c>ChatThreadId</c> the mutation was applied to.</summary>
    public required string ChatThreadId { get; init; }

    /// <summary>Count of distinct participants the caller requested to add/remove.</summary>
    public required int Requested { get; init; }

    /// <summary>Count actually applied (net-new adds, or actual removals).</summary>
    public required int Applied { get; init; }

    /// <summary>
    /// Count that were safe no-ops — already-present on add, or already-absent on remove — plus any
    /// participants ACS reported as non-fatally invalid (they are retried by the next reconcile sweep).
    /// </summary>
    public required int NoOped { get; init; }

    /// <summary>
    /// Number of underlying ACS mutation calls issued (add is chunked into batches of at most
    /// <see cref="Acs.AcsThreadService.MaxParticipantsPerBatch"/> so task 041 can throttle at the batch
    /// boundary). Remove is always a single call.
    /// </summary>
    public required int BatchCount { get; init; }
}
