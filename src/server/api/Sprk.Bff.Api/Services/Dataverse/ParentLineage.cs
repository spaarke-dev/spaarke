// unified-access-control-r2 task 173 (owner rounds 81 and 84; GitHub #1423).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — SecureChildShareSynchronizer walks a row's lookups upward (LineageOfAsync), but only through
//       Secure-team-owned rows, inside a share run, and it answers "which secure roots" (with their sharees), not "which
//       records sit at the top of this row's filing, and what do they hold". CoreAncestorResolver reads ONE hop (an
//       intermediate's own root columns) and covers four of the ~23 lineage tables. RecordOwnershipResolver reads a row's
//       direct parents for an owner decision. None of them can say what a row's parents hold, whatever their owner.
//   (2) Extension — Not the synchronizer: its walk is scoped to isolated rows by design (an ordinary row ends it), and the
//       owner rule "a child follows its parent" (round 84) applies to every row. Not CoreAncestorResolver.ResolveStampsAsync:
//       a one-hop read cannot reach the top of a document → analysis → to-do chain. So the walk is one small class over
//       the ONE lineage map (SecureChildLineage), and CoreAncestorResolver (the shared stamp path) and
//       SecureChildReconciliationJob (the reconcile) both call it; tasks 174/175 pass their own parent map and columns.
//   (3) Cost of doing nothing — a To Do filed under a Restricted matter shows Standard (UAT 2026-10-08, PAT-176903), and
//       no path keeps a child's displayed Access Permission equal to its parent's (owner rounds 81 and 84).
//
// Placement (bff-extensions.md): Services/Dataverse beside CoreAncestorResolver, the shared child write path. No package,
// no endpoint, no DI registration (constructed per operation from services its callers already hold), no interface.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Which lookups FILE a row under another record, for the values a filed record takes from its parents (owner rounds 81
/// and 84: "a child's access always follows its parent").
/// </summary>
/// <remarks>
/// <para><b>One source.</b> <see cref="SecureChildLineage"/> is the map of which lookups make a row a child of what (task
/// 149). <see cref="ChildFiling"/> is that map minus <see cref="NotFiling"/>, nothing added. The form library
/// <c>sprk_accesspermission_inherited.js</c> carries the same map literally (it decides "has a parent" on the form), and
/// <c>ParentLineageTests.FormLibraryParentLookups_MatchTheServerMap</c> fails the build if the two drift.</para>
/// <para><b>Roots are terminal here.</b> A project, matter or work assignment has no entry, so a walk stops at it and reads
/// its own values. Tasks 174/175 (round 84: a work assignment or project filed under a parent takes the parent's flags)
/// pass a map that adds the roots' own filing lookups to <see cref="ParentLineageWalk"/>.</para>
/// </remarks>
internal static class ParentLineage
{
    /// <summary>
    /// Lineage lookups that never file a row under another record. <c>sprk_document.sprk_currentversionid</c> names the
    /// document's own current file version, whose <c>sprk_document</c> lookup names the document again: it is the record
    /// itself, not a parent. Kept in the lineage for share narrowing (task 149), excluded here so a document with a file
    /// but no filing is parentless (it keeps its own value and its field stays editable).
    /// </summary>
    internal static readonly IReadOnlySet<(string Table, string Column)> NotFiling = new HashSet<(string, string)>
    {
        ("sprk_document", "sprk_currentversionid"),
    };

