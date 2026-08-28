// -----------------------------------------------------------------------------
// DispatchDecision.cs
//
// L2 CONTROL-PLANE dispatch-decision record (task 102, Phase C'' Wave G-1).
//
// PURPOSE:
//   Discriminated result of ProvisioningHandlerDispatcher.DispatchCoreAsync
//   -- the pure decision function per DS-2 §2.5's 8-step flow. Separating
//   "what to do with the message" (this record) from "how to do it" (the
//   ServiceBusSessionMessageEventArgs.Complete/DeadLetter/Abandon wire
//   calls) is what makes DispatchCoreAsync unit-testable without a live
//   Service Bus wire per POML step 4:
//       Task<DispatchDecision> DispatchCoreAsync(
//           HandlerEnvelope envelope,
//           int deliveryCount,
//           IServiceProvider scopedProvider,
//           CancellationToken ct);
//
//   The event-side wrapper (OnSessionMessageAsync) translates the returned
//   decision into the corresponding wire call:
//     Complete           -> args.CompleteMessageAsync
//     DeadLetter(r, d)   -> args.DeadLetterMessageAsync(reason: r, description: d)
//     Abandon            -> args.AbandonMessageAsync(propertiesToModify: null)
//
// DESIGN REF:
//   - design-study-ds2-dispatcher-design.md §2.2 (class shape) + §2.5
//     (8-step flow) + §2.8 (dead-letter policy table -- the 5 reasons
//     enumerated in DeadLetterReasons below).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// Discriminated outcome of <see cref="ProvisioningHandlerDispatcher.DispatchCoreAsync"/>.
/// Exhaustive: <see cref="Complete"/> | <see cref="DeadLetter"/> | <see cref="Abandon"/>.
/// The event wrapper (<c>OnSessionMessageAsync</c>) translates into the
/// corresponding <c>ProcessSessionMessageEventArgs</c> wire call.
/// </summary>
public abstract record DispatchDecision
{
    private DispatchDecision() { }

    /// <summary>
    /// Message handled successfully OR domain-failed with the §4C transition
    /// APPLIED to Cosmos via <see cref="Reconciler.IHandlerOutcomeApplier"/>.
    /// The event wrapper calls <c>CompleteMessageAsync</c>. Handler DOMAIN
    /// failures reach this path (they are NOT dead-lettered -- retry authority
    /// belongs to §4C RollbackTransitions.ShouldReEnqueue, never SB Abandon).
    /// </summary>
    public sealed record Complete : DispatchDecision;

    /// <summary>
    /// Message is un-processable at this dispatcher instance (transient
    /// infrastructure fault -- Cosmos unreachable during step-6 outcome apply,
    /// Redis lock held by a peer dispatcher for step-3). The event wrapper
    /// calls <c>AbandonMessageAsync(propertiesToModify: null)</c>; Service Bus
    /// redelivers per broker backoff; <see cref="DispatcherOptions.MaxDeliveryCount"/>
    /// backstops the loop -- once <c>DeliveryCount &gt;= MaxDeliveryCount</c>
    /// the next visit dead-letters as <c>OutcomeApplyFailed</c>.
    /// </summary>
    public sealed record Abandon : DispatchDecision;

    /// <summary>
    /// Message is structurally un-processable at ANY dispatcher instance
    /// (invalid body, unknown handler id, orphan run, etc.). The event
    /// wrapper calls <c>DeadLetterMessageAsync(reason, description)</c> --
    /// <paramref name="Reason"/> is a stable operator-parseable code from
    /// <see cref="DeadLetterReasons"/>; <paramref name="Description"/> is
    /// the human-readable diagnostic for the operator DLQ scan.
    /// </summary>
    /// <param name="Reason">Stable dead-letter reason code -- one of <see cref="DeadLetterReasons"/>.</param>
    /// <param name="Description">Operator-facing diagnostic (safe to log; MUST NOT contain cleartext secrets).</param>
    public sealed record DeadLetter(string Reason, string Description) : DispatchDecision;
}

/// <summary>
/// Canonical dead-letter reason constants per design-study-ds2-dispatcher-design.md
/// §2.8. These are the ONLY five reasons the dispatcher will DLQ a message.
/// Handler DOMAIN failures NEVER reach this list -- they land as §4C Cosmos
/// transitions via <see cref="Reconciler.IHandlerOutcomeApplier"/> with full
/// operator visibility.
/// </summary>
public static class DeadLetterReasons
{
    /// <summary>Message body did not deserialize into <see cref="Enqueue.HandlerEnvelope"/>. Operator: fix + re-send.</summary>
    public const string InvalidFormat = "InvalidFormat";

    /// <summary>
    /// No keyed <see cref="Handlers.IProvisioningHandler"/> registration for the envelope's
    /// <see cref="Enqueue.HandlerEnvelope.HandlerId"/>. Task 103's
    /// HandlerRegistrationCompletenessTests makes this near-unreachable at
    /// build time. Operator: deploy fix.
    /// </summary>
    public const string NoHandler = "NoHandler";

    /// <summary>
    /// Handler's DI dependency graph threw during construction (mirror of BFF
    /// <c>ServiceBusJobProcessor</c>'s <c>HandlerResolutionFailed</c>).
    /// Operator: fix broken registration; the exception chain is logged on the
    /// dispatcher error path.
    /// </summary>
    public const string HandlerResolutionFailed = "HandlerResolutionFailed";

    /// <summary>
    /// <c>runs/{RunId}</c> Cosmos document is absent (deleted between enqueue
    /// and dispatch, or the enqueuer skipped its Cosmos-create step). Handlers
    /// MUST NOT mutate anything if the run doc is missing (see
    /// <see cref="Handlers.IProvisioningHandler"/> contract). Lost-message
    /// class -- no operator recovery.
    /// </summary>
    public const string OrphanRun = "OrphanRun";

    /// <summary>
    /// <see cref="Reconciler.IHandlerOutcomeApplier.ApplyHandlerOutcomeAsync"/>
    /// failed <see cref="DispatcherOptions.MaxDeliveryCount"/> times in a row
    /// (transient Cosmos unavailability at step 6). Operator +
    /// <c>CrashRecoveryStartupService</c> recover the run on next boot per
    /// design.md §4.1 I6 crash recovery.
    /// </summary>
    public const string OutcomeApplyFailed = "OutcomeApplyFailed";
}
