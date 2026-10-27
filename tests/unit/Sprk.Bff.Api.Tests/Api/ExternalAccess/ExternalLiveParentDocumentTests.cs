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
        if (table == "sprk_communication")
        {
            // Task 188: a document linked to a communication is visible only when that communication is (the 065 rule:
            // shareable, on an Open thread), so the linked communication here is a visible one.
            var thread = Guid.Parse("c1870000-0000-4000-8000-0000000000c1");
            _fixture.Data.AddCommunication(E, internalOnly: false, thread);
            _fixture.Data.AddThread(thread, (int)Sprk.Bff.Api.Services.Communication.Membership.ThreadPrivacyState.Open);
        }

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

    // ---- Task 187, fix round 1, owner decision D4: the invoice document routes ----
    //
    // An invoice is reached through the union of its parents (owner decision 2026-10-11, option A). But the route invoice's
    // own LIVE roots join each document's rule: when they include a SECURE root the caller cannot read, the invoice's
    // documents are withheld from the list and refused on download and versions, as the invoices list withholds the
    // invoice itself (D2). A caller who reaches an invoice whose other parents are ordinary keeps today's behaviour.

    /// <summary>Invoice <see cref="I"/> under project <see cref="P"/> and a matter that is secure X or ordinary O.</summary>
    private Guid SeedInvoiceUnderPAnd(bool secureMatter)
    {
        var matter = secureMatter ? X : O;
        _fixture.Data.AddInvoice(I, matter: matter, project: P);
        if (secureMatter) _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        else _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
        return matter;
    }

    [Fact]
    public async Task OnAnInvoiceRoute_AnInvoiceAlsoUnderSecureX_WithholdsItsDocuments_FromAPOnlyReader_D4()
    {
        _fixture.Principal = Reader(P);
        SeedInvoiceUnderPAnd(secureMatter: true);
        _fixture.Data.AddDocument("invoices", I, "Filed to the invoice");

        (await InvoiceDocuments()).Value.Should().BeEmpty("the invoice is behind secure X's wall");
    }

    [Fact]
    public async Task OnAnInvoiceRoute_AnInvoiceAlsoUnderAnOrdinaryRoot_ListsItsDocuments_OptionA()
    {
        _fixture.Principal = Reader(P);
        SeedInvoiceUnderPAnd(secureMatter: false);
        SeedParentUnder("sprk_event", E, X, secure: true);
        var onlyInvoice = _fixture.Data.AddDocument("invoices", I, "Filed to the invoice");
        _fixture.Data.AddDocument("invoices", I, "And to an event of secure X", ("_sprk_relatedevent_value", E));

        (await InvoiceDocuments()).Value.Select(d => d.SprkDocumentid).Should().Equal(new[] { onlyInvoice },
            "option A for the ordinary invoice; the document's other parent still counts");
    }

    [Theory]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task OnAnInvoiceRoute_ADocumentOfAnInvoiceAlsoUnderSecureX_IsRefused_AsNotUnderTheParent_D4(string route)
    {
        _fixture.Principal = Reader(P);
        SeedInvoiceUnderPAnd(secureMatter: true);
        var reference = await InvoiceRefusalOf(route);
        var id = _fixture.Data.AddDocument("invoices", I, "Filed to the invoice");
        _fixture.ResetCalls();

        using var response = await GetOnInvoice(id, route);

        (await Summarise(response)).Should().Be(reference, "the same refusal as a document not under the invoice");
        _fixture.PointerResolutions.Should().Be(0, "nothing is read from SPE");
    }

    [Theory]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task OnAnInvoiceRoute_ADocumentOfAnInvoiceAlsoUnderAnOrdinaryRoot_IsServed_OptionA(string route)
    {
        _fixture.Principal = Reader(P);
        SeedInvoiceUnderPAnd(secureMatter: false);
        var id = _fixture.Data.AddDocument("invoices", I, "Filed to the invoice");

        using var response = await GetOnInvoice(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Task 187, fix round 2 (F1): an upload to an invoice passes the invoice gate, over the REAL reads ----
    //
    // A caller with Collaborate on ordinary matter O; invoice I under O and project X. Through the real invoice read, the real
    // secure-flag read and the invoices module's predicate: X secure and unreadable refuses the upload (403) before SPE or
    // Dataverse is touched; X ordinary keeps option A (the list offers an upload); a flag read that fails is the 503.

    private static CallerPrincipal CollaboratorOn(Guid matter) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [matter] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate) },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private void SeedInvoiceUnderOAndProjectX(bool? secureX)
    {
        _fixture.Data.AddInvoice(I, matter: O, project: X);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
        if (secureX is { } secure) _fixture.Data.AddRoot(ExternalGrantRootType.Project, X, secure);
        _fixture.Principal = CollaboratorOn(O);
    }

    private async Task<HttpResponseMessage> UploadToInvoice()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "brief.pdf" } };
        return await client.PostAsync($"/api/v1/external/invoices/{I}/documents", form);
    }

    private void NothingWasWritten()
    {
        _fixture.Uploads.Should().Be(0, "nothing reaches SPE");
        _fixture.Data.Requests.Should().OnlyContain(r => r.StartsWith("GET ", StringComparison.Ordinal),
            "nothing is written to Dataverse");
    }

    [Fact]
    public async Task UploadToAnInvoiceAlsoUnderSecureProjectX_IsRefused_403_BeforeAnyWrite_F1()
    {
        SeedInvoiceUnderOAndProjectX(secureX: true);

        using var response = await UploadToInvoice();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        NothingWasWritten();
        _fixture.Data.Urls.Should().Contain(u => u.Contains("/sprk_invoices(", StringComparison.Ordinal)
                                                 && u.Contains("_sprk_regardingagreement_value", StringComparison.Ordinal),
            "the gate reads every attribute the rule needs, the agreement included");
        (await InvoiceDocuments()).CanCreate.Should().BeFalse("the list offers no upload that can only fail");
    }

    [Fact]
    public async Task AnInvoiceAlsoUnderAnOrdinaryProjectX_KeepsCreate_OptionA_F1()
    {
        SeedInvoiceUnderOAndProjectX(secureX: false);

        (await InvoiceDocuments()).CanCreate.Should().BeTrue("option A: Create on O admits an upload");
    }

    [Fact]
    public async Task UploadToAnInvoice_WhenTheSecureFlagReadFails_Is503_BeforeAnyWrite_F1()
    {
        SeedInvoiceUnderOAndProjectX(secureX: false);
        _fixture.Data.FlagReadThrows = true;

        using var response = await UploadToInvoice();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sdap.access.invoice_unreadable");
        NothingWasWritten();
    }

    private async Task<ExternalDocumentListResponse> InvoiceDocuments()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/invoices/{I}/documents"))!;
    }

    private async Task<HttpResponseMessage> GetOnInvoice(string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/invoices/{I}/documents/{documentId}/{route}");
    }

    /// <summary>The invoice route's response for a document that is not under <see cref="I"/> at all.</summary>
    private async Task<string> InvoiceRefusalOf(string route)
    {
        var elsewhere = _fixture.Data.AddDocument("invoices", Guid.NewGuid(), "Another invoice's");
        using var response = await GetOnInvoice(elsewhere, route);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        return await Summarise(response);
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
