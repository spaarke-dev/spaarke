// -----------------------------------------------------------------------------
// IHandlerEnqueuer.cs
//
// L2 CONTROL-PLANE handler-dispatch abstraction (task 038).
//
// PURPOSE:
//   Single abstraction the L2 REST endpoint layer (Wave C5) + the
//   state-reconciler (Wave C5) call to dispatch a handler. The
//   Service Bus wire is one implementation (ServiceBusHandlerEnqueuer);
//   a second (in-memory) implementation may exist under Tests/ to enable
//   fast unit + integration coverage without a live SB namespace — this
//   satisfies ADR-010's "genuine seam requires ≥2 implementations from
//   day 1" rule.
//
// CONSUMED BY (planned, Wave C5):
//   - Endpoints/RunEndpoints.cs POST /api/runs (initial handler dispatch).
//   - Services/StateReconciler.cs (DAG-advancement + retry dispatch).
//
// NOT CONSUMED BY:
//   - Handler bodies themselves — the handlers run in BFF's IJobHandler
//     infrastructure (per spec.md FR-22) and do NOT re-enqueue via L2.
//     Any handler-to-handler chaining goes through the reconciler.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Enqueue;

/// <summary>
/// Dispatches a provisioning handler by enqueueing a
/// <see cref="HandlerEnvelope"/> onto the fleet-scoped queue the BFF's
/// <c>ServiceBusJobProcessor</c> drains.
/// </summary>
/// <remarks>
/// Implementations MUST be idempotent at the wire level via Service Bus
/// duplicate detection (level-1 idempotency per spec.md FR-22). Two calls
/// with an identical envelope MUST produce an identical Service Bus
/// <c>MessageId</c>; within the SB dedup window (queue-configured), only
/// ONE message is retained.
/// </remarks>
public interface IHandlerEnqueuer
{
    /// <summary>
    /// Enqueues the envelope for dispatch by BFF's IJobHandler infrastructure.
    /// </summary>
    /// <param name="envelope">The dispatch envelope. All fields required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that completes when the send has been acknowledged by the
    /// Service Bus broker. Does NOT wait for the handler to run.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="envelope"/> is null.</exception>
    /// <exception cref="Azure.Messaging.ServiceBus.ServiceBusException">
    /// Thrown when the broker rejects the send (transient errors are surfaced
    /// unchanged; callers own retry policy).
    /// </exception>
    Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken);
}
