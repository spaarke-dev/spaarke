using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;
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
    public async Task Todos_AlsoFiledToASecureRootTheCallerCannotRead_AreWithheld_OnAllThreeRoutes(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        SeedHiddenAsSecure();
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
    public async Task Documents_AlsoFiledToASecureRootTheCallerCannotRead_AreWithheld_OnAllThreeRoutes(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        SeedHiddenAsSecure();
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
    // Owner decision 2026-10-12, option B: only a SECURE linked root the caller cannot read withholds a list row.
    // Events, to-dos and documents, on the three root routes, through the real secure-flag read over FakeDataverse.
    // =====================================================================

    public static TheoryData<string, string> KindsAndRoots
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var kind in new[] { "events", "todos", "documents" })
                foreach (var segment in new[] { "projects", "matters", "workassignments" })
                    data.Add(kind, segment);
            return data;
        }
    }

    public static TheoryData<string> Kinds => new() { "events", "todos", "documents" };

    [Theory]
    [MemberData(nameof(KindsAndRoots))]
    public async Task OptionB_ARowAlsoFiledToASecureRootTheCallerCannotRead_IsWithheld(string kind, string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var other = OtherSegment(segment);
        _fixture.Data.AddSecureRoot(RootOf(other), Hidden);
        var plain = AddRow(kind, segment, RecordA, "Single root");
        AddRow(kind, segment, RecordA, "Also in a secure record", other, Hidden);

        (await ListIds(kind, segment)).Should().Equal(plain);
    }

    [Theory]
    [MemberData(nameof(KindsAndRoots))]
    public async Task OptionB_ARowAlsoFiledToANonSecureRootTheCallerCannotRead_IsListed(string kind, string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var other = OtherSegment(segment);
        _fixture.Data.AddOpenRoot(RootOf(other), Hidden);
        var plain = AddRow(kind, segment, RecordA, "Single root");
        var dual = AddRow(kind, segment, RecordA, "Also in a non-secure record", other, Hidden);

        (await ListIds(kind, segment)).Should().BeEquivalentTo(new[] { plain, dual });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_ARowAlsoFiledToASecureRootTheCallerCanRead_IsListed_WithoutAFlagRead(string kind)
    {
        _fixture.Principal = PrincipalHolding(("projects", RecordA), ("matters", RecordB));
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, RecordB);
        var dual = AddRow(kind, "projects", RecordA, "Project and readable secure matter", "matters", RecordB);

        (await ListIds(kind, "projects")).Should().Equal(dual);
        _fixture.Data.FlagReads.Should().BeEmpty("a root the caller can read never needs its secure flag");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_ARowFiledToOneRoot_CausesNoFlagRead(string kind)
    {
        _fixture.Principal = Principal("projects", RecordA);
        var plain = AddRow(kind, "projects", RecordA, "Single root");

        (await ListIds(kind, "projects")).Should().Equal(plain);
        _fixture.Data.FlagReads.Should().BeEmpty();
    }

    public static TheoryData<string, string> KindsAndFailures
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var kind in new[] { "events", "todos", "documents" })
                foreach (var failure in new[] { "throws", "fault", "missing", "flag-null", "malformed" })
                    data.Add(kind, failure);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KindsAndFailures))]
    public async Task OptionB_TheOtherRootsSecureStateCannotBeRead_TheRowIsWithheld_FailClosed(string kind, string failure)
    {
        _fixture.Principal = Principal("projects", RecordA);
        switch (failure)
        {
            case "throws": _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden); _fixture.Data.FlagReadThrows = true; break;
            case "fault": _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden); _fixture.Data.FlagFault = HttpStatusCode.InternalServerError; break;
            case "flag-null": _fixture.Data.AddRoot(ExternalGrantRootType.Matter, Hidden, isSecure: null); break;
            // "missing": the matter record does not exist; "malformed": the lookup is not a GUID.
        }

        var plain = AddRow(kind, "projects", RecordA, "Single root");
        if (failure == "malformed")
            AddMalformedMatterRow(kind, "projects", RecordA, "Garbled");
        else
            AddRow(kind, "projects", RecordA, "Unknown secure state", "matters", Hidden);

        (await ListIds(kind, "projects")).Should().Equal(plain);
    }

    // ---- Review of 039 option B, K2: an INCOMPLETE flag read must not silently shrink a list ----

    [Theory]
    [InlineData("events", "throws")]
    [InlineData("events", "fault")]
    [InlineData("todos", "throws")]
    [InlineData("todos", "fault")]
    [InlineData("documents", "throws")]
    [InlineData("documents", "fault")]
    public async Task OptionB_AnIncompleteFlagRead_ThatWithholdsARow_SetsTruncated(string kind, string failure)
    {
        _fixture.Principal = Principal("projects", RecordA);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden);
        if (failure == "throws") _fixture.Data.FlagReadThrows = true; else _fixture.Data.FlagFault = HttpStatusCode.InternalServerError;
        var plain = AddRow(kind, "projects", RecordA, "Single root");
        AddRow(kind, "projects", RecordA, "Withheld only because its matter could not be read", "matters", Hidden);

        var (ids, truncated) = await ListIdsAndTruncated(kind, "projects");

        ids.Should().Equal(plain);
        truncated.Should().BeTrue("the row was withheld only because the secure-flag read failed, so the list may be short");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_AnUnresolvedRootInASucceededFlagRead_ThatWithholdsARow_SetsTruncated(string kind)
    {
        // F4: the chunk succeeds, but the linked matter has no row (missing, or invisible to the app identity).
        _fixture.Principal = Principal("projects", RecordA);
        var plain = AddRow(kind, "projects", RecordA, "Single root");
        AddRow(kind, "projects", RecordA, "Withheld because its matter did not resolve", "matters", Hidden);

        var (ids, truncated) = await ListIdsAndTruncated(kind, "projects");

        ids.Should().Equal(plain);
        truncated.Should().BeTrue("the row was withheld on an unresolved root, so the list may be short");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_ARootWithANonBooleanFlag_ThatWithholdsARow_SetsTruncated(string kind)
    {
        _fixture.Principal = Principal("projects", RecordA);
        _fixture.Data.AddRootWithRawFlag(ExternalGrantRootType.Matter, Hidden, "no");
        var plain = AddRow(kind, "projects", RecordA, "Single root");
        AddRow(kind, "projects", RecordA, "Withheld because its flag is not a boolean", "matters", Hidden);

        var (ids, truncated) = await ListIdsAndTruncated(kind, "projects");

        ids.Should().Equal(plain);
        truncated.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_AResolvedNonSecureRoot_ListsTheRow_WithoutTruncated(string kind)
    {
        _fixture.Principal = Principal("projects", RecordA);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden);
        var plain = AddRow(kind, "projects", RecordA, "Single root");
        var shown = AddRow(kind, "projects", RecordA, "Its matter resolved as non-secure", "matters", Hidden);

        var (ids, truncated) = await ListIdsAndTruncated(kind, "projects");

        ids.Should().BeEquivalentTo(new[] { plain, shown });
        truncated.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_ACompleteFlagRead_WithASecureRowWithheld_DoesNotSetTruncated(string kind)
    {
        _fixture.Principal = Principal("projects", RecordA);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, Hidden);
        var plain = AddRow(kind, "projects", RecordA, "Single root");
        AddRow(kind, "projects", RecordA, "Genuinely secure", "matters", Hidden);

        var (ids, truncated) = await ListIdsAndTruncated(kind, "projects");

        ids.Should().Equal(plain);
        truncated.Should().BeFalse("a complete read that withheld a secure row is not a truncated list");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_AFaultedFlagRead_ThatWithholdsNothing_DoesNotSetTruncated(string kind)
    {
        // A fault only matters when it costs a row: a list with no linked unreadable root never reads a flag.
        _fixture.Principal = Principal("projects", RecordA);
        _fixture.Data.FlagFault = HttpStatusCode.InternalServerError;
        var plain = AddRow(kind, "projects", RecordA, "Single root");

        var (ids, truncated) = await ListIdsAndTruncated(kind, "projects");

        ids.Should().Equal(plain);
        truncated.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task OptionB_ManyRowsLinkedToTheSameUnreadableRoots_CostOneFlagQueryPerRootType(string kind)
    {
        _fixture.Principal = Principal("projects", RecordA);
        var secureMatter = Guid.Parse("a0390000-0000-4000-8000-0000000000ee");
        var openWorkAssignment = Guid.Parse("a0390000-0000-4000-8000-0000000000ef");
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, secureMatter);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, openWorkAssignment);
        var listed = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var id = AddRow(kind, "projects", RecordA, $"Row {i}", "matters", i % 3 == 0 ? secureMatter : Hidden);
            if (i % 3 != 0) listed.Add(id);
        }

        listed.Add(AddRow(kind, "projects", RecordA, "Work assignment", "workassignments", openWorkAssignment));

        (await ListIds(kind, "projects")).Should().BeEquivalentTo(listed);
        var flagReads = _fixture.Data.FlagReads;
        flagReads.Should().HaveCount(2, "one query per root entity for the whole list, never one per row");
        flagReads.Should().ContainSingle(u => u.Contains("/sprk_matters?")).Which.Should()
            .Contain(Hidden.ToString()).And.Contain(secureMatter.ToString()).And.Contain("$select=sprk_matterid,sprk_issecure");
        flagReads.Should().ContainSingle(u => u.Contains("/sprk_workassignments?"));
    }

    // =====================================================================
    // Main-session decision 2026-10-12 (stricter reading of option B): "secure" means EFFECTIVELY secure (UAC-r2 task 174).
    // An unreadable work assignment W whose OWN flag is false but which is filed under a secure matter M is secure, even when
    // the row carries no stamp for M. The own flag comes over the Web API; the filing chain over the fake filing reader.
    // =====================================================================

    private static readonly Guid FiledWorkAssignment = Guid.Parse("a0390000-0000-4000-8000-0000000000e1");
    private static readonly Guid FilingMatter = Guid.Parse("a0390000-0000-4000-8000-0000000000e2");

    /// <summary>W (own flag FALSE) filed under matter M, which is <paramref name="matterSecure"/>. The caller reads neither.</summary>
    private void SeedWorkAssignmentUnderMatter(bool matterSecure)
    {
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, FiledWorkAssignment);
        _fixture.Data.AddRoot(ExternalGrantRootType.Matter, FilingMatter, matterSecure);
        _fixture.Data.FileWorkAssignmentUnder(FiledWorkAssignment, ExternalGrantRootType.Matter, FilingMatter);
    }

    public static TheoryData<string, string> KindsAndProjectOrMatterRoutes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var kind in new[] { "events", "todos", "documents" })
                foreach (var segment in new[] { "projects", "matters" })
                    data.Add(kind, segment);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KindsAndProjectOrMatterRoutes))]
    public async Task Effective_ARowFiledToAWorkAssignmentWithAFalseOwnFlag_UnderASecureMatter_IsWithheld_EvenWithNoStampForTheMatter(
        string kind, string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        SeedWorkAssignmentUnderMatter(matterSecure: true);
        var plain = AddRow(kind, segment, RecordA, "Single root");
        AddRow(kind, segment, RecordA, "Filed to W only; W is under a secure matter", "workassignments", FiledWorkAssignment);

        (await ListIds(kind, segment)).Should().Equal(plain);
    }

    [Theory]
    [MemberData(nameof(KindsAndProjectOrMatterRoutes))]
    public async Task Effective_ARowFiledToAWorkAssignmentWithAFalseOwnFlag_UnderANonSecureMatter_IsListed(string kind, string segment)
    {
        _fixture.Principal = Principal(segment, RecordA);
        SeedWorkAssignmentUnderMatter(matterSecure: false);
        var plain = AddRow(kind, segment, RecordA, "Single root");
        var dual = AddRow(kind, segment, RecordA, "Filed to W only; W is under a non-secure matter", "workassignments", FiledWorkAssignment);

        (await ListIds(kind, segment)).Should().BeEquivalentTo(new[] { plain, dual });
    }

    public static TheoryData<string, string> KindsAndAncestryFailures
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var kind in new[] { "events", "todos", "documents" })
                foreach (var failure in new[] { "walk-throws", "ancestor-read-throws", "ancestor-flag-empty" })
                    data.Add(kind, failure);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KindsAndAncestryFailures))]
    public async Task Effective_WhenTheFilingChainCannotBeRead_TheRowIsWithheld_FailClosed(string kind, string failure)
    {
        _fixture.Principal = Principal("projects", RecordA);
        SeedWorkAssignmentUnderMatter(matterSecure: false);
        switch (failure)
        {
            case "walk-throws": _fixture.Data.FilingReadThrows = true; break;
            case "ancestor-read-throws": _fixture.Data.FilingFaultEntity = "sprk_matter"; break;
            case "ancestor-flag-empty": _fixture.Data.AddRoot(ExternalGrantRootType.Matter, FilingMatter, isSecure: null); break;
        }

        var plain = AddRow(kind, "projects", RecordA, "Single root");
        AddRow(kind, "projects", RecordA, "Filed to W; its ancestry cannot be decided", "workassignments", FiledWorkAssignment);

        (await ListIds(kind, "projects")).Should().Equal(plain);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Effective_ManyRowsLinkedToTheSameWorkAssignment_CostABoundedNumberOfReads(string kind)
    {
        _fixture.Principal = Principal("projects", RecordA);
        SeedWorkAssignmentUnderMatter(matterSecure: false);
        var secondWorkAssignment = Guid.Parse("a0390000-0000-4000-8000-0000000000e3");
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.WorkAssignment, secondWorkAssignment);
        _fixture.Data.FileWorkAssignmentUnder(secondWorkAssignment, ExternalGrantRootType.Matter, FilingMatter);
        var listed = new List<string>();
        for (var i = 0; i < 12; i++)
            listed.Add(AddRow(kind, "projects", RecordA, $"Row {i}", "workassignments", i % 4 == 0 ? secondWorkAssignment : FiledWorkAssignment));

        (await ListIds(kind, "projects")).Should().BeEquivalentTo(listed);
        _fixture.Data.FlagReads.Should().ContainSingle("one own-flag query for the two work assignments, never one per row");
        _fixture.Data.FilingReadLog.Should().Equal(
            new[] { "sprk_workassignment", "sprk_matter" }, "one batched read of the two work assignments, one of their matter");
    }

    [Fact]
    public async Task Effective_DocumentsModuleFetch_AWorkAssignmentWithAFalseOwnFlag_UnderASecureMatter_IsWithheld()
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        SeedWorkAssignmentUnderMatter(matterSecure: true);

        var (body, _) = await RunDocumentsFetchAsync(
            caller, ProjectOnlyGridFetch,
            new[]
            {
                DocRow("Project only", project: RecordA),
                DocRow("W under a secure matter", project: RecordA, workAssignment: FiledWorkAssignment),
                DocRow("W under a secure matter, through the twin", project: RecordA, extra: ("sprk_relatedworkassignment", FiledWorkAssignment)),
            });

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
    }

    [Fact]
    public async Task Effective_DocumentsModuleFetch_AWorkAssignmentWithAFalseOwnFlag_UnderANonSecureMatter_IsListed()
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        SeedWorkAssignmentUnderMatter(matterSecure: false);

        var (body, _) = await RunDocumentsFetchAsync(
            caller, ProjectOnlyGridFetch,
            new[] { DocRow("Project only", project: RecordA), DocRow("W under a non-secure matter", project: RecordA, workAssignment: FiledWorkAssignment) });

        body.Entities.Select(r => r["sprk_documentname"]).Should().BeEquivalentTo(new[] { "Project only", "W under a non-secure matter" });
    }

    [Fact]
    public async Task Effective_DocumentsModuleFetch_WhenTheFilingChainCannotBeRead_TheRowIsWithheld_FailClosed()
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        SeedWorkAssignmentUnderMatter(matterSecure: false);
        _fixture.Data.FilingFaultEntity = "sprk_matter";

        var (body, _) = await RunDocumentsFetchAsync(
            caller, ProjectOnlyGridFetch,
            new[] { DocRow("Project only", project: RecordA), DocRow("W with an unreadable ancestry", project: RecordA, workAssignment: FiledWorkAssignment) });

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
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

    // ---- K3 (fix round 2): a root reached only through a RELATED TWIN of a typed link ----

    [Theory]
    [InlineData("sprk_relatedproject")]
    [InlineData("sprk_relatedmatter")]
    [InlineData("sprk_relatedworkassignment")]
    public void DocumentsModule_ScopeRows_WithholdsADocumentLinkedToAHiddenRootOnlyThroughARelatedTwin(string twin)
    {
        var module = Registry().FindByEntity("sprk_document")!;
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });

        var kept = module.ScopeRows(caller, new[]
        {
            DocRow("Project only", project: RecordA),
            DocRow("Project and a hidden twin", project: RecordA, extra: (twin, (object)Hidden)),
            DocRow("Project and a garbled twin", project: RecordA, extra: (twin, "not-a-guid")),
        });

        kept.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
    }

    [Fact]
    public void DocumentsModule_ScopeRows_ListsADocumentWhoseTwinIsARootTheCallerCanRead()
    {
        var module = Registry().FindByEntity("sprk_document")!;
        var caller = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });

        module.ScopeRows(caller, new[] { DocRow("Twin readable", project: RecordA, extra: ("sprk_relatedmatter", RecordB)) })
            .Should().HaveCount(1);
    }

    [Fact]
    public void DocumentsModule_DeclaresTheThreeRelatedTwins_AsRootLookups()
    {
        Registry().FindByEntity("sprk_document")!.AdditionalRootLookups.Should().BeEquivalentTo(new[]
        {
            new RootLookup("sprk_relatedproject", ExternalGrantRootType.Project),
            new RootLookup("sprk_relatedmatter", ExternalGrantRootType.Matter),
            new RootLookup("sprk_relatedworkassignment", ExternalGrantRootType.WorkAssignment),
        });
        foreach (var other in new[] { "sprk_invoice", "sprk_project", "sprk_matter", "sprk_workassignment" })
            Registry().FindByEntity(other)!.AdditionalRootLookups.Should().BeEmpty($"{other} keeps its rule");
    }

    [Fact]
    public async Task DocumentsModuleFetch_ProjectsTheTwinsServerSide_WithholdsOnThem_AndNeverReturnsThem()
    {
        // The REAL fetch pipeline over the production registry. The grid's FetchXML names only the typed columns; the server
        // adds the twins AFTER the column guard, withholds on them, and the response strip removes them.
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        const string gridFetch = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/>"
            + "<attribute name='sprk_documentname'/><attribute name='sprk_project'/><attribute name='sprk_matter'/>"
            + "<attribute name='sprk_workassignment'/><order attribute='sprk_documentname'/></entity></fetch>";
        string? executed = null;
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            DocRow("Project only", project: RecordA),
            DocRow("Project and a hidden twin", project: RecordA, extra: ("sprk_relatedmatter", Hidden)),
        };

        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_document", gridFetch, PagingCookie: null),
            caller, Registry(), new FetchXmlEntityExtractor(),
            (request, _) =>
            {
                executed = request.FetchXml;
                return Task.FromResult(new FetchResponseDto(rows, MoreRecords: false, PagingCookie: null));
            },
            _fixture.Data.NewService().GetNonSecureRootsAsync, _fixture.Data.NewService().NarrowDocumentsAsync,
            NullLogger.Instance, CancellationToken.None);

        executed.Should().Contain("name=\"sprk_relatedproject\"").And.Contain("name=\"sprk_relatedmatter\"")
            .And.Contain("name=\"sprk_relatedworkassignment\"");
        var body = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!;
        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
        body.Entities.Single().Keys.Should().NotContain(k => k.StartsWith("sprk_related", StringComparison.Ordinal));
    }

    // ---- F1 (fix round 3): a grid whose FetchXML omits a typed root column must not fail OPEN ----

    private const string ProjectOnlyGridFetch =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>"
        + "<attribute name='sprk_project'/></entity></fetch>";

    [Fact]
    public async Task DocumentsModuleFetch_NamingOnlySprkProject_StillWithholdsARowFiledToAHiddenRoot_AndNeverReturnsTheUnrequestedRoots()
    {
        // Dataverse omits a null attribute, and a column the query did not read is omitted too. The executeFetch double
        // therefore projects every row through the EXECUTED FetchXML's attribute list (as FakeDataverse does for $select):
        // a pipeline that never asked for sprk_matter / sprk_workassignment / the twins would see the P+M row as project-only.
        var caller = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        SeedHiddenAsSecure();
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            DocRow("Project only", project: RecordA),
            DocRow("Project and hidden matter", project: RecordA, matter: Hidden),
            DocRow("Project and hidden work assignment", project: RecordA, workAssignment: Hidden),
            DocRow("Project and hidden twin", project: RecordA, extra: ("sprk_relatedproject", Hidden)),
        };

        var (body, executed) = await RunDocumentsFetchAsync(caller, ProjectOnlyGridFetch, rows);

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
        // The server asked for every attribute the rule reads...
        foreach (var attribute in new[]
                 {
                     "sprk_matter", "sprk_workassignment", "sprk_relatedproject", "sprk_relatedmatter", "sprk_relatedworkassignment",
                 })
        {
            executed.Should().Contain($"name=\"{attribute}\"");
        }
        // ...and gave back none the caller did not.
        body.Entities.Single().Keys.Should().BeEquivalentTo(new[] { "@logicalName", "sprk_documentname", "sprk_project", "hasFile" }); // hasFile: R3 ISS-011, derived by the server
    }

    [Fact]
    public async Task DocumentsModuleFetch_NamingOnlySprkProject_ListsARowFiledToTwoReadableRoots_WithoutTheUnrequestedRootColumn()
    {
        var caller = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });
        var dual = (Dictionary<string, object?>)DocRow("Project and readable matter", project: RecordA, matter: RecordB);
        dual["@formattedValues"] = new Dictionary<string, string> { ["sprk_matter"] = "Matter B", ["sprk_project"] = "Project A" };

        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, RecordB);

        var (body, _) = await RunDocumentsFetchAsync(caller, ProjectOnlyGridFetch, new[] { dual });

        var row = body.Entities.Should().ContainSingle().Subject;
        row.Keys.Should().NotContain("sprk_matter");
        ((IReadOnlyDictionary<string, string>)row["@formattedValues"]!).Keys.Should().Equal("sprk_project");
        _fixture.Data.FlagReads.Should().BeEmpty("a secure root the caller can read needs no flag read");
    }

    // ---- Owner decision 2026-10-12, option B: the grid's documents module ----

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    [InlineData("sprk_relatedproject")]
    [InlineData("sprk_relatedmatter")]
    public async Task DocumentsModuleFetch_ARowAlsoFiledToANonSecureRootTheCallerCannotRead_IsListed_OptionB(string column)
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        foreach (var root in new[] { ExternalGrantRootType.Project, ExternalGrantRootType.Matter, ExternalGrantRootType.WorkAssignment })
            _fixture.Data.AddOpenRoot(root, Hidden);

        var (body, _) = await RunDocumentsFetchAsync(
            caller, ProjectOnlyGridFetch,
            new[] { DocRow("Project only", project: RecordA), DocRow("Also non-secure", project: RecordA, extra: (column, Hidden)) });

        body.Entities.Select(r => r["sprk_documentname"]).Should().BeEquivalentTo(new[] { "Project only", "Also non-secure" });
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_relatedmatter")]
    public async Task DocumentsModuleFetch_ARowAlsoFiledToASecureRootTheCallerCannotRead_IsWithheld_OptionB(string column)
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, Hidden);

        var (body, _) = await RunDocumentsFetchAsync(
            caller, ProjectOnlyGridFetch,
            new[] { DocRow("Project only", project: RecordA), DocRow("Also secure", project: RecordA, extra: (column, Hidden)) });

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
    }

    [Theory]
    [InlineData("throws")]
    [InlineData("fault")]
    [InlineData("missing")]
    [InlineData("flag-null")]
    [InlineData("malformed")]
    public async Task DocumentsModuleFetch_TheOtherRootsSecureStateCannotBeRead_TheRowIsWithheld_FailClosed(string failure)
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        switch (failure)
        {
            case "throws": _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden); _fixture.Data.FlagReadThrows = true; break;
            case "fault": _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden); _fixture.Data.FlagFault = HttpStatusCode.InternalServerError; break;
            case "flag-null": _fixture.Data.AddRoot(ExternalGrantRootType.Matter, Hidden, isSecure: null); break;
        }

        var (body, _) = await RunDocumentsFetchAsync(
            caller, ProjectOnlyGridFetch,
            new[]
            {
                DocRow("Project only", project: RecordA),
                failure == "malformed"
                    ? DocRow("Garbled twin", project: RecordA, extra: ("sprk_relatedmatter", "not-a-guid"))
                    : DocRow("Unknown secure state", project: RecordA, matter: Hidden),
            });

        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal("Project only");
    }

    [Fact]
    public async Task DocumentsModuleFetch_ManyRowsLinkedToTheSameUnreadableRoot_CostOneFlagQuery()
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden);
        var rows = Enumerable.Range(1, 5)
            .Select(i => DocRow($"Doc {i}", project: RecordA, matter: i % 2 == 0 ? Hidden : null, extra: ("sprk_relatedmatter", Hidden)))
            .ToArray();

        var (body, _) = await RunDocumentsFetchAsync(caller, ProjectOnlyGridFetch, rows);

        body.Entities.Should().HaveCount(5);
        _fixture.Data.FlagReads.Should().ContainSingle().Which.Should().Contain(Hidden.ToString())
            .And.Contain("$select=sprk_matterid,sprk_issecure");
    }

    [Fact]
    public void DocumentsModule_ScopeRows_WithoutFlags_CountsEveryUnreadableRootAsSecure()
    {
        // ScopeRows is synchronous: without the pre-fetched flags it knows nothing and fails closed.
        var module = Registry().FindByEntity("sprk_document")!;
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        var rows = new[] { DocRow("Project only", project: RecordA), DocRow("Also elsewhere", project: RecordA, matter: Hidden) };

        module.ScopeRows(caller, rows).Select(r => r["sprk_documentname"]).Should().Equal("Project only");
        module.ScopeRows(caller, rows, SecureRootFlags.None).Select(r => r["sprk_documentname"]).Should().Equal("Project only");
    }

    [Fact]
    public async Task DocumentsModuleFetch_NamingEveryRootColumn_ReturnsThem_AsBefore()
    {
        const string fetch = "<fetch><entity name='sprk_document'><attribute name='sprk_documentname'/>"
            + "<attribute name='sprk_project'/><attribute name='sprk_matter'/></entity></fetch>";
        var caller = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordB] = AccessRights.Read });

        var (body, _) = await RunDocumentsFetchAsync(
            caller, fetch, new[] { DocRow("Both readable", project: RecordA, matter: RecordB) });

        body.Entities.Single().Keys.Should().BeEquivalentTo(
            new[] { "@logicalName", "sprk_documentname", "sprk_project", "sprk_matter", "hasFile" }); // hasFile: R3 ISS-011
    }

    [Fact]
    public void EveryModuleWithTheEveryRootRule_ProjectsEveryAttributeTheRuleReads()
    {
        // "Projection covers every attribute the rule reads": the scope dimensions plus the declared extra root lookups.
        // Every module that sets RequireReadOnSecureRoots is checked, so a new one cannot leave a root unprojected.
        var modules = Registry().Modules.Where(m => m.RequireReadOnSecureRoots).ToList();
        modules.Should().NotBeEmpty();
        foreach (var module in modules)
        {
            module.RootRuleAttributes.Should().BeEquivalentTo(
                module.EffectiveDimensions.Select(d => d.Attribute).Concat(module.AdditionalRootLookups.Select(l => l.Attribute))
                    .Concat(module.LiveParentAttributes));
        }

        // Task 187: plus the document's other parents, whose LIVE roots the rule takes in.
        Registry().FindByEntity("sprk_document")!.RootRuleAttributes.Should().BeEquivalentTo(new[]
        {
            "sprk_project", "sprk_matter", "sprk_workassignment",
            "sprk_relatedproject", "sprk_relatedmatter", "sprk_relatedworkassignment",
            "sprk_canonicaldocument", "sprk_invoice", "sprk_parentdocument", "sprk_relatedagreement",
            "sprk_relatedcommunication", "sprk_relatedevent", "sprk_relatedinvoice", "sprk_relatedtodo",
        });
    }

    /// <summary>
    /// Runs the real documents-module fetch pipeline over the production registry. The double executes the FetchXML it is
    /// handed the way Dataverse does: each row keeps <c>@logicalName</c> plus only the attributes named in the executed
    /// FetchXML, and a null attribute is omitted.
    /// </summary>
    private async Task<(FetchResponseDto Body, string Executed)> RunDocumentsFetchAsync(
        CallerPrincipal caller, string callerFetchXml, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var executed = string.Empty;
        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_document", callerFetchXml, PagingCookie: null),
            caller, Registry(), new FetchXmlEntityExtractor(),
            (request, _) =>
            {
                executed = request.FetchXml;
                return Task.FromResult(new FetchResponseDto(
                    ProjectThroughFetch(request.FetchXml, rows), MoreRecords: false, PagingCookie: null));
            },
            // Owner option B: the REAL secure-flag read over FakeDataverse (root-set queries through $select, nulls omitted).
            _fixture.Data.NewService().GetNonSecureRootsAsync,
            // Task 188: the REAL document narrowing over the same fake.
            _fixture.Data.NewService().NarrowDocumentsAsync,
            NullLogger.Instance, CancellationToken.None);
        return (result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!, executed);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ProjectThroughFetch(
        string fetchXml, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var names = System.Xml.Linq.XDocument.Parse(fetchXml).Descendants("attribute")
            .Select(a => (string?)a.Attribute("name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rows.Select(row =>
        {
            var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in row)
            {
                if (key == "@logicalName")
                    projected[key] = value;
                else if (key == "@formattedValues" && value is IEnumerable<KeyValuePair<string, string>> formatted)
                    projected[key] = formatted.Where(f => names.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value);
                else if (value is not null && names.Contains(key))
                    projected[key] = value;
            }

            return (IReadOnlyDictionary<string, object?>)projected;
        }).ToList();
    }

    [Fact]
    public async Task DocumentsModule_SingleRecordRead_IsRefusedForADocumentId()
    {
        // /data/record/sprk_document/{id}: the module's dimensions are PARENT ids, so a document's own id is never in them and the
        // Tier-2 gate refuses before any read. The every-root rule has no single-record path here to apply to.
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        var reads = 0;

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            "sprk_document", Guid.NewGuid(), select: null, caller, Registry(),
            (_, _, _, _) => { reads++; return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>()); },
            NullLogger.Instance, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
        reads.Should().Be(0);
    }

    // ---- Task 187, fix round 1, owner decision D2: the invoices module (/fetch) applies the live-parent rule ----
    //
    // An invoice is still reached through ANY accessible parent (owner decision 2026-10-11, option A): one also under an
    // ORDINARY root the caller cannot read is listed. One whose live roots include a SECURE root the caller cannot read (its
    // own matter or project, or the root its agreement is filed under) is withheld, as round 100's family rule asks.

    private static readonly Guid Agreement = Guid.Parse("a0390000-0000-4000-8000-0000000000a6");

    private const string InvoiceGridFetch =
        "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/>"
        + "<attribute name='sprk_project'/></entity></fetch>";

    [Fact]
    public void InvoicesModule_RequiresReadOnSecureRoots_AndReadsItsAgreement()
    {
        var module = Registry().FindByEntity("sprk_invoice")!;

        module.RequireReadOnSecureRoots.Should().BeTrue("D2: an invoice behind a secure root's wall is withheld");
        module.RootRuleAttributes.Should().BeEquivalentTo(new[] { "sprk_matter", "sprk_project", "sprk_regardingagreement" });
    }

    [Fact]
    public async Task InvoicesModuleFetch_AnInvoiceAlsoUnderASecureRootTheCallerCannotRead_IsWithheld()
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, Hidden);

        var (body, executed) = await RunInvoicesFetchAsync(caller, new[]
        {
            InvoiceRow("Project only", project: RecordA),
            InvoiceRow("Also under secure matter", project: RecordA, matter: Hidden),
        });

        body.Entities.Select(r => r["sprk_name"]).Should().Equal("Project only");
        executed.Should().Contain("name=\"sprk_matter\"").And.Contain("name=\"sprk_regardingagreement\"");
        body.Entities.Single().Keys.Should().BeEquivalentTo(new[] { "@logicalName", "sprk_name", "sprk_project" },
            "the columns the server projected for the rule are not returned");
    }

    [Fact]
    public async Task InvoicesModuleFetch_AnInvoiceAlsoUnderAnOrdinaryRootTheCallerCannotRead_IsListed_OptionA()
    {
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden);

        var (body, _) = await RunInvoicesFetchAsync(caller, new[] { InvoiceRow("Also under an ordinary matter", project: RecordA, matter: Hidden) });

        body.Entities.Select(r => r["sprk_name"]).Should().Equal("Also under an ordinary matter");
    }

    [Fact]
    public async Task InvoicesModuleFetch_AnInvoiceWhoseAgreementIsUnderSecureX_IsWithheld_AndListed_ForACallerWhoReadsX()
    {
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, Hidden);
        _fixture.Data.AddFilingRow("sprk_agreement", Agreement, ("sprk_regardingmatter", "sprk_matter", Hidden));
        var rows = new[] { InvoiceRow("Plain", project: RecordA), InvoiceRow("Agreement under secure X", project: RecordA, agreement: Agreement) };

        var (projectOnly, _) = await RunInvoicesFetchAsync(
            PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) }), rows);
        var (alsoX, _) = await RunInvoicesFetchAsync(
            PrincipalWith(
                projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) },
                matters: new Dictionary<Guid, AccessRights> { [Hidden] = AccessRights.Read }),
            rows);

        projectOnly.Entities.Select(r => r["sprk_name"]).Should().Equal("Plain");
        alsoX.Entities.Select(r => r["sprk_name"]).Should().Equal("Plain", "Agreement under secure X");
    }

    [Fact]
    public async Task InvoicesModuleFetch_AnInvoiceWhoseAgreementIsUnderAnOrdinaryRoot_IsListed()
    {
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, Hidden);
        _fixture.Data.AddFilingRow("sprk_agreement", Agreement, ("sprk_regardingmatter", "sprk_matter", Hidden));
        var caller = PrincipalWith(projects: new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.ViewOnly) });

        var (body, _) = await RunInvoicesFetchAsync(caller, new[] { InvoiceRow("Agreement under ordinary", project: RecordA, agreement: Agreement) });

        body.Entities.Select(r => r["sprk_name"]).Should().Equal("Agreement under ordinary");
    }

    private static IReadOnlyDictionary<string, object?> InvoiceRow(
        string name, Guid? project = null, Guid? matter = null, Guid? agreement = null) => new Dictionary<string, object?>
    {
        ["@logicalName"] = "sprk_invoice",
        ["sprk_name"] = name,
        ["sprk_project"] = project,
        ["sprk_matter"] = matter,
        ["sprk_regardingagreement"] = agreement is { } a ? new Microsoft.Xrm.Sdk.EntityReference("sprk_agreement", a) : null,
    };

    /// <summary>The real invoices-module fetch pipeline, with the REAL secure-flag and live-parent reads over FakeDataverse.</summary>
    private async Task<(FetchResponseDto Body, string Executed)> RunInvoicesFetchAsync(
        CallerPrincipal caller, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var executed = string.Empty;
        var data = _fixture.Data.NewService();
        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_invoice", InvoiceGridFetch, PagingCookie: null),
            caller, Registry(), new FetchXmlEntityExtractor(),
            (request, _) =>
            {
                executed = request.FetchXml;
                return Task.FromResult(new FetchResponseDto(
                    ProjectThroughFetch(request.FetchXml, rows), MoreRecords: false, PagingCookie: null));
            },
            data.GetNonSecureRootsAsync, data.NarrowDocumentsAsync, NullLogger.Instance, CancellationToken.None,
            data.GetLiveParentRootsAsync);
        return (result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!, executed);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary><see cref="Hidden"/> as a SECURE project, matter and work assignment (option B's must-fire case).</summary>
    private void SeedHiddenAsSecure()
    {
        foreach (var root in new[] { ExternalGrantRootType.Project, ExternalGrantRootType.Matter, ExternalGrantRootType.WorkAssignment })
            _fixture.Data.AddSecureRoot(root, Hidden);
    }

    private static string OtherSegment(string segment) => segment == "matters" ? "projects" : "matters";

    private static ExternalGrantRootType RootOf(string segment) => segment switch
    {
        "projects" => ExternalGrantRootType.Project,
        "matters" => ExternalGrantRootType.Matter,
        _ => ExternalGrantRootType.WorkAssignment,
    };

    /// <summary>Seeds an event, to-do or document filed to <paramref name="segment"/>'s <paramref name="record"/>, and also
    /// to <paramref name="otherSegment"/>'s <paramref name="otherRecord"/> when given (a typed link for a document).</summary>
    private string AddRow(string kind, string segment, Guid record, string name, string? otherSegment = null, Guid? otherRecord = null)
    {
        Guid? project = otherSegment == "projects" ? otherRecord : null;
        Guid? matter = otherSegment == "matters" ? otherRecord : null;
        Guid? workAssignment = otherSegment == "workassignments" ? otherRecord : null;
        return kind switch
        {
            "events" => _fixture.Data.Add(segment, record, name, project, matter, workAssignment),
            "todos" => _fixture.Data.AddTodo(segment, record, name, project, matter, workAssignment),
            _ => otherSegment is null
                ? _fixture.Data.AddDocument(segment, record, name)
                : _fixture.Data.AddDocument(segment, record, name, OtherThan(otherSegment, otherRecord!.Value)),
        };
    }

    /// <summary>A row whose matter lookup is present but not a GUID.</summary>
    private string AddMalformedMatterRow(string kind, string segment, Guid record, string name) => kind switch
    {
        "events" => _fixture.Data.Add(segment, record, name, rawMatterLookup: "not-a-guid"),
        "todos" => _fixture.Data.AddTodo(segment, record, name, rawMatterLookup: "not-a-guid"),
        _ => _fixture.Data.AddDocumentRaw(segment, record, name, ("_sprk_matter_value", "not-a-guid")),
    };

    /// <summary>The ids the list route returned, through the real host.</summary>
    private async Task<IReadOnlyList<string>> ListIds(string kind, string segment)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var json = await client.GetStringAsync($"/api/v1/external/{segment}/{RecordA}/{kind}");
        var idProperty = kind switch { "events" => "sprk_eventid", "todos" => "sprk_todoid", _ => "sprk_documentid" };
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(v => v.GetProperty(idProperty).GetString()!).ToList();
    }

    /// <summary><see cref="ListIds"/>, and the list's <c>truncated</c> flag (omitted from the JSON when false).</summary>
    private async Task<(IReadOnlyList<string> Ids, bool Truncated)> ListIdsAndTruncated(string kind, string segment)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var json = await client.GetStringAsync($"/api/v1/external/{segment}/{RecordA}/{kind}");
        var idProperty = kind switch { "events" => "sprk_eventid", "todos" => "sprk_todoid", _ => "sprk_documentid" };
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var ids = doc.RootElement.GetProperty("value").EnumerateArray().Select(v => v.GetProperty(idProperty).GetString()!).ToList();
        var truncated = doc.RootElement.TryGetProperty("truncated", out var flag) && flag.GetBoolean();
        return (ids, truncated);
    }

    private static IReadOnlyDictionary<string, object?> DocRow(
        string name, Guid? project = null, Guid? matter = null, Guid? workAssignment = null,
        (string Column, object? Value)? extra = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["@logicalName"] = "sprk_document",
            ["sprk_documentname"] = name,
            ["sprk_project"] = project,
            ["sprk_matter"] = matter,
            ["sprk_workassignment"] = workAssignment,
        };
        if (extra is { } e) row[e.Column] = e.Value;
        return row;
    }

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
