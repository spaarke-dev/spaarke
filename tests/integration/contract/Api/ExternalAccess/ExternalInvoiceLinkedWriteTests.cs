using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDataService;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 2 (F1): a caller cannot change what it cannot see. A to-do PATCH on a to-do that links to an invoice
/// the caller may not see is refused with the route's existing answer for a record the caller cannot see, and nothing is written; a
/// decision that could not be made is the route's existing 503, only after the record and rights checks.
/// </summary>
/// <remarks>The REAL route and <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/> (its
/// request log is the read-back: a write is a PATCH request), with the REAL caller-aware invoice seam over <see cref="InvoiceWorld"/>.</remarks>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceLinkedTodoUpdateTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid Matter = Guid.Parse("13000000-0000-4000-8000-00000000002a");
    private static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b2");

    private readonly ExternalRootEventReadTests.Fixture _fixture;
    private readonly InvoiceWorld _world = new();
    private readonly InvoiceHidingStub _hiding;
    private readonly Guid _own;
    private readonly Guid _other;

    public ExternalInvoiceLinkedTodoUpdateTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = CallerOn(Matter, AccessRights.Read | AccessRights.Write);
        _own = _world.OwnedInvoice();
        _other = _world.OwnedInvoice();
        _world.InvoiceFirm(_other, OtherFirm);
        _fixture.Data.AddInvoice(_own, Matter, null);
        _fixture.Data.AddInvoice(_other, Matter, null);
        _hiding = InvoiceHidingStub.Real(_world);
        _fixture.InvoiceHiding = _hiding;
    }

    [Fact(DisplayName = "130-0 fix2 F1 to-do PATCH (must fire): a to-do of another firm's invoice is refused with the existing 403 and nothing is written")]
    public async Task ATodoOfAnotherFirmsInvoice_IsRefused_AndNotWritten()
    {
        var reference = await RefusalForATodoOutsideTheCallersSet();
        var id = _fixture.Data.AddTodoWith("matters", Matter, "Other firm's invoice", ("_sprk_regardinginvoice_value", _other.ToString()));

        using var response = await Patch(id);

        (await Summarise(response)).Should().Be(reference, "the same answer as a to-do the caller cannot see");
        Writes().Should().BeEmpty("nothing was written");
        _hiding.Calls.Should().ContainSingle().Which.Should().Equal(_other);
    }

    [Fact(DisplayName = "130-0 fix2 F1 to-do PATCH (must not fire): a to-do of the caller's own firm's invoice, and one with no invoice, are written")]
    public async Task ATodoOfTheCallersOwnInvoice_AndOneWithNoInvoice_AreWritten()
    {
        var own = _fixture.Data.AddTodoWith("matters", Matter, "Own firm's invoice", ("_sprk_regardinginvoice_value", _own.ToString()));
        var plain = _fixture.Data.AddTodo("matters", Matter, "No invoice");

        using var ownResponse = await Patch(own);
        using var plainResponse = await Patch(plain);

        ownResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await ownResponse.Content.ReadAsStringAsync());
        plainResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await plainResponse.Content.ReadAsStringAsync());
        Writes().Should().HaveCount(2);
        _hiding.Calls.Should().ContainSingle("a to-do with no invoice link asks nothing").Which.Should().Equal(_own);
    }

    [Fact(DisplayName = "130-0 fix2 F1 to-do PATCH (fault): the invoice decision faults after the rights check: the existing 503, nothing written")]
    public async Task WhenTheInvoiceDecisionFaults_ItIsTheExisting503_AndNothingIsWritten()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var id = _fixture.Data.AddTodoWith("matters", Matter, "Own firm's invoice", ("_sprk_regardinginvoice_value", _own.ToString()));

        using var response = await Patch(id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ExternalProjectDataEndpoints.TodoWriteUnavailableReasonCode);
        Writes().Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0 fix2 F1 to-do PATCH (must fire): a caller known to be in no firm is refused with the 403, not the 503 (a decided answer)")]
    public async Task ACallerInNoFirm_IsRefused_NotUnavailable()
    {
        _world.CallerFirmsAnswer = CallerFirms.From(Array.Empty<Guid>(), unreadable: false);
        var id = _fixture.Data.AddTodoWith("matters", Matter, "Own firm's invoice", ("_sprk_regardinginvoice_value", _own.ToString()));

        using var response = await Patch(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Writes().Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0 fix2 F1 to-do PATCH (order): a caller without Write is refused before the invoice decision (no 503 oracle)")]
    public async Task ACallerWithoutWrite_IsRefused_BeforeTheInvoiceDecision()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        _fixture.Principal = CallerOn(Matter, AccessRights.Read);
        var id = _fixture.Data.AddTodoWith("matters", Matter, "Other firm's invoice", ("_sprk_regardinginvoice_value", _other.ToString()));

        using var response = await Patch(id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _hiding.Calls.Should().BeEmpty();
        Writes().Should().BeEmpty();
    }

    // ebilling-r1 task 130-0 fix round 3: uniform refusal for single to-do routes. A to-do id that does not exist answers exactly as one
    // outside the caller's set: same status, same body, nothing written.
    [Fact(DisplayName = "130-0 fix3 to-do PATCH (must fire): a missing to-do id is the same 403, with the same body, as one outside the caller's set")]
    public async Task AMissingTodoId_IsTheSameRefusal_AsATodoOutsideTheCallersSet()
    {
        var reference = await RefusalForATodoOutsideTheCallersSet();

        using var response = await Patch(Guid.NewGuid().ToString());

        (await Summarise(response)).Should().Be(reference);
        Writes().Should().BeEmpty("nothing was written");
        _hiding.Calls.Should().BeEmpty("a missing to-do asks no invoice decision");
    }

    private async Task<string> RefusalForATodoOutsideTheCallersSet()
    {
        var elsewhere = _fixture.Data.AddTodo("matters", Guid.NewGuid(), "Another record's");
        using var response = await Patch(elsewhere);
        var summary = await Summarise(response);
        summary.Should().StartWith("403");
        _fixture.Data.Requests.Clear();
        return summary;
    }

    private IReadOnlyList<string> Writes() =>
        _fixture.Data.Requests.Where(r => r.StartsWith("PATCH ", StringComparison.Ordinal)).ToList();

    private async Task<HttpResponseMessage> Patch(string id)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.PatchAsJsonAsync($"/api/v1/external/todos/{id}", new { sprk_name = "Renamed" });
    }

    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return $"{(int)response.StatusCode}";
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|", new[] { "403" }.Concat(doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }

    private static CallerPrincipal CallerOn(Guid matter, AccessRights rights) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [matter] = rights },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}

