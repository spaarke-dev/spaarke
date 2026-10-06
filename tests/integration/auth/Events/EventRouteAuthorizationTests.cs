using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiUpdateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.UpdateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;
using DataverseUpdateEventRequest = Spaarke.Dataverse.UpdateEventRequest;

namespace Sprk.Bff.Api.Tests.AccessControl.Events;

/// <summary>
/// spaarke-ontology-platform-r1 task 097, second review (SECURITY) — every <c>/api/v1/events</c> route refuses a
/// caller who lacks the right on the record it names, BEFORE the handler runs.
/// </summary>
/// <remarks>
/// <para><b>Before:</b> the group carried only <c>RequireAuthorization()</c> ("are you anyone?") and every handler acted
/// APP-ONLY on whatever id the caller supplied — read, update, complete, cancel, soft-delete any event, re-parent it
/// onto any record, and list the application user's view.</para>
///
/// <para><b>403 for every refusal, never 404.</b> <see cref="CallerRecordAccessProbe"/> reports "no such record" and
/// "you may not see it" identically (Dataverse hides existence under OBO), and the filter answers both with ONE
/// constant body — the <c>TodoSourceAccessFilter</c> reasoning — so no route is an existence oracle. Asserted by
/// <see cref="Denied_And_NonExistent_AreIndistinguishable"/>.</para>
///
/// <para><b>Doubles are module boundaries only</b> (ADR-038 §4): the probe's virtual <c>GetCallerRightsAsync</c>
/// (stands in for <c>RetrievePrincipalAccess</c>, deny by default) and the Dataverse services. The route, the filter
/// chain and the handlers are shipped code. "The handler did not run" is proven by the event service having received
/// NO call.</para>
/// </remarks>
public class EventRouteAuthorizationTests
{
    private static readonly Guid EventId = Guid.Parse("97097097-aaaa-4000-8000-000000000001");
    private static readonly Guid MatterId = Guid.Parse("97097097-bbbb-4000-8000-000000000002");
    private static readonly Guid CallerSystemUserId = Guid.Parse("97097097-cccc-4000-8000-000000000003");

    public static TheoryData<string, string> RecordRoutes => new()
    {
        { "GET", "" },
        { "GET", "/logs" },
        { "PUT", "" },
        { "POST", "/complete" },
        { "POST", "/cancel" },
        { "DELETE", "" },
    };

    public static TheoryData<string, string> WriteRoutes => new()
    {
        { "PUT", "" },
        { "POST", "/complete" },
        { "POST", "/cancel" },
        { "DELETE", "" },
    };

