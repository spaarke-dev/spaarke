using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b F3b: the invoice-linked DESCENDANTS. A row with no invoice link of its own that hangs from an invoice-linked row
/// (an analysis output, an event log, a file version, an attachment or email artifact ...), and a child document up its
/// <c>sprk_parentdocument</c> chain, is decided by the invoice links of the rows it hangs from. The edge set is derived from the filing
/// edges (<see cref="InvoiceLinks.Descendants"/>), never listed by hand. Part 1 holds the derivation to the registry, part 2 the bounded
/// chain walk, part 3 the assistant tools' seam over the real predicate.
/// </summary>
/// <remarks>Maintain-class (ADR-038): a descendant the walk missed shows another firm's invoice work to a partner.</remarks>
[Trait("category", "external-access")]
public sealed class InvoiceLinkedDescendantsTests
{
    private const string Invoice = "sprk_invoice";

    private static RecordFamilyEdges.Edge Edge(string child, string column, string parent) =>
        new(RecordFamilyEdges.FamilyKind.Filing, child, column, parent, RecordFamilyEdges.EdgeKind.LineageHop, InSecureFamily: true, null);

    // ── Part 1: the derivation ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The conscious pin: the descendant-only tables the filing edges derive today. A registry change that adds, drops or moves
    /// one fails here until this list (and the notes' table) says so.</summary>
    private static readonly string[] DerivedDescendantOnly =
    {
        "sprk_agreement", "sprk_analysisoutput", "sprk_attachmentartifact", "sprk_communicationattachment",
        "sprk_communicationparticipant", "sprk_emailartifact", "sprk_emailreviewlog", "sprk_eventlog", "sprk_fileversion",
    };

    [Fact(DisplayName = "F3b the descendant-only tables are the ones the filing edges derive, and include the four named ones")]
    public void TheDescendants_AreDerivedFromTheFilingEdges()
    {
        InvoiceLinks.Descendants.Keys.Where(InvoiceLinks.IsDescendantOnly).Order(StringComparer.Ordinal).Should().Equal(DerivedDescendantOnly);
        InvoiceLinks.Descendants.Keys.Should().Contain(new[] { "sprk_analysisoutput", "sprk_eventlog", "sprk_fileversion", "sprk_document" });
        InvoiceLinks.Descendants["sprk_analysisoutput"].Parents.Should().Equal(new InvoiceLinks.ParentEdge("sprk_analysisid", "sprk_analysis"));
        InvoiceLinks.Descendants["sprk_eventlog"].Parents.Should().Equal(new InvoiceLinks.ParentEdge("sprk_event", "sprk_event"));
        InvoiceLinks.Descendants["sprk_fileversion"].Parents.Should().Equal(new InvoiceLinks.ParentEdge("sprk_document", "sprk_document"));
        InvoiceLinks.Descendants["sprk_document"].Parents.Should().Equal(new InvoiceLinks.ParentEdge("sprk_parentdocument", "sprk_document"));
        InvoiceLinks.Descendants.Values.Should().OnlyContain(d => d.EntitySet != null, "every descendant can be read (a table with no read shape throws)");
    }

    [Fact(DisplayName = "F3b the descendants are decided as child tables; a table with its own link, a root and an invoice-family table are not descendant-only")]
    public void TheDescendantOnlyTables_AreChildTables_AndOthersAreNot()
    {
        foreach (var table in DerivedDescendantOnly)
            ExternalInvoiceVisibility.IsInvoiceLinkedChildTable(table).Should().BeTrue(table);
        ExternalInvoiceVisibility.IsInvoiceLinkedChildTable("sprk_servicerequest").Should().BeTrue("a service request follows the child rule (F3b)");

        foreach (var table in new[] { "sprk_todo", "sprk_workassignment", "sprk_signal", "sprk_invoicesubmission", "sprk_matter", "sprk_budget", "sprk_decisionrecord" })
            InvoiceLinks.IsDescendantOnly(table).Should().BeFalse(table);
        InvoiceLinks.IsDescendantOnly("sprk_document").Should().BeFalse("a document has its own links; it is in Tables and keeps the parent chain");
        InvoiceLinks.IsDescendantOnly(null).Should().BeFalse();
        InvoiceLinks.Descendants.Keys.Should().NotContain(t => InvoiceLinks.IsRootWithInvoiceLink(t) || ExternalInvoiceVisibility.IsInvoiceFamilyTable(t));
    }

