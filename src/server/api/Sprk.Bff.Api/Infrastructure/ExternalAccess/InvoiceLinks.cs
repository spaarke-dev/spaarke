using System.Text.Json;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b F3b: the invoice links of the rows above each row of a FetchXML (module) page of <c>table</c>, by position,
/// through every parent edge of the table (the document's parent document and, since fix round 1, its related rows;
/// <see cref="ExternalDataService.ReadModuleDocumentAncestorLinksAsync"/>). <c>Failed</c>: the chain read failed, so every row that has a
/// parent is undecidable.
/// </summary>
internal delegate Task<(IReadOnlyList<InvoiceLinks.RowInvoiceLinks> Chains, bool Failed)> ModuleDocumentChainReader(
    string table, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string idAttribute, CancellationToken ct);

/// <summary>
/// ebilling-r1 task 130-0b F3b fix round 1: the invoices of the rows above each Web API row of <c>table</c> that a REST read has already
/// read, by position (<see cref="ExternalDataService.ReadAncestorLinksOfRowsAsync"/>): an empty list (no parent), the ancestors'
/// invoices (with <see cref="ExternalDocumentNarrowing.UnreadableLink"/> when undecidable), or <see langword="null"/> when the chain read
/// failed for a row that has a parent (the caller withholds it and says truncated).
/// </summary>
internal delegate Task<IReadOnlyList<IReadOnlyList<Guid>?>> InvoiceAncestorReader(
    string table, IReadOnlyList<(Guid Id, IReadOnlyDictionary<string, JsonElement>? Row)> rows, CancellationToken ct);

/// <summary>
/// ebilling-r1 task 130-0b (design.md:414, uac-r2 ruling 2026-11-08): the ONE answer to "which invoices does a row of table T link to",
/// for the rule "a partner sees only invoices of its own firms" on rows that belong to an invoice without being in the invoice family
/// (<see cref="ExternalInvoiceVisibility.FamilyTables"/>, which stays closed): to-dos, events, documents, communications, analyses, memos
/// and every other table with an edge to <c>sprk_invoice</c>. A row is hidden when ANY linked invoice is not visible (each invoice gets
/// the full invoice decision), and is decided by its invoice even when it is also filed under a shared matter. Both the assistant
/// tools and the external routes call this; neither keeps its own table-to-column list. A ROOT (<see cref="LinkedTable.IsRoot"/>: a work
/// assignment, matter or project) is never hidden by an invoice link: a root is decided by its grant, and its invoice link is shown only
/// when that invoice is visible (otherwise every form of the link is blanked; uac-r2 ruling 2026-11-08).
/// </summary>
/// <remarks>
/// <para><b>Derived, never hand-written.</b> A table's invoice link columns are every edge to <c>sprk_invoice</c> in the edge registry
/// (<see cref="RecordFamilyEdges.All"/>, Filing edges) together with every lookup to <c>sprk_invoice</c> in
/// <see cref="SecureChildLineage.Children"/> (which also lists a submission's <c>sprk_invoice</c>, an output the registry leaves out).
/// A document's columns therefore come from <c>DocumentLinkFields</c> through both (<c>sprk_invoice</c> and <c>sprk_relatedinvoice</c>).
/// How a row is read (entity set and id column) comes from <see cref="SecureChildLineage.Children"/>, or, for a root, from
/// <see cref="ExternalGrantRoot"/>. A table with an invoice edge but no known read shape is still listed (<see cref="HasInvoiceLink"/> is
/// true) and its read THROWS, so it fails closed; <c>InvoiceLinksTests</c> fails the build on such a table.</para>
/// <para><b>Descendants (ebilling-r1 task 130-0b F3b).</b> A row that has no invoice link of its own but is filed UNDER an
/// invoice-linked row (an analysis output under its analysis, an event log under its event, a file version under its document, an
/// attachment or email artifact under its document, ...) is decided by its parent's invoice links, and a child document by EVERY invoice
/// link up its <c>sprk_parentdocument</c> chain, bounded by <see cref="MaxWalkHops"/> (its own bound, EB-D-215; the task 188 narrowing
/// keeps <see cref="ExternalDocumentNarrowing.MaxHops"/>, and the two are independent). The edge set is derived from the filing edges (<see cref="Descendants"/>, <see cref="BuildDescendants"/>), never listed by hand: a
/// table with no invoice link of its own, not a root and not in the invoice family, that has a Filing edge to an invoice-linked child
/// table (or to another descendant). A cycle, a chain past the bound, a parent that does not come back, an unreadable parent link and an
/// unreadable parent column each make the row undecidable (<see cref="RowInvoiceLinks.ChainUndecidable"/>, and
/// <see cref="ExternalDocumentNarrowing.UnreadableLink"/> in the flat list): the caller hides the row, and a single read answers
/// "could not decide". Only edges whose parent is an invoice-linked child or a descendant are followed: a root is decided by its grant.</para>
/// <para><b>Supplemental links.</b> <see cref="Supplemental"/> lists an invoice link the edge registries do not carry (a service request
/// is deliberately outside the Secure lineage map). The K-a guard (<c>InvoiceLookupCoverageGuardTests</c>) compares every lookup to
/// <c>sprk_invoice</c> in the solution source with this class, so a lookup missing from both fails the build.</para>
/// <para><b>Fail closed.</b> A row that does not come back, and a link that is present but not a GUID, read as
/// <see cref="ExternalDocumentNarrowing.UnreadableLink"/>: an invoice that cannot be decided, so the caller hides the row. A failed read
/// THROWS. A row with no invoice link reads as an empty list (the rule does not apply to it).</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="ExternalInvoiceVisibility.ReadInvoicesOfAsync"/> maps
/// the closed invoice family only, with one hand-listed link per table; the edge registries list every invoice edge but read nothing.
/// (2) Extension — widening <see cref="ExternalInvoiceVisibility.FamilyTables"/> would change the closed taint list and every guard that
/// scans for its table names; this reads the registries instead, beside the predicate. The descendant walk extends the same reader
/// (one more hop per level) instead of adding a second one. (3) Cost of doing nothing — a to-do, event, document or message about
/// another firm's invoice reaches a partner through the assistant tools and the external routes, and so does a file version, an
/// analysis output or a child document of a document that belongs to it.</para>
/// </remarks>
internal static class InvoiceLinks
{
    /// <summary>
    /// EB-D-215 (owner 2026-11-10, conditions from uac-r2): the most parent hops the invoice walk takes up from a row, on both
    /// planes (<see cref="ReadRowInvoiceLinksAsync"/> and the REST <see cref="ReadAncestorInvoiceLinksAsync"/>). Only hops between
    /// child rows count: a core record other than a service request is never walked; a service request follows the child rule
    /// (<see cref="Supplemental"/>), so it is walked and its hop counts toward the bound. A row whose chain needs a hop past this bound is undecidable
    /// (hidden, and logged once). Independent of the task 188 narrowing's <see cref="ExternalDocumentNarrowing.MaxHops"/>.
    /// </summary>
    internal const int MaxWalkHops = 5;

