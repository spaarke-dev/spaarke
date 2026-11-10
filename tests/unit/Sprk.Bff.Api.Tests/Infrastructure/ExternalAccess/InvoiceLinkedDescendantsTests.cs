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
        InvoiceLinks.Descendants["sprk_document"].Parents.Should().Equal(LinkedTableEdges["sprk_document"]);
        InvoiceLinks.Descendants.Values.Should().OnlyContain(d => d.EntitySet != null, "every descendant can be read (a table with no read shape throws)");
    }

    /// <summary>
    /// F3b fix round 1, the conscious pin: the tables WITH invoice links of their own whose rows also follow their filing edges (the walk
    /// is per row), and every edge each one follows. A registry change that adds, drops or moves one fails here until this list (and the
    /// notes' table) says so.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, InvoiceLinks.ParentEdge[]> LinkedTableEdges = new Dictionary<string, InvoiceLinks.ParentEdge[]>
    {
        ["sprk_analysis"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_documentid", "sprk_document"), new("sprk_regardingcommunication", "sprk_communication"),
            new("sprk_regardingdocument", "sprk_document"), new("sprk_regardingservicerequest", "sprk_servicerequest"),
        },
        ["sprk_communication"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_communicationthread", "sprk_communicationthread"), new("sprk_regardinganalysis", "sprk_analysis"),
            new("sprk_regardingevent", "sprk_event"), new("sprk_regardingservicerequest", "sprk_servicerequest"),
        },
        ["sprk_communicationthread"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_regardinganalysis", "sprk_analysis"), new("sprk_regardingevent", "sprk_event"),
            new("sprk_regardingservicerequest", "sprk_servicerequest"),
        },
        ["sprk_document"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_parentdocument", "sprk_document"), new("sprk_relatedagreement", "sprk_agreement"),
            new("sprk_relatedcommunication", "sprk_communication"), new("sprk_relatedevent", "sprk_event"),
            new("sprk_relatedservicerequest", "sprk_servicerequest"), new("sprk_relatedtodo", "sprk_todo"),
        },
        ["sprk_event"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_regardingagreement", "sprk_agreement"), new("sprk_regardinganalysis", "sprk_analysis"),
            new("sprk_regardingcommunication", "sprk_communication"), new("sprk_regardingevent", "sprk_event"),
            new("sprk_regardingservicerequest", "sprk_servicerequest"),
        },
        ["sprk_memo"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_regardingagreement", "sprk_agreement"), new("sprk_regardinganalysis", "sprk_analysis"),
            new("sprk_regardingcommunication", "sprk_communication"), new("sprk_regardingdocument", "sprk_document"),
            new("sprk_regardingevent", "sprk_event"), new("sprk_regardingservicerequest", "sprk_servicerequest"),
        },
        ["sprk_todo"] = new InvoiceLinks.ParentEdge[]
        {
            new("sprk_regardingagreement", "sprk_agreement"), new("sprk_regardinganalysis", "sprk_analysis"),
            new("sprk_regardingcommunication", "sprk_communication"), new("sprk_regardingdocument", "sprk_document"),
            new("sprk_regardingevent", "sprk_event"), new("sprk_regardingservicerequest", "sprk_servicerequest"),
        },
    };

    [Fact(DisplayName = "F3b fix round 1 pin: the tables with their own invoice link that follow their filing edges, and every edge each follows")]
    public void TheLinkedTables_FollowTheirFilingEdges()
    {
        InvoiceLinks.Descendants.Keys.Where(t => !InvoiceLinks.IsDescendantOnly(t)).Order(StringComparer.Ordinal)
            .Should().Equal(LinkedTableEdges.Keys.Order(StringComparer.Ordinal));
        foreach (var (table, edges) in LinkedTableEdges)
        {
            InvoiceLinks.Descendants[table].Parents.Should().BeEquivalentTo(edges, table);
            InvoiceLinks.HasInvoiceLink(table).Should().BeTrue($"{table} keeps its own link: its rows decide by the union");
            InvoiceLinks.ParentValueColumnsOf(table).Should().BeEquivalentTo(edges.Select(e => e.ValueColumn), table);
        }

        InvoiceLinks.ParentEdgesOf("sprk_servicerequest").Should().BeEmpty("a service request is filed under nothing the walk follows");
        InvoiceLinks.ParentEdgesOf(null).Should().BeEmpty();
    }

    /// <summary>F3b fix round 1: every (table, edge) of <see cref="LinkedTableEdges"/>.</summary>
    public static TheoryData<string, string, string> LinkedTableEdgeCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (table, edges) in LinkedTableEdges.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var edge in edges)
                data.Add(table, edge.Column, edge.ParentTable);
        }

        return data;
    }

    [Theory(DisplayName = "F3b fix round 1 FirstParentsOf: one entry per set edge; an explicit null is not filed; an absent column, a non-GUID and an unread row are unreadable")]
    [MemberData(nameof(LinkedTableEdgeCases))]
    public void FirstParentsOf_ReadsEveryEdge(string table, string column, string parentTable)
    {
        _ = parentTable;
        var edge = InvoiceLinks.ParentEdgesOf(table).Single(e => e.Column == column);
        var (rowId, parent) = (Guid.NewGuid(), Guid.NewGuid());
        var allNull = InvoiceLinks.ParentValueColumnsOf(table).ToDictionary(c => c, _ => JsonSerializer.SerializeToElement<string?>(null));

        // Must not fire: explicit nulls are not filed.
        InvoiceLinks.FirstParentsOf(table, rowId, allNull).Should().BeEmpty();

        var set = new Dictionary<string, JsonElement>(allNull) { [edge.ValueColumn] = JsonSerializer.SerializeToElement(parent.ToString()) };
        InvoiceLinks.FirstParentsOf(table, rowId, set).Should().Equal((rowId, column, parent));

        // Must fire: a column that was not read, a value that is not a GUID, a row that was not read.
        var absent = allNull.Where(kv => kv.Key != edge.ValueColumn).ToDictionary(kv => kv.Key, kv => kv.Value);
        InvoiceLinks.FirstParentsOf(table, rowId, absent).Should().Equal((rowId, column, ExternalDocumentNarrowing.UnreadableLink));
        var garbled = new Dictionary<string, JsonElement>(allNull) { [edge.ValueColumn] = JsonSerializer.SerializeToElement("not-a-guid") };
        InvoiceLinks.FirstParentsOf(table, rowId, garbled).Should().Equal((rowId, column, ExternalDocumentNarrowing.UnreadableLink));
        InvoiceLinks.FirstParentsOf(table, rowId, null).Should().HaveCount(InvoiceLinks.ParentEdgesOf(table).Count)
            .And.OnlyContain(p => p.Parent == ExternalDocumentNarrowing.UnreadableLink);
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

    [Fact(DisplayName = "EB-D-215 pin: the invoice walk has its own bound (5); the task 188 narrowing keeps 3")]
    public void TheWalkBound_IsItsOwn()
    {
        InvoiceLinks.MaxWalkHops.Should().Be(5);
        ExternalDocumentNarrowing.MaxHops.Should().Be(3);
    }

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

        /// <summary>The GUID in <paramref name="column"/> (written <c>_col_value</c>) of the row <paramref name="id"/> of <paramref name="table"/>.</summary>
        public Guid FirstParent(string table, Guid id, string column)
        {
            var set = InvoiceLinks.Tables.TryGetValue(table, out var linked) ? linked.EntitySet! : InvoiceLinks.Descendants[table].EntitySet!;
            return Guid.Parse((string)_rows[(set, id)][column]!);
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

    [Fact(DisplayName = "F3b walk: a child document is decided by EVERY invoice link up its chain (its own, and each ancestor's up to the bound)")]
    public async Task AChildDocument_IsDecidedByEveryLinkUpItsChain()
    {
        var store = new Store();
        // The top of a chain of exactly the bound, then one document per hop down to the child; each links to its own invoice,
        // alternating the two link columns.
        var invoices = Enumerable.Range(0, InvoiceLinks.MaxWalkHops + 1).Select(_ => Guid.NewGuid()).ToList();
        Guid? above = null;
        for (var i = 0; i <= InvoiceLinks.MaxWalkHops; i++)
        {
            var link = i % 2 == 0 ? "_sprk_invoice_value" : "_sprk_relatedinvoice_value";
            above = above is { } a
                ? store.Row("sprk_document", (link, invoices[i]), ("_sprk_parentdocument_value", a))
                : store.Row("sprk_document", (link, invoices[i]));
        }

        var child = above!.Value;
        var result = await WalkAsync(store, "sprk_document", child);

        result[child].ChainUndecidable.Should().BeFalse("a chain of exactly the bound is decidable");
        result[child].Invoices.Should().BeEquivalentTo(invoices);
        store.Reads.Should().HaveCount(InvoiceLinks.MaxWalkHops + 1, "one batched read per hop level");
    }

    [Fact(DisplayName = "EB-D-215 pin: the two bounds are independent - the invoice walk decides a 4- and a 5-hop document chain that the narrowing stops at its third hop")]
    public async Task TheTwoBounds_AreIndependent()
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        Guid ChainOf(int hops)
        {
            var current = store.Row("sprk_document", ("_sprk_invoice_value", invoice));
            for (var i = 0; i < hops; i++)
                current = store.Row("sprk_document", ("_sprk_parentdocument_value", current));
            return current;
        }

        var (three, four, five) = (ChainOf(3), ChainOf(4), ChainOf(5));

        var walked = await WalkAsync(store, "sprk_document", three, four, five);
        var narrowed = await ExternalDocumentNarrowing.ReadAndDecideAsync(new[] { three, four, five }, store.Query, CancellationToken.None);

        Decided(walked[four], invoice);
        Decided(walked[five], invoice);
        narrowed.ReasonFor(three).Should().Be(DocumentNarrowingReason.Visible, "three hops are within the narrowing's bound");
        narrowed.ReasonFor(four).Should().Be(DocumentNarrowingReason.ChainTooLong, "the narrowing still stops at its third hop");
        narrowed.ReasonFor(five).Should().Be(DocumentNarrowingReason.ChainTooLong);
    }

    [Fact(DisplayName = "F3b walk: a chain one hop past the bound is undecidable, and says so in the list")]
    public async Task AChainPastTheBound_IsUndecidable()
    {
        var store = new Store();
        var rows = new List<Guid> { store.Row("sprk_document") };
        for (var i = 0; i < InvoiceLinks.MaxWalkHops + 1; i++)
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
    // F3b fix round 1: a document also selects every other row it is filed under.
    [InlineData("sprk_document", "$select=sprk_documentid,_sprk_invoice_value,_sprk_relatedinvoice_value,_sprk_parentdocument_value,_sprk_relatedagreement_value,_sprk_relatedcommunication_value,_sprk_relatedevent_value,_sprk_relatedservicerequest_value,_sprk_relatedtodo_value&$filter=")]
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

    /// <summary>F3b fix round 1: a row of <paramref name="parentTable"/> that leads to <paramref name="invoice"/>: its own link when it has
    /// one, else (an agreement) through the first of its edges whose parent has a link. Null: a row that leads to no invoice.</summary>
    private static Guid StoreParent(Store store, string parentTable, Guid? invoice)
    {
        if (InvoiceLinks.HasInvoiceLink(parentTable))
        {
            return invoice is { } i
                ? store.Row(parentTable, ($"_{InvoiceLinks.LinkColumnsOf(parentTable)[0]}_value", i))
                : store.Row(parentTable);
        }

        var edge = InvoiceLinks.ParentEdgesOf(parentTable).First(e => InvoiceLinks.HasInvoiceLink(e.ParentTable));
        return invoice is null ? store.Row(parentTable) : store.Row(parentTable, (edge.ValueColumn, StoreParent(store, edge.ParentTable, invoice)));
    }

    [Theory(DisplayName = "F3b fix round 1 walk: a row with its own link ALSO takes the invoices of the row each filing edge names (the union); an unlinked parent adds none; a parent that does not come back is undecidable")]
    [MemberData(nameof(LinkedTableEdgeCases))]
    public async Task ALinkedRow_TakesItsOwnAndItsParentsInvoices(string table, string column, string parentTable)
    {
        var edge = InvoiceLinks.ParentEdgesOf(table).Single(e => e.Column == column);
        var ownColumn = $"_{InvoiceLinks.LinkColumnsOf(table)[0]}_value";
        var (own, above) = (Guid.NewGuid(), Guid.NewGuid());
        var store = new Store();
        var linkedParent = StoreParent(store, parentTable, above);
        var unlinkedParent = StoreParent(store, parentTable, null);
        var both = store.Row(table, (ownColumn, own), (edge.ValueColumn, linkedParent));
        var onlyAbove = store.Row(table, (edge.ValueColumn, linkedParent));
        var onUnlinked = store.Row(table, (edge.ValueColumn, unlinkedParent));
        var plain = store.Row(table);
        var orphan = store.Row(table, (edge.ValueColumn, Guid.NewGuid()));

        var result = await WalkAsync(store, table, both, onlyAbove, onUnlinked, plain, orphan);

        // Must fire: a row whose own link is null takes its parent's (or grandparent's) invoice.
        Decided(result[both], own, above);
        Decided(result[onlyAbove], above);
        result[orphan].ChainUndecidable.Should().BeTrue("the parent does not come back");
        // Must not fire: no invoice anywhere.
        Decided(result[onUnlinked]);
        Decided(result[plain]);
    }

    [Theory(DisplayName = "F3b fix round 1 walk: the REST helper follows each edge of a row already read; a column that is not an edge of the table is refused")]
    [MemberData(nameof(LinkedTableEdgeCases))]
    public async Task TheAncestorWalk_FollowsEveryEdge(string table, string column, string parentTable)
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        var parent = StoreParent(store, parentTable, invoice);
        var (row, unlinked) = (Guid.NewGuid(), Guid.NewGuid());

        var result = await InvoiceLinks.ReadAncestorInvoiceLinksAsync(
            table, new[] { (row, column, parent), (unlinked, column, StoreParent(store, parentTable, null)) }, store.Query, CancellationToken.None);

        Decided(result[row], invoice);
        Decided(result[unlinked]);
        var wrong = () => InvoiceLinks.ReadAncestorInvoiceLinksAsync(
            table, new[] { (row, "sprk_notanedge", parent) }, store.Query, CancellationToken.None);
        await wrong.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "F3b fix round 1 walk: a row's answer is the union over ALL its edges; one undecidable edge makes the row undecidable")]
    public async Task TheAncestorWalk_UnionsEdges_AndOneUndecidableEdgeDecides()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var store = new Store();
        var ev = store.Row("sprk_event", ("_sprk_regardinginvoice_value", a));
        var doc = store.Row("sprk_document", ("_sprk_invoice_value", b));
        var (row, mixed) = (Guid.NewGuid(), Guid.NewGuid());

        var result = await InvoiceLinks.ReadAncestorInvoiceLinksAsync("sprk_todo", new[]
        {
            (row, "sprk_regardingevent", ev), (row, "sprk_regardingdocument", doc),
            (mixed, "sprk_regardingevent", ev), (mixed, "sprk_regardingdocument", ExternalDocumentNarrowing.UnreadableLink),
        }, store.Query, CancellationToken.None);

        Decided(result[row], a, b);
        result[mixed].ChainUndecidable.Should().BeTrue();
        result[mixed].Invoices.Should().Contain(ExternalDocumentNarrowing.UnreadableLink);
    }

    [Fact(DisplayName = "F3b fix round 1 walk: the bound counts from the row: a to-do as many hops under a linked event as the bound is decided, one more is undecidable; a self-edge cycle is undecidable")]
    public async Task TheBound_CountsFromTheRow_AndASelfEdgeCycleIsUndecidable()
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        // events[0] is the linked top; events[i] regards events[i - 1].
        var events = new List<Guid> { store.Row("sprk_event", ("_sprk_regardinginvoice_value", invoice)) };
        for (var i = 1; i <= InvoiceLinks.MaxWalkHops; i++)
            events.Add(store.Row("sprk_event", ("_sprk_regardingevent_value", events[^1])));
        var atTheBound = store.Row("sprk_todo", ("_sprk_regardingevent_value", events[InvoiceLinks.MaxWalkHops - 1]));
        var pastTheBound = store.Row("sprk_todo", ("_sprk_regardingevent_value", events[InvoiceLinks.MaxWalkHops]));
        var (cycleA, cycleB) = (store.Row("sprk_event"), store.Row("sprk_event"));
        store.Set("sprk_event", cycleA, "_sprk_regardingevent_value", cycleB);
        store.Set("sprk_event", cycleB, "_sprk_regardingevent_value", cycleA);

        var result = await WalkAsync(store, "sprk_todo", atTheBound, pastTheBound);
        var cycle = await WalkAsync(store, "sprk_event", cycleA);

        Decided(result[atTheBound], invoice);
        result[pastTheBound].ChainUndecidable.Should().BeTrue("one hop more is past the bound");
        cycle[cycleA].ChainUndecidable.Should().BeTrue("an event regarding an event regarding the first is a cycle");
    }

    // ── EB-D-215 C1: a walk that hits the bound is never silent ──────────────────────────────────────────────────

    /// <summary>Records every log entry with its structured fields.</summary>
    private sealed class FieldLogger : Microsoft.Extensions.Logging.ILogger
    {
        public readonly List<(Microsoft.Extensions.Logging.LogLevel Level, IReadOnlyDictionary<string, object?> Fields)> Entries = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            lock (Entries) Entries.Add((logLevel, fields));
        }

        /// <summary>The past-the-bound warnings.</summary>
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> PastTheBound => Entries
            .Where(e => e.Fields.TryGetValue("EventCode", out var code) && Equals(code, InvoiceLinks.WalkPastTheBoundEvent))
            .Select(e => e.Fields)
            .ToList();
    }

    /// <summary>A to-do under a chain of <paramref name="events"/> events (each regarding the next), the top one linked to
    /// <paramref name="invoice"/>.</summary>
    private static Guid TodoUnderEvents(Store store, int events, Guid invoice)
    {
        var above = store.Row("sprk_event", ("_sprk_regardinginvoice_value", invoice));
        for (var i = 1; i < events; i++)
            above = store.Row("sprk_event", ("_sprk_regardingevent_value", above));
        return store.Row("sprk_todo", ("_sprk_regardingevent_value", above));
    }

    [Fact(DisplayName = "EB-D-215 C1 must fire: a row past the bound logs exactly one warning with the event code, table, row id, hops and plane, and no invoice value; on both walk entry points")]
    public async Task PastTheBound_LogsOnce_WithItsFields()
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        var past = TodoUnderEvents(store, InvoiceLinks.MaxWalkHops + 1, invoice);
        // A second edge of the same row also past the bound: still one warning for the row.
        var documents = store.Row("sprk_document", ("_sprk_invoice_value", invoice));
        for (var i = 0; i < InvoiceLinks.MaxWalkHops; i++)
            documents = store.Row("sprk_document", ("_sprk_parentdocument_value", documents));
        store.Set("sprk_todo", past, "_sprk_regardingdocument_value", documents);

        var assistant = new FieldLogger();
        var walked = await InvoiceLinks.ReadRowInvoiceLinksAsync("sprk_todo", new[] { past }, store.Query, CancellationToken.None, assistant, "assistant");

        walked[past].ChainUndecidable.Should().BeTrue();
        walked[past].Reasons.Should().Be(InvoiceLinks.UndecidableReason.PastTheBound);
        var entry = assistant.PastTheBound.Should().ContainSingle().Subject;
        assistant.Entries.Single(e => ReferenceEquals(e.Fields, entry)).Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        entry["Table"].Should().Be("sprk_todo");
        entry["RowId"].Should().Be(past);
        entry["Hops"].Should().Be(InvoiceLinks.MaxWalkHops);
        entry["Plane"].Should().Be("assistant");
        entry.Values.Select(v => v?.ToString()).Should().NotContain(invoice.ToString(), "no invoice value is logged");

        // The REST entry point (the row already read: its first parents).
        var rest = new FieldLogger();
        var firstEvent = store.FirstParent("sprk_todo", past, "_sprk_regardingevent_value");
        var ancestors = await InvoiceLinks.ReadAncestorInvoiceLinksAsync(
            "sprk_todo", new[] { (past, "sprk_regardingevent", firstEvent), (past, "sprk_regardingdocument", documents) },
            store.Query, CancellationToken.None, rest, "rest");

        ancestors[past].Reasons.Should().Be(InvoiceLinks.UndecidableReason.PastTheBound);
        var restEntry = rest.PastTheBound.Should().ContainSingle().Subject;
        restEntry["Plane"].Should().Be("rest");
        restEntry["RowId"].Should().Be(past);
        restEntry["Hops"].Should().Be(InvoiceLinks.MaxWalkHops);
    }

    [Fact(DisplayName = "EB-D-215 C1 must not fire: a row decided within the bound logs nothing; a cycle and an unreadable link are undecidable for their own reason and are not logged as the bound")]
    public async Task WithinTheBound_LogsNothing_ACycleAndAnUnreadableLinkAreTheirOwnReason()
    {
        var invoice = Guid.NewGuid();
        var store = new Store();
        var atTheBound = TodoUnderEvents(store, InvoiceLinks.MaxWalkHops, invoice);
        var (cycleA, cycleB) = (store.Row("sprk_event"), store.Row("sprk_event"));
        store.Set("sprk_event", cycleA, "_sprk_regardingevent_value", cycleB);
        store.Set("sprk_event", cycleB, "_sprk_regardingevent_value", cycleA);
        var cyclic = store.Row("sprk_todo", ("_sprk_regardingevent_value", cycleA));
        var garbled = store.Row("sprk_todo", ("_sprk_regardingevent_value", "not-a-guid"));
        var orphan = store.Row("sprk_todo", ("_sprk_regardingevent_value", Guid.NewGuid()));

        var logger = new FieldLogger();
        var result = await InvoiceLinks.ReadRowInvoiceLinksAsync(
            "sprk_todo", new[] { atTheBound, cyclic, garbled, orphan }, store.Query, CancellationToken.None, logger, "assistant");

        Decided(result[atTheBound], invoice);
        result[atTheBound].Reasons.Should().Be(InvoiceLinks.UndecidableReason.None);
        result[cyclic].Reasons.Should().Be(InvoiceLinks.UndecidableReason.Cycle);
        result[garbled].Reasons.Should().Be(InvoiceLinks.UndecidableReason.Unreadable);
        result[orphan].Reasons.Should().Be(InvoiceLinks.UndecidableReason.Unreadable, "a parent that does not come back");
        logger.PastTheBound.Should().BeEmpty();
        logger.Entries.Should().BeEmpty("the walk logs only the bound");
    }

    // ── Part 3: the assistant tools' seam ────────────────────────────────────────────────────────────────────

    /// <summary>F3b fix round 1: a seam row of <paramref name="parentTable"/> that leads to <paramref name="invoice"/> (its own link, or an
    /// agreement's document), or to none.</summary>
    private static Guid SeamParent(InvoiceWorld world, string parentTable, Guid? invoice)
    {
        if (InvoiceLinks.Tables.ContainsKey(parentTable))
        {
            return invoice is { } i
                ? world.LinkedChildRow(parentTable, (InvoiceLinks.LinkColumnsOf(parentTable)[0], i))
                : world.LinkedChildRow(parentTable);
        }

        var edge = InvoiceLinks.ParentEdgesOf(parentTable).First(e => InvoiceLinks.Tables.ContainsKey(e.ParentTable));
        return world.DescendantRow(parentTable, (edge.Column, SeamParent(world, edge.ParentTable, invoice)));
    }

    [Theory(DisplayName = "F3b fix round 1 seam: a row with a null own link filed under a row (or a grandparent) of another firm's invoice is hidden; its own firm's, an unlinked parent's and a plain row are shown; one firm read")]
    [MemberData(nameof(LinkedTableEdgeCases))]
    public async Task ALinkedRow_FollowsTheInvoiceOfEveryRowItIsFiledUnder(string table, string column, string parentTable)
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var other = world.OwnedInvoice();
        world.InvoiceFirm(other, InvoiceFamilyFirmWorld.OtherFirm);
        var ofOther = world.LinkedChildRow(table, (column, SeamParent(world, parentTable, other)));
        var ofOwn = world.LinkedChildRow(table, (column, SeamParent(world, parentTable, own)));
        var ofUnlinked = world.LinkedChildRow(table, (column, SeamParent(world, parentTable, null)));
        var plain = world.LinkedChildRow(table);
        var asked = new List<Guid> { ofOther, ofOwn, ofUnlinked, plain };
        var expected = new List<Guid> { ofOther };

        // Must fire within the bound: the parent has no link of its own; the GRANDPARENT is another firm's.
        var grandEdge = InvoiceLinks.ParentEdgesOf(parentTable).FirstOrDefault(e => InvoiceLinks.Tables.ContainsKey(e.ParentTable));
        if (InvoiceLinks.Tables.ContainsKey(parentTable) && grandEdge is not null)
        {
            var parent = world.LinkedChildRow(parentTable, (grandEdge.Column, SeamParent(world, grandEdge.ParentTable, other)));
            var viaGrandparent = world.LinkedChildRow(table, (column, parent));
            asked.Add(viaGrandparent);
            expected.Add(viaGrandparent);
        }

        var hidden = await HideAsync(world, world.NewFamilyCaller(), table, asked.ToArray());

        hidden.Should().BeEquivalentTo(expected);
        world.CallerFirmReads.Should().Be(1);
    }

    [Fact(DisplayName = "F3b fix round 1 seam: a self-edge cycle (an event regarding an event regarding the first) hides only the cyclic row")]
    public async Task ASelfEdgeCycle_IsHiddenAlone()
    {
        var world = new InvoiceWorld();
        var first = world.LinkedChildRow("sprk_event");
        var second = world.LinkedChildRow("sprk_event", ("sprk_regardingevent", first));
        world.SetRow("sprk_events", "sprk_eventid", first, "_sprk_regardingevent_value", second.ToString());
        var fine = world.LinkedChildRow("sprk_event");

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_event", first, fine);

        hidden.Should().BeEquivalentTo(new[] { first });
    }

    [Fact(DisplayName = "F3b fix round 1 seam firms: a to-do whose only invoice is its event's is hidden from a caller in no firm, and fails the call when firms cannot be read")]
    public async Task ALinkedRowsInheritedInvoice_FollowsTheFirmRulings()
    {
        var world = new InvoiceWorld();
        var invoice = world.OwnedInvoice();
        var todo = world.LinkedChildRow("sprk_todo", ("sprk_regardingevent", SeamParent(world, "sprk_event", invoice)));

        (await HideAsync(world, new InvoiceFamilyCaller(null), "sprk_todo", todo)).Should().BeEquivalentTo(new[] { todo });

        var faulted = new InvoiceWorld { CallerFirmsFault = new TimeoutException("firms (test)") };
        var faultedInvoice = faulted.OwnedInvoice();
        var faultedTodo = faulted.LinkedChildRow("sprk_todo", ("sprk_regardingevent", SeamParent(faulted, "sprk_event", faultedInvoice)));
        var act = () => HideAsync(faulted, faulted.NewFamilyCaller(), "sprk_todo", faultedTodo);
        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

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

        var underHidden = Chain(hiddenTop, InvoiceLinks.MaxWalkHops);
        var underVisible = Chain(visibleTop, InvoiceLinks.MaxWalkHops);
        var tooLong = Chain(visibleTop, InvoiceLinks.MaxWalkHops + 1);

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_document", underHidden, underVisible, tooLong);

        hidden.Should().BeEquivalentTo(new[] { underHidden, tooLong }, "the hop at the bound decides; one more is past the bound and fails closed");
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