    // ── Every record route: no rights → 403, handler never runs ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(RecordRoutes))]
    public async Task RecordRoute_CallerWithNoRightsOnTheEvent_Is403_AndTheHandlerNeverRuns(string method, string suffix)
    {
        using var factory = new EventAccessTestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request(method, suffix));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("event_access_denied");
        factory.Events.Invocations.Should().BeEmpty("the refusal happens in the filter, before any Dataverse read or write");
        factory.Probed.Should().Contain(("sprk_events", EventId));
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task WriteRoute_CallerWithReadOnlyOnTheEvent_Is403_AndTheHandlerNeverRuns(string method, string suffix)
    {
        using var factory = new EventAccessTestWebAppFactory();
        factory.Grant("sprk_events", EventId, AccessRights.Read);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request(method, suffix));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "complete / cancel / soft-delete / update are status WRITES and cost Write, not Read");
        factory.Events.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/logs")]
    public async Task ReadRoute_CallerWithRead_ReachesTheHandler(string method, string suffix)
    {
        using var factory = new EventAccessTestWebAppFactory();
        factory.Grant("sprk_events", EventId, AccessRights.Read);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request(method, suffix));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Events.Verify(e => e.GetEventAsync(EventId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Complete_CallerWithWrite_ReachesTheHandler()
    {
        using var factory = new EventAccessTestWebAppFactory();
        factory.Grant("sprk_events", EventId, AccessRights.Read | AccessRights.Write);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Request("POST", "/complete"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Events.Verify(e => e.UpdateEventStatusAsync(EventId, EventStatusCode.Completed, It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Denied_And_NonExistent_AreIndistinguishable()
    {
        // One id the caller may not touch, one that does not exist: the probe answers None for both, and the
        // response must not differ in status or body (the per-request trace id normalized out).
        using var factory = new EventAccessTestWebAppFactory();
        using var client = factory.CreateClient();

        var denied = await client.SendAsync(Request("GET", ""));
        var absent = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/v1/events/{Guid.NewGuid()}"));

        denied.StatusCode.Should().Be(absent.StatusCode);
        (await Normalize(denied)).Should().Be(await Normalize(absent));
    }

    // ── Create / re-parent: the PARENT is authorized before it is resolved, read or stamped ──────────────────

    [Fact]
    public async Task Create_WithAParentTheCallerCannotRead_Is403_AndNothingIsWritten()
    {
        using var factory = new EventAccessTestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/events",
            new ApiCreateEventRequest("zz-097", RegardingRecordId: MatterId, RegardingRecordType: RegardingRecordType.Matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Events.Invocations.Should().BeEmpty("the parent is neither resolved nor stamped nor written against");
        factory.Probed.Should().Equal(("sprk_matters", MatterId));
    }

    [Fact]
    public async Task Create_WithAReadableParent_ReachesTheHandler()
    {
        using var factory = new EventAccessTestWebAppFactory();
        factory.Grant("sprk_matters", MatterId, AccessRights.Read);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/events",
            new ApiCreateEventRequest("zz-097", RegardingRecordId: MatterId, RegardingRecordType: RegardingRecordType.Matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.Events.Verify(e => e.CreateEventAsync(It.IsAny<DataverseCreateEventRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithNoParent_ProbesNothing_AndReachesTheHandler()
    {
        using var factory = new EventAccessTestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/events", new ApiCreateEventRequest("zz-097 standalone"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        factory.Probed.Should().BeEmpty("no record was named, so there is nothing to authorize");
    }

    [Fact]
    public async Task Reparent_WithWriteOnTheEventButNoReadOnTheNewParent_Is403_AndNothingIsWritten()
    {
        using var factory = new EventAccessTestWebAppFactory();
        factory.Grant("sprk_events", EventId, AccessRights.Read | AccessRights.Write);
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/v1/events/{EventId}",
            new ApiUpdateEventRequest(RegardingRecordId: MatterId, RegardingRecordType: RegardingRecordType.Matter));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Events.Invocations.Should().BeEmpty();
    }

    // ── The list: impersonated as the caller, refused when the caller cannot be resolved ──────────────────────

    [Fact]
    public async Task List_CallerWhoDoesNotResolveToASystemUser_Is403_AndNothingIsQueried()
    {
        using var factory = new EventAccessTestWebAppFactory(callerResolves: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/events");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "an unresolvable caller cannot be impersonated; never fall back to app-only");
        factory.Events.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task List_RunsImpersonatedAsTheCaller_WithTheMineScope()
    {
        using var factory = new EventAccessTestWebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Events.Verify(e => e.QueryEventsAsync(
            It.Is<EventQueryFilter>(f => f.ImpersonateSystemUserId == CallerSystemUserId && f.OwnerUserId == CallerSystemUserId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static HttpRequestMessage Request(string method, string suffix)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/events/{EventId}{suffix}");
        if (method == "PUT")
            request.Content = JsonContent.Create(new ApiUpdateEventRequest(Subject: "zz-097 renamed"));
        return request;
    }

    private static async Task<string> Normalize(HttpResponseMessage response) =>
        Regex.Replace(await response.Content.ReadAsStringAsync(), "\"(correlationId|traceId)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"*\"");

    /// <summary>A grant-table probe and recording Dataverse doubles over the shared Office test host.</summary>
    private sealed class EventAccessTestWebAppFactory : OfficeTestWebAppFactory
    {
        private readonly Dictionary<(string, Guid), AccessRights> _grants = new();
        private readonly Guid? _callerSystemUser;

        public EventAccessTestWebAppFactory(bool callerResolves = true) =>
            _callerSystemUser = callerResolves ? CallerSystemUserId : null;

        public Mock<IEventDataverseService> Events { get; } = new(MockBehavior.Loose);
        public List<(string EntitySet, Guid Id)> Probed { get; } = new();

        public void Grant(string entitySet, Guid id, AccessRights rights) => _grants[(entitySet, id)] = rights;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            Events.Setup(e => e.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EventEntity { Id = EventId, Name = "zz-097", StatusCode = EventStatusCode.Open });
            Events.Setup(e => e.QueryEventLogsAsync(EventId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<EventLogEntity>());
            Events.Setup(e => e.CreateEventAsync(It.IsAny<DataverseCreateEventRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((EventId, DateTime.UtcNow));
            Events.Setup(e => e.QueryEventsAsync(It.IsAny<EventQueryFilter>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Array.Empty<EventEntity>(), 0));

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEventDataverseService>();
                services.AddSingleton(Events.Object);

                var communication = new Mock<ICommunicationDataverseService>(MockBehavior.Loose);
                communication.Setup(c => c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(_callerSystemUser);
                services.RemoveAll<ICommunicationDataverseService>();
                services.AddSingleton(communication.Object);

                var identity = new Mock<IIdentityNormalizationService>(MockBehavior.Loose);
                identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(id, ContactId: null));
                services.RemoveAll<IIdentityNormalizationService>();
                services.AddSingleton(identity.Object);

                services.RemoveAll<CoreAncestorResolver>();
                services.AddSingleton(CoreAncestorResolverFixtures.Inert());

                services.RemoveAll<CallerRecordAccessProbe>();
                services.AddSingleton<CallerRecordAccessProbe>(new GrantTableProbe(_grants, Probed));
            });
        }
    }

    private sealed class GrantTableProbe(Dictionary<(string, Guid), AccessRights> grants, List<(string, Guid)> probed)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            lock (probed)
                probed.Add((entitySet, recordId));
            return Task.FromResult(grants.TryGetValue((entitySet, recordId), out var rights) ? rights : AccessRights.None);
        }
    }
}