    /// <summary>How a table that links to an invoice is read: its entity set and id column (null when unknown: the read throws), its
    /// invoice link columns (logical names), and whether it is a ROOT (uac-r2 ruling 2026-11-08: a root is decided by its grant and is never
    /// hidden by an invoice link; its invoice link is shown only when that invoice is visible, otherwise blanked). A child row is hidden.</summary>
    internal sealed record LinkedTable(
        string LogicalName, string? EntitySet, string IdColumn, IReadOnlyList<string> LinkColumns, bool IsRoot = false)
    {
        /// <summary>The Web API value columns of <see cref="LinkColumns"/> (<c>_col_value</c>).</summary>
        internal IReadOnlyList<string> ValueColumns => LinkColumns.Select(c => $"_{c}_value").ToArray();
    }

    /// <summary>An invoice link the edge registries do not carry: the table, its entity set and the lookup column (logical names).</summary>
    internal sealed record SupplementalLink(string Table, string EntitySet, string Column);

    /// <summary>
    /// F3b: a service request links to an invoice (<c>sprk_servicerequest.sprk_regardinginvoice</c>), but a service request is a root of
    /// its own that the Secure lineage map leaves out, and the filing edges list it only as a PARENT. It follows the child rule on both
    /// planes (uac-r2 ruling), so its link is listed here; the K-a guard fails the build if this list and the registries together miss a
    /// lookup to <c>sprk_invoice</c> in the solution source.
    /// </summary>
    internal static readonly IReadOnlyList<SupplementalLink> Supplemental =
        new[] { new SupplementalLink("sprk_servicerequest", "sprk_servicerequests", "sprk_regardinginvoice") };

