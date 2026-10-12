using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 039, fix round: a row filed to SEVERAL core roots (project, matter, work
/// assignment) is listed only if the caller holds Read on EVERY root it carries (<see cref="ExternalMultiRootRule"/>, an
/// INTERIM decision pending the owner), on the to-do and document lists as on the event list.
/// </summary>
/// <remarks>
/// Driven through the REAL routes and the REAL <see cref="ExternalDataService"/> over
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/>, which projects every response through the request's
/// <c>$select</c>: a query that drops a lookup the rule needs reads that root as "not filed" and a withholding test fails.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalMultiRootListReadTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid RecordA = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0390000-0000-4000-8000-00000000000b");

    /// <summary>A secure record the caller holds nothing on.</summary>
    private static readonly Guid Hidden = Guid.Parse("a0390000-0000-4000-8000-0000000000dd");

    private readonly ExternalRootEventReadTests.Fixture _fixture;

    public ExternalMultiRootListReadTests(ExternalRootEventReadTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string> Roots => new() { "projects", "matters", "workassignments" };

    // =====================================================================
    // To-dos (F1): the leak. A to-do under an invoice of secure matter M and project P is stamped with both.
    // =====================================================================

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Todos_AlsoFiledToARootTheCallerCannotRead_AreWithheld_OnAllThreeRoutes(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var plain = _fixture.Data.AddTodo(segment, RecordA, "Single root");
        if (segment != "projects") _fixture.Data.AddTodo(segment, RecordA, "Also in a hidden project", project: Hidden);
        if (segment != "matters") _fixture.Data.AddTodo(segment, RecordA, "Also in a hidden matter", matter: Hidden);
        if (segment != "workassignments") _fixture.Data.AddTodo(segment, RecordA, "Also in a hidden work assignment", workAssignment: Hidden);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalTodoDto>>($"/api/v1/external/{segment}/{RecordA}/todos");

        body!.Value.Select(t => t.SprkTodoid).Should().Equal(plain);
        body.Truncated.Should().BeFalse("a withheld to-do is not a truncated list");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Todos_AlsoFiledToARootTheCallerCanRead_AreListed(string segment)
    {
        // The caller reads the route root AND the second root (a matter, or a project for a matter route).
        var second = segment == "matters" ? "projects" : "matters";
        _fixture.Principal = PrincipalHolding((segment, RecordA), (second, RecordB));
        var dual = _fixture.Data.AddTodo(
            segment, RecordA, "Dual",
            project: second == "projects" ? RecordB : null,
            matter: second == "matters" ? RecordB : null);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalTodoDto>>($"/api/v1/external/{segment}/{RecordA}/todos");

        body!.Value.Select(t => t.SprkTodoid).Should().Equal(dual);
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Todos_FiledToOneRoot_ListExactlyAsBefore(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var a = _fixture.Data.AddTodo(segment, RecordA, "One");
        var b = _fixture.Data.AddTodo(segment, RecordA, "Two");
        _fixture.Data.AddTodo(segment, RecordB, "Another record's");

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalTodoDto>>($"/api/v1/external/{segment}/{RecordA}/todos");

        body!.Value.Select(t => t.SprkTodoid).Should().BeEquivalentTo(new[] { a, b });
        _fixture.Data.Urls.Should().ContainSingle().Which.Should().Contain("sprk_todos?");
    }

    [Fact]
    public async Task Todos_FiledToAMalformedLookup_AreWithheld_FailClosed()
    {
        _fixture.Principal = Principal("projects", RecordA);
        _fixture.Data.AddTodo("projects", RecordA, "Fine");
        _fixture.Data.AddTodo("projects", RecordA, "Garbled", rawMatterLookup: "not-a-guid");

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalTodoDto>>($"/api/v1/external/projects/{RecordA}/todos");

        body!.Value.Select(t => t.SprkName).Should().Equal("Fine");
    }

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.Project)]
    [InlineData(ExternalDataService.TodoRootKind.Matter)]
    [InlineData(ExternalDataService.TodoRootKind.WorkAssignment)]
    public void BuildTodoListUrl_SelectsEveryRootLookup_TheRuleNeeds(ExternalDataService.TodoRootKind kind)
    {
        var select = Uri.UnescapeDataString(ExternalDataService.BuildTodoListUrl("https://org.test/api/data/v9.2", kind, RecordA));

        select.Should().Contain("_sprk_regardingproject_value").And.Contain("_sprk_regardingmatter_value")
            .And.Contain("_sprk_regardingworkassignment_value");
    }

    // =====================================================================
    // Documents (F2 audit): a document carries typed links to a project, a matter and a work assignment (+ related twins)
    // =====================================================================

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Documents_AlsoFiledToARootTheCallerCannotRead_AreWithheld_OnAllThreeRoutes(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var plain = _fixture.Data.AddDocument(segment, RecordA, "Single root");
        if (segment != "projects") _fixture.Data.AddDocument(segment, RecordA, "Typed link to a hidden project", OtherThan("projects", Hidden));
        if (segment != "matters") _fixture.Data.AddDocument(segment, RecordA, "Typed link to a hidden matter", OtherThan("matters", Hidden));
        if (segment != "workassignments") _fixture.Data.AddDocument(segment, RecordA, "Typed link to a hidden work assignment", OtherThan("workassignments", Hidden));
        _fixture.Data.AddDocument(segment, RecordA, "Related-twin link to a hidden matter", ("_sprk_relatedmatter_value", Hidden));
        _fixture.Data.AddDocument(segment, RecordA, "Related-twin link to a hidden project", ("_sprk_relatedproject_value", Hidden));
        _fixture.Data.AddDocument(segment, RecordA, "Related-twin link to a hidden work assignment", ("_sprk_relatedworkassignment_value", Hidden));

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/{segment}/{RecordA}/documents");

        body!.Value.Select(d => d.SprkDocumentid).Should().Equal(plain);
        body.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Documents_AlsoFiledToARootTheCallerCanRead_AreListed()
    {
        _fixture.Principal = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });
        var dual = _fixture.Data.AddDocument("projects", RecordA, "Project and readable matter", ("_sprk_matter_value", RecordB));

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/projects/{RecordA}/documents");

        body!.Value.Select(d => d.SprkDocumentid).Should().Equal(dual);
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Documents_FiledToOneRoot_ListExactlyAsBefore(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var a = _fixture.Data.AddDocument(segment, RecordA, "One");
        var b = _fixture.Data.AddDocument(segment, RecordA, "Two");

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/{segment}/{RecordA}/documents");

        body!.Value.Select(d => d.SprkDocumentid).Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void BuildDocumentListUrl_SelectsEveryRootLink_OfTheCanonicalDocumentLinkVocabulary()
    {
        // The rule can only withhold on a column the query read. DocumentLinkFields is the one declaration of the document's
        // links; every project / matter / work assignment link in it (typed + related twin) must be in the select.
        var url = Uri.UnescapeDataString(ExternalDataService.BuildDocumentListUrl(
            "https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, RecordA));

        foreach (var field in DocumentLinkFields.All.Where(f =>
                     f.TargetEntityLogicalName is "sprk_project" or "sprk_matter" or "sprk_workassignment"))
        {
            url.Should().Contain($"_{field.LogicalName}_value", $"{field.LogicalName} is a root link of a document");
        }
    }

    // =====================================================================
    // The module grid's documents module (GET /data/savedquery/...): ScopeRows
    // =====================================================================

    [Fact]
    public void DocumentsModule_ScopeRows_WithholdsADocumentAlsoFiledToARootTheCallerCannotRead()
    {
        var module = Registry().FindByEntity("sprk_document")!;
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });

        var kept = module.ScopeRows(caller, new[]
        {
            DocRow("Project only", project: RecordA),
            DocRow("Project and hidden matter", project: RecordA, matter: Hidden),
            DocRow("Project and hidden work assignment", project: RecordA, workAssignment: Hidden),
        });

        kept.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
    }

    [Fact]
    public void DocumentsModule_ScopeRows_ListsADocumentFiledToTwoRootsTheCallerCanRead()
    {
        var module = Registry().FindByEntity("sprk_document")!;
        var caller = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });

        module.ScopeRows(caller, new[] { DocRow("Both readable", project: RecordA, matter: RecordB) }).Should().HaveCount(1);
    }

    [Fact]
    public void InvoicesModule_ScopeRows_StaysTheUnionOfItsParents()
    {
        // Owner decision 2026-10-11: an invoice is reached through ANY accessible parent. Not changed.
        var module = Registry().FindByEntity("sprk_invoice")!;
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        var row = new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_invoice", ["sprk_project"] = RecordA, ["sprk_matter"] = Hidden,
        };

        module.ScopeRows(caller, new[] { row }).Should().HaveCount(1);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static IReadOnlyDictionary<string, object?> DocRow(
        string name, Guid? project = null, Guid? matter = null, Guid? workAssignment = null) => new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_document",
            ["sprk_documentname"] = name,
            ["sprk_project"] = project,
            ["sprk_matter"] = matter,
            ["sprk_workassignment"] = workAssignment,
        };

    private static ExternalModuleRegistry Registry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
    }

    /// <summary>The typed link column of <paramref name="other"/>'s root, set to <paramref name="value"/>.</summary>
    private static (string Column, Guid Value)[] OtherThan(string other, Guid value) =>
        new[] { (other switch
        {
            "projects" => "_sprk_project_value",
            "matters" => "_sprk_matter_value",
            _ => "_sprk_workassignment_value",
        }, value) };

    /// <summary>A caller holding Read on each (route segment, record) pair.</summary>
    private static CallerPrincipal PrincipalHolding(params (string Segment, Guid Record)[] held) => PrincipalWith(
        projects: held.Where(h => h.Segment == "projects")
            .Select(h => CallerProjectAccess.FromLevel(h.Record, ExternalAccessLevel.ViewOnly)).ToList(),
        matters: held.Where(h => h.Segment == "matters").ToDictionary(h => h.Record, _ => AccessRights.Read),
        workAssignments: held.Where(h => h.Segment == "workassignments").ToDictionary(h => h.Record, _ => AccessRights.Read));

    /// <summary>A caller holding Read on <paramref name="record"/> of the route's type, and nothing else.</summary>
    private static CallerPrincipal Principal(string segment, Guid record) => PrincipalWith(
        projects: segment == "projects" ? new[] { CallerProjectAccess.FromLevel(record, ExternalAccessLevel.Collaborate) } : null,
        matters: segment == "matters" ? new Dictionary<Guid, AccessRights> { [record] = AccessRights.Read } : null,
        workAssignments: segment == "workassignments" ? new Dictionary<Guid, AccessRights> { [record] = AccessRights.Read } : null);

    private static CallerPrincipal PrincipalWith(
        IReadOnlyList<CallerProjectAccess>? projects = null,
        IReadOnlyDictionary<Guid, AccessRights>? matters = null,
        IReadOnlyDictionary<Guid, AccessRights>? workAssignments = null) => new()
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = projects ?? Array.Empty<CallerProjectAccess>(),
            MatterAccess = matters ?? new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = workAssignments ?? new Dictionary<Guid, AccessRights>(),
        };
}
