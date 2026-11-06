using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 039 (FR-03): the calendar events of a Matter and of a Work Assignment, read
/// through the REAL <c>/api/v1/external</c> routes and handler, exactly as <c>/projects/{id}/events</c> reads a project's.
/// </summary>
/// <remarks>
/// <para>The only double is the Dataverse Web API transport (<see cref="FakeDataverse"/>): the service under test is the REAL
/// <see cref="ExternalDataService"/>, so the <c>$filter</c> it builds (which lookup, which id), the paging, the row mapping
/// and the withheld-event rule all run for real. The caller principal is stubbed at <see cref="ICallerPrincipalResolver"/>,
/// the seam that exists for it.</para>
/// <para>Every denial asserts that Dataverse was NOT read (<see cref="FakeDataverse.Urls"/> empty): the decision is the
/// principal's rights, made before any read.</para>
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalRootEventReadTests : IClassFixture<ExternalRootEventReadTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");

    private static readonly Guid RecordA = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0390000-0000-4000-8000-00000000000b");
    private static readonly Guid OutsideRecord = Guid.Parse("a0390000-0000-4000-8000-0000000000ff");

    /// <summary>A secure matter / project the caller holds nothing on.</summary>
    private static readonly Guid Hidden = Guid.Parse("a0390000-0000-4000-8000-0000000000dd");

    private readonly Fixture _fixture;

    public ExternalRootEventReadTests(Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string> Roots => new() { "projects", "matters", "workassignments" };

    public static TheoryData<string, ExternalAccessLevel> RootsAndLevels
    {
        get
        {
            var data = new TheoryData<string, ExternalAccessLevel>();
            foreach (var root in new[] { "projects", "matters", "workassignments" })
                foreach (var level in new[] { ExternalAccessLevel.ViewOnly, ExternalAccessLevel.Collaborate, ExternalAccessLevel.FullAccess })
                    data.Add(root, level);
            return data;
        }
    }

    // =====================================================================
    // AC 1: a caller with Read gets that record's events, at every level (ViewOnly reads)
    // =====================================================================

    [Theory]
    [MemberData(nameof(RootsAndLevels))]
    public async Task Events_ForACallerWithRead_ReturnTheRecordsEvents_AndNoOthers(string segment, ExternalAccessLevel level)
    {
        _fixture.Principal = Principal(segment, level, RecordA, RecordB);
        var mine = _fixture.Data.Add(segment, RecordA, "Hearing");
        _fixture.Data.Add(segment, RecordA, "Filing deadline");
        _fixture.Data.Add(segment, RecordB, "Another record's event");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>();
        body!.Value.Select(e => e.SprkName).Should().BeEquivalentTo("Hearing", "Filing deadline");
        body.Truncated.Should().BeFalse();
        body.Value.Single(e => e.SprkEventid == mine).Should().Match<ExternalEventDto>(e =>
            e.SprkDuedate == "2026-10-21" && e.SprkStatus == 659490001 && e.SprkCreatedbycontactValue == CallerContact.ToString());

        // The query asked for THIS root through THIS root's own lookup, and no other column filtered it.
        _fixture.Data.Urls.Should().ContainSingle().Which.Should().Contain($"{LookupOf(segment)} eq {RecordA}");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_ListedByTheDtoTheProjectRouteAlwaysReturned(string segment)
    {
        _fixture.Principal = Principal(segment, ExternalAccessLevel.ViewOnly, RecordA);
        _fixture.Data.Add(segment, RecordA, "Hearing");

        using var client = _fixture.CreateAuthenticatedClient();
        var raw = await client.GetStringAsync($"/api/v1/external/{segment}/{RecordA}/events");

        // The wire shape is the project route's: one envelope, one row shape, the creator stamps for Delete, and no
        // member that names another root (the extra lookups are read to withhold rows, never returned).
        using var doc = JsonDocument.Parse(raw);
        var row = doc.RootElement.GetProperty("value")[0];
        row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "sprk_eventid", "sprk_name", "sprk_duedate", "sprk_status", "createdon", "_sprk_regardingproject_value",
            "_sprk_createdbyperson_value", "_sprk_createdbycontact_value");
    }

    // =====================================================================
    // AC 2: a record outside the caller's set is 403, with no Dataverse read
    // =====================================================================

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_ForARecordOutsideTheCallersSet_Are403_AndNothingIsRead(string segment)
    {
        _fixture.Principal = Principal(segment, ExternalAccessLevel.FullAccess, RecordA);
        _fixture.Data.Add(segment, OutsideRecord, "Not yours");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{OutsideRecord}/events");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Not yours");
        _fixture.Data.Urls.Should().BeEmpty("the decision is the principal's rights, made before any read");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_ForAnIdThatDoesNotExist_AndOneThatIsForbidden_AreTheSameRefusal(string segment)
    {
        // A record the caller holds nothing on and an id that exists nowhere cannot be told apart.
        _fixture.Principal = Principal(segment, ExternalAccessLevel.FullAccess, RecordA);
        _fixture.Data.Add(segment, Hidden, "Exists, but not yours");

        using var client = _fixture.CreateAuthenticatedClient();
        using var forbidden = await client.GetAsync($"/api/v1/external/{segment}/{Hidden}/events");
        using var missing = await client.GetAsync($"/api/v1/external/{segment}/{Guid.NewGuid()}/events");

        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        missing.StatusCode.Should().Be(forbidden.StatusCode);
        ProblemOf(await missing.Content.ReadAsStringAsync()).Should().Be(ProblemOf(await forbidden.Content.ReadAsStringAsync()));
        _fixture.Data.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task Events_ForARecordTheCallerHoldsNoRightsOn_Are403()
    {
        // A secure matter reached only through an organization grant, or a grant row with no level, sits in the
        // principal's map with None (task 136). Presence is not authorization.
        _fixture.Principal = PrincipalWith(
            matters: new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.None },
            workAssignments: new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.None });
        _fixture.Data.Add("matters", RecordA, "Secure matter event");
        _fixture.Data.Add("workassignments", RecordA, "Secure WA event");

        using var client = _fixture.CreateAuthenticatedClient();
        using var matter = await client.GetAsync($"/api/v1/external/matters/{RecordA}/events");
        using var workAssignment = await client.GetAsync($"/api/v1/external/workassignments/{RecordA}/events");

        matter.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        workAssignment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.Urls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("matters", "workassignments")]
    [InlineData("workassignments", "matters")]
    [InlineData("matters", "projects")]
    [InlineData("projects", "workassignments")]
    public async Task Events_RightsOnOneKindOfRecord_DoNotOpenAnotherKindWithTheSameId(string held, string asked)
    {
        _fixture.Principal = Principal(held, ExternalAccessLevel.FullAccess, RecordA);
        _fixture.Data.Add(asked, RecordA, "Wrong kind");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{asked}/{RecordA}/events");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.Urls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_ForAnUnauthenticatedCaller_Are401(string segment)
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/events");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.Data.Urls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("projects", "project")]
    [InlineData("matters", "matter")]
    [InlineData("workassignments", "work assignment")]
    public async Task Events_Denial_NamesTheRecordKind_InTheShippedProblemBody(string segment, string noun)
    {
        _fixture.Principal = PrincipalWith();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/events");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("title").GetString().Should().Be("Forbidden");
        doc.RootElement.GetProperty("detail").GetString().Should().Be($"You do not have access to this {noun}");
    }

    // =====================================================================
    // Secure-record isolation: an event is listed only if the caller can read EVERY record it is filed to
    // =====================================================================

    [Fact]
    public async Task ProjectEvents_AlsoFiledToASecureMatterTheCallerCannotRead_AreWithheld()
    {
        _fixture.Principal = Principal("projects", ExternalAccessLevel.Collaborate, RecordA);
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, Hidden); // F4: the matter genuinely resolves as secure
        _fixture.Data.Add("projects", RecordA, "Project only");
        _fixture.Data.Add("projects", RecordA, "Project and secure matter", matter: Hidden);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>(
            $"/api/v1/external/projects/{RecordA}/events");

        body!.Value.Select(e => e.SprkName).Should().Equal("Project only");
        body.Truncated.Should().BeFalse("a withheld event is not a truncated list");
    }

    [Fact]
    public async Task MatterEvents_AlsoFiledToAProjectOrWorkAssignmentTheCallerCannotRead_AreWithheld()
    {
        _fixture.Principal = Principal("matters", ExternalAccessLevel.Collaborate, RecordA);
        _fixture.Data.Add("matters", RecordA, "Matter only");
        _fixture.Data.Add("matters", RecordA, "Matter and hidden project", project: Hidden);
        _fixture.Data.Add("matters", RecordA, "Matter and hidden work assignment", workAssignment: Hidden);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>(
            $"/api/v1/external/matters/{RecordA}/events");

        body!.Value.Select(e => e.SprkName).Should().Equal("Matter only");
    }

    [Fact]
    public async Task MatterEvents_AlsoFiledToAProjectTheCallerCanRead_AreListed()
    {
        _fixture.Principal = PrincipalWith(
            projects: new[] { CallerProjectAccess.FromLevel(RecordB, ExternalAccessLevel.ViewOnly) },
            matters: new Dictionary<Guid, AccessRights> { [RecordA] = AccessRights.Read });
        _fixture.Data.Add("matters", RecordA, "Matter and readable project", project: RecordB);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>(
            $"/api/v1/external/matters/{RecordA}/events");

        body!.Value.Select(e => e.SprkName).Should().Equal("Matter and readable project");
    }

    [Fact]
    public async Task Events_FiledToAMalformedLookup_AreWithheld_FailClosed()
    {
        _fixture.Principal = Principal("workassignments", ExternalAccessLevel.Collaborate, RecordA);
        _fixture.Data.Add("workassignments", RecordA, "Fine");
        _fixture.Data.Add("workassignments", RecordA, "Garbled parent", rawMatterLookup: "not-a-guid");

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>(
            $"/api/v1/external/workassignments/{RecordA}/events");

        body!.Value.Select(e => e.SprkName).Should().Equal("Fine");
    }

    // =====================================================================
    // An unreadable events read is never an empty list (task 105), on the new routes as on the project route
    // =====================================================================

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task Events_WhenDataverseCannotBeRead_ReturnEmptyFlaggedTruncated_NeverACleanEmptyList(string segment)
    {
        _fixture.Principal = Principal(segment, ExternalAccessLevel.ViewOnly, RecordA);
        _fixture.Data.Add(segment, RecordA, "Unread");
        _fixture.Data.Fault = HttpStatusCode.ServiceUnavailable;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ExternalCollectionResponse<ExternalEventDto>>();
        body!.Value.Should().BeEmpty();
        body.Truncated.Should().BeTrue("a fault must not read as 'this record has no events'");
    }

    // =====================================================================
    // Read only: event create stays project-only
    // =====================================================================

    [Theory]
    [InlineData("matters")]
    [InlineData("workassignments")]
    public async Task EventCreate_OnAMatterOrWorkAssignment_IsNotARoute(string segment)
    {
        _fixture.Principal = Principal(segment, ExternalAccessLevel.FullAccess, RecordA);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{RecordA}/events", new { sprk_name = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "the path is a GET-only route");
        _fixture.Data.Urls.Should().BeEmpty();
    }

    // =====================================================================
    // The query (pure): the filter lookup comes from the shared root table, and the project query is the shipped one
    // =====================================================================

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.Project, "_sprk_regardingproject_value")]
    [InlineData(ExternalDataService.TodoRootKind.Matter, "_sprk_regardingmatter_value")]
    [InlineData(ExternalDataService.TodoRootKind.WorkAssignment, "_sprk_regardingworkassignment_value")]
    public void BuildEventsQuery_FiltersOnTheRootsOwnLookup(ExternalDataService.TodoRootKind kind, string lookup)
    {
        var query = ExternalDataService.BuildEventsQuery(kind, RecordA);

        Uri.UnescapeDataString(query).Should().StartWith($"sprk_events?$filter={lookup} eq {RecordA}&$select=");
        query.Should().EndWith("&$orderby=sprk_duedate asc,sprk_eventid asc");
        query.Should().NotContain("$top", "$top suppresses @odata.nextLink (task 105)");
    }

    [Fact]
    public void BuildEventsQuery_ForTheProject_IsTheShippedQueryPlusTheTwoOtherLookups()
    {
        ExternalDataService.BuildEventsQuery(RecordA).Should().Be(
            "sprk_events?$filter=" + Uri.EscapeDataString($"_sprk_regardingproject_value eq {RecordA}")
            + "&$select=sprk_eventid,sprk_eventname,sprk_duedate,statuscode,createdon,_sprk_regardingproject_value"
            + ",_sprk_regardingmatter_value,_sprk_regardingworkassignment_value"
            + ",_sprk_createdbyperson_value,_sprk_createdbycontact_value"
            // Task 187: the event's other parents, whose LIVE roots the rule takes in.
            + ",_sprk_regardingagreement_value,_sprk_regardinganalysis_value,_sprk_regardingbudget_value"
            + ",_sprk_regardingcommunication_value,_sprk_regardingevent_value,_sprk_regardinginvoice_value"
            + ",_sprk_regardingreportcard_value"
            + "&$orderby=sprk_duedate asc,sprk_eventid asc");
    }

    [Theory]
    [InlineData(ExternalDataService.TodoRootKind.None)]
    [InlineData(ExternalDataService.TodoRootKind.Ambiguous)]
    public void BuildEventsQuery_ForADenyState_Throws(ExternalDataService.TodoRootKind kind)
    {
        var act = () => ExternalDataService.BuildEventsQuery(kind, RecordA);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string LookupOf(string segment) => segment switch
    {
        "projects" => "_sprk_regardingproject_value",
        "matters" => "_sprk_regardingmatter_value",
        _ => "_sprk_regardingworkassignment_value",
    };

    /// <summary>The problem's title and detail: what a caller can see of a refusal.</summary>
    private static (string? Title, string? Detail) ProblemOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("title").GetString(), doc.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>A caller holding <paramref name="level"/> on <paramref name="records"/> of the route's type.</summary>
    private static CallerPrincipal Principal(string segment, ExternalAccessLevel level, params Guid[] records)
    {
        var rights = records.ToDictionary(r => r, _ => ExternalAccessLevels.ToAccessRights(level));
        return PrincipalWith(
            projects: segment == "projects" ? records.Select(r => CallerProjectAccess.FromLevel(r, level)).ToList() : null,
            matters: segment == "matters" ? rights : null,
            workAssignments: segment == "workassignments" ? rights : null);
    }

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

    // =====================================================================
    // Fixture
    // =====================================================================

    /// <summary>The external collaboration host with the principal stubbed and the REAL <see cref="ExternalDataService"/> over <see cref="Data"/>.</summary>
    public sealed class Fixture : ExternalCollaborationTestFixture
    {
        private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();

        public FakeDataverse Data { get; } = new();

        public CallerPrincipal? Principal
        {
            get => _principals.Principal;
            set => _principals.Principal = value;
        }

        public void Reset()
        {
            Principal = null;
            Data.Reset();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
                services.AddScoped(_ => new ExternalDataService(
                    new HttpClient(Data),
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                        .Build(),
                    new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
                    NullLogger<ExternalDataService>.Instance,
                    Data.FilingReader));
            });
        }
    }

    /// <summary>
    /// A Dataverse Web API for <c>sprk_events</c>, <c>sprk_todos</c> and <c>sprk_documents</c>: answers a GET by applying
    /// the <c>$filter</c> the service built (<c>_sprk_..._value eq {guid}</c>) to the seeded rows of that entity set, so a
    /// wrong lookup or id returns the wrong rows, and then PROJECTS each row through the request's <c>$select</c>, as
    /// Dataverse does: a column the query did not ask for is not in the response. A query that drops a lookup the
    /// withholding rule needs therefore reads it as absent ("not filed") and the withholding tests fail.
    /// </summary>
    public sealed class FakeDataverse : HttpMessageHandler
    {
        private readonly Dictionary<string, List<Dictionary<string, object?>>> _sets = new();

        /// <summary>Every request URL (unescaped). A denied request leaves this empty.</summary>
        public ConcurrentQueue<string> Urls { get; } = new();

        /// <summary>Task 187: every request as "METHOD url" (unescaped), so a test can assert nothing was written.</summary>
        public ConcurrentQueue<string> Requests { get; } = new();

        /// <summary>When set, every request answers with this status.</summary>
        public HttpStatusCode? Fault { get; set; }

        /// <summary>When set, only the secure-flag reads (a GET to a root entity set) answer with this status.</summary>
        public HttpStatusCode? FlagFault { get; set; }

        /// <summary>When true, the secure-flag reads throw (a transport fault).</summary>
        public bool FlagReadThrows { get; set; }

        /// <summary>
        /// Task 188: the entity sets the document narrowing reads by primary key (<c>id eq a or id eq b ...</c>): a hop read of
        /// parent documents, the 065 rule's communication and thread reads. Answered from the seeded rows whose key is one of
        /// the ids, projected through <c>$select</c>.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> KeyReadSets = new Dictionary<string, string>
        {
            ["sprk_documents"] = "sprk_documentid",
            ["sprk_communications"] = "sprk_communicationid",
            ["sprk_communicationthreads"] = "sprk_communicationthreadid",
        };

        /// <summary>Task 188: entity sets whose by-key reads answer this status (a narrowing read fault).</summary>
        public Dictionary<string, HttpStatusCode> KeyReadFaults { get; } = new();

        /// <summary>Task 188: every by-key read sent to one of <see cref="KeyReadSets"/> ("set?query", unescaped).</summary>
        public IReadOnlyList<string> KeyReads(string set) =>
            Urls.Where(u => u.Contains($"/{set}?", StringComparison.Ordinal) && u.Contains($"{KeyReadSets[set]} eq ", StringComparison.Ordinal))
                .ToList();

        /// <summary>The root entity sets and their primary keys: the secure-flag reads (owner option B) query these.</summary>
        private static readonly IReadOnlyDictionary<string, string> RootSets = new Dictionary<string, string>
        {
            ["sprk_projects"] = "sprk_projectid",
            ["sprk_matters"] = "sprk_matterid",
            ["sprk_workassignments"] = "sprk_workassignmentid",
        };

        /// <summary>Every secure-flag query sent (a GET to a root entity set), unescaped.</summary>
        public IReadOnlyList<string> FlagReads =>
            Urls.Where(u => RootSets.Keys.Any(set => u.Contains($"/{set}?", StringComparison.Ordinal))).ToList();

        public FakeDataverse()
        {
            // The filing walk's queries (the ONE walk, EffectiveRootFlags), answered from _filing by the id condition each one
            // carries (Equal or In), like DocumentIndexParentResolverTests does.
            _filingReader.Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) =>
                {
                    FilingReadLog.Enqueue(query.EntityName);
                    if (FilingReadThrows || string.Equals(FilingFaultEntity, query.EntityName, StringComparison.OrdinalIgnoreCase))
                        throw new TimeoutException("The filing read failed (test).");

                    var ids = query.Criteria.Conditions
                        .Where(c => string.Equals(c.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase))
                        .SelectMany(c => c.Values.Select(v => (Guid)v))
                        .ToHashSet();
                    lock (_filing)
                    {
                        return new EntityCollection(_filing
                            .Where(kv => string.Equals(kv.Key.Table, query.EntityName, StringComparison.OrdinalIgnoreCase) && ids.Contains(kv.Key.Id))
                            .Select(kv => Projected(kv.Value, query.ColumnSet))
                            .ToList());
                    }
                });
        }

        /// <summary>
        /// Task 187 fix round 1 (R5): a stored row as the SDK returns it for <paramref name="columns"/>: only the requested
        /// attributes (and the id). A walk whose select leaves a filing lookup out reads it as "not filed", so a test fails.
        /// </summary>
        private static Entity Projected(Entity stored, ColumnSet? columns)
        {
            if (columns is null || columns.AllColumns)
                return stored;

            var row = new Entity(stored.LogicalName, stored.Id);
            foreach (var column in columns.Columns)
            {
                if (stored.Attributes.TryGetValue(column, out var value))
                    row[column] = value;
            }

            return row;
        }

        // ---- The filing world (effective secure status: a work assignment filed under a secure matter is secure) ----

        private readonly Dictionary<(string Table, Guid Id), Entity> _filing = new();
        private readonly Mock<IGenericEntityService> _filingReader = new(MockBehavior.Strict);

        /// <summary>The app-only reader the REAL <see cref="ExternalDataService"/> climbs the filing chain through.</summary>
        public IGenericEntityService FilingReader => _filingReader.Object;

        /// <summary>The entity name of every filing-walk query sent, in order.</summary>
        public ConcurrentQueue<string> FilingReadLog { get; } = new();

        /// <summary>When true, every filing-walk read throws (a transport fault).</summary>
        public bool FilingReadThrows { get; set; }

        /// <summary>When set (e.g. <c>sprk_matter</c>), only the filing-walk reads of that entity throw.</summary>
        public string? FilingFaultEntity { get; set; }

        public void Reset()
        {
            lock (_sets) _sets.Clear();
            lock (_filing) { _filing.Clear(); _recordTypes.Clear(); }
            Urls.Clear();
            Requests.Clear();
            FilingReadLog.Clear();
            Fault = null;
            FlagFault = null;
            FlagReadThrows = false;
            KeyReadFaults.Clear();
            FilingReadThrows = false;
            FilingFaultEntity = null;
        }

        /// <summary>
        /// Files work assignment <paramref name="workAssignment"/> under <paramref name="parent"/> (its typed
        /// <c>sprk_regardingmatter</c> / <c>sprk_regardingproject</c>): the walk then reads the parent's own flag. Seed both
        /// with <see cref="AddRoot"/> first; the HTTP side (each root's OWN flag) is unaffected.
        /// </summary>
        public void FileWorkAssignmentUnder(Guid workAssignment, ExternalGrantRootType parent, Guid parentId)
        {
            var (column, table) = parent switch
            {
                ExternalGrantRootType.Matter => ("sprk_regardingmatter", "sprk_matter"),
                ExternalGrantRootType.Project => ("sprk_regardingproject", "sprk_project"),
                _ => throw new NotSupportedException("A work assignment files under a matter or a project."),
            };
            lock (_filing) _filing[("sprk_workassignment", workAssignment)][column] = new EntityReference(table, parentId);
        }

        /// <summary>
        /// Files project <paramref name="project"/> under <paramref name="parent"/> through the POLYMORPHIC PAIR, the only way a
        /// project files under anything (it has no typed regarding lookup): <c>sprk_regardingrecordid</c> holds the parent's id
        /// as text and <c>sprk_regardingrecordtype</c> points at the <c>sprk_recordtype_ref</c> row whose
        /// <c>sprk_recordlogicalname</c> names the parent's table, exactly the columns <c>SecureRootInheritance</c> reads
        /// (<c>PairIdColumn</c>, <c>PairTypeColumn</c>, <c>RecordTypeLogicalNameColumn</c>). Seed the project with
        /// <see cref="AddRoot"/> first; the parent need not exist (a named-but-absent parent).
        /// </summary>
        public void FileProjectUnderByPair(Guid project, ExternalGrantRootType parent, Guid parentId)
        {
            var parentTable = parent switch
            {
                ExternalGrantRootType.Matter => "sprk_matter",
                ExternalGrantRootType.Project => "sprk_project",
                _ => throw new NotSupportedException("A project files under a matter or a project."),
            };
            lock (_filing)
            {
                if (!_recordTypes.TryGetValue(parentTable, out var typeId))
                {
                    typeId = Guid.NewGuid();
                    _recordTypes[parentTable] = typeId;
                    var typeRow = new Entity("sprk_recordtype_ref", typeId);
                    typeRow["sprk_recordlogicalname"] = parentTable;
                    _filing[("sprk_recordtype_ref", typeId)] = typeRow;
                }

                var row = _filing[("sprk_project", project)];
                row["sprk_regardingrecordid"] = parentId.ToString("D");
                row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", typeId);
            }
        }

        /// <summary>
        /// #1630: files work assignment <paramref name="workAssignment"/> under an INTERMEDIATE by its typed lookup
        /// (<c>sprk_regardinginvoice</c> / <c>sprk_regardingevent</c>). The intermediate row itself is NOT created here
        /// (<see cref="AddIntermediate"/> does that): a work assignment filed under an intermediate nobody added names an
        /// intermediate the walk's read does not return. Seed the work assignment with <see cref="AddRoot"/> first.
        /// </summary>
        public void FileWorkAssignmentUnderIntermediate(Guid workAssignment, string intermediateTable, Guid intermediateId)
        {
            var column = intermediateTable switch
            {
                "sprk_invoice" => "sprk_regardinginvoice",
                "sprk_event" => "sprk_regardingevent",
                _ => throw new NotSupportedException("A work assignment files under an invoice or an event."),
            };
            lock (_filing) _filing[("sprk_workassignment", workAssignment)][column] = new EntityReference(intermediateTable, intermediateId);
        }

        /// <summary>
        /// #1630: creates the intermediate row in the filing world. With a <paramref name="belongsTo"/> root it belongs to that
        /// matter or project through its own root column (an invoice: <c>sprk_matter</c> / <c>sprk_project</c>; an event:
        /// <c>sprk_regardingmatter</c> / <c>sprk_regardingproject</c>, the stamp topology); without one it exists but belongs to
        /// nothing - which is NOT the same as an intermediate the read does not return.
        /// </summary>
        public void AddIntermediate(string intermediateTable, Guid id, ExternalGrantRootType? belongsTo = null, Guid? parentId = null)
        {
            var row = new Entity(intermediateTable, id);
            if (belongsTo is { } root)
            {
                var column = (intermediateTable, root) switch
                {
                    ("sprk_invoice", ExternalGrantRootType.Matter) => "sprk_matter",
                    ("sprk_invoice", ExternalGrantRootType.Project) => "sprk_project",
                    ("sprk_event", ExternalGrantRootType.Matter) => "sprk_regardingmatter",
                    ("sprk_event", ExternalGrantRootType.Project) => "sprk_regardingproject",
                    _ => throw new NotSupportedException("An invoice or an event belongs to a matter or a project."),
                };
                row[column] = new EntityReference(ExternalGrantRoot.LogicalNameFor(root), parentId!.Value);
            }

            lock (_filing) _filing[(intermediateTable, id)] = row;
        }

        private readonly Dictionary<string, Guid> _recordTypes = new();

        /// <summary>
        /// Task 187: a record in the FILING world (what the live-parent walk reads app-only), filed through each
        /// <paramref name="lookups"/> entry (column, target table, id), e.g. a communication whose <c>sprk_regardingmatter</c>
        /// names a matter. The HTTP side is unaffected. A record not added here is one the walk's read does not return.
        /// </summary>
        public void AddFilingRow(string table, Guid id, params (string Column, string Target, Guid Id)[] lookups)
        {
            var row = new Entity(table, id);
            foreach (var (column, target, targetId) in lookups)
                row[column] = new EntityReference(target, targetId);
            lock (_filing) _filing[(table, id)] = row;
        }

        /// <summary>Task 187: sets one column of a seeded HTTP-side row (e.g. a to-do's <c>_sprk_regardingcommunication_value</c>).</summary>
        public void SetColumn(string set, string idColumn, string id, string column, object? value)
        {
            lock (_sets)
            {
                _sets[set].Single(r => string.Equals(r[idColumn] as string, id, StringComparison.OrdinalIgnoreCase))[column] = value;
            }
        }

        /// <summary>
        /// Seeds a root record (project, matter, work assignment) with its <c>sprk_issecure</c>. A <c>null</c> flag is OMITTED
        /// from the response, as Dataverse omits a null (or a field-security-masked) value. The row carries other columns too,
        /// so a read that did not project through <c>$select</c> would be visible.
        /// </summary>
        public void AddRoot(ExternalGrantRootType root, Guid id, bool? isSecure)
        {
            var set = ExternalGrantRoot.BindFor(root).EntitySet;

            // The same record in the filing world, as the walk reads it: filed under nothing until FileWorkAssignmentUnder.
            var table = ExternalGrantRoot.LogicalNameFor(root);
            var filingRow = new Entity(table, id);
            if (isSecure is { } secure) filingRow["sprk_issecure"] = secure;
            filingRow["sprk_accesspermission"] = new OptionSetValue(100000000);
            if (root == ExternalGrantRootType.Matter) filingRow["sprk_mattername"] = $"root {id}";
            if (root == ExternalGrantRootType.Project) filingRow["sprk_projectname"] = $"root {id}";
            lock (_filing) _filing[(table, id)] = filingRow;

            Seed(set, new Dictionary<string, object?>
            {
                [RootSets[set]] = id.ToString(),
                ["sprk_issecure"] = isSecure,
                ["sprk_name"] = $"root {id}",
                ["sprk_accesspermission"] = 100000000,
                ["statecode"] = 0,
            });
        }

        /// <summary>
        /// Seeds a root whose <c>sprk_issecure</c> comes back as a raw, NON-boolean JSON value (e.g. a string), the shape a
        /// misconfigured or masked column could take. The filing world sees it as an absent flag.
        /// </summary>
        public void AddRootWithRawFlag(ExternalGrantRootType root, Guid id, object rawFlag)
        {
            AddRoot(root, id, isSecure: null);
            var set = ExternalGrantRoot.BindFor(root).EntitySet;
            lock (_sets)
            {
                _sets[set].Single(r => string.Equals(r[RootSets[set]] as string, id.ToString(), StringComparison.OrdinalIgnoreCase))
                    ["sprk_issecure"] = rawFlag;
            }
        }

        /// <summary>A secure root (<c>sprk_issecure = true</c>).</summary>
        public void AddSecureRoot(ExternalGrantRootType root, Guid id) => AddRoot(root, id, isSecure: true);

        /// <summary>A Standard, non-secure root (<c>sprk_issecure = false</c>).</summary>
        public void AddOpenRoot(ExternalGrantRootType root, Guid id) => AddRoot(root, id, isSecure: false);

        /// <summary>
        /// A REAL <see cref="ExternalDataService"/> over this fake, e.g. for its secure-flag read. Task 188: with
        /// <paramref name="identity"/>, the document narrowing can tell an internal systemuser; without one every caller is
        /// narrowed (the fail-closed default).
        /// </summary>
        public ExternalDataService NewService(
            bool withFilingReader = true, Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver? identity = null) => new(
            new HttpClient(this),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build(),
            new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
            NullLogger<ExternalDataService>.Instance,
            withFilingReader ? FilingReader : null,
            columns: null,
            identity: identity);

        /// <summary>
        /// Task 188: seeds a communication as the 065 rule reads it: <c>sprk_isinternalonly</c> (<c>null</c> stays null, which
        /// hides) and its thread lookup (<c>null</c>: no thread, which hides). <paramref name="rawFlag"/>, when given, is
        /// stored as the flag instead (an unreadable value).
        /// </summary>
        public void AddCommunication(Guid id, bool? internalOnly, Guid? thread, object? rawFlag = null) =>
            Seed("sprk_communications", new Dictionary<string, object?>
            {
                ["sprk_communicationid"] = id.ToString(),
                ["sprk_isinternalonly"] = rawFlag ?? internalOnly,
                ["_sprk_communicationthread_value"] = thread?.ToString(),
                ["sprk_subject"] = $"subject {id}",
            });

        /// <summary>Task 188: seeds a thread with its privacy (<c>sprk_privacystate</c>; Open = 100000000, Private = 100000001).</summary>
        public void AddThread(Guid id, int? privacy) =>
            Seed("sprk_communicationthreads", new Dictionary<string, object?>
            {
                ["sprk_communicationthreadid"] = id.ToString(),
                ["sprk_privacystate"] = privacy,
            });

        /// <summary>
        /// Task 188: seeds a document that is NOT under any route parent (an ancestor the narrowing's hop reads reach), with
        /// its own Access Permission, communication link and parent document link. <paramref name="rawCommunication"/>, when
        /// given, is stored as the communication link instead (an unreadable value: not a GUID, or an empty one).
        /// </summary>
        public void AddAncestorDocument(Guid id, int? accessPermission, Guid? communication, Guid? parent, object? rawCommunication = null) =>
            Seed("sprk_documents", new Dictionary<string, object?>
            {
                ["sprk_documentid"] = id.ToString(),
                ["sprk_documentname"] = $"ancestor {id}",
                ["sprk_accesspermission"] = accessPermission,
                ["_sprk_relatedcommunication_value"] = rawCommunication ?? communication?.ToString(),
                ["_sprk_parentdocument_value"] = parent?.ToString(),
            });

        private void Seed(string set, Dictionary<string, object?> row)
        {
            lock (_sets)
            {
                if (!_sets.TryGetValue(set, out var rows)) _sets[set] = rows = new List<Dictionary<string, object?>>();
                rows.Add(row);
            }
        }

        /// <summary>Seeds an event filed to <paramref name="segment"/>'s record <paramref name="record"/> (and optionally others).</summary>
        public string Add(
            string segment, Guid record, string name, Guid? project = null, Guid? matter = null, Guid? workAssignment = null,
            string? rawMatterLookup = null)
        {
            var id = Guid.NewGuid().ToString();
            Seed("sprk_events", new Dictionary<string, object?>
            {
                ["sprk_eventid"] = id,
                ["sprk_eventname"] = name,
                ["sprk_duedate"] = "2026-10-21",
                ["statuscode"] = 659490001,
                ["createdon"] = "2026-10-01T10:00:00Z",
                ["_sprk_regardingproject_value"] = segment == "projects" ? record.ToString() : project?.ToString(),
                ["_sprk_regardingmatter_value"] = segment == "matters" ? record.ToString() : rawMatterLookup ?? matter?.ToString(),
                ["_sprk_regardingworkassignment_value"] = segment == "workassignments" ? record.ToString() : workAssignment?.ToString(),
                ["_sprk_createdbyperson_value"] = null,
                ["_sprk_createdbycontact_value"] = CallerContact.ToString(),
            });
            return id;
        }

        /// <summary>Seeds a to-do filed to <paramref name="segment"/>'s record <paramref name="record"/> (and optionally others).</summary>
        public string AddTodo(
            string segment, Guid record, string name, Guid? project = null, Guid? matter = null, Guid? workAssignment = null,
            string? rawMatterLookup = null)
        {
            var id = Guid.NewGuid().ToString();
            Seed("sprk_todos", new Dictionary<string, object?>
            {
                ["sprk_todoid"] = id,
                ["sprk_name"] = name,
                ["sprk_duedate"] = "2026-10-21",
                ["statecode"] = 0,
                ["statuscode"] = 1,
                ["createdon"] = "2026-10-01T10:00:00Z",
                ["_sprk_regardingproject_value"] = segment == "projects" ? record.ToString() : project?.ToString(),
                ["_sprk_regardingmatter_value"] = segment == "matters" ? record.ToString() : rawMatterLookup ?? matter?.ToString(),
                ["_sprk_regardingworkassignment_value"] = segment == "workassignments" ? record.ToString() : workAssignment?.ToString(),
                ["_sprk_createdbyperson_value"] = null,
                ["_sprk_createdbycontact_value"] = CallerContact.ToString(),
            });
            return id;
        }

        /// <summary>
        /// Task 187 fix round 1 (R1): <see cref="AddTodo"/> with extra raw columns set at seed time (e.g. a parent lookup
        /// <c>_sprk_regardingcommunication_value</c>), so a page of thousands of rows is seeded without a scan per row.
        /// </summary>
        public string AddTodoWith(string segment, Guid record, string name, params (string Column, string Value)[] columns)
        {
            var id = AddTodo(segment, record, name);
            lock (_sets)
            {
                var row = _sets["sprk_todos"][^1];
                foreach (var (column, value) in columns) row[column] = value;
            }

            return id;
        }

        /// <summary>
        /// Seeds a document filed to <paramref name="segment"/>'s record <paramref name="record"/> through that root's typed
        /// lookup, plus any <paramref name="extra"/> link columns (e.g. <c>_sprk_matter_value</c>, <c>_sprk_relatedmatter_value</c>).
        /// </summary>
        public string AddDocument(string segment, Guid record, string name, params (string Column, Guid Value)[] extra)
        {
            var id = Guid.NewGuid().ToString();
            var row = new Dictionary<string, object?>
            {
                ["sprk_documentid"] = id,
                ["sprk_documentname"] = name,
                ["sprk_documenttype"] = null,
                ["sprk_filesummary"] = $"summary of {name}",
                ["createdon"] = "2026-10-01T10:00:00Z",
                ["_sprk_createdbyperson_value"] = null,
                ["_sprk_createdbycontact_value"] = CallerContact.ToString(),
                [segment switch
                {
                    "projects" => "_sprk_project_value",
                    "matters" => "_sprk_matter_value",
                    "invoices" => "_sprk_invoice_value",
                    "servicerequests" => "_sprk_relatedservicerequest_value",
                    _ => "_sprk_workassignment_value",
                }] = record.ToString(),
            };
            foreach (var (column, value) in extra) row[column] = value.ToString();
            Seed("sprk_documents", row);
            return id;
        }

        /// <summary>
        /// Seeds an invoice with its parent matter and/or project (what <c>GetInvoiceParentsAsync</c> reads), and the same
        /// record in the filing world (task 187 D4: an invoice route walks its own invoice, so both reads see one record).
        /// </summary>
        public void AddInvoice(Guid id, Guid? matter, Guid? project)
        {
            Seed("sprk_invoices", new Dictionary<string, object?>
            {
                ["sprk_invoiceid"] = id.ToString(),
                ["_sprk_matter_value"] = matter?.ToString(),
                ["_sprk_project_value"] = project?.ToString(),
            });
            var lookups = new List<(string Column, string Target, Guid Id)>();
            if (matter is { } m) lookups.Add(("sprk_matter", "sprk_matter", m));
            if (project is { } p) lookups.Add(("sprk_project", "sprk_project", p));
            AddFilingRow("sprk_invoice", id, lookups.ToArray());
        }

        /// <summary>Seeds a service request with its requester contact (what <c>GetServiceRequestRequesterAsync</c> reads).</summary>
        public void AddServiceRequest(Guid id, Guid requester) =>
            Seed("sprk_servicerequests", new Dictionary<string, object?>
            {
                ["sprk_servicerequestid"] = id.ToString(),
                ["_sprk_requestedby_value"] = requester.ToString(),
            });

        /// <summary><see cref="AddDocument"/> with a link column set to an arbitrary raw value (a malformed lookup).</summary>
        public string AddDocumentRaw(string segment, Guid record, string name, (string Column, object? Value) rawLink)
        {
            var id = AddDocument(segment, record, name);
            lock (_sets)
            {
                var row = _sets["sprk_documents"].Single(r => (string?)r["sprk_documentid"] == id);
                row[rawLink.Column] = rawLink.Value;
            }

            return id;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            Urls.Enqueue(url);
            Requests.Enqueue($"{request.Method} {url}");

            if (Fault is { } fault)
                return Task.FromResult(new HttpResponseMessage(fault));

            // The secure-flag read: `sprk_matters?$filter=sprk_matterid eq {a} or sprk_matterid eq {b}&$select=...`. Answers the
            // seeded rows whose key is one of the ids, projected through $select, a null value omitted (as Dataverse does).
            var path = request.RequestUri!.AbsolutePath;
            if (RootSets.TryGetValue(path[(path.LastIndexOf('/') + 1)..], out var rootKey))
            {
                if (FlagReadThrows)
                    throw new HttpRequestException("The secure-flag read failed (test).");
                if (FlagFault is { } flagFault)
                    return Task.FromResult(new HttpResponseMessage(flagFault));

                var ids = Regex.Matches(url, $@"{rootKey} eq ([0-9a-fA-F-]{{36}})")
                    .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var rootSelect = Regex.Match(url, @"\$select=([^&]*)");
                var rootColumns = rootSelect.Success ? rootSelect.Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal) : null;
                List<Dictionary<string, object?>> roots;
                lock (_sets)
                {
                    roots = (_sets.TryGetValue(path[(path.LastIndexOf('/') + 1)..], out var seededRoots) ? seededRoots : new())
                        .Where(r => r[rootKey] is string key && ids.Contains(key))
                        .Select(r => r.Where(kv => kv.Value is not null && (rootColumns is null || rootColumns.Contains(kv.Key)))
                            .ToDictionary(kv => kv.Key, kv => kv.Value))
                        .ToList();
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { value = roots }), System.Text.Encoding.UTF8, "application/json"),
                });
            }

            // A single-row read, `sprk_documents({id})?$select=...`: the row whose primary key is {id}, projected through
            // $select, as the document / delete scoping reads send it (fix round for task 039). 404 when there is none.
            var single = Regex.Match(request.RequestUri.AbsolutePath, @"/(\w+)\(([0-9a-fA-F-]{36})\)$");
            if (single.Success)
            {
                var wanted = single.Groups[2].Value;
                Dictionary<string, object?>? hit;
                lock (_sets)
                {
                    hit = _sets.TryGetValue(single.Groups[1].Value, out var seededRows)
                        ? seededRows.FirstOrDefault(r => new[] { "sprk_documentid", "sprk_todoid", "sprk_eventid", "sprk_invoiceid", "sprk_servicerequestid" }
                            .Any(k => r.TryGetValue(k, out var v) && string.Equals(v as string, wanted, StringComparison.OrdinalIgnoreCase)))
                        : null;
                }

                if (hit is null)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

                var selected = Regex.Match(url, @"\$select=([^&]*)");
                var wantedColumns = selected.Success ? selected.Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal) : null;
                var projectedRow = hit.Where(kv => wantedColumns is null || wantedColumns.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(projectedRow), System.Text.Encoding.UTF8, "application/json"),
                });
            }

            var set = request.RequestUri.AbsolutePath[(request.RequestUri.AbsolutePath.LastIndexOf('/') + 1)..];

            // Task 188: a by-key read (`sprk_documents?$select=...&$filter=sprk_documentid eq {a} or ...&$top=n`) answers the
            // seeded rows whose key is one of the ids, projected through $select (a null value comes back as null, as the Web
            // API sends it).
            if (KeyReadSets.TryGetValue(set, out var keyColumn) && url.Contains($"{keyColumn} eq ", StringComparison.Ordinal))
            {
                if (KeyReadFaults.TryGetValue(set, out var keyFault))
                    return Task.FromResult(new HttpResponseMessage(keyFault));

                var keys = Regex.Matches(url, $@"{keyColumn} eq ([0-9a-fA-F-]{{36}})")
                    .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var keySelect = Regex.Match(url, @"\$select=([^&]*)");
                var keyColumns = keySelect.Success ? keySelect.Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal) : null;
                List<Dictionary<string, object?>> keyed;
                lock (_sets)
                {
                    keyed = (_sets.TryGetValue(set, out var seededKeyed) ? seededKeyed : new())
                        .Where(r => r.TryGetValue(keyColumn, out var k) && k is string key && keys.Contains(key))
                        .Select(r => r.Where(kv => keyColumns is null || keyColumns.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                        .ToList();
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { value = keyed }), System.Text.Encoding.UTF8, "application/json"),
                });
            }

            var filter = Regex.Match(url, @"(_sprk_\w+_value) eq ([0-9a-fA-F-]{36})");
            var select = Regex.Match(url, @"\$select=([^&]*)");
            var columns = select.Success ? select.Groups[1].Value.Split(',').ToHashSet(StringComparer.Ordinal) : null;

            List<Dictionary<string, object?>> match;
            lock (_sets)
            {
                var rows = _sets.TryGetValue(set, out var seeded) ? seeded : new List<Dictionary<string, object?>>();
                match = filter.Success
                    ? rows.Where(r => r.TryGetValue(filter.Groups[1].Value, out var v)
                                      && string.Equals(v as string, filter.Groups[2].Value, StringComparison.OrdinalIgnoreCase)).ToList()
                    : new List<Dictionary<string, object?>>();
            }

            // $select projection: only the requested columns come back.
            var projected = match
                .Select(r => columns is null ? r : r.Where(kv => columns.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { value = projected }), System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
