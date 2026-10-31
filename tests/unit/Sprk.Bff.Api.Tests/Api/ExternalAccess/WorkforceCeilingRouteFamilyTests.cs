using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 186, review round 1 F1: the workforce ceiling on EVERY workforce-reachable
/// <c>/api/v1/external</c> route family, through the REAL routes and the one <see cref="WorkforceCeilingEndpointFilter"/>.
/// For each route: removing one privilege its declared set needs is a 403 <c>workforce_baseline_privilege_missing</c>
/// decided before the handler (must-fire); an unreadable role is <c>workforce_baseline_unavailable</c>; the full set passes
/// the ceiling and the handler's own scope decides (must-not-fire); and a systemuser is never checked.
/// </summary>
[Trait("category", "external-access")]
public sealed class WorkforceCeilingRouteFamilyTests : IClassFixture<ExternalDeleteOwnTests.Fixture>
{
    private static readonly Guid Me = Guid.Parse("99999999-9999-9999-9999-99999999f186");
    private static readonly Guid Project = Guid.Parse("a1860000-0000-4000-8000-0000000000a1");
    private static readonly Guid Matter = Guid.Parse("a1860000-0000-4000-8000-0000000000b2");
    private static readonly Guid WorkAssignment = Guid.Parse("a1860000-0000-4000-8000-0000000000c3");
    private static readonly Guid Invoice = Guid.Parse("a1860000-0000-4000-8000-0000000000d4");
    private static readonly Guid ServiceRequest = Guid.Parse("a1860000-0000-4000-8000-0000000000e5");
    private static readonly Guid Item = Guid.Parse("a1860000-0000-4000-8000-0000000000f6");

    private const string Json = "json";
    private const string File = "file";
    private const string None = "";

    private readonly ExternalDeleteOwnTests.Fixture _fixture;

