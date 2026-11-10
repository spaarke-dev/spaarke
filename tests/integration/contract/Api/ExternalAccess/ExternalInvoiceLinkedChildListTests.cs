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

    [Fact(DisplayName = "130-0 fix2 (must fire): a caller known to be in no firm sees no invoice-linked row, and it is an answer, not a fault (not truncated)")]
    public async Task Todos_ForACallerInNoFirm_HideEveryInvoiceLinkedRow_WithoutAFault()
    {
        _world.CallerFirmsAnswer = CallerFirms.From(Array.Empty<Guid>(), unreadable: false);
        _fixture.Data.AddTodoWith("matters", Matter, "Own firm's invoice", ("_sprk_regardinginvoice_value", _own.ToString()));
        _fixture.Data.AddTodo("matters", Matter, "No invoice");

        var body = await List<ExternalTodoDto>("todos");

        body.Value.Select(t => t.SprkName).Should().Equal("No invoice");
        body.Truncated.Should().BeFalse("no firm is a decided answer (hide all), not a read failure");
    }

    [Fact(DisplayName = "130-0 fix2 (must fire): a decision that could not be read (the ask faulted for the invoice) hides that invoice's row and says truncated; a row with no invoice stays")]
    public async Task TheFilter_AFaultedDecision_HidesThatRow_AndSaysTruncated()
    {
        ExternalInvoiceHiding faultOnOne = (_, _, _) => Task.FromResult(InvoiceVisibilityResult.ReadFailed(new[] { _other }));

        var (visible, truncated) = await ExternalDataService.HideInvoiceLinkedRowsAsync(
            CallerOn(Matter), new[] { ("faulted", (IReadOnlyList<Guid>)new[] { _other }), ("plain", Array.Empty<Guid>()) },
            r => r.Item2, faultOnOne, NullLogger.Instance, "test", CancellationToken.None);

        visible.Select(r => r.Item1).Should().Equal("plain");
        truncated.Should().BeTrue();
    }

    [Theory(DisplayName = "130-0 fix3b (must fire / must not fire): a decision that failed for one invoice alone says truncated; a decided hidden invoice does not")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheFilter_APerInvoiceFault_HidesThatRow_AndSaysTruncated(bool fault)
    {
        var stub = fault ? InvoiceHidingStub.PerInvoiceFault(_other) : InvoiceHidingStub.Hiding(_other);

        var (visible, truncated) = await ExternalDataService.HideInvoiceLinkedRowsAsync(
            CallerOn(Matter),
            new[] { ("affected", (IReadOnlyList<Guid>)new[] { _other }), ("own", (IReadOnlyList<Guid>)new[] { _own }), ("plain", Array.Empty<Guid>()) },
            r => r.Item2, stub.Seam, NullLogger.Instance, "test", CancellationToken.None);

        visible.Select(r => r.Item1).Should().Equal("own", "plain");
        truncated.Should().Be(fault, "a fault for one invoice must not read as 'there are no more rows'");
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

    // ebilling-r1 task 130-0 fix round 3: the FetchXML overload of ExternalDataService.InvoiceLinksOf reads an absent attribute as "no
    // link" (FetchXML omits a null attribute). That is sound only while every FetchXML caller asks for every link column of the table;
    // this pins it for every registered child module with invoice links.
    [Fact(DisplayName = "130-0 fix3 FetchXML pin: every child module with invoice links projects every registry link column in the FetchXML it executes")]
    public async Task EveryFetchXmlChildModule_ProjectsEveryRegistryLinkColumn()
    {
        var registry = Registry();
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(Guid.NewGuid(), ExternalAccessLevel.FullAccess) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [Matter] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [Guid.NewGuid()] = AccessRights.Read },
        };
        var children = registry.Modules
            .Where(m => InvoiceLinks.HasInvoiceLink(m.RecordEntity)
                && !InvoiceLinks.IsRootWithInvoiceLink(m.RecordEntity)
                && !ExternalInvoiceVisibility.IsInvoiceFamilyTable(m.RecordEntity))
            .ToList();
        children.Should().NotBeEmpty("the pin is not vacuous");
        children.Select(m => m.RecordEntity).Should().Contain("sprk_document");
        // F3b: a service request follows the child rule too; its module scopes to the requester (a workforce contact always qualifies).
        children.Select(m => m.RecordEntity).Should().Contain("sprk_servicerequest");

        foreach (var module in children)
        {
            string? executed = null;
            var data = _fixture.Data.NewService();
            var idColumn = InvoiceLinks.Tables[module.RecordEntity].IdColumn;
            var moduleCaller = module.RecordEntity == "sprk_servicerequest"
                ? new CallerPrincipal
                {
                    Plane = CallerPrincipalPlane.Workforce, ContactId = CallerContact, Email = caller.Email, ProjectAccess = caller.ProjectAccess,
                }
                : caller;
            await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
                new FetchRequestDto(
                    module.RecordEntity, $"<fetch><entity name='{module.RecordEntity}'><attribute name='{idColumn}'/></entity></fetch>", PagingCookie: null),
                moduleCaller, registry, new FetchXmlEntityExtractor(),
                (request, _) =>
                {
                    executed = request.FetchXml;
                    return Task.FromResult(new FetchResponseDto(Array.Empty<IReadOnlyDictionary<string, object?>>(), MoreRecords: false, PagingCookie: null));
                },
                data.GetNonSecureRootsAsync, data.NarrowDocumentsAsync, NullLogger.Instance, CancellationToken.None,
                data.GetLiveParentRootsAsync, _hiding.Seam);

            executed.Should().NotBeNull($"the {module.Name} module executes the fetch for a caller with a grant on every root");
            var named = System.Xml.Linq.XDocument.Parse(executed!).Descendants("attribute")
                .Select(a => (string?)a.Attribute("name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            named.Should().Contain(InvoiceLinks.LinkColumnsOf(module.RecordEntity),
                $"the {module.Name} module's FetchXML must name every invoice link column, or a hidden link would read as no link");
        }
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

    [Fact(DisplayName = "130-0 fix2 root (must fire): every form of the root redaction set, annotations included, is blanked when the regarding pair names the hidden invoice")]
    public async Task RootRead_EveryFormOfTheRedactionSet_IsBlanked_WhenThePairNamesTheHiddenInvoice()
    {
        var row = RowWithEveryRedactionForm(link: _other, pair: _other);

        var result = (await Redact(row)).Should().ContainSingle().Subject;

        result["sprk_name"].Should().Be("WA");
        foreach (var key in row.Keys.Where(k => k != "sprk_name"))
            result[key].Should().BeNull($"{key} is a form of the root redaction set and the pair names the hidden invoice");
    }

    [Fact(DisplayName = "130-0 fix2 root (must not fire): when the regarding pair names another record, only the link forms are blanked")]
    public async Task RootRead_ThePairNamingAnotherRecord_KeepsEveryRegardingForm()
    {
        var matter = Guid.NewGuid();
        var row = RowWithEveryRedactionForm(link: _other, pair: matter);

        var result = (await Redact(row)).Should().ContainSingle().Subject;

        foreach (var key in row.Keys.Where(k => k != "sprk_name"))
        {
            if (RootInvoiceLinkRedaction.LinkFormsOf("sprk_workassignment").Contains(RootInvoiceLinkRedaction.ColumnOf(key)))
                result[key].Should().BeNull($"{key} is a form of the hidden invoice link");
            else
                result[key].Should().Be(matter.ToString(), $"{key} names a matter, not the hidden invoice");
        }
    }

    /// <summary>A work assignment row carrying every form of <see cref="RootInvoiceLinkRedaction.ColumnsOf"/>, each with an annotation:
    /// the link forms name <paramref name="link"/>, the regarding forms <paramref name="pair"/>.</summary>
    private static Dictionary<string, object?> RowWithEveryRedactionForm(Guid link, Guid pair)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["sprk_name"] = "WA" };
        var linkForms = RootInvoiceLinkRedaction.LinkFormsOf("sprk_workassignment");
        linkForms.Should().NotBeEmpty("the work assignment is a root with an invoice link");
        foreach (var form in RootInvoiceLinkRedaction.ColumnsOf("sprk_workassignment"))
        {
            var value = (linkForms.Contains(form) ? link : pair).ToString();
            row[form] = value;
            row[form + "@OData.Community.Display.V1.FormattedValue"] = value;
        }
        return row;
    }

    [Fact(DisplayName = "130-0 fix1 root (must not fire): a work assignment with no invoice link is unchanged and asks nothing")]
    public async Task RootRead_WithNoInvoiceLink_IsUnchanged()
    {
        var original = new Dictionary<string, object?> { ["sprk_name"] = "WA", ["sprk_regardinginvoice"] = null };

        var rows = await Redact(original);

        rows.Should().ContainSingle().Which.Should().BeSameAs(original);
        _hiding.Calls.Should().BeEmpty();
    }

    // Task 130-0 fix round 2 (F3): the production work-assignments module does not admit sprk_regardinginvoice (nor the regarding
    // resolver columns), so on /data/record the redaction is defence in depth. These tests drive /data/record with a TEST registry whose
    // work-assignments module admits them, so the redaction itself runs on the route's path.
    [Fact(DisplayName = "130-0 fix2 F3 root record read (must fire): with a module that admits the link, another firm's invoice link and the regarding resolver columns naming it are blanked; the root is returned")]
    public async Task WorkAssignmentRecordRead_ThroughAModuleThatAdmitsTheLink_BlanksAnotherFirmsInvoice()
    {
        var workAssignment = Guid.NewGuid();

        var row = await ReadWorkAssignmentAsync(workAssignment, WithRegardingPair(WorkAssignmentRow("WA", _other), _other));

        row["sprk_name"].Should().Be("WA");
        row["sprk_regardinginvoice"].Should().BeNull("another firm's invoice is never named on a root read");
        foreach (var column in RootInvoiceLinkRedaction.RegardingColumns)
            row[column].Should().BeNull($"{column} names the hidden invoice (its number among them)");
    }

    [Fact(DisplayName = "130-0 fix2 F3 root record read (must not fire): the caller's own firm's invoice link and resolver columns are shown")]
    public async Task WorkAssignmentRecordRead_ThroughAModuleThatAdmitsTheLink_ShowsTheCallersOwnInvoice()
    {
        var workAssignment = Guid.NewGuid();

        var row = await ReadWorkAssignmentAsync(workAssignment, WithRegardingPair(WorkAssignmentRow("WA", _own), _own));

        row["sprk_regardinginvoice"].Should().NotBeNull();
        row["sprk_regardingrecordnumber"].Should().Be("INV-0001");
        row["sprk_regardingrecordid"].Should().Be(_own.ToString());
    }

    [Fact(DisplayName = "130-0 fix2 F3 root record read (must not fire): a regarding pair that names another record is left alone when the link is blanked")]
    public async Task WorkAssignmentRecordRead_ARegardingPairNamingAnotherRecord_IsNotBlanked()
    {
        var workAssignment = Guid.NewGuid();
        var matter = Guid.NewGuid();

        var row = await ReadWorkAssignmentAsync(workAssignment, WithRegardingPair(WorkAssignmentRow("WA", _other), matter));

        row["sprk_regardinginvoice"].Should().BeNull();
        row["sprk_regardingrecordid"].Should().Be(matter.ToString(), "the pair names a matter, not the hidden invoice");
    }

    private async Task<IReadOnlyDictionary<string, object?>> ReadWorkAssignmentAsync(
        Guid workAssignment, IReadOnlyDictionary<string, object?> stored)
    {
        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            "sprk_workassignment", workAssignment,
            select: "sprk_name,sprk_regardinginvoice,sprk_regardingrecordid,sprk_regardingrecordtype,sprk_regardingrecordname,sprk_regardingrecordurl,sprk_regardingrecordnumber",
            CallerOnWorkAssignment(workAssignment), RegistryAdmittingTheWorkAssignmentLink(),
            (_, _, _, _) => Task.FromResult(stored), NullLogger.Instance, CancellationToken.None, _hiding.Seam);
        return result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
    }

    private static IReadOnlyDictionary<string, object?> WithRegardingPair(Dictionary<string, object?> row, Guid target)
    {
        row["sprk_regardingrecordid"] = target.ToString();
        row["sprk_regardingrecordtype"] = new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["logicalName"] = "sprk_recordtype_ref", ["name"] = "Invoice" };
        row["sprk_regardingrecordname"] = "INV-0001 name";
        row["sprk_regardingrecordurl"] = "/main.aspx?etn=sprk_invoice";
        row["sprk_regardingrecordnumber"] = "INV-0001";
        return row;
    }

    /// <summary>A TEST registry: the work-assignments module admitting its invoice link and the regarding resolver columns (the production
    /// module admits neither), so a /data/record read returns them and the redaction runs.</summary>
    private static ExternalModuleRegistry RegistryAdmittingTheWorkAssignmentLink()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(new ExternalModuleDescriptor
        {
            Name = "work-assignments",
            RecordEntity = "sprk_workassignment",
            RecordIdAttribute = "sprk_workassignmentid",
            AccessibleRecordIds = p => p.GetAccessibleWorkAssignmentIds(),
            PrimaryNameAttribute = "sprk_name",
            ReadableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sprk_workassignmentid", "sprk_name", "sprk_regardinginvoice", "sprk_regardingrecordid", "sprk_regardingrecordtype",
                "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
            },
        });
        return registry;
    }

    // =====================================================================
    // Task 130-0 fix round 2 (K3, D-28): the invoices module scopes through matter, project and work assignment
    // =====================================================================

    private const string InvoiceGridFetch =
        "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/></entity></fetch>";

    [Fact(DisplayName = "130-0 fix2 K3 invoices module: an invoice under the caller's work assignment is listed; one under another (or a deactivated) work assignment, or under a secure one the caller holds no named grant on, is not; the column is projected server-side and not returned unasked")]
    public async Task InvoicesModuleFetch_ScopesThroughTheWorkAssignment()
    {
        var assignment = Guid.NewGuid();
        var otherAssignment = Guid.NewGuid();
        var secureAssignment = Guid.NewGuid();
        _fixture.Data.AddSecureRoot(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType.WorkAssignment, secureAssignment);
        var underAssignment = _world.OwnedInvoice();
        var underOther = _world.OwnedInvoice();
        var underSecure = _world.OwnedInvoice();
        var caller = CallerOn(Matter);
        caller = new CallerPrincipal
        {
            Plane = caller.Plane, ContactId = caller.ContactId, Email = caller.Email, ProjectAccess = caller.ProjectAccess,
            MatterAccess = caller.MatterAccess,
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [assignment] = AccessRights.Read },
        };

        var (body, executed) = await RunFetchAsync(
            "sprk_invoice", InvoiceGridFetch, caller,
            InvoiceRow("Under the caller's work assignment", underAssignment, ("sprk_regardingworkassignment", assignment)),
            InvoiceRow("Under another work assignment", underOther, ("sprk_regardingworkassignment", otherAssignment)),
            InvoiceRow("Under the matter and a secure work assignment", underSecure,
                ("sprk_matter", Matter), ("sprk_regardingworkassignment", secureAssignment)));

        body.Entities.Select(r => r["sprk_name"]).Should().Equal("Under the caller's work assignment");
        executed.Should().Contain("name=\"sprk_regardingworkassignment\"", "the scope and the secure-roots rule read it server-side");
        body.Entities.Should().OnlyContain(r => !r.ContainsKey("sprk_regardingworkassignment"), "it is returned only when the grid asks");
    }

    [Fact(DisplayName = "130-0 fix2 K3 invoices module (readable column): asked for, the work assignment is returned only on rows that passed the secure-roots rule, in the same form as sprk_matter, and filters and sorts as sprk_matter does")]
    public async Task InvoicesModuleFetch_TheWorkAssignmentColumn_WhenAsked_FollowsTheSecureRootsRule_LikeTheMatter()
    {
        var assignment = Guid.NewGuid();
        var secureAssignment = Guid.NewGuid();
        _fixture.Data.AddSecureRoot(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType.WorkAssignment, secureAssignment);
        var underAssignment = _world.OwnedInvoice();
        var underSecure = _world.OwnedInvoice();
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [Matter] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [assignment] = AccessRights.Read },
        };
        const string asked =
            "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/>"
            + "<attribute name='sprk_matter'/><attribute name='sprk_regardingworkassignment'/>"
            + "<filter><condition attribute='sprk_regardingworkassignment' operator='not-null'/><condition attribute='sprk_matter' operator='not-null'/></filter>"
            + "<order attribute='sprk_regardingworkassignment'/><order attribute='sprk_matter'/></entity></fetch>";

        var (body, _) = await RunFetchAsync("sprk_invoice", asked, caller,
            InvoiceRow("Under the caller's work assignment", underAssignment, ("sprk_matter", Matter), ("sprk_regardingworkassignment", assignment)),
            InvoiceRow("Under a secure work assignment the caller holds no named grant on", underSecure,
                ("sprk_matter", Matter), ("sprk_regardingworkassignment", secureAssignment)));

        var row = body.Entities.Should().ContainSingle("a row filed to a secure work assignment the caller cannot read never returns").Subject;
        row["sprk_name"].Should().Be("Under the caller's work assignment");
        row.Keys.Where(k => k.Contains("regardingworkassignment", StringComparison.OrdinalIgnoreCase))
            .Should().Equal(new[] { "sprk_regardingworkassignment" }, "the same single lookup form as sprk_matter, no name or URL form");
        row.Keys.Where(k => k.Contains("sprk_matter", StringComparison.OrdinalIgnoreCase)).Should().Equal("sprk_matter");
        body.Entities.SelectMany(r => r.Values).Should().NotContain(v => Equals(v, secureAssignment));
    }

    [Fact(DisplayName = "130-0 fix2 K3 invoices module (must not fire): matter-only and project-only scoping are unchanged")]
    public async Task InvoicesModuleFetch_MatterScopingIsUnchanged()
    {
        var invoice = _world.OwnedInvoice();

        var (body, _) = await RunFetchAsync("sprk_invoice", InvoiceGridFetch, CallerOn(Matter),
            InvoiceRow("Under the matter", invoice, ("sprk_matter", Matter)));

        body.Entities.Select(r => r["sprk_name"]).Should().Equal("Under the matter");
    }

    private static IReadOnlyDictionary<string, object?> InvoiceRow(string name, Guid id, params (string Column, Guid Value)[] links)
    {
        var row = new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_invoice",
            ["sprk_invoiceid"] = id,
            ["sprk_name"] = name,
        };
        foreach (var (column, value) in links) row[column] = value;
        return row;
    }

    private async Task<(FetchResponseDto Body, string Executed)> RunFetchAsync(
        string entity, string fetch, CallerPrincipal caller, params IReadOnlyDictionary<string, object?>[] rows)
    {
        var executed = string.Empty;
        var data = _fixture.Data.NewService();
        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(entity, fetch, PagingCookie: null),
            caller, Registry(), new FetchXmlEntityExtractor(),
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

    [Fact(DisplayName = "130-0 fix3 absent link (must fire / must not fire): an absent link column reads as unreadable (hidden); a selected explicit null is no link")]
    public async Task TheFilter_AnAbsentLinkColumn_HidesTheRow_AndASelectedNullIsVisible()
    {
        var absent = new Dictionary<string, System.Text.Json.JsonElement>();
        var selectedNull = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["_sprk_regardinginvoice_value"] = System.Text.Json.JsonDocument.Parse("null").RootElement,
        };
        var absentLinks = ExternalDataService.InvoiceLinksOf("sprk_todo", absent);
        var nullLinks = ExternalDataService.InvoiceLinksOf("sprk_todo", selectedNull);

        var (visible, _) = await ExternalDataService.HideInvoiceLinkedRowsAsync(
            CallerOn(Matter), new[] { ("absent", absentLinks), ("null", nullLinks) },
            r => r.Item2, _hiding.Seam, NullLogger.Instance, "test", CancellationToken.None);

        absentLinks.Should().Equal(ExternalDocumentNarrowing.UnreadableLink);
        nullLinks.Should().BeEmpty();
        ExternalDataService.InvoiceLinksOf("sprk_todo", (IReadOnlyDictionary<string, System.Text.Json.JsonElement>?)null)
            .Should().Equal(new[] { ExternalDocumentNarrowing.UnreadableLink }, "a row that was never read has no link columns either");
        visible.Select(r => r.Item1).Should().Equal("null");
        _hiding.Calls.Should().BeEmpty("neither row names an invoice to decide");
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
