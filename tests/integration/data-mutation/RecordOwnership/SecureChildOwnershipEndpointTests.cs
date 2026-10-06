using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordMatching;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r1 — the two HTTP RE-FILE routes the verifier found changing ownership without
/// authorization, through the REAL endpoint mappers, the REAL per-record authorization stack (substituted only at
/// <see cref="IAccessDataSource"/>, the recording double the finance contract tests use) and the REAL
/// <see cref="RecordOwnershipResolver"/> over <see cref="Directory"/>.
/// </summary>
/// <remarks>
/// <para><b>PUT /api/v1/events/{id}</b> was DELETED by task 159 (owner round 10 item 1: no caller, not published), so the
/// re-file guards task 146 r1 pinned on it (verifier items 2, 9, 18) protect nothing that still exists; one test pins that the
/// route answers nothing and re-owns nothing. <b>POST /api/v1/events</b> keeps 146's owner resolution behind 159's
/// as-the-caller gate (Create privilege + AppendTo on the regarding record).</para>
/// <para><b>POST /api/ai/document-intelligence/associate-record</b> was DELETED by task 164 (owner round 10 item 1); its
/// absence is pinned by <c>AiPlaybookPromptRecordMatchRouteRetirementTests</c>.</para>
/// <para>No HTTP handler is mocked (ADR-038 B1); the Dataverse seams are module-boundary doubles.</para>
/// </remarks>
[Trait("status", "new")]
public class SecureChildOwnershipEndpointTests
{
    private static readonly Guid SecureProject = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrdinaryProject = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecureMatter = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OrdinaryMatter = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid FlaggedNotIsolatedProject = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>Full Access: Collaborate plus Delete — F3 admits it (owner round 3b; round 10 item 7 for children).</summary>
    private const AccessRights FullAccess =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share
        | AccessRights.Delete;

    /// <summary>Collaborate: Write without Delete — the Write holder F3 refuses.</summary>
    private const AccessRights Collaborate =
        AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    /// <summary>Who the F3 probe answers WhoAmI with (task 146 c1).</summary>
    internal static readonly Guid ProbeCaller = Guid.Parse("f3f3f3f3-0000-4000-8000-0000000000ca");

    private const string Events = "sprk_events";
    private const string Projects = "sprk_projects";
    private const string Matters = "sprk_matters";
    private const string Documents = "sprk_documents";

    private static Directory World() => Directory.Standard()
        .WithSecureRoot("sprk_project", SecureProject)
        .WithOrdinaryRoot("sprk_project", OrdinaryProject)
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithRecord("sprk_project", FlaggedNotIsolatedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam);

    // =====================================================================================
    // PUT /api/v1/events/{id} — DELETED (task 159, owner round 10 item 1)
    // =====================================================================================

