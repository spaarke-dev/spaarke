using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 008 (uac-r2 review 2026-11-13, conditions B and B1):
/// <c>POST /api/v1/external/{projects|matters|workassignments}/{id}/events</c> accepts an optional <c>eventType</c> from the
/// CLOSED set {"task"}, which binds the Task row of <c>sprk_eventtype_ref</c> (the ONE id Signals shares,
/// <see cref="EventTypeRef.Task"/>). Omitted is today's create exactly; any other value is 400 <c>sdap.event.type_not_allowed</c>
/// and nothing is created. A Dataverse-decided caller (a systemuser) must also hold AppendTo on the Task type row, asked app-only
/// naming the systemuser: no → the create's own 403; no answer → 503. A contact is not asked.
/// </summary>
/// <remarks>
/// Driven through the REAL routes, filters and handlers on <see cref="ExternalTodoScopeTestFixture"/>, as
/// <see cref="ExternalRootEventCreateContractTests"/> is. Doubles sit at module boundaries only: the principal, the data service's
/// virtual write (<c>CreateEventCallCount</c> is the load-bearing "nothing created"), the owner resolver and the caller's own
/// Dataverse access probe (which records every question asked, so "not probed" is asserted). The body the write would send is read
/// from the REAL <see cref="ExternalDataService.BuildEventCreatePayload"/> with the arguments the route handed the write.
/// Placement: <c>tests/integration/contract/**</c> (ADR-038, endpoint contract).
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalEventTypeCreateContractTests : IClassFixture<ExternalTodoScopeTestFixture>
{
    private static readonly Guid Project = Guid.Parse("a0080000-0000-4000-8000-00000000e001");
    private static readonly Guid Matter = Guid.Parse("a0080000-0000-4000-8000-00000000e002");
    private static readonly Guid WorkAssignment = Guid.Parse("a0080000-0000-4000-8000-00000000e003");
    private static readonly Guid SystemUserId = Guid.Parse("a0080000-0000-4000-8000-00000000e051");
    private static readonly Guid ContactId = Guid.Parse("a0080000-0000-4000-8000-00000000e0c1");

    /// <summary>The live Task row id, written out independently of the production constant (verified on spaarkedev1 2026-10-22).</summary>
    private const string LiveTaskEventTypeId = "124f5fc9-98ff-f011-8406-7c1e525abd8b";

    private const string EventCreatePrivilege = "prvCreatesprk_Event";
    private const string TypeNotAllowed = "sdap.event.type_not_allowed";
    private const string InsufficientRights = "sdap.access.deny.insufficient_rights";
    private const string DataverseUnavailable = "sdap.access.dataverse_unavailable";
    private const string EventTypeBind = "sprk_eventtype_ref@odata.bind";

    private readonly ExternalTodoScopeTestFixture _fixture;

    public ExternalEventTypeCreateContractTests(ExternalTodoScopeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
    }

    public static TheoryData<string, ExternalDataService.TodoRootKind, Guid> Roots() => new()
    {
        { "projects", ExternalDataService.TodoRootKind.Project, Project },
        { "matters", ExternalDataService.TodoRootKind.Matter, Matter },
        { "workassignments", ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment },
    };

    private static object Event(string? eventType = null) => eventType is null
        ? new { sprk_name = "Send the draft", sprk_duedate = "2026-12-01" }
        : new { sprk_name = "Send the draft", sprk_duedate = "2026-12-01", eventType };

    private static CallerPrincipal Holding(
        CallerPrincipalPlane plane, ExternalDataService.TodoRootKind kind, Guid rootId, AccessRights rights,
        Guid? systemUserId = null, bool dataverseDecides = false) => new()
        {
            Plane = plane,
            ContactId = ContactId,
            SystemUserId = systemUserId,
            Email = "caller@outside.test",
            ProjectAccess = kind == ExternalDataService.TodoRootKind.Project
                ? new[] { new CallerProjectAccess { ProjectId = rootId, Rights = rights } }
                : Array.Empty<CallerProjectAccess>(),
            MatterAccess = kind == ExternalDataService.TodoRootKind.Matter
                ? new Dictionary<Guid, AccessRights> { [rootId] = rights }
                : new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = kind == ExternalDataService.TodoRootKind.WorkAssignment
                ? new Dictionary<Guid, AccessRights> { [rootId] = rights }
                : new Dictionary<Guid, AccessRights>(),
            DataverseConfirmsWrites = dataverseDecides,
        };

    private static CallerPrincipal Collaborator(ExternalDataService.TodoRootKind kind, Guid rootId) =>
        Holding(CallerPrincipalPlane.CiamContact, kind, rootId, ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));

    /// <summary>A systemuser whose access Dataverse decides, with Read on the root, AppendTo there and the event privilege.</summary>
    private CallerPrincipal DataverseDecidedCreator(ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Probe.AnswerAppOnly(SystemUserId, EntitySetOf(kind), rootId, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(SystemUserId, EventCreatePrivilege);
        return Holding(CallerPrincipalPlane.Workforce, kind, rootId, AccessRights.Read, SystemUserId, dataverseDecides: true);
    }

    private static string EntitySetOf(ExternalDataService.TodoRootKind kind) =>
        ExternalDataService.TryGetRootBinding(kind)!.EntitySet;

    private static async Task<string?> ExtensionOf(HttpResponseMessage response, string name)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty(name, out var code) ? code.GetString() : null;
    }

    private Dictionary<string, object?> SentPayload()
    {
        var args = _fixture.Data.LastCreateEventArgs!.Value;
        return ExternalDataService.BuildEventCreatePayload(args.Kind, args.RootId, args.Request, args.OwningTeamId);
    }

    private bool TaskRowWasProbed() =>
        _fixture.Probe.AppOnlyCalls.Any(c => c.EntitySet == EventTypeRef.EntitySet && c.RecordId == EventTypeRef.Task);

    // ── B: "task" binds the Task type row; omitted binds no type ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_WithEventTypeTask_Is201_AndBindsTheTaskTypeRow(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Collaborator(kind, rootId);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event("task"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(1);
        var payload = SentPayload();
        payload.Should().ContainKey(EventTypeBind).WhoseValue.Should().Be($"/sprk_eventtype_refs({LiveTaskEventTypeId})");
        payload.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)).Should().Be(3,
            "the root, the owner and the type — never a second root");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_WithoutEventType_Is201_AndBindsNoType(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = Collaborator(kind, rootId);

        using var client = _fixture.CreateAuthenticatedClient();
        using var omitted = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event());
        omitted.StatusCode.Should().Be(HttpStatusCode.Created, await omitted.Content.ReadAsStringAsync());
        SentPayload().Should().NotContainKey(EventTypeBind, "omitted is today's create exactly: no type");

        using var explicitNull = await client.PostAsync($"/api/v1/external/{segment}/{rootId}/events",
            JsonContent.Create(new { sprk_name = "Send the draft", eventType = (string?)null }));
        explicitNull.StatusCode.Should().Be(HttpStatusCode.Created, await explicitNull.Content.ReadAsStringAsync());
        SentPayload().Should().NotContainKey(EventTypeBind, "an explicit null is the same as omitted");
        SentPayload().Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal)).Should().Be(2);

        _fixture.Data.CreateEventCallCount.Should().Be(2);
    }

    [Fact]
    public void EventTypeRef_Task_IsTheLiveTaskRow()
    {
        // The shared constant (Signals' DecisionExec aliases it) pinned to the live row id, written out independently.
        EventTypeRef.Task.Should().Be(Guid.Parse(LiveTaskEventTypeId));
        EventTypeRef.EntitySet.Should().Be("sprk_eventtype_refs");
    }

    // ── B (ADR-019): any other value is 400 with the stable errorCode, and nothing is created ────────────────────────

    [Theory]
    [InlineData("projects", "Task")]
    [InlineData("matters", "todo")]
    [InlineData("workassignments", "milestone")]
    [InlineData("matters", "")]
    [InlineData("workassignments", " task")]
    [InlineData("projects", LiveTaskEventTypeId)]
    public async Task CreateEvent_WithAnEventTypeOutsideTheClosedSet_Is400TypeNotAllowed_AndCreatesNothing(
        string segment, string eventType)
    {
        var (kind, rootId) = segment switch
        {
            "projects" => (ExternalDataService.TodoRootKind.Project, Project),
            "matters" => (ExternalDataService.TodoRootKind.Matter, Matter),
            _ => (ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment),
        };
        _fixture.Principal = Collaborator(kind, rootId);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event(eventType));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await ExtensionOf(response, "errorCode")).Should().Be(TypeNotAllowed);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
        _fixture.Ownership.Requests.Should().BeEmpty("a refused body costs no owner read");
    }

    [Fact]
    public void BuildEventCreatePayload_WithAnEventTypeOutsideTheClosedSet_Throws()
    {
        // Defence in depth: the payload builder refuses what the route already refused, so no future caller can write it.
        var request = new Sprk.Bff.Api.Api.ExternalAccess.Dtos.CreateExternalEventRequest { SprkName = "x", EventType = "todo" };

        var act = () => ExternalDataService.BuildEventCreatePayload(
            ExternalDataService.TodoRootKind.Matter, Matter, request, Guid.Parse("a0080000-0000-4000-8000-00000000e0aa"));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── B1: a Dataverse-decided caller must hold AppendTo on the Task type row ─────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_TaskByADataverseDecidedCallerWithAppendToOnTheTaskType_Is201_AndTheRowWasAskedAppOnly(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = DataverseDecidedCreator(kind, rootId);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task, AccessRights.Read | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event("task"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(1);
        _fixture.Probe.AppOnlyCalls.Should().Contain((SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task),
            "asked app-only, naming the caller's own systemuser");
        _fixture.Probe.Calls.Should().BeEmpty("never OBO on the external plane");
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_TaskByADataverseDecidedCallerWithoutAppendToOnTheTaskType_Is403TheCreateRefusal_AndCreatesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = DataverseDecidedCreator(kind, rootId);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task, AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event("task"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ExtensionOf(response, "reasonCode")).Should().Be(InsufficientRights, "the existing create refusal, unchanged");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
        _fixture.Ownership.Requests.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_TaskWhenDataverseGivesNoAnswerForTheTaskType_Is503Retryable_AndCreatesNothing(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        _fixture.Principal = DataverseDecidedCreator(kind, rootId);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task, null);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event("task"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await ExtensionOf(response, "reasonCode")).Should().Be(DataverseUnavailable);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Roots))]
    public async Task CreateEvent_UntypedByADataverseDecidedCaller_DoesNotAskAboutTheTaskType(
        string segment, ExternalDataService.TodoRootKind kind, Guid rootId)
    {
        // Must-not-fire: without "task" nothing binds the type row, so the row is not asked about — even if it would say no.
        _fixture.Principal = DataverseDecidedCreator(kind, rootId);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task, AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event());

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        TaskRowWasProbed().Should().BeFalse();
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle("only the target root, as before task 008");
    }

    [Theory]
    [InlineData(CallerPrincipalPlane.CiamContact)]
    [InlineData(CallerPrincipalPlane.Workforce)]
    public async Task CreateEvent_TaskByAContact_IsNotProbed_TheClosedSetIsTheAuthorization(CallerPrincipalPlane plane)
    {
        _fixture.Principal = Holding(plane, ExternalDataService.TodoRootKind.Matter, Matter,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/matters/{Matter}/events", Event("task"));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(1);
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("a contact is not asked anything");
        _fixture.Probe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateEvent_TaskByAViewOnlyCaller_Is403BeforeTheTypeIsConsidered_AndCreatesNothing()
    {
        // The type never widens the create: Read alone is refused by the root gate, task or not.
        _fixture.Principal = Holding(CallerPrincipalPlane.CiamContact, ExternalDataService.TodoRootKind.Matter, Matter,
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PostAsJsonAsync($"/api/v1/external/matters/{Matter}/events", Event("task"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ExtensionOf(response, "reasonCode")).Should().Be(InsufficientRights);
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    // ── Review round 1 (F2-1, FR-07): the event lists' canCreateTask is the Task create's own answer ─────────────────────

    private static async Task<(bool? CanCreate, bool? CanCreateTask)> ListFlags(HttpResponseMessage list)
    {
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        bool? Flag(string name) => json.RootElement.TryGetProperty(name, out var v) ? v.GetBoolean() : null;
        return (Flag("canCreate"), Flag("canCreateTask"));
    }

    private int TaskRowProbeCount() =>
        _fixture.Probe.AppOnlyCalls.Count(c => c.EntitySet == EventTypeRef.EntitySet && c.RecordId == EventTypeRef.Task);

    [Theory]
    [InlineData("projects", true)]
    [InlineData("projects", false)]
    [InlineData("matters", true)]
    [InlineData("matters", false)]
    [InlineData("workassignments", true)]
    [InlineData("workassignments", false)]
    public async Task List_CanCreateTask_ForADataverseDecidedCaller_IsAppendToOnTheTaskType_AndTheTaskCreateAgrees(
        string segment, bool holdsAppendToOnTheTaskType)
    {
        // uac-r2 addendum (a): the list and the create ask the ONE B1 check, so they agree both ways — denied is false on the list
        // AND 403 on the create; held is true AND 201. canCreate (an untyped Add event) is true either way.
        var (kind, rootId) = segment switch
        {
            "projects" => (ExternalDataService.TodoRootKind.Project, Project),
            "matters" => (ExternalDataService.TodoRootKind.Matter, Matter),
            _ => (ExternalDataService.TodoRootKind.WorkAssignment, WorkAssignment),
        };
        _fixture.Principal = DataverseDecidedCreator(kind, rootId);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task,
            holdsAppendToOnTheTaskType ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/{segment}/{rootId}/events");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var (canCreate, canCreateTask) = await ListFlags(list);
        canCreate.Should().BeTrue("an untyped event needs nothing of the Task row");
        canCreateTask.Should().Be(holdsAppendToOnTheTaskType);
        TaskRowProbeCount().Should().Be(1, "one Task-row probe per list call, app-only, naming the systemuser");
        _fixture.Probe.Calls.Should().BeEmpty("never OBO on the external plane");

        using var create = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/events", Event("task"));
        create.StatusCode.Should().Be(holdsAppendToOnTheTaskType ? HttpStatusCode.Created : HttpStatusCode.Forbidden,
            "the SPA offers a Task exactly when the create admits one");
    }

    [Fact]
    public async Task List_CanCreateTask_WhenTheTaskTypeProbeIsUnanswered_IsFalse_AndTheListStill200()
    {
        _fixture.Principal = DataverseDecidedCreator(ExternalDataService.TodoRootKind.Matter, Matter);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task, null);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        list.StatusCode.Should().Be(HttpStatusCode.OK, "an unanswered affordance probe never fails the list");
        var (canCreate, canCreateTask) = await ListFlags(list);
        canCreate.Should().BeTrue();
        canCreateTask.Should().BeFalse("unknown hides the Task action");
    }

    [Theory]
    [InlineData(CallerPrincipalPlane.CiamContact, ExternalAccessLevel.Collaborate, true)]
    [InlineData(CallerPrincipalPlane.CiamContact, ExternalAccessLevel.ViewOnly, false)]
    [InlineData(CallerPrincipalPlane.Workforce, ExternalAccessLevel.Collaborate, true)]
    public async Task List_CanCreateTask_ForAContact_EqualsCanCreate_WithoutProbing(
        CallerPrincipalPlane plane, ExternalAccessLevel level, bool expected)
    {
        _fixture.Principal = Holding(plane, ExternalDataService.TodoRootKind.Matter, Matter, ExternalAccessLevels.ToAccessRights(level));

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var (canCreate, canCreateTask) = await ListFlags(list);
        canCreate.Should().Be(expected);
        canCreateTask.Should().Be(canCreate, "for a contact the closed set is the authorization");
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("a contact is not asked anything");
    }

    [Fact]
    public async Task List_CanCreateTask_WhenCanCreateIsFalse_IsFalse_WithoutProbingTheTaskType()
    {
        // A Dataverse-decided caller with no AppendTo on the root: no create at all, so the Task row is not asked about — even
        // though it would say yes.
        _fixture.Probe.AnswerAppOnly(SystemUserId, EntitySetOf(ExternalDataService.TodoRootKind.Matter), Matter, AccessRights.Read)
            .HoldsAppOnly(SystemUserId, EventCreatePrivilege);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task, AccessRights.Read | AccessRights.AppendTo);
        _fixture.Principal = Holding(CallerPrincipalPlane.Workforce, ExternalDataService.TodoRootKind.Matter, Matter,
            AccessRights.Read, SystemUserId, dataverseDecides: true);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var (canCreate, canCreateTask) = await ListFlags(list);
        canCreate.Should().BeFalse();
        canCreateTask.Should().BeFalse();
        TaskRowProbeCount().Should().Be(0);
    }

    [Fact]
    public async Task TodoList_DoesNotCarryCanCreateTask()
    {
        // Must-not-fire: the field belongs to the event lists only; every other list omits it.
        _fixture.Principal = Collaborator(ExternalDataService.TodoRootKind.Matter, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/todos");

        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var (canCreate, canCreateTask) = await ListFlags(list);
        canCreate.Should().BeTrue();
        canCreateTask.Should().BeNull();
    }
}