    /// <summary>Every child table's filing lookups (column → target table): <see cref="SecureChildLineage"/> minus <see cref="NotFiling"/>.</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ChildFiling =
        SecureChildLineage.Children.Values.ToDictionary(
            t => t.LogicalName,
            t => (IReadOnlyDictionary<string, string>)t.Lookups
                .Where(l => !NotFiling.Contains((t.LogicalName, l.Key)))
                .ToDictionary(l => l.Key, l => l.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The parents a payload names: every filing lookup of <paramref name="table"/> whose value <paramref name="read"/>
    /// returns, as (target table, id). Empty when the table has no filing lookups in <paramref name="filing"/>.
    /// </summary>
    internal static IReadOnlyList<(string Table, Guid Id)> ParentsIn(
        string table, Func<string, EntityReference?> read,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? filing = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!(filing ?? ChildFiling).TryGetValue(table, out var lookups))
            return [];

        return lookups
            .Select(l => (Table: l.Value, Id: read(l.Key)?.Id ?? Guid.Empty))
            .Where(p => p.Id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    /// <summary>True when <paramref name="table"/> can be filed under <paramref name="parentTable"/> (a lookup of it targets that table).</summary>
    internal static bool CanBeFiledUnder(
        string table, string parentTable, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? filing = null) =>
        (filing ?? ChildFiling).TryGetValue(table, out var lookups)
        && lookups.Values.Any(t => string.Equals(t, parentTable, StringComparison.OrdinalIgnoreCase));
}

/// <summary>How a walk to the top of a row's filing ended.</summary>
internal enum ParentTopsStatus
{
    /// <summary>The row names no parent: it has no parent-derived value, and its own value is its own.</summary>
    NoParent,

    /// <summary>Every top record was reached and read (<see cref="ParentTops.Tops"/>).</summary>
    Found,

    /// <summary>
    /// The top could not be decided: a record on the way could not be read or does not exist, the filing runs deeper than
    /// <see cref="ParentLineageWalk.MaxDepth"/>, or it loops without reaching a record that has no parent. A caller writes
    /// nothing from it.
    /// </summary>
    Undetermined,
}

/// <summary>One record at the top of a filing: it has no parent of its own. <paramref name="Row"/> carries the walk's value columns that its table has.</summary>
internal sealed record ParentTop(string Table, Guid Id, Entity Row);

/// <summary>The outcome of <see cref="ParentLineageWalk.TopsAsync"/>.</summary>
internal sealed record ParentTops(ParentTopsStatus Status, IReadOnlyList<ParentTop> Tops, string? Reason)
{
    internal static readonly ParentTops NoParent = new(ParentTopsStatus.NoParent, [], null);

    internal static ParentTops Undetermined(string reason) => new(ParentTopsStatus.Undetermined, [], reason);
}

/// <summary>
/// One walk session UP a row's filing to the records at its top — the records that have no parent of their own — reading
/// the requested value columns from them. Caches every row it reads, so many rows of one operation (a reconcile pass)
/// share their parents' reads. Not thread-safe; one per operation.
/// </summary>
/// <remarks>
/// <para><b>The walk.</b> Breadth first from the row's parents, one level per round: each level's unread records are read
/// in one query per table (<c>id IN (…)</c>, chunked), selecting the table's filing lookups and the value columns its
/// table has (live metadata, the 6h-cached probe). A record with no non-null filing lookup is a TOP. A record seen before is
/// not walked again, so a loop ends; one that never reaches a top is <see cref="ParentTopsStatus.Undetermined"/>.</para>
/// <para><b>Fail closed for a writer.</b> A read or metadata fault, a record that does not come back, or a filing deeper
/// than <see cref="MaxDepth"/> levels answers <see cref="ParentTopsStatus.Undetermined"/> with the reason. It never
/// throws (cancellation aside) and never guesses a top.</para>
/// <para><b>Lookup columns that do not exist</b> in this environment (a schema deployed after the BFF, #1378) are not
/// selected: a missing column cannot hold a parent.</para>
/// </remarks>
internal sealed class ParentLineageWalk
{
    /// <summary>The deepest filing a walk follows (the share synchronizer's own limit).</summary>
    internal const int MaxDepth = SecureChildShareSynchronizer.MaxLineageDepth;

    /// <summary>Ids per <c>IN</c> condition.</summary>
    private const int IdsPerQuery = SecureChildShareSynchronizer.DescendantConditionsPerQuery;

    private readonly IGenericEntityService _dataverse;
    private readonly CoreAncestorResolver.EntityColumnProbe _probe;
    private readonly IReadOnlyList<string> _valueColumns;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _filing;
    private readonly Dictionary<(string Table, Guid Id), Entity?> _rows = new();
    private readonly Dictionary<string, (IReadOnlyList<string> Lookups, IReadOnlyList<string> Select)> _columns =
        new(StringComparer.OrdinalIgnoreCase);

    /// <param name="dataverse">Reads (app-only).</param>
    /// <param name="probe">Which columns a table has (CoreAncestorResolver's metadata probe).</param>
    /// <param name="valueColumns">The columns read from each top record (e.g. <c>sprk_accesspermission</c>).</param>
    /// <param name="filing">Which lookups file a row under what; default <see cref="ParentLineage.ChildFiling"/>.</param>
    internal ParentLineageWalk(
        IGenericEntityService dataverse,
        CoreAncestorResolver.EntityColumnProbe probe,
        IReadOnlyList<string> valueColumns,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? filing = null)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _valueColumns = valueColumns ?? throw new ArgumentNullException(nameof(valueColumns));
        _filing = filing ?? ParentLineage.ChildFiling;
    }

    /// <summary>The filing map this walk follows.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Filing => _filing;

    /// <summary>The filing queries this walk has sent (one per table and chunk of ids). Task 187 fix round 2, K1: logged.</summary>
    internal int QueriesSent { get; private set; }

    /// <summary>The records this walk has asked Dataverse for (found or not).</summary>
    internal int RecordsAsked { get; private set; }

    /// <summary>
    /// The columns to select for a row of <paramref name="table"/> so that <see cref="Remember"/> can take it: its filing
    /// lookups and the value columns, each only when the table has it. A metadata fault propagates.
    /// </summary>
    internal async Task<IReadOnlyList<string>> ColumnsForAsync(string table, CancellationToken ct) =>
        (await ColumnsAsync(table, ct).ConfigureAwait(false)).Select;

    /// <summary>
    /// The filing lookups of <paramref name="table"/> that exist in this environment. A metadata fault propagates.
    /// </summary>
    internal async Task<IReadOnlyList<string>> FilingLookupsOfAsync(string table, CancellationToken ct) =>
        (await ColumnsAsync(table, ct).ConfigureAwait(false)).Lookups;

    /// <summary>
    /// Records a row already read with <see cref="ColumnsForAsync"/>'s columns (a listing), so the walk does not read it
    /// again.
    /// </summary>
    internal void Remember(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Id != Guid.Empty && !string.IsNullOrWhiteSpace(row.LogicalName))
            _rows[(row.LogicalName.ToLowerInvariant(), row.Id)] = row;
    }

    /// <summary>
    /// Owner round 122: the rows of <paramref name="table"/> with <paramref name="ids"/>, as this walk holds them (its filing
    /// lookups and value columns). A row it already read or remembered is not read again; the rest are read in one query per
    /// chunk. A row that does not exist is left out. A read or metadata fault propagates.
    /// </summary>
    internal async Task<IReadOnlyList<Entity>> RowsAsync(string table, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentNullException.ThrowIfNull(ids);
        var nodes = ids.Where(id => id != Guid.Empty).Distinct().Select(id => (Table: table.ToLowerInvariant(), Id: id)).ToList();
        await LoadAsync(nodes, ct).ConfigureAwait(false);
        return nodes.Select(n => _rows[n]).OfType<Entity>().ToList();
    }

    /// <summary>The non-null filing parents of a row read with <see cref="ColumnsForAsync"/>'s columns.</summary>
    internal async Task<IReadOnlyList<(string Table, Guid Id)>> ParentsOfAsync(Entity row, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        var lookups = await FilingLookupsOfAsync(row.LogicalName, ct).ConfigureAwait(false);
        return ParentsOf(row.LogicalName, row, lookups);
    }

    /// <summary>
    /// The records at the top of a filing that starts at <paramref name="parents"/> (a row's own non-null filing parents).
    /// No parents → <see cref="ParentTopsStatus.NoParent"/>. <paramref name="self"/> is the row being decided, when it
    /// exists: a filing that leads back to it adds nothing (its STORED lookups may be the ones a write is replacing).
    /// </summary>
    internal async Task<ParentTops> TopsAsync(
        IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct, (string Table, Guid Id)? self = null)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var start = parents
            .Where(p => p.Id != Guid.Empty && !string.IsNullOrWhiteSpace(p.Table))
            .Select(p => (Table: p.Table.ToLowerInvariant(), p.Id))
            .Distinct()
            .ToList();
        if (start.Count == 0)
            return ParentTops.NoParent;

        try
        {
            var visited = new HashSet<(string, Guid)>(start);
            if (self is { } me && me.Id != Guid.Empty && !string.IsNullOrWhiteSpace(me.Table))
            {
                var key = (me.Table.ToLowerInvariant(), me.Id);
                visited.Add(key);
                start.Remove(key);
                if (start.Count == 0)
                    return ParentTops.Undetermined("it is filed only under itself");
            }

            var frontier = start;
            var tops = new List<ParentTop>();
            for (var depth = 1; frontier.Count > 0; depth++)
            {
                if (depth > MaxDepth)
                    return ParentTops.Undetermined($"its filing runs deeper than {MaxDepth} levels");

                await LoadAsync(frontier, ct).ConfigureAwait(false);
                var next = new List<(string Table, Guid Id)>();
                foreach (var node in frontier)
                {
                    if (_rows[node] is not { } row)
                        return ParentTops.Undetermined($"{node.Table} {node.Id:D} on its filing does not exist");

                    var lookups = (await ColumnsAsync(node.Table, ct).ConfigureAwait(false)).Lookups;
                    var above = ParentsOf(node.Table, row, lookups);
                    if (above.Count == 0)
                    {
                        tops.Add(new ParentTop(node.Table, node.Id, row));
                        continue;
                    }

                    foreach (var parent in above)
                    {
                        if (visited.Add(parent))
                            next.Add(parent);
                    }
                }

                frontier = next;
            }

            return tops.Count == 0
                ? ParentTops.Undetermined("its filing loops back on itself and reaches no record without a parent")
                : new ParentTops(ParentTopsStatus.Found, tops, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ParentTops.Undetermined($"a record on its filing could not be read ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>
    /// Task 187 (owner round 99, D-123): the BATCHED counterpart of <see cref="TopsAsync"/> for the parents of a whole page of
    /// rows: one breadth-first walk over the UNION of <paramref name="starts"/>, each level read in one query per table
    /// (<c>id IN (…)</c>, chunked), then each start's tops decided from what was read, with no further query.
    /// </summary>
    /// <remarks>
    /// <para><b>Terminal records are not read.</b> A record <paramref name="isTerminal"/> names (the external read path passes
    /// the three roots) is a top without a query: its existence and its secure state are the caller's next read to decide (the
    /// external secure-flag read counts a root it cannot read as secure).</para>
    /// <para><b>Bounded, fail closed.</b> At most <paramref name="maxReads"/> distinct non-terminal records are read and at most
    /// <see cref="MaxDepth"/> levels are followed; a level the bound cuts is read up to the bound, in frontier order. A start
    /// whose filing reaches a record that was not read (past either bound),
    /// a record that did not come back, or a read that faulted is <see cref="ParentTopsStatus.Undetermined"/>; it never
    /// guesses a top. A loop that reaches no top is Undetermined too. Never throws (cancellation aside): a fault is reported
    /// through <c>Faulted</c>.</para>
    /// </remarks>
    /// <param name="starts">The parents of every row (table, id). Empty ids are ignored.</param>
    /// <param name="isTerminal">Whether a table is a top that is never read.</param>
    /// <param name="maxReads">The most distinct non-terminal records the walk reads.</param>
    /// <param name="ct">Cancellation token.</param>
    internal async Task<(IReadOnlyDictionary<(string Table, Guid Id), ParentTops> Tops, bool Faulted)> TopsOfManyAsync(
        IReadOnlyCollection<(string Table, Guid Id)> starts, Func<string, bool> isTerminal, int maxReads, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(isTerminal);
        var distinct = starts
            .Where(s => s.Id != Guid.Empty && !string.IsNullOrWhiteSpace(s.Table))
            .Select(s => (Table: s.Table.ToLowerInvariant(), s.Id))
            .Distinct()
            .ToList();

        // What each record read is filed under; a record absent here was not read (bound, fault) or did not come back.
        var above = new Dictionary<(string Table, Guid Id), IReadOnlyList<(string Table, Guid Id)>>();
        var faulted = false;
        try
        {
            var seen = new HashSet<(string, Guid)>(distinct);
            var frontier = distinct.Where(s => !isTerminal(s.Table)).ToList();
            var read = 0;
            for (var depth = 1; frontier.Count > 0 && depth <= MaxDepth; depth++)
            {
                // The bound (task 187 fix round 1, R1): the part of the level that fits is read; the rest is not, and only a
                // start whose filing reaches an unread record is Undetermined. (Stopping the WHOLE level emptied a normal list:
                // 251 to-dos, each under its own communication and thread, read 251 + 251 and listed none.)
                var remaining = maxReads - read;
                if (remaining <= 0)
                    break;
                if (frontier.Count > remaining)
                    frontier = frontier.Take(remaining).ToList();

                await LoadAsync(frontier, ct).ConfigureAwait(false);
                read += frontier.Count;
                var next = new List<(string Table, Guid Id)>();
                foreach (var node in frontier)
                {
                    if (_rows[node] is not { } row)
                        continue; // did not come back: Undetermined for every start that reaches it

                    var lookups = (await ColumnsAsync(node.Table, ct).ConfigureAwait(false)).Lookups;
                    var parents = ParentsOf(node.Table, row, lookups);
                    above[node] = parents;
                    foreach (var parent in parents)
                    {
                        if (seen.Add(parent) && !isTerminal(parent.Table))
                            next.Add(parent);
                    }
                }

                frontier = next;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            faulted = true; // every start whose filing reaches an unread record is Undetermined below
        }

        var tops = new Dictionary<(string Table, Guid Id), ParentTops>();
        foreach (var start in distinct)
            tops[start] = TopsFromRead(start, above, isTerminal, faulted);
        return (tops, faulted);
    }

    /// <summary>One start's tops from the records <see cref="TopsOfManyAsync"/> read (no query), with <see cref="TopsAsync"/>'s depth rule.</summary>
    private static ParentTops TopsFromRead(
        (string Table, Guid Id) start,
        IReadOnlyDictionary<(string Table, Guid Id), IReadOnlyList<(string Table, Guid Id)>> above,
        Func<string, bool> isTerminal,
        bool faulted)
    {
        var visited = new HashSet<(string, Guid)> { start };
        var frontier = new List<(string Table, Guid Id)> { start };
        var tops = new List<ParentTop>();
        for (var depth = 1; frontier.Count > 0; depth++)
        {
            if (depth > MaxDepth)
                return ParentTops.Undetermined($"its filing runs deeper than {MaxDepth} levels");

            var next = new List<(string Table, Guid Id)>();
            foreach (var node in frontier)
            {
                if (isTerminal(node.Table))
                {
                    tops.Add(new ParentTop(node.Table, node.Id, new Entity(node.Table, node.Id)));
                    continue;
                }

                if (!above.TryGetValue(node, out var parents))
                {
                    return ParentTops.Undetermined(faulted
                        ? $"{node.Table} {node.Id:D} on its filing could not be read"
                        : $"{node.Table} {node.Id:D} on its filing was not read or does not exist");
                }

                if (parents.Count == 0)
                {
                    tops.Add(new ParentTop(node.Table, node.Id, new Entity(node.Table, node.Id)));
                    continue;
                }

                foreach (var parent in parents)
                {
                    if (visited.Add(parent))
                        next.Add(parent);
                }
            }

            frontier = next;
        }

        return tops.Count == 0
            ? ParentTops.Undetermined("its filing loops back on itself and reaches no record without a parent")
            : new ParentTops(ParentTopsStatus.Found, tops, null);
    }

    private IReadOnlyList<(string Table, Guid Id)> ParentsOf(string table, Entity row, IReadOnlyList<string> lookups)
    {
        if (!_filing.TryGetValue(table, out var map))
            return [];

        return lookups
            .Select(column => (Table: map[column].ToLowerInvariant(), Id: row.GetAttributeValue<EntityReference>(column)?.Id ?? Guid.Empty))
            .Where(p => p.Id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private async Task<(IReadOnlyList<string> Lookups, IReadOnlyList<string> Select)> ColumnsAsync(string table, CancellationToken ct)
    {
        if (_columns.TryGetValue(table, out var known))
            return known;

        var present = await _probe(table, ct).ConfigureAwait(false);
        var lookups = _filing.TryGetValue(table, out var map)
            ? map.Keys.Where(present.Contains).OrderBy(c => c, StringComparer.Ordinal).ToList()
            : new List<string>();
        var values = _valueColumns.Where(present.Contains).ToList();
        var select = lookups.Concat(values).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return _columns[table] = (lookups, select);
    }

    private async Task LoadAsync(IReadOnlyList<(string Table, Guid Id)> nodes, CancellationToken ct)
    {
        foreach (var group in nodes.Where(n => !_rows.ContainsKey(n)).GroupBy(n => n.Table, StringComparer.OrdinalIgnoreCase))
        {
            var table = group.Key;
            var select = (await ColumnsAsync(table, ct).ConfigureAwait(false)).Select;
            foreach (var chunk in group.Select(n => n.Id).Distinct().Chunk(IdsPerQuery))
            {
                var query = new QueryExpression(table)
                {
                    ColumnSet = select.Count > 0 ? new ColumnSet(select.ToArray()) : new ColumnSet(table + "id"),
                    NoLock = true,
                };
                query.Criteria.AddCondition(table + "id", ConditionOperator.In, chunk.Cast<object>().ToArray());

                QueriesSent++;
                RecordsAsked += chunk.Length;
                var found = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
                foreach (var id in chunk)
                    _rows[(table, id)] = found.FirstOrDefault(e => e.Id == id);
            }
        }
    }
}

/// <summary>
/// The Access Permission a To Do, Event, Communication or Document takes from the records it is filed under (owner round
/// 81, extended by round 84): the MOST RESTRICTIVE <c>sprk_accesspermission</c> of the records at the top of its filing.
/// </summary>
/// <remarks>
/// <para><b>Display only on To Do, Event and Communication.</b> No access decision reads their value (round 2 Q6; round 81):
/// enforcement keeps reading the roots. A parentless child's own value is recorded only (owner, 2026-10-08: "This is fine
/// (BUT in future we might need to revisit)"). A DOCUMENT may narrow: under D7 (#2126) its own Restricted hides it from
/// partners, so its stored value is max(own, inherited) and is never lowered below its own narrowing (task 189,
/// <see cref="DecideDocument"/>).</para>
/// <para><b>The rule.</b> Restricted over Limited over Standard; a null value, or a top whose table has no such column,
/// counts as Standard. A row with no non-null filing lookup has no inherited value: its own value is the user's and is
/// never overwritten. Written by the shared stamp path (<see cref="CoreAncestorResolver"/>) and kept in step by
/// <c>SecureChildReconciliationJob</c>, both through this one class.</para>
/// </remarks>
internal static class InheritedAccessPermission
{
    /// <summary>The column, on the roots and on the four child tables.</summary>
    internal const string Column = "sprk_accesspermission";

    internal const int Standard = 100000000;
    internal const int Limited = 100000001;
    internal const int Restricted = 100000002;

    /// <summary>The tables whose value is inherited (round 81). Work assignments and projects are tasks 174/175.</summary>
    internal static readonly IReadOnlySet<string> Tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_todo", "sprk_event", "sprk_communication", "sprk_document",
    };

    /// <summary>True when <paramref name="table"/> shows an inherited Access Permission.</summary>
    internal static bool AppliesTo(string? table) => table is not null && Tables.Contains(table);

    /// <summary>
    /// The most restrictive of <paramref name="values"/> (null counts as Standard), or <see langword="null"/> when one of
    /// them is not a known level — an unknown option is never ranked by guess.
    /// </summary>
    internal static int? MostRestrictive(IEnumerable<int?> values)
    {
        var rank = 0;
        foreach (var value in values)
        {
            var r = value switch
            {
                null or Standard => 0,
                Limited => 1,
                Restricted => 2,
                _ => -1,
            };
            if (r < 0)
                return null;
            rank = Math.Max(rank, r);
        }

        return rank switch { 2 => Restricted, 1 => Limited, _ => Standard };
    }

    /// <summary>What a row's parents give it.</summary>
    internal sealed record Answer(ParentTopsStatus Status, int? Value, IReadOnlyList<ParentTop> Tops, string? Reason);

    /// <summary>
    /// The inherited value for a row whose filing parents are <paramref name="parents"/>, through <paramref name="walk"/>
    /// (which must read <see cref="Column"/>). <see cref="ParentTopsStatus.Found"/> carries the value.
    /// </summary>
    internal static async Task<Answer> ResolveAsync(
        ParentLineageWalk walk, IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct,
        (string Table, Guid Id)? self = null)
    {
        ArgumentNullException.ThrowIfNull(walk);
        var tops = await walk.TopsAsync(parents, ct, self).ConfigureAwait(false);
        if (tops.Status != ParentTopsStatus.Found)
            return new Answer(tops.Status, null, tops.Tops, tops.Reason);

        var value = MostRestrictive(tops.Tops.Select(t => t.Row.GetAttributeValue<OptionSetValue>(Column)?.Value));
        return value is null
            ? new Answer(ParentTopsStatus.Undetermined, null, tops.Tops,
                "a record at the top of its filing holds an Access Permission that is not Standard, Limited or Restricted")
            : new Answer(ParentTopsStatus.Found, value, tops.Tops, null);
    }

    /// <summary>A short "table id" list of the tops, for logs and the job report.</summary>
    internal static string Describe(IEnumerable<ParentTop> tops) =>
        string.Join(", ", tops.Select(t => $"{t.Table} {t.Id:D}"));

    /// <summary>
    /// Task 189 (D7 T7a, #2126): the tables whose rows RECORD their own narrowing in <see cref="AccessInheritance.Column"/>.
    /// Only documents do: under D7 a document's own Restricted hides it from partners. On these tables the stored value is
    /// max(own, inherited) (<see cref="DecideDocument"/>), and the BFF never lowers it below the own narrowing. On the other
    /// three tables it stays equal to the inherited value, as before.
    /// </summary>
    internal static readonly IReadOnlySet<string> RecordTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_document",
    };

    /// <summary>True when rows of <paramref name="table"/> record their own narrowing (<see cref="RecordTables"/>).</summary>
    internal static bool RecordsOwnNarrowing(string? table) => table is not null && RecordTables.Contains(table);

    /// <summary>
    /// Task 189 fix round C (K5): the reason code when an app-only generic writer names a document's Access Permission or
    /// its own-narrowing record (<see cref="RefuseAppOnlyWrite"/>).
    /// </summary>
    internal const string AppOnlyDocumentWriteReasonCode = "sdap.access.document_access_server_only";

    /// <summary>
    /// Task 188 round 4e: the reason code when an app-only generic writer names one of a document's D7 link columns
    /// (<see cref="DocumentLinkColumns"/>; <see cref="RefuseAppOnlyWrite"/>).
    /// </summary>
    internal const string AppOnlyDocumentLinkWriteReasonCode = "sdap.access.document_link_server_only";

    /// <summary>
    /// Task 188 round 4e: the document columns THE external document narrowing walks (D7 rules 3 and 4): its parent document
    /// and the communication it comes from. They are visibility inputs, so re-pointing or clearing either one can show a
    /// hidden document to partners. The same constants the predicate reads, so the two cannot drift.
    /// </summary>
    internal static readonly IReadOnlySet<string> DocumentLinkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDocumentNarrowing.ParentDocumentAttribute,
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDocumentNarrowing.RelatedCommunicationAttribute,
    };

    /// <summary>
    /// #2240: the reason code when an app-only generic writer names one of a document's BFF-owned columns
    /// (<see cref="BffOwnedDocumentColumns"/>; <see cref="RefuseAppOnlyWrite"/>).
    /// </summary>
    internal const string AppOnlyDocumentBffColumnWriteReasonCode = "sdap.access.document_bff_column_server_only";

    /// <summary>
    /// #2240: the document columns only the BFF's own typed writers write. The generic writers run AS the BFF, so field
    /// security does not stop them; this refusal does. None is a lookup, and the refusal's normalization covers every form
    /// anyway. <c>scripts/Set-DocumentPointerFieldSecurity.ps1</c> (target DocumentPointers) lists five of them and
    /// field-secures four: <c>sprk_graphdriveid</c>, <c>sprk_graphitemidbound</c>, <c>sprk_relocationpending</c> and
    /// <c>sprk_relocatedversions</c>. It lists <c>sprk_graphitemid</c> but skips it (an alternate key cannot be secured), and
    /// it does not list <c>sprk_driveitemid</c>, <c>sprk_filepath</c>, <c>sprk_emailmessageid</c> or
    /// <c>sprk_isemailarchive</c>; for those five this refusal is the only lock on these paths. Every column the script
    /// lists must be in this set (<c>DocumentBffOwnedColumnAppOnlyWriterTests</c> reads the script).
    /// <list type="bullet">
    /// <item><c>sprk_graphdriveid</c>, <c>sprk_graphitemid</c>: the SharePoint Embedded pointer the BFF follows as the
    /// application (task 166, owner round 21). <c>sprk_graphitemid</c> cannot be field-secured (alternate key).</item>
    /// <item><c>sprk_graphitemidbound</c>: the BFF-only copy of the item id the pointer check compares with
    /// (<see cref="DocumentPointerBinding"/>, task 171). Setting it alone makes every app-only read refuse the document;
    /// setting it with the item id forges a pointer that passes the check.</item>
    /// <item><c>sprk_driveitemid</c>: the legacy copy of the item id, and an access key. A caller passes the retrieval trim
    /// (<c>RetrievalRecordKey.DocumentByDriveItem</c>) and the AI filter's item check
    /// (<c>AiAuthorizationFilter.IsSpeItemShownToCallerAsync</c>) when they can read ANY document whose
    /// <c>sprk_driveitemid</c> equals the item, so setting it on a document a partner can read shows them another
    /// document's content. Its only writer is the relocator's re-key (<c>DocumentContainerRelocator.ReKeyedOwnColumns</c>,
    /// a typed app-only update that does not ask this refusal).</item>
    /// <item><c>sprk_relocationpending</c>: the relocator's ledger; it names a source the BFF may later delete (task 166
    /// f1-v1). <c>sprk_relocatedversions</c>: the relocation's version record (task 166 f1-v2).</item>
    /// <item><c>sprk_filepath</c>: the item's web URL, written with the pointer (a resource-naming field the document
    /// update endpoint refuses from a caller, <c>DataverseDocumentsEndpoints.ResourceNamingFieldsIn</c>).</item>
    /// <item><c>sprk_emailmessageid</c>, <c>sprk_isemailarchive</c>: the archive document's recorded key and its archive
    /// marker. The task 190 sweep (<c>EmailArchiveCommunicationLinkSweepJob</c>) turns them into a D7 link
    /// (<c>sprk_relatedcommunication</c>), so writing them can link a document to a communication of the writer's
    /// choosing.</item>
    /// </list>
    /// The owners' constants where they are public, so the set cannot drift from them.
    /// </summary>
    internal static readonly IReadOnlySet<string> BffOwnedDocumentColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_graphdriveid",
        DocumentPointerBinding.ItemIdColumn,
        DocumentPointerBinding.BoundItemIdColumn,
        "sprk_driveitemid",
        Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator.RelocationLedgerColumn,
        Sprk.Bff.Api.Services.Documents.RelocatedVersionHistory.Column,
        "sprk_filepath",
        Sprk.Bff.Api.Services.Communication.CrossPathLink.EmailMessageIdAttribute,
        Sprk.Bff.Api.Services.Communication.CrossPathLink.IsEmailArchiveAttribute,
    };

    /// <summary>
    /// #2247: the reason code when an app-only generic writer names a row's owner (<see cref="OwnerColumns"/>;
    /// <see cref="RefuseAppOnlyWrite"/>), on any table.
    /// </summary>
    internal const string AppOnlyOwnerWriteReasonCode = "sdap.access.owner_server_only";

    /// <summary>
    /// #2247: the reason code when an app-only generic writer names a row's creator (<see cref="CreatorColumns"/>;
    /// <see cref="RefuseAppOnlyWrite"/>), on any table.
    /// </summary>
    internal const string AppOnlyCreatorWriteReasonCode = "sdap.access.creator_server_only";

    /// <summary>
    /// #2247: a row's owner, in every column Dataverse names it by. The owner decides who may read the row, and the BFF app
    /// user is a System Administrator, so an app-only write of <c>ownerid</c> re-owns any row (permanently on a standard
    /// record; under a secure root the reconciler puts it back within minutes). Its writers are the BFF's typed ones (the
    /// create's owner step, the reparent, the reconciler, Repair's owner step), none of which asks this refusal over it. The
    /// other three are read-only in Dataverse; they are listed so a configuration naming them is refused, not sent.
    /// </summary>
    internal static readonly IReadOnlySet<string> OwnerColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ownerid",
        "owninguser",
        "owningteam",
        "owningbusinessunit",
    };

    /// <summary>
    /// #2247: a row's creator. <c>sprk_createdbyperson</c> (<see cref="RecordCreatorPerson.Column"/>, on 38 tables on dev) is read by
    /// the move-out-of-secure creator check (<c>RecordOwnershipResolver</c>), provisioning resume, event visibility and the
    /// document-pointer creator rule; <c>sprk_createdbycontact</c> (field-secured, the BFF its only writer) by the external
    /// delete-own check; <c>sprk_requestedby</c> (a service request's requester, a contact) by the external Service Requests
    /// module's scope, message and document access on a service request, and the requester-only hard delete
    /// (<c>ExternalProjectDataEndpoints</c>). Their writers are the create paths' typed stamps (for the requester, the
    /// intake create, <c>ExternalDataService.BuildServiceRequestCreatePayload</c>), none of which uses these paths. Every
    /// column of <c>ExternalDataService.CreatorColumns</c> is refused here or is a platform audit column
    /// (<c>OwnerCreatorColumnAppOnlyWriterTests</c>).
    /// </summary>
    internal static readonly IReadOnlySet<string> CreatorColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        RecordCreatorPerson.Column,
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService.CreatedByContactColumn,
        "sprk_requestedby",
    };

    /// <summary>
    /// Task 189 fix round C (K5): an APP-ONLY generic writer never writes a document's <see cref="Column"/> or its
    /// own-narrowing record (<see cref="AccessInheritance.Column"/>). These writers are the Update Record playbook node and
    /// the action seam when they act for no person (<c>UpdateRecordActionCore</c>), and an AI output mapping
    /// (<c>DataverseUpdateHandler</c>). A model or a configuration builds their payload, not a person. Only
    /// <see cref="DecideDocument"/>'s writers keep the value at max(own, inherited). A payload that lowered it, or rewrote
    /// the record, would show a user's Restricted document to partners (D7, task 188). This is the same refusal
    /// <c>ChildRecordEndpoints.RootServerOwnedColumns</c> makes for a work assignment. A user's own write is not refused: the
    /// form, the user-OBO chat tools, and an update impersonating its user all run as that user. The field-mapping push also
    /// runs as its caller, but its rules are a configuration applied to every child it reaches, so it asks this too and
    /// skips such a rule on a document (the rest of the push applies; security pass 2).
    /// <para>Task 188 round 4e: the same writers never write a document's D7 link columns either
    /// (<see cref="DocumentLinkColumns"/>, refused with <see cref="AppOnlyDocumentLinkWriteReasonCode"/>), in any form: set,
    /// cleared (a null), <c>{nav}@odata.bind</c> or <c>_{column}_value</c>, any case. Legitimate link changes go through the
    /// re-file routes (<c>RecordOwnershipResolver.ReparentAsync</c>, caller-checked), the Office create (I-19, the link in the
    /// create) and the BFF's own typed writers, none of which uses these paths. Every caller of the generic update is held to
    /// this by <c>ExternalDocumentNarrowingGuardTests</c> (round 4e).</para>
    /// <para>#2240: nor a document's BFF-owned columns (<see cref="BffOwnedDocumentColumns"/>: its file pointer, binding and
    /// legacy item id, relocation record, file path and email-archive key), refused with
    /// <see cref="AppOnlyDocumentBffColumnWriteReasonCode"/> in the same forms. Their writers are the BFF's typed ones (the upload and Office persistence, the pointer binder, the
    /// relocator, the email archive), none of which uses these paths.</para>
    /// <para>#2247: on EVERY table, nor a row's owner (<see cref="OwnerColumns"/>, refused with
    /// <see cref="AppOnlyOwnerWriteReasonCode"/>) or its creator (<see cref="CreatorColumns"/>, refused with
    /// <see cref="AppOnlyCreatorWriteReasonCode"/>), in the same forms. A writer that decides one of these itself (Repair's owner
    /// step) asks this refusal only over the columns a configuration gave it, never over the one it decided.</para>
    /// <para><b>Precedence</b> (the first match decides the code): on a document, its Access Permission or own-narrowing
    /// record, then its D7 links, then its BFF-owned columns; then, on every table, the owner, then the creator.</para>
    /// </summary>
    /// <returns>The refusal (nothing is written), or <see langword="null"/> when the write may proceed.</returns>
    internal static RecordOwnerResolution? RefuseAppOnlyWrite(string? table, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var trimmed = table?.Trim();
        var narrows = RecordsOwnNarrowing(trimmed);
        var links = string.Equals(trimmed, Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDocumentNarrowing.DocumentTable, StringComparison.OrdinalIgnoreCase);

        var columns = keys.Select(SecureRootInheritance.NormalizeColumn).ToList();
        if (narrows && columns.FirstOrDefault(k => k is Column or AccessInheritance.Column) is { } named)
        {
            return RecordOwnerResolution.Refused(AppOnlyDocumentWriteReasonCode,
                $"{named} of a document is set by Spaarke (it keeps the document's own narrowing), so it was not written");
        }

        var link = links ? columns.FirstOrDefault(DocumentLinkColumns.Contains) : null;
        if (link is not null)
        {
            return RecordOwnerResolution.Refused(AppOnlyDocumentLinkWriteReasonCode,
                $"{link} of a document is set by Spaarke (it decides whether partners may see the document), so it was not written");
        }

        // #2240: the document's pointer, relocation and email-archive columns are the BFF's own (BffOwnedDocumentColumns).
        var owned = links ? columns.FirstOrDefault(BffOwnedDocumentColumns.Contains) : null;
        if (owned is not null)
        {
            return RecordOwnerResolution.Refused(AppOnlyDocumentBffColumnWriteReasonCode,
                $"{owned} of a document is set by Spaarke (it names the document's file or its email), so it was not written");
        }

        // #2247: on every table, the row's owner and its creator are the BFF's typed writers' (OwnerColumns, CreatorColumns).
        if (columns.FirstOrDefault(OwnerColumns.Contains) is { } owner)
        {
            return RecordOwnerResolution.Refused(AppOnlyOwnerWriteReasonCode,
                $"{owner} of a record is set by Spaarke (it decides who may see the record), so it was not written");
        }

        var creator = columns.FirstOrDefault(CreatorColumns.Contains);
        return creator is null
            ? null
            : RecordOwnerResolution.Refused(AppOnlyCreatorWriteReasonCode,
                $"{creator} of a record is set by Spaarke (it records who created the record), so it was not written");
    }

    /// <summary>What to hold and what to write on one row of a <see cref="RecordTables"/> table (task 189).</summary>
    internal sealed record DocumentDecision
    {
        /// <summary>
        /// Not null: the row's own narrowing, or its stored level, cannot be decided (fail closed). Holds the reason, which
        /// names no person; callers log it and look at the row again. Causes: its record cannot be read, or its stored value is
        /// not a known level. Its record is never written, and its value only by a raise (<see cref="RaiseOnly"/>).
        /// </summary>
        public string? Undetermined { get; init; }

        /// <summary>
        /// Task 189 fix round C (K1+K2): the row's own narrowing is not known (its record cannot be read, or this environment
        /// has no record column), so its value is only RAISED to what its parents give, never lowered, and its record is
        /// never written (<see cref="Record"/> is null). <see cref="Own"/> is null: it is not known.
        /// </summary>
        public bool RaiseOnly { get; init; }

        /// <summary>The value the row holds after the decision: max(own, inherited), or its own value when it has no parent.</summary>
        public int Target { get; init; }

        /// <summary>The stored value differs from <see cref="Target"/>, so it is written. Never on a parentless row.</summary>
        public bool WriteValue { get; init; }

        /// <summary>The record to write in the same update, or <see langword="null"/> when the stored one is current.</summary>
        public AccessInheritance? Record { get; init; }

        /// <summary>The row's own narrowing after the decision (<see langword="null"/>: it follows its parents).</summary>
        public int? Own { get; init; }

        /// <summary>The own narrowing holds the value above what the parents give: the parents' value was NOT written over it.</summary>
        public bool OwnKept { get; init; }
    }

    /// <summary>
    /// Task 189 (D7 T7a): the ONE decision for a document's Access Permission, made by both of its BFF writers (the
    /// reconcile job and the child-records create refresh). The stored value is max(own, inherited): inherited values follow
    /// the parents both ways, and an own narrowing is never widened.
    /// </summary>
    /// <remarks>
    /// <para><b>Own vs inherited</b> is task 175's classification, one copy (<see cref="AccessInheritance.OwnPermissionOf"/>):
    /// a value set outside these writers (a user's edit, a BFF writer's narrowing) becomes the document's own when it is
    /// stricter than the last inherited value or the live one; an edit at or below both removes the own narrowing and is put
    /// back to the live inherited value. So a user's Limited set after a Restricted matter widened to Standard stays Limited
    /// (the task 189 fix round's "K1 variant"). Without a
    /// record (existing rows) a value stricter than the current inherited one is own, and one equal to it is inherited. That
    /// is sound because task 173 kept every parented row equal to its parents before this task.</para>
    /// <para><b>A re-file follows the new parents.</b> The task 173 contract is kept for every row without an own narrowing
    /// (unlike task 175's "a re-file never loosens", which is for work assignments and projects).</para>
    /// <para><b>A parentless row</b> keeps its own value, which is never written (round 81). When that value narrows, it is
    /// RECORDED as its own, so a later filing keeps it.</para>
    /// <para><b>Fail closed, never below the floor</b> (fix round C, K1+K2): an unreadable record decides no own narrowing
    /// (<see cref="DocumentDecision.Undetermined"/>), so its row is decided by <see cref="DecideDocumentRaiseOnly"/>: only
    /// raised to the live floor, and its record left as it is. A stored value that is not a known level decides
    /// nothing.</para>
    /// <para><b>Every parented row gets a record</b> (fix round C, F1). Without one, a user's narrowing made in the same window
    /// as its parent tightening to the same level reads as inherited, and the document follows the parent's later widening.
    /// With the record (the last inherited value) that edit is stricter than it, so it is the user's own. Each row without a
    /// record is written once (the sweep reaches the legacy rows), then not again while nothing changes.</para>
    /// <para><b>A provisional narrowing is released by a link</b> (owner round 122). The save-time Restricted of an email that
    /// could not be identified (<see cref="AccessInheritance.OwnProvisional"/>) counts as no own value once
    /// <paramref name="communicationLinked"/> is true (<see cref="AccessInheritance.OwnPermissionOf(int, int?, AccessInheritance?, int, bool, out bool)"/>,
    /// the one place). The decision then writes the inherited value and a record with no own value and no marker, in one
    /// update; the release is one way. While it stands untouched, every record written keeps the marker; any other change of
    /// the own value drops it.</para>
    /// </remarks>
    /// <param name="stored">The row's stored <see cref="Column"/>.</param>
    /// <param name="recordText">The row's stored <see cref="AccessInheritance.Column"/>.</param>
    /// <param name="parents">The row's non-null filing parents (<see cref="ParentLineageWalk.ParentsOfAsync"/>).</param>
    /// <param name="inherited">What its parents give it (<see cref="ResolveAsync"/>); ignored when it has none.</param>
    /// <param name="communicationLinked">Owner round 122: the communication walk found a link for this document
    /// (<c>ExternalDocumentNarrowing.FindCommunicationLinksAsync</c>). False when it found none, could not decide, or was not
    /// asked; then nothing is released.</param>
    internal static DocumentDecision DecideDocument(
        int? stored, string? recordText, IReadOnlyCollection<(string Table, Guid Id)> parents, int? inherited,
        bool communicationLinked = false)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var (record, unreadable) = AccessInheritance.Parse(recordText);
        if (unreadable)
            return DecideDocumentRaiseOnly(stored, parents, inherited, $"its own narrowing ({AccessInheritance.Column}) cannot be read");

        var storedRank = SecureRootInheritance.RankOf(stored);
        if (storedRank < 0)
            return new DocumentDecision { Undetermined = UnknownStoredLevel };

        if (parents.Count == 0)
        {
            var ownValue = storedRank > 0 ? stored : null;
            var parentless = new AccessInheritance(Array.Empty<string>(), false, Standard, false, ownValue);
            // Round 122: a parentless row has no link, so a provisional narrowing is never released here. It stays
            // provisional while its value is untouched.
            if (record is { OwnProvisional: true } && record.OwnPermission == ownValue)
                parentless = record.KeepingProvisionalWith(parentless);
            var writeParentless = record is null ? ownValue is not null : !parentless.SameAs(record);
            return new DocumentDecision
            {
                Target = SecureRootInheritance.PermissionOf(storedRank),
                Record = writeParentless ? parentless : null,
                Own = ownValue,
            };
        }

        var floorRank = SecureRootInheritance.RankOf(inherited);
        if (inherited is null || floorRank < 0)
            return new DocumentDecision { Undetermined = "what its parents give it could not be decided" };

        var own = AccessInheritance.OwnPermissionOf(
            storedRank, stored, record, floorRank, communicationLinked, out var stillProvisional);
        var targetRank = Math.Max(SecureRootInheritance.RankOf(own), floorRank);
        var target = SecureRootInheritance.PermissionOf(targetRank);
        var next = new AccessInheritance(
            parents.Select(p => $"{p.Table.Trim().ToLowerInvariant()}:{p.Id:D}")
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(k => k, StringComparer.Ordinal).ToList(),
            false, SecureRootInheritance.PermissionOf(floorRank), false, own);
        if (stillProvisional && record is not null)
            next = record.KeepingProvisionalWith(next); // round 122: unreleased and untouched, so it stays provisional
        var writeRecord = record is null || !next.SameAs(record);
        return new DocumentDecision
        {
            Target = target,
            WriteValue = stored != target,
            Record = writeRecord ? next : null,
            Own = own,
            OwnKept = targetRank > floorRank,
        };
    }

