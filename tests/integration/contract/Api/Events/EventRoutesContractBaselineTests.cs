using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Integration.Contract.Signals;
using Xunit;
using EventStatusCode = Spaarke.Dataverse.EventStatusCode;
using EventsAuthHost = Sprk.Bff.Api.Tests.Api.Events.EventEndpointsAuthorizationContractTests.EventsAuthHost;

namespace Sprk.Bff.Api.Tests.Api.Events;

/// <summary>
/// spaarkeai-mcp-bff-api-r2 task 006 (spec FR-07, MUST "REST bytes unchanged"): the frozen bytes of the four event routes the
/// events capability takes over - list, get, create and complete - recorded against the UNCHANGED <c>EventEndpoints</c> before
/// the extraction and kept unchanged after it. Each case is one golden file under <c>contract/Api/Events/Baselines/</c> holding
/// the status, the Content-Type and the body bytes (<see cref="GoldenContract"/>; a missing file fails, a baseline is never
/// rewritten to make a refactor pass).
/// </summary>
/// <remarks>
/// <para><b>What is real.</b> The REAL <c>MapEventEndpoints</c> (routing, <c>RequireAuthorization</c>, binding, the validation and
/// record-access filters, the handlers, the JSON writer) and the REAL <c>RecordCreationService</c> on
/// <see cref="EventEndpointsAuthorizationContractTests.EventsAuthHost"/>, doubled only at the module boundaries that suite already
/// doubles (the caller-rights probe, the event and catalog Dataverse services, the owner resolver). Every id and date is fixed, so
/// the bytes are the same on every run.</para>
/// <para><b>Normalised, and only these.</b> <c>traceId</c> / <c>correlationId</c> (GoldenContract); the create's <c>createdOn</c>
/// and the complete's <c>actionTimestamp</c> (each the server's clock at the request); the create's new id (minted by the service),
/// replaced by <c>&lt;guid-N&gt;</c>, and its <c>Location</c> header, asserted to name that same id.</para>
/// <para><b>Not applicable.</b> No 404 on the list or the create (neither names a record by route); no 400 on get or complete (the
/// <c>{id:guid}</c> route constraint answers a malformed id before any handler) and no 403 on get (Read is all it needs, and no
/// Read is the uniform 404).</para>
/// </remarks>
public sealed class EventRoutesContractBaselineTests
{
    private const string Area = "Events";
    private const string EventsSet = "sprk_events";
    private const string AnalysesSet = "sprk_analysises";
    private const string MattersSet = "sprk_matters";
    private const string CreateEventPrivilege = "prvCreatesprk_Event";

    private static readonly Guid FirstEvent = Guid.Parse("e0060000-0000-4000-8000-0000000000a1");
    private static readonly Guid SecondEvent = Guid.Parse("e0060000-0000-4000-8000-0000000000a2");
    private static readonly Guid EventType = Guid.Parse("e0060000-0000-4000-8000-0000000000e1");
    private static readonly Guid Matter = Guid.Parse("e0060000-0000-4000-8000-0000000000b1");
    private static readonly Guid Analysis = Guid.Parse("e0060000-0000-4000-8000-0000000000c1");

    private static readonly string[] CreatedOn = ["createdOn"];
    private static readonly string[] ActionTimestamp = ["actionTimestamp"];

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<GoldenContract.Observed> SendAsync(EventsAuthHost host, HttpRequestMessage request, bool authenticated = true)
    {
        using var response = await host.SendAsync(request, authenticated, withToken: authenticated);
        return await GoldenContract.ObserveAsync(response);
    }

