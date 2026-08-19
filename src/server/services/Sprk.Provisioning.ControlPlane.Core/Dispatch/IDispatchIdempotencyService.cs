// -----------------------------------------------------------------------------
// IDispatchIdempotencyService.cs
//
// L2 CONTROL-PLANE Level-2 idempotency seam (task 102 seam; concrete Redis
// impl is task 105, Phase C'' Wave G-1).
//
// PURPOSE:
//   The dispatcher's dequeue-path idempotency guard (Level 2 of the 3-level
//   stack per spec.md FR-22 / DS-2 §4):
//     Level 1 -- Service Bus MessageId dedup (queue-configured; enqueuer's
//                deterministic MessageId per (HandlerId, RunId, CustomerId,
//                paramHash, attempt)).
//     Level 2 -- THIS interface. Per-message processed-marker + in-flight
//                lock in a shared cache (Redis). Prevents two dispatcher
//                instances from both invoking the same handler on the same
//                envelope in the narrow window where SB's own dedup window
//                has expired and the handler's L3 (Cosmos-alt-key /
//                CompletedPhases-scan) has not yet run.
//     Level 3 -- Handler body durable dedup (per-handler
//                <c>ProvisioningRun.CompletedPhases</c> scan +
//                <c>(Phase, IdempotencyKey)</c> hit -> return
//                <see cref="Handlers.HandlerResult.Success"/> no-op).
//
// FAIL-OPEN POSTURE (mirror of BFF <c>IdempotencyService</c>):
//   Every method on the concrete Redis impl (task 105) MUST fail open on
//   cache outage: IsProcessed=false, TryAcquireLock=true, MarkProcessed=noop,
//   ReleaseLock=noop. L1 + L3 backstop provisioning must not hard-depend on
//   cache availability -- matches BFF <c>IdempotencyService.cs:43,96</c>
//   fail-open convention.
//
// PLACEHOLDER PENDING TASK 105:
//   Wave G-1 ships <see cref="NoOpDispatchIdempotencyService"/> (permissive
//   default -- IsProcessed=false, TryAcquireLock=true always) as the DI-
//   registered implementation. Task 105 replaces the registration with the
//   real Redis-backed <c>DispatchIdempotencyService</c> that consumes
//   <c>IDistributedCache</c> transitively from the
//   <c>Microsoft.Extensions.Caching.StackExchangeRedis</c> package this
//   task adds to <c>Sprk.Provisioning.ControlPlane.Core.csproj</c>. The
//   dispatcher's dispatch flow is UNCHANGED across that swap -- only the
//   registration line in <see cref="DispatchModule.AddDispatchModule"/>
//   moves from <see cref="NoOpDispatchIdempotencyService"/> to the Redis
//   impl.
//
//   Under the no-op default, Level-2 is effectively OFF; L1 + L3 backstop
//   idempotency. This is a deliberate degraded state that matches the
//   Wave-C4 handler shipping posture ("Level 2 gate is currently a no-op
//   for this project's Wave-C4 handler set" -- see
//   IProvisioningHandler.cs:38-40 preamble). Task 105 restores the full
//   3-level guarantee before Phase F E2E acceptance.
//
// DESIGN REF:
//   - design-study-ds2-dispatcher-design.md §4 (three-level idempotency
//     table) + §4-L2 (Level-2 semantics + key/TTL contract this interface
//     encodes). Concrete Redis key convention (task 105):
//       Processed key: "provisioning:idempotency:processed:{messageId}"  TTL 24h
//       Lock key:      "provisioning:idempotency:lock:{messageId}"       TTL = MaxHandlerDuration
//     {messageId} = ServiceBusHandlerEnqueuer.ComputeMessageId(envelope)
//     (recomputed on the receive side so the gate works identically even
//     when L1 dedup is off during Wave-C4 pre-queue-recreate).
//   - CLAUDE.md §11 justification for adding this seam:
//     Existing -- L2 currently has NO cache abstraction; BFF's
//     IdempotencyService is BFF-scoped (project MUST rule -- L2 cannot
//     reference BFF assemblies).
//     Extension -- no L2 interface currently owns a per-message dedup
//     gate; this is a wholly new concern.
//     Cost-of-doing-nothing -- without a Level-2 gate, POML acceptance
//     criterion 3's "processed-marker hit returns Complete-without-invoke"
//     + "lock-held returns Abandon" behaviors have no compile target;
//     DispatchCoreAsync cannot satisfy DS-2 §2.5 step 3.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// Level-2 dispatcher-side idempotency guard. Sits in the dequeue path so a
/// duplicate SB delivery whose L1 window expired never fires the handler a
/// second time; the concurrent-instance in-flight lock prevents two
/// dispatchers from both invoking the same handler for the same envelope
/// while it runs. Fail-open on cache outage (L1 + L3 backstop) per the BFF
/// <c>IdempotencyService</c> convention.
/// </summary>
public interface IDispatchIdempotencyService
{
    /// <summary>
    /// Returns true iff a previous dispatch of the same <paramref name="messageId"/>
    /// completed its handler invocation and marked itself processed via
    /// <see cref="MarkProcessedAsync"/> within the marker TTL window (24h in
    /// the Redis impl). Callers treat true as "duplicate; complete the
    /// message without invoking the handler again" per DS-2 §2.5 step 3.
    /// </summary>
    /// <param name="messageId">
    /// Deterministic Service Bus MessageId --
    /// <c>ServiceBusHandlerEnqueuer.ComputeMessageId(envelope)</c>. The
    /// dispatcher RECOMPUTES this on the receive side rather than reading
    /// <c>args.Message.MessageId</c> so the gate remains correct even when
    /// L1 dedup is off (Wave-C4 pre-queue-recreate posture).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True on marker hit; false otherwise (including cache-outage fail-open).</returns>
    Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to acquire an in-flight lock for <paramref name="messageId"/>
    /// for at most <paramref name="ttl"/> wall-clock. Returns true when the
    /// lock is now held by the caller; false when a peer holds it. Callers
    /// treat false as "another instance mid-flight; Abandon and let SB
    /// redeliver" per DS-2 §2.5 step 3.
    /// </summary>
    /// <param name="messageId">Deterministic MessageId (as for <see cref="IsProcessedAsync"/>).</param>
    /// <param name="ttl">
    /// Lock lifetime -- <see cref="DispatcherOptions.MaxHandlerDuration"/>
    /// (default 65 min per DS-2 §4-L2). Sized to survive the H6/H9 poles;
    /// there is no mid-flight renewal on <c>IDistributedCache</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when lock acquired; false when peer holds it OR on cache-outage fail-open.</returns>
    /// <remarks>
    /// Fail-open on cache outage: the Redis impl returns TRUE (lock
    /// granted) so provisioning does NOT hard-depend on cache availability.
    /// L1 + L3 backstop mean the duplicate window is bounded even when L2
    /// is effectively off.
    /// </remarks>
    Task<bool> TryAcquireLockAsync(string messageId, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>
    /// Records that <paramref name="messageId"/> has been successfully
    /// processed (called by the dispatcher after
    /// <see cref="Reconciler.IHandlerOutcomeApplier.ApplyHandlerOutcomeAsync"/>
    /// returns AND the message is about to be Completed). Subsequent
    /// <see cref="IsProcessedAsync"/> calls within the marker TTL return
    /// true. Swallow-on-outage per fail-open convention.
    /// </summary>
    /// <param name="messageId">Deterministic MessageId (as for <see cref="IsProcessedAsync"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases the in-flight lock acquired via <see cref="TryAcquireLockAsync"/>.
    /// Idempotent -- absent-key is not an error. The lock self-expires at
    /// its TTL, so a swallowed error here does not deadlock the message.
    /// </summary>
    /// <param name="messageId">Deterministic MessageId (as for <see cref="IsProcessedAsync"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReleaseLockAsync(string messageId, CancellationToken cancellationToken);
}
