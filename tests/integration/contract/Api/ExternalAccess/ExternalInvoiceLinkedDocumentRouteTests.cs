using System.Net;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 1: the single-document routes (content download and version history) under a shared record
/// serve a document that belongs to an invoice only when that invoice is visible to the caller (a partner sees only invoices of its
/// own firms, and what belongs to them). Through either invoice link (<c>sprk_invoice</c>, <c>sprk_relatedinvoice</c>). A refused
/// document answers exactly as one that is not under the record, before any storage pointer is resolved.
/// </summary>
/// <remarks>The REAL routes and <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/>, with the
/// REAL caller-aware invoice seam over <see cref="InvoiceWorld"/>.</remarks>
[Trait("category", "external-access")]
public sealed class ExternalInvoiceLinkedDocumentRouteTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid Matter = Guid.Parse("13000000-0000-4000-8000-00000000001a");
    private static readonly Guid OtherMatter = Guid.Parse("13000000-0000-4000-8000-00000000001b");
    private static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b2");

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;
    private readonly InvoiceWorld _world = new();
    private readonly InvoiceHidingStub _hiding;
    private readonly Guid _own;
    private readonly Guid _other;

    public ExternalInvoiceLinkedDocumentRouteTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
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
        _fixture.Data.AddInvoice(_own, Matter, null);
        _fixture.Data.AddInvoice(_other, Matter, null);
        _hiding = InvoiceHidingStub.Real(_world);
        _fixture.InvoiceHiding = _hiding;
    }

    public static TheoryData<string, string> LinksAndRoutes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var link in new[] { "_sprk_invoice_value", "_sprk_relatedinvoice_value" })
                foreach (var route in new[] { "content", "versions" })
                    data.Add(link, route);
            return data;
        }
    }

    [Theory(DisplayName = "130-0 fix1 single document (must fire): a document of another firm's invoice is refused as not under the record, before any pointer is resolved")]
    [MemberData(nameof(LinksAndRoutes))]
    public async Task ADocumentOfAnotherFirmsInvoice_IsRefused_AsNotUnderTheRecord(string link, string route)
    {
        var reference = await RefusalOf(route);
        _fixture.ResetCalls();
        var id = _fixture.Data.AddDocument("matters", Matter, "Other firm's invoice", (link, _other));

        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference, "a hidden invoice's document reads as one that is not here");
        _fixture.PointerResolutions.Should().Be(0, "authorization precedes pointer resolution");
        _fixture.FileReads.Should().Be(0);
        _hiding.Calls.Should().ContainSingle().Which.Should().Equal(_other);
    }

    [Theory(DisplayName = "130-0 fix1 single document (must not fire): a document of the caller's own firm's invoice is served")]
    [MemberData(nameof(LinksAndRoutes))]
    public async Task ADocumentOfTheCallersOwnFirmsInvoice_IsServed(string link, string route)
    {
        var id = _fixture.Data.AddDocument("matters", Matter, "Own firm's invoice", (link, _own));

        using var response = await Get(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.PointerResolutions.Should().Be(1);
    }

    [Theory(DisplayName = "130-0 fix1 single document (must not fire): a document with no invoice link is served and asks no invoice decision")]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task ADocumentWithNoInvoiceLink_IsServed_AndAsksNothing(string route)
    {
        var id = _fixture.Data.AddDocument("matters", Matter, "No invoice");

        using var response = await Get(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _hiding.Calls.Should().BeEmpty();
    }

    [Theory(DisplayName = "130-0 fix2 single document (fault, must fire): the caller's firms cannot be read, so an invoice-linked document is the invoice-unreadable 503, never the refusal")]
    [MemberData(nameof(LinksAndRoutes))]
    public async Task WhenTheFirmReadFails_AnInvoiceLinkedDocument_IsThe503(string link, string route)
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var id = _fixture.Data.AddDocument("matters", Matter, "Own firm's invoice", (link, _own));

        using var response = await Get(id, route);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("reasonCode").GetString()
            .Should().Be(ExternalProjectDataEndpoints.InvoiceUnreadableReasonCode);
        _fixture.PointerResolutions.Should().Be(0, "nothing is served while the invoice decision is unknown");
        _fixture.FileReads.Should().Be(0);
    }

    [Theory(DisplayName = "130-0 fix2 single document (fault, must not fire): a document with no invoice link is served while the caller's firms cannot be read")]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task WhenTheFirmReadFails_ADocumentWithNoInvoiceLink_IsServed(string route)
    {
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var id = _fixture.Data.AddDocument("matters", Matter, "No invoice");

        using var response = await Get(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _hiding.Calls.Should().BeEmpty();
    }

    [Theory(DisplayName = "130-0 fix2 single document (fault, order): an invoice-linked document not under the record is the uniform refusal, not the 503")]
    [MemberData(nameof(LinksAndRoutes))]
    public async Task WhenTheFirmReadFails_AnInvoiceLinkedDocumentElsewhere_IsTheRefusal(string link, string route)
    {
        var reference = await RefusalOf(route);
        _fixture.ResetCalls();
        _world.CallerFirmsFault = new TimeoutException("The caller-firm read failed (test).");
        var id = _fixture.Data.AddDocument("matters", OtherMatter, "Another record's invoice", (link, _own));

        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference, "the record check precedes the invoice decision");
        _hiding.Calls.Should().BeEmpty();
    }

    private async Task<string> RefusalOf(string route)
    {
        var elsewhere = _fixture.Data.AddDocument("matters", OtherMatter, "Another record's");
        using var response = await Get(elsewhere, route);
        var summary = await Summarise(response);
        summary.Should().StartWith("403");
        return summary;
    }

    private async Task<HttpResponseMessage> Get(string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/matters/{Matter}/documents/{documentId}/{route}");
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
}