    /// <summary>Asserts the case produced the status and reason code it is named for, then compares it with its golden file.</summary>
    private static void Verify(
        string caseName, GoldenContract.Observed observed, int status, string? reasonCode = null, string[]? clockFields = null,
        bool generatedGuids = false)
    {
        observed.Status.Should().Be(status, observed.Body);
        if (reasonCode is not null)
        {
            JsonDocument.Parse(observed.Body).RootElement.GetProperty("reasonCode").GetString().Should().Be(reasonCode, observed.Body);
        }

        var body = observed.Body;
        foreach (var field in clockFields ?? [])
        {
            body = Regex.Replace(body, "(\"" + field + "\"\\s*:\\s*)\"[^\"]*\"", "$1\"" + GoldenContract.VolatilePlaceholder + "\"", RegexOptions.CultureInvariant);
        }

        GoldenContract.AssertMatches(caseName, observed with { Body = body }, generatedGuids, Area);
    }

    private static EventEntity Event(Guid id, string name, int statusCode = EventStatusCode.Open) => new()
    {
        Id = id,
        Name = name,
        Description = "Prepare the hearing bundle",
        EventTypeId = EventType,
        EventTypeName = "Deadline",
        StateCode = EventStatusCode.GetStateCode(statusCode),
        StatusCode = statusCode,
        BaseDate = new DateOnly(2026, 11, 2),
        DueDate = new DateOnly(2026, 12, 1),
        FinalDueDate = new DateOnly(2026, 12, 15),
        ApprovedDate = new DateOnly(2026, 11, 3),
        MeetingDate = new DateOnly(2026, 11, 20),
        Priority = EventPriority.High,
        Source = 0,
        RegardingRecordId = Matter.ToString("D"),
        RegardingRecordName = "Acme v Beta",
        RegardingRecordType = 1,
        CreatedOn = new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc),
        ModifiedOn = new DateTime(2026, 11, 4, 16, 45, 0, DateTimeKind.Utc),
    };

    private static void ListAnswers(EventsAuthHost host, EventEntity[] items, int totalCount) =>
        host.Events.Setup(e => e.QueryEventsAsCallerAsync(
                It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<EventOwnershipScope?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, totalCount));

    private static HttpRequestMessage List(string query = "") => new(HttpMethod.Get, "/api/v1/events" + query);

    private static HttpRequestMessage Get(Guid id) => new(HttpMethod.Get, $"/api/v1/events/{id}");

    private static HttpRequestMessage Complete(Guid id) => new(HttpMethod.Post, $"/api/v1/events/{id}/complete");

    private static HttpRequestMessage Create(object body) =>
        new(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(body) };

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // GET /api/v1/events
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_200_APageOfTheCallersEvents()
    {
        await using var host = await EventsAuthHost.StartAsync();
        ListAnswers(host, [Event(FirstEvent, "Hearing prep"), Event(SecondEvent, "File the brief")], totalCount: 7);

        Verify("list.200", await SendAsync(host, List("?status=open&pageSize=2")), 200);
    }

    [Fact]
    public async Task List_200_ATypeWithNoRecordTypeRow_IsAnEmptyPage()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_budget", It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);

        Verify("list.200.no-type-row", await SendAsync(host, List("?regardingRecordType=7")), 200);
    }

    [Fact]
    public async Task List_400_ARegardingIdThatIsNotAGuid()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("list.400.regarding-id", await SendAsync(host, List("?regardingRecordId=abc")), 400);
    }

    [Fact]
    public async Task List_400_ARegardingTypeOutOfRange()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("list.400.regarding-type", await SendAsync(host, List("?regardingRecordType=9")), 400);
    }

    [Fact]
    public async Task List_400_APriorityNotInTheOptionSet()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("list.400.priority", await SendAsync(host, List("?priority=2")), 400);
    }

    [Fact]
    public async Task List_400_APageWindowPastTheDataverseLimit()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("list.400.window", await SendAsync(host, List("?pageNumber=51&pageSize=100")), 400);
    }

    [Fact]
    public async Task List_403_ACallerWithNoDataverseUser()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Caller.Setup(c => c.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        Verify("list.403.caller-unresolved", await SendAsync(host, List()), 403, "sdap.access.deny.caller_unresolved");
    }

    [Fact]
    public async Task List_401_NoCaller()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("list.401", await SendAsync(host, List(), authenticated: false), 401);
    }

    [Fact]
    public async Task List_500_TheQueryFaults()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Events.Setup(e => e.QueryEventsAsCallerAsync(
                It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<EventOwnershipScope?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        Verify("list.500", await SendAsync(host, List()), 500);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // GET /api/v1/events/{id}
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_200_AReaderGetsTheEvent()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read);
        host.Events.Setup(e => e.GetEventAsync(FirstEvent, It.IsAny<CancellationToken>())).ReturnsAsync(Event(FirstEvent, "Hearing prep"));

        Verify("get.200", await SendAsync(host, Get(FirstEvent)), 200);
    }

    [Fact]
    public async Task Get_404_ACallerWithoutRead()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("get.404.no-read", await SendAsync(host, Get(FirstEvent)), 404, "sdap.access.deny.record_unavailable");
    }

    [Fact]
    public async Task Get_404_AReadableEventTheReadDoesNotFind()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read);
        host.Events.Setup(e => e.GetEventAsync(FirstEvent, It.IsAny<CancellationToken>())).ReturnsAsync((EventEntity?)null);

        Verify("get.404.not-found", await SendAsync(host, Get(FirstEvent)), 404, "sdap.access.deny.record_unavailable");
    }

    [Fact]
    public async Task Get_500_TheReadFaults()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read);
        host.Events.Setup(e => e.GetEventAsync(FirstEvent, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        Verify("get.500", await SendAsync(host, Get(FirstEvent)), 500);
    }

    [Fact]
    public async Task Get_401_NoCaller()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("get.401", await SendAsync(host, Get(FirstEvent), authenticated: false), 401);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // POST /api/v1/events
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_201_AnEventFiledUnderNothing()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        host.CaptureCreates();

        using var response = await host.SendAsync(Create(new { subject = "Hearing prep" }));
        var observed = await GoldenContract.ObserveAsync(response);

        Verify("create.201", observed, 201, clockFields: CreatedOn, generatedGuids: true);
        var id = JsonDocument.Parse(observed.Body).RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/v1/events/{id}");
    }

    [Fact]
    public async Task Create_201_AnEventFiledUnderAnAnalysis()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(AnalysesSet, Analysis, AccessRights.AppendTo);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync(AnalysesSet);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);
        host.RecordTypes.Setup(r => r.QueryRecordTypeRefAsync("sprk_analysis", It.IsAny<CancellationToken>())).ReturnsAsync((Entity?)null);
        host.Entities.Setup(e => e.RetrieveAsync("sprk_analysis", Analysis, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_analysis", Analysis) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter) });
        host.CaptureCreates();

        using var response = await host.SendAsync(Create(new { subject = "Review", regardingRecordType = 3, regardingRecordId = Analysis }));
        var observed = await GoldenContract.ObserveAsync(response);

        Verify("create.201.regarding", observed, 201, clockFields: CreatedOn, generatedGuids: true);
        var id = JsonDocument.Parse(observed.Body).RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/v1/events/{id}");
    }

    [Fact]
    public async Task Create_400_NoSubject()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("create.400.subject", await SendAsync(host, Create(new { subject = " " })), 400);
    }

    [Fact]
    public async Task Create_400_APriorityNotInTheOptionSet()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("create.400.priority", await SendAsync(host, Create(new { subject = "Hearing", priority = 3 })), 400);
    }

    [Fact]
    public async Task Create_400_HalfARegarding()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("create.400.half-regarding", await SendAsync(host, Create(new { subject = "Hearing", regardingRecordType = 1 })), 400);
    }

    [Fact]
    public async Task Create_400_AScheduledEndBeforeItsStart()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("create.400.dates", await SendAsync(host, Create(new { subject = "Hearing", scheduledStart = "2026-12-02", scheduledEnd = "2026-12-01" })), 400);
    }

    [Fact]
    public async Task Create_403_WithoutTheCreatePrivilege()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("create.403.privilege", await SendAsync(host, Create(new { subject = "Hearing" })), 403, "sdap.access.deny.insufficient_privilege");
    }

    [Fact]
    public async Task Create_403_WithoutAppendToOnTheRegarding()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        host.Probe.Grant(MattersSet, Matter, AccessRights.Read | AccessRights.Write);
        host.Entities.Setup(e => e.GetEntitySetNameAsync("sprk_matter", It.IsAny<CancellationToken>())).ReturnsAsync(MattersSet);

        Verify("create.403.rights", await SendAsync(host, Create(new { subject = "Hearing", regardingRecordType = 1, regardingRecordId = Matter })),
            403, "sdap.access.deny.insufficient_rights");
    }

    [Fact]
    public async Task Create_409_NoOwnerResolves()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Hold(CreateEventPrivilege);
        host.CaptureCreates();
        host.Ownership.TeamId = null;

        Verify("create.409.owner", await SendAsync(host, Create(new { subject = "Hearing prep" })), 409);
    }

    [Fact]
    public async Task Create_401_NoCaller()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("create.401", await SendAsync(host, Create(new { subject = "Hearing" }), authenticated: false), 401);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
    // POST /api/v1/events/{id}/complete
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

    private static void CompletesAndLogs(EventsAuthHost host, Guid id)
    {
        host.Events.Setup(e => e.UpdateEventStatusAsync(id, EventStatusCode.Completed, It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        host.Events.Setup(e => e.CreateEventLogAsync(id, EventLogAction.Completed, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Parse("e0060000-0000-4000-8000-0000000000f1"));
    }

    [Fact]
    public async Task Complete_200_AWriterCompletesOpenWork()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read | AccessRights.Write);
        host.Events.Setup(e => e.GetEventAsync(FirstEvent, It.IsAny<CancellationToken>())).ReturnsAsync(Event(FirstEvent, "Hearing prep"));
        CompletesAndLogs(host, FirstEvent);

        Verify("complete.200", await SendAsync(host, Complete(FirstEvent)), 200, clockFields: ActionTimestamp);
    }

    [Fact]
    public async Task Complete_400_AnEventThatIsNotOpenWork()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read | AccessRights.Write);
        host.Events.Setup(e => e.GetEventAsync(FirstEvent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Event(FirstEvent, "Hearing prep", EventStatusCode.Completed));

        Verify("complete.400.transition", await SendAsync(host, Complete(FirstEvent)), 400);
    }

    [Fact]
    public async Task Complete_403_AReaderWithoutWrite()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read);

        Verify("complete.403.read-only", await SendAsync(host, Complete(FirstEvent)), 403, "sdap.access.deny.insufficient_rights");
    }

    [Fact]
    public async Task Complete_404_ACallerWithoutRead()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("complete.404.no-read", await SendAsync(host, Complete(FirstEvent)), 404, "sdap.access.deny.record_unavailable");
    }

    [Fact]
    public async Task Complete_404_AWritableEventTheReadDoesNotFind()
    {
        await using var host = await EventsAuthHost.StartAsync();
        host.Probe.Grant(EventsSet, FirstEvent, AccessRights.Read | AccessRights.Write);
        host.Events.Setup(e => e.GetEventAsync(FirstEvent, It.IsAny<CancellationToken>())).ReturnsAsync((EventEntity?)null);

        Verify("complete.404.not-found", await SendAsync(host, Complete(FirstEvent)), 404, "sdap.access.deny.record_unavailable");
    }

    [Fact]
    public async Task Complete_401_NoCaller()
    {
        await using var host = await EventsAuthHost.StartAsync();
        Verify("complete.401", await SendAsync(host, Complete(FirstEvent), authenticated: false), 401);
    }
}
