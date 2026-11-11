using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b: <see cref="InvoiceLinks"/>, the one answer to "which invoices does a row link to" (a partner sees only invoices
/// of its own firms, and a row that belongs to an invoice follows it). The guard holds it to the edge registries: every table with an edge
/// to <c>sprk_invoice</c> is covered, with every such column, and can be read.
/// </summary>
/// <remarks>Maintain-class (ADR-038): a new invoice edge that the helper missed would let a row about another firm's invoice through.</remarks>
[Trait("category", "external-access")]
public sealed class InvoiceLinksTests
{
    private const string Invoice = "sprk_invoice";

    // ── The guard ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every invoice edge of the registries the helper does not cover, and every covered table it cannot read.</summary>
    private static IReadOnlyList<string> Uncovered(
        IEnumerable<RecordFamilyEdges.Edge> edges, IReadOnlyDictionary<string, SecureChildLineage.Table> lineage,
        IReadOnlyDictionary<string, InvoiceLinks.LinkedTable> tables)
    {
        var required = edges.Where(e => e.FamilyKind == RecordFamilyEdges.FamilyKind.Filing && e.ParentTable == Invoice)
            .Select(e => (Table: e.ChildTable, Column: e.LookupColumn))
            .Concat(lineage.Values.SelectMany(t => t.Lookups.Where(l => l.Value == Invoice).Select(l => (Table: t.LogicalName, Column: l.Key))))
            .Distinct()
            .ToList();
        var problems = required
            .Where(r => !tables.TryGetValue(r.Table, out var linked) || !linked.LinkColumns.Contains(r.Column))
            .Select(r => $"NOT COVERED: {r.Table}.{r.Column}")
            .ToList();
        problems.AddRange(tables.Values.Where(t => t.EntitySet is null).Select(t => $"NO READ SHAPE: {t.LogicalName}"));
        // A root is redacted, never hidden; a child is hidden. Each table must be classed as what it is.
        problems.AddRange(tables.Values.Where(t => t.IsRoot != SecureChildLineage.IsRoot(t.LogicalName))
            .Select(t => $"WRONG CLASS: {t.LogicalName} IsRoot={t.IsRoot}"));
        return problems.Order(StringComparer.Ordinal).ToList();
    }

    private static RecordFamilyEdges.Edge Edge(string child, string column, string parent) =>
        new(RecordFamilyEdges.FamilyKind.Filing, child, column, parent, RecordFamilyEdges.EdgeKind.Stamp, InSecureFamily: true, null);

    [Fact(DisplayName = "130-0b guard: every table with an invoice edge in the registries is covered, with each invoice column, and can be read")]
    public void EveryInvoiceEdgeOfTheRegistries_IsCovered()
    {
        Uncovered(RecordFamilyEdges.All, SecureChildLineage.Children, InvoiceLinks.Tables).Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0b guard must fire: a seeded new invoice edge the helper does not cover fails, and a table with an invoice edge but no read shape fails")]
    public void Guard_MustFire_OnASeededInvoiceEdge()
    {
        var seeded = RecordFamilyEdges.All.Append(Edge("sprk_seededtable", "sprk_regardinginvoice", Invoice)).ToList();

        Uncovered(seeded, SecureChildLineage.Children, InvoiceLinks.Tables)
            .Should().Equal("NOT COVERED: sprk_seededtable.sprk_regardinginvoice");

        // Built from the seeded registry, the table is covered, but its rows cannot be read (no entity set): the guard still fails.
        var rebuilt = InvoiceLinks.Build(seeded, SecureChildLineage.Children);
        rebuilt["sprk_seededtable"].LinkColumns.Should().Equal("sprk_regardinginvoice");
        Uncovered(seeded, SecureChildLineage.Children, rebuilt).Should().Equal("NO READ SHAPE: sprk_seededtable");
    }

    [Fact(DisplayName = "130-0b guard must not fire: a table with no invoice edge is not listed and passes, and a seeded edge to another parent adds nothing")]
    public void Guard_MustNotFire_OnATableWithNoInvoiceEdge()
    {
        InvoiceLinks.HasInvoiceLink("sprk_budget").Should().BeFalse();
        InvoiceLinks.LinkColumnsOf("sprk_budget").Should().BeEmpty();
        var seeded = RecordFamilyEdges.All.Append(Edge("sprk_seededtable", "sprk_regardingmatter", "sprk_matter")).ToList();

        Uncovered(seeded, SecureChildLineage.Children, InvoiceLinks.Tables).Should().BeEmpty();
        InvoiceLinks.Build(seeded, SecureChildLineage.Children).Should().NotContainKey("sprk_seededtable");
    }

    [Theory(DisplayName = "130-0b the rows that belong to an invoice outside the invoice family are covered, each with its invoice columns")]
    [InlineData("sprk_todo", "sprk_todos", "sprk_regardinginvoice")]
    [InlineData("sprk_event", "sprk_events", "sprk_regardinginvoice")]
    [InlineData("sprk_document", "sprk_documents", "sprk_invoice,sprk_relatedinvoice")]
    [InlineData("sprk_communication", "sprk_communications", "sprk_regardinginvoice")]
    [InlineData("sprk_analysis", "sprk_analysises", "sprk_regardinginvoice")]
    [InlineData("sprk_memo", "sprk_memos", "sprk_regardinginvoice")]
    public void TheRowsThatBelongToAnInvoice_AreCovered(string table, string entitySet, string columns)
    {
        InvoiceLinks.HasInvoiceLink(table).Should().BeTrue();
        InvoiceLinks.Tables[table].EntitySet.Should().Be(entitySet);
        InvoiceLinks.Tables[table].IdColumn.Should().Be(table + "id");
        InvoiceLinks.LinkColumnsOf(table).Should().BeEquivalentTo(columns.Split(','));
    }

    [Fact(DisplayName = "130-0b a root with an invoice link is classed as a root (its link is redacted, the row is never hidden); a child is not")]
    public void ARootWithAnInvoiceLink_IsARoot_AndAChildIsNot()
    {
        InvoiceLinks.Tables["sprk_workassignment"].IsRoot.Should().BeTrue();
        InvoiceLinks.Tables["sprk_workassignment"].EntitySet.Should().Be("sprk_workassignments");
        InvoiceLinks.LinkColumnsOf("sprk_workassignment").Should().Equal("sprk_regardinginvoice");
        InvoiceLinks.IsRootWithInvoiceLink("sprk_workassignment").Should().BeTrue();
        InvoiceLinks.IsRootWithInvoiceLink("sprk_todo").Should().BeFalse();
        InvoiceLinks.IsRootWithInvoiceLink("sprk_matter").Should().BeFalse("a root with no invoice link is not listed");
        InvoiceLinks.Tables.Values.Where(t => !t.IsRoot).Should().NotContain(t => SecureChildLineage.IsRoot(t.LogicalName));
    }

    [Fact(DisplayName = "130-0b guard must fire: a seeded invoice edge on a root is classed as a root, and a table misclassed is reported")]
    public void Guard_ClassesARootWithASeededInvoiceEdge()
    {
        var seeded = RecordFamilyEdges.All.Append(Edge("sprk_matter", "sprk_regardinginvoice", Invoice)).ToList();

        var rebuilt = InvoiceLinks.Build(seeded, SecureChildLineage.Children);
        rebuilt["sprk_matter"].IsRoot.Should().BeTrue();
        rebuilt["sprk_matter"].EntitySet.Should().Be("sprk_matters");
        Uncovered(seeded, SecureChildLineage.Children, rebuilt).Should().BeEmpty();

        var misclassed = rebuilt.ToDictionary(kv => kv.Key, kv => kv.Key == "sprk_matter" ? kv.Value with { IsRoot = false } : kv.Value);
        Uncovered(seeded, SecureChildLineage.Children, misclassed).Should().Equal("WRONG CLASS: sprk_matter IsRoot=False");
    }

    // ── The read ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Rows
    {
        public readonly List<(string Set, string OData)> Reads = new();
        public readonly Dictionary<Guid, Dictionary<string, object?>> Seeded = new();
        public Exception? Fault;

        public Rows Row(Guid id, string idColumn, params (string Column, object? Value)[] values)
        {
            var row = new Dictionary<string, object?> { [idColumn] = id.ToString() };
            foreach (var (column, value) in values)
                row[column] = value;
            Seeded[id] = row;
            return this;
        }

        public Sprk.Bff.Api.Services.Communication.CommunicationOpenThreadGate.RowQuery Query => (set, odata, _) =>
        {
            lock (Reads) Reads.Add((set, odata));
            if (Fault is { } fault)
                throw fault;
            var asked = Seeded.Keys.Where(id => odata.Contains(id.ToString("D"), StringComparison.Ordinal));
            // Fix round 3d: the Web API answers a selected column that is not set as an explicit null (an absent column was not read).
            var selected = odata.Split("$select=", 2)[1].Split('&')[0].Split(',');
            IReadOnlyList<Dictionary<string, JsonElement>> rows = asked
                .Select(id =>
                {
                    var row = Seeded[id].ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
                    foreach (var column in selected)
                        row.TryAdd(column, JsonSerializer.SerializeToElement<object?>(null));
                    return row;
                })
                .ToList();
            return Task.FromResult(rows);
        };
    }

    [Fact(DisplayName = "130-0b a document's linked invoices come from both of its invoice links; a row with none links to none")]
    public async Task ReadLinkedInvoices_ReadsEveryInvoiceColumn()
    {
        var (both, one, none) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (invoiceA, invoiceB) = (Guid.NewGuid(), Guid.NewGuid());
        var rows = new Rows()
            .Row(both, "sprk_documentid", ("_sprk_invoice_value", invoiceA.ToString()), ("_sprk_relatedinvoice_value", invoiceB.ToString()))
            .Row(one, "sprk_documentid", ("_sprk_relatedinvoice_value", invoiceB.ToString()))
            .Row(none, "sprk_documentid");

        var linked = await InvoiceLinks.ReadLinkedInvoicesAsync("sprk_document", new[] { both, one, none }, rows.Query, CancellationToken.None);

        linked[both].Should().BeEquivalentTo(new[] { invoiceA, invoiceB });
        linked[one].Should().Equal(invoiceB);
        linked[none].Should().BeEmpty();
        rows.Reads.Should().ContainSingle().Which.Set.Should().Be("sprk_documents");
        // F3b: the select is pinned; a child document also carries its parent document (the chain the decision follows) and, since
        // F3b fix round 1, every other row it is filed under (agreement, communication, event, service request, to-do).
        rows.Reads[0].OData.Should().Contain("$select=sprk_documentid,_sprk_invoice_value,_sprk_relatedinvoice_value,_sprk_parentdocument_value,"
            + "_sprk_relatedagreement_value,_sprk_relatedcommunication_value,_sprk_relatedevent_value,_sprk_relatedservicerequest_value,"
            + "_sprk_relatedtodo_value&");
    }

    [Fact(DisplayName = "130-0b fail closed: a row that does not come back, or a link that is not a GUID, links to an invoice that cannot be decided")]
    public async Task ReadLinkedInvoices_UndecidableLinks_ReadAsUnreadable()
    {
        var (missing, garbled) = (Guid.NewGuid(), Guid.NewGuid());
        var rows = new Rows().Row(garbled, "sprk_todoid", ("_sprk_regardinginvoice_value", "not-a-guid"));

        var linked = await InvoiceLinks.ReadLinkedInvoicesAsync("sprk_todo", new[] { missing, garbled }, rows.Query, CancellationToken.None);

        linked[missing].Should().Equal(ExternalDocumentNarrowing.UnreadableLink);
        linked[garbled].Should().Equal(ExternalDocumentNarrowing.UnreadableLink);
    }

    [Fact(DisplayName = "130-0b a failed read throws (never 'no invoice'), and a table with no invoice link is refused")]
    public async Task ReadLinkedInvoices_FaultThrows_AndAnUnlinkedTableIsRefused()
    {
        var rows = new Rows { Fault = new TimeoutException("The read failed (test).") };

        var fault = () => InvoiceLinks.ReadLinkedInvoicesAsync("sprk_event", new[] { Guid.NewGuid() }, rows.Query, CancellationToken.None);
        var unlinked = () => InvoiceLinks.ReadLinkedInvoicesAsync("sprk_budget", new[] { Guid.NewGuid() }, rows.Query, CancellationToken.None);

        await fault.Should().ThrowAsync<TimeoutException>();
        await unlinked.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "130-0b one read per chunk of rows")]
    public async Task ReadLinkedInvoices_OneReadPerChunk()
    {
        var rows = new Rows();
        var ids = Enumerable.Range(0, Sprk.Bff.Api.Services.Communication.CommunicationOpenThreadGate.ChunkSize + 1).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
            rows.Row(id, "sprk_memoid");

        var linked = await InvoiceLinks.ReadLinkedInvoicesAsync("sprk_memo", ids, rows.Query, CancellationToken.None);

        rows.Reads.Should().HaveCount(2);
        linked.Values.Should().OnlyContain(invoices => invoices.Count == 0);
    }
}
