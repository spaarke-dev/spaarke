using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b (design.md:414, uac-r2 ruling 2026-11-08): the ONE answer to "which invoices does a row of table T link to",
/// for the rule "a partner sees only invoices of its own firms" on rows that belong to an invoice without being in the invoice family
/// (<see cref="ExternalInvoiceVisibility.FamilyTables"/>, which stays closed): to-dos, events, documents, communications, analyses, memos
/// and every other table with an edge to <c>sprk_invoice</c>. A row is hidden when ANY linked invoice is not visible (each invoice gets
/// the full invoice decision), and is decided by its invoice even when it is also filed under a shared matter. Both the assistant
/// tools and the external routes call this; neither keeps its own table-to-column list.
/// </summary>
/// <remarks>
/// <para><b>Derived, never hand-written.</b> A table's invoice link columns are every edge to <c>sprk_invoice</c> in the edge registry
/// (<see cref="RecordFamilyEdges.All"/>, Filing edges) together with every lookup to <c>sprk_invoice</c> in
/// <see cref="SecureChildLineage.Children"/> (which also lists a submission's <c>sprk_invoice</c>, an output the registry leaves out).
/// A document's columns therefore come from <c>DocumentLinkFields</c> through both (<c>sprk_invoice</c> and <c>sprk_relatedinvoice</c>).
/// How a row is read (entity set and id column) comes from <see cref="SecureChildLineage.Children"/>, or, for a root, from
/// <see cref="ExternalGrantRoot"/>. A table with an invoice edge but no known read shape is still listed (<see cref="HasInvoiceLink"/> is
/// true) and its read THROWS, so it fails closed; <c>InvoiceLinksTests</c> fails the build on such a table.</para>
/// <para><b>Fail closed.</b> A row that does not come back, and a link that is present but not a GUID, read as
/// <see cref="ExternalDocumentNarrowing.UnreadableLink"/>: an invoice that cannot be decided, so the caller hides the row. A failed read
/// THROWS. A row with no invoice link reads as an empty list (the rule does not apply to it).</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalInvoiceVisibility.ReadInvoicesOfAsync"/> maps
/// the closed invoice family only, with one hand-listed link per table; the edge registries list every invoice edge but read nothing.
/// (2) Extension — widening <see cref="ExternalInvoiceVisibility.FamilyTables"/> would change the closed taint list and every guard that
/// scans for its table names; this reads the registries instead, beside the predicate. (3) Cost of doing nothing — a to-do, event,
/// document or message about another firm's invoice reaches a partner through the assistant tools and the external routes.</para>
/// </remarks>
internal static class InvoiceLinks
{
    /// <summary>How a table that links to an invoice is read: its entity set and id column (null when unknown: the read throws), and
    /// its invoice link columns (logical names).</summary>
    internal sealed record LinkedTable(string LogicalName, string? EntitySet, string IdColumn, IReadOnlyList<string> LinkColumns)
    {
        /// <summary>The Web API value columns of <see cref="LinkColumns"/> (<c>_col_value</c>).</summary>
        internal IReadOnlyList<string> ValueColumns => LinkColumns.Select(c => $"_{c}_value").ToArray();
    }

    /// <summary>Every table with at least one invoice link, keyed by logical name.</summary>
    internal static readonly IReadOnlyDictionary<string, LinkedTable> Tables =
        Build(RecordFamilyEdges.Of(RecordFamilyEdges.FamilyKind.Filing), SecureChildLineage.Children);

    /// <summary>True when <paramref name="table"/> has at least one invoice link.</summary>
    internal static bool HasInvoiceLink(string? table) => table is not null && Tables.ContainsKey(table);

    /// <summary>The invoice link columns of <paramref name="table"/> (logical names); empty when it has none.</summary>
    internal static IReadOnlyList<string> LinkColumnsOf(string? table) =>
        table is not null && Tables.TryGetValue(table, out var linked) ? linked.LinkColumns : Array.Empty<string>();