    [Fact(DisplayName = "F3b guard must fire: a seeded filing edge from a new plain table to an invoice-linked child is derived as a descendant (and cannot be read without a shape)")]
    public void Guard_MustFire_OnASeededDescendantEdge()
    {
        var seeded = RecordFamilyEdges.All.Append(Edge("sprk_seededlog", "sprk_event", "sprk_event")).ToList();

        var rebuilt = InvoiceLinks.BuildDescendants(seeded, SecureChildLineage.Children, InvoiceLinks.Tables);

        rebuilt.Should().ContainKey("sprk_seededlog").WhoseValue.Parents.Should().Equal(new InvoiceLinks.ParentEdge("sprk_event", "sprk_event"));
        rebuilt["sprk_seededlog"].EntitySet.Should().BeNull("a table with no read shape throws on read: fail closed");
        rebuilt.Keys.Where(k => !InvoiceLinks.Tables.ContainsKey(k)).Should().Contain("sprk_seededlog")
            .And.Contain(DerivedDescendantOnly, "the seeded edge adds one; it takes none away");
    }

    [Fact(DisplayName = "F3b a descendant of a descendant is derived to a fixed point")]
    public void ADescendantOfADescendant_IsDerived()
    {
        var seeded = RecordFamilyEdges.All.Append(Edge("sprk_seededdetail", "sprk_eventlog", "sprk_eventlog")).ToList();

        var rebuilt = InvoiceLinks.BuildDescendants(seeded, SecureChildLineage.Children, InvoiceLinks.Tables);

        rebuilt["sprk_seededdetail"].Parents.Should().Equal(new InvoiceLinks.ParentEdge("sprk_eventlog", "sprk_eventlog"));
    }

    [Fact(DisplayName = "F3b guard must not fire: an edge to a root, to an invoice-family table, or from a table with its own link adds nothing")]
    public void Guard_MustNotFire_OnEdgesToRootsFamilyOrLinkedTables()
    {
        var before = InvoiceLinks.Descendants.Keys.Order(StringComparer.Ordinal).ToList();
        var seeded = RecordFamilyEdges.All
            .Append(Edge("sprk_seededa", "sprk_regardingmatter", "sprk_matter"))
            .Append(Edge("sprk_seededb", "sprk_regardinginvoice", Invoice))
            .Append(Edge("sprk_seededc", "sprk_signal", "sprk_signal"))
            .Append(Edge("sprk_todo", "sprk_eventlog", "sprk_eventlog"))
            .ToList();

        var rebuilt = InvoiceLinks.BuildDescendants(seeded, SecureChildLineage.Children, InvoiceLinks.Tables);

        rebuilt.Keys.Order(StringComparer.Ordinal).Should().Equal(before);
    }

    [Fact(DisplayName = "F3b the document is decided by its parent chain: the descendants of the document use the same bound as the 188 narrowing")]
    public void TheBound_IsTheNarrowingsBound() => ExternalDocumentNarrowing.MaxHops.Should().Be(3);

