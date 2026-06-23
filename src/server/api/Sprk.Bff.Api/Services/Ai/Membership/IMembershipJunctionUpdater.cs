// R3 Part 1 Phase 2 — Subscription handler contract (task 084).
//
// Separates the message-handling logic (deserialized event → Dataverse
// junction-row upsert/delete) from the Service Bus consumer lifecycle
// (`MembershipJunctionUpdaterHost`). Two reasons:
//
//   1. Unit-testability — the handler is a focused Scoped service with a
//      single `HandleAsync(MembershipChangedEvent, CancellationToken)`
//      method that can be exercised directly in xUnit tests without
//      spinning up a `ServiceBusProcessor`.
//   2. Cross-cutting reuse — task 085's `MembershipReconciliationJob` will
//      reuse the same handler by synthesizing `MembershipChangedEvent`
//      payloads from source-of-truth lookup scans and invoking
//      `HandleAsync` directly. Same idempotency contract, same write
//      path; no duplicated upsert logic.
//
// The interface intentionally exposes a SINGLE method — the consumer
// handles one event at a time per `ProcessMessageAsync` callback, and
// the recon job iterates events serially.
//
// ADR-010 (DI minimalism): an interface is appropriate here because
// (a) testing requires substitution + (b) cross-cutting reuse benefits
// from a contract. The Null-Object pattern (ADR-032) is applied at the
// HOST level (the `BackgroundService` peer) — not at this handler level
// — because handler invocation does NOT participate in minimal-API
// metadata-gen (background services are not endpoint-mapped).
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.4 +
//            AC-1P2.5; docs/data-model/sprk_userentityassociation.md
//            §"Service Usage Map".

using Sprk.Bff.Api.Services.Ai.Membership.Events;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Idempotent handler for <see cref="MembershipChangedEvent"/> payloads.
/// Consumed by <see cref="MembershipJunctionUpdaterHost"/> (Service Bus
/// subscription consumer) AND directly by the membership reconciliation
/// job (task 085) for source-of-truth → junction-row drift correction.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Idempotency contract (spec FR-2P2.4)</strong>: keyed on the
/// composite tuple
/// <c>{personId, entityRecordId, sourceField}</c> (plus
/// <c>personIdType</c> + <c>entityLogicalName</c> which the
/// <c>sprk_uea_natural_key</c> alternate key includes). Duplicate
/// delivery of the same event MUST yield the same final state — at-least-
/// once Service Bus semantics make this non-negotiable.
/// </para>
/// <para>
/// <strong>Cancellation (spec NFR-07)</strong>: the host honors a 30-second
/// drain on <c>StopAsync</c>; <see cref="HandleAsync"/> implementations
/// MUST propagate <see cref="OperationCanceledException"/> promptly and
/// MUST NOT swallow the cancellation token.
/// </para>
/// </remarks>
public interface IMembershipJunctionUpdater
{
    /// <summary>
    /// Apply the membership mutation to the
    /// <c>sprk_userentityassociation</c> junction table. Mutation
    /// semantics:
    /// <list type="bullet">
    ///   <item><see cref="MembershipMutationType.Added"/> — upsert the
    ///     junction row keyed on the composite tuple.</item>
    ///   <item><see cref="MembershipMutationType.Updated"/> — re-upsert
    ///     the junction row (role + last-synced-on overwritten).</item>
    ///   <item><see cref="MembershipMutationType.Removed"/> — delete the
    ///     junction row keyed on the composite tuple. Absent row is
    ///     non-error.</item>
    /// </list>
    /// </summary>
    /// <param name="evt">Deserialized membership-change payload.</param>
    /// <param name="ct">Cancellation token; honored by the
    /// <see cref="MembershipJunctionUpdaterHost"/> 30-second drain.</param>
    Task HandleAsync(MembershipChangedEvent evt, CancellationToken ct);
}
