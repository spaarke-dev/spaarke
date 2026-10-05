namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// The ONE app-only step the user-OBO AI update tool (<c>dataverse.update_record</c>,
/// <c>DataverseUpdateRecordHandler</c>) may take: after the caller's own update succeeded, re-stamp the core-ancestor
/// copies that update moved (unified-access-control-r2 task 156; owner decisions round 8 item 1, CLAUDE.md §6.5 path B).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The tool's spec rule is "User-OBO ONLY": the caller's update runs under the caller's own
/// token so Dataverse authorizes it. The core-ancestor stamp is a SERVER-owned invariant written app-only (ADR-002 WP-1),
/// and AC1 of task 156 requires every BFF re-file path to re-stamp the children IN THE SAME OPERATION. The owner amended
/// the rule for this one helper only (round 8 item 1, the same reasoning as round 7 item 3 for the two AI create tools):
/// the update stays the user's; the re-stamp runs inline, here.</para>
/// <para><b>Narrow by construction.</b> This type's only member is <see cref="AfterWriteAsync"/>. It holds the restamper
/// and nothing else: no <see cref="IServiceProvider"/> (so nothing can be resolved through it), no Dataverse client
/// exposed, no entry point onto an arbitrary child or intermediate (<see cref="CoreAncestorRestamper.RestampChildAsync"/>
/// and <see cref="CoreAncestorRestamper.RestampChildrenOfAsync"/> stay out of the tool's reach). It writes ONLY the stamp
/// columns, and only values derived from the data (the source's current root) — never a value from the caller, the
/// owner or a direct, user-chosen link. The worst a caller could do with it is make a stamp correct.</para>
/// <para><b>Runs to completion once the caller's record is written.</b> It takes no cancellation token, like every
/// other after-write call site (the document PUT, the field-mapping push, the playbook update node; associate-record and
/// the event PUT were deleted by tasks 164 and 159): an update that landed must not be reported as cancelled with its children half re-stamped. A child that
/// fails is in the report and the log, never thrown; the reconciliation job repairs it within one cycle.</para>
/// <para><b>ADR-010.</b> A concrete class, not an interface: the narrowness is its single public member, and there is
/// one implementation. Registered beside the restamper (<c>AddCoreAncestorResolver</c>), unconditionally, so every
/// composition that has the tool framework has it too (CLAUDE.md §10 F.1; ADR-032: no flag, no Null-Object).</para>
/// </remarks>
public sealed class CoreAncestorAfterWriteRestamp
{
    private readonly CoreAncestorRestamper _restamper;

    public CoreAncestorAfterWriteRestamp(CoreAncestorRestamper restamper)
    {
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
    }

    /// <summary>
    /// Re-stamp what the caller's write of <paramref name="writtenColumns"/> to <paramref name="entityLogicalName"/>
    /// <paramref name="recordId"/> moved: the record's own copy when it changed what the record is filed under, and the
    /// copies of every record filed under it when it changed the record's root. Reads and writes nothing for a write that
    /// can move no stamp.
    /// </summary>
    public Task<RestampReport> AfterWriteAsync(
        string entityLogicalName, Guid recordId, IReadOnlyCollection<string> writtenColumns) =>
        _restamper.AfterWriteAsync(entityLogicalName, recordId, writtenColumns, CancellationToken.None);
}
