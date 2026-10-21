// unified-access-control-r2, GitHub #2091 round 4 — the generic re-file writers' read of "was this row isolated", BEFORE their
// write, for SecureChildReconciler.AfterRefileAsync.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureChildReconciler.IsSecureTeamOwnedAsync is the read; the playbook update node, the output
//       orchestrator's update, the field-mapping push and the chat update tool (round 5) each called it inline with the same
//       fault capture.
//   (2) Extension — SecureChildReconciler itself is the natural home, but it is held unchanged until ontology 042b (which
//       restructures AfterRefileAsync) merges; this is the writers' side of the call, one copy instead of four.
//   (3) Cost of doing nothing — one transient fault on the read before a move OUT of Secure answers "unknown", so nothing is
//       released and the Secure sharees keep Collaborate on a now-ordinary row with no job to remove it; four inline copies
//       of the retry drift.

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// #2091 round 4: whether a row is isolated (owned by the Secure Record owner team), read BEFORE a re-file writes it, as the
/// <c>wasIsolated</c> answer <see cref="SecureChildReconciler.AfterRefileAsync"/> takes.
/// </summary>
internal static class RefileIsolationRead
{
    /// <summary>
    /// Reads it, once more after a fault (a timeout, a throttled or dropped request), and answers it — or, when both reads
    /// fault, a faulted answer: the step then releases nothing. Only the caller's own cancellation propagates.
    /// </summary>
    public static async Task<Func<Task<bool>>> BeforeWriteAsync(
        SecureChildReconciler children, string table, Guid id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(children);
        Exception? fault = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var isolated = await children.IsSecureTeamOwnedAsync(table, id, ct).ConfigureAwait(false);
                return () => Task.FromResult(isolated);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                fault = ex;
            }
        }

        return () => Task.FromException<bool>(fault!);
    }
}
