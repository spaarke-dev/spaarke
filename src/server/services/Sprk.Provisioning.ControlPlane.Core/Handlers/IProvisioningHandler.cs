// -----------------------------------------------------------------------------
// IProvisioningHandler.cs
//
// L2 CONTROL-PLANE handler contract (task 041 — first handler wave C4).
//
// PURPOSE:
//   Local L2 analog of the BFF's Services/Jobs/IJobHandler shape (see
//   src/server/api/Sprk.Bff.Api/Services/Jobs/IJobHandler.cs). Defined here
//   rather than referenced across the assembly boundary because L2 is a
//   PEER service to Sprk.Bff.Api (spec.md D3/D8/D12 + CLAUDE.md §10 MUST
//   rule: "no reference to Sprk.Bff.Api assemblies from here"). Two
//   independent copies of the shape are the intentional cost of that
//   isolation — the interface is stable + trivially small.
//
// INVOCATION:
//   In wave C4, no dispatcher exists yet — handlers are exercised by unit
//   tests + (planned wave C5) an L2-owned reconciler background service.
//   The wave C5 reconciler will:
//     1. Pull a HandlerEnvelope off the fleet-scoped Service Bus queue
//        (`sprk-provisioning-jobs`) via a BackgroundService drain loop.
//     2. Resolve the matching IProvisioningHandler by HandlerId string
//        match (a keyed-services registration in a future
//        HandlersModule refactor is one option).
//     3. Invoke HandleAsync + interpret the returned HandlerResult
//        against the §4C rollback taxonomy.
//
//   Until the reconciler ships, H0 (this wave) is the only handler + its
//   downstream H0.5 enqueue is done directly by the handler as a
//   temporary bridge (see H0PreflightHandler for the code-comment
//   rationale + the wave-C5 refactor path).
//
// IDEMPOTENCY:
//   Every implementation MUST be idempotent (spec.md FR-22 + ADR-004).
//   The three-level stack per design.md §4.1 preamble:
//     Level 1: Service Bus MessageId dedup (implemented by
//              ServiceBusHandlerEnqueuer — deterministic MessageId per
//              (HandlerId, RunId, CustomerId, paramHash)).
//     Level 2: (Future) Redis IdempotencyService — L2 does not yet have
//              a Redis dependency; the level-2 gate is currently a no-op
//              for this project's Wave-C4 handler set.
//     Level 3: Handler body durable dedup — check
//              ProvisioningRun.CompletedPhases for a matching
//              (Phase, IdempotencyKey) tuple; return
//              HandlerResult.Success no-op on hit.
//
//   H0's idempotency key format is `preflight-{customerId}-{paramHash}`
//   per POML constraint + design.md §4.1 preamble on `{schemaVer}`
//   semantics (paramHash is a deterministic content hash of the run
//   parameters, NOT an attempt counter).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Enqueue;

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>
/// Contract for a single provisioning-pipeline handler executed against a
/// <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun"/> row.
/// Local L2 analog of the BFF's <c>Services/Jobs/IJobHandler</c> shape;
/// L2 cannot reference the BFF assembly (see file header for rationale).
/// </summary>
/// <remarks>
/// Implementations MUST be idempotent + MUST classify their own failures
/// per the §4C rollback taxonomy (<see cref="FailureClass"/>). Handlers
/// SHOULD NOT throw on domain failures — construct a
/// <see cref="HandlerResult.Failure"/> instead so the classification is
/// carried in the return value + observed by the reconciler.
/// </remarks>
public interface IProvisioningHandler
{
    /// <summary>
    /// Handler identifier used for dispatch routing + Cosmos
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.CurrentPhase"/>
    /// values. Examples: <c>"H0"</c>, <c>"H0.5"</c>, <c>"H2a"</c>. Match the
    /// design.md §4.1 handler-catalog column verbatim.
    /// </summary>
    string HandlerId { get; }

    /// <summary>
    /// Executes the handler against the ProvisioningRun identified by
    /// <paramref name="envelope"/>. Returns a typed
    /// <see cref="HandlerResult"/> — Success on happy path (including
    /// idempotent no-op), Failure with a §4C rollback classification on
    /// any recoverable or quarantine-required error.
    /// </summary>
    /// <param name="envelope">Dispatch envelope carrying handler id, run id, customer id, and opaque handler-specific parameters JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken);
}