    public WorkforceCeilingRouteFamilyTests(ExternalDeleteOwnTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    /// <summary>One route, one privilege its declared set needs: (verb, path, body kind, table, the privilege removed).</summary>
    public static TheoryData<string, string, string, string, AccessRights> Routes => new()
    {
        // Projects, contacts, organizations
        { "GET", "/projects", None, "sprk_project", AccessRights.Read },
        { "GET", $"/projects/{Project}", None, "sprk_project", AccessRights.Read },
        { "GET", $"/projects/{Project}/contacts", None, "contact", AccessRights.Read },
        { "GET", $"/projects/{Project}/organizations", None, "account", AccessRights.Read },
        // Documents on project, matter, work assignment, invoice (list, content, versions, upload)
        { "GET", $"/projects/{Project}/documents", None, "sprk_document", AccessRights.Read },
        { "GET", $"/projects/{Project}/documents/{Item}/content", None, "sprk_document", AccessRights.Read },
        { "GET", $"/projects/{Project}/documents/{Item}/versions", None, "sprk_project", AccessRights.Read },
        { "POST", $"/projects/{Project}/documents", File, "sprk_document", AccessRights.Create },
        { "GET", $"/matters/{Matter}/documents", None, "sprk_matter", AccessRights.Read },
        { "GET", $"/matters/{Matter}/documents/{Item}/content", None, "sprk_document", AccessRights.Read },
        { "GET", $"/matters/{Matter}/documents/{Item}/versions", None, "sprk_document", AccessRights.Read },
        { "POST", $"/matters/{Matter}/documents", File, "sprk_matter", AccessRights.AppendTo },
        { "GET", $"/workassignments/{WorkAssignment}/documents", None, "sprk_document", AccessRights.Read },
        { "GET", $"/workassignments/{WorkAssignment}/documents/{Item}/content", None, "sprk_workassignment", AccessRights.Read },
        { "GET", $"/workassignments/{WorkAssignment}/documents/{Item}/versions", None, "sprk_document", AccessRights.Read },
        { "POST", $"/workassignments/{WorkAssignment}/documents", File, "sprk_workassignment", AccessRights.AppendTo },
        { "GET", $"/invoices/{Invoice}/documents", None, "sprk_invoice", AccessRights.Read },
        { "GET", $"/invoices/{Invoice}/documents/{Item}/content", None, "sprk_document", AccessRights.Read },
        { "GET", $"/invoices/{Invoice}/documents/{Item}/versions", None, "sprk_invoice", AccessRights.Read },
        { "POST", $"/invoices/{Invoice}/documents", File, "sprk_invoice", AccessRights.AppendTo },
        // To-dos
        { "GET", $"/projects/{Project}/todos", None, "sprk_todo", AccessRights.Read },
        { "POST", $"/projects/{Project}/todos", Json, "sprk_todo", AccessRights.Create },
        { "GET", $"/matters/{Matter}/todos", None, "sprk_matter", AccessRights.Read },
        { "POST", $"/matters/{Matter}/todos", Json, "sprk_matter", AccessRights.AppendTo },
        { "GET", $"/workassignments/{WorkAssignment}/todos", None, "sprk_todo", AccessRights.Read },
        { "POST", $"/workassignments/{WorkAssignment}/todos", Json, "sprk_todo", AccessRights.Create },
        { "PATCH", $"/todos/{Item}", Json, "sprk_todo", AccessRights.Write },
        // Calendar events
        { "GET", $"/projects/{Project}/events", None, "sprk_event", AccessRights.Read },
        { "POST", $"/projects/{Project}/events", Json, "sprk_project", AccessRights.AppendTo },
        { "GET", $"/matters/{Matter}/events", None, "sprk_event", AccessRights.Read },
        { "GET", $"/workassignments/{WorkAssignment}/events", None, "sprk_workassignment", AccessRights.Read },
        // Delete own
        { "DELETE", $"/documents/{Item}", None, "sprk_document", AccessRights.Delete },
        { "DELETE", $"/todos/{Item}", None, "sprk_todo", AccessRights.Delete },
        { "DELETE", $"/events/{Item}", None, "sprk_event", AccessRights.Delete },
        // Messages (the root from {root})
        { "GET", $"/matters/{Matter}/messages", None, "sprk_communication", AccessRights.Read },
        { "GET", $"/servicerequests/{ServiceRequest}/messages", None, "sprk_servicerequest", AccessRights.Read },
        { "POST", $"/projects/{Project}/messages", Json, "sprk_communication", AccessRights.Create },
        { "POST", $"/servicerequests/{ServiceRequest}/messages", Json, "sprk_servicerequest", AccessRights.AppendTo },
        // Contact-side grants (the record from the body)
        { "POST", "/contact-grants", "grant", "sprk_externalrecordaccess", AccessRights.Create },
        { "POST", "/contact-grants", "grant", "sprk_project", AccessRights.AppendTo },
        { "POST", "/contact-grants", "grant", "contact", AccessRights.Read },
        { "GET", $"/contact-grants?recordType=project&recordId={Project}", None, "sprk_externalrecordaccess", AccessRights.Read },
        { "GET", $"/contact-grants?recordType=project&recordId={Project}", None, "contact", AccessRights.Read },
        { "POST", "/contact-grants/revoke", "revoke", "sprk_externalrecordaccess", AccessRights.Write },
        // Invoice submissions (ebilling-r1 task 044): one submit set per core table, the read set, the firm-records set
        { "POST", $"/matters/{Matter}/invoice-submissions", File, "sprk_invoicesubmission", AccessRights.Create },
        { "POST", $"/matters/{Matter}/invoice-submissions", File, "sprk_document", AccessRights.Create },
        { "POST", $"/matters/{Matter}/invoice-submissions", File, "sprk_document", AccessRights.AppendTo },
        { "POST", $"/matters/{Matter}/invoice-submissions", File, "sprk_matter", AccessRights.AppendTo },
        { "POST", $"/projects/{Project}/invoice-submissions", File, "sprk_invoicesubmission", AccessRights.Create },
        { "POST", $"/projects/{Project}/invoice-submissions", File, "sprk_document", AccessRights.Create },
        { "POST", $"/projects/{Project}/invoice-submissions", File, "sprk_document", AccessRights.AppendTo },
        { "POST", $"/projects/{Project}/invoice-submissions", File, "sprk_project", AccessRights.AppendTo },
        { "POST", $"/workassignments/{WorkAssignment}/invoice-submissions", File, "sprk_invoicesubmission", AccessRights.Create },
        { "POST", $"/workassignments/{WorkAssignment}/invoice-submissions", File, "sprk_document", AccessRights.Create },
        { "POST", $"/workassignments/{WorkAssignment}/invoice-submissions", File, "sprk_document", AccessRights.AppendTo },
        { "POST", $"/workassignments/{WorkAssignment}/invoice-submissions", File, "sprk_workassignment", AccessRights.AppendTo },
        { "GET", $"/invoice-submissions/{Item}", None, "sprk_invoicesubmission", AccessRights.Read },
        { "GET", "/invoice-submissions", None, "sprk_invoicesubmission", AccessRights.Read },
        { "GET", "/invoice-submissions/firm-records", None, "sprk_matter", AccessRights.Read },
        { "GET", "/invoice-submissions/firm-records", None, "sprk_project", AccessRights.Read },
        { "GET", "/invoice-submissions/firm-records", None, "sprk_workassignment", AccessRights.Read },
        { "GET", "/invoice-submissions/firm-records", None, "sprk_invoicesubmission", AccessRights.Create },
    };

    /// <summary>Each distinct route once.</summary>
    public static TheoryData<string, string, string> DistinctRoutes()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (verb, path, body) in Routes.Select(r => ((string)r[0], (string)r[1], (string)r[2])).Distinct())
            data.Add(verb, path, body);
        return data;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task AWorkforceContact_WhoseRoleLacksOneDeclaredPrivilege_Is403_BeforeTheHandler(
        string verb, string path, string body, string table, AccessRights removed)
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(table, removed);
        var todo = _fixture.Seed(ExternalDataService.DeletableKind.Todo, contact: Me);