    /// <summary>
    /// Batch-4 sweep integration: task 159 deleted the PUT route (no caller in the repo, not in the published Copilot
    /// description), so the event can no longer be re-filed over HTTP at all. Every caller — even one holding Full Access on
    /// the event and on both projects — reaches no handler, and nothing is written, logged or re-owned.
    /// </summary>
    [Fact]
    public async Task EventRefile_TheDeletedPutRoute_ReachesNothing_AndNothingIsWrittenOrReowned()
    {
        var eventId = Guid.NewGuid();
        var world = SecureProjectEvent(eventId);
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = SecureProjectEventEntity(eventId);
        host.Access.Grant(Events, eventId, FullAccess);
        host.Access.Grant(Projects, SecureProject, FullAccess);
        host.Access.Grant(Projects, OrdinaryProject, FullAccess);

        var response = await host.SendAsync(Authenticated(HttpMethod.Put, $"/api/v1/events/{eventId}",
            new { regardingRecordType = 0, regardingRecordId = OrdinaryProject, statusCode = 4 }));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        host.Events.Creates.Should().BeEmpty();
        host.Events.StatusUpdates.Should().BeEmpty();
        host.Events.LogOwners.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task EventCreate_RegardingASecureProject_IsOwnedByTheNamedSecureTeam_AndSoIsItsLogRow()
    {
        await using var host = await EventsHost.StartAsync(World().Resolver());
        host.MayCreateEventsUnder(Projects, SecureProject);

        var response = await host.SendAsync(Authenticated(HttpMethod.Post, "/api/v1/events",
            new { subject = "Filing deadline", regardingRecordType = 0, regardingRecordId = SecureProject }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Events.Creates.Should().ContainSingle().Which.OwningTeamId.Should().Be(Directory.SecureNamedTeam);
        host.Events.LogOwners.Should().Equal(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task EventCreate_RecordsTheCallerAsThePersonWhoAsked_OnTheEventAndItsLogRow()
    {
        // c1-r1 (owner round 13 item 9): the create is app-only (createdby = the application), so the signed-in caller —
        // looked up by their object id — is recorded as sprk_createdbyperson on both rows.
        var callerSystemUser = Guid.Parse("c1c1c1c1-0000-4000-8000-000000000146");
        var world = World().WithUser(callerSystemUser, Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId), Directory.ChildBu);
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.MayCreateEventsUnder(Projects, SecureProject);

        var response = await host.SendAsync(Authenticated(HttpMethod.Post, "/api/v1/events",
            new { subject = "Filing deadline", regardingRecordType = 0, regardingRecordId = SecureProject }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Events.Creates.Should().ContainSingle().Which.CreatedByPersonId.Should().Be(callerSystemUser);
        host.Events.LogPersons.Should().Equal(callerSystemUser);
    }

    [Fact]
    public async Task EventCreate_RegardingAFlaggedButNotIsolatedProject_Is409_AndCreatesNothing()
    {
        await using var host = await EventsHost.StartAsync(World().Resolver());
        host.MayCreateEventsUnder(Projects, FlaggedNotIsolatedProject);

        var response = await host.SendAsync(Authenticated(HttpMethod.Post, "/api/v1/events",
            new { subject = "Filing deadline", regardingRecordType = 0, regardingRecordId = FlaggedNotIsolatedProject }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        host.Events.Creates.Should().BeEmpty();
    }

    // =====================================================================================
    // POST /api/ai/document-intelligence/associate-record — DELETED (task 164, owner round 10 item 1)
    // =====================================================================================
    // The seven re-file tests task 146 r1/c1 pinned here (Write on the document, AppendTo on the target, F3 on a move out
    // of a secure matter, the named-team re-own, the 400 before any rights query) protected a route task 164 retired (no
    // caller, not published). Its absence — from the endpoint table AND on the wire — is pinned through the real Program by
    // tests/integration/regression/AiPlaybookPromptRecordMatchRouteRetirementTests.cs; a document re-file now goes only
    // through PUT /api/v1/documents/{id}, whose re-file authorization (AuthorizeRefileTargetsAsync) and F3 gate are 146's
    // and are pinned on that route by SecureChildOwnershipDocumentRefileTests and DocumentRefileRestampRouteTests.

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static Directory SecureProjectEvent(Guid eventId) => World()
        .WithRecord("sprk_event", eventId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", SecureProject) });

    private static EventEntity SecureProjectEventEntity(Guid eventId) => new()
    {
        Id = eventId,
        Name = "Hearing",
        StatusCode = 3,
        RegardingRecordType = Spaarke.Dataverse.RegardingRecordType.Project,
        RegardingProjectId = SecureProject,
    };

    private static async Task<string?> ReasonCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>();

    private static HttpRequestMessage Authenticated(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    /// <summary>Records what the event routes write at the IEventDataverseService boundary.</summary>
    internal sealed class RecordingEventService : IEventDataverseService
    {
        public EventEntity? Existing { get; set; }
        public List<Spaarke.Dataverse.CreateEventRequest> Creates { get; } = new();
        public List<(Guid Id, int StatusCode)> StatusUpdates { get; } = new();
        public List<Guid?> LogOwners { get; } = new();

        /// <summary>The creator person each log row was written with (task 146 c1-r1).</summary>
        public List<Guid?> LogPersons { get; } = new();

        public Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Existing is { } e && e.Id == id ? e : null);

        public Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(Spaarke.Dataverse.CreateEventRequest request, CancellationToken ct = default)
        {
            Creates.Add(request);
            return Task.FromResult((Guid.NewGuid(), DateTime.UtcNow));
        }

        public Task<Guid> CreateEventLogAsync(
            Guid eventId, int action, string? description, Guid? owningTeamId, Guid? createdByPersonId = null,
            CancellationToken ct = default)
        {
            LogOwners.Add(owningTeamId);
            LogPersons.Add(createdByPersonId);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
            int? regardingRecordType = null, Guid? regardingRecordId = null, Guid? eventTypeId = null, int? statusCode = null,
            int? priority = null, DateTime? dueDateFrom = null, DateTime? dueDateTo = null, int skip = 0, int top = 50,
            Guid? ownerUserId = null, IReadOnlyCollection<int>? excludeStatusCodes = null, CancellationToken ct = default) =>
            Task.FromResult((Array.Empty<EventEntity>(), 0));

        public Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsCallerAsync(
            Guid callerSystemUserId, int? regardingRecordType = null, Guid? regardingRecordId = null,
            Guid? regardingRecordTypeRefId = null, Guid? eventTypeId = null, int? statusCode = null, int? priority = null,
            DateTime? dueDateFrom = null, DateTime? dueDateTo = null, int skip = 0, int top = 50, EventOwnershipScope? mine = null,
            CancellationToken ct = default) =>
            Task.FromResult((Array.Empty<EventEntity>(), 0));

        public Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default)
        {
            StatusUpdates.Add((id, statusCode));
            return Task.CompletedTask;
        }

    }

    /// <summary>The shared minimal host: fake authentication, the real authorization stack at the access seam.</summary>
    internal abstract class OwnershipHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource Access { get; } = new();

        /// <summary>The F3 probe (task 146 c1): WhoAmI = <see cref="ProbeCaller"/>; rights = the grants in <see cref="Access"/>.</summary>
        public GrantsProbe Probe { get; }

        protected OwnershipHost()
        {
            Probe = new GrantsProbe(Access);
        }

        protected async Task InitializeAsync(IRecordOwnershipResolver resolver)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(opt =>
                opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

            builder.Services.AddSingleton<IAccessDataSource>(Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton(resolver);
            builder.Services.AddSingleton<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>(Probe);
            // Batch 4 integration (task 156): the re-file routes re-stamp after the write ([FromServices] CoreAncestorRestamper)
            // — an in-memory restamper over an empty world (nothing filed under the re-filed rows here).
            builder.Services.AddSingleton(new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper);
            Register(builder.Services);

            builder.WebHost.UseTestServer();
            _app = builder.Build();
            _app.UseRouting();
            _app.UseAuthentication();
            _app.UseAuthorization();
            _app.UseRateLimiter();
            Map(_app);

            await _app.StartAsync();
            _client = _app.GetTestClient();
        }

        protected abstract void Register(IServiceCollection services);

        protected abstract void Map(WebApplication app);

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client!.SendAsync(request);

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

    /// <summary>
    /// The caller-scoped probe F3 asks (task 146 c1), answering from the SAME stated grants as the access seam, so one
    /// statement of the caller's rights decides both the route filters and the move-out check. WhoAmI is
    /// <see cref="ProbeCaller"/>.
    /// </summary>
    internal sealed class GrantsProbe : Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe
    {
        private readonly FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource _access;

        public GrantsProbe(FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource access)
            : base(new HttpClient(),
                   new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                   Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>.Instance)
        {
            _access = access;
        }

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(ProbeCaller);

        /// <summary>The table privileges the caller holds (task 159's create gate asks for <c>prvCreatesprk_Event</c>).</summary>
        public HashSet<string> HeldPrivileges { get; } = new(StringComparer.Ordinal);

        public override Task<bool> CallerHoldsPrivilegeAsync(
            string? callerBearerToken, string privilegeName, CancellationToken ct = default) =>
            Task.FromResult(!string.IsNullOrEmpty(callerBearerToken) && HeldPrivileges.Contains(privilegeName));

        public override async Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default) =>
            (await _access.GetRecordAccessAsync(ProbeCaller.ToString(), entitySet, recordId, callerBearerToken, ct)).AccessRights;
    }

    internal sealed class EventsHost : OwnershipHost
    {
        public RecordingEventService Events { get; } = new();

        public static async Task<EventsHost> StartAsync(IRecordOwnershipResolver resolver)
        {
            var host = new EventsHost();
            await host.InitializeAsync(resolver);
            return host;
        }

        /// <summary>
        /// Task 159's create gate, as the caller: the <c>prvCreatesprk_Event</c> privilege and AppendTo on the regarding
        /// record. The owner question (task 146) is asked only after it passes.
        /// </summary>
        public void MayCreateEventsUnder(string entitySet, Guid regardingId)
        {
            Probe.HeldPrivileges.Add(EventEndpoints.CreateEventPrivilege);
            Access.Grant(entitySet, regardingId, AccessRights.AppendTo);
        }

        protected override void Register(IServiceCollection services)
        {
            services.AddSingleton<IEventDataverseService>(Events);
            services.AddSingleton(Mock.Of<ICommunicationDataverseService>());
            services.AddSingleton(Mock.Of<IMembershipEventPublisher>());

            // Task 159 (sweep integration): the create resolves the regarding write set — the entity set from live
            // metadata, the target's name/number and the FR-26 core stamps — through IGenericEntityService and the
            // REAL CoreAncestorResolver. A row read answers an empty row (no core lookups populated).
            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string logicalName, CancellationToken _) => logicalName + "s");
            entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => new Entity(entity, id));
            services.AddSingleton(entities.Object);
            services.AddSingleton(new CoreAncestorResolver(
                entities.Object,
                (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                    CoreAncestorResolver.CoreAncestorLookups.Select(c => c.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CoreAncestorResolver>.Instance));

            // Task 152 (merged after 146): the create also names the person the event is FOR (sprk_assignedto). Not
            // under test here — the caller resolves to no systemuser, so it is left blank.
            var callerResolver = new Mock<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver>();
            callerResolver
                .Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Unresolved("not under test"));
            services.AddSingleton(callerResolver.Object);
            services.AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact());
        }

        protected override void Map(WebApplication app) => app.MapEventEndpoints();
    }
}
