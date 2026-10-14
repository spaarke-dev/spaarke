using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 187 (owner round 99, D-123; #1857), documents: the document's own root links stay the primary
/// input; the LIVE roots of its related links (<c>sprk_relatedevent</c> / <c>sprk_relatedcommunication</c> /
/// <c>sprk_relatedtodo</c> / <c>sprk_invoice</c> ...) join the rule on the list, the download and the versions routes.
/// </summary>
[Trait("category", "external-access")]
[Trait("status", "repaired")]
public sealed class ExternalLiveParentDocumentTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid P = Guid.Parse("a1870000-0000-4000-8000-0000000000a1");
    private static readonly Guid X = Guid.Parse("a1870000-0000-4000-8000-0000000000d1");
    private static readonly Guid O = Guid.Parse("a1870000-0000-4000-8000-0000000000e1");
    private static readonly Guid E = Guid.Parse("c1870000-0000-4000-8000-0000000000e1");
    private static readonly Guid I = Guid.Parse("c1870000-0000-4000-8000-0000000000f1");

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;

    public ExternalLiveParentDocumentTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string, string> RelatedLinks => new()
    {
        { "_sprk_relatedevent_value", "sprk_event" },
        { "_sprk_relatedcommunication_value", "sprk_communication" },
        { "_sprk_relatedtodo_value", "sprk_todo" },
        { "_sprk_invoice_value", "sprk_invoice" },
    };

    [Theory]
    [MemberData(nameof(RelatedLinks))]
    public async Task ADocumentWhoseRelatedLinkIsUnderSecureX_IsWithheldFromTheList(string column, string table)
    {
        _fixture.Principal = Reader(P);
        SeedParentUnder(table, E, X, secure: true);
        var plain = _fixture.Data.AddDocument("projects", P, "Plain");
        _fixture.Data.AddDocument("projects", P, "Related to secure X", (column, E));

        var body = await List(P);

        body.Value.Select(d => d.SprkDocumentid).Should().Equal(plain);
    }

    [Theory]
    [MemberData(nameof(RelatedLinks))]
    public async Task ADocumentWhoseRelatedLinkIsUnderAnOrdinaryRoot_IsListed(string column, string table)
    {
        _fixture.Principal = Reader(P);
        SeedParentUnder(table, E, O, secure: false);
        var doc = _fixture.Data.AddDocument("projects", P, "Related to ordinary O", (column, E));

        (await List(P)).Value.Select(d => d.SprkDocumentid).Should().Equal(doc);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task ADocumentWhoseRelatedEventIsUnderSecureX_IsRefused_AsNotUnderTheParent(string route)
    {
        _fixture.Principal = Reader(P);
        var reference = await RefusalOf(route);
        SeedParentUnder("sprk_event", E, X, secure: true);
        var id = _fixture.Data.AddDocument("projects", P, "Related to secure X", ("_sprk_relatedevent_value", E));
        _fixture.ResetCalls();

        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference, "the same refusal as a document not under the parent");
        _fixture.PointerResolutions.Should().Be(0, "nothing is read from SPE");
    }

    [Theory]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task ADocumentWhoseRelatedEventIsUnderSecureX_IsServed_ToACallerWhoReadsX(string route)
    {
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.NewGuid(),
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(P, ExternalAccessLevel.ViewOnly) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [X] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };
        SeedParentUnder("sprk_event", E, X, secure: true);
        var id = _fixture.Data.AddDocument("projects", P, "Related to secure X", ("_sprk_relatedevent_value", E));

        using var response = await Get(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task ADocumentWhoseRelatedLinkCannotBeResolved_IsRefused_FailClosed(string route)
    {
        _fixture.Principal = Reader(P);
        var reference = await RefusalOf(route);
        // E is named by the document, but the walk's read does not return it.
        var id = _fixture.Data.AddDocument("projects", P, "Unresolvable related event", ("_sprk_relatedevent_value", E));

        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference);
    }

    [Fact]
    public async Task OnAnInvoiceRoute_TheRouteInvoiceIsNotWalkedAgain_OptionAKept()
    {
        // Owner decision 2026-10-11 (option A): an invoice is reached through the union of its parents. A document filed to the
        // route invoice only is not withheld for the invoice's OTHER (secure) parent; its other related links still count.
        _fixture.Principal = Reader(P);
        _fixture.Data.AddInvoice(I, matter: X, project: P);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        _fixture.Data.AddFilingRow("sprk_invoice", I, ("sprk_matter", "sprk_matter", X), ("sprk_project", "sprk_project", P));
        SeedParentUnder("sprk_event", E, X, secure: true);
        var onlyInvoice = _fixture.Data.AddDocument("invoices", I, "Filed to the invoice");
        _fixture.Data.AddDocument("invoices", I, "And to an event of secure X", ("_sprk_relatedevent_value", E));

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/invoices/{I}/documents");

        body!.Value.Select(d => d.SprkDocumentid).Should().Equal(onlyInvoice);
    }

    // ---- helpers ----

    private void SeedParentUnder(string table, Guid parent, Guid matter, bool secure)
    {
        if (secure) _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, matter);
        else _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, matter);
        var column = table == "sprk_invoice" ? "sprk_matter" : "sprk_regardingmatter";
        _fixture.Data.AddFilingRow(table, parent, (column, "sprk_matter", matter));
    }

    private async Task<ExternalDocumentListResponse> List(Guid project)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/projects/{project}/documents"))!;
    }

    private async Task<HttpResponseMessage> Get(string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/projects/{P}/documents/{documentId}/{route}");
    }

    /// <summary>The route's response for a document that is not under the parent at all (the reference refusal).</summary>
    private async Task<string> RefusalOf(string route)
    {
        var elsewhere = _fixture.Data.AddDocument("projects", Guid.NewGuid(), "Another record's");
        using var response = await Get(elsewhere, route);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        return await Summarise(response);
    }

    /// <summary>A refusal as a caller sees it: status, title and detail (no per-request fields).</summary>
    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return $"{(int)response.StatusCode}";

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|", new[] { "403" }.Concat(doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }

    private static CallerPrincipal Reader(Guid project) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.ViewOnly) },
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}
