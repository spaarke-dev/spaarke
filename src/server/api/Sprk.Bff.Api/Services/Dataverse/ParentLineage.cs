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
/// <para><b>Display only.</b> No access decision reads a child table's value (round 2 Q6; round 81): enforcement keeps
/// reading the roots. A parentless child's own value is recorded only (owner, 2026-10-08: "This is fine (BUT in future we
/// might need to revisit)").</para>
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
}
