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
/// <para><b>PUT /api/v1/events/{id}</b> (verifier items 2, 9, 18): the owner is re-derived from EXACTLY the regarding lookups
/// the PATCH writes; a re-file is authorized as the caller (Write on the event, AppendTo on the target); a status log row
/// whose owner is refused answers 409 BEFORE anything is written.</para>
/// <para><b>POST /api/ai/document-intelligence/associate-record</b> (verifier item 1): authorized as the caller (Write on the
/// document, AppendTo on the target) before the reparent runs.</para>
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
    // PUT /api/v1/events/{id} — re-file
    // =====================================================================================

    [Fact]
    public async Task EventRefile_SecureProjectToOrdinaryProject_WritesTheLookupItReownsFrom_AndTheyAgree()
    {
        // The verifier's reproduction: the owner used to follow sprk_regardingproject while the PATCH never wrote it.
        var eventId = Guid.NewGuid();
        var world = SecureProjectEvent(eventId);
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = SecureProjectEventEntity(eventId);
        host.Access.Grant(Events, eventId, AccessRights.Read | AccessRights.Write);
        host.Access.Grant(Projects, OrdinaryProject, AccessRights.Read | AccessRights.AppendTo);

        var response = await host.PutEventAsync(eventId, new { regardingRecordType = 0, regardingRecordId = OrdinaryProject });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var written = host.Events.Updates.Should().ContainSingle().Subject;
        var payload = DataverseWebApiService.BuildEventUpdatePayload(written);
        payload["sprk_RegardingProject@odata.bind"].Should().Be($"/sprk_projects({OrdinaryProject})",
            "the lookup the owner is re-derived from must be the lookup that is written");
        world.Assignments.Should().Equal(("sprk_event", eventId, Directory.ChildTeam));
        world.Row("sprk_event", eventId).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(Directory.ChildTeam, "read back");
    }

    [Fact]
    public async Task EventRefile_ProjectToMatter_ClearsThePreviousTypesLookup_AndOwnsFromTheMatter()
    {
        var eventId = Guid.NewGuid();
        var world = SecureProjectEvent(eventId);
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = SecureProjectEventEntity(eventId);
        host.Access.Grant(Events, eventId, AccessRights.Write);
        host.Access.Grant(Matters, OrdinaryMatter, AccessRights.AppendTo);

        var response = await host.PutEventAsync(eventId, new { regardingRecordType = 1, regardingRecordId = OrdinaryMatter });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = DataverseWebApiService.BuildEventUpdatePayload(host.Events.Updates.Single());
        payload["sprk_RegardingMatter@odata.bind"].Should().Be($"/sprk_matters({OrdinaryMatter})");
        payload.Should().ContainKey("sprk_RegardingProject@odata.bind")
            .WhoseValue.Should().BeNull("moving the event to a matter must un-file it from the secure project");
        world.Assignments.Should().Equal(("sprk_event", eventId, Directory.ChildTeam));
    }

    [Fact]
    public async Task EventRefile_ByACallerWithoutWriteOnTheEvent_Is403_AndNothingIsWrittenOrReowned()
    {
        var eventId = Guid.NewGuid();
        var world = SecureProjectEvent(eventId);
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = SecureProjectEventEntity(eventId);
        host.Access.Grant(Projects, OrdinaryProject, AccessRights.AppendTo); // the target, but not the event

        var response = await host.PutEventAsync(eventId, new { regardingRecordType = 0, regardingRecordId = OrdinaryProject });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Events.Updates.Should().BeEmpty();
        world.Assignments.Should().BeEmpty("a signed-in user must not move a secure event out by knowing its id");
    }

    [Fact]
    public async Task EventRefile_WithoutAppendToOnTheTarget_Is403_AndNothingIsWritten()
    {
        var eventId = Guid.NewGuid();
        var world = SecureProjectEvent(eventId);
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = SecureProjectEventEntity(eventId);
        host.Access.Grant(Events, eventId, AccessRights.Write);

        var response = await host.PutEventAsync(eventId, new { regardingRecordType = 0, regardingRecordId = OrdinaryProject });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Events.Updates.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task EventRefile_UnderAFlaggedButNotIsolatedProject_Is409WithTheStableCode_AndNothingIsWritten()
    {
        var eventId = Guid.NewGuid();
        var world = World().WithRecord("sprk_event", eventId, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", OrdinaryProject) });
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = new EventEntity
        {
            Id = eventId, Name = "Hearing", StatusCode = 3,
            RegardingRecordType = 0, RegardingProjectId = OrdinaryProject,
        };
        host.Access.Grant(Events, eventId, AccessRights.Write);
        host.Access.Grant(Projects, FlaggedNotIsolatedProject, AccessRights.AppendTo);

        var response = await host.PutEventAsync(
            eventId, new { regardingRecordType = 0, regardingRecordId = FlaggedNotIsolatedProject });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        host.Events.Updates.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task EventStatusUpdate_WhenItsLogRowIsRefusedAnOwner_Is409BeforeAnythingIsWritten()
    {
        // Verifier item 9: the 409 used to come AFTER the update had landed. A user-owned (pre-146) event filed under a
        // flagged-but-not-isolated project: its log row's owner refuses — and now nothing at all is written.
        var eventId = Guid.NewGuid();
        var world = World().WithRecord("sprk_event", eventId, Directory.GeneralBu, owningTeam: null,
            extra: new() { ["sprk_regardingproject"] = new EntityReference("sprk_project", FlaggedNotIsolatedProject) });
        await using var host = await EventsHost.StartAsync(world.Resolver());
        host.Events.Existing = new EventEntity
        {
            Id = eventId, Name = "Hearing", StatusCode = 3,
            RegardingRecordType = 0, RegardingProjectId = FlaggedNotIsolatedProject,
        };

        var response = await host.PutEventAsync(eventId, new { statusCode = 4 });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        host.Events.Updates.Should().BeEmpty("a client that sees 409 must be able to retry: nothing was written");
        host.Events.LogOwners.Should().BeEmpty();
    }

    [Fact]
    public async Task EventCreate_RegardingASecureProject_IsOwnedByTheNamedSecureTeam_AndSoIsItsLogRow()
    {
        await using var host = await EventsHost.StartAsync(World().Resolver());

        var response = await host.SendAsync(Authenticated(HttpMethod.Post, "/api/v1/events",
            new { subject = "Filing deadline", regardingRecordType = 0, regardingRecordId = SecureProject }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Events.Creates.Should().ContainSingle().Which.OwningTeamId.Should().Be(Directory.SecureNamedTeam);
        host.Events.LogOwners.Should().Equal(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task EventCreate_RegardingAFlaggedButNotIsolatedProject_Is409_AndCreatesNothing()
    {
        await using var host = await EventsHost.StartAsync(World().Resolver());

        var response = await host.SendAsync(Authenticated(HttpMethod.Post, "/api/v1/events",
            new { subject = "Filing deadline", regardingRecordType = 0, regardingRecordId = FlaggedNotIsolatedProject }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        host.Events.Creates.Should().BeEmpty();
    }

    [Fact]
    public void EventUpdate_ParentChangesAreExactlyTheLookupsThePatchWrites()
    {
        // The pin behind the fix: ONE derivation (UpdateEventRequest.RegardingLookupWrites) feeds both sides.
        var update = new Spaarke.Dataverse.UpdateEventRequest
        {
            RegardingRecordType = Spaarke.Dataverse.RegardingRecordType.Matter,
            RegardingRecordId = OrdinaryMatter.ToString(),
            PreviousRegardingRecordType = Spaarke.Dataverse.RegardingRecordType.Project,
        };

        var changes = EventEndpoints.ParentChangesFor(update);
        var payload = DataverseWebApiService.BuildEventUpdatePayload(update);

        changes.Keys.Should().BeEquivalentTo("sprk_regardingmatter", "sprk_regardingproject");
        changes["sprk_regardingmatter"]!.Id.Should().Be(OrdinaryMatter);
        changes["sprk_regardingproject"].Should().BeNull();
        payload.Keys.Where(k => k.EndsWith("@odata.bind", StringComparison.Ordinal) && k != "sprk_EventType_Ref@odata.bind")
            .Select(k => k[..^"@odata.bind".Length].ToLowerInvariant())
            .Should().BeEquivalentTo(changes.Keys);
    }

    // =====================================================================================
    // POST /api/ai/document-intelligence/associate-record
    // =====================================================================================

    [Fact]
    public async Task AssociateRecord_ByACallerWithNoRightsOnTheDocument_Is403_AndTheSecureDocumentStaysPut()
    {
        // The verifier's reproduction: a secure document whose only secure lookup is sprk_matter, re-filed by GUID to an
        // ordinary matter by any signed-in user — it used to land with that matter's business unit team.
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", SecureMatter) });
        await using var host = await RecordMatchHost.StartAsync(world.Resolver());
        host.Access.Grant(Matters, OrdinaryMatter, AccessRights.AppendTo); // the target only

        var response = await host.AssociateAsync(documentId, OrdinaryMatter, "matter");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.DocumentUpdates.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task AssociateRecord_WithoutAppendToOnTheTarget_Is403_AndWritesNothing()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        await using var host = await RecordMatchHost.StartAsync(world.Resolver());
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);

        var response = await host.AssociateAsync(documentId, SecureMatter, "sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.DocumentUpdates.Should().BeEmpty();
        world.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task AssociateRecord_Authorized_IntoASecureMatter_IsReownedByTheNamedTeam_ReadBack()
    {
        var documentId = Guid.NewGuid();
        var world = World().WithRecord("sprk_document", documentId, Directory.ChildBu, owningTeam: Directory.ChildTeam,
            extra: new() { ["sprk_project"] = new EntityReference("sprk_project", OrdinaryProject) });
        await using var host = await RecordMatchHost.StartAsync(world.Resolver());
        host.Access.Grant(Documents, documentId, AccessRights.Read | AccessRights.Write);
        host.Access.Grant(Matters, SecureMatter, AccessRights.AppendTo);

        var response = await host.AssociateAsync(documentId, SecureMatter, "matter");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.DocumentUpdates.Should().ContainSingle().Which.MatterLookup.Should().Be(SecureMatter);
        world.Assignments.Should().Equal(("sprk_document", documentId, Directory.SecureNamedTeam));
        world.Row("sprk_document", documentId).GetAttributeValue<EntityReference>("owningteam").Id
            .Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task AssociateRecord_WithAnUnsupportedRecordType_Is400_BeforeAnyRightsQuery()
    {
        await using var host = await RecordMatchHost.StartAsync(World().Resolver());

        var response = await host.AssociateAsync(Guid.NewGuid(), Guid.NewGuid(), "account");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().BeEmpty();
        host.DocumentUpdates.Should().BeEmpty();
    }

    [Fact]
    public void AssociateRecord_DeclaresWriteOnTheDocumentAndAppendToOnTheTarget()
    {
        AssociateRecordEndpointsContract.TargetOperation.Should().Be("entity.associate_document");
        OperationAccessPolicy.GetRequiredRights(AssociateRecordEndpointsContract.TargetOperation)
            .Should().Be(AccessRights.AppendTo);
    }

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
        public List<Spaarke.Dataverse.UpdateEventRequest> Updates { get; } = new();
        public List<Spaarke.Dataverse.CreateEventRequest> Creates { get; } = new();
        public List<Guid?> LogOwners { get; } = new();

        public Task<EventEntity?> GetEventAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Existing is { } e && e.Id == id ? e : null);

        public Task UpdateEventAsync(Guid id, Spaarke.Dataverse.UpdateEventRequest request, CancellationToken ct = default)
        {
            Updates.Add(request);
            return Task.CompletedTask;
        }

        public Task<(Guid Id, DateTime CreatedOn)> CreateEventAsync(Spaarke.Dataverse.CreateEventRequest request, CancellationToken ct = default)
        {
            Creates.Add(request);
            return Task.FromResult((Guid.NewGuid(), DateTime.UtcNow));
        }

        public Task<Guid> CreateEventLogAsync(Guid eventId, int action, string? description, Guid? owningTeamId, CancellationToken ct = default)
        {
            LogOwners.Add(owningTeamId);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<(EventEntity[] Items, int TotalCount)> QueryEventsAsync(
            int? regardingRecordType = null, string? regardingRecordId = null, Guid? eventTypeId = null, int? statusCode = null,
            int? priority = null, DateTime? dueDateFrom = null, DateTime? dueDateTo = null, int skip = 0, int top = 50,
            Guid? ownerUserId = null, CancellationToken ct = default) =>
            Task.FromResult((Array.Empty<EventEntity>(), 0));

        public Task UpdateEventStatusAsync(Guid id, int statusCode, DateTime? completedDate = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<EventLogEntity[]> QueryEventLogsAsync(Guid eventId, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<EventLogEntity>());

        public Task<EventTypeEntity[]> GetEventTypesAsync(bool activeOnly = true, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<EventTypeEntity>());

        public Task<EventTypeEntity?> GetEventTypeAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<EventTypeEntity?>(null);
    }

    /// <summary>The shared minimal host: fake authentication, the real authorization stack at the access seam.</summary>
    internal abstract class OwnershipHost : IAsyncDisposable
    {
        private WebApplication? _app;
        private HttpClient? _client;

        public FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource Access { get; } = new();

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
            // Task 156 (merged with 146): a re-file re-stamps the copies under the record after it stands. Not under test
            // here — an empty in-memory world, so the re-stamp finds nothing to move and never throws.
            builder.Services.AddSingleton(
                new Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping.StampWorld().Restamper);
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

    internal sealed class EventsHost : OwnershipHost
    {
        public RecordingEventService Events { get; } = new();

        public static async Task<EventsHost> StartAsync(IRecordOwnershipResolver resolver)
        {
            var host = new EventsHost();
            await host.InitializeAsync(resolver);
            return host;
        }

        public Task<HttpResponseMessage> PutEventAsync(Guid id, object body) =>
            SendAsync(Authenticated(HttpMethod.Put, $"/api/v1/events/{id}", body));

        protected override void Register(IServiceCollection services)
        {
            services.AddSingleton<IEventDataverseService>(Events);
            services.AddSingleton(Mock.Of<ICommunicationDataverseService>());
            services.AddSingleton(Mock.Of<IMembershipEventPublisher>());

            // Task 152 (merged after 146): the create also names the person the event is FOR (sprk_assignedto). Not
            // under test here — the caller resolves to no systemuser, so it is left blank.
            services.AddSingleton(Mock.Of<IGenericEntityService>());
            var callerResolver = new Mock<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver>();
            callerResolver
                .Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolution.Unresolved("not under test"));
            services.AddSingleton(callerResolver.Object);
            services.AddSingleton(Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.NoLinkedContact());
        }

        protected override void Map(WebApplication app) => app.MapEventEndpoints();
    }

    internal sealed class RecordMatchHost : OwnershipHost
    {
        public List<UpdateDocumentRequest> DocumentUpdates { get; } = new();

        public static async Task<RecordMatchHost> StartAsync(IRecordOwnershipResolver resolver)
        {
            var host = new RecordMatchHost();
            await host.InitializeAsync(resolver);
            return host;
        }

        public Task<HttpResponseMessage> AssociateAsync(Guid documentId, Guid recordId, string recordType) =>
            SendAsync(Authenticated(HttpMethod.Post, "/api/ai/document-intelligence/associate-record",
                new { documentId = documentId.ToString(), recordId = recordId.ToString(), recordType }));

        protected override void Register(IServiceCollection services)
        {
            var documents = new Mock<IDocumentDataverseService>(MockBehavior.Strict);
            documents
                .Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
                .Callback<string, UpdateDocumentRequest, CancellationToken>((_, r, _) => DocumentUpdates.Add(r))
                .Returns(Task.CompletedTask);
            services.AddSingleton(documents.Object);
            services.AddSingleton(Mock.Of<IRecordMatchService>());
        }

        protected override void Map(WebApplication app) => app.MapRecordMatchEndpoints();
    }
}

/// <summary>The associate-record contract constants, read through the endpoint class (internal, InternalsVisibleTo).</summary>
internal static class AssociateRecordEndpointsContract
{
    public const string TargetOperation = RecordMatchEndpoints.AssociateTargetOperation;
}