    /// <summary>One edge a descendant's decision follows UP: the lookup column on the descendant and the table it names.</summary>
    internal sealed record ParentEdge(string Column, string ParentTable)
    {
        /// <summary>The Web API value form of <see cref="Column"/> (<c>_col_value</c>).</summary>
        internal string ValueColumn => $"_{Column}_value";
    }

    /// <summary>A table decided through its parent edges (F3b): how it is read and the edges followed up from it. <c>sprk_document</c> is
    /// both an invoice-linked child (its own links, <see cref="Tables"/>) and a descendant (its parent document chain).</summary>
    internal sealed record DescendantTable(string LogicalName, string? EntitySet, string IdColumn, IReadOnlyList<ParentEdge> Parents);

    /// <summary>What a row's invoice decision runs on: the invoices it links to (its own and its ancestors') and whether the chain it
    /// hangs from could be decided. When undecidable the list also carries <see cref="ExternalDocumentNarrowing.UnreadableLink"/>, and
    /// <paramref name="Reasons"/> says why (set by the walk; a caller's own fail-closed answer may leave it
    /// <see cref="UndecidableReason.None"/>).</summary>
    internal sealed record RowInvoiceLinks(
        IReadOnlyList<Guid> Invoices, bool ChainUndecidable, UndecidableReason Reasons = UndecidableReason.None);

    /// <summary>EB-D-215 (C1): why the walk could not decide a row's chain, kept apart so a chain past the bound is reported on its
    /// own (<see cref="WalkPastTheBoundEvent"/>) and never confused with a cycle or an unreadable link.</summary>
    [Flags]
    internal enum UndecidableReason
    {
        /// <summary>Decided.</summary>
        None = 0,

        /// <summary>A link or parent column that was not read or is not a GUID, or a parent that did not come back.</summary>
        Unreadable = 1,

        /// <summary>A parent already on the row's own path (a row that is its own parent included).</summary>
        Cycle = 2,

        /// <summary>The chain needs a hop past <see cref="MaxWalkHops"/>.</summary>
        PastTheBound = 4,
    }

    /// <summary>
    /// EB-D-215 (C1): the stable code of the one warning logged per row the walk leaves undecidable because its chain needs a hop past
    /// <see cref="MaxWalkHops"/>. Fields: the table logical name, the row id, the hops walked and the plane. No firm, invoice, caller or
    /// identity value.
    /// </summary>
    internal const string WalkPastTheBoundEvent = "EXT-INVOICE-WALK-PAST-BOUND";

    /// <summary>Every table with at least one invoice link, keyed by logical name.</summary>
    internal static readonly IReadOnlyDictionary<string, LinkedTable> Tables =
        Build(RecordFamilyEdges.Of(RecordFamilyEdges.FamilyKind.Filing), SecureChildLineage.Children, Supplemental);

    /// <summary>F3b: every table decided through its parent edges, keyed by logical name (derived: <see cref="BuildDescendants"/>).</summary>
    internal static readonly IReadOnlyDictionary<string, DescendantTable> Descendants =
        BuildDescendants(RecordFamilyEdges.Of(RecordFamilyEdges.FamilyKind.Filing), SecureChildLineage.Children, Tables);

    /// <summary>True when <paramref name="table"/> has at least one invoice link.</summary>
    internal static bool HasInvoiceLink(string? table) => table is not null && Tables.ContainsKey(table);

    /// <summary>F3b: true for a table that has NO invoice link of its own and is decided only through its parent edges (an analysis
    /// output, an event log, a file version ...). <c>sprk_document</c> is not one: it has its own links and is in <see cref="Tables"/>.</summary>
    internal static bool IsDescendantOnly(string? table) =>
        table is not null && Descendants.ContainsKey(table) && !Tables.ContainsKey(table);

    /// <summary>True when <paramref name="table"/> is a root with an invoice link (its link is redacted, the row is never hidden).</summary>
    internal static bool IsRootWithInvoiceLink(string? table) => table is not null && Tables.TryGetValue(table, out var linked) && linked.IsRoot;

    /// <summary>The invoice link columns of <paramref name="table"/> (logical names); empty when it has none.</summary>
    internal static IReadOnlyList<string> LinkColumnsOf(string? table) =>
        table is not null && Tables.TryGetValue(table, out var linked) ? linked.LinkColumns : Array.Empty<string>();

