using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// #2143 round 6: the ONE rule for "the parents a message's record thread files it under" (S6): a <c>sprk_communication</c>
/// in a RECORD thread takes every ownership parent the thread row carries (<see cref="RecordOwnershipContext.ParentsOf(Entity)"/>),
/// in addition to its own. Round 7d: decided by the thread's TYPE (<c>sprk_threadtype</c>, <see cref="ParentsOf"/>) — a
/// Direct thread, the per-user master included, adds nothing whatever lookups it carries; a thread whose type is not set or
/// not known counts as a record thread (secure-if-any makes that the isolating answer). The thread itself is never an
/// ownership parent (<see cref="RecordOwnershipResolver.OwnershipParentEntities"/>' remarks). Shared by the live
/// re-file path (<see cref="RecordOwnershipResolver"/>'s <c>ReparentAsync</c> and its settle reads) and the backstop
/// (<c>SecureChildReconciler</c>'s <c>ContextFor</c>), so the two never disagree on a message's owner.
/// </summary>
/// <remarks>Component Justification (CLAUDE.md §11): (1) Existing — <c>SecureChildReconciler.ContextFor</c> applied this rule
/// inline, over its preloaded rows only; the resolver did not apply it at all (it compared the thread lookup, never resolved
/// it). (2) Extension — the rule is extracted from <c>ContextFor</c> rather than written a second time; a static helper
/// beside the resolver, no DI surface. (3) Cost of doing nothing — a message in a secure record's thread re-filed off its
/// other secure parent was given an ordinary owner with no F3 check on the thread's record, and a join already on the row
/// at the first read was invisible to the release, K4 and the guard.</remarks>
internal static class RecordThreadFiling
{
    /// <summary>The table whose rows sit in threads.</summary>
    internal const string CommunicationTable = "sprk_communication";

    /// <summary>The thread table.</summary>
    internal const string ThreadTable = "sprk_communicationthread";

    /// <summary>A message's thread lookup.</summary>
    internal const string ThreadColumn = "sprk_communicationthread";

    /// <summary>The thread's type (<c>ThreadTopology</c>).</summary>
    internal const string ThreadTypeColumn = "sprk_threadtype";

    /// <summary><c>sprk_threadtype</c> Direct (1:1, and the per-user master): files its messages under nothing.</summary>
    internal const int DirectThreadType = 100000001;

    /// <summary>Whether <paramref name="table"/> is the table whose rows take their thread's filing.</summary>
    internal static bool AppliesTo(string? table) =>
        string.Equals(table?.Trim(), CommunicationTable, StringComparison.OrdinalIgnoreCase);

    /// <summary>The thread <paramref name="row"/> sits in — <c>null</c> for a row of another table or one in no thread.</summary>
    internal static Guid? ThreadOf(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return AppliesTo(row.LogicalName) && row.GetAttributeValue<EntityReference>(ThreadColumn) is { Id: var id } && id != Guid.Empty
            ? id
            : null;
    }

    /// <summary>
    /// The parents <paramref name="thread"/> files its messages under, by its TYPE (#2143 round 7d): a Direct thread
    /// (<see cref="DirectThreadType"/>, the per-user master included) none; a record thread (<c>100000000</c>) — and a thread
    /// whose type is not set or not known, counted as one (fail closed by isolating: secure-if-any) — every ownership parent it
    /// carries.
    /// </summary>
    internal static IReadOnlyList<RecordOwnershipParent> ParentsOf(Entity thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return thread.GetAttributeValue<OptionSetValue>(ThreadTypeColumn)?.Value == DirectThreadType
            ? Array.Empty<RecordOwnershipParent>()
            : RecordOwnershipContext.OrderedParentsOf(thread); // UAC task 194b K2: a fixed order, never the attribute order
    }

    /// <summary>
    /// The thread row, read COMMITTED (<c>NoLock = false</c>) with every column — its ownership parents are whatever lookups
    /// it carries, as for the message itself. A thread that does not come back is a fault (fail closed): "no parents" would
    /// release a message from a secure thread. A Dataverse fault propagates.
    /// </summary>
    /// <exception cref="InvalidOperationException">The thread does not exist (or cannot be seen).</exception>
    /// <summary>
    /// UAC task 194b R4: a message's thread filing (<see cref="ParentsOf"/>) as lookups of the MESSAGE — each parent keyed under
    /// <see cref="ThreadColumn"/>, the column that files the message under it — for <c>RecordOwnershipContext.ForRow</c>, which
    /// ranks that column after the message's own lookups (<c>SecureChildLineage</c>). Two parents under the one column keep
    /// their order (the ordering is a stable sort). A Direct thread's filing is empty, so it adds nothing.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> FilingLookupsOf(IEnumerable<RecordOwnershipParent> threadParents) =>
        threadParents.Select(p => KeyValuePair.Create(ThreadColumn, (RecordOwnershipParent?)p));

    /// <summary>
    /// UAC task 194b R4: THE ownership question for an existing message and its thread's filing — the row's own lookups, then
    /// <paramref name="threadParents"/> under <see cref="ThreadColumn"/>, through the one builder. The secure-child reconciler
    /// and <c>RecordCreationService</c>'s Repair both ask it, so they cannot drift.
    /// </summary>
    internal static RecordOwnershipContext QuestionWithThreadFiling(Entity row, IEnumerable<RecordOwnershipParent> threadParents) =>
        RecordOwnershipContext.ForRow(
            row.LogicalName,
            RecordOwnershipContext.LookupsOf(row).Concat(FilingLookupsOf(threadParents)),
            RecordOwnershipContext.PairIdOf(row));

    internal static async Task<Entity> ReadThreadAsync(IGenericEntityService dataverse, Guid threadId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        var query = new QueryExpression(ThreadTable)
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1,
            NoLock = false,
        };
        query.Criteria.AddCondition($"{ThreadTable}id", ConditionOperator.Equal, threadId);
        return (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"The {ThreadTable} {threadId:D} a message sits in could not be read, so the parents it files the message under "
                + "are not known; no owner is decided from it (#2143).");
    }

    /// <summary>
    /// The parents the thread <paramref name="threadId"/> files a row of <paramref name="table"/> under: none for another table
    /// or no thread; else <see cref="ParentsOf"/> of the thread, read by <see cref="ReadThreadAsync"/>.
    /// </summary>
    internal static async Task<IReadOnlyList<RecordOwnershipParent>> ParentsAsync(
        IGenericEntityService dataverse, string table, Guid? threadId, CancellationToken ct)
    {
        if (!AppliesTo(table) || threadId is not { } id || id == Guid.Empty)
            return Array.Empty<RecordOwnershipParent>();
        return ParentsOf(await ReadThreadAsync(dataverse, id, ct).ConfigureAwait(false));
    }
}