    /// <summary>
    /// The tables with an invoice link, from <paramref name="edges"/> and <paramref name="lineage"/> (the registries; a parameter so the
    /// guard can seed an edge). Columns keep the registries' order, the edge registry first.
    /// </summary>
    internal static IReadOnlyDictionary<string, LinkedTable> Build(
        IEnumerable<RecordFamilyEdges.Edge> edges, IReadOnlyDictionary<string, SecureChildLineage.Table> lineage)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(lineage);
        var columns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string table, string column)
        {
            if (!columns.TryGetValue(table, out var list))
                columns[table] = list = new List<string>();
            if (!list.Contains(column, StringComparer.OrdinalIgnoreCase))
                list.Add(column);
        }

        foreach (var edge in edges.Where(e => IsInvoice(e.ParentTable)))
            Add(edge.ChildTable, edge.LookupColumn);
        foreach (var table in lineage.Values)
        {
            foreach (var column in table.LookupOrder.Where(c => table.Lookups.TryGetValue(c, out var target) && IsInvoice(target)))
                Add(table.LogicalName, column);
        }

        return columns.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var (entitySet, idColumn) = ReadShapeOf(kv.Key, lineage);
                return new LinkedTable(kv.Key, entitySet, idColumn, kv.Value.ToArray());
            },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The invoices each row of <paramref name="rowIds"/> (rows of <paramref name="table"/>) links to, through every invoice link column,
    /// read BY ID through <paramref name="query"/> in one read per chunk of <see cref="CommunicationOpenThreadGate.ChunkSize"/>. An empty list:
    /// the row links to no invoice. <see cref="ExternalDocumentNarrowing.UnreadableLink"/> in the list: a link that cannot be decided (the
    /// row did not come back, or a link is not a GUID). A failed read THROWS, as does a table with no invoice link or no known read shape.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ReadLinkedInvoicesAsync(
        string table, IReadOnlyCollection<Guid> rowIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(query);
        if (table is null || !Tables.TryGetValue(table, out var linked))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table with an invoice link.");
        if (linked.EntitySet is null)
            throw new InvalidOperationException($"No read shape for {linked.LogicalName}; its invoice links cannot be read (fail closed).");

        var distinct = rowIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var found = new Dictionary<Guid, IReadOnlyList<Guid>>();
        if (distinct.Count == 0)
            return found;

        var values = linked.ValueColumns;
        var select = string.Join(",", new[] { linked.IdColumn }.Concat(values));
        var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(distinct, (chunk, c) => query(linked.EntitySet,
            $"$select={select}" +
            $"&$filter={string.Join(" or ", chunk.Select(id => $"{linked.IdColumn} eq {id:D}"))}&$top={chunk.Length}", c), ct)
            .ConfigureAwait(false);
        foreach (var (chunk, rows) in chunks)
        {
            var inChunk = chunk.ToHashSet();
            foreach (var row in rows)
            {
                if (ExternalInvoiceVisibility.LinkOf(row, linked.IdColumn) is not { } id || !inChunk.Contains(id))
                    continue;
                found[id] = values.Select(column => ExternalInvoiceVisibility.LinkOf(row, column)).OfType<Guid>().Distinct().ToList();
            }
        }

        // A row that did not come back links to invoices that cannot be decided.
        IReadOnlyList<Guid> undecidable = new[] { ExternalDocumentNarrowing.UnreadableLink };
        return distinct.ToDictionary(id => id, id => found.TryGetValue(id, out var invoices) ? invoices : undecidable);
    }

    private static bool IsInvoice(string? table) =>
        string.Equals(table, ExternalInvoiceVisibility.InvoiceTable, StringComparison.OrdinalIgnoreCase);

    private static (string? EntitySet, string IdColumn) ReadShapeOf(
        string table, IReadOnlyDictionary<string, SecureChildLineage.Table> lineage)
    {
        if (lineage.TryGetValue(table, out var child))
            return (child.EntitySet, child.IdColumn);
        foreach (var root in Enum.GetValues<ExternalGrantRootType>())
        {
            if (string.Equals(ExternalGrantRoot.LogicalNameFor(root), table, StringComparison.OrdinalIgnoreCase))
                return (ExternalGrantRoot.BindFor(root).EntitySet, table + "id");
        }

        return (null, table + "id");
    }
}