    // ── Part 2: the walk ─────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Store
    {
        private readonly Dictionary<(string Set, Guid Id), Dictionary<string, object?>> _rows = new();
        public readonly List<(string Set, string OData)> Reads = new();
        public Exception? Fault;

        /// <summary>Columns a read does NOT return (the Web API omits a column it was not asked for or could not read).</summary>
        public readonly HashSet<string> Omit = new(StringComparer.Ordinal);

        public Guid Row(string table, params (string Column, object? Value)[] values)
        {
            var shape = InvoiceLinks.Tables.TryGetValue(table, out var linked)
                ? (linked.EntitySet!, linked.IdColumn)
                : (InvoiceLinks.Descendants[table].EntitySet!, InvoiceLinks.Descendants[table].IdColumn);
            var id = Guid.NewGuid();
            var row = new Dictionary<string, object?> { [shape.Item2] = id.ToString() };
            foreach (var (column, value) in values)
                row[column] = value?.ToString();
            _rows[(shape.Item1, id)] = row;
            return id;
        }

        /// <summary>Sets <paramref name="column"/> (written <c>_col_value</c>) on the row <paramref name="id"/> of <paramref name="table"/>.</summary>
        public void Set(string table, Guid id, string column, Guid value)
        {
            var set = InvoiceLinks.Tables.TryGetValue(table, out var linked) ? linked.EntitySet! : InvoiceLinks.Descendants[table].EntitySet!;
            _rows[(set, id)][column] = value.ToString();
        }

        public CommunicationOpenThreadGate.RowQuery Query => (set, odata, _) =>
        {
            lock (Reads) Reads.Add((set, odata));
            if (Fault is { } fault)
                throw fault;
            var selected = odata.Split("$select=", 2)[1].Split('&')[0].Split(',');
            var asked = Regex.Matches(odata.Split("$filter=", 2)[1], "[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")
                .Select(m => Guid.Parse(m.Value)).ToHashSet();
            IReadOnlyList<Dictionary<string, JsonElement>> rows = _rows
                .Where(kv => kv.Key.Set == set && asked.Contains(kv.Key.Id))
                .Select(kv =>
                {
                    var row = new Dictionary<string, JsonElement>();
                    foreach (var column in selected.Where(c => !Omit.Contains(c)))
                    {
                        // The Web API answers a selected column that is not set as an explicit null.
                        kv.Value.TryGetValue(column, out var value);
                        row[column] = JsonSerializer.SerializeToElement(value);
                    }

                    return row;
                })
                .ToList();
            return Task.FromResult(rows);
        };
    }

    /// <summary>The row's chain was decidable and its invoices are exactly <paramref name="invoices"/>.</summary>
    private static void Decided(InvoiceLinks.RowInvoiceLinks actual, params Guid[] invoices)
    {
        actual.ChainUndecidable.Should().BeFalse();
        actual.Invoices.Should().BeEquivalentTo(invoices);
    }

    private static Task<IReadOnlyDictionary<Guid, InvoiceLinks.RowInvoiceLinks>> WalkAsync(Store store, string table, params Guid[] ids) =>
        InvoiceLinks.ReadRowInvoiceLinksAsync(table, ids, store.Query, CancellationToken.None);

    [Theory(DisplayName = "F3b walk: a descendant takes the invoice links of the row it hangs from; no parent and a parent with no link give none")]
    [InlineData("sprk_analysisoutput", "sprk_analysisid", "sprk_analysis")]
    [InlineData("sprk_eventlog", "sprk_event", "sprk_event")]
    [InlineData("sprk_fileversion", "sprk_document", "sprk_document")]
    public async Task ADescendant_TakesItsParentsInvoiceLinks(string table, string parentColumn, string parentTable)
    {
        var linkColumn = InvoiceLinks.LinkColumnsOf(parentTable)[0];
        var invoice = Guid.NewGuid();
        var store = new Store();
        var linkedParent = store.Row(parentTable, ($"_{linkColumn}_value", invoice));
        var unlinkedParent = store.Row(parentTable);
        var onLinked = store.Row(table, ($"_{parentColumn}_value", linkedParent));
        var onUnlinked = store.Row(table, ($"_{parentColumn}_value", unlinkedParent));
        var noParent = store.Row(table);

        var result = await WalkAsync(store, table, onLinked, onUnlinked, noParent);

        Decided(result[onLinked], invoice);
        Decided(result[onUnlinked]);
        Decided(result[noParent]);
        store.Reads.Select(r => r.Set).Should().Equal(InvoiceLinks.Descendants[table].EntitySet, InvoiceLinks.Tables[parentTable].EntitySet);
    }

    [Fact(DisplayName = "F3b walk: a child document is decided by EVERY invoice link up its chain (own, parent, grandparent, great-grandparent)")]
    public async Task AChildDocument_IsDecidedByEveryLinkUpItsChain()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var store = new Store();
        var great = store.Row("sprk_document", ("_sprk_invoice_value", d));
        var grand = store.Row("sprk_document", ("_sprk_relatedinvoice_value", c), ("_sprk_parentdocument_value", great));
        var parent = store.Row("sprk_document", ("_sprk_invoice_value", b), ("_sprk_parentdocument_value", grand));
        var child = store.Row("sprk_document", ("_sprk_invoice_value", a), ("_sprk_parentdocument_value", parent));

