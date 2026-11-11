using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3f: a document's own Access Permission (<c>sprk_accesspermission</c>, a choice column) in a Web API
/// row. A row lists a selected column it holds as its value, an unset choice as an explicit JSON <c>null</c>; a row that does not carry
/// the column did not read it, so the column is treated as unreadable (the document is hidden on a list and refused on a single read, an
/// invoice whose source document it is is hidden). An explicit null keeps its meaning (not narrowed), as do the known levels. SDK and
/// FetchXML rows omit a null attribute, so there an absent attribute stays "not set" and the projection is pinned instead.
/// </summary>
/// <remarks>
/// The routes are the REAL routes over the REAL <see cref="ExternalDataService"/> and
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/>, which answers a selected unset column as an explicit null and omits a
/// <see cref="ExternalRootEventReadTests.FakeDataverse.MaskedColumns"/> column.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalDocumentAccessPermissionAbsentColumnTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private const string Column = "sprk_accesspermission";
    private const int Standard = 100000000;
    private const int Limited = 100000001;
    private const int Restricted = 100000002;

    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-99999999113f");
    private static readonly Guid RecordA = Guid.Parse("a113f000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a113f000-0000-4000-8000-00000000000b");

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;

    public ExternalDocumentAccessPermissionAbsentColumnTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string, string> SingleReads
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var segment in new[] { "projects", "matters", "workassignments" })
                foreach (var route in new[] { "content", "versions" })
                    data.Add(segment, route);
            return data;
        }
    }

    // =====================================================================
    // Single document: refused (the one 403, never a 500) when the column was not read
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3f single document (must fire): a read that did not return the Access Permission column is refused as not under the record, before any pointer is resolved")]
    [MemberData(nameof(SingleReads))]
    public async Task ADocumentReadWithoutTheAccessPermission_IsRefused(string segment, string route)
    {
        _fixture.Principal = Holding(segment);
        var reference = await RefusalOf(segment, route);
        _fixture.ResetCalls();
        var id = _fixture.Data.AddDocument(segment, RecordA, "Filed only here");

        using (var served = await Get(segment, id, route))
            served.StatusCode.Should().Be(HttpStatusCode.OK, await served.Content.ReadAsStringAsync()); // control: explicit null
        _fixture.ResetCalls();

        _fixture.Data.MaskedColumns.Add(Column);
        using var response = await Get(segment, id, route);

        (await Summarise(response)).Should().Be(reference, "the column was not returned, so the document reads as not under this record");
        _fixture.PointerResolutions.Should().Be(0, "authorization precedes pointer resolution");
        _fixture.FileReads.Should().Be(0);
    }

    [Theory(DisplayName = "130-0 fix3f single document (must not fire): an explicit null, Standard and Limited serve the document; Restricted refuses it")]
    [InlineData("projects", "content", null, true)]
    [InlineData("projects", "versions", null, true)]
    [InlineData("matters", "content", Standard, true)]
    [InlineData("matters", "versions", Limited, true)]
    [InlineData("workassignments", "content", Standard, true)]
    [InlineData("projects", "content", Restricted, false)]
    [InlineData("matters", "versions", Restricted, false)]
    public async Task AKnownValue_BehavesAsBefore(string segment, string route, int? value, bool served)
    {
        _fixture.Principal = Holding(segment);
        var reference = await RefusalOf(segment, route);
        var id = _fixture.Data.AddDocument(segment, RecordA, "Filed only here");
        if (value is { } v)
            _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", id, Column, v);

        using var response = await Get(segment, id, route);

        if (served)
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        else
            (await Summarise(response)).Should().Be(reference);
    }

    // =====================================================================
    // List: hidden when the column was not read
    // =====================================================================

    [Theory(DisplayName = "130-0 fix3f document list (must fire): rows whose read did not return the Access Permission column are hidden (not a fault); with it returned they are listed")]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    public async Task AListReadWithoutTheAccessPermission_HidesTheRows(string segment)
    {
        _fixture.Principal = Holding(segment);
        _fixture.Data.AddDocument(segment, RecordA, "First");
        _fixture.Data.AddDocument(segment, RecordA, "Second");
        (await List(segment)).Value.Select(d => d.SprkName).Should().BeEquivalentTo("First", "Second");

        _fixture.Data.MaskedColumns.Add(Column);
        var masked = await List(segment);

        masked.Value.Should().BeEmpty("the column was not returned, so no row can be shown");
        masked.Truncated.Should().BeFalse("a document the rule hides is not a read fault");
    }

    [Fact(DisplayName = "130-0 fix3f document list (must not fire): explicit null, Standard and Limited list; Restricted is hidden")]
    public async Task AListWithKnownValues_IsUnchanged()
    {
        _fixture.Principal = Holding("matters");
        _fixture.Data.AddDocument("matters", RecordA, "Null");
        var standard = _fixture.Data.AddDocument("matters", RecordA, "Standard");
        var limited = _fixture.Data.AddDocument("matters", RecordA, "Limited");
        var restricted = _fixture.Data.AddDocument("matters", RecordA, "Restricted");
        _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", standard, Column, Standard);
        _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", limited, Column, Limited);
        _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", restricted, Column, Restricted);

        (await List("matters")).Value.Select(d => d.SprkName).Should().BeEquivalentTo("Null", "Standard", "Limited");
    }

    // =====================================================================
    // The row parse and the decision
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3f FromWebApi (must fire): a row without the column, and no row, read the own value as unreadable")]
    public void FromWebApi_AnAbsentColumn_IsUnreadable()
    {
        var otherColumns = ExternalDocumentNarrowing.SelectColumns.Where(c => c != Column).ToDictionary(c => c, _ => NullElement());

        ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), otherColumns).Own.Should().Be(DocumentPermission.Unreadable);
        ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), null).Own.Should().Be(DocumentPermission.Unreadable);
        ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), otherColumns, readSubmissionMarker: true).Own.Should().Be(DocumentPermission.Unreadable);
    }

    [Fact(DisplayName = "130-0 fix3f FromWebApi (must not fire): an explicit null is not narrowed; Standard and Limited are not narrowed; Restricted is Restricted; an unknown level is unreadable")]
    public void FromWebApi_ReturnedValues_AreReadAsBefore()
    {
        DocumentPermission Own(JsonElement value)
        {
            var row = ExternalDocumentNarrowing.SelectColumns.ToDictionary(c => c, _ => NullElement());
            row[Column] = value;
            return ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), row).Own;
        }

        Own(NullElement()).Should().Be(DocumentPermission.NotNarrowed);
        Own(JsonSerializer.SerializeToElement(Standard)).Should().Be(DocumentPermission.NotNarrowed);
        Own(JsonSerializer.SerializeToElement(Limited)).Should().Be(DocumentPermission.NotNarrowed);
        Own(JsonSerializer.SerializeToElement(Restricted)).Should().Be(DocumentPermission.Restricted);
        Own(JsonSerializer.SerializeToElement(424242)).Should().Be(DocumentPermission.Unreadable);
        Own(JsonSerializer.SerializeToElement("100000002")).Should().Be(DocumentPermission.Unreadable);
    }

    private static Task<IReadOnlyList<Dictionary<string, JsonElement>>> NoRead(string set, string odata, CancellationToken ct) =>
        throw new InvalidOperationException($"No read was expected ({set}).");

    [Fact(DisplayName = "130-0 fix3f decision (must fire): an absent column hides the document without a read, and the submission marker never lifts it")]
    public async Task TheDecision_AnAbsentColumn_HidesTheDocument_EvenWithTheMarker()
    {
        var row = ExternalDocumentNarrowing.SelectColumns.Where(c => c != Column).ToDictionary(c => c, _ => NullElement());
        row[ExternalDocumentNarrowing.EmailArchiveField] = JsonSerializer.SerializeToElement(true);
        row[ExternalDocumentNarrowing.SubmissionMarkerField] = JsonSerializer.SerializeToElement(true);
        var document = ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), row, readSubmissionMarker: true);
        document.SubmissionMarked.Should().BeTrue();

        var plain = await ExternalDocumentNarrowing.DecideAsync(new[] { document }, NoRead, default);
        var marked = await ExternalDocumentNarrowing.DecideAsync(new[] { document }, NoRead, default, honourSubmissionMarker: true);

        plain.ReasonFor(document).Should().Be(DocumentNarrowingReason.OwnUnreadable);
        marked.ReasonFor(document).Should().Be(DocumentNarrowingReason.OwnUnreadable);
    }

    [Fact(DisplayName = "130-0 fix3f decision (must not fire): an explicit null is visible; an explicit Restricted .eml with the marker keeps the own-leg lift, and without the marker stays hidden")]
    public async Task TheDecision_ReturnedValues_BehaveAsBefore()
    {
        DocumentNarrowingRow Row(JsonElement value, bool marker)
        {
            var row = ExternalDocumentNarrowing.SelectColumns.ToDictionary(c => c, _ => NullElement());
            row[Column] = value;
            row[ExternalDocumentNarrowing.EmailArchiveField] = JsonSerializer.SerializeToElement(marker);
            row[ExternalDocumentNarrowing.SubmissionMarkerField] = JsonSerializer.SerializeToElement(marker);
            return ExternalDocumentNarrowing.FromWebApi(Guid.NewGuid(), row, readSubmissionMarker: true);
        }

        var explicitNull = Row(NullElement(), marker: false);
        var restrictedMarked = Row(JsonSerializer.SerializeToElement(Restricted), marker: true);
        var restrictedUnmarked = Row(JsonSerializer.SerializeToElement(Restricted), marker: false);

        var result = await ExternalDocumentNarrowing.DecideAsync(
            new[] { explicitNull, restrictedMarked, restrictedUnmarked }, NoRead, default, honourSubmissionMarker: true);

        result.IsVisible(explicitNull).Should().BeTrue();
        result.IsVisible(restrictedMarked).Should().BeTrue("the own-leg lift of the invoice's marked .eml is unchanged");
        result.ReasonFor(restrictedUnmarked).Should().Be(DocumentNarrowingReason.OwnRestricted);
    }

    // =====================================================================
    // SDK / FetchXML rows are unchanged: the SDK omits a null attribute, so the projection is pinned instead
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3f FromFetch / FromEntity (unchanged): an absent attribute is not set, because the SDK omits a null one; an option value is read")]
    public void SdkRows_AnAbsentAttribute_IsNotSet()
    {
        var withNothing = new Dictionary<string, object?> { [ExternalDocumentNarrowing.DocumentPkField] = Guid.NewGuid() };
        ExternalDocumentNarrowing.FromFetch(withNothing).Own.Should().Be(DocumentPermission.NotNarrowed);
        ExternalDocumentNarrowing.FromEntity(new Entity("sprk_document", Guid.NewGuid())).Own.Should().Be(DocumentPermission.NotNarrowed);

        var restricted = new Entity("sprk_document", Guid.NewGuid()) { [Column] = new OptionSetValue(Restricted) };
        ExternalDocumentNarrowing.FromEntity(restricted).Own.Should().Be(DocumentPermission.Restricted);
    }

    // =====================================================================
    // Pins: every reader of a document row selects / projects the column
    // =====================================================================

    [Fact(DisplayName = "130-0 fix3f pin: the narrowing's select and projection name the Access Permission")]
    public void TheNarrowingColumns_NameTheAccessPermission()
    {
        ExternalDocumentNarrowing.SelectColumns.Should().Contain(ExternalDocumentNarrowing.AccessPermissionField);
        ExternalDocumentNarrowing.FetchAttributes.Should().Contain(ExternalDocumentNarrowing.AccessPermissionField);
        ExternalDocumentNarrowing.AccessPermissionField.Should().Be(Column);
    }

    [Theory(DisplayName = "130-0 fix3f pin: the document LIST select names the Access Permission")]
    [InlineData(ExternalDataService.DocumentParentKind.Project)]
    [InlineData(ExternalDataService.DocumentParentKind.Matter)]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment)]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest)]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice)]
    public void TheListSelect_NamesTheAccessPermission(ExternalDataService.DocumentParentKind kind)
    {
        var url = Uri.UnescapeDataString(ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", kind, RecordA));

        SelectOf(url).Should().Contain(ExternalDocumentNarrowing.SelectColumns);
    }

    [Theory(DisplayName = "130-0 fix3f pin: the scoping read of the single-document routes, run through the real fetch, selects the Access Permission")]
    [InlineData(ExternalDataService.DocumentParentKind.Project)]
    [InlineData(ExternalDataService.DocumentParentKind.Matter)]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment)]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest)]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice)]
    public async Task TheScopingRead_SelectsTheAccessPermission(ExternalDataService.DocumentParentKind kind)
    {
        await _fixture.NewService().GetDocumentParentAndNameAsync(kind, Guid.NewGuid());

        var url = _fixture.Data.Urls.Should().ContainSingle().Subject;
        SelectOf(url).Should().Contain(ExternalDocumentNarrowing.SelectColumns);
    }

    [Fact(DisplayName = "130-0 fix3f pin: the invoice source-document read selects the Access Permission")]
    public void TheInvoiceSourceDocumentRead_SelectsTheAccessPermission()
    {
        ExternalInvoiceVisibility.SourceSelectColumns.Should().Contain(ExternalDocumentNarrowing.SelectColumns);
    }

    [Fact(DisplayName = "130-0 fix3f pin: the narrowing's own by-id read (parent hops, assistant tools) selects the Access Permission")]
    public async Task TheByIdRead_SelectsTheAccessPermission()
    {
        var asked = new List<string>();

        await ExternalDocumentNarrowing.ReadAndDecideAsync(
            new[] { Guid.NewGuid() },
            (set, odata, _) =>
            {
                asked.Add(Uri.UnescapeDataString(odata));
                return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(new List<Dictionary<string, JsonElement>>());
            },
            default);

        asked.Should().ContainSingle();
        SelectOf(asked[0]).Should().Contain(ExternalDocumentNarrowing.SelectColumns);
    }

    [Fact(DisplayName = "130-0 fix3f pin: every documents module that narrows projects the Access Permission (the SDK omits an unset attribute)")]
    public void EveryNarrowingModule_ProjectsTheAccessPermission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        var registry = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();

        var modules = registry.Modules.Where(m => m.NarrowsDocuments).ToList();
        modules.Should().NotBeEmpty();
        foreach (var module in modules)
            module.NarrowingAttributes.Should().Contain(ExternalDocumentNarrowing.AccessPermissionField, $"module {module.Name}");
    }

    [Fact(DisplayName = "130-0 fix3f pin: the reconciler's document queries (ChildAccessPermissionReconciler.ColumnSetAsync) select the Access Permission when the table has it")]
    public async Task TheReconcilerColumnSet_SelectsTheAccessPermission()
    {
        async Task<IReadOnlyCollection<string>> ColumnsAsync(bool tableHasColumn)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_documentid", AccessInheritance.Column };
            if (tableHasColumn)
                present.Add(InheritedAccessPermission.Column);
            var reconciler = new ChildAccessPermissionReconciler(
                new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
                (_, _) => Task.FromResult<IReadOnlySet<string>>(present),
                NullLogger.Instance,
                _ => Task.FromResult(true));
            var method = typeof(ChildAccessPermissionReconciler).GetMethod("ColumnSetAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var columnSet = await (Task<ColumnSet>)method.Invoke(reconciler, new object[] { "sprk_document", CancellationToken.None })!;
            return columnSet.Columns;
        }

        (await ColumnsAsync(tableHasColumn: true)).Should().Contain(ExternalDocumentNarrowing.AccessPermissionField);
        (await ColumnsAsync(tableHasColumn: false)).Should().NotContain(ExternalDocumentNarrowing.AccessPermissionField, "control: the projection follows the table's metadata");
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
