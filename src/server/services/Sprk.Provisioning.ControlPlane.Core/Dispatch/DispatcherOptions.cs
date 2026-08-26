// -----------------------------------------------------------------------------
// DispatcherOptions.cs
//
// L2 CONTROL-PLANE dispatcher configuration (task 102, Phase C'' Wave G-1).
//
// PURPOSE:
//   Bound options for ProvisioningHandlerDispatcher — the session-serialized
//   Service Bus consumer that drains sprk-provisioning-jobs and invokes
//   handlers by HandlerId. Loaded from the "Dispatcher" configuration section.
//
// DESIGN REF:
//   - design-study-ds2-dispatcher-design.md §2.4 (this options table verbatim).
//   - design-study-ds2b-concurrency-safety-deep-dive.md §9 R3 (freeze rule:
//     MaxConcurrentCallsPerSession is a HARD-CODED correctness invariant --
//     NOT surfaced here as tuning; it lives as a `const int` in the dispatcher
//     itself and is protected by ProvisioningHandlerDispatcherInvariantTests).
//   - spec.md FR-22 (handler execution model + 3-level idempotency).
//   - CLAUDE.md §11 justification: Existing options types (ReconcilerOptions,
//     CrashRecoveryOptions, ServiceBusModuleOptions) do not carry these
//     dispatcher-specific knobs; extending any of them would conflate
//     bounded contexts. Cost-of-doing-nothing: without an Enabled kill
//     switch, feature-gating the dispatcher for a rollback would require a
//     code deploy; without MaxConcurrentCustomers, we cannot bound fleet
//     concurrency per-instance under scale-out.
//
// KILL-SWITCH POSTURE (ADR-032 parity with ReconcilerOptions):
//   Enabled=false makes ExecuteAsync exit cleanly BEFORE creating the
//   ServiceBusSessionProcessor. This is the Null-Object kill-switch
//   equivalent for a HostedService — the service instance still exists in
//   DI (unconditional registration) but performs no work. Matches
//   StateReconcilerService's ReconcilerOptions.Enabled posture verbatim.
//
// HARD PROHIBITION:
//   MaxConcurrentCallsPerSession IS DELIBERATELY ABSENT from this options
//   surface. Per DS-2b Sec.9 R3 forcing function, it is a correctness
//   invariant (single-writer-per-customer guarantee against the 39
//   ETag-guarded Cosmos write sites), not a tuning parameter. Adding it
//   here would put the DAG's Cosmos-write safety property behind a config
//   knob an operator could set to 2+ and silently reintroduce the
//   systematic branch-join ETag-conflict class DS-2 was built to
//   eliminate. ProvisioningHandlerDispatcherInvariantTests protects the
//   hard-coded value from configuration drift.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// Bound options for <see cref="ProvisioningHandlerDispatcher"/>. Loaded from
/// the <c>Dispatcher</c> configuration section via
/// <see cref="DispatchModule.AddDispatchModule"/>.
/// </summary>
public sealed class DispatcherOptions
{
    /// <summary>Configuration section that carries these options.</summary>
    public const string SectionName = "Dispatcher";

    /// <summary>
    /// Kill switch (parity with <c>ReconcilerOptions.Enabled</c>). When false,
    /// <see cref="ProvisioningHandlerDispatcher.ExecuteAsync"/> exits cleanly
    /// without creating the session processor. Registration itself is
    /// UNCONDITIONAL per ADR-032 — this bool is the runtime kill switch.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Bound to <c>ServiceBusSessionProcessorOptions.MaxConcurrentSessions</c>
    /// — the per-instance cap on distinct customer sessions the processor
    /// will drain in parallel. Defaults to 4. Bump this to widen fleet
    /// concurrency per L2 App Service instance; horizontal scale-out
    /// multiplies this by instance count.
    /// </summary>
    /// <remarks>
    /// Gated runs (24h SPE replication, admin consent) hold NO session slot
    /// — see DS-2b §1.4. "Concurrent customers" means concurrently-EXECUTING
    /// customers, not concurrently-provisioning.
    /// </remarks>
    public int MaxConcurrentCustomers { get; set; } = 4;

    /// <summary>
    /// Bound to <c>ServiceBusSessionProcessorOptions.MaxAutoLockRenewalDuration</c>.
    /// Sized for the H6 solution-import pole (30–60 min per DS-2 §1.2) and
    /// the H9 BFF-deploy pole (15–30 min). Defaults to 65 minutes so a
    /// single handler at the upper bound cannot exhaust its lock during
    /// execution. Also serves as the Level-2 Redis lock TTL
    /// (<see cref="IDispatchIdempotencyService.TryAcquireLockAsync"/>) so
    /// the two guards expire at the same wall-clock horizon.
    /// </summary>
    public TimeSpan MaxHandlerDuration { get; set; } = TimeSpan.FromMinutes(65);

    /// <summary>
    /// Bound to <c>ServiceBusSessionProcessorOptions.SessionIdleTimeout</c>.
    /// Releases a customer's session lock once no messages arrive within this
    /// window so a waiting customer's session can be picked up promptly by
    /// the same processor. Between-handler reconciler-tick gaps are ~5s, so
    /// 30s is generous but tight enough for prompt fairness. Defaults to
    /// 30 seconds.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Threshold at which the dispatcher stops abandoning transient failures
    /// and dead-letters the message with reason <c>OutcomeApplyFailed</c> or
    /// <c>MaxRetriesExceeded</c>. Mirrors the BFF <c>ServiceBusJobProcessor</c>'s
    /// <c>DeliveryCount &gt;= 5</c> threshold. Defaults to 5.
    /// </summary>
    /// <remarks>
    /// This is the LAST-RESORT dead-letter — infrastructure faults land here.
    /// Handler DOMAIN failures NEVER reach this counter; they land as §4C
    /// Cosmos transitions via <see cref="Reconciler.IHandlerOutcomeApplier"/>
    /// (see DS-2 §2.8 dead-letter policy table).
    /// </remarks>
    public int MaxDeliveryCount { get; set; } = 5;

    /// <summary>
    /// Fail-fast validation invoked via <see cref="Microsoft.Extensions.Options.OptionsBuilder{TOptions}.Validate(System.Func{TOptions, bool})"/>
    /// / <c>PostConfigure</c> at startup. Throws <see cref="InvalidOperationException"/>
    /// on any invalid combination — NFR-05 parity with
    /// <c>ReconcilerOptions.Validate()</c> / <c>CrashRecoveryOptions.Validate()</c>.
    /// </summary>
    public void Validate()
    {
        if (MaxConcurrentCustomers < 1)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxConcurrentCustomers)} must be >= 1 (was {MaxConcurrentCustomers}). " +
                "This value binds ServiceBusSessionProcessorOptions.MaxConcurrentSessions per-instance.");
        }

        if (MaxHandlerDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxHandlerDuration)} must be > 0 (was {MaxHandlerDuration}). " +
                "This value binds ServiceBusSessionProcessorOptions.MaxAutoLockRenewalDuration AND " +
                "the Level-2 Redis idempotency lock TTL — both must be non-zero for the " +
                "dispatcher to survive its longest handler (H6/H9 poles at 30–60 min).");
        }

        if (SessionIdleTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SessionIdleTimeout)} must be > 0 (was {SessionIdleTimeout}). " +
                "This value binds ServiceBusSessionProcessorOptions.SessionIdleTimeout.");
        }

        if (MaxDeliveryCount < 1)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxDeliveryCount)} must be >= 1 (was {MaxDeliveryCount}). " +
                "Dead-letter threshold for infrastructure-fault redeliveries.");
        }
    }
}
