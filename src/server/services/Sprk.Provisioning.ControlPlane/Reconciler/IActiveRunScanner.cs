// -----------------------------------------------------------------------------
// IActiveRunScanner.cs
//
// L2 CONTROL-PLANE state-reconciler cross-partition scan seam (task 058, Wave C5).
//
// PURPOSE:
//   The state-reconciler needs to enumerate runs whose Status is Running or
//   WaitingOnGate ACROSS ALL customer partitions to decide which handlers to
//   dispatch next. <see cref="Repositories.IProvisioningRunRepository"/>
//   deliberately does NOT expose this operation — its interface shape
//   REQUIRES a customerId partition-key predicate on every call (§4D I3 /
//   FR-30 enforced by construction). A cross-partition scan is a NARROW,
//   RESERVED capability that lives on its own interface so:
//
//     1. The invariant on <see cref="Repositories.IProvisioningRunRepository"/>
//        stays intact — no caller can accidentally reach for a cross-partition
//        read via the repository.
//     2. The single implementation of this interface is annotated with
//        <see cref="Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute"/>
//        + reason citing this design rule, so the Wave-C6 tenant-isolation
//        ArchTest (task 064 / I3) recognises the waiver by name.
//     3. Task 060 (I6 crash-recovery orphan scan) can compose this seam OR
//        extend it with an "older than X" filter without reshaping the
//        repository.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2
//     (handler-execution model — reconciler polls Cosmos every 5s).
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-22.
//   - Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute
//     (introduced by task 064).
//
// NOT CONSUMED BY:
//   - Endpoint handlers (they always know the customerId).
//   - IProvisioningHandler impls (they receive customerId in the envelope).
//   - BFF (L2-only surface).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// Cross-partition scan of runs that are candidates for state-reconciler
/// advancement — those with <see cref="RunStatus.Running"/> or
/// <see cref="RunStatus.WaitingOnGate"/>. This is the ONLY interface in the
/// L2 project that intentionally exposes a cross-partition Cosmos read (see
/// <see cref="Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute"/>);
/// every other Cosmos operation MUST route through
/// <see cref="Repositories.IProvisioningRunRepository"/> which requires a
/// customerId partition-key predicate by construction (§4D I3 / FR-30).
/// </summary>
public interface IActiveRunScanner
{
    /// <summary>
    /// Returns a snapshot of runs with <see cref="RunStatus.Running"/> or
    /// <see cref="RunStatus.WaitingOnGate"/> across ALL customer partitions.
    /// Callers MUST treat the result as a point-in-time snapshot — a run may
    /// transition between the scan and the caller acting on it, which is why
    /// the reconciler's dispatch uses deterministic Service Bus MessageId
    /// (level-1 idempotency per ADR-036) as its primary duplicate-prevention.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A read-only list of matching runs. Empty when nothing is active.
    /// May be paginated internally; the returned list is materialised in
    /// full before the task completes.
    /// </returns>
    Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken cancellationToken);
}
