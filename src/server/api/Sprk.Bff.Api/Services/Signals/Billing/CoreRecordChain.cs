// ebilling-r1 task 082 (notes/procedure-and-file-model.md 6.4, 7.1; notes/rule-set-scope-design.md 3.1; ontology D-172; uac-r2 review
// item 8, round 2 items 082.1 / 082.2): the invoice's core record and its DIRECT parents, the only records whose Procedure assignments
// and overrides judge the invoice.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureRootInheritance's parent walks (NameParentsAsync, FindFilingParentsAsync) answer "what is this record filed
//       under" for access: they follow intermediates (#1630) and climb more than one hop. Its maps (TypedFilingColumns) and its pair
//       rule (NamePairParentAsync) are the definition of a direct filing.
//   (2) Extension — the walks cannot be reused as they are: D-172 forbids billing inheritance through an intermediate and past the
//       direct parent. The maps and the pair rule ARE reused (never copied); only the one-row read and the distance are ours.
//   (3) Cost of doing nothing — a project's or work assignment's invoice never sees its parent's Procedures and overrides, or (with
//       the access walk) sees its grandparent's, which the owner ruled out (W under P does not inherit M).
//
// Placement (bff-extensions.md): BFF, Services/Signals/Billing, a static reader over the app-only IGenericEntityService. No DI
// registration, no endpoint, no package.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>One record of a <see cref="CoreRecordChain"/>: the invoice's core record at distance 0, a direct parent at distance 1.
/// <see cref="object.ToString"/> prints the table and distance only (082c C17): a chain may hold a Secure parent, so never its id.</summary>
public sealed record CoreRecordLink(string Table, Guid Id, int Distance)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Table = ").Append(Table).Append(", Distance = ").Append(Distance);
        return true;
    }
}

/// <summary>
/// The core-record chain of an invoice (D-172, direct association only): its core record (distance 0) and the core records that record
/// is DIRECTLY filed under (distance 1), never more.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Matter:</b> itself only. A matter is filed under no core record, so nothing is read.</item>
/// <item><b>Project:</b> itself, then the matter or project its BFF-managed ADR-024 pair names. The pair is resolved ONLY through
/// uac-r2's <see cref="SecureRootInheritance.NamePairParentAsync"/>: <c>Parent</c> is the distance-1 record; <c>None</c> and
/// <c>NotAFilingParent</c> (a type that is not a matter or project) give no parent and no fault; <c>Unknown</c> fails closed (the
/// chain read throws, as an unreadable row does).</item>
/// <item><b>Work assignment:</b> itself, then each core record a TYPED filing lookup names (<see cref="SecureRootInheritance.TypedFilingColumns"/>).
/// Its pair is not read: on a work assignment the pair files under nothing on its own (#1616).</item>
/// <item><b>No second hop</b> and <b>no intermediate:</b> a parent's own parent is never read, and a work assignment filed under an
/// invoice or event (#1630) inherits nothing through it. Both are narrower than uac-r2's secure wall, which follows them for access;
/// billing does not, by the owner's decision. An ArchTest (<c>CoreRecordChainNoMultiHopGuardTests</c>) keeps the multi-hop and
/// intermediate members out of this file.</item>
/// <item><b>One app-only row read per record</b> (plus the record-type row a project's pair names). A parent that no longer exists is
/// not read: it holds no assignment rows, so it confers nothing.</item>
/// <item><b>Fail closed:</b> a read fault propagates; a core record that is not found, or a pair UAC answers <c>Unknown</c> for, throws
/// <see cref="CoreRecordChainUnreadableException"/>. The invoice is never judged on a shortened chain, which would silently drop the
/// parent's terms; the job retries.</item>
/// </list>
/// </remarks>
public static class CoreRecordChain
{
    /// <summary>The chain of <paramref name="core"/>, distance 0 first, then the direct parents in a stable order (table, then id).</summary>
    public static async Task<IReadOnlyList<CoreRecordLink>> ReadAsync(
        IGenericEntityService entities, ILogger logger, CoreRecordReference core, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(core);
        if (core.Id == Guid.Empty)
        {
            throw new ArgumentException("The core record id must not be empty.", nameof(core));
        }

        var table = TableOf(core.Kind);
        var self = new CoreRecordLink(table, core.Id, 0);
        var typed = SecureRootInheritance.TypedFilingColumns.TryGetValue(table, out var columns) ? columns : [];
        var readsPair = SecureRootInheritance.PairNamesAParentOnItsOwn(table);
        if (typed.Count == 0 && !readsPair)
        {
            return [self];
        }

        var select = typed.Select(c => c.Column).ToList();
        if (readsPair)
        {
            select.Add(SecureRootInheritance.PairIdColumn);
            select.Add(SecureRootInheritance.PairTypeColumn);
        }

        // NoLock = true, app-only: this read serves PROCEDURE SELECTION only (which Procedures judge the invoice). It is NOT an access
        // decision and must not be reused to judge access; access goes through SecureRootInheritance and the unified evaluator.
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(select.ToArray()), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, core.Id);
        var row = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault(r => r.Id == core.Id)
                  ?? throw new CoreRecordChainUnreadableException(
                      $"The invoice's core record {table} {core.Id} was not found, so its direct parents cannot be read.");