    private const string UnknownStoredLevel = "it holds an Access Permission that is not Standard, Limited or Restricted";

    /// <summary>
    /// Task 189 fix round C (K1+K2): the decision for a document whose own narrowing cannot be known: its record cannot be
    /// read (not JSON, another version, or a level inside it that is not known), or this environment has no record column
    /// (the schema script has not run). Whatever its own narrowing is, max(own, inherited) is at least what its parents give,
    /// so a stored value BELOW a decided floor is raised to the floor. Nothing else is written: a value at or above the floor
    /// is kept (it may be the user's own narrowing), nothing is ever lowered, and the record is never written (an unreadable
    /// one is left for a person to look at).
    /// </summary>
    /// <param name="stored">The row's stored <see cref="Column"/>. A level that is not known decides nothing.</param>
    /// <param name="parents">The row's non-null filing parents. A parentless row has no floor: nothing is written.</param>
    /// <param name="inherited">What its parents give it (<see cref="ResolveAsync"/>).</param>
    /// <param name="undecided">Why the own narrowing is not known, reported as <see cref="DocumentDecision.Undetermined"/>
    /// (the row is looked at again). Null when the reason is the environment (no record column): that is reported once per
    /// run, not per row.</param>
    internal static DocumentDecision DecideDocumentRaiseOnly(
        int? stored, IReadOnlyCollection<(string Table, Guid Id)> parents, int? inherited, string? undecided)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var storedRank = SecureRootInheritance.RankOf(stored);
        if (storedRank < 0)
            return new DocumentDecision
            {
                Undetermined = undecided is null ? UnknownStoredLevel : $"{undecided}, and {UnknownStoredLevel}",
                RaiseOnly = true,
            };

        var floorRank = SecureRootInheritance.RankOf(inherited);
        if (parents.Count > 0 && (inherited is null || floorRank < 0))
            return new DocumentDecision
            {
                Undetermined = undecided is null
                    ? "what its parents give it could not be decided"
                    : $"{undecided}, and what its parents give it could not be decided",
                Target = SecureRootInheritance.PermissionOf(storedRank),
                RaiseOnly = true,
            };

        var raise = parents.Count > 0 && storedRank < floorRank;
        return new DocumentDecision
        {
            Undetermined = undecided,
            Target = SecureRootInheritance.PermissionOf(raise ? floorRank : storedRank),
            WriteValue = raise,
            RaiseOnly = true,
        };
    }
}
