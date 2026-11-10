using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 F3b, the REST plane: a document that hangs from another document (<c>sprk_parentdocument</c>) is decided by
/// EVERY invoice link up its parent chain, as the assistant tools decide it (hop bound 3, the 188 narrowing's). A child of a document
/// linked to another firm's invoice is hidden on the list and the module fetch and refused on the single reads; a decision that
/// cannot be made is a dropped row on a list (200, <c>truncated</c>) and the invoice-unreadable 503 on a single read.
/// </summary>
/// <remarks>The REAL routes and <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/> (every
/// read is projected through its <c>$select</c>), with the REAL caller-aware invoice seam over <see cref="InvoiceWorld"/>.</remarks>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceLinkedDescendantRouteTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid Matter = Guid.Parse("13000000-0000-4000-8000-00000000002a");
    private static readonly Guid OtherMatter = Guid.Parse("13000000-0000-4000-8000-00000000002b");
    private static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b3");

    private const string InvoiceColumn = "_sprk_invoice_value";
    private const string ChainSelect = "$select=sprk_documentid,_sprk_invoice_value,_sprk_relatedinvoice_value,_sprk_parentdocument_value";

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;
    private readonly InvoiceWorld _world = new();
    private readonly InvoiceHidingStub _hiding;
    private readonly Guid _own;
    private readonly Guid _other;

    public ExternalInvoiceLinkedDescendantRouteTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = CallerOn(Matter);
        _own = _world.OwnedInvoice();
        _other = _world.OwnedInvoice();
        _world.InvoiceFirm(_other, OtherFirm);
        _fixture.Data.AddInvoice(_own, Matter, null);
        _fixture.Data.AddInvoice(_other, Matter, null);
        _hiding = InvoiceHidingStub.Real(_world);
        _fixture.InvoiceHiding = _hiding;
    }

    public static TheoryData<string> Routes => new() { "content", "versions" };

    // =====================================================================
    // Seeding: an ancestor is a document NOT filed to the matter (so it is never listed); a child is filed to it
    // =====================================================================

    /// <summary>An ancestor document with its own parent and (optionally) an invoice link (<paramref name="raw"/>: a stored value that
    /// is not a GUID).</summary>
    private Guid Ancestor(Guid? parent = null, Guid? invoice = null, object? raw = null)
    {
        var id = Guid.NewGuid();
        _fixture.Data.AddAncestorDocument(id, accessPermission: null, communication: null, parent: parent);
        // Filed under the matter in the filing world, so the root rule's live-parent walk passes it: what decides a descendant is then
        // the invoice rule alone.
        _fixture.Data.AddFilingRow("sprk_document", id, ("sprk_matter", "sprk_matter", Matter));
        if (invoice is { } inv)
            _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", id.ToString(), InvoiceColumn, inv.ToString());
        if (raw is not null)
            _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", id.ToString(), InvoiceColumn, raw);
        return id;
    }

    /// <summary>A chain of <paramref name="ancestors"/> documents above a child; the TOP one carries <paramref name="topInvoice"/>.
    /// Returns the child's parent (hop 1).</summary>
    private Guid ChainOf(int ancestors, Guid? topInvoice)
    {
        Guid? above = null;
        for (var i = ancestors; i >= 1; i--)
            above = Ancestor(above, i == ancestors ? topInvoice : null);
        return above!.Value;
    }

    /// <summary>A document filed to the matter, hanging from <paramref name="parent"/> (none: a plain document).</summary>
    private string Child(string name, Guid? parent = null, params (string Column, Guid Value)[] own)
    {
        var id = _fixture.Data.AddDocument("matters", Matter, name, own);
        if (parent is { } p)
            _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", id, "_sprk_parentdocument_value", p.ToString());
        return id;
    }

    // =====================================================================
    // The matter's document list
    // =====================================================================

    [Fact(DisplayName = "F3b REST list (must fire / must not fire): a child of a document linked to another firm's invoice is absent; a child of the caller's own firm's, of an unlinked parent, and a parentless document are listed")]
    public async Task List_AChildFollowsItsParentsInvoice()
    {
        Child("Under other firm's", Ancestor(invoice: _other));
        Child("Under own firm's", Ancestor(invoice: _own));
        Child("Under unlinked", Ancestor());
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Should().BeEquivalentTo("Under own firm's", "Under unlinked", "Parentless");
        body.Truncated.Should().BeFalse("a row the own-firms rule hides is an answer, not a read failure");
        _hiding.Calls.Should().ContainSingle("one invoice decision per page").Which.Should().BeEquivalentTo(new[] { _other, _own });
    }

    [Fact(DisplayName = "F3b REST list (must fire): a child is decided by EVERY invoice on its chain - its own firm's link plus another firm's on the parent hides it")]
    public async Task List_ADocumentWithAnOwnLinkAndAParentOnAnotherFirm_IsHidden()
    {
        Child("Both", Ancestor(invoice: _other), (InvoiceColumn, _own));
        Child("Own only", Ancestor(), (InvoiceColumn, _own));

        var body = await ListAsync();

        Names(body).Should().Equal("Own only");
    }

    /// <summary>EB-D-215: a document's PARENT DOCUMENT chain is also walked by the task 188 narrowing, which keeps its own bound
    /// (<see cref="ExternalDocumentNarrowing.MaxHops"/>, 3) and hides a document whose chain needs a hop past it. So on the document
    /// routes a parent-document chain is bounded by the narrowing, below the invoice walk's <see cref="InvoiceLinks.MaxWalkHops"/>.
    /// The invoice walk's own bound is pinned on the to-do routes (<c>ExternalInvoiceLinkedFamilyRouteTests</c>).</summary>
    [Theory(DisplayName = "F3b REST list (hop bound): a parent-document chain as long as the narrowing's bound is decided by its top; one more is hidden by the narrowing (its own bound), even for the caller's own firm's invoice")]
    [InlineData(ExternalDocumentNarrowing.MaxHops, true, false)]      // top is another firm's: hidden
    [InlineData(ExternalDocumentNarrowing.MaxHops, false, true)]      // top is the caller's own firm's: shown
    [InlineData(ExternalDocumentNarrowing.MaxHops + 1, false, false)] // past the narrowing's bound: hidden although the invoice at the top is visible
    public async Task List_TheParentDocumentChainIsBoundedByTheNarrowing(int ancestors, bool topIsOtherFirms, bool shown)
    {
        Child("Chained", ChainOf(ancestors, topIsOtherFirms ? _other : _own));
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Contains("Chained").Should().Be(shown);
        Names(body).Should().Contain("Parentless");
    }

    [Fact(DisplayName = "F3b REST list (cycle): a child whose chain loops back on itself is hidden; a parentless document is listed")]
    public async Task List_ACycleOnTheChain_IsHidden()
    {
        var first = Ancestor();
        var second = Ancestor(parent: first);
        _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", first.ToString(), "_sprk_parentdocument_value", second.ToString());
        Child("Cyclic", second);
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Should().Equal("Parentless");
    }

    [Fact(DisplayName = "F3b REST list (fault): a chain read that fails drops the documents that have a parent, says truncated, and still answers 200; a parentless document stays")]
    public async Task List_WhenTheChainReadFails_DropsTheChainedRows_AndSaysTruncated()
    {
        _fixture.Data.KeyReadFaultWhenSelecting = "_sprk_invoice_value";
        Child("Chained", Ancestor(invoice: _own));
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Should().Equal("Parentless");
        body.Truncated.Should().BeTrue("a fault must not read as 'there are no more documents'");
    }

    [Fact(DisplayName = "F3b REST list (must not fire): a list of parentless documents reads no chain")]
    public async Task List_WithNoParent_ReadsNoChain()
    {
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Should().Equal("Parentless");
        _fixture.Data.KeyReads("sprk_documents").Should().NotContain(u => u.Contains("_sprk_invoice_value", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "F3b REST list pin: the chain read selects the id, both invoice links and the parent document, one batched read per hop")]
    public async Task List_TheChainRead_SelectsTheIdTheLinksAndTheParent()
    {
        Child("Chained", ChainOf(2, _own));

        await ListAsync();

        var reads = _fixture.Data.KeyReads("sprk_documents").Where(u => u.Contains(ChainSelect, StringComparison.Ordinal)).ToList();
        reads.Should().HaveCount(2, "one read for the parent (hop 1) and one for the grandparent (hop 2)");
    }

    [Fact(DisplayName = "F3b REST list firms (unknown firms, must fire): when the caller's firms cannot be read, a chained row is hidden and the list says truncated; a parentless document stays")]
    public async Task List_UnknownFirms_HideTheChainedRows_AndSayTruncated()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        Child("Chained", Ancestor(invoice: _own));
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Should().Equal("Parentless");
        body.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "F3b REST list firms (no firm, must fire): a caller known to be in no firm sees no chained row, and it is an answer (not truncated)")]
    public async Task List_ACallerInNoFirm_SeesNoChainedRow_WithoutAFault()
    {
        _world.CallerFirmsAnswer = CallerFirms.From(Array.Empty<Guid>(), unreadable: false);
        Child("Chained", Ancestor(invoice: _own));
        Child("Parentless");

        var body = await ListAsync();

        Names(body).Should().Equal("Parentless");
        body.Truncated.Should().BeFalse();
    }

    [Fact(DisplayName = "F3b REST list firms (per-row fault, must fire): with the firms known, one invoice's failed decision hides that row (truncated); a row on another invoice is decided on its own")]
    public async Task List_APerRowFault_HidesThatRowOnly()
    {
        _fixture.InvoiceHiding = InvoiceHidingStub.PerInvoiceFault(_other);
        Child("Faulted", Ancestor(invoice: _other));
        Child("Own", Ancestor(invoice: _own));

        var body = await ListAsync();

        Names(body).Should().Equal("Own");
        body.Truncated.Should().BeTrue();
    }

    // =====================================================================
    // The single-document routes (content download, version history)
    // =====================================================================

    [Theory(DisplayName = "F3b REST single read (must fire): a child of a document linked to another firm's invoice is refused as not under the record, before any pointer is resolved")]
    [MemberData(nameof(Routes))]
    public async Task Single_AChildOfAnotherFirmsDocument_IsRefused(string route)
    {
        var reference = await RefusalOf(route);
        _fixture.ResetCalls();
        var id = Child("Under other firm's", Ancestor(invoice: _other));

        using var response = await GetAsync(id, route);

        (await Summarise(response)).Should().Be(reference);
        _fixture.PointerResolutions.Should().Be(0, "authorization precedes pointer resolution");
        _fixture.FileReads.Should().Be(0);
        _hiding.Calls.Should().ContainSingle().Which.Should().Equal(_other);
    }

    [Theory(DisplayName = "F3b REST single read (must not fire): a child of the caller's own firm's document, and one of an unlinked parent, are served")]
    [MemberData(nameof(Routes))]
    public async Task Single_AChildOfAVisibleOrUnlinkedDocument_IsServed(string route)
    {
        var own = Child("Under own firm's", Ancestor(invoice: _own));
        var unlinked = Child("Under unlinked", Ancestor());

        using (var response = await GetAsync(own, route))
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using (var response = await GetAsync(unlinked, route))
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Theory(DisplayName = "F3b REST single read (must fire): EVERY invoice on the chain counts - another firm's link three hops up refuses the document")]
    [MemberData(nameof(Routes))]
    public async Task Single_AnotherFirmsInvoiceThreeHopsUp_IsRefused(string route)
    {
        var reference = await RefusalOf(route);
        _fixture.ResetCalls();
        var id = Child("Deep", ChainOf(3, _other), (InvoiceColumn, _own));

        using var response = await GetAsync(id, route);

        (await Summarise(response)).Should().Be(reference);
        _fixture.PointerResolutions.Should().Be(0);
    }

    [Theory(DisplayName = "F3b REST single read (must not fire): a parentless document with no invoice is served and asks no invoice decision and no chain read")]
    [MemberData(nameof(Routes))]
    public async Task Single_AParentlessDocument_AsksNothing(string route)
    {
        var id = Child("Parentless", parent: null);

        using var response = await GetAsync(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _hiding.Calls.Should().BeEmpty();
        _fixture.Data.KeyReads("sprk_documents").Should().NotContain(u => u.Contains("_sprk_invoice_value", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "F3b REST single read (undecidable, must fire): an invoice link on the chain that is not a GUID makes the decision impossible - the invoice-unreadable 503, not the refusal and not the file")]
    [MemberData(nameof(Routes))]
    public async Task Single_AMalformedLinkOnTheChain_IsThe503(string route)
    {
        var id = Child("Undecidable", Ancestor(raw: "not-a-guid"));

        using var response = await GetAsync(id, route);

        await AssertUnreadable503(response);
        _fixture.PointerResolutions.Should().Be(0);
        _fixture.FileReads.Should().Be(0);
    }

    [Theory(DisplayName = "F3b REST single read (undecidable, must fire): a chain read that fails is the invoice-unreadable 503")]
    [MemberData(nameof(Routes))]
    public async Task Single_WhenTheChainReadFails_IsThe503(string route)
    {
        _fixture.Data.KeyReadFaultWhenSelecting = "_sprk_invoice_value";
        var id = Child("Chained", Ancestor(invoice: _own));

        using var response = await GetAsync(id, route);

        await AssertUnreadable503(response);
        _fixture.PointerResolutions.Should().Be(0);
    }

    [Theory(DisplayName = "F3b REST single read (undecidable, decided hidden wins): a chain with another firm's invoice AND an undecidable link is still the refusal, never the 503")]
    [MemberData(nameof(Routes))]
    public async Task Single_ADecidedHiddenLink_BeatsAnUndecidableOne(string route)
    {
        var reference = await RefusalOf(route);
        _fixture.ResetCalls();
        var id = Child("Both", Ancestor(parent: Ancestor(raw: "not-a-guid"), invoice: _other));

        using var response = await GetAsync(id, route);

        (await Summarise(response)).Should().Be(reference);
    }

    [Theory(DisplayName = "F3b REST single read firms (must fire): unknown firms are the 503, a caller in no firm is the refusal, a failed decision for one invoice is the 503")]
    [InlineData("unknown", 503)]
    [InlineData("nofirm", 403)]
    [InlineData("perinvoice", 503)]
    public async Task Single_TheFirmRulings_OnAChainedDocument(string state, int expected)
    {
        switch (state)
        {
            case "unknown": _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test)."); break;
            case "nofirm": _world.CallerFirmsAnswer = CallerFirms.From(Array.Empty<Guid>(), unreadable: false); break;
            default: _fixture.InvoiceHiding = InvoiceHidingStub.PerInvoiceFault(_own); break;
        }

        var id = Child("Chained", Ancestor(invoice: _own));

        using var response = await GetAsync(id, "content");

        ((int)response.StatusCode).Should().Be(expected, await response.Content.ReadAsStringAsync());
        _fixture.PointerResolutions.Should().Be(0);
        if (expected == 503)
            await AssertUnreadable503(response);
    }

    // =====================================================================
    // The documents module (/fetch)
    // =====================================================================

    private const string DocumentGridFetch =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>"
        + "<attribute name='sprk_matter'/></entity></fetch>";

    [Fact(DisplayName = "F3b module fetch (must fire / must not fire): a child of another firm's document is absent; a child of the caller's own firm's, of an unlinked parent, and a parentless row are returned")]
    public async Task ModuleFetch_AChildFollowsItsParentsInvoice()
    {
        var (body, executed) = await RunDocumentsFetchAsync(
            Row("Under other firm's", Ancestor(invoice: _other)), Row("Under own firm's", Ancestor(invoice: _own)),
            Row("Under unlinked", Ancestor()), Row("Parentless", null));

        body.Entities.Select(r => r["sprk_documentname"]).Should().BeEquivalentTo(new[] { "Under own firm's", "Under unlinked", "Parentless" });
        body.Truncated.Should().BeFalse();
        executed.Should().Contain("name=\"sprk_parentdocument\"", "the parent is read server-side to walk the chain");
        body.Entities.Should().OnlyContain(r => !r.ContainsKey("sprk_parentdocument"), "the parent is read to decide the row, never returned unasked");
    }

    [Fact(DisplayName = "F3b module fetch (hop bound): a parent-document chain one ancestor past the narrowing's bound is hidden; one at that bound is decided by its top")]
    public async Task ModuleFetch_TheParentDocumentChainIsBoundedByTheNarrowing()
    {
        // As List_TheParentDocumentChainIsBoundedByTheNarrowing: the narrowing's own bound applies to a parent-document chain.
        var (body, _) = await RunDocumentsFetchAsync(
            Row("At the bound, own", ChainOf(ExternalDocumentNarrowing.MaxHops, _own)),
            Row("At the bound, other", ChainOf(ExternalDocumentNarrowing.MaxHops, _other)),
            Row("Past the bound, own", ChainOf(ExternalDocumentNarrowing.MaxHops + 1, _own)));

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("At the bound, own");
    }

    [Fact(DisplayName = "F3b module fetch (fault): a chain read that fails drops the rows that have a parent and says truncated; a parentless row stays")]
    public async Task ModuleFetch_WhenTheChainReadFails_DropsTheChainedRows_AndSaysTruncated()
    {
        _fixture.Data.KeyReadFaultWhenSelecting = "_sprk_invoice_value";

        var (body, _) = await RunDocumentsFetchAsync(Row("Chained", Ancestor(invoice: _own)), Row("Parentless", null));

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Parentless");
        body.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "F3b module fetch (no reader wired, fail closed): a row that has a parent is hidden and the page says truncated; a parentless row stays")]
    public async Task ModuleFetch_WithNoChainReader_HidesTheChainedRows()
    {
        var (body, _) = await RunDocumentsFetchAsync(
            wireChainReader: false, Row("Chained", Ancestor(invoice: _own)), Row("Parentless", null));

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Parentless");
        body.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "F3b module fetch (must not fire): a page of parentless rows reads no chain and is not truncated")]
    public async Task ModuleFetch_WithNoParents_ReadsNoChain()
    {
        var (body, _) = await RunDocumentsFetchAsync(Row("Parentless", null));

        body.Entities.Should().ContainSingle();
        body.Truncated.Should().BeFalse();
        _fixture.Data.KeyReads("sprk_documents").Should().NotContain(u => u.Contains("_sprk_invoice_value", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "F3b module fetch firms (must fire): unknown firms hide the chained rows and say truncated; a caller in no firm hides them without truncating")]
    public async Task ModuleFetch_TheFirmRulings()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var (unknown, _) = await RunDocumentsFetchAsync(Row("Chained", Ancestor(invoice: _own)), Row("Parentless", null));
        unknown.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Parentless");
        unknown.Truncated.Should().BeTrue();

        _world.CallerFirmsFault = null;
        _world.CallerFirmsAnswer = CallerFirms.From(Array.Empty<Guid>(), unreadable: false);
        var (none, _) = await RunDocumentsFetchAsync(Row("Chained", Ancestor(invoice: _own)), Row("Parentless", null));
        none.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Parentless");
        none.Truncated.Should().BeFalse();
    }

    [Fact(DisplayName = "F3b module fetch pin: the module chain read selects the id, both invoice links and the parent document")]
    public async Task ModuleFetch_TheChainRead_IsPinned()
    {
        await RunDocumentsFetchAsync(Row("Chained", Ancestor(invoice: _own)));

        _fixture.Data.KeyReads("sprk_documents").Should().Contain(u => u.Contains(ChainSelect, StringComparison.Ordinal));
    }

    // =====================================================================
    // The registry refuses a module on a descendant-only table
    // =====================================================================

    public static TheoryData<string> DescendantOnlyTables
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var table in InvoiceLinks.Descendants.Keys.Where(InvoiceLinks.IsDescendantOnly).Order(StringComparer.Ordinal))
                data.Add(table);
            return data;
        }
    }

    [Theory(DisplayName = "F3b registry (must fire): a module on a descendant-only table is refused at registration, for every derived descendant")]
    [MemberData(nameof(DescendantOnlyTables))]
    public void TheRegistry_RefusesAModuleOnADescendantOnlyTable(string table)
    {
        var registry = new ExternalModuleRegistry();

        var act = () => registry.Register(ModuleOn(table));

        act.Should().Throw<ArgumentException>().WithMessage($"*{table}*descendant*");
    }

    [Fact(DisplayName = "F3b registry (must not fire): a module on a table with its own invoice link, and the shipped registry, register")]
    public void TheRegistry_AcceptsAModuleWithItsOwnLink_AndTheShippedRegistryStillBuilds()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(ModuleOn("sprk_document"));
        registry.Register(ModuleOn("sprk_workassignment"));

        registry.Modules.Select(m => m.RecordEntity).Should().BeEquivalentTo("sprk_document", "sprk_workassignment");
        Registry().Modules.Should().NotBeEmpty("the production modules are all decided by the rule");
        Registry().Modules.Should().NotContain(m => InvoiceLinks.IsDescendantOnly(m.RecordEntity));
    }

    private static ExternalModuleDescriptor ModuleOn(string table) => new()
    {
        Name = $"test-{table}",
        RecordEntity = table,
        RecordIdAttribute = table + "id",
        AccessibleRecordIds = p => p.GetAccessibleWorkAssignmentIds(),
        PrimaryNameAttribute = "sprk_name",
        ReadableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { table + "id", "sprk_name" },
    };

    // =====================================================================
    // Helpers
    // =====================================================================

    private static IReadOnlyList<string?> Names(ExternalCollectionResponse<ExternalDocumentDto> body) =>
        body.Value.Select(d => d.SprkName).ToList();

    private async Task<ExternalCollectionResponse<ExternalDocumentDto>> ListAsync()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Matter}/documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExternalCollectionResponse<ExternalDocumentDto>>())!;
    }

    private async Task<HttpResponseMessage> GetAsync(string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/matters/{Matter}/documents/{documentId}/{route}");
    }

    private async Task<string> RefusalOf(string route)
    {
        var elsewhere = _fixture.Data.AddDocument("matters", OtherMatter, "Another record's");
        using var response = await GetAsync(elsewhere, route);
        var summary = await Summarise(response);
        summary.Should().StartWith("403");
        return summary;
    }

    private static async Task AssertUnreadable503(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("reasonCode").GetString().Should().Be(ExternalProjectDataEndpoints.InvoiceUnreadableReasonCode);
    }

    /// <summary>A refusal as a caller sees it: status, title and detail (no per-request fields).</summary>
    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return $"{(int)response.StatusCode}";

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|", new[] { "403" }.Concat(doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }

    private static IReadOnlyDictionary<string, object?> Row(string name, Guid? parent)
    {
        var row = new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_document",
            ["sprk_documentid"] = Guid.NewGuid(),
            ["sprk_documentname"] = name,
            ["sprk_matter"] = Matter,
        };
        if (parent is { } p)
            row["sprk_parentdocument"] = p;
        return row;
    }

    private Task<(FetchResponseDto Body, string Executed)> RunDocumentsFetchAsync(params IReadOnlyDictionary<string, object?>[] rows) =>
        RunDocumentsFetchAsync(wireChainReader: true, rows);

    /// <summary>The real documents-module fetch, with the REAL secure-flag, narrowing and live-parent reads over FakeDataverse, the REAL
    /// caller-aware invoice seam and (unless <paramref name="wireChainReader"/> is false) the REAL chain reader. The double returns each
    /// row with only the attributes the executed FetchXML names.</summary>
    private async Task<(FetchResponseDto Body, string Executed)> RunDocumentsFetchAsync(
        bool wireChainReader, params IReadOnlyDictionary<string, object?>[] rows)
    {
        var executed = string.Empty;
        var data = _fixture.Data.NewService();
        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_document", DocumentGridFetch, PagingCookie: null),
            CallerOn(Matter), Registry(), new FetchXmlEntityExtractor(),
            (request, _) =>
            {
                executed = request.FetchXml;
                var names = System.Xml.Linq.XDocument.Parse(request.FetchXml).Descendants("attribute")
                    .Select(a => (string?)a.Attribute("name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(new FetchResponseDto(
                    rows.Select(r => (IReadOnlyDictionary<string, object?>)r
                            .Where(kv => kv.Key == "@logicalName" || (kv.Value is not null && names.Contains(kv.Key)))
                            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase))
                        .ToList(),
                    MoreRecords: false, PagingCookie: null));
            },
            data.GetNonSecureRootsAsync, data.NarrowDocumentsAsync, NullLogger.Instance, CancellationToken.None,
            data.GetLiveParentRootsAsync, _hiding.Seam,
            wireChainReader ? data.ReadModuleDocumentAncestorLinksAsync : null);
        return (result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!, executed);
    }

    // ebilling-r1 task 130-0b F3b: the service requests module's single-record route (/data/record/sprk_servicerequest/{id}). The
    // module's scope is the REQUESTER (sprk_requestedby), so the Tier-2 gate compares the request id with the caller's contact id and
    // refuses every request id before any read: it has no row to decide by an invoice (the same shape as the documents module).
    [Fact(DisplayName = "F3b service request: the module's single-record route refuses a request id before any read, so it returns no linked row")]
    public async Task ServiceRequestModule_SingleRecordRead_IsRefusedForARequestId_BeforeAnyRead()
    {
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };
        var registry = Registry();
        var request = Guid.NewGuid();
        var reads = 0;

        registry.FindByEntity("sprk_servicerequest")!.IsRecordAccessible(caller, request).Should().BeFalse();
        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            "sprk_servicerequest", request, select: null, caller, registry,
            (_, _, _, _) => { reads++; return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>()); },
            NullLogger.Instance, CancellationToken.None, _hiding.Seam);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        reads.Should().Be(0, "no request row is read, so none can be returned with an invoice link");
    }

    private static ExternalModuleRegistry Registry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
    }

    private static CallerPrincipal CallerOn(Guid matter) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [matter] = AccessRights.Read },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}
