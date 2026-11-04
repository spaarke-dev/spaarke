using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EventStatusCode = Spaarke.Dataverse.EventStatusCode;

namespace Sprk.Bff.Api.Tests.Api.Events;

/// <summary>
/// The per-record authorization of the <c>/api/v1/events</c> routes, exercised through the REAL
/// <c>MapEventEndpoints</c> (unified-access-control-r2 task 159, #1098).
/// </summary>
/// <remarks>
/// <para><b>What is real and what is substituted.</b> The endpoint mapper, the validation filters,
/// <c>RecordRouteAccessAuthorizationFilter</c>, <see cref="OperationAccessPolicy"/> and
/// <see cref="CoreAncestorResolver"/> are the production types. Substituted: <see cref="CallerRecordAccessProbe"/> at
/// its virtual seams (a recording subclass — the task 130 <c>RecordingPrivilegeProbe</c> pattern; the real probe where a
/// test says so), and the Dataverse service seams the handlers sit on (<see cref="IEventDataverseService"/>,
/// <see cref="ICallerSystemUserResolver"/>, <see cref="ICommunicationDataverseService"/>, <see cref="IGenericEntityService"/>
/// and the resolver's column probe). No HTTP handler is mocked (ADR-038). Live behaviour is the main session's gate.</para>
/// <para><b>Four routes are gone.</b> PUT /{id}, DELETE /{id}, POST /{id}/cancel and GET /{id}/logs had no caller and
/// no published description, so task 159 deleted them (owner round 10 item 1);
/// <see cref="DeletedRoutes_AreNotMapped_AndReachNothing"/> keeps them deleted.</para>
/// <para><b>One route was added (task 147 r1c, owner round 36).</b> <c>PATCH /{id}/filing</c> is the event's ONE re-file
/// route: a shape filter (filing columns only, before any rights question), then 159's
/// <c>RecordRouteAccessAuthorizationFilter("write")</c> on <c>sprk_events({id})</c>, then the ONE re-file core (its own
/// tests: <c>SecureChildOwnershipAiToolTests.ChildRecordRoutes</c>). The deleted-route pins name exact verbs and paths,
/// so none of them matches it; two more rows pin that no general update came back with it.</para>
/// </remarks>
public class EventEndpointsAuthorizationContractTests
{
    private const string EventsSet = "sprk_events";            // live EntityDefinitions, 2026-10-03
    private const string MattersSet = "sprk_matters";          // live
    private const string AnalysesSet = "sprk_analysises";      // live — NOT "sprk_analysiss"
    private const string CreateEventPrivilege = "prvCreatesprk_Event"; // live privileges(name), 2026-10-03

    private static readonly Guid CallerSystemUserId = Guid.Parse("5ca11e40-0000-4000-8000-000000000159");
    private static readonly Guid ProjectRecordTypeRef = Guid.Parse("ca68b3bb-8600-f111-8407-7c1e520aa4df"); // live row

    // =========================================================================================
    // Routes
    // =========================================================================================

    /// <summary>The two /{id} routes left after the deletions.</summary>
    public static TheoryData<string> IdRoutes => new() { "get", "complete" };

    private static HttpRequestMessage IdRequest(string route, Guid id) => route switch
    {
        "get" => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/events/{id}"),
        "complete" => new HttpRequestMessage(HttpMethod.Post, $"/api/v1/events/{id}/complete"),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static HttpRequestMessage CreateRequest(object body) =>
        new(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(body) };

    // =========================================================================================
    // GET /api/v1/events — the query runs AS the caller
    // =========================================================================================

    [Fact]
    public async Task List_ResolvedCaller_QueryRunsAsThatCaller_OwnerNarrowedToTheSameId_AppOnlyQueryNeverCalled()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var captured = host.CaptureCallerScopedQuery(totalCount: 7);

        var response = await host.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/events?status=open"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().ContainSingle();
        captured[0].Caller.Should().Be(CallerSystemUserId, "the query runs AS the caller's resolved systemuserid");
        captured[0].Owner.Should().Be(CallerSystemUserId, "the kept owner narrowing uses the same resolved id");
        // Owner decision B (task 097): "my events" is owner OR assigned contact OR created-by person — the created-by
        // branch is what keeps a team-owned (I-6) event the BFF created visible to the person who created it.
        captured[0].Mine!.CreatedByPersonId.Should().Be(CallerSystemUserId);
        captured[0].Mine!.AssignedToContactId.Should().BeNull("this caller has no linked contact");
        captured[0].Status.Should().Be(EventStatusCode.Open, "the alias maps to the LIVE Open statuscode");
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["totalCount"]!.GetValue<int>()
            .Should().Be(7, "TotalCount is the trimmed query's own count");
        host.Events.Verify(e => e.QueryEventsAsync(
            It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(),
            It.IsAny<IReadOnlyCollection<int>?>(), It.IsAny<CancellationToken>()), Times.Never);    }

    [Fact]
    public async Task List_ACallerWithALinkedContact_ListsWhatIsAssignedToThatContact()
    {
        // Owner decision B's assigned branch at the ROUTE (task 097 round 9; review mutation M3): the contact the identity
        // service links to the caller reaches the query — an event assigned to the caller's contact is theirs.
        var linkedContact = Guid.Parse("c0c0c0c0-0097-4097-8097-000000000009");
        await using var host = await EventsAuthHost.StartAsync();
        host.Identity.Setup(i => i.ResolveAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(CallerSystemUserId, ContactId: linkedContact));
        var captured = host.CaptureCallerScopedQuery(totalCount: 1);

        var response = await host.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/events"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var mine = captured.Should().ContainSingle().Subject.Mine!;
        mine.AssignedToContactId.Should().Be(linkedContact);
        mine.OwnerUserId.Should().Be(CallerSystemUserId);
        mine.CreatedByPersonId.Should().Be(CallerSystemUserId);

    }

    public static TheoryData<string> UnresolvedCallers => new()
    {
        "no-oid-claim", "no-matching-systemuser", "lookup-failed", "resolver-throws", "unparseable-id", "empty-id",
    };