/// <summary>
/// ebilling-r1 task 130-0 fix round 2 (F1): the delete-own routes (documents, to-dos, events) refuse a row that links to an invoice the
/// caller may not see, through ANY invoice link column of its table (a document's <c>sprk_invoice</c> and <c>sprk_relatedinvoice</c>),
/// with the existing delete-own 403; a faulted decision is the existing delete-own 503, after the creator and parent checks. The row is
/// read back from the double.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceLinkedDeleteTests : IClassFixture<ExternalDeleteOwnTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999032");
    private static readonly Guid Matter = Guid.Parse("13000000-0000-4000-8000-00000000003a");
    private static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b2");

    private readonly ExternalDeleteOwnTests.Fixture _fixture;
    private readonly InvoiceWorld _world = new();
    private readonly InvoiceHidingStub _hiding;
    private readonly Guid _own;
    private readonly Guid _other;

    public ExternalInvoiceLinkedDeleteTests(ExternalDeleteOwnTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [Matter] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };
        _own = _world.OwnedInvoice();
        _other = _world.OwnedInvoice();
        _world.InvoiceFirm(_other, OtherFirm);
        _hiding = InvoiceHidingStub.Real(_world);
        _fixture.InvoiceHiding = _hiding;
    }

    public static TheoryData<string> Kinds => new() { "documents", "todos", "events" };

    [Theory(DisplayName = "130-0 fix2 F1 delete (must fire): a row of another firm's invoice is refused with the delete-own 403, and the row is still there")]
    [MemberData(nameof(Kinds))]
    public async Task ARowOfAnotherFirmsInvoice_IsRefused_AndKept(string segment)
    {
        var id = SeedLinkedTo(segment, _other);

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ExternalProjectDataEndpoints.DeleteOwnDeniedReasonCode);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue("nothing was deleted");
    }

    [Theory(DisplayName = "130-0 fix2 F1 delete (must not fire): a row of the caller's own firm's invoice, and one with no invoice, are deleted")]
    [MemberData(nameof(Kinds))]
    public async Task ARowOfTheCallersOwnInvoice_AndOneWithNone_AreDeleted(string segment)
    {
        var own = SeedLinkedTo(segment, _own);
        var plain = SeedLinkedTo(segment);

        using var ownResponse = await Delete(segment, own);
        using var plainResponse = await Delete(segment, plain);

        ownResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await ownResponse.Content.ReadAsStringAsync());
        plainResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, await plainResponse.Content.ReadAsStringAsync());
        _fixture.Exists(KindOf(segment), own).Should().BeFalse();
        _fixture.Exists(KindOf(segment), plain).Should().BeFalse();
    }

    [Theory(DisplayName = "130-0 fix2 F1 delete (fault): the invoice decision faults after the creator and parent checks: the delete-own 503, row kept")]
    [MemberData(nameof(Kinds))]
    public async Task WhenTheInvoiceDecisionFaults_ItIsTheDeleteOwn503_AndTheRowIsKept(string segment)
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var id = SeedLinkedTo(segment, _own);

        using var response = await Delete(segment, id);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ExternalProjectDataEndpoints.DeleteOwnUnavailableReasonCode);
        _fixture.Exists(KindOf(segment), id).Should().BeTrue();
    }

    [Fact(DisplayName = "130-0 fix2 F1 delete (order): someone else's row is the uniform 403 even when the invoice decision would fault")]
    public async Task SomeoneElsesRow_IsTheUniform403_BeforeTheInvoiceDecision()
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var id = _fixture.Seed(DeletableKind.Todo, contact: Guid.NewGuid(), parents: new DeleteTargetParent(DocumentParentKind.Matter, Matter));
        _fixture.Data.Targets[(DeletableKind.Todo, id)] = _fixture.Data.Targets[(DeletableKind.Todo, id)] with { InvoiceLinks = new[] { _own } };

        using var response = await Delete("todos", id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _hiding.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0 fix2 F1 delete (fail closed by type): a row whose invoice links were not read is refused")]
    public async Task ARowWhoseLinksWereNotRead_IsRefused()
    {
        var id = SeedLinkedTo("todos");
        _fixture.Data.Targets[(DeletableKind.Todo, id)] = _fixture.Data.Targets[(DeletableKind.Todo, id)] with { InvoiceLinks = null };

        using var response = await Delete("todos", id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Exists(DeletableKind.Todo, id).Should().BeTrue();
    }

    [Fact(DisplayName = "130-0 fix2 F1 document delete: the read takes every invoice link column, so a hidden sprk_relatedinvoice is a link the delete follows")]
    public void TheDeleteRead_TakesEveryInvoiceLinkColumn_SoAHiddenRelatedInvoiceRefuses()
    {
        BuildDeleteTargetUrl("https://org.test/api/data/v9.2", DeletableKind.Document, Guid.NewGuid())
            .Should().Contain("_sprk_invoice_value").And.Contain("_sprk_relatedinvoice_value");
        BuildDeleteTargetUrl("https://org.test/api/data/v9.2", DeletableKind.Todo, Guid.NewGuid())
            .Should().Contain("_sprk_regardinginvoice_value");
        BuildDeleteTargetUrl("https://org.test/api/data/v9.2", DeletableKind.Event, Guid.NewGuid())
            .Should().Contain("_sprk_regardinginvoice_value");

        var related = Guid.NewGuid();
        var row = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["_sprk_createdbycontact_value"] = CallerContact.ToString(),
            ["_sprk_matter_value"] = Matter.ToString(),
            ["_sprk_invoice_value"] = null, // a selected lookup that is not set: the Web API answers an explicit null
            ["_sprk_relatedinvoice_value"] = related.ToString(),
        })).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        ParseDeleteTarget(DeletableKind.Document, row).InvoiceLinks.Should().Equal(related);
    }

    // ebilling-r1 task 130-0 fix round 3: a link column the read did not carry is not "no link". The target's links then include the
    // never-visible sentinel, so the delete is refused.
    [Fact(DisplayName = "130-0 fix3 delete target (must fire): a row read without an invoice link column is refused, and kept; a selected explicit null is no link")]
    public async Task ARowReadWithoutALinkColumn_IsRefused_AndASelectedNullIsNotALink()
    {
        var row = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["_sprk_createdbycontact_value"] = CallerContact.ToString(),
            ["_sprk_matter_value"] = Matter.ToString(),
            ["_sprk_invoice_value"] = null, // selected, not set
        })).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        // sprk_relatedinvoice was not read: the read cannot say there is no link.
        var parsed = ParseDeleteTarget(DeletableKind.Document, row);
        parsed.InvoiceLinks.Should().Equal(ExternalDocumentNarrowing.UnreadableLink);

        var id = SeedLinkedTo("documents");
        _fixture.Data.Targets[(DeletableKind.Document, id)] = _fixture.Data.Targets[(DeletableKind.Document, id)] with { InvoiceLinks = parsed.InvoiceLinks };

        using var refused = await Delete("documents", id);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();

        var allNull = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["_sprk_invoice_value"] = null,
            ["_sprk_relatedinvoice_value"] = null,
        })).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        ParseDeleteTarget(DeletableKind.Document, allNull).InvoiceLinks.Should().BeEmpty("a selected explicit null is no link");
    }

    [Fact(DisplayName = "130-0 fix2 F1 document delete (must fire): a document whose related invoice is another firm's is refused, and kept")]
    public async Task ADocumentWhoseRelatedInvoiceIsAnotherFirms_IsRefused()
    {
        // The route sees the links ParseDeleteTarget reads (above: sprk_relatedinvoice among them); here the related invoice is hidden.
        var id = SeedLinkedTo("documents", _other);

        using var response = await Delete("documents", id);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Exists(DeletableKind.Document, id).Should().BeTrue();
    }

    private Guid SeedLinkedTo(string segment, params Guid[] invoices)
    {
        var kind = KindOf(segment);
        var id = _fixture.Seed(kind, contact: CallerContact, parents: new DeleteTargetParent(DocumentParentKind.Matter, Matter));
        _fixture.Data.Targets[(kind, id)] = _fixture.Data.Targets[(kind, id)] with { InvoiceLinks = invoices };
        return id;
    }

    private async Task<HttpResponseMessage> Delete(string segment, Guid id)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.DeleteAsync($"/api/v1/external/{segment}/{id}");
    }

    private static DeletableKind KindOf(string segment) => segment switch
    {
        "documents" => DeletableKind.Document,
        "todos" => DeletableKind.Todo,
        _ => DeletableKind.Event,
    };
}

