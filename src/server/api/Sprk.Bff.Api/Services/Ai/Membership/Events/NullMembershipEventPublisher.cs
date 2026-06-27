// R3 Part 1 Phase 2 — Task 081 (2026-06-22)
// ADR-032 P2 Quiet no-op Null-Object peer for IMembershipEventPublisher.
// Registered when MembershipEventPublisherOptions.Enabled is false —
// i.e., while task 071 is still blocked-operator and the Service Bus
// topic `sprk-membership-changes` has not been provisioned in Azure.
// Calls log an Information-level message + return Task.CompletedTask.
//
// Rationale (ADR-032 P2): publish is a side-effecting fire-and-forget
// operation; absence semantically equals "nothing happened" — consumers
// receive no payload, downstream junctions stay at their previous state,
// and the nightly recon job (task 085) is the reconciler. A P3 Fail-fast
// Null-Object would be wrong here because the caller does not consume
// the publish result; the call IS the operation.
//
// Reference: .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            projects/spaarke-platform-foundations-r3/spec.md FR-2P2.6, Q2;
//            projects/spaarke-platform-foundations-r3/notes/event-source-inventory.md §6.6.

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// ADR-032 P2 Quiet no-op Null-Object peer for
/// <see cref="IMembershipEventPublisher"/>. Logs the would-be publish at
/// Information level + returns immediately without any Service Bus
/// interaction. Registered when
/// <see cref="MembershipEventPublisherOptions.Enabled"/> is <c>false</c>
/// (default state until operator deploys the topic per task 071).
/// </summary>
public sealed class NullMembershipEventPublisher : IMembershipEventPublisher
{
    private readonly ILogger<NullMembershipEventPublisher> _logger;

    public NullMembershipEventPublisher(ILogger<NullMembershipEventPublisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// ADR-032 P2 Quiet no-op — logs at Information (not Warning) because
    /// "publisher disabled" is the intended kill-switch state, NOT an
    /// error. Operators monitor for the absence of "Published
    /// MembershipChangedEvent" log entries from
    /// <see cref="MembershipEventPublisher"/> as the signal of the
    /// disabled state.
    /// </remarks>
    public Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        _logger.LogInformation(
            "MembershipEventPublisher disabled — event suppressed. " +
            "Entity={EntityLogicalName} Record={EntityRecordId} Person={PersonId} " +
            "Field={SourceField} Mutation={MutationType} CorrelationId={CorrelationId}. " +
            "Nightly recon will reconcile.",
            evt.EntityLogicalName, evt.EntityRecordId, evt.PersonId,
            evt.SourceField, evt.MutationType, evt.CorrelationId);

        return Task.CompletedTask;
    }
}