    [Theory]
    [MemberData(nameof(UnresolvedCallers))]
    public async Task List_UnresolvedCaller_Is403CallerUnresolved_AndNoEventQueryRuns(string shape)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var setup = host.Caller.Setup(c => c.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()));
        switch (shape)
        {
            case "resolver-throws": setup.ThrowsAsync(new InvalidOperationException("dataverse down")); break;
            case "unparseable-id": setup.ReturnsAsync(CallerSystemUserResolution.Resolved("not-a-guid")); break;
            case "empty-id": setup.ReturnsAsync(CallerSystemUserResolution.Resolved(Guid.Empty.ToString("D"))); break;
            default: setup.ReturnsAsync(CallerSystemUserResolution.Unresolved(shape)); break;
        }

        var response = await host.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/events"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        json["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.caller_unresolved");
        json["correlationId"].Should().NotBeNull();
        host.Events.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("x' or sprk_regardingrecordid ne 'zz")]
    [InlineData("abc")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task List_NonGuidRegardingRecordId_Is400_AndNothingIsQueried(string regardingRecordId)
    {
        await using var host = await EventsAuthHost.StartAsync();

        var response = await host.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/events?regardingRecordId={Uri.EscapeDataString(regardingRecordId)}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Events.VerifyNoOtherCalls();
        host.Caller.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_TypeWithoutId_FiltersOnTheRecordTypeRow_AndAMissingRowIsAnEmptyPageWithNoQuery()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var captured = host.CaptureCallerScopedQuery(totalCount: 3);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_recordtype_ref", ProjectRecordTypeRef));
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_budget", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity?)null);

        var withRow = await host.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/events?regardingRecordType=0"));
        var withoutRow = await host.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/events?regardingRecordType=7"));

        withRow.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().ContainSingle("the type with no sprk_recordtype_ref row issued no query");
        captured[0].RecordTypeRef.Should().Be(ProjectRecordTypeRef);
        withoutRow.StatusCode.Should().Be(HttpStatusCode.OK);
        (await withoutRow.Content.ReadFromJsonAsync<JsonObject>())!["totalCount"]!.GetValue<int>().Should().Be(0);
    }

    // =========================================================================================
    // The /{id} routes — no Read is the uniform 404; Read without the route's right is 403
    // =========================================================================================

    [Theory]
    [MemberData(nameof(IdRoutes))]
    public async Task IdRoute_CallerWithoutRead_GetsTheUniform404_AndNoEventServiceIsCalled(string route)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete);

        var response = await host.SendAsync(IdRequest(route, eventId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["reasonCode"]!.GetValue<string>()
            .Should().Be("sdap.access.deny.record_unavailable");
        host.Probe.RightsCalls.Should().Contain((EventsSet, eventId, true));
        host.Events.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("complete")]
    public async Task WriteRoute_CallerWithReadOnly_Gets403InsufficientRights_AndNothingIsWritten(string route)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Read | AccessRights.Delete);

        var response = await host.SendAsync(IdRequest(route, eventId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.insufficient_rights");
        body.Should().NotContain(eventId.ToString());
        host.Events.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(IdRoutes))]
    public async Task IdRoute_AbsentUnreadableFaultedAndHandlerNotFound_AreByteIdenticalAndNeverEchoTheId(string route)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var absent = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        var vanished = Guid.NewGuid();
        host.Probe.Grant(EventsSet, unreadable, AccessRights.Write | AccessRights.AppendTo);
        host.Probe.Grant(EventsSet, vanished, AccessRights.Read | AccessRights.Write | AccessRights.Append);
        host.Events.Setup(e => e.GetEventAsync(vanished, It.IsAny<CancellationToken>())).ReturnsAsync((EventEntity?)null);

        var bodies = new List<(Guid Id, string Body)>();
        foreach (var id in new[] { absent, unreadable, vanished })
        {
            var response = await host.SendAsync(IdRequest(route, id));
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            bodies.Add((id, await response.Content.ReadAsStringAsync()));
        }

        host.Probe.ThrowOnEveryCall = new HttpRequestException("RetrievePrincipalAccess unavailable");
        var faulted = Guid.NewGuid();
        var faultedResponse = await host.SendAsync(IdRequest(route, faulted));
        faultedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        bodies.Add((faulted, await faultedResponse.Content.ReadAsStringAsync()));

        bodies.Select(b => Normalize(b.Body)).Distinct().Should().ContainSingle(
            "absent, unreadable, probe-fault and the handler's own not-found must be indistinguishable");
        foreach (var (id, body) in bodies)
        {
            body.Should().NotContain(id.ToString()).And.NotContainEquivalentOf(id.ToString("N"));
        }

        var json = JsonNode.Parse(bodies[0].Body)!.AsObject();
        json["title"]!.GetValue<string>().Should().Be("Not Found");
        json["detail"]!.GetValue<string>().Should().Be("The requested record was not found.");
        json["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
    }

    [Fact]
    public async Task GetById_ReaderGets200WithTheEvent()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Read);
        host.SetupEvent(eventId, EventStatusCode.Open);

        var response = await host.SendAsync(IdRequest("get", eventId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>().Should().Be(eventId);
    }

    [Theory]
    [InlineData("complete", EventStatusCode.Completed, EventLogAction.Completed)]
    public async Task Complete_WriterGets200_WithTheLiveStatusAndALogRow(string route, int newStatus, int logAction)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Read | AccessRights.Write);
        host.SetupEvent(eventId, EventStatusCode.Open);
        host.Events.Setup(e => e.UpdateEventStatusAsync(eventId, newStatus, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        host.Events.Setup(e => e.CreateEventLogAsync(eventId, logAction, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var response = await host.SendAsync(IdRequest(route, eventId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Events.Verify(e => e.UpdateEventStatusAsync(eventId, newStatus, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("complete")]
    public async Task Complete_InvalidTransitionIs400ForAWriter_ButTheUniform404ForACallerWithoutRead(string route)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var writable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        host.Probe.Grant(EventsSet, writable, AccessRights.Read | AccessRights.Write);
        host.SetupEvent(writable, EventStatusCode.Completed);
        host.SetupEvent(unreadable, EventStatusCode.Completed);

        var forWriter = await host.SendAsync(IdRequest(route, writable));
        var forStranger = await host.SendAsync(IdRequest(route, unreadable));

        forWriter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        forStranger.StatusCode.Should().Be(HttpStatusCode.NotFound, "the transition check must not disclose the state of an event the caller cannot see");
        host.Events.Verify(e => e.GetEventAsync(unreadable, It.IsAny<CancellationToken>()), Times.Never);
        host.Events.Verify(e => e.UpdateEventStatusAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================================
    // POST /api/v1/events — check as the caller, write as the app
    // =========================================================================================

    [Fact]
    public async Task Create_WithoutTheCreatePrivilege_Is403InsufficientPrivilege_AndNothingIsWritten()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        host.Probe.Grant(MattersSet, matter, AccessRights.AppendTo);

        var response = await host.SendAsync(CreateRequest(new { subject = "Hearing", regardingRecordType = 1, regardingRecordId = matter }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>()
            .Should().Be("sdap.access.deny.insufficient_privilege");
        host.Probe.PrivilegeCalls.Should().Equal((CreateEventPrivilege, true));
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    [Fact]
    public async Task Create_WithThePrivilegeButNoAppendToOnTheRegarding_Is403_AndNoEventOrLogRowIsWritten()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(MattersSet, matter, AccessRights.Read | AccessRights.Write);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);

        var response = await host.SendAsync(CreateRequest(new { subject = "Hearing", regardingRecordType = 1, regardingRecordId = matter }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>()
            .Should().Be("sdap.access.deny.insufficient_rights");
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    [Fact]
    public async Task Create_UnknownDeniedAndSetLookupFault_AreByteIdentical403s()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var unknown = Guid.NewGuid();
        var denied = Guid.NewGuid();
        host.Probe.Grant(MattersSet, denied, AccessRights.Read);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_project", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata unavailable"));

        var bodies = new List<string>();
        foreach (var body in new object[]
                 {
                     new { subject = "a", regardingRecordType = 1, regardingRecordId = unknown },
                     new { subject = "b", regardingRecordType = 1, regardingRecordId = denied },
                     new { subject = "c", regardingRecordType = 0, regardingRecordId = Guid.NewGuid() },
                 })
        {
            var response = await host.SendAsync(CreateRequest(body));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            bodies.Add(Normalize(await response.Content.ReadAsStringAsync()));
        }

        bodies.Distinct().Should().ContainSingle("an unknown id, a denied one and a set-lookup fault must not be told apart");
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    [Fact]
    public async Task Create_AppendToIsAskedOfTheLiveEntitySet_AndThePayloadBindsThatSameSet_WithTheCoreStamp()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var analysis = Guid.NewGuid();
        var matter = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(AnalysesSet, analysis, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync(AnalysesSet);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        // The CHILD target's own core-ancestor lookup, read through the resolver's IGenericEntityService seam.
        host.Entities.Setup(e => e.RetrieveAsync("sprk_analysis", analysis, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_analysis", analysis) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) });
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Review", regardingRecordType = 3, regardingRecordId = analysis }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.RightsCalls.Should().Equal((AnalysesSet, analysis, true));
        var created = creates.Should().ContainSingle().Subject;
        // Task 031: the capture seam moved from the DTO to the service's create body; the entity set the create BINDS is read from
        // the bind path (the DTO's RegardingEntitySetName no longer exists on this route).
        var payload = created.Payload;
        ((string)payload["sprk_RegardingAnalysis@odata.bind"]!).Should().StartWith($"/{AnalysesSet}(", "the record authorized is the record bound");
        payload["sprk_RegardingAnalysis@odata.bind"].Should().Be($"/{AnalysesSet}({analysis:D})");
        payload["sprk_RegardingMatter@odata.bind"].Should().Be($"/{MattersSet}({matter:D})", "FR-26: the analysis's core ancestor is stamped");
        payload.Keys.Should().NotContain("sprk_RegardingRecordType@odata.bind", "this environment double has no record-type row for analysis");
    }

    [Fact]
    public async Task Create_ABodyNamingAContact_IsIgnored_TheRowNamesTheCallersLinkedContact()
    {
        // Task 031 (UAC-r2, 2026-11-04): sprk_assignedto is the server-set lookup that costs no AppendTo, so it must come from
        // the caller's identity on the server and never from the body. The create body has no contact field; every spelling
        // a client might try is dropped at binding, and the row names the caller's LINKED contact.
        var linkedContact = Guid.Parse("c0c0c0c0-0031-4031-8031-000000000031");
        var bodyContact = Guid.Parse("c0c0c0c0-0031-4031-8031-0000000000bb");
        await using var host = await EventsAuthHost.StartAsync();
        host.Identity.Setup(i => i.ResolveAsync(CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonIdentity(CallerSystemUserId, ContactId: linkedContact));
        var analysis = Guid.NewGuid();
        var matter = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(AnalysesSet, analysis, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync(AnalysesSet);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        host.Entities.Setup(e => e.RetrieveAsync("sprk_analysis", analysis, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_analysis", analysis) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) });
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new Dictionary<string, object>
        {
            ["subject"] = "Review",
            ["regardingRecordType"] = 3,
            ["regardingRecordId"] = analysis,
            ["assigneeContactId"] = bodyContact,
            ["assignedToContactId"] = bodyContact,
            ["sprk_assignedto"] = bodyContact,
            ["sprk_AssignedTo@odata.bind"] = $"/contacts({bodyContact:D})",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var payload = creates.Should().ContainSingle().Subject.Payload;
        payload["sprk_AssignedTo@odata.bind"].Should().Be($"/contacts({linkedContact:D})", "the caller's linked contact, resolved on the server");
        System.Text.Json.JsonSerializer.Serialize(payload).Should().NotContain(bodyContact.ToString("D"), "nothing the body names reaches the row");
    }

    /// <summary>
    /// Task 173 (owner round 81): an event filed under an analysis of a RESTRICTED matter is created showing Restricted —
    /// the value comes from <c>DeriveForHostAsync</c> and rides the same create body as the stamps.
    /// </summary>
    [Fact]
    public async Task Create_EventFiledUnderAnAnalysisOfARestrictedMatter_CarriesRestrictedInTheCreateBody()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var analysis = Guid.NewGuid();
        var matter = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(AnalysesSet, analysis, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync(AnalysesSet);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        var analysisRow = new Entity("sprk_analysis", analysis) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter) };
        host.Entities.Setup(e => e.RetrieveAsync("sprk_analysis", analysis, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisRow);
        var rows = new Dictionary<string, Entity>
        {
            ["sprk_analysis"] = analysisRow,
            ["sprk_matter"] = new Entity("sprk_matter", matter) { ["sprk_accesspermission"] = new OptionSetValue(100000002) },
        };
        host.Entities
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => rows.ContainsKey(q.EntityName)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) => new EntityCollection(new List<Entity> { rows[q.EntityName] }));
        host.ColumnProbe = (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
            CoreAncestorResolver.CoreAncestorLookups.Select(c => c.LookupAttribute).Append("sprk_accesspermission"),
            StringComparer.OrdinalIgnoreCase));
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Review", regardingRecordType = 3, regardingRecordId = analysis }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = creates.Should().ContainSingle().Subject;
        created.AccessPermission.Should().Be(100000002);
        created.Payload["sprk_accesspermission"].Should().Be(100000002,
            "the event shows the matter at the top of its filing, from the moment it exists");
    }

    /// <summary>
    /// Task 097 round 9: the regarding NUMBER column is the one the type's <c>sprk_recordtype_ref</c> row names
    /// (<c>sprk_regardingrecordnumberfield</c>, live spaarkedev1 values below) — every type, not only matter/project.
    /// </summary>
    [Theory]
    [InlineData(0, "sprk_project", "sprk_projects", "sprk_projectnumber", "PRJ-0097")]
    [InlineData(1, "sprk_matter", "sprk_matters", "sprk_matternumber", "MAT-0097")]
    [InlineData(2, "sprk_invoice", "sprk_invoices", "sprk_invoicenumber", "INV-0097")]
    [InlineData(3, "sprk_analysis", "sprk_analysises", "sprk_analysis_number", "AN-0097")]
    [InlineData(4, "account", "accounts", "accountnumber", "ACC-0097")]
    [InlineData(6, "sprk_workassignment", "sprk_workassignments", "sprk_workassignmentnumber", "WA-0097")]
    [InlineData(7, "sprk_budget", "sprk_budgets", "sprk_budgetnumber", "BUD-0097")]
    public async Task Create_WritesTheRegardingNumber_FromTheColumnTheCatalogRowNames(
        int type, string logicalName, string entitySet, string numberColumn, string number)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var target = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(entitySet, target, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync(logicalName, It.IsAny<CancellationToken>())).ReturnsAsync(entitySet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync(logicalName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(host.InCallerCatalog(logicalName, new Entity("sprk_recordtype_ref", Guid.NewGuid()) { [RegardingRecordType.RecordNumberFieldColumn] = numberColumn }));
        host.Entities.Setup(e => e.RetrieveAsync(logicalName, target, It.Is<string[]>(c => c.Contains(numberColumn)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(logicalName, target) { [numberColumn] = number });
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Numbered", regardingRecordType = type, regardingRecordId = target }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        creates.Should().ContainSingle().Which.RegardingRecordNumber.Should().Be(number);
    }

    [Fact]
    public async Task Create_ACatalogNamingAColumnTheRecordLacks_StillWritesTheServersName()
    {
        // Task 097 round 10: the combined name+number read faults on a column the record lacks; the name is read alone.
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant("sprk_matters", matter, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(host.InCallerCatalog("sprk_matter", new Entity("sprk_recordtype_ref", Guid.NewGuid()) { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_nosuchnumber" }));
        host.Entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.Is<string[]>(c => c.Contains("sprk_nosuchnumber")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("'sprk_matter' entity doesn't contain attribute with Name = 'sprk_nosuchnumber'"));
        host.Entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.Is<string[]>(c => c.Length == 1 && c[0] == "sprk_mattername"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matter) { ["sprk_mattername"] = "Acme Holdings" });
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new
        {
            subject = "Named", regardingRecordType = 1, regardingRecordId = matter, regardingRecordName = "MAT-100",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = creates.Should().ContainSingle().Subject;
        created.RegardingRecordName.Should().Be("Acme Holdings");
        created.RegardingRecordNumber.Should().BeNull();
    }
    // ── Round 11: the event create has no catalog cache, so a transient catalog fault is retried ONCE ────────────────

    private static void MatterWithNumber(EventsAuthHost host, Guid matter)
    {
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant("sprk_matters", matter, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync("sprk_matters");
        host.Entities.Setup(e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matter) { ["sprk_mattername"] = "Acme Holdings", ["sprk_matternumber"] = "MAT-0097" });
    }

    private static Entity MatterCatalogRow() =>
        new("sprk_recordtype_ref", Guid.NewGuid()) { [RegardingRecordType.RecordNumberFieldColumn] = "sprk_matternumber" };

    [Fact]
    public async Task Create_ACatalogTimeout_IsRetriedOnce_AndTheNumberIsStillWritten()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNumber(host, matter);
        host.RecordTypes.SetupSequence(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("catalog timed out"))
            .ReturnsAsync(host.InCallerCatalog("sprk_matter", MatterCatalogRow()));
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Retried", regardingRecordType = 1, regardingRecordId = matter }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        creates.Should().ContainSingle().Which.RegardingRecordNumber.Should().Be("MAT-0097");
        host.RecordTypes.Verify(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_APersistentCatalogTimeout_LeavesTheNumberEmpty_AndTheCreateStillSucceeds()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNumber(host, matter);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("catalog timed out"));
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Unnumbered", regardingRecordType = 1, regardingRecordId = matter }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = creates.Should().ContainSingle().Subject;
        created.RegardingRecordNumber.Should().BeNull();
        created.RegardingRecordName.Should().Be("Acme Holdings");
        host.RecordTypes.Verify(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()), Times.Exactly(2),
            "once, then exactly one retry");
    }

    [Fact]
    public async Task Create_APermanentCatalogFault_IsNotRetried()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var matter = Guid.NewGuid();
        MatterWithNumber(host, matter);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("privilege check failed"));
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Not retried", regardingRecordType = 1, regardingRecordId = matter }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.RecordTypes.Verify(r => r.QueryRecordTypeRefAsync("sprk_matter", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TransientCatalogFault_IsATimeoutOrAThrottle_NeverTheCallersCancellationOrARejection()
    {
        var throttle = new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = -2147015902 }, "Number of requests exceeded the limit");
        var rejection = new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = -2147220960 }, "Principal user is missing prvReadsprk_recordtype_ref");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        EventEndpoints.IsTransientCatalogFault(new TimeoutException(), CancellationToken.None).Should().BeTrue();
        EventEndpoints.IsTransientCatalogFault(new TaskCanceledException("HttpClient timeout"), CancellationToken.None).Should().BeTrue();
        EventEndpoints.IsTransientCatalogFault(throttle, CancellationToken.None).Should().BeTrue();
        EventEndpoints.IsTransientCatalogFault(
            new HttpRequestException("429", null, HttpStatusCode.TooManyRequests), CancellationToken.None).Should().BeTrue();
        EventEndpoints.IsTransientCatalogFault(rejection, CancellationToken.None).Should().BeFalse();
        EventEndpoints.IsTransientCatalogFault(new InvalidOperationException(), CancellationToken.None).Should().BeFalse();
        EventEndpoints.IsTransientCatalogFault(new TaskCanceledException(), cancelled.Token).Should().BeFalse();
    }
    [Fact]
    public async Task Create_ACatalogRowThatNamesNoNumberColumn_WritesNoNumber()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var contact = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant("contacts", contact, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("contact", It.IsAny<CancellationToken>())).ReturnsAsync("contacts");
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(host.InCallerCatalog("contact", new Entity("sprk_recordtype_ref", Guid.NewGuid()))); // live: contact names no number column
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Call", regardingRecordType = 5, regardingRecordId = contact }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        creates.Should().ContainSingle().Which.RegardingRecordNumber.Should().BeNull("never guessed");
    }
    [Fact]
    public async Task Create_WithNoRegarding_NeedsOnlyThePrivilege()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();

        var response = await host.SendAsync(CreateRequest(new { subject = "Standalone" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.RightsCalls.Should().BeEmpty();
        // Task 031: the old DTO's RegardingRecordType == null is, in the create body, no regarding column of any kind.
        creates.Should().ContainSingle().Which.Payload.Keys.Should().NotContain(
            k => k.StartsWith("sprk_regarding", StringComparison.OrdinalIgnoreCase), "an event filed under nothing carries no regarding column");
    }

    [Fact]
    public async Task Create_WhenTheEventLogWriteFails_StillAnswers201_BecauseTheEventExists()
    {
        // The event is written before its log row. A failed log (on dev it was a 400 for a column sprk_eventlog lacks)
        // must not turn a successful create into a 500 that the caller retries into a duplicate event.
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        var creates = host.CaptureCreates();
        host.Events.Setup(e => e.CreateEventLogAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 400 (Bad Request)."));

        var response = await host.SendAsync(CreateRequest(new { subject = "Log write fails" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        creates.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_HalfARegardingIs400_BeforeAnyCheck()
    {
        await using var host = await EventsAuthHost.StartAsync();

        var response = await host.SendAsync(CreateRequest(new { subject = "x", regardingRecordId = Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Probe.PrivilegeCalls.Should().BeEmpty();
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    [Fact]
    public async Task Create_CoreStampFailure_WritesNothing_And500sWithoutTheId()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var analysis = Guid.NewGuid();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(AnalysesSet, analysis, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync(AnalysesSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        host.ColumnProbe = (_, _) => throw new InvalidOperationException("metadata unavailable"); // the resolver fails closed (Error)
        var creates = host.CaptureCreates(); // task 031: the request reaches the service (as the caller), which refuses it

        var response = await host.SendAsync(CreateRequest(new { subject = "Review", regardingRecordType = 3, regardingRecordId = analysis }));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(analysis.ToString());
        JsonNode.Parse(body)!["errorCode"]!.GetValue<string>().Should().Be("events.regarding_stamp_failed");
        creates.Should().BeEmpty("task 031: the service wrote nothing - not the row, not an owner");
        host.AppOnlyCreates.Should().BeEmpty();
        host.Publisher.Published.Should().BeEmpty("no membership event for a row that was never created");
        host.Events.Verify(e => e.CreateEventLogAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================================
    // Fail closed and authentication
    // =========================================================================================

    [Theory]
    [InlineData("get")]
    [InlineData("complete")]
    [InlineData("create")]
    public async Task FailClosed_NoBearerToken_TheRealProbeDenies_AndNoEventServiceIsCalled(string route)
    {
        await using var host = await EventsAuthHost.StartAsync(useRealProbe: true);
        var request = route == "create"
            ? CreateRequest(new { subject = "x", regardingRecordType = 1, regardingRecordId = Guid.NewGuid() })
            : IdRequest(route, Guid.NewGuid());

        var response = await host.SendAsync(request, withToken: false);

        response.StatusCode.Should().Be(route == "create" ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound);
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    [Fact]
    public async Task FailClosed_PrivilegeCheckThrows_CreateIs403()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.ThrowOnEveryCall = new HttpRequestException("Dataverse unavailable");

        var response = await host.SendAsync(CreateRequest(new { subject = "x" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    [Theory]
    [InlineData("GET", "/api/v1/events")]
    [InlineData("GET", "/api/v1/events/{id}")]
    [InlineData("POST", "/api/v1/events/{id}/complete")]
    [InlineData("POST", "/api/v1/events")]
    [InlineData("PATCH", "/api/v1/events/{id}/filing")]
    public async Task Unauthenticated_EveryRouteIs401(string verb, string path)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var request = new HttpRequestMessage(new HttpMethod(verb), path.Replace("{id}", Guid.NewGuid().ToString()));
        if (verb is "POST")
        {
            request.Content = JsonContent.Create(new { subject = "x" });
        }

        var response = await host.SendAsync(request, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Events.VerifyNoOtherCalls();
        host.AssertServiceNotReached();
    }

    /// <summary>
    /// The four routes task 159 deleted (owner round 10 item 1: no caller, not published) stay deleted: an authorized
    /// caller reaches no handler, no filter and no Dataverse seam on any of them. The last two rows (task 147 r1c, owner
    /// round 36): the re-file added PATCH /{id}/filing ONLY — no general update came back with it. (The rows sit directly
    /// above the method, with no comment line between them: RouteAuthorizationGuardTests reads this method's attribute
    /// lines as the absence pin of sweep S-11, S-14, S-15 and S-37.)
    /// </summary>
    [Theory]
    [InlineData("PUT", "/api/v1/events/{id}")]
    [InlineData("DELETE", "/api/v1/events/{id}")]
    [InlineData("POST", "/api/v1/events/{id}/cancel")]
    [InlineData("GET", "/api/v1/events/{id}/logs")]
    [InlineData("PATCH", "/api/v1/events/{id}")]
    [InlineData("PUT", "/api/v1/events/{id}/filing")]
    public async Task DeletedRoutes_AreNotMapped_AndReachNothing(string verb, string path)
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.Delete);
        var request = new HttpRequestMessage(new HttpMethod(verb), path.Replace("{id}", eventId.ToString()));
        if (verb is "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { subject = "x" });
        }

        var response = await host.SendAsync(request);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty("no handler or filter produced a body");
        host.Probe.RightsCalls.Should().BeEmpty();
        host.Events.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // PATCH /api/v1/events/{id}/filing — task 147 r1c (owner round 36): the re-file, in 159's family
    // =========================================================================================

    private static HttpRequestMessage FilingRequest(Guid id, object body) =>
        new(HttpMethod.Patch, $"/api/v1/events/{id}/filing") { Content = JsonContent.Create(body) };

    private static readonly Dictionary<string, object?> FilingBody = new()
    {
        ["sprk_RegardingMatter@odata.bind"] = "/sprk_matters(5ca11e40-0000-4000-8000-0000000000aa)",
        ["sprk_regardingrecordid"] = "5ca11e40-0000-4000-8000-0000000000aa",
    };

    [Fact]
    public async Task FilingRoute_ABodyNamingAnythingButTheFiling_Is400_BeforeAnyRightsQuestion()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();

        var response = await host.SendAsync(FilingRequest(eventId, new Dictionary<string, object?>
        {
            ["sprk_RegardingMatter@odata.bind"] = "/sprk_matters(5ca11e40-0000-4000-8000-0000000000aa)",
            ["statuscode"] = 2,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["reasonCode"]!.GetValue<string>()
            .Should().Be("child_record.not_filing");
        host.Probe.RightsCalls.Should().BeEmpty("a 400 is never turned into a 403 and discloses nothing about a record");
        host.FilingUser.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FilingRoute_CallerWithoutRead_GetsTheUniform404_AndTheReFileCoreNeverRuns()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Write | AccessRights.Append | AccessRights.AppendTo);

        var response = await host.SendAsync(FilingRequest(eventId, FilingBody));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["reasonCode"]!.GetValue<string>()
            .Should().Be("sdap.access.deny.record_unavailable");
        host.Probe.RightsCalls.Should().Contain((EventsSet, eventId, true));
        host.FilingUser.Invocations.Should().BeEmpty();
        host.Events.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FilingRoute_CallerWithReadButNotWrite_Is403InsufficientRights_AndTheReFileCoreNeverRuns()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Read | AccessRights.AppendTo);

        var response = await host.SendAsync(FilingRequest(eventId, FilingBody));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.insufficient_rights");
        body.Should().NotContain(eventId.ToString());
        host.FilingUser.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FilingRoute_NoBearerToken_TheRealProbeDenies_AndTheReFileCoreNeverRuns()
    {
        await using var host = await EventsAuthHost.StartAsync(useRealProbe: true);

        var response = await host.SendAsync(FilingRequest(Guid.NewGuid(), FilingBody), withToken: false);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.FilingUser.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task FilingRoute_AWriter_PassesTheFilters_IntoTheReFileCore_AsTheCaller()
    {
        await using var host = await EventsAuthHost.StartAsync();
        var eventId = Guid.NewGuid();
        host.Probe.Grant(EventsSet, eventId, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(FilingRequest(eventId, FilingBody));

        // The core's first question is the caller's own metadata read; the host's caller client answers 503, so the
        // response is the core's own failure — proof that the filters let a writer through to it, and that it asked as the
        // caller (IDataverseUserClient), never the event service.
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        host.FilingUser.Verify(
            u => u.GetAsync(It.Is<string>(p => p.StartsWith("EntityDefinitions(LogicalName='sprk_event')", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        host.Events.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // Helpers
    // =========================================================================================

    /// <summary>The body with its correlation/trace identifiers removed — the only fields allowed to differ.</summary>
    private static string Normalize(string problemJson)
    {
        var node = JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return node.ToJsonString();
    }

    internal sealed record CallerScopedQuery(Guid Caller, int? Type, Guid? RegardingId, Guid? RecordTypeRef, int? Status, Guid? Owner, EventOwnershipScope? Mine = null);

    /// <summary>
    /// <see cref="CallerRecordAccessProbe"/> at its virtual seams: answers from a rights table and a held-privilege set,
    /// and records every question. Like the real probe, a missing caller token answers None / "not held".
    /// </summary>
    internal sealed class RecordingProbe : CallerRecordAccessProbe
    {
        private readonly Dictionary<(string Set, Guid Id), AccessRights> _rights = new();
        private readonly HashSet<string> _held = new(StringComparer.Ordinal);

        public RecordingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public List<(string Set, Guid Id, bool HasToken)> RightsCalls { get; } = new();

        public List<(string Privilege, bool HasToken)> PrivilegeCalls { get; } = new();

        public Exception? ThrowOnEveryCall { get; set; }

        public void Grant(string set, Guid id, AccessRights rights) => _rights[(set, id)] = rights;

        public void Hold(string privilege) => _held.Add(privilege);

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            RightsCalls.Add((entitySet, recordId, !string.IsNullOrEmpty(callerBearerToken)));
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<AccessRights>(ThrowOnEveryCall);
            }

            return Task.FromResult(string.IsNullOrEmpty(callerBearerToken)
                ? AccessRights.None
                : _rights.GetValueOrDefault((entitySet, recordId), AccessRights.None));
        }

        public override Task<bool> CallerHoldsPrivilegeAsync(
            string? callerBearerToken, string privilegeName, CancellationToken ct = default)
        {
            PrivilegeCalls.Add((privilegeName, !string.IsNullOrEmpty(callerBearerToken)));
            if (ThrowOnEveryCall is not null)
            {
                return Task.FromException<bool>(ThrowOnEveryCall);
            }

            return Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && _held.Contains(privilegeName));
        }
    }

    /// <summary>A minimal host over the REAL <c>MapEventEndpoints</c>.</summary>
    internal sealed class EventsAuthHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public RecordingProbe Probe { get; } = new();
        public Mock<IEventDataverseService> Events { get; } = new(MockBehavior.Strict);
        public Mock<ICallerSystemUserResolver> Caller { get; } = new(MockBehavior.Strict);
        public Mock<ICommunicationDataverseService> RecordTypes { get; } = new(MockBehavior.Strict);
        public Mock<IGenericEntityService> Entities { get; } =
            OwnerEventTestKit.Dataverse(new EntityReference("systemuser", OwnerEventTestKit.ApplicationUserId));
        public Mock<IIdentityNormalizationService> Identity { get; } = new(MockBehavior.Strict);
        public RecordingMembershipEventPublisher Publisher { get; } = new();

        /// <summary>
        /// Task 147 r1c: the caller's own Dataverse client the filing route's re-file core asks first — strict; its metadata
        /// read answers 503, so a request that reaches the core ends there (the core's own tests drive it fully).
        /// </summary>
        public Mock<Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient> FilingUser { get; } = FilingUserClient();

        /// <summary>
        /// Field-mapping-server-write-path-r1 task 031 (FR-19): the caller's Dataverse client the REAL <see cref="RecordCreationService"/>
        /// asks (WhoAmI, its own as-caller checks and metadata). Registered behind <see cref="CallerClient"/>: the filing tests keep
        /// the strict <see cref="FilingUser"/>; <see cref="CaptureCreates"/> switches to this scripted caller, whose every question
        /// is counted so "the service was not reached" is asserted, not assumed.
        /// </summary>
        public SecureChildOwnershipAiToolTests.ScriptedUserClient ScriptedCaller { get; } = new(CallerSystemUserId);

        /// <summary>The user client the host registers: the strict filing client until a test lets the service create as the caller.</summary>
        public CountingUserClient CallerClient { get; }

        /// <summary>
        /// Task 031: every app-only create the service makes (its upsert of a client-chosen id), as the table, the id and the
        /// Web API body it sent. Replaces the old <c>IEventDataverseService.CreateEventAsync</c> capture: that create no longer
        /// exists on this route.
        /// </summary>
        public List<(string Table, Guid Id, Dictionary<string, object?> Fields)> AppOnlyCreates { get; } = new();

        /// <summary>Task 031: the service's app-only writer (loose: its reads answer nothing; its one create is captured).</summary>
        public Mock<IFieldMappingDataverseService> AppOnly { get; } = new(MockBehavior.Loose);

        /// <summary>
        /// Task 031: the catalog row the route reads (<c>QueryRecordTypeRefAsync</c>) is ALSO a row the service reads back as the caller
        /// ("the core's pair rule": the pair's type must name the table the id belongs to). Registers it in the scripted caller's
        /// catalog and returns it, so a test states the row once.
        /// </summary>
        public Entity InCallerCatalog(string logicalName, Entity row)
        {
            ScriptedCaller.RecordTypes[row.Id] = logicalName;
            return row;
        }

        public EventsAuthHost()
        {
            CallerClient = new CountingUserClient(FilingUser.Object);
            AppOnly.Setup(a => a.UpdateRecordFieldsAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) => AppOnlyCreates.Add((table, id, fields)))
                .Returns(Task.CompletedTask);
        }

        /// <summary>
        /// Task 031 (AC "service not invoked when the filter denies"): nothing the service does is visible anywhere - no owner
        /// question, no question to the caller's Dataverse, no app-only write and no membership event.
        /// </summary>
        public void AssertServiceNotReached()
        {
            Ownership.Requests.Should().BeEmpty("the route filter answered before the service was reached");
            CallerClient.Calls.Should().Be(0, "the service asks the caller's Dataverse first; a denied request never gets there");
            AppOnlyCreates.Should().BeEmpty();
            Publisher.Published.Should().BeEmpty();
        }

        private static Mock<Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient> FilingUserClient()
        {
            var user = new Mock<Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient>(MockBehavior.Strict);
            user.Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserResponse.Fail(
                    503, "dataverse.unavailable", "Dataverse is unavailable."));
            return user;
        }

        /// <summary>
        /// Task 146's ONE owner resolver (sweep integration): every create and log row is owned by the team it names. A
        /// module-boundary double — 146's own tests drive the real resolver; here only the gate is under test.
        /// </summary>
        public Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble Ownership { get; } = new();

        /// <summary>The CoreAncestorResolver's column seam. Default: every entity carries the four core lookups.</summary>
        public CoreAncestorResolver.EntityColumnProbe ColumnProbe { get; set; } = (_, _) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                CoreAncestorResolver.CoreAncestorLookups.Select(c => c.LookupAttribute), StringComparer.OrdinalIgnoreCase));

        public static async Task<EventsAuthHost> StartAsync(bool useRealProbe = false)
        {
            var host = new EventsAuthHost();
            await host.InitializeAsync(useRealProbe);
            return host;
        }

        private async Task InitializeAsync(bool useRealProbe)
        {
            Caller.Setup(c => c.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
            Identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(id, ContactId: null));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();

            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = EventsAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = EventsAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, EventsAuthzTestAuthHandler>(EventsAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

            builder.Services.AddSingleton<CallerRecordAccessProbe>(useRealProbe
                ? new CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
                : Probe);
            builder.Services.AddSingleton(Events.Object);
            builder.Services.AddSingleton(Caller.Object);
            builder.Services.AddSingleton(RecordTypes.Object);
            builder.Services.AddSingleton(Entities.Object);
            builder.Services.AddSingleton(Identity.Object);
            builder.Services.AddSingleton<IMembershipEventPublisher>(Publisher);
            builder.Services.AddSingleton<Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver>(Ownership);
            var coreAncestors = new CoreAncestorResolver(
                Entities.Object, (entity, ct) => ColumnProbe(entity, ct), NullLogger<CoreAncestorResolver>.Instance);
            builder.Services.AddSingleton(coreAncestors);

            // Task 031 (FR-19): the create is the ONE server create, as the caller. The REAL service over this host's own seams: the
            // owner double, the app-only writer, the host's Dataverse double, the real stamp derivation over the host's column probe
            // (so a stamp failure still fails the create closed), and the host's membership publisher.
            builder.Services.AddSingleton<IDataverseUserClient>(CallerClient);
            builder.Services.AddSingleton(RouteRecordCreation.Over(
                Ownership,
                AppOnly.Object,
                new CoreAncestorRestamper(Entities.Object, coreAncestors, NullLogger<CoreAncestorRestamper>.Instance),
                Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard()
                    .Synchronizer(new Sprk.Bff.Api.Tests.AccessControl.FakeRecordShareTable()),
                SecureRootFilingGateFixtures.NothingSecure(),
                null,
                Publisher,
                Entities.Object));

            // Task 147 r1c: the filing route's services (the re-file core's own tests drive them fully).
            builder.Services.AddSingleton(new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper);
            builder.Services.AddScoped(_ => Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.ReconcilerOver(
                () => Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.Standard(), new Sprk.Bff.Api.Tests.AccessControl.FakeRecordShareTable(), null!));

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();

            _app.MapEventEndpoints();

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool authenticated = true, bool withToken = true)
        {
            if (authenticated)
            {
                request.Headers.Add(EventsAuthzTestAuthHandler.CallerHeader, "1");
            }

            if (withToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            }

            return _client!.SendAsync(request);
        }

        public void SetupEvent(Guid id, int statusCode) =>
            Events.Setup(e => e.GetEventAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EventEntity { Id = id, Name = "Filing deadline", StatusCode = statusCode });

        public List<CallerScopedQuery> CaptureCallerScopedQuery(int totalCount)
        {
            var captured = new List<CallerScopedQuery>();
            Events.Setup(e => e.QueryEventsAsCallerAsync(
                    It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<EventOwnershipScope?>(), It.IsAny<CancellationToken>()))
                .Returns((Guid caller, int? type, Guid? regardingId, Guid? typeRef, Guid? _, int? status, int? _,
                    DateTime? _, DateTime? _, int _, int _, EventOwnershipScope? mine, CancellationToken _) =>
                {
                    captured.Add(new CallerScopedQuery(caller, type, regardingId, typeRef, status, mine?.OwnerUserId, mine));
                    return Task.FromResult((Array.Empty<EventEntity>(), totalCount));
                });
            return captured;
        }

        /// <summary>
        /// Lets the create reach the service as the caller (the scripted caller replaces the strict filing client) and returns what
        /// the service's ONE app-only create wrote, parsed back into the values these tests read. Task 031: the row is no longer
        /// created through <see cref="IEventDataverseService"/>; the capture seam moved, the assertions did not.
        /// </summary>
        public List<CapturedEventCreate> CaptureCreates()
        {
            CallerClient.Inner = ScriptedCaller;
            var captured = new List<CapturedEventCreate>();
            AppOnly.Setup(a => a.UpdateRecordFieldsAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
                .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
                {
                    AppOnlyCreates.Add((table, id, fields));
                    captured.Add(new CapturedEventCreate(table, id, fields));
                })
                .Returns(Task.CompletedTask);
            Events.Setup(e => e.CreateEventLogAsync(It.IsAny<Guid>(), EventLogAction.Created, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());
            return captured;
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}

/// <summary>
/// The caller's Dataverse client the host registers: forwards to <see cref="Inner"/> and counts every question, so a test can
/// assert the create service (which asks the caller's Dataverse first) was never reached.
/// </summary>
internal sealed class CountingUserClient(IDataverseUserClient inner) : IDataverseUserClient
{
    public IDataverseUserClient Inner { get; set; } = inner;

    public int Calls { get; private set; }

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        Calls++;
        return Inner.GetAsync(relativePath, cancellationToken);
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
    {
        Calls++;
        return Inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
    {
        Calls++;
        return Inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
    {
        Calls++;
        return Inner.PatchAsync(relativePath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        Calls++;
        return Inner.DeleteAsync(relativePath, cancellationToken);
    }
}

/// <summary>
/// The event as the service's one app-only create wrote it (Web API logical names). <see cref="Payload"/> reads the body the way
/// <c>DataverseWebApiService.BuildCreateEventPayload</c> was read by the contract assertions: a lookup is its
/// <c>{navigation}@odata.bind</c> path, a number an <see cref="int"/>, a string a string.
/// </summary>
internal sealed class CapturedEventCreate(string table, Guid id, IReadOnlyDictionary<string, object?> fields)
{
    public string Table { get; } = table;

    public Guid Id { get; } = id;

    public IReadOnlyDictionary<string, object?> Fields { get; } = fields;

    public Dictionary<string, object?> Payload { get; } = fields.ToDictionary(f => f.Key, f => Plain(f.Value), StringComparer.Ordinal);

    /// <summary>The regarding record's number as the create wrote it (<c>sprk_regardingrecordnumber</c>), or null.</summary>
    public string? RegardingRecordNumber => Payload.GetValueOrDefault("sprk_regardingrecordnumber") as string;

    /// <summary>The regarding record's name as the create wrote it (<c>sprk_regardingrecordname</c>), or null.</summary>
    public string? RegardingRecordName => Payload.GetValueOrDefault("sprk_regardingrecordname") as string;

    /// <summary>The regarding record's id as the create wrote it (<c>sprk_regardingrecordid</c>), or null when it filed under nothing.</summary>
    public string? RegardingRecordId => Payload.GetValueOrDefault("sprk_regardingrecordid") as string;

    /// <summary>The Access Permission the create wrote (<c>sprk_accesspermission</c>), or null.</summary>
    public int? AccessPermission => Payload.GetValueOrDefault("sprk_accesspermission") as int?;

    private static object? Plain(object? value) => value is not System.Text.Json.JsonElement element
        ? value
        : element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number when element.TryGetInt32(out var number) => number,
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => element.GetRawText(),
        };
}

/// <summary>Authenticates a request carrying <see cref="CallerHeader"/> as a caller with an Entra <c>oid</c>,
/// independent of the Authorization header — so a test can present an authenticated caller with no bearer token.</summary>
public sealed class EventsAuthzTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "EventsAuthzTest";
    public const string CallerHeader = "X-Test-Caller";

    public EventsAuthzTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(CallerHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(new[] { new Claim("oid", "6f0c1a52-0000-4000-8000-000000000159") }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
