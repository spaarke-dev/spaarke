namespace Sprk.Bff.Api.Services.Todo;

/// <summary>
/// Reconciles a user's full <c>sprk_todo</c> set with their Graph Spaarke list — used to recover
/// from missed webhooks, dropped subscriptions, or initial onboarding when a user first opts in
/// to the Graph sync feature.
/// </summary>
/// <remarks>
/// <para>Feature-gated by <c>Spaarke:Graph:TodoSync:Enabled</c>. When false, the Null-Object
/// implementation is a no-op but logs once per invocation per ADR-032 P2 (observability matters
/// for backfill: an operator manually triggering a backfill against a flag-off BFF should see
/// a log entry confirming the no-op rather than silent success).</para>
/// <para>Real implementation arrives in Phase 7 (task 065). It will run a two-way diff
/// (Dataverse ↔ Graph) and emit per-todo sync jobs.</para>
/// </remarks>
public interface ITodoSyncBackfiller
{
    /// <summary>
    /// Reconciles all <c>sprk_todo</c> records owned by <paramref name="userId"/> with their
    /// Graph mirrors.
    /// </summary>
    /// <param name="userId">Entra ID object id of the user.</param>
    /// <param name="ct">Cancellation token.</param>
    Task BackfillAsync(Guid userId, CancellationToken ct);
}
