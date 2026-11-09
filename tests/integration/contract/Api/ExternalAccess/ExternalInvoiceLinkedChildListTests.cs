using System.Net;
using System.Net.Http.Json;
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
/// ebilling-r1 task 130-0 fix round 1: a partner sees only invoices of its own firms, and what belongs to them. A to-do, an event
/// or a document that belongs to an invoice is listed only when that invoice is visible to the caller, even when the row is also
/// filed under a record the caller can read; a row with no invoice link is unaffected. A root (a work assignment) is never hidden
/// by its invoice link: the link itself is shown only when that invoice is visible.
/// </summary>
/// <remarks>
/// Driven through the REAL routes and the REAL <see cref="ExternalDataService"/> over
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/> (every read is projected through its <c>$select</c>, so a list query that
/// does not read the invoice link sees no link and a must-fire test fails), with the REAL caller-aware invoice seam
/// (<see cref="ExternalDataService.HideInvoicesForAsync"/>) over <see cref="InvoiceWorld"/>: the caller's firm is
/// <see cref="InvoiceWorld.Firm"/>, and an invoice is another firm's when <see cref="InvoiceWorld.InvoiceFirm"/> says so.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceLinkedChildListTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid Matter = Guid.Parse("13000000-0000-4000-8000-00000000000a");
    private static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b2");

    private readonly ExternalRootEventReadTests.Fixture _fixture;
    private readonly InvoiceWorld _world = new();
    private readonly InvoiceHidingStub _hiding;
    private readonly Guid _own;
    private readonly Guid _other;

    public ExternalInvoiceLinkedChildListTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = CallerOn(Matter);
        _own = _world.OwnedInvoice();
        _other = _world.OwnedInvoice();
        _world.InvoiceFirm(_other, OtherFirm);
        // Both invoices are filed under the shared matter, so the live-parent rule admits them: only the own-firms rule decides.
        _fixture.Data.AddInvoice(_own, Matter, null);
        _fixture.Data.AddInvoice(_other, Matter, null);
        _hiding = InvoiceHidingStub.Real(_world);
        _fixture.InvoiceHiding = _hiding;
    }

    // =====================================================================
    // To-dos and events on a shared record
    // =====================================================================

    [Fact(DisplayName = "130-0 fix1 to-dos (must fire / must not fire): a to-do of another firm's invoice is absent; one of the caller's own firm's invoice and one with no invoice are listed")]
    public async Task Todos_OnASharedMatter_FollowTheirInvoice()
    {
        _fixture.Data.AddTodoWith("matters", Matter, "Other firm's invoice", ("_sprk_regardinginvoice_value", _other.ToString()));
        _fixture.Data.AddTodoWith("matters", Matter, "Own firm's invoice", ("_sprk_regardinginvoice_value", _own.ToString()));
        _fixture.Data.AddTodo("matters", Matter, "No invoice");

        var body = await List<ExternalTodoDto>("todos");

        body.Value.Select(t => t.SprkName).Should().BeEquivalentTo("Own firm's invoice", "No invoice");
        body.Value.Should().HaveCount(2, "the list's count excludes the hidden row");
        body.Truncated.Should().BeFalse("a row the own-firms rule hides is an answer, not a read failure");
        _hiding.Calls.Should().ContainSingle("one invoice decision per page")
            .Which.Should().BeEquivalentTo(new[] { _other, _own });
    }

    [Fact(DisplayName = "130-0 fix1 to-dos (fault): the caller's firms cannot be read, so every invoice-linked to-do is hidden and the list says truncated")]
    public async Task Todos_WhenTheFirmReadFails_HideEveryInvoiceLinkedRow_AndSayTruncated()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        _fixture.Data.AddTodoWith("matters", Matter, "Own firm's invoice", ("_sprk_regardinginvoice_value", _own.ToString()));
        _fixture.Data.AddTodo("matters", Matter, "No invoice");

        var body = await List<ExternalTodoDto>("todos");

        body.Value.Select(t => t.SprkName).Should().Equal("No invoice");
        body.Truncated.Should().BeTrue("a fault must not read as 'there are no more to-dos'");
    }

    [Fact(DisplayName = "130-0 fix1 events (must fire / must not fire): an event of another firm's invoice is absent; one of the caller's own firm's invoice and one with no invoice are listed")]
    public async Task Events_OnASharedMatter_FollowTheirInvoice()
    {
        var other = _fixture.Data.Add("matters", Matter, "Other firm's invoice");
        _fixture.Data.SetColumn("sprk_events", "sprk_eventid", other, "_sprk_regardinginvoice_value", _other.ToString());
        var own = _fixture.Data.Add("matters", Matter, "Own firm's invoice");
        _fixture.Data.SetColumn("sprk_events", "sprk_eventid", own, "_sprk_regardinginvoice_value", _own.ToString());
        _fixture.Data.Add("matters", Matter, "No invoice");

        var body = await List<ExternalEventDto>("events");

        body.Value.Select(e => e.SprkName).Should().BeEquivalentTo("Own firm's invoice", "No invoice");
        body.Value.Should().HaveCount(2);
        body.Truncated.Should().BeFalse();
        _hiding.Calls.Should().ContainSingle("one invoice decision per page");
    }

    [Fact(DisplayName = "130-0 fix1 events (fault): every invoice-linked event is hidden and the list says truncated")]
    public async Task Events_WhenTheFirmReadFails_HideEveryInvoiceLinkedRow_AndSayTruncated()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var own = _fixture.Data.Add("matters", Matter, "Own firm's invoice");
        _fixture.Data.SetColumn("sprk_events", "sprk_eventid", own, "_sprk_regardinginvoice_value", _own.ToString());
        _fixture.Data.Add("matters", Matter, "No invoice");

        var body = await List<ExternalEventDto>("events");

        body.Value.Select(e => e.SprkName).Should().Equal("No invoice");
        body.Truncated.Should().BeTrue();
    }

    // =====================================================================
    // Documents on a shared record: both invoice links count
    // =====================================================================

    [Theory(DisplayName = "130-0 fix1 documents (must fire / must not fire): a document of another firm's invoice is absent, through either invoice link")]
    [InlineData("_sprk_invoice_value")]
    [InlineData("_sprk_relatedinvoice_value")]
    public async Task Documents_OnASharedMatter_FollowTheirInvoice(string link)
    {
        _fixture.Data.AddDocument("matters", Matter, "Other firm's invoice", (link, _other));
        _fixture.Data.AddDocument("matters", Matter, "Own firm's invoice", (link, _own));
        _fixture.Data.AddDocument("matters", Matter, "No invoice");

        var body = await List<ExternalDocumentDto>("documents");

        body.Value.Select(d => d.SprkName).Should().BeEquivalentTo("Own firm's invoice", "No invoice");
        body.Value.Should().HaveCount(2);
        body.Truncated.Should().BeFalse();
        _hiding.Calls.Should().ContainSingle("one invoice decision per page");
    }

    [Fact(DisplayName = "130-0 fix1 documents (must fire): a document linked to the caller's own invoice AND another firm's is absent (any linked invoice not visible hides it)")]
    public async Task Documents_LinkedToAnOwnAndAnotherFirmsInvoice_AreHidden()
    {
        _fixture.Data.AddDocument("matters", Matter, "Both", ("_sprk_invoice_value", _own), ("_sprk_relatedinvoice_value", _other));
        _fixture.Data.AddDocument("matters", Matter, "No invoice");

        var body = await List<ExternalDocumentDto>("documents");

        body.Value.Select(d => d.SprkName).Should().Equal("No invoice");
    }

    [Fact(DisplayName = "130-0 fix1 documents (fault): every invoice-linked document is hidden and the list says truncated")]
    public async Task Documents_WhenTheFirmReadFails_HideEveryInvoiceLinkedRow_AndSayTruncated()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        _fixture.Data.AddDocument("matters", Matter, "Own firm's invoice", ("_sprk_relatedinvoice_value", _own));
        _fixture.Data.AddDocument("matters", Matter, "No invoice");

        var body = await List<ExternalDocumentDto>("documents");

        body.Value.Select(d => d.SprkName).Should().Equal("No invoice");
        body.Truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "130-0 fix1: a page with no invoice-linked row asks no invoice decision")]
    public async Task Lists_WithNoInvoiceLink_AskNothing()
    {
        _fixture.Data.AddTodo("matters", Matter, "No invoice");
        _fixture.Data.AddDocument("matters", Matter, "No invoice");

        (await List<ExternalTodoDto>("todos")).Value.Should().ContainSingle();
        (await List<ExternalDocumentDto>("documents")).Value.Should().ContainSingle();
        _hiding.Calls.Should().BeEmpty();
        _world.CallerFirmReads.Should().Be(0);
    }

    // =====================================================================
    // The documents module (/fetch)
    // =====================================================================

    private const string DocumentGridFetch =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>"
        + "<attribute name='sprk_matter'/></entity></fetch>";

    [Theory(DisplayName = "130-0 fix1 documents module (must fire / must not fire): another firm's invoice's document is absent, through either invoice link")]
    [InlineData("sprk_invoice")]
    [InlineData("sprk_relatedinvoice")]
    public async Task DocumentsModuleFetch_FollowsTheInvoice(string link)
    {
        var (body, executed) = await RunDocumentsFetchAsync(
            DocRow("Other firm's invoice", (link, _other)), DocRow("Own firm's invoice", (link, _own)), DocRow("No invoice"));

        body.Entities.Select(r => r["sprk_documentname"]).Should().BeEquivalentTo(new[] { "Own firm's invoice", "No invoice" });
        body.Truncated.Should().BeFalse();
        executed.Should().Contain("name=\"sprk_invoice\"").And.Contain("name=\"sprk_relatedinvoice\"");
        body.Entities.Should().OnlyContain(r => !r.ContainsKey("sprk_invoice") && !r.ContainsKey("sprk_relatedinvoice"),
            "the link is read to decide the row, never returned unasked");
        _hiding.Calls.Should().ContainSingle();
    }

    [Fact(DisplayName = "130-0 fix1 documents module (fault): every invoice-linked row is hidden and the page says truncated")]
    public async Task DocumentsModuleFetch_WhenTheFirmReadFails_HidesEveryInvoiceLinkedRow()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");

        var (body, _) = await RunDocumentsFetchAsync(DocRow("Own firm's invoice", ("sprk_invoice", _own)), DocRow("No invoice"));

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("No invoice");
        body.Truncated.Should().BeTrue();
    }

    // =====================================================================
    // Roots: decided by their grant; the invoice link is shown only when that invoice is visible
    // =====================================================================

    [Fact(DisplayName = "130-0 fix1 root (must fire): a work assignment linked to another firm's invoice is returned, with every form of the link blanked")]
    public async Task RootRead_AnotherFirmsInvoiceLink_IsBlanked_TheRootIsReturned()
    {
        var rows = await Redact(WorkAssignmentRow("WA", _other));

        var row = rows.Should().ContainSingle("a root is never hidden by its invoice link").Subject;
        row["sprk_name"].Should().Be("WA");
        foreach (var key in LinkKeys)
            row[key].Should().BeNull($"{key} names an invoice the caller may not see");
    }

    [Fact(DisplayName = "130-0 fix1 root (must not fire): a work assignment linked to the caller's own firm's invoice shows the link")]
    public async Task RootRead_OwnFirmsInvoiceLink_IsShown()
    {
        var rows = await Redact(WorkAssignmentRow("WA", _own));

        var row = rows.Should().ContainSingle().Subject;
        row["_sprk_regardinginvoice_value"].Should().Be(_own.ToString());
        row["sprk_regardinginvoicename"].Should().Be("INV-1");
        _hiding.Calls.Should().ContainSingle();
    }

    [Fact(DisplayName = "130-0 fix1 root (fault): the caller's firms cannot be read, so the link is blanked and the root returned")]
    public async Task RootRead_WhenTheFirmReadFails_TheLinkIsBlanked()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");

        var row = (await Redact(WorkAssignmentRow("WA", _own))).Should().ContainSingle().Subject;

        row["sprk_name"].Should().Be("WA");
        foreach (var key in LinkKeys)
            row[key].Should().BeNull();
    }

    [Fact(DisplayName = "130-0 fix1 root (must not fire): a work assignment with no invoice link is unchanged and asks nothing")]
    public async Task RootRead_WithNoInvoiceLink_IsUnchanged()
    {
        var original = new Dictionary<string, object?> { ["sprk_name"] = "WA", ["sprk_regardinginvoice"] = null };

        var rows = await Redact(original);

        rows.Should().ContainSingle().Which.Should().BeSameAs(original);
        _hiding.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0 fix1 root: a work assignment's record read is returned even when its invoice is another firm's, and never returns the link")]
    public async Task WorkAssignmentRecordRead_IsReturned_WithoutAnotherFirmsInvoiceLink()
    {
        var workAssignment = Guid.NewGuid();
        var caller = CallerOnWorkAssignment(workAssignment);

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            "sprk_workassignment", workAssignment, select: null, caller, Registry(),
            (_, _, _, _) => Task.FromResult<IReadOnlyDictionary<string, object?>>(WorkAssignmentRow("WA", _other)),
            NullLogger.Instance, CancellationToken.None, _hiding.Seam);

        var row = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        row["sprk_name"].Should().Be("WA");
        row.Where(kv => kv.Key.Contains("regardinginvoice", StringComparison.OrdinalIgnoreCase))
            .Should().NotContain(kv => kv.Value != null, "another firm's invoice is never named on a root read");
    }

    // =====================================================================
    // The shared filter (pure)
    // =====================================================================

    [Fact(DisplayName = "130-0 fix1 filter: the invoice link columns are the ones the edge registries name")]
    public void TheLinkColumns_AreTheRegistryLinks()
    {
        InvoiceLinks.LinkColumnsOf("sprk_todo").Should().Contain("sprk_regardinginvoice");
        InvoiceLinks.LinkColumnsOf("sprk_event").Should().Contain("sprk_regardinginvoice");
        InvoiceLinks.LinkColumnsOf("sprk_document").Should().Contain(new[] { "sprk_invoice", "sprk_relatedinvoice" });
        ExternalDataService.BuildEventsQuery(ExternalDataService.TodoRootKind.Matter, Matter).Should().Contain("_sprk_regardinginvoice_value");
        ExternalDataService.WithInvoiceLinkColumns("sprk_documentid", "sprk_document")
            .Split(',').Should().Contain(new[] { "_sprk_invoice_value", "_sprk_relatedinvoice_value" });
        ExternalDataService.WithInvoiceLinkColumns("sprk_documentid,_sprk_invoice_value", "sprk_document")
            .Split(',').Count(c => c == "_sprk_invoice_value").Should().Be(1, "a column already read is not added twice");
    }

    [Fact(DisplayName = "130-0 fix1 filter (fault): no seam hides every invoice-linked row and says truncated")]
    public async Task TheFilter_WithNoSeam_HidesEveryInvoiceLinkedRow()
    {
        var (visible, truncated) = await ExternalDataService.HideInvoiceLinkedRowsAsync(
            CallerOn(Matter), new[] { ("linked", new[] { _own }), ("plain", Array.Empty<Guid>()) },
            r => r.Item2, hideInvoices: null, NullLogger.Instance, "test", CancellationToken.None);

        visible.Select(r => r.Item1).Should().Equal("plain");
        truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "130-0 fix1 filter (fault): a seam that throws hides every invoice-linked row and says truncated")]
    public async Task TheFilter_WhenTheSeamThrows_HidesEveryInvoiceLinkedRow()
    {
        ExternalInvoiceHiding throwing = (_, _, _) => throw new TimeoutException("The invoice decision failed (test).");

        var (visible, truncated) = await ExternalDataService.HideInvoiceLinkedRowsAsync(
            CallerOn(Matter), new[] { ("linked", new[] { _own }), ("plain", Array.Empty<Guid>()) },
            r => r.Item2, throwing, NullLogger.Instance, "test", CancellationToken.None);

        visible.Select(r => r.Item1).Should().Equal("plain");
        truncated.Should().BeTrue();
    }

    [Fact(DisplayName = "130-0 fix1 filter: an unreadable invoice link hides its row, and is never put to the seam")]
    public async Task TheFilter_AnUnreadableLink_HidesTheRow()
    {
        var garbled = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["_sprk_regardinginvoice_value"] = System.Text.Json.JsonDocument.Parse("\"not-a-guid\"").RootElement,
        };
        var links = ExternalDataService.InvoiceLinksOf("sprk_todo", garbled);

        var (visible, truncated) = await ExternalDataService.HideInvoiceLinkedRowsAsync(
            CallerOn(Matter), new[] { ("garbled", links), ("own", (IReadOnlyList<Guid>)new[] { _own }) },
            r => r.Item2, _hiding.Seam, NullLogger.Instance, "test", CancellationToken.None);

        links.Should().Equal(ExternalDocumentNarrowing.UnreadableLink);
        visible.Select(r => r.Item1).Should().Equal("own");
        truncated.Should().BeFalse("an unreadable link is an answer (hide), not a read failure");
        _hiding.Calls.Should().ContainSingle().Which.Should().Equal(_own);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static readonly string[] LinkKeys =
    {
        "sprk_regardinginvoice", "_sprk_regardinginvoice_value", "sprk_regardinginvoicename",
        "sprk_regardinginvoice@OData.Community.Display.V1.FormattedValue",
        "_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.lookuplogicalname",
    };

    private static Dictionary<string, object?> WorkAssignmentRow(string name, Guid invoice) => new()
    {
        ["sprk_name"] = name,
        ["sprk_regardinginvoice"] = new Dictionary<string, object?> { ["id"] = invoice, ["logicalName"] = "sprk_invoice", ["name"] = "INV-1" },
        ["_sprk_regardinginvoice_value"] = invoice.ToString(),
        ["sprk_regardinginvoicename"] = "INV-1",
        ["sprk_regardinginvoice@OData.Community.Display.V1.FormattedValue"] = "INV-1",
        ["_sprk_regardinginvoice_value@Microsoft.Dynamics.CRM.lookuplogicalname"] = "sprk_invoice",
    };

    private Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> Redact(IReadOnlyDictionary<string, object?> row) =>
        ExternalModuleDataEndpoints.RedactRootInvoiceLinksAsync(
            "sprk_workassignment", new[] { row }, CallerOn(Matter), _hiding.Seam, NullLogger.Instance, CancellationToken.None);

    private async Task<ExternalCollectionResponse<T>> List<T>(string kind)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Matter}/{kind}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExternalCollectionResponse<T>>())!;
    }

    private static IReadOnlyDictionary<string, object?> DocRow(string name, (string Column, Guid Value)? link = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_document",
            ["sprk_documentid"] = Guid.NewGuid(),
            ["sprk_documentname"] = name,
            ["sprk_matter"] = Matter,
        };
        if (link is { } l) row[l.Column] = l.Value;
        return row;
    }

    /// <summary>The real documents-module fetch, with the REAL secure-flag, narrowing and live-parent reads over FakeDataverse and
    /// the REAL caller-aware invoice seam. The double returns each row with only the attributes the executed FetchXML names.</summary>
    private async Task<(FetchResponseDto Body, string Executed)> RunDocumentsFetchAsync(params IReadOnlyDictionary<string, object?>[] rows)
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
            data.GetLiveParentRootsAsync, _hiding.Seam);
        return (result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!, executed);
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

    private static CallerPrincipal CallerOnWorkAssignment(Guid workAssignment) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [workAssignment] = AccessRights.Read },
    };
}