        var parents = new List<CoreRecordLink>();
        foreach (var (column, parentTable) in typed)
        {
            if (row.GetAttributeValue<EntityReference>(column)?.Id is { } parentId && parentId != Guid.Empty)
            {
                parents.Add(new CoreRecordLink(parentTable, parentId, 1));
            }
        }

        if (readsPair)
        {
            var answer = await SecureRootInheritance.NamePairParentAsync(
                    entities, logger, table, row.GetAttributeValue<string>(SecureRootInheritance.PairIdColumn),
                    row.GetAttributeValue<EntityReference>(SecureRootInheritance.PairTypeColumn)?.Id,
                    new ConcurrentDictionary<Guid, string?>(), ct, parents.Select(p => p.Id).ToHashSet())
                .ConfigureAwait(false);
            switch (answer)
            {
                case PairParentAnswer.Parent parent:
                    parents.Add(new CoreRecordLink(parent.Table, parent.Id, 1));
                    break;
                case PairParentAnswer.None:
                case PairParentAnswer.NotAFilingParent:
                    break; // no pair, a repeat, or a type that is not a matter or project: no parent and no fault
                case PairParentAnswer.Unknown unknown:
                    throw new CoreRecordChainUnreadableException(
                        $"What the invoice's core record {table} {core.Id} is filed under cannot be told: {unknown.Reason}.", unknown.Transient);
                default:
                    // The answer type is a closed set of four (a private base constructor stops other assemblies; a record's protected copy
                    // constructor still lets code in this one derive). A fifth must be decided here on purpose, never dropped as "no parent",
                    // which would judge the invoice on a shortened chain.
                    throw new UnreachableException(
                        $"Unexpected pair answer {answer.GetType().Name} for core record {table} {core.Id}; CoreRecordChain must handle it explicitly.");
            }
        }

        // A record filed under itself names no parent (data written around the BFF); the same parent twice is one link.
        return [self, .. parents
            .Where(p => p.Id != core.Id)
            .DistinctBy(p => (p.Table, p.Id))
            .OrderBy(p => p.Table, StringComparer.Ordinal)
            .ThenBy(p => p.Id)];
    }

    /// <summary>The core table of <paramref name="kind"/>.</summary>
    internal static string TableOf(CoreRecordKind kind) => kind switch
    {
        CoreRecordKind.Matter => SecureRootInheritance.Matter,
        CoreRecordKind.Project => SecureRootInheritance.Project,
        CoreRecordKind.WorkAssignment => SecureRootInheritance.WorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a core record kind."),
    };
}

/// <summary>
/// The core-record chain could not be read in full (<see cref="CoreRecordChain"/>): the core record was not found, or what it is filed
/// under cannot be told. The evaluation faults and the job retries; nothing is judged on a shortened chain.
/// </summary>
/// <param name="message">For operators only: it names the core record. Never shown to a reader.</param>
/// <param name="transient">ebilling-r1 082c-2 fix round 1 (K1): the chain could not be read because a read FAULTED (an outage), not because the
/// stored data is wrong, so a retry may help. False for every other cause (the core record is missing, a pair that names nothing readable).</param>
public sealed class CoreRecordChainUnreadableException(string message, bool transient = false) : InvalidOperationException(message)
{
    /// <summary>True when only a failed read stopped the chain (a retry may help); false when the data is what cannot be read.</summary>
    public bool Transient { get; } = transient;
}
