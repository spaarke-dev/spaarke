using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3b: a document's six root link columns decide whether a secure root it is filed to withholds it.
/// A root link column that is ABSENT from a Web API row was not read, so it is treated as unreadable and the document is refused (a
/// single read) or hidden (a list); an explicit JSON null is "not filed to that root". Every Web API read that feeds
/// <see cref="ExternalDataService.DocumentRootLinks"/> selects all six columns (pinned below).
/// </summary>
/// <remarks>
/// The routes are the REAL routes over the REAL <see cref="ExternalDataService"/> and
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/>, which answers a selected unset lookup as an explicit null and omits a
/// <see cref="ExternalRootEventReadTests.FakeDataverse.MaskedColumns"/> column. No FetchXML path feeds <c>DocumentRootLinks</c>
/// (the module reads take their root links from <c>ExternalModuleDescriptor.RootLinks</c>), so the select pins are on the Web API
/// reads: the list, the scoping read of the download and versions routes, the delete-own live-parent read and the invoice
/// source-document read.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalDocumentRootLinkReadTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid RecordA = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0390000-0000-4000-8000-00000000000b");

    private static readonly IReadOnlyDictionary<string, string> RouteColumnOf = new Dictionary<string, string>
    {
        ["projects"] = "_sprk_project_value",
        ["matters"] = "_sprk_matter_value",
        ["workassignments"] = "_sprk_workassignment_value",
    };

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;

    public ExternalDocumentRootLinkReadTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    /// <summary>(segment, route, masked column): every root link column other than the one the route itself binds.</summary>
    public static TheoryData<string, string, string> SingleReadCases
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (segment, routeColumn) in RouteColumnOf)
                foreach (var route in new[] { "content", "versions" })
                    foreach (var column in ExternalDataService.DocumentRootLinks.SelectColumns.Where(c => c != routeColumn))
                        data.Add(segment, route, column);
            return data;
        }
    }

    public static TheoryData<string, string> ListCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (segment, routeColumn) in RouteColumnOf)
                foreach (var column in ExternalDataService.DocumentRootLinks.SelectColumns.Where(c => c != routeColumn))
                    data.Add(segment, column);
            return data;
        }
    }

    // =====================================================================
    // Single document: refused when a root link column was not read
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3b single document (must fire): a read that did not return a root link column is refused as not under the record, before any pointer is resolved")]
    [MemberData(nameof(SingleReadCases))]
    public async Task ADocumentReadWithoutARootLinkColumn_IsRefused(string segment, string route, string maskedColumn)
    {
        _fixture.Principal = Holding(segment);
        var reference = await RefusalOf(segment, route);
        _fixture.ResetCalls();
        var id = _fixture.Data.AddDocument(segment, RecordA, "Filed only here");

        // Control: the same document, every column returned, is served.
        using (var served = await Get(segment, id, route))
            served.StatusCode.Should().Be(HttpStatusCode.OK, await served.Content.ReadAsStringAsync());
        _fixture.ResetCalls();

        _fixture.Data.MaskedColumns.Add(maskedColumn);
        using var response = await Get(segment, id, route);

        (await Summarise(response)).Should().Be(reference, $"{maskedColumn} was not returned, so the document reads as not under this record");
        _fixture.PointerResolutions.Should().Be(0, "authorization precedes pointer resolution");
        _fixture.FileReads.Should().Be(0);
    }

    [Theory(DisplayName = "130-0 fix3b single document (must not fire): an explicit null root link is not filed to that root, so the document is served")]
    [InlineData("projects", "content")]
    [InlineData("projects", "versions")]
    [InlineData("matters", "content")]
    [InlineData("matters", "versions")]
    [InlineData("workassignments", "content")]
    [InlineData("workassignments", "versions")]
    public async Task ADocumentWithExplicitNullRootLinks_IsServed(string segment, string route)
    {
        _fixture.Principal = Holding(segment);
        var id = _fixture.Data.AddDocument(segment, RecordA, "Filed only here");

        using var response = await Get(segment, id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.PointerResolutions.Should().Be(1);
    }

    // =====================================================================
    // List: hidden when a root link column was not read
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3b document list (must fire): rows whose read did not return a root link column are hidden; with every column returned they are listed")]
    [MemberData(nameof(ListCases))]
    public async Task AListReadWithoutARootLinkColumn_HidesTheRows(string segment, string maskedColumn)
    {
        _fixture.Principal = Holding(segment);
        _fixture.Data.AddDocument(segment, RecordA, "First");
        _fixture.Data.AddDocument(segment, RecordA, "Second");

        var control = await List(segment);
        control.Value.Select(d => d.SprkName).Should().BeEquivalentTo("First", "Second");

        _fixture.Data.MaskedColumns.Add(maskedColumn);
        var masked = await List(segment);

        masked.Value.Should().BeEmpty($"{maskedColumn} was not returned, so no row can be shown");
    }

    [Fact(DisplayName = "130-0 fix3b document list (must not fire): rows with explicit null root links are listed; a row filed to a readable twin root is unchanged")]
    public async Task AListWithExplicitNullRootLinks_IsUnchanged()
    {
        _fixture.Principal = Holding("matters");
        _fixture.Data.AddDocument("matters", RecordA, "Filed only here");
        _fixture.Data.AddDocument("matters", RecordA, "Also on another matter the caller reads", ("_sprk_relatedmatter_value", RecordA));

        var body = await List("matters");

        body.Value.Select(d => d.SprkName).Should().BeEquivalentTo("Filed only here", "Also on another matter the caller reads");
    }

    // =====================================================================
    // The link value itself
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3b DocumentRootLinks.From: an absent column is unreadable (withheld); an explicit null is not filed; a GUID is kept")]
    public void From_AnAbsentColumn_IsUnreadable_AnExplicitNull_IsNotFiled()
    {
        var caller = Holding("projects");
        // Fix round 3c: the live-parent lookup columns are read from the same row, so they are present (explicit nulls) here too.
        var allNull = ExternalDataService.DocumentRootLinks.SelectColumns.Concat(ExternalMultiRootRule.LiveParentSelect("sprk_document"))
            .ToDictionary(c => c, _ => NullElement());
        var link = RecordB.ToString();
        var all = new Dictionary<string, JsonElement>(allNull);
        all["_sprk_matter_value"] = JsonSerializer.SerializeToElement(link);

        var explicitNulls = ExternalDataService.DocumentRootLinks.From(allNull);
        var kept = ExternalDataService.DocumentRootLinks.From(all);

        explicitNulls.Links.Select(l => l.LookupValue).Should().OnlyContain(v => v == null);
        explicitNulls.SecureRootsReadableBy(caller, SecureRootFlags.None).Should().BeTrue("a document filed to no root is not withheld by one");
        kept.Matter.Should().Be(link);
        foreach (var column in ExternalDataService.DocumentRootLinks.SelectColumns)
        {
            var absent = allNull.Where(kv => kv.Key != column).ToDictionary(kv => kv.Key, kv => kv.Value);

            var links = ExternalDataService.DocumentRootLinks.From(absent);

            links.Links.Count(l => l.LookupValue == ExternalDataService.DocumentRootLinks.Unreadable).Should().Be(1, $"only {column} was not read");
            links.SecureRootsReadableBy(caller, SecureRootFlags.None).Should().BeFalse($"{column} was not read, so the document is withheld");
        }
    }

    // =====================================================================
    // Pins: every Web API read that feeds DocumentRootLinks selects all six columns
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3b pin: the document LIST select names every root link column")]
    [InlineData(ExternalDataService.DocumentParentKind.Project)]
    [InlineData(ExternalDataService.DocumentParentKind.Matter)]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment)]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest)]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice)]
    public void TheListSelect_NamesEveryRootLinkColumn(ExternalDataService.DocumentParentKind kind)
    {
        var url = Uri.UnescapeDataString(ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", kind, RecordA));

        SelectOf(url).Should().Contain(ExternalDataService.DocumentRootLinks.SelectColumns);
    }

    [Theory(DisplayName = "130-0 fix3b pin: the scoping read of the single-document routes, run through the real fetch, selects every root link column")]
    [InlineData(ExternalDataService.DocumentParentKind.Project)]
    [InlineData(ExternalDataService.DocumentParentKind.Matter)]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment)]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest)]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice)]
    public async Task TheScopingRead_SelectsEveryRootLinkColumn(ExternalDataService.DocumentParentKind kind)
    {
        await _fixture.NewService().GetDocumentParentAndNameAsync(kind, Guid.NewGuid());

        var url = _fixture.Data.Urls.Should().ContainSingle().Subject;
        SelectOf(url).Should().Contain(ExternalDataService.DocumentRootLinks.SelectColumns);
    }

    [Fact(DisplayName = "130-0 fix3b pin: the delete-own live-parent read of a document, run through the real fetch, selects every root link column")]
    public async Task TheDeleteOwnLiveParentRead_SelectsEveryRootLinkColumn()
    {
        await _fixture.NewService().CheckLiveParentsAsync(
            ExternalDataService.DeletableKind.Document, Guid.NewGuid(), Holding("matters"));

        var url = _fixture.Data.Urls.Single(u => u.Contains("/sprk_documents("));
        SelectOf(url).Should().Contain(ExternalDataService.DocumentRootLinks.SelectColumns);
    }

    [Fact(DisplayName = "130-0 fix3b pin: the invoice source-document read selects every root link column")]
    public void TheInvoiceSourceDocumentRead_SelectsEveryRootLinkColumn()
    {
        ExternalInvoiceVisibility.SourceSelectColumns.Should().Contain(ExternalDataService.DocumentRootLinks.SelectColumns);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string[] SelectOf(string url) =>
        url.Split("$select=")[1].Split('&')[0].Split(',');

    private static JsonElement NullElement() => JsonDocument.Parse("null").RootElement.Clone();

    private static CallerPrincipal Holding(string segment) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = segment == "projects"
            ? new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) }
            : Array.Empty<CallerProjectAccess>(),
        MatterAccess = segment == "matters"
            ? new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.Read }
            : new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = segment == "workassignments"
            ? new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.Read }
            : new Dictionary<Guid, AccessRights>(),
    };

    /// <summary>The refusal the route gives a document that is not under the record (it exists, under another record).</summary>
    private async Task<string> RefusalOf(string segment, string route)
    {
        var elsewhere = _fixture.Data.AddDocument(segment, RecordB, "Another record's");
        using var response = await Get(segment, elsewhere, route);
        var summary = await Summarise(response);
        summary.Should().StartWith("403");
        return summary;
    }

    private async Task<HttpResponseMessage> Get(string segment, string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents/{documentId}/{route}");
    }

    private async Task<ExternalCollectionResponse<ExternalDocumentDto>> List(string segment)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExternalCollectionResponse<ExternalDocumentDto>>())!;
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
