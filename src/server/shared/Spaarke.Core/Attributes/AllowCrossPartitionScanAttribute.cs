namespace Spaarke.Core.Attributes;

/// <summary>
/// Waiver marker for the §4D I3 partition-key ArchTest
/// (<c>I3_CosmosPartitionKeyTests</c> in <c>tests/Spaarke.ArchTests/TenantIsolation/</c>).
/// Marks a Cosmos DB call site (or the enclosing type/method) as an
/// intentionally cross-partition operation. Legitimate use cases are narrow:
/// <list type="bullet">
///   <item>Reconciler startup scans that must enumerate every partition to find
///     orphaned runs (customer-provisioning-orchestration-r1 spec §4.2 crash
///     recovery / task 060).</item>
///   <item>Fleet-wide analytics / audit sweeps that legitimately span partitions.</item>
///   <item>Migration / backfill jobs that must touch every partition once.</item>
/// </list>
///
/// <para>
/// <b>Not for use as a general escape hatch.</b> The I3 invariant exists to
/// prevent cross-tenant Cosmos data bleed (HIGH severity per §4D — a
/// cross-partition query on a tenant-scoped container returns AI conversation
/// history / prompts / audit rows from other customers). Every use of this
/// attribute is a reviewed exception with a documented rationale.
/// </para>
///
/// <para>
/// <b>Reviewer contract</b>: adding this attribute to a call site requires a
/// non-empty <see cref="Reason"/> citing the design section, spec FR, or task
/// ID that justifies the waiver. The PR that introduces the attribute usage
/// must call out the addition explicitly (reviewer sign-off), and — where the
/// waiver is inside the BFF — must also cite the CLAUDE.md §11 (Component
/// Justification) cost-of-doing-nothing that would result from adding a
/// per-tenant scoping instead.
/// </para>
///
/// <para>
/// <b>Attribute-target semantics</b>: attached to a METHOD, waives every
/// Cosmos SDK call inside that method body. Attached to a CLASS, waives every
/// Cosmos SDK call inside the class. Prefer method-level attachment — it
/// keeps the waiver's blast radius tight to the specific operation that needs
/// it.
/// </para>
///
/// <para>
/// The ArchTest matches this attribute BY NAME (regex over the source syntax
/// <c>[AllowCrossPartitionScan]</c> or <c>[AllowCrossPartitionScan("...")]</c>)
/// — it does not resolve the attribute type. This is deliberate: it lets each
/// consumer project (BFF, L2, future services) reference this canonical
/// definition when convenient, but does not force a project reference on any
/// consumer that would prefer to declare a same-named local attribute
/// instead.
/// </para>
/// </summary>
/// <remarks>
/// Introduced by <c>customer-provisioning-orchestration-r1</c> Wave 4B task
/// 064 alongside the 5 tenant-isolation ArchTests (I1–I5). Placed in
/// <c>Spaarke.Core</c> because both the BFF and (as of task 060) L2's
/// reconciler will consume it; <c>Spaarke.Core</c> is the least-blast-radius
/// shared home (BFF already references it; L2 picks up the reference when it
/// needs the attribute).
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AllowCrossPartitionScanAttribute : Attribute
{
    /// <summary>
    /// Concise justification for the cross-partition waiver — MUST cite a
    /// design section, spec FR, task ID, or ADR that documents WHY this call
    /// site cannot be per-tenant scoped. Reviewer checks this on every PR
    /// that adds the attribute.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Constructs the waiver. <paramref name="reason"/> is required and must
    /// be non-empty; a blank reason defeats the reviewer-visibility purpose
    /// of the attribute.
    /// </summary>
    /// <param name="reason">
    /// Concise justification (see <see cref="Reason"/>). Example:
    /// <c>"design §4.2 crash recovery / task 060 orphan scan"</c>.
    /// </param>
    public AllowCrossPartitionScanAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "AllowCrossPartitionScan requires a non-empty reason citing the design section, " +
                "spec FR, task ID, or ADR that justifies the waiver.",
                nameof(reason));
        }
        Reason = reason;
    }
}