/// <summary>
/// ebilling-r1 task 130-0 fix round 2 (F1, creates and updates; K1): the external create and update request types cannot carry an
/// invoice link, so a create or a to-do PATCH can never set or change one. Pinned: a future property that maps to an invoice link column
/// of the target table (<see cref="InvoiceLinks.LinkColumnsOf"/>) fails here. And a row DTO whose links were never set is hidden.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalWriteRequestInvoiceLinkTests
{
    public static TheoryData<Type, string> RequestTypes => new()
    {
        { typeof(CreateExternalTodoRequest), "sprk_todo" },
        { typeof(UpdateExternalTodoRequest), "sprk_todo" },
        { typeof(CreateExternalEventRequest), "sprk_event" },
    };

    [Theory(DisplayName = "130-0 fix2 F1: no external create or update request type has a property that maps to an invoice link column")]
    [MemberData(nameof(RequestTypes))]
    public void NoRequestType_CarriesAnInvoiceLink(Type requestType, string table)
    {
        var links = InvoiceLinks.LinkColumnsOf(table);
        links.Should().NotBeEmpty($"{table} has invoice links, so the pin is not vacuous");
        var forms = links.SelectMany(c => new[] { c, $"_{c}_value", $"{c}@odata.bind" }).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var property in requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var wire = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            forms.Should().NotContain(wire, $"{requestType.Name}.{property.Name} would let a write set an invoice link");
            property.GetCustomAttribute<JsonExtensionDataAttribute>().Should().BeNull(
                $"{requestType.Name} must not accept arbitrary fields that could carry a link");
        }
    }

    [Fact(DisplayName = "130-0 fix2 F1: the document upload is a multipart file post; it takes no link field (the parent is the route)")]
    public void TheDocumentUpload_TakesNoInvoiceLinkField()
    {
        // The upload endpoints bind the file from the form and the parent from the route; the create writes the route parent's lookup
        // only (ExternalDataService.CreateDocumentAsync takes the parent kind and id). Pinned by the create's signature.
        var create = typeof(ExternalDataService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(ExternalDataService.CreateDocumentAsync)).ToList();
        create.Should().NotBeEmpty();
        create.SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
            .Should().NotContain(t => t == typeof(IDictionary<string, object?>) || t == typeof(IReadOnlyDictionary<string, object?>),
                "the document create takes no free-form field map");
    }

    [Fact(DisplayName = "130-0 fix2 K1: a row DTO whose invoice links were never set carries null, which the list filter hides")]
    public void ARowWithNoLinksSet_CarriesNull()
    {
        new ExternalTodoDto().InvoiceLinks.Should().BeNull();
        new ExternalEventDto().InvoiceLinks.Should().BeNull();
        new ExternalDocumentDto().InvoiceLinks.Should().BeNull();
        new DeleteTarget(null, null, Array.Empty<DeleteTargetParent>()).InvoiceLinks.Should().BeNull();
    }
}
