using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r4 task 014b (FR-35, FR-36, FR-37; owner Q-1–Q-7, S-1–S-4; uac-r2 review of task 014,
/// Q1–Q6, and its conditions of 2026-11-16): the event forms' BFF — the creatable event types on the event lists, the create's new
/// fields and rules, the per-row edit affordances, <c>GET</c> and <c>PATCH /api/v1/external/events/{id}</c>, and
/// <c>GET /api/v1/external/{root}/{id}/assignable-people</c>.
/// </summary>
/// <remarks>
/// Driven through the REAL routes, filters and handlers on <see cref="ExternalTodoScopeTestFixture"/>, as
/// <see cref="ExternalRootEventCreateContractTests"/> is. Doubles sit at module boundaries only: the principal, the data service's
/// virtual reads and writes (<c>CreateEventCallCount</c> and <c>EventUpdates</c> are the load-bearing "nothing written"), the
/// owner resolver, the caller's own Dataverse access probe (which records every question, so "asked once" and "not asked" are
/// asserted) and <see cref="RecordContactAccessService"/> (its rules — Restricted, Secure, the deny list, removed and inactive
/// contacts, a read fault — are pinned by <c>RecordContactAccessServiceTests</c>). Placement: <c>tests/integration/contract/**</c>
/// (ADR-038, endpoint contract).
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalEventFormsContractTests : IClassFixture<ExternalTodoScopeTestFixture>
{
    private static readonly Guid Matter = Guid.Parse("a0140000-0000-4000-8000-00000000e002");
    private static readonly Guid OtherMatter = Guid.Parse("a0140000-0000-4000-8000-00000000e012");
    private static readonly Guid Project = Guid.Parse("a0140000-0000-4000-8000-00000000e001");
    private static readonly Guid Event = Guid.Parse("a0140000-0000-4000-8000-0000000e0001");
    private static readonly Guid UnknownEvent = Guid.Parse("a0140000-0000-4000-8000-0000000e00ff");
    private static readonly Guid SystemUserId = Guid.Parse("a0140000-0000-4000-8000-00000000e051");
    private static readonly Guid ContactId = Guid.Parse("a0140000-0000-4000-8000-00000000e0c1");
    private static readonly Guid Colleague = Guid.Parse("a0140000-0000-4000-8000-00000000e0c2");
    private static readonly Guid Stranger = Guid.Parse("a0140000-0000-4000-8000-00000000e0c3");

    private static readonly Guid Reminder = Guid.Parse("a0140000-0000-4000-8000-0000000071e1");
    private static readonly Guid Retired = Guid.Parse("a0140000-0000-4000-8000-0000000071e2");

    private const string EventCreatePrivilege = "prvCreatesprk_Event";
    private const string EventSet = "sprk_events";
    private const string RecordUnavailable = "sdap.access.deny.record_unavailable";

    private readonly ExternalTodoScopeTestFixture _fixture;

    public ExternalEventFormsContractTests(ExternalTodoScopeTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.WorkforceCeiling.Reset();
        _fixture.Data.EventTypes.Add(new ExternalDataService.EventTypeOption(Reminder, "Reminder", RequiresDueDate: true, IsActive: true));
        _fixture.Data.EventTypes.Add(new ExternalDataService.EventTypeOption(Retired, "Retired", RequiresDueDate: false, IsActive: false));
    }

    // ── principals and records ───────────────────────────────────────────────────────────────────────────────────────

    private static CallerPrincipal Contact(params (Guid Matter, ExternalAccessLevel Level)[] matters) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = ContactId,
        Email = "caller@outside.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = matters.ToDictionary(m => m.Matter, m => ExternalAccessLevels.ToAccessRights(m.Level)),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal SystemUser(AccessRights onMatter) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = ContactId,
        SystemUserId = SystemUserId,
        Email = "caller@outside.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new Dictionary<Guid, AccessRights> { [Matter] = onMatter },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        DataverseConfirmsWrites = true,
    };

    /// <summary>The event, filed under <paramref name="matters"/>, readable by the single read with the given type and status.</summary>
    private void EventFiledUnder(Guid typeId, int status, params Guid[] matters)
    {
        _fixture.Data.EventTargets[Event] = new ExternalDataService.DeleteTarget(
            null, ContactId,
            matters.Select(m => new ExternalDataService.DeleteTargetParent(ExternalDataService.DocumentParentKind.Matter, m)).ToList(),
            InvoiceLinks: Array.Empty<Guid>());
        _fixture.Data.EventsById[Event] = Dto(typeId, status, matters);
    }

    private static ExternalEventDto Dto(Guid typeId, int status, params Guid[] matters) => new()
    {
        SprkEventid = Event.ToString(),
        SprkName = "Hearing",
        SprkStatus = status,
        EventTypeId = typeId.ToString(),
        EventTypeName = typeId == Reminder ? "Reminder" : "Task",
        Parents = matters.Select(m => new ExternalEventParent(ExternalDataService.TodoRootKind.Matter, m, "Matter name")).ToList(),
        InvoiceLinks = Array.Empty<Guid>(), // links to no invoice (the list hides a row whose links were not read)
    };

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static async Task<string?> Code(HttpResponseMessage response, string name = "errorCode")
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;
    }

    private static Task<HttpResponseMessage> Patch(HttpClient client, Guid id, object body) =>
        client.PatchAsync($"/api/v1/external/events/{id}", JsonContent.Create(body));

    private static Task<HttpResponseMessage> Create(HttpClient client, object body) =>
        client.PostAsJsonAsync($"/api/v1/external/matters/{Matter}/events", body);

    private int TypeRowProbes(Guid typeId) =>
        _fixture.Probe.AppOnlyCalls.Count(c => c.EntitySet == EventTypeRef.EntitySet && c.RecordId == typeId);

    // =====================================================================
    // FR-35: creatableEventTypes on the event lists
    // =====================================================================

    [Fact]
    public async Task List_ForACollaboratorContact_OffersEveryActiveType_WithItsDueDateRule_AndNoInactiveOne()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var types = (await Json(list)).GetProperty("creatableEventTypes").EnumerateArray()
            .Select(t => (Id: Guid.Parse(t.GetProperty("id").GetString()!), Due: t.GetProperty("requiresDueDate").GetBoolean()))
            .ToList();
        types.Should().BeEquivalentTo(new[] { (EventTypeRef.Task, false), (Reminder, true) });
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("a contact is not asked: an event type confers no access");
    }

    [Fact]
    public async Task List_ForAViewOnlyContact_OffersNoType_AndReadsNoTypeRow()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.ViewOnly));

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        var json = await Json(list);
        json.GetProperty("canCreate").GetBoolean().Should().BeFalse();
        json.GetProperty("creatableEventTypes").GetArrayLength().Should().Be(0);
        _fixture.Data.EventTypeReads.Should().Be(0);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task List_ForASystemUser_OffersOnlyTheTypesItHoldsAppendToOn_AskingEachRowOncePerRequest(bool onTask, bool onReminder)
    {
        _fixture.Principal = SystemUser(AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(SystemUserId, "sprk_matters", Matter, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(SystemUserId, EventCreatePrivilege);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, EventTypeRef.Task,
            onTask ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, Reminder,
            onReminder ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        var json = await Json(list);
        var offered = json.GetProperty("creatableEventTypes").EnumerateArray()
            .Select(t => Guid.Parse(t.GetProperty("id").GetString()!)).ToList();
        var expected = new List<Guid>();
        if (onTask) expected.Add(EventTypeRef.Task);
        if (onReminder) expected.Add(Reminder);
        offered.Should().BeEquivalentTo(expected);
        json.GetProperty("canCreateTask").GetBoolean().Should().Be(onTask, "canCreateTask is the Task row's place in the same set");

        // The call-count pin: ONE type read, each candidate row asked ONCE, the inactive row never.
        _fixture.Data.EventTypeReads.Should().Be(1);
        TypeRowProbes(EventTypeRef.Task).Should().Be(1);
        TypeRowProbes(Reminder).Should().Be(1);
        TypeRowProbes(Retired).Should().Be(0);
        _fixture.Probe.Calls.Should().BeEmpty("never OBO on the external plane");
    }

    [Fact]
    public async Task List_WhenTheTypeRowsCannotBeRead_OffersNoCreate_AndStill200()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.Data.EventTypesFault = true;

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await Json(list);
        json.GetProperty("canCreate").GetBoolean().Should().BeFalse("no type set, no create offered");
        json.GetProperty("creatableEventTypes").GetArrayLength().Should().Be(0);
    }

    // =====================================================================
    // FR-35/FR-36: the create's type, priority, description, due-date and assignee rules
    // =====================================================================

    [Fact]
    public async Task Create_WithAnActiveTypeId_Is201_AndWritesTheTypePriorityDescriptionAndFirstAssignee()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.ContactAccess.Answer = new RecordContactAccess.Known(new[] { new RecordAccessContact(Colleague, "Col League") });

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new
        {
            sprk_name = "Call back",
            sprk_duedate = "2026-12-01",
            eventTypeId = Reminder,
            sprk_description = "Ask about the draft",
            assignedToContactId = Colleague,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var args = _fixture.Data.LastCreateEventArgs!.Value;
        var payload = ExternalDataService.BuildEventCreatePayload(args.Kind, args.RootId, args.Request, args.OwningTeamId);
        payload["sprk_eventtype_ref@odata.bind"].Should().Be($"/sprk_eventtype_refs({Reminder})");
        payload["sprk_priority"].Should().Be(100000001, "Normal when not sent");
        payload["sprk_description"].Should().Be("Ask about the draft");
        payload["sprk_AssignedTo@odata.bind"].Should().Be($"/contacts({Colleague})");
        _fixture.ContactAccess.Calls.Should().Equal(new[] { (ExternalGrantRootType.Matter, Matter) }, "the ROUTE's record");
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("unknown")]
    public async Task Create_WithATypeOutsideTheActiveSet_Is400TypeNotAllowed_AndCreatesNothing(string which)
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        var typeId = which == "retired" ? Retired : Guid.NewGuid();

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventTypeId = typeId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("sdap.event.type_not_allowed");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_TaskAndADifferentTypeId_Is400_AndCreatesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventType = "task", eventTypeId = Reminder, sprk_duedate = "2026-12-01" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("sdap.event.type_not_allowed");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_ATypeThatRequiresADueDate_WithoutOne_Is400_WithOne_Is201()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var without = await Create(client, new { sprk_name = "x", eventTypeId = Reminder });
        without.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(without)).Should().Be("sdap.event.due_date_required");
        _fixture.Data.CreateEventCallCount.Should().Be(0);

        using var with = await Create(client, new { sprk_name = "x", eventTypeId = Reminder, sprk_duedate = "2026-12-01" });
        with.StatusCode.Should().Be(HttpStatusCode.Created, await with.Content.ReadAsStringAsync());

        // Must-not-fire: a type that does not require one creates without it.
        using var task = await Create(client, new { sprk_name = "x", eventTypeId = EventTypeRef.Task });
        task.StatusCode.Should().Be(HttpStatusCode.Created, await task.Content.ReadAsStringAsync());
        _fixture.Data.CreateEventCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Create_ABadPriorityOrALongDescription_Is400_AndCreatesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var priority = await Create(client, new { sprk_name = "x", eventType = "task", sprk_priority = 3 });
        using var description = await Create(client, new { sprk_name = "x", eventType = "task", sprk_description = new string('d', 2001) });
        using var atTheLimit = await Create(client, new { sprk_name = "x", eventType = "task", sprk_priority = 100000003, sprk_description = new string('d', 2000) });

        (await Code(priority)).Should().Be("sdap.event.priority_invalid");
        (await Code(description)).Should().Be("sdap.event.description_too_long");
        atTheLimit.StatusCode.Should().Be(HttpStatusCode.Created, "2,000 characters and Urgent are allowed");
        _fixture.Data.CreateEventCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_AnAssigneeOutsideTheAssignableSet_Is400_AndCreatesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.ContactAccess.Answer = new RecordContactAccess.Known(new[] { new RecordAccessContact(Colleague, "Col League") });

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventType = "task", assignedToContactId = Stranger });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("sdap.event.assignee_not_allowed");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
        _fixture.Ownership.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenTheAssignableSetIsUnknown_Is503_AndCreatesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.ContactAccess.Answer = new RecordContactAccess.Unknown(RecordContactAccessUnknownReason.GrantsUnreadable);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventType = "task", assignedToContactId = Colleague });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Code(response, "reasonCode")).Should().Be("sdap.event.assignees_unavailable");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_WithoutAnAssignee_DoesNotAskForTheAssignableSet()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventType = "task" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.ContactAccess.Calls.Should().BeEmpty();
        var args = _fixture.Data.LastCreateEventArgs!.Value;
        ExternalDataService.BuildEventCreatePayload(args.Kind, args.RootId, args.Request, args.OwningTeamId)
            .Should().NotContainKey("sprk_AssignedTo@odata.bind");
    }

    [Fact]
    public async Task Create_WhenTheTypeRowsCannotBeRead_Is503_AndCreatesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.Data.EventTypesFault = true;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventType = "task" });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Code(response, "reasonCode")).Should().Be("sdap.event.types_unavailable");
        _fixture.Data.CreateEventCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task Create_ByASystemUser_NeedsAppendToOnTheChosenTypeRow(bool holds, HttpStatusCode expected)
    {
        _fixture.Principal = SystemUser(AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(SystemUserId, "sprk_matters", Matter, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(SystemUserId, EventCreatePrivilege);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventTypeRef.EntitySet, Reminder,
            holds ? AccessRights.Read | AccessRights.AppendTo : AccessRights.Read);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Create(client, new { sprk_name = "x", eventTypeId = Reminder, sprk_duedate = "2026-12-01" });

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        TypeRowProbes(Reminder).Should().Be(1);
        TypeRowProbes(EventTypeRef.Task).Should().Be(0, "only the chosen row is asked");
        _fixture.Data.CreateEventCallCount.Should().Be(holds ? 1 : 0);
    }

    // =====================================================================
    // FR-36 / owner Q-7: GET /events/{id} — Read on EVERY parent, else the uniform 404
    // =====================================================================

    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly, false)]
    [InlineData(ExternalAccessLevel.Collaborate, true)]
    [InlineData(ExternalAccessLevel.FullAccess, true)]
    public async Task Get_AReadableEvent_Is200_WithItsRegarding_AndTheEditAffordancesOfThePatch(ExternalAccessLevel level, bool canEdit)
    {
        _fixture.Principal = Contact((Matter, level));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/events/{Event}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await Json(response);
        json.GetProperty("regarding").GetProperty("kind").GetString().Should().Be("matter");
        json.GetProperty("regarding").GetProperty("id").GetString().Should().Be(Matter.ToString());
        json.GetProperty("canEdit").GetBoolean().Should().Be(canEdit);
        json.GetProperty("canChangeStatus").GetBoolean().Should().Be(canEdit);
        var allowed = json.GetProperty("allowedStatuses").EnumerateArray().Select(s => s.GetInt32()).ToList();
        if (canEdit)
            allowed.Should().NotContain(EventStatusCode.Open).And.Contain(EventStatusCode.Completed);
        else
            allowed.Should().BeEmpty();

        // canEdit agrees with the PATCH.
        using var patch = await Patch(client, Event, new { sprk_priority = 100000002 });
        patch.StatusCode.Should().Be(canEdit ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_AnUnknownId_AndAnEventWithOneUnreadableParent_AreTheSameUniform404()
    {
        // uac-r2 Q6: denied and not-found cannot be told apart.
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter, OtherMatter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var denied = await client.GetAsync($"/api/v1/external/events/{Event}");
        using var unknown = await client.GetAsync($"/api/v1/external/events/{UnknownEvent}");

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ShouldBeTheSame404(denied, unknown);
    }

    [Fact]
    public async Task Get_WhenEveryParentIsReadable_ButTheReadWithholdsTheEvent_IsTheUniform404()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);
        _fixture.Data.EventsById.Clear(); // the list rules (visibility, invoice chain) hide it

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/events/{Event}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Code(response, "reasonCode")).Should().Be(RecordUnavailable);
    }

    [Fact]
    public async Task Get_WhenTheEventRowCannotBeRead_Is503_NotA404()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);
        _fixture.Data.EventTargetFault = true;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/events/{Event}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Code(response, "reasonCode")).Should().Be("sdap.event.unavailable");
    }

    private static async Task ShouldBeTheSame404(HttpResponseMessage a, HttpResponseMessage b)
    {
        static async Task<string> Shape(HttpResponseMessage r)
        {
            var json = await Json(r);
            return string.Join("|", json.EnumerateObject()
                .Where(p => p.Name is not ("correlationId" or "traceId"))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value}"));
        }

        (await Shape(a)).Should().Be(await Shape(b));
        (await Code(a, "reasonCode")).Should().Be(RecordUnavailable);
    }

    // =====================================================================
    // FR-36: PATCH /events/{id} — the edit right, re-checked every time (uac-r2 Q1)
    // =====================================================================

    [Fact]
    public async Task Patch_ByAViewOnlyReader_Is403EventUpdate_AndWritesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.ViewOnly));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_name = "Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(response, "reasonCode")).Should().Be("sdap.access.deny.event_update");
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExternalAccessLevel.Collaborate)]
    [InlineData(ExternalAccessLevel.FullAccess)]
    public async Task Patch_ByAnEditor_Is204_AndWritesOnlyTheSentFields(ExternalAccessLevel level)
    {
        _fixture.Principal = Contact((Matter, level));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_name = "Renamed", sprk_priority = 100000003 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        _fixture.Data.EventUpdates.Should().ContainSingle().Which.Payload.Should().BeEquivalentTo(
            new Dictionary<string, object?> { ["sprk_eventname"] = "Renamed", ["sprk_priority"] = 100000003 });
    }

    [Theory]
    [InlineData(EventStatusCode.Completed)]
    [InlineData(EventStatusCode.Cancelled)]
    [InlineData(EventStatusCode.OnHold)]
    [InlineData(EventStatusCode.Draft)]
    public async Task Patch_AnyStatus_IsWrittenWithItsStatecode(int status)
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_status = status });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var payload = _fixture.Data.EventUpdates.Single().Payload;
        payload["statuscode"].Should().Be(status);
        payload["statecode"].Should().Be(EventStatusCode.GetStateCode(status));
    }

    [Fact]
    public async Task Patch_ABodyThatSetsNothingEditable_Is400UpdateEmpty_AndReadsNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PatchAsync($"/api/v1/external/events/{Event}",
            new StringContent("{\"sprk_issecure\":true}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("sdap.event.update_empty");
        _fixture.Data.EventTargetReads.Should().BeEmpty();
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_AnUndefinedStatus_Is400_AndWritesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_status = 42 });

        (await Code(response)).Should().Be("sdap.event.status_invalid");
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_WithAnEventTypeId_Is400TypeImmutable_BeforeAnyRead_AndWritesNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_name = "x", eventTypeId = Reminder });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(response)).Should().Be("sdap.event.type_immutable");
        _fixture.Data.EventTargetReads.Should().BeEmpty("a body refused on its shape reads nothing");
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_WithAnAssignee_Is400AssigneeUpdateUnsupported_AndWritesNothing()
    {
        // uac-r2 Q4 / owner a-b-c pending: the PATCH writes no assignee yet, even one in the assignable set.
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        _fixture.ContactAccess.Answer = new RecordContactAccess.Known(new[] { new RecordAccessContact(Colleague, "Col League") });
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var inSet = await Patch(client, Event, new { assignedToContactId = Colleague });
        using var outOfSet = await Patch(client, Event, new { sprk_name = "x", assignedToContactId = Stranger });

        (await Code(inSet)).Should().Be("sdap.event.assignee_update_unsupported");
        outOfSet.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _fixture.Data.EventUpdates.Should().BeEmpty();
        _fixture.Data.EventTargetReads.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_AnUnknownId_AndAnEventWithOneUnreadableParent_AreTheSameUniform404_AndWriteNothing()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter, OtherMatter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var denied = await Patch(client, Event, new { sprk_name = "x" });
        using var unknown = await Patch(client, UnknownEvent, new { sprk_name = "x" });

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ShouldBeTheSame404(denied, unknown);
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_WriteOnOneParent_ReadOnTheOther_Is403_AndWritesNothing()
    {
        // Every parent the event is filed under, not just one (uac-r2 Q1).
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess), (OtherMatter, ExternalAccessLevel.ViewOnly));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter, OtherMatter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_name = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Code(response, "reasonCode")).Should().Be("sdap.access.deny.event_update");
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_HiddenByTheExternalVisibilityPredicate_IsTheUniform404_AndUndecided_Is503()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        _fixture.Data.ExternalVisibility = ExternalDataService.LiveParentDecision.Withheld;
        using var hidden = await Patch(client, Event, new { sprk_name = "x" });
        _fixture.Data.ExternalVisibility = ExternalDataService.LiveParentDecision.Unavailable;
        using var undecided = await Patch(client, Event, new { sprk_name = "x" });

        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        undecided.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccessRights.Read | AccessRights.Write, HttpStatusCode.NoContent)]
    [InlineData(AccessRights.Read, HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.ServiceUnavailable)]
    public async Task Patch_ByASystemUserWithParentWrite_AlsoNeedsDataverseWriteOnTheEventRow(AccessRights? onRow, HttpStatusCode expected)
    {
        _fixture.Principal = SystemUser(AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(SystemUserId, "sprk_matters", Matter, AccessRights.Read | AccessRights.Write);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventSet, Event, onRow);
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_name = "x" });

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        _fixture.Data.EventUpdates.Should().HaveCount(expected == HttpStatusCode.NoContent ? 1 : 0);
        _fixture.Probe.AppOnlyCalls.Should().Contain((SystemUserId, EventSet, Event), "the ROW is asked, app-only");
        _fixture.Probe.Calls.Should().BeEmpty("never OBO on the external plane");
    }

    [Fact]
    public async Task Patch_ByASystemUserWithoutParentWrite_Is403_AndTheRowIsNotAsked()
    {
        _fixture.Principal = SystemUser(AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(SystemUserId, "sprk_matters", Matter, AccessRights.Read);
        _fixture.Probe.AnswerAppOnly(SystemUserId, EventSet, Event, AccessRights.Read | AccessRights.Write);
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await Patch(client, Event, new { sprk_name = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Probe.AppOnlyCalls.Should().NotContain(c => c.EntitySet == EventSet, "row Write never stands in for the parents");
        _fixture.Data.EventUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Patch_ABodyCarryingOwnershipOrSecurityColumns_WritesOnlyTheAllowListedOnes()
    {
        // uac-r2 2026-11-16 condition 3: ownership, the ancestor stamp, sprk_accesspermission and sprk_issecure are never written.
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PatchAsync($"/api/v1/external/events/{Event}", new StringContent(
            "{\"sprk_name\":\"x\",\"ownerid@odata.bind\":\"/teams(00000000-0000-0000-0000-000000000001)\",\"sprk_issecure\":true," +
            "\"sprk_accesspermission\":1,\"sprk_coreancestor\":\"y\",\"statecode\":1}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        _fixture.Data.EventUpdates.Single().Payload.Keys.Should().Equal("sprk_eventname");
    }

    [Theory]
    [InlineData(true, HttpStatusCode.BadRequest)]
    [InlineData(false, HttpStatusCode.NoContent)]
    public async Task Patch_ClearingTheDueDate_IsRefusedOnlyWhenTheEventsTypeRequiresOne(bool requiring, HttpStatusCode expected)
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        EventFiledUnder(requiring ? Reminder : EventTypeRef.Task, EventStatusCode.Open, Matter);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.PatchAsync($"/api/v1/external/events/{Event}",
            new StringContent("{\"sprk_duedate\":null}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        if (requiring)
        {
            (await Code(response)).Should().Be("sdap.event.due_date_required");
            _fixture.Data.EventUpdates.Should().BeEmpty();
        }
        else
        {
            _fixture.Data.EventUpdates.Single().Payload.Should().ContainKey("sprk_duedate").WhoseValue.Should().BeNull();
        }
    }

    // =====================================================================
    // FR-36: the list's per-row affordances agree with the PATCH
    // =====================================================================

    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly, false)]
    [InlineData(ExternalAccessLevel.Collaborate, true)]
    public async Task List_CanEditPerRow_AgreesWithThePatch(ExternalAccessLevel level, bool canEdit)
    {
        _fixture.Principal = Contact((Matter, level));
        EventFiledUnder(EventTypeRef.Task, EventStatusCode.Open, Matter);
        _fixture.Data.Events.Add(Dto(EventTypeRef.Task, EventStatusCode.Open, Matter));

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");
        var row = (await Json(list)).GetProperty("value")[0];
        row.GetProperty("canEdit").GetBoolean().Should().Be(canEdit);
        row.GetProperty("canChangeStatus").GetBoolean().Should().Be(canEdit);
        row.GetProperty("allowedStatuses").GetArrayLength().Should().Be(canEdit ? EventStatusCode.All.Count - 1 : 0);

        using var patch = await Patch(client, Event, new { sprk_status = EventStatusCode.Completed });
        patch.StatusCode.Should().Be(canEdit ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_CanEdit_ForARowAlsoFiledUnderARecordTheCallerOnlyReads_IsFalse()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.FullAccess), (OtherMatter, ExternalAccessLevel.ViewOnly));
        _fixture.Data.Events.Add(Dto(EventTypeRef.Task, EventStatusCode.Open, Matter, OtherMatter));

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/matters/{Matter}/events");

        (await Json(list)).GetProperty("value")[0].GetProperty("canEdit").GetBoolean().Should().BeFalse();
    }

    // =====================================================================
    // FR-37 / owner Q-5: GET /{root}/{id}/assignable-people
    // =====================================================================

    [Fact]
    public async Task AssignablePeople_Known_Is200_NamesOnly_WithIsMe()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.ContactAccess.Answer = new RecordContactAccess.Known(new[]
        {
            new RecordAccessContact(ContactId, "Me Myself"),
            new RecordAccessContact(Colleague, "Col League"),
        });

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Matter}/assignable-people");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var people = (await Json(response)).GetProperty("value").EnumerateArray().ToList();
        people.Select(p => (p.GetProperty("contactId").GetString(), p.GetProperty("name").GetString(), p.GetProperty("isMe").GetBoolean()))
            .Should().Equal((ContactId.ToString(), "Me Myself", true), (Colleague.ToString(), "Col League", false));
        people.Should().OnlyContain(p => p.EnumerateObject().Select(x => x.Name).SequenceEqual(new[] { "contactId", "name", "isMe" }),
            "names only — never an email");
        _fixture.ContactAccess.Calls.Should().Equal(new[] { (ExternalGrantRootType.Matter, Matter) });
    }

    [Fact]
    public async Task AssignablePeople_KnownEmpty_Is200Empty()
    {
        // A Restricted record (RecordContactAccessServiceTests pins that it is Known with nobody).
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Matter}/assignable-people");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("value").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task AssignablePeople_Unknown_Is503_NeverAnEmptyList()
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.Collaborate));
        _fixture.ContactAccess.Answer = new RecordContactAccess.Unknown(RecordContactAccessUnknownReason.GrantsUnreadable);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{Matter}/assignable-people");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Code(response, "reasonCode")).Should().Be("sdap.event.assignees_unavailable");
    }

    [Theory]
    [InlineData("view-only")]
    [InlineData("outside")]
    public async Task AssignablePeople_WithoutTheCreateOrEditRight_Is403_AndAsksNothing(string who)
    {
        _fixture.Principal = Contact((Matter, ExternalAccessLevel.ViewOnly));
        var record = who == "outside" ? OtherMatter : Matter;

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{record}/assignable-people");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        if (who == "view-only")
            (await Code(response, "reasonCode")).Should().Be("sdap.access.deny.event_assign");
        _fixture.ContactAccess.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignablePeople_OnAProject_AsksForTheProjectsSet()
    {
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = ContactId,
            Email = "caller@outside.test",
            ProjectAccess = new[]
            {
                new CallerProjectAccess
                {
                    ProjectId = Project, Rights = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
                },
            },
            MatterAccess = new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
        };

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/projects/{Project}/assignable-people");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.ContactAccess.Calls.Should().Equal(new[] { (ExternalGrantRootType.Project, Project) });
    }

    // =====================================================================
    // Pure: the payload builders, the read columns and the type set
    // =====================================================================

    [Fact]
    public void BuildEventUpdatePayload_EmitsOnlyTheSentAllowListedColumns()
    {
        var request = JsonSerializer.Deserialize<UpdateExternalEventRequest>(
            "{\"sprk_name\":\"x\",\"sprk_description\":\"\",\"sprk_status\":659490002,\"sprk_duedate\":\"2026-12-01\","
            + "\"ownerid\":\"o\",\"sprk_issecure\":true}")!;

        var payload = ExternalDataService.BuildEventUpdatePayload(request);

        payload.Keys.Should().BeSubsetOf(ExternalDataService.EventUpdateColumns);
        payload.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sprk_eventname"] = "x",
            ["sprk_description"] = null,
            ["statuscode"] = EventStatusCode.Completed,
            ["statecode"] = EventStatusCode.GetStateCode(EventStatusCode.Completed),
            ["sprk_duedate"] = "2026-12-01",
        });
        ExternalDataService.EventUpdateColumns.Should().BeEquivalentTo(
            "sprk_eventname", "sprk_duedate", "sprk_priority", "sprk_description", "statuscode", "statecode");
    }

    [Theory]
    [InlineData("{\"eventTypeId\":\"a0140000-0000-4000-8000-0000000071e1\"}")]
    [InlineData("{\"assignedToContactId\":\"a0140000-0000-4000-8000-00000000e0c2\"}")]
    [InlineData("{\"sprk_name\":\" \"}")]
    [InlineData("{\"sprk_priority\":7}")]
    [InlineData("{\"sprk_status\":42}")]
    public void BuildEventUpdatePayload_RefusesWhatTheRouteRefuses(string body)
    {
        var request = JsonSerializer.Deserialize<UpdateExternalEventRequest>(body)!;

        var act = () => ExternalDataService.BuildEventUpdatePayload(request);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EventReadColumns_AddTheFormFields_AndNoSecurityOrPersonColumn()
    {
        // The read allow-list (sprk_event has no external module, so its columns are pinned here — uac-r2 review C, Q5).
        var select = ExternalDataService.EventSelect().Split(',');

        select.Should().Contain(new[] { "sprk_description", "sprk_priority", "_sprk_assignedto_value", "_sprk_eventtype_ref_value" });
        select.Where(c => c.Contains("email", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        select.Should().NotContain(new[] { "sprk_issecure", "sprk_accesspermission", "_ownerid_value" });
        ExternalDataService.BuildEventByIdQuery(Event).Should().Contain(Uri.EscapeDataString($"sprk_eventid eq {Event}"));
    }

    [Fact]
    public void CreatableEventTypes_IsActiveOnly_NarrowedByConfiguration_AndByAppendTo()
    {
        var types = new[]
        {
            new ExternalDataService.EventTypeOption(EventTypeRef.Task, "Task", false, true),
            new ExternalDataService.EventTypeOption(Reminder, "Reminder", true, true),
            new ExternalDataService.EventTypeOption(Retired, "Retired", false, false),
        };

        ExternalDataService.CreatableEventTypes(types, null, null).Select(t => t.Id)
            .Should().Equal(EventTypeRef.Task, Reminder);
        ExternalDataService.CreatableEventTypes(types, new HashSet<Guid> { Reminder, Retired }, null).Select(t => t.Id)
            .Should().Equal(Reminder);
        ExternalDataService.CreatableEventTypes(types, null, new Dictionary<Guid, bool?> { [EventTypeRef.Task] = null, [Reminder] = true })
            .Select(t => t.Id).Should().Equal(new[] { Reminder }, "an unanswered row is left out");
    }

    [Fact]
    public void AllowedEventTypeIds_IsNullWhenUnset_AndFailsClosedOnBadEntries()
    {
        static IConfiguration Config(params string[] values) => new ConfigurationBuilder().AddInMemoryCollection(
            values.Select((v, i) => new KeyValuePair<string, string?>($"ExternalAccess:EventTypes:Allowed:{i}", v))).Build();

        ExternalDataService.AllowedEventTypeIds(Config()).Should().BeNull();
        ExternalDataService.AllowedEventTypeIds(Config(Reminder.ToString(), "not-a-guid")).Should().BeEquivalentTo(new[] { Reminder });
        ExternalDataService.AllowedEventTypeIds(Config("not-a-guid")).Should().BeEmpty("a list of only bad entries allows nothing");
    }
}