        var result = await WalkAsync(store, "sprk_document", child);

        result[child].ChainUndecidable.Should().BeFalse("a chain of exactly the bound is decidable");
        result[child].Invoices.Should().BeEquivalentTo(new[] { a, b, c, d });
        store.Reads.Should().HaveCount(4, "one batched read per hop level");
    }

    [Fact(DisplayName = "F3b walk: a chain one hop past the bound is undecidable, and says so in the list")]
    public async Task AChainPastTheBound_IsUndecidable()
    {
        var store = new Store();
        var rows = new List<Guid> { store.Row("sprk_document") };
        for (var i = 0; i < ExternalDocumentNarrowing.MaxHops + 1; i++)
            rows.Add(store.Row("sprk_document", ("_sprk_parentdocument_value", rows[^1])));

        var result = await WalkAsync(store, "sprk_document", rows[^1]);

        result[rows[^1]].ChainUndecidable.Should().BeTrue();
        result[rows[^1]].Invoices.Should().Contain(ExternalDocumentNarrowing.UnreadableLink);
    }

    [Fact(DisplayName = "F3b walk: a cycle, and a row that is its own parent, are undecidable; a diamond is not a cycle")]
    public async Task ACycle_IsUndecidable_ADiamondIsNot()
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        var first = store.Row("sprk_document");
        var second = store.Row("sprk_document", ("_sprk_parentdocument_value", first));
        var (cycleA, cycleB) = (store.Row("sprk_document"), store.Row("sprk_document"));
        var self = store.Row("sprk_document");
        store.Set("sprk_document", first, "_sprk_parentdocument_value", second); // first -> second -> first
        store.Set("sprk_document", cycleA, "_sprk_parentdocument_value", cycleB);
        store.Set("sprk_document", cycleB, "_sprk_parentdocument_value", cycleA);
        store.Set("sprk_document", self, "_sprk_parentdocument_value", self);
        var document = store.Row("sprk_document", ("_sprk_invoice_value", invoice));
        var email = store.Row("sprk_emailartifact", ("_sprk_document_value", document));
        var attachment = store.Row("sprk_attachmentartifact", ("_sprk_document_value", document), ("_sprk_emailartifact_value", email));

        var cycles = await WalkAsync(store, "sprk_document", first, cycleA, self);
        var diamond = await WalkAsync(store, "sprk_attachmentartifact", attachment);

        cycles.Values.Should().OnlyContain(r => r.ChainUndecidable && r.Invoices.Contains(ExternalDocumentNarrowing.UnreadableLink));
        // The document is reached twice, by two edges: that is a diamond, not a cycle.
        Decided(diamond[attachment], invoice);
    }

    [Fact(DisplayName = "F3b walk: a parent that does not come back, an unreadable parent link and an absent parent column are each undecidable (the absent-column rule)")]
    public async Task AnUnreadableParent_IsUndecidable()
    {
        var store = new Store();
        var orphan = store.Row("sprk_fileversion", ("_sprk_document_value", Guid.NewGuid()));
        var garbled = store.Row("sprk_fileversion", ("_sprk_document_value", "not-a-guid"));
        var ok = store.Row("sprk_fileversion");

        var result = await WalkAsync(store, "sprk_fileversion", orphan, garbled, ok);

        result[orphan].ChainUndecidable.Should().BeTrue("the parent does not come back");
        result[garbled].ChainUndecidable.Should().BeTrue("the parent link is not a GUID");
        result[ok].ChainUndecidable.Should().BeFalse("an explicit null is not filed (must not fire)");

        store.Omit.Add("_sprk_document_value");
        var absent = await WalkAsync(store, "sprk_fileversion", ok);
        absent[ok].ChainUndecidable.Should().BeTrue("a parent column the read did not return is unreadable, never 'no parent'");
    }

    [Theory(DisplayName = "F3b pin: the select of the four named descendants is exactly the id and the parent column(s) (and a document's own links)")]
    [InlineData("sprk_analysisoutput", "$select=sprk_analysisoutputid,_sprk_analysisid_value&$filter=")]
    [InlineData("sprk_eventlog", "$select=sprk_eventlogid,_sprk_event_value&$filter=")]
    [InlineData("sprk_fileversion", "$select=sprk_fileversionid,_sprk_document_value&$filter=")]
    [InlineData("sprk_document", "$select=sprk_documentid,_sprk_invoice_value,_sprk_relatedinvoice_value,_sprk_parentdocument_value&$filter=")]
    public async Task TheNamedDescendants_PinTheirSelect(string table, string expectedStart)
    {
        var store = new Store();
        var row = store.Row(table);

        await WalkAsync(store, table, row);

        store.Reads.Should().ContainSingle().Which.OData.Should().StartWith(expectedStart);
    }

    [Theory(DisplayName = "F3b pin: every derived descendant selects its id and every parent column; one that is not returned makes the row undecidable (the absent-column rule), an explicit null does not")]
    [MemberData(nameof(DescendantTables))]
    public async Task EveryDescendant_SelectsItsParents_AndAnAbsentParentColumnIsUndecidable(string table)
    {
        var shape = InvoiceLinks.Descendants[table];
        var store = new Store();
        var row = store.Row(table);

        var decided = await WalkAsync(store, table, row);

        Decided(decided[row]);
        var select = store.Reads[0].OData.Split("$select=", 2)[1].Split('&')[0].Split(',');
        select.Should().Contain(new[] { shape.IdColumn }.Concat(shape.Parents.Select(e => e.ValueColumn)));

        foreach (var edge in shape.Parents)
        {
            var absent = new Store();
            absent.Omit.Add(edge.ValueColumn);
            var absentRow = absent.Row(table);
            (await WalkAsync(absent, table, absentRow))[absentRow].ChainUndecidable.Should().BeTrue(edge.ValueColumn);
        }
    }

    public static TheoryData<string> DescendantTables => Tables();

    private static TheoryData<string> Tables()
    {
        var data = new TheoryData<string>();
        foreach (var table in InvoiceLinks.Descendants.Keys.Where(InvoiceLinks.IsDescendantOnly).OrderBy(t => t, StringComparer.Ordinal))
            data.Add(table);
        return data;
    }

    [Fact(DisplayName = "F3b walk: a descendant that does not come back is undecidable; a failed read throws; a table with no chain is refused")]
    public async Task AMissingRow_IsUndecidable_AFaultThrows()
    {
        var store = new Store();

        var missing = Guid.NewGuid();
        (await WalkAsync(store, "sprk_eventlog", missing))[missing].ChainUndecidable.Should().BeTrue();

        store.Fault = new TimeoutException("The read failed (test).");
        await ((Func<Task>)(() => WalkAsync(store, "sprk_eventlog", missing))).Should().ThrowAsync<TimeoutException>();
        await ((Func<Task>)(() => WalkAsync(store, "sprk_budget", missing))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "F3b walk: the REST helper walks the ancestors of rows already read; an unreadable or self parent is undecidable")]
    public async Task TheAncestorWalk_ForRowsAlreadyRead()
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        var top = store.Row("sprk_document", ("_sprk_invoice_value", invoice));
        var (rowA, rowB, rowC, rowD) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        const string Parent = "sprk_parentdocument";
        var result = await InvoiceLinks.ReadAncestorInvoiceLinksAsync("sprk_document", new[]
        {
            (rowA, Parent, top), (rowB, Parent, ExternalDocumentNarrowing.UnreadableLink), (rowC, Parent, rowC), (rowD, Parent, Guid.NewGuid()),
        }, store.Query, CancellationToken.None);

        Decided(result[rowA], invoice);
        result[rowB].ChainUndecidable.Should().BeTrue();
        result[rowC].ChainUndecidable.Should().BeTrue();
        result[rowD].ChainUndecidable.Should().BeTrue("the parent does not come back");
    }

    // ── Part 3: the assistant tools' seam ────────────────────────────────────────────────────────────────────

    private static Task<IReadOnlySet<Guid>> HideAsync(InvoiceWorld world, InvoiceFamilyCaller caller, string table, params Guid[] rows) =>
        InvoiceFamilyHidingStubs.Real(world)(caller, table, rows, CancellationToken.None);

    /// <summary>A parent row of <paramref name="table"/> linked to <paramref name="invoice"/> (the first edge whose parent has invoice links).</summary>
    private static (string Column, Guid Parent) LinkedParent(InvoiceWorld world, string table, Guid? invoice)
    {
        var edge = InvoiceLinks.Descendants[table].Parents.First(e => InvoiceLinks.HasInvoiceLink(e.ParentTable));
        var parent = invoice is { } i
            ? world.LinkedChildRow(edge.ParentTable, (InvoiceLinks.LinkColumnsOf(edge.ParentTable)[0], i))
            : world.LinkedChildRow(edge.ParentTable);
        return (edge.Column, parent);
    }

    [Theory(DisplayName = "F3b seam: every descendant of a row linked to another firm's invoice is hidden; its own firm's, an unlinked parent's and a parentless row are shown; one firm read")]
    [MemberData(nameof(DescendantTables))]
    public async Task EveryDescendant_FollowsItsParentsInvoice(string table)
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var other = world.OwnedInvoice();
        world.InvoiceFirm(other, InvoiceFamilyFirmWorld.OtherFirm);
        var ofOther = world.DescendantRow(table, LinkedParent(world, table, other));
        var ofOwn = world.DescendantRow(table, LinkedParent(world, table, own));
        var ofUnlinked = world.DescendantRow(table, LinkedParent(world, table, null));
        var parentless = world.DescendantRow(table);

        var hidden = await HideAsync(world, world.NewFamilyCaller(), table, ofOther, ofOwn, ofUnlinked, parentless);

        hidden.Should().BeEquivalentTo(new[] { ofOther });
        world.CallerFirmReads.Should().Be(1);
    }

    [Fact(DisplayName = "F3b seam: a child document is hidden when ANY invoice up its chain is another firm's, and shown when every one is visible")]
    public async Task AChildDocument_IsHiddenByAnyInvoiceUpTheChain()
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var other = world.OwnedInvoice();
        world.InvoiceFirm(other, InvoiceFamilyFirmWorld.OtherFirm);
        var hiddenTop = world.LinkedChildRow("sprk_document", ("sprk_invoice", other));
        var visibleTop = world.LinkedChildRow("sprk_document", ("sprk_invoice", own));

        Guid Chain(Guid top, int depth)
        {
            var current = top;
            for (var i = 0; i < depth; i++)
            {
                var child = world.LinkedChildRow("sprk_document");
                world.SetRow("sprk_documents", "sprk_documentid", child, "_sprk_parentdocument_value", current.ToString());
                current = child;
            }

            return current;
        }

        var underHidden = Chain(hiddenTop, 3);
        var underVisible = Chain(visibleTop, 3);
        var tooLong = Chain(visibleTop, 4);

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_document", underHidden, underVisible, tooLong);

        hidden.Should().BeEquivalentTo(new[] { underHidden, tooLong }, "the third hop decides; a fourth is past the bound and fails closed");
    }

    [Fact(DisplayName = "F3b seam: a cycle hides only the cyclic row (a per-row fault with known firms); its sibling is shown")]
    public async Task ACyclicDocument_IsHiddenAlone()
    {
        var world = new InvoiceWorld();
        var first = world.LinkedChildRow("sprk_document");
        var second = world.LinkedChildRow("sprk_document");
        world.SetRow("sprk_documents", "sprk_documentid", first, "_sprk_parentdocument_value", second.ToString());
        world.SetRow("sprk_documents", "sprk_documentid", second, "_sprk_parentdocument_value", first.ToString());
        var fine = world.LinkedChildRow("sprk_document");

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_document", first, fine);

        hidden.Should().BeEquivalentTo(new[] { first });
    }

    [Fact(DisplayName = "F3b seam: a service request follows the child rule (another firm's invoice hides it)")]
    public async Task AServiceRequest_FollowsTheChildRule()
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var other = world.OwnedInvoice();
        world.InvoiceFirm(other, InvoiceFamilyFirmWorld.OtherFirm);
        var ofOther = world.LinkedChildRow("sprk_servicerequest", ("sprk_regardinginvoice", other));
        var ofOwn = world.LinkedChildRow("sprk_servicerequest", ("sprk_regardinginvoice", own));
        var unlinked = world.LinkedChildRow("sprk_servicerequest");

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_servicerequest", ofOther, ofOwn, unlinked);

        hidden.Should().BeEquivalentTo(new[] { ofOther });
    }

    // ── Firms (uac-r2 ruling): unknown fails the call, no firm hides everything, a per-row fault hides the row ───────

    [Theory(DisplayName = "F3b firms must fire: firms that cannot be read fail the call (a tool error) on a descendant, a child and a family table")]
    [InlineData("sprk_fileversion")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_signal")]
    public async Task UnknownFirms_FailTheCall(string table)
    {
        foreach (var mode in new[] { "throw", "null", "unknown" })
        {
            var world = new InvoiceWorld();
            switch (mode)
            {
                case "throw": world.CallerFirmsFault = new TimeoutException("firms (test)"); break;
                case "null": world.CallerFirmsNull = true; break;
                default: world.CallerFirmsAnswer = CallerFirms.Unknown; break;
            }

            var invoice = world.OwnedInvoice();
            var row = InvoiceLinks.IsDescendantOnly(table)
                ? world.DescendantRow(table, LinkedParent(world, table, invoice))
                : ExternalInvoiceVisibility.IsInvoiceFamilyTable(table)
                    ? world.FamilyRow(table, invoice)
                    : world.LinkedChildRow(table, (InvoiceLinks.LinkColumnsOf(table)[0], invoice));

            var act = () => HideAsync(world, world.NewFamilyCaller(), table, row);

            await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>(mode);
        }
    }

    [Fact(DisplayName = "F3b firms must not fire: unknown firms and an invoice-linked row only matter together (an unlinked descendant makes no firm read and is shown)")]
    public async Task UnknownFirms_DoNotFailACallWithNoInvoiceLinkedRow()
    {
        var world = new InvoiceWorld { CallerFirmsFault = new TimeoutException("firms (test)") };
        var row = world.DescendantRow("sprk_fileversion", LinkedParent(world, "sprk_fileversion", null));

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_fileversion", row);

        hidden.Should().BeEmpty();
        world.CallerFirmReads.Should().Be(0);
    }

    [Theory(DisplayName = "F3b firms must fire: a caller in no firm sees nothing linked to an invoice (descendant, child, family); an unlinked row is shown")]
    [InlineData("sprk_eventlog")]
    [InlineData("sprk_memo")]
    [InlineData("sprk_signal")]
    public async Task NoFirm_HidesEverythingLinked(string table)
    {
        var world = new InvoiceWorld();
        var invoice = world.OwnedInvoice();
        var linked = InvoiceLinks.IsDescendantOnly(table)
            ? world.DescendantRow(table, LinkedParent(world, table, invoice))
            : ExternalInvoiceVisibility.IsInvoiceFamilyTable(table)
                ? world.FamilyRow(table, invoice)
                : world.LinkedChildRow(table, (InvoiceLinks.LinkColumnsOf(table)[0], invoice));

        var hidden = await HideAsync(world, new InvoiceFamilyCaller(null), table, linked);

        hidden.Should().BeEquivalentTo(new[] { linked });
        world.CallerFirmReads.Should().Be(0, "no resolved systemuser: nothing is read");
    }

    [Fact(DisplayName = "F3b family path: a fault in the decision hides the asked rows and does not throw")]
    public async Task TheFamilyPath_AFaultHidesTheRows_ItNeverThrows()
    {
        var world = new InvoiceWorld { FaultOn = url => url.Contains("/sprk_signals?", StringComparison.Ordinal) ? HttpStatusCode.InternalServerError : null };
        var invoice = world.OwnedInvoice();
        var signal = world.FamilyRow("sprk_signal", invoice);
        var another = world.FamilyRow("sprk_signal", invoice);

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_signal", signal, another);

        hidden.Should().BeEquivalentTo(new[] { signal, another });
    }

    [Fact(DisplayName = "F3b family path must not fire: with no fault the same rows are decided by their invoice (visible invoice, nothing hidden)")]
    public async Task TheFamilyPath_NoFault_DecidesTheRows()
    {
        var world = new InvoiceWorld();
        var invoice = world.OwnedInvoice();
        var signal = world.FamilyRow("sprk_signal", invoice);

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_signal", signal);

        hidden.Should().BeEmpty();
    }
}
