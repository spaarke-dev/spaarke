// R3 Part 1 Phase 2 — Task 081 (2026-06-22)
// IMembershipEventPublisher contract. Fire-and-forget per spec FR-2P2.6 +
// Q2 owner clarification (2026-06-20). PublishAsync MUST NOT throw —
// failures are logged as structured warnings carrying correlationId
// (NFR-08); the nightly MembershipReconciliationJob (task 085) is the
// backstop for missed events. The interface exists so the BFF can swap
// in NullMembershipEventPublisher (ADR-032 P2) when the topic is not yet
// deployed (task 071 blocked-operator state) — minimal-API param
// inference picks the registered implementation transparently.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.6,
//            Q2, NFR-08; projects/spaarke-platform-foundations-r3/notes/
//            event-source-inventory.md §5; .claude/adr/ADR-032-bff-nullobject-kill-switch.md.

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// Fire-and-forget publisher for <see cref="MembershipChangedEvent"/>
/// payloads. Sends to the Service Bus topic configured by
/// <see cref="MembershipEventPublisherOptions.TopicName"/> when the
/// publisher is enabled; otherwise the registered
/// <see cref="NullMembershipEventPublisher"/> peer (ADR-032 P2 Quiet
/// no-op) logs + returns.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fire-and-forget contract (FR-2P2.6 + Q2)</strong>: implementations
/// MUST swallow all transport / serialization exceptions and log them as
/// structured warnings. Callers MUST NOT rely on a thrown exception to
/// detect publish failure. Mutation endpoints publish AFTER the Dataverse
/// write succeeds; if the publish fails the mutation is NOT rolled back —
/// nightly recon (task 085) closes the gap.
/// </para>
/// <para>
/// Singleton lifetime per ADR-010 + matches the existing
/// <c>ServiceBusJobProcessor</c> lifetime convention.
/// </para>
/// </remarks>
public interface IMembershipEventPublisher
{
    /// <summary>
    /// Publish a single <see cref="MembershipChangedEvent"/> to the
    /// Service Bus topic. Never throws — see fire-and-forget contract in
    /// the remarks section.
    /// </summary>
    /// <param name="evt">The event payload. <c>CorrelationId</c> MUST be
    /// non-empty (NFR-08) — implementations log a Warning and short-circuit
    /// if it is.</param>
    /// <param name="ct">Cancellation token. Implementations MAY ignore it
    /// (fire-and-forget does not require honoring caller cancellation).</param>
    /// <returns>A completed task. The task NEVER faults — failure surfaces
    /// only via logs.</returns>
    Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct);
}
