using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using WireShape = Sprk.Bff.Api.Tests.TestInfrastructure.CallerRecordAccessProbeDouble.WireShape;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 002 (FR-08, FR-06): <c>POST /api/v1/external/matters/{id}/events</c> and
/// <c>POST /api/v1/external/workassignments/{id}/events</c>, copied from the project event create — the same filters, DTO,
/// service and codes — and the <c>canCreate</c> each root's event and to-do lists now report, so the SPA offers Add only
/// where the create route admits the caller.
/// </summary>
/// <remarks>
/// Driven through the REAL routes, filters and handlers on <see cref="ExternalTodoScopeTestFixture"/>. Doubles sit at module
/// boundaries only: the caller principal, <see cref="ExternalDataService"/>'s virtual write (the load-bearing
/// <c>CreateEventCallCount</c>: a 403 alone would pass even if the write had been issued), the owner resolver, the caller's
/// own Dataverse access probe and the workforce role read. The payload the write would send is read from the REAL
/// <see cref="ExternalDataService.BuildEventCreatePayload"/> with the arguments the route handed the write, so a wrong root
/// bind fails here rather than in Dataverse. Placement: <c>tests/integration/contract/**</c> (ADR-038, new endpoints).
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalRootEventCreateContractTests : IClassFixture<ExternalTodoScopeTestFixture>
{
    private static readonly Guid Matter = Guid.Parse("a0020000-0000-4000-8000-00000000000a");
    private static readonly Guid WorkAssignment = Guid.Parse("a0020000-0000-4000-8000-00000000000b");
    private static readonly Guid Outside = Guid.Parse("a0020000-0000-4000-8000-0000000000ff");
    private static readonly Guid SystemUserId = Guid.Parse("a0020000-0000-4000-8000-000000000051");
    private static readonly Guid ContactId = Guid.Parse("a0020000-0000-4000-8000-0000000000c1");

    private const string EventCreatePrivilege = "prvCreatesprk_Event";
    private const string InsufficientRights = "sdap.access.deny.insufficient_rights";
    private const string DataverseUnavailable = "sdap.access.dataverse_unavailable";

    private readonly ExternalTodoScopeTestFixture _fixture;

    public ExternalRootEventCreateContractTests(ExternalTodoScopeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    /// <summary>Each new route: its segment, the root kind it must hand the write, the record, and the bind it must send.</summary>
    public static TheoryData<string, ExternalDataService.TodoRootKind, Guid, string, string> Roots() => new()
    {
        { "matters", ExternalDataService.TodoRootKind.Matter, Matter, "sprk_RegardingMatter@odata.bind", "sprk_matters" },
        { "workassignments", ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment, "sprk_RegardingWorkAssignment@odata.bind", "sprk_workassignments" },
    };

    public static TheoryData<string, ExternalDataService.TodoRootKind, Guid> Segments() => new()
    {
        { "matters", ExternalDataService.TodoRootKind.Matter, Matter },
        { "workassignments", ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment },
    };

    private static object ValidEvent() => new { sprk_name = "Hearing", sprk_duedate = "2026-12-01" };

    /// <summary>A CIAM partner holding <paramref name="level"/> on <paramref name="rootId"/> of <paramref name="kind"/>.</summary>
    private static CallerPrincipal Partner(ExternalDataService.TodoRootKind kind, Guid rootId, ExternalAccessLevel level) =>
        Holding(CallerPrincipalPlane.CiamContact, kind, rootId, ExternalAccessLevels.ToAccessRights(level));

    private static CallerPrincipal Holding(
        CallerPrincipalPlane plane, ExternalDataService.TodoRootKind kind, Guid rootId, AccessRights rights,
        Guid? systemUserId = null, bool dataverseDecides = false) => new()
        {
            Plane = plane,
            ContactId = ContactId,
            SystemUserId = systemUserId,
            Email = "caller@outside.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = kind == ExternalDataService.TodoRootKind.Matter
                ? new Dictionary<Guid, AccessRights> { [rootId] = rights }
                : new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = kind == ExternalDataService.TodoRootKind.WorkAssignment
                ? new Dictionary<Guid, AccessRights> { [rootId] = rights }
                : new Dictionary<Guid, AccessRights>(),
            DataverseConfirmsWrites = dataverseDecides,
        };

    private static string EntitySetOf(ExternalDataService.TodoRootKind kind) =>
        ExternalDataService.TryGetRootBinding(kind)!.EntitySet;

    private static async Task<string?> ReasonCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }

    // ── 201: the event is created under the ROUTE's root, owned by the root's team ──────────────────────────────

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_CollaboratorOnTheRoot_Is201_BindsTheRouteRoot_AndTheRootsOwner(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId, string bindKey, string entitySet)
    {
        _fixture.Principal = Partner(kind, rootId, ExternalAccessLevel.Collaborate);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<ExternalEventDto>();
        created!.SprkName.Should().Be("Hearing");
        response.Headers.Location!.OriginalString.Should().Be($"/api/v1/external/{segment}/{rootId}/events/{created.SprkEventid}");

        _fixture.Data.CreateEventCallCount.Should().Be(1);
        var args = _fixture.Data.LastCreateEventArgs!.Value;
        args.Kind.Should().Be(kind, "the root kind comes from the ROUTE, never the body");
        args.RootId.Should().Be(rootId);
        args.OwningTeamId.Should().Be(_fixture.Ownership.TeamId!.Value, "task 146: owned by the root's team");
        args.Creator.ContactId.Should().Be(ContactId, "FR-25: the creator stamp is the resolved principal");
        _fixture.Ownership.Requests.Should().ContainSingle().Which.Should().Match<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext>(
            c => c.TargetEntityLogicalName == ExternalDataService.TryGetRootBinding(kind)!.EntityLogicalName && c.TargetRecordId == rootId);

        // The body the write sends: ONE root bind, to the route's record, and the owner bind.
        var payload = ExternalDataService.BuildEventCreatePayload(args.Kind, args.RootId, args.Request, args.OwningTeamId);
        payload.Should().ContainKey(bindKey).WhoseValue.Should().Be($"/{entitySet}({rootId})");
        payload.Should().ContainKey(ExternalDataService.OwnerBindKey).WhoseValue.Should().Be($"/teams({args.OwningTeamId})");
        payload.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)).Should().Be(2,
            "one root bind and the owner — never a second root");
        payload["sprk_duedate"].Should().Be("2026-12-01");
    }

    // ── 403: ViewOnly (no Create), and a root outside the caller's set — nothing written ───────────────────────

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_ViewOnlyOnTheRoot_Is403InsufficientRights_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Partner(kind, rootId, ExternalAccessLevel.ViewOnly);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be(InsufficientRights);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
        _fixture.Ownership.Requests.Should().BeEmpty("the owner is resolved only after the caller is admitted");
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_OnARootOutsideTheCallersSet_Is403_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Partner(kind, rootId, ExternalAccessLevel.FullAccess);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{Outside}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateEvent_AMatterGrant_DoesNotAdmitAWorkAssignmentWithTheSameId_AndWritesNothing()
    {
        // The rights are per root TYPE: Collaborate on matter X is no right on work assignment X.
        _fixture.Principal = Partner(ExternalDataService.TodoRootKind.Matter, Matter, ExternalAccessLevel.Collaborate);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/workassignments/{Matter}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_Unauthenticated_Is401_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Partner(kind, rootId, ExternalAccessLevel.Collaborate);

        using var client = _fixture.CreateUnauthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    // ── 400: the project create's body checks, unchanged ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_WithoutAName_Is400_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Partner(kind, rootId, ExternalAccessLevel.Collaborate);

        using var client = _fixture.CreateAuthenticatedClient();
        using var noName = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", new { sprk_duedate = "2026-12-01" });
        using var badStatus = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", new { sprk_name = "x", sprk_status = 2 });
        using var badDate = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", new { sprk_name = "x", sprk_duedate = "next week" });

        noName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badStatus.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
        _fixture.Ownership.Requests.Should().BeEmpty("a malformed body costs no owner read");
    }

    // ── 409: no owner can be resolved for the root — refused, nothing written ───────────────────────────────

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_WhenTheRootsOwnerIsRefused_Is409_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Partner(kind, rootId, ExternalAccessLevel.Collaborate);
        _fixture.Ownership.TeamId = null;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    // ── Dataverse-decided callers (FR-20): confirmed on the TARGET root; an unanswered question is a 503 ────────

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_DataverseDecidedCaller_WithAppendToAndTheEventPrivilege_Is201(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, kind, rootId, AccessRights.Read, SystemUserId, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(SystemUserId, EventCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(1);
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle().Which.Should().Match<(Guid, string EntitySet, Guid RecordId)>(
            c => c.EntitySet == EntitySetOf(kind) && c.RecordId == rootId, "the TARGET root is asked about");
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (SystemUserId, EventCreatePrivilege) });
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_DataverseDecidedCaller_WithAppendToButNoEventPrivilege_Is403_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, kind, rootId, AccessRights.Read, SystemUserId, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be(InsufficientRights);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_DataverseGivesNoAnswer_Is503Retryable_NotA403_AndWritesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, kind, rootId, AccessRights.Read, SystemUserId, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(WireShape.ServerFault);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await ReasonCode(response)).Should().Be(DataverseUnavailable);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    // ── workforce ceiling: the project create's pair (event Create, root AppendTo) ───────────────────────────

    [Theory]
    [InlineData("matters", WorkforceRoleCeilingGate.EventTable, AccessRights.Create)]
    [InlineData("matters", WorkforceRoleCeilingGate.MatterTable, AccessRights.AppendTo)]
    [InlineData("workassignments", WorkforceRoleCeilingGate.EventTable, AccessRights.Create)]
    [InlineData("workassignments", WorkforceRoleCeilingGate.WorkAssignmentTable, AccessRights.AppendTo)]
    public async Task CreateEvent_WorkforceContactWhoseRoleLacksThePrivilege_Is403Ceiling_AndWritesNothing(
        string segment, string table, AccessRights removed)
    {
        var (kind, rootId) = segment == "matters"
            ? (ExternalDataService.TodoRootKind.Matter, Matter)
            : (ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment);
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, kind, rootId,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));
        _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(table, removed);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        await CeilingRouteTestData.ShouldBeCeilingDenial(response, WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Segments))]
    public async Task CreateEvent_WorkforceContactWhoseRoleHoldsThePair_Is201(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, kind, rootId,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", ValidEvent());

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(1);
    }

    // ── canCreate on the root's event and to-do lists (FR-06): the create route's own answer ────────────────

    [Theory]
    [InlineData("matters", "events", ExternalAccessLevel.Collaborate, true)]
    [InlineData("matters", "events", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("matters", "todos", ExternalAccessLevel.Collaborate, true)]
    [InlineData("matters", "todos", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("workassignments", "events", ExternalAccessLevel.Collaborate, true)]
    [InlineData("workassignments", "events", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("workassignments", "todos", ExternalAccessLevel.Collaborate, true)]
    [InlineData("workassignments", "todos", ExternalAccessLevel.ViewOnly, false)]
    public async Task List_ReportsCanCreate_AndTheCreateRouteAgrees(
        string segment, string child, ExternalAccessLevel level, bool expected)
    {
        var (kind, rootId) = segment == "matters"
            ? (ExternalDataService.TodoRootKind.Matter, Matter)
            : (ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment);
        _fixture.Principal = Partner(kind, rootId, level);

        using var client = _fixture.CreateAuthenticatedClient();
        var raw = await client.GetStringAsync($"/api/v1/external/{segment}/{rootId}/{child}");
        using var json = JsonDocument.Parse(raw);
        json.RootElement.GetProperty("canCreate").GetBoolean().Should().Be(expected);

        using var create = await client.PostAsJsonAsync(
            $"/api/v1/external/{segment}/{rootId}/{child}",
            child == "events" ? ValidEvent() : new { sprk_name = "new to-do" });
        create.StatusCode.Should().Be(expected ? HttpStatusCode.Created : HttpStatusCode.Forbidden,
            "the SPA offers exactly what the create route allows");
    }

    // Each list reads its OWN create's ceiling: removing Create on sprk_event hides Add event but not Add to-do, and the
    // reverse, so a list wired to the other child's ceiling fails here. Removing the root's AppendTo hides both.
    [Theory]
    [InlineData("events", null, true)]
    [InlineData("todos", null, true)]
    [InlineData("events", WorkforceRoleCeilingGate.MatterTable, false)]
    [InlineData("todos", WorkforceRoleCeilingGate.MatterTable, false)]
    [InlineData("events", WorkforceRoleCeilingGate.EventTable, false)]
    [InlineData("todos", WorkforceRoleCeilingGate.EventTable, true)]
    [InlineData("todos", WorkforceRoleCeilingGate.TodoTable, false)]
    [InlineData("events", WorkforceRoleCeilingGate.TodoTable, true)]
    public async Task List_ForAWorkforceContact_CanCreate_FollowsTheCreateRoutesCeiling(
        string child, string? tableLosingItsWriteRight, bool roleHoldsCreate)
    {
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, ExternalDataService.TodoRootKind.Matter, Matter,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));
        if (tableLosingItsWriteRight is { } table)
        {
            // The list needs Read on the root and the child; the create also needs AppendTo on the root and Create on the
            // child — remove only that write right, so the list itself still answers.
            _fixture.WorkforceCeiling.Ceiling = CeilingRouteTestData.Without(
                table, table == WorkforceRoleCeilingGate.MatterTable ? AccessRights.AppendTo : AccessRights.Create);
        }

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/{child}");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("canCreate").GetBoolean().Should().Be(roleHoldsCreate,
            "a create the ceiling refuses is not offered");

        using var create = await client.PostAsJsonAsync(
            $"/api/v1/external/matters/{Matter}/{child}",
            child == "events" ? ValidEvent() : new { sprk_name = "new to-do" });
        create.StatusCode.Should().Be(roleHoldsCreate ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("todos")]
    public async Task List_WhenTheWorkforceRoleCannotBeRead_IsTheListsOwnCeilingDenial_NotACanCreateAnswer(string child)
    {
        // Pins the order: the list's own ceiling refuses first, so canCreate never has to answer an unreadable role.
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, ExternalDataService.TodoRootKind.Matter, Matter,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));
        _fixture.WorkforceCeiling.Ceiling = WorkforceRoleCeiling.Unavailable("read_failed");

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/{child}");

        await CeilingRouteTestData.ShouldBeCeilingDenial(list, WorkforceRoleCeilingGate.DenyBaselineUnavailable);
        _fixture.WorkforceCeiling.Reset();
    }
}