    /// <summary>
    /// The tables with an invoice link, from <paramref name="edges"/> and <paramref name="lineage"/> (the registries; a parameter so the
    /// guard can seed an edge) and <paramref name="supplemental"/> (links the registries do not carry). Columns keep the registries'
    /// order, the edge registry first.
    /// </summary>
    internal static IReadOnlyDictionary<string, LinkedTable> Build(
        IEnumerable<RecordFamilyEdges.Edge> edges, IReadOnlyDictionary<string, SecureChildLineage.Table> lineage,
        IReadOnlyList<SupplementalLink>? supplemental = null)
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

        foreach (var link in supplemental ?? Array.Empty<SupplementalLink>())
            Add(link.Table, link.Column);

        return columns.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var (entitySet, idColumn, isRoot) = ReadShapeOf(kv.Key, lineage);
                if (entitySet is null
                    && supplemental?.FirstOrDefault(s => string.Equals(s.Table, kv.Key, StringComparison.OrdinalIgnoreCase)) is { } extra)
                    entitySet = extra.EntitySet;
                return new LinkedTable(kv.Key, entitySet, idColumn, kv.Value.ToArray(), isRoot);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// F3b: the descendant tables, derived from <paramref name="edges"/> (a parameter so the guard can seed an edge). (1) The document's
    /// parent chain: the Filing edge <c>sprk_document.sprk_parentdocument → sprk_document</c>
    /// (<see cref="ExternalDocumentNarrowing.ParentDocumentAttribute"/>, the chain the task 188 narrowing walks), listed first. (2) Every
    /// PLAIN table (not in the invoice family, not a core record) with a Filing edge to an invoice-linked child table
    /// (<paramref name="tables"/>: not a root, not in the family) or to another descendant, followed to a fixed point. An edge to a root
    /// is not followed (a root is decided by its grant). The registry decides which tables these are; nothing here names one.
    /// <para>F3b fix round 1: the walk is per ROW, not per table. A table with invoice links of its own (a to-do, an event, a document,
    /// a communication, an analysis ...) is plain too, so its rows also follow their filing edges: a row whose own invoice column is
    /// null but which is filed under an invoice-linked row (an analysis on a document, a to-do on an event, an archived email
    /// document on its communication, a row on a service request) takes that row's invoices. A row's decision is the union of its
    /// own links and its ancestors' (<see cref="ShapeOf"/>). A self edge (an event regarding an event) is followed like any other;
    /// the walk's path check makes a cycle undecidable.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, DescendantTable> BuildDescendants(
        IEnumerable<RecordFamilyEdges.Edge> edges, IReadOnlyDictionary<string, SecureChildLineage.Table> lineage,
        IReadOnlyDictionary<string, LinkedTable> tables)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(tables);
        var all = edges.ToList();
        var parents = new Dictionary<string, List<ParentEdge>>(StringComparer.OrdinalIgnoreCase);

        static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        bool IsDecidedChild(string table) =>
            tables.TryGetValue(table, out var linked) && !linked.IsRoot && !ExternalInvoiceVisibility.IsInvoiceFamilyTable(table);
        // A core record other than a service request is never a parent here and never a descendant, so it is never walked; a service
        // request follows the child rule (Supplemental), so it is walked and its hop counts toward the bound.
        bool IsPlain(string table) =>
            !ExternalInvoiceVisibility.IsInvoiceFamilyTable(table) && !RecordFamilyEdges.CoreTables.Contains(table)
            && !(tables.TryGetValue(table, out var linked) && linked.IsRoot);
        bool Add(string child, string column, string parent)
        {
            if (!parents.TryGetValue(child, out var list))
                parents[child] = list = new List<ParentEdge>();
            if (list.Any(e => Same(e.Column, column)))
                return false;
            list.Add(new ParentEdge(column, parent));
            return true;
        }

        foreach (var edge in all.Where(e => Same(e.ChildTable, ExternalDocumentNarrowing.DocumentTable)
                     && Same(e.ParentTable, ExternalDocumentNarrowing.DocumentTable)
                     && Same(e.LookupColumn, ExternalDocumentNarrowing.ParentDocumentAttribute)))
        {
            Add(edge.ChildTable, edge.LookupColumn, edge.ParentTable);
        }

        // Fixed point: an edge is followed only toward a decided child (a service request included) or a descendant, never toward a
        // core record, so only child-to-child hops are walked.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var edge in all.Where(e => IsPlain(e.ChildTable)
                         && (IsDecidedChild(e.ParentTable) || parents.ContainsKey(e.ParentTable))))
            {
                changed |= Add(edge.ChildTable, edge.LookupColumn, edge.ParentTable);
            }
        }

        return parents.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var shape = ReadShapeOf(kv.Key, lineage);
                return new DescendantTable(kv.Key, shape.EntitySet, shape.IdColumn, kv.Value.ToArray());
            },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The invoices each row of <paramref name="rowIds"/> (rows of <paramref name="table"/>) links to, through every invoice link column,
    /// read BY ID through <paramref name="query"/> in one read per chunk of <see cref="CommunicationOpenThreadGate.ChunkSize"/>. F3b: for a
    /// descendant (<see cref="Descendants"/>) the list also carries the invoices of the rows it hangs from (its parent chain, bounded by
    /// <see cref="MaxWalkHops"/>). An empty list: the row links to no invoice.
    /// <see cref="ExternalDocumentNarrowing.UnreadableLink"/> in the list: a link that cannot be decided (the row did not come back, a
    /// link is not a GUID, or the chain is undecidable: <see cref="ReadRowInvoiceLinksAsync"/>). A failed read THROWS, as does a table
    /// with no invoice link or no known read shape.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ReadLinkedInvoicesAsync(
        string table, IReadOnlyCollection<Guid> rowIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct,
        ILogger? logger = null, string? plane = null) =>
        (await ReadRowInvoiceLinksAsync(table, rowIds, query, ct, logger, plane).ConfigureAwait(false))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Invoices);

    /// <summary>
    /// <see cref="ReadLinkedInvoicesAsync"/> with the chain verdict kept apart (F3b): per row, the invoices it decides on and whether
    /// the chain it hangs from was undecidable. The walk: hop 0 is the row (its own links and its parent edges); each following hop is one
    /// batched read per parent table over the distinct parents not yet in hand, up to <see cref="MaxWalkHops"/> hops. A parent column
    /// absent from a row, a parent link that is not a GUID, a parent that does not come back, a parent already on THIS row's path (a
    /// cycle) and a parent needed past the bound each make the row undecidable (<see cref="RowInvoiceLinks.Reasons"/> says which). Two
    /// edges to the same parent (a diamond) are not a cycle. A failed read THROWS. EB-D-215 (C1): each row left undecidable by the
    /// bound is logged once to <paramref name="logger"/> (<see cref="WalkPastTheBoundEvent"/>, labelled with <paramref name="plane"/>).
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, RowInvoiceLinks>> ReadRowInvoiceLinksAsync(
        string table, IReadOnlyCollection<Guid> rowIds, CommunicationOpenThreadGate.RowQuery query, CancellationToken ct,
        ILogger? logger = null, string? plane = null)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentNullException.ThrowIfNull(query);
        if (table is null || ShapeOf(table) is not { } shape)
            throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table with an invoice link.");
        if (shape.EntitySet is null)
            throw new InvalidOperationException($"No read shape for {shape.LogicalName}; its invoice links cannot be read (fail closed).");

        var distinct = rowIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var walks = distinct.ToDictionary(id => id, _ => new RowWalk());
        var known = new Dictionary<(string Table, Guid Id), IReadOnlyDictionary<string, JsonElement>?>();
        var level = distinct.Select(id => new Pending(id, table, id, new[] { (table, id) })).ToList();

        for (var hop = 0; level.Count > 0; hop++)
            level = await StepAsync(level, hop, walks, known, query, ct).ConfigureAwait(false);

        LogPastTheBound(table, walks, logger, plane);
        return walks.ToDictionary(kv => kv.Key, kv => kv.Value.Result());
    }

    /// <summary>F3b fix round 1: the parent edges of <paramref name="table"/> (<see cref="Descendants"/>); empty when it has none.</summary>
    internal static IReadOnlyList<ParentEdge> ParentEdgesOf(string? table) =>
        table is not null && Descendants.TryGetValue(table, out var descendant) ? descendant.Parents : Array.Empty<ParentEdge>();

    /// <summary>F3b fix round 1: the Web API value columns (<c>_col_value</c>) of the parent edges of <paramref name="table"/>. A REST read
    /// whose rows are put to <see cref="ReadAncestorInvoiceLinksAsync"/> selects every one of them.</summary>
    internal static IReadOnlyList<string> ParentValueColumnsOf(string? table) => ParentEdgesOf(table).Select(e => e.ValueColumn).ToArray();

    /// <summary>
    /// F3b fix round 1: the first parents (hop 1) a Web API <paramref name="row"/> of <paramref name="table"/> carries, one entry per
    /// parent edge that is set, for <see cref="ReadAncestorInvoiceLinksAsync"/>. An explicit null is "not filed there" (no entry). A
    /// column the row does not carry was not read and a value that is not a GUID cannot be read: each gives an entry whose parent is
    /// <see cref="ExternalDocumentNarrowing.UnreadableLink"/> (the row is undecidable). A row that was not read (null) gives one such
    /// entry per edge. A table with no parent edge gives none.
    /// </summary>
    internal static IReadOnlyList<(Guid Row, string Column, Guid Parent)> FirstParentsOf(
        string table, Guid rowId, IReadOnlyDictionary<string, JsonElement>? row)
    {
        var firsts = new List<(Guid, string, Guid)>();
        foreach (var edge in ParentEdgesOf(table))
        {
            var parent = row is null ? ExternalDocumentNarrowing.UnreadableLink : ExternalInvoiceVisibility.LinkOf(row, edge.ValueColumn);
            if (parent is { } id)
                firsts.Add((rowId, edge.Column, id));
        }

        return firsts;
    }

    /// <summary>
    /// The ancestors' invoice links for rows the caller has ALREADY read (F3b, the REST plane): <paramref name="parents"/> are the rows'
    /// first parents (hop 1), one entry per (row, parent edge column, parent id), as <see cref="FirstParentsOf"/> reads them. Same walk,
    /// same bound, same undecidable cases as <see cref="ReadRowInvoiceLinksAsync"/>; the rows' own links are not read here. F3b fix
    /// round 1: a table with several parent edges (a document's parent document and its related communication, event, to-do,
    /// agreement and service request; a to-do's or an event's regarding rows ...) gives one entry per edge that is set, and the row's
    /// answer is the union over all of them; any one edge that is undecidable makes the row undecidable. An entry whose column is not
    /// a parent edge of <paramref name="table"/> is refused (it would be followed as the wrong table). A failed read THROWS. A row left
    /// undecidable by the bound is logged once, as on <see cref="ReadRowInvoiceLinksAsync"/>.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, RowInvoiceLinks>> ReadAncestorInvoiceLinksAsync(
        string table, IReadOnlyCollection<(Guid Row, string Column, Guid Parent)> parents, CommunicationOpenThreadGate.RowQuery query,
        CancellationToken ct, ILogger? logger = null, string? plane = null)
    {
        ArgumentNullException.ThrowIfNull(parents);
        ArgumentNullException.ThrowIfNull(query);
        if (table is null || ShapeOf(table) is not { } shape || shape.Parents.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table with a parent edge.");

        var walks = new Dictionary<Guid, RowWalk>();
        var known = new Dictionary<(string Table, Guid Id), IReadOnlyDictionary<string, JsonElement>?>();
        var level = new List<Pending>();
        foreach (var (row, column, parent) in parents.Where(p => p.Row != Guid.Empty).Distinct())
        {
            var edge = shape.Parents.FirstOrDefault(e => string.Equals(e.Column, column, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentOutOfRangeException(nameof(parents), column, $"Not a parent edge of {table}.");
            var walk = walks.TryGetValue(row, out var existing) ? existing : walks[row] = new RowWalk();
            // An unreadable parent link, or a row that is its own parent, cannot be decided.
            if (parent == ExternalDocumentNarrowing.UnreadableLink || parent == Guid.Empty)
            {
                walk.MarkUndecidable(UndecidableReason.Unreadable);
                continue;
            }

            if (parent == row && string.Equals(edge.ParentTable, table, StringComparison.OrdinalIgnoreCase))
            {
                walk.MarkUndecidable(UndecidableReason.Cycle);
                continue;
            }

            level.Add(new Pending(row, edge.ParentTable, parent, new[] { (table, row), (edge.ParentTable, parent) }));
        }

        for (var hop = 1; level.Count > 0; hop++)
            level = await StepAsync(level, hop, walks, known, query, ct).ConfigureAwait(false);

        LogPastTheBound(table, walks, logger, plane);
        return walks.ToDictionary(kv => kv.Key, kv => kv.Value.Result());
    }

    /// <summary>EB-D-215 (C1): one warning per row the bound left undecidable, so a walk that hits the bound is never silent. The row id
    /// and the hops walked only: no firm, invoice, caller or identity value. A cycle and an unreadable link are not logged here.</summary>
    private static void LogPastTheBound(string table, Dictionary<Guid, RowWalk> walks, ILogger? logger, string? plane)
    {
        if (logger is null)
            return;
        foreach (var (rowId, walk) in walks.Where(kv => kv.Value.Reasons.HasFlag(UndecidableReason.PastTheBound)))
        {
            logger.LogWarning(
                "[{EventCode}] {Table} row {RowId}: the invoice walk stopped at the bound after {Hops} hop(s) (plane={Plane}); the row is undecidable and withheld (fail closed).",
                WalkPastTheBoundEvent, table, rowId, walk.HopsAtTheBound, plane ?? "unknown");
        }
    }

    private sealed record Pending(Guid Origin, string Table, Guid Id, IReadOnlyList<(string Table, Guid Id)> Path);

    private sealed class RowWalk
    {
        private readonly List<Guid> _invoices = new();

        /// <summary>Why the row is undecidable (<see cref="UndecidableReason.None"/>: decided so far).</summary>
        internal UndecidableReason Reasons { get; private set; }

        /// <summary>The hops walked when the bound stopped the walk (set with <see cref="UndecidableReason.PastTheBound"/>).</summary>
        internal int HopsAtTheBound { get; private set; }

        internal void MarkUndecidable(UndecidableReason reason) => Reasons |= reason;

        internal void MarkPastTheBound(int hops)
        {
            Reasons |= UndecidableReason.PastTheBound;
            HopsAtTheBound = Math.Max(HopsAtTheBound, hops);
        }

        internal void AddLink(Guid? link)
        {
            // A link column that was not read, or is not a GUID, cannot be decided: the row is undecidable (never "no link").
            if (link == ExternalDocumentNarrowing.UnreadableLink)
            {
                MarkUndecidable(UndecidableReason.Unreadable);
                return;
            }

            if (link is { } id && !_invoices.Contains(id))
                _invoices.Add(id);
        }

        internal RowInvoiceLinks Result()
        {
            var undecidable = Reasons != UndecidableReason.None;
            var invoices = _invoices.ToList();
            if (undecidable && !invoices.Contains(ExternalDocumentNarrowing.UnreadableLink))
                invoices.Add(ExternalDocumentNarrowing.UnreadableLink);
            return new RowInvoiceLinks(invoices, undecidable, Reasons);
        }
    }

    /// <summary>One hop: reads the distinct rows of <paramref name="level"/> not in hand (one batched read per table and chunk), adds each
    /// row's own links to its walk and returns the next level (the parents still to read).</summary>
    private static async Task<List<Pending>> StepAsync(
        List<Pending> level, int hop, Dictionary<Guid, RowWalk> walks,
        Dictionary<(string Table, Guid Id), IReadOnlyDictionary<string, JsonElement>?> known,
        CommunicationOpenThreadGate.RowQuery query, CancellationToken ct)
    {
        foreach (var group in level.Where(p => !known.ContainsKey((p.Table, p.Id))).GroupBy(p => p.Table, StringComparer.OrdinalIgnoreCase))
        {
            var rowShape = ShapeOf(group.Key)
                ?? throw new InvalidOperationException($"No read shape for {group.Key}; its invoice links cannot be read (fail closed).");
            if (rowShape.EntitySet is null)
                throw new InvalidOperationException($"No read shape for {rowShape.LogicalName}; its invoice links cannot be read (fail closed).");
            var ids = group.Select(p => p.Id).Distinct().ToList();
            foreach (var id in ids)
                known[(group.Key, id)] = null; // asked: a row that does not come back stays null
            var select = string.Join(",", new[] { rowShape.IdColumn }
                .Concat(rowShape.ValueColumns).Concat(rowShape.Parents.Select(e => e.ValueColumn)).Distinct());
            var entitySet = rowShape.EntitySet;
            var idColumn = rowShape.IdColumn;
            var chunks = await CommunicationOpenThreadGate.ReadChunksAsync(ids, (chunk, c) => query(entitySet,
                $"$select={select}" +
                $"&$filter={string.Join(" or ", chunk.Select(id => $"{idColumn} eq {id:D}"))}&$top={chunk.Length}", c), ct)
                .ConfigureAwait(false);
            foreach (var (chunk, rows) in chunks)
            {
                var inChunk = chunk.ToHashSet();
                foreach (var row in rows)
                {
                    if (ExternalInvoiceVisibility.LinkOf(row, idColumn) is { } rowId && inChunk.Contains(rowId))
                        known[(group.Key, rowId)] = row;
                }
            }
        }

        var next = new List<Pending>();
        foreach (var pending in level)
        {
            var walk = walks[pending.Origin];
            if (known[(pending.Table, pending.Id)] is not { } row)
            {
                walk.MarkUndecidable(UndecidableReason.Unreadable); // a row that did not come back
                continue;
            }

            var rowShape = ShapeOf(pending.Table)!.Value;
            foreach (var column in rowShape.ValueColumns)
                walk.AddLink(ExternalInvoiceVisibility.LinkOf(row, column));

            foreach (var edge in rowShape.Parents)
            {
                var parent = ExternalInvoiceVisibility.LinkOf(row, edge.ValueColumn);
                if (parent is null)
                    continue;
                if (parent == ExternalDocumentNarrowing.UnreadableLink)
                {
                    walk.MarkUndecidable(UndecidableReason.Unreadable); // an unreadable column or link
                    continue;
                }

                if (pending.Path.Contains((edge.ParentTable, parent.Value), TableIdComparer.Instance))
                {
                    walk.MarkUndecidable(UndecidableReason.Cycle);
                    continue;
                }

                if (hop >= MaxWalkHops)
                {
                    walk.MarkPastTheBound(hop); // a chain that needs a hop past the bound
                    continue;
                }

                next.Add(new Pending(pending.Origin, edge.ParentTable, parent.Value,
                    pending.Path.Append((edge.ParentTable, parent.Value)).ToArray()));
            }
        }

        return next;
    }

    private sealed class TableIdComparer : IEqualityComparer<(string Table, Guid Id)>
    {
        internal static readonly TableIdComparer Instance = new();

        public bool Equals((string Table, Guid Id) x, (string Table, Guid Id) y) =>
            x.Id == y.Id && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Table, Guid Id) obj) =>
            HashCode.Combine(obj.Id, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table));
    }

    /// <summary>How <paramref name="table"/> is read: from <see cref="Tables"/> (own links) and <see cref="Descendants"/> (parent edges);
    /// null when it is in neither.</summary>
    private static (string LogicalName, string? EntitySet, string IdColumn, IReadOnlyList<string> ValueColumns, IReadOnlyList<ParentEdge> Parents)? ShapeOf(string table)
    {
        var inTables = Tables.TryGetValue(table, out var linked);
        var inDescendants = Descendants.TryGetValue(table, out var descendant);
        if (!inTables && !inDescendants)
            return null;
        return inTables
            ? (linked!.LogicalName, linked.EntitySet, linked.IdColumn, linked.ValueColumns,
                inDescendants ? descendant!.Parents : Array.Empty<ParentEdge>())
            : (descendant!.LogicalName, descendant.EntitySet, descendant.IdColumn, Array.Empty<string>(), descendant.Parents);
    }

    private static bool IsInvoice(string? table) =>
        string.Equals(table, ExternalInvoiceVisibility.InvoiceTable, StringComparison.OrdinalIgnoreCase);

    /// <summary>The read shape of <paramref name="table"/> and whether it is a root (one of the grant roots, <see cref="ExternalGrantRoot"/>).
    /// A table neither a known child nor a root has no shape (its read throws).</summary>
    private static (string? EntitySet, string IdColumn, bool IsRoot) ReadShapeOf(
        string table, IReadOnlyDictionary<string, SecureChildLineage.Table> lineage)
    {
        foreach (var root in Enum.GetValues<ExternalGrantRootType>())
        {
            if (string.Equals(ExternalGrantRoot.LogicalNameFor(root), table, StringComparison.OrdinalIgnoreCase))
                return (ExternalGrantRoot.BindFor(root).EntitySet, table + "id", true);
        }

        return lineage.TryGetValue(table, out var child) ? (child.EntitySet, child.IdColumn, false) : (null, table + "id", false);
    }
}