        using var response = await Send(verb, path.Replace(Item.ToString(), todo.ToString()), body);

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
        _fixture.Data.Targets.Should().ContainKey((ExternalDataService.DeletableKind.Todo, todo), "nothing ran after the ceiling");
        _fixture.Steps.Should().BeEmpty("no file or row was touched");
    }

    [Theory]
    [MemberData(nameof(DistinctRoutes))]
    public async Task AWorkforceContact_WhoseRoleCannotBeRead_Is403_BaselineUnavailable(string verb, string path, string body)
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        using var response = await Send(verb, path, body);

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
    }

    [Theory]
    [MemberData(nameof(DistinctRoutes))]
    public async Task AWorkforceContact_WhoseRoleHoldsEveryRoutePrivilege_PassesTheCeiling_AndTheHandlerDecides(
        string verb, string path, string body)
    {
        // Must-not-fire: the full set (the owner table) passes; this caller has no grant, so the handler's own scope
        // answers — never a ceiling code.
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);

        using var response = await Send(verb, path, body);

        (await CeilingRouteTestData.ReasonCode(response)).Should()
            .NotBe(WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing)
            .And.NotBe(WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.WorkforceCeiling.Reads.Should().Be(1, "the ceiling was asked, and passed");
    }

    [Theory]
    [MemberData(nameof(DistinctRoutes))]
    public async Task ASystemuser_IsNeverChecked_EvenWhenTheRoleCannotBeRead(string verb, string path, string body)
    {
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Me,
            SystemUserId = Guid.NewGuid(),
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var response = await Send(verb, path, body);

        (await CeilingRouteTestData.ReasonCode(response)).Should().NotBe(WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }

    [Fact]
    public async Task AnUnsupportedMessageRoot_ResolvesToNoSet_AndTheHandlersOwn404Answers_WithoutARoleRead()
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var response = await Send("GET", $"/documents/{Item}/messages", None);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }

    [Fact]
    public async Task TheExemptRoutes_AreNotChecked_ForAWorkforceContact()
    {
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("role_missing");

        using var client = _fixture.CreateAuthenticatedClient();
        using var me = await client.GetAsync("/api/v1/external/me");
        using var seen = await client.PostAsync("/api/v1/external/notifications/seen", null);
        using var metadata = await client.GetAsync("/api/v1/external/api/dataverse/metadata/sprk_project");

        foreach (var response in new[] { me, seen, metadata })
            (await CeilingRouteTestData.ReasonCode(response)).Should().NotBe(WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.WorkforceCeiling.Reads.Should().Be(0);
    }

    [Fact]
    public async Task TheNotificationFeed_UnderAnUnreadableRole_Is200_AndEmpty()
    {
        // The feed filters rather than refuses (WorkforceCeilingInHandler); an unavailable role reads nothing.
        _fixture.Principal = CeilingRouteTestData.WorkforceContact(Me);
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        using var response = await _fixture.CreateAuthenticatedClient().GetAsync("/api/v1/external/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var feed = await response.Content.ReadFromJsonAsync<ExternalNotificationFeedResponse>();
        feed!.Value.Should().BeEmpty();
        _fixture.WorkforceCeiling.Reads.Should().Be(1);
    }

    [Fact]
    public void TheFixtureDefault_IsEveryRoutesPrivilege()
    {
        // The owner table (WorkforceCeilingDouble.OwnerPrerequisite) is exactly the union of what the routes declare, the
        // message and contact-grant roots, every module the seam serves, and what /me/entitlements intersects with. A route
        // that needs more fails here first.
        var union = new Dictionary<string, AccessRights>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<WorkforceCeilingRequirement>? requirements)
        {
            foreach (var r in requirements ?? Array.Empty<WorkforceCeilingRequirement>())
                union[r.Table] = union.GetValueOrDefault(r.Table) | r.Rights;
        }

        var endpoints = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/external/", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        endpoints.Should().HaveCountGreaterThan(50);
        foreach (var declaration in endpoints.Select(e => e.Metadata.GetMetadata<WorkforceCeilingMetadata>()))
        {
            if (declaration?.Coverage == WorkforceCeilingCoverage.Route)
                Add(declaration.Requirements);
        }

        foreach (var root in ExternalMessageEndpoints.Roots.Values)
        {
            Add(WorkforceRoleCeilingGate.ReadMessagesOn(root.EntityLogicalName));
            Add(WorkforceRoleCeilingGate.PostMessageOn(root.EntityLogicalName));
        }

        foreach (var recordType in new[] { "project", "matter", "workassignment" })
            Add(WorkforceRoleCeilingGate.GrantAsContact(recordType));

        foreach (var module in _fixture.Services.GetRequiredService<ExternalModuleRegistry>().Modules)
            Add(WorkforceRoleCeilingGate.ReadModuleRecords(module.RecordEntity));

        Add(WorkforceRoleCeilingGate.SubmitServiceRequest);
        Add(WorkforceRoleCeilingGate.ModuleTables.Values.Select(t => new WorkforceCeilingRequirement(t, AccessRights.Read)));

        union.Should().BeEquivalentTo(WorkforceCeilingDouble.OwnerPrerequisite);
    }

    private Task<HttpResponseMessage> Send(string verb, string path, string body)
    {
        var client = _fixture.CreateAuthenticatedClient();
        var request = new HttpRequestMessage(new HttpMethod(verb), "/api/v1/external" + path)
        {
            Content = body switch
            {
                Json => JsonContent.Create(new { sprk_name = "Ceiling", sprk_body = "Ceiling", sprk_eventname = "Ceiling" }),
                File => new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "brief.pdf" } },
                "grant" => JsonContent.Create(new
                {
                    recordType = "project",
                    recordId = Project,
                    granteeEmail = "colleague@customer.test",
                    accessLevel = (int)ExternalAccessLevel.ViewOnly,
                }),
                "revoke" => JsonContent.Create(new { accessRecordId = Guid.NewGuid() }),
                _ => null,
            },
        };
        return client.SendAsync(request);
    }
}
