using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 r1 (verifier items 5 and 7) — the EXTERNAL PORTAL writer family through the REAL
/// <c>/api/v1/external</c> routes and the REAL <see cref="RecordOwnershipResolver"/> over <see cref="Directory"/>: a to-do,
/// an event and an uploaded document created on a secure root are owned by the named Secure team, never the root's
/// business unit and never the Secure business unit's default team; a root flagged secure but not isolated refuses with
/// a 409 and nothing is created.
/// </summary>
/// <remarks>
/// The contact's authorization is unchanged by ownership (task 146 constraint): the principal is the same stub the
/// external scope suites use, and the owner is asserted at the <see cref="ExternalDataService"/> create boundary — the
/// team the route resolved and handed to the create. The upload's container and bytes are module-boundary doubles.
/// </remarks>
[Trait("status", "new")]
public sealed class SecureChildOwnershipExternalTests : IClassFixture<ExternalChildOwnershipTestFixture>
{
    private readonly ExternalChildOwnershipTestFixture _fixture;

    public SecureChildOwnershipExternalTests(ExternalChildOwnershipTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("matters")]
    [InlineData("workassignments")]
    public async Task ExternalTodo_OnASecureRoot_IsOwnedByTheNamedSecureTeam(string segment)
    {
        _fixture.Reset();
        var rootId = ExternalChildOwnershipTestFixture.SecureRootFor(segment);
        _fixture.Principal = ExternalChildOwnershipTestFixture.FullAccessTo(rootId);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/v1/external/{segment}/{rootId}/todos", new { sprk_name = "Respond" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.TodoOwners.Should().Equal(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task ExternalTodo_OnAnOrdinaryProject_IsOwnedByThatProjectsBusinessUnitTeam()
    {
        _fixture.Reset();
        _fixture.Principal = ExternalChildOwnershipTestFixture.FullAccessTo(ExternalChildOwnershipTestFixture.OrdinaryProject);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/projects/{ExternalChildOwnershipTestFixture.OrdinaryProject}/todos", new { sprk_name = "Respond" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.TodoOwners.Should().Equal(Directory.ChildTeam);
    }

    [Fact]
    public async Task ExternalEvent_OnASecureProject_IsOwnedByTheNamedSecureTeam()
    {
        // The verifier's plant: resolving the event's owner from sprk_matter instead of sprk_project passed every test.
        // Here the PROJECT is secure and no matter with that id exists, so a wrong root type refuses rather than passes.
        _fixture.Reset();
        _fixture.Principal = ExternalChildOwnershipTestFixture.FullAccessTo(ExternalChildOwnershipTestFixture.SecureProject);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/projects/{ExternalChildOwnershipTestFixture.SecureProject}/events", new { sprk_name = "Hearing" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.EventOwners.Should().Equal(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task ExternalEvent_OnAFlaggedButNotIsolatedProject_Is409WithTheStableCode_AndCreatesNothing()
    {
        _fixture.Reset();
        _fixture.Principal = ExternalChildOwnershipTestFixture.FullAccessTo(ExternalChildOwnershipTestFixture.FlaggedProject);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/external/projects/{ExternalChildOwnershipTestFixture.FlaggedProject}/events", new { sprk_name = "Hearing" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _fixture.Data.EventOwners.Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalDocumentUpload_ToASecureProject_IsOwnedByTheNamedSecureTeam()
    {
        _fixture.Reset();
        _fixture.Principal = ExternalChildOwnershipTestFixture.FullAccessTo(ExternalChildOwnershipTestFixture.SecureProject);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsync(
            $"/api/v1/external/projects/{ExternalChildOwnershipTestFixture.SecureProject}/documents", FileForm());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Data.DocumentOwners.Should().Equal(Directory.SecureNamedTeam);
        _fixture.Uploads.Should().Be(1);
    }

    [Fact]
    public async Task ExternalDocumentUpload_ToAFlaggedButNotIsolatedProject_Is409_AndUploadsNoBytesAndCreatesNoRow()
    {
        _fixture.Reset();
        _fixture.Principal = ExternalChildOwnershipTestFixture.FullAccessTo(ExternalChildOwnershipTestFixture.FlaggedProject);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsync(
            $"/api/v1/external/projects/{ExternalChildOwnershipTestFixture.FlaggedProject}/documents", FileForm());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be(RecordOwnerRefusal.SecureParentNotIsolated);
        _fixture.Uploads.Should().Be(0, "the owner is resolved BEFORE the bytes go to SPE");
        _fixture.Data.DocumentOwners.Should().BeEmpty();
    }

    // Task 146 r2 (verifier item 3): every external create payload binds the owner the route resolved, as the
    // `ownerid@odata.bind` Dataverse reads. The seeded removal of the to-do's bind passed every test before this.
    [Fact]
    public void ExternalCreatePayloads_EachBindTheResolvedOwnerTeam()
    {
        var team = Directory.SecureNamedTeam;
        var projectId = ExternalChildOwnershipTestFixture.SecureProject;

        var todo = ExternalDataService.BuildTodoCreatePayload(
            new CreateExternalTodoRequest { SprkName = "Respond" },
            ExternalDataService.TryGetRootBinding(ExternalDataService.TodoRootKind.Project)!, projectId, "Project", null, team);
        var @event = ExternalDataService.BuildEventCreatePayload(
            projectId, new CreateExternalEventRequest { SprkName = "Hearing" }, team);
        var document = ExternalDataService.BuildDocumentCreatePayload(
            projectId, new ExternalUploadedFilePointers("b!drive", "item-1", "brief.pdf", 3, null), team);

        foreach (var payload in new[] { todo, @event, document })
        {
            payload[ExternalDataService.OwnerBindKey].Should().Be($"/teams({team})");
            ExternalChildOwnershipTestFixture.RecordingExternalDataService.BoundOwner(payload).Should().Be(team);
        }
    }

    private static MultipartFormDataContent FileForm()
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "brief.pdf");
        return form;
    }

    private static async Task<string?> ReasonCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())?["reasonCode"]?.GetValue<string>();
}

/// <summary>
/// The external collaboration host with the REAL owner resolver over an in-memory directory (secure, ordinary and
/// flagged-not-isolated roots of each type), the scope suites' principal stub, a recording
/// <see cref="ExternalDataService"/> and module-boundary doubles for the upload's container and bytes.
/// </summary>
public sealed class ExternalChildOwnershipTestFixture : ExternalCollaborationTestFixture
{
    public static readonly Guid SecureProject = Guid.Parse("a1460000-0000-4000-8000-000000000001");
    public static readonly Guid OrdinaryProject = Guid.Parse("a1460000-0000-4000-8000-000000000002");
    public static readonly Guid FlaggedProject = Guid.Parse("a1460000-0000-4000-8000-000000000003");
    public static readonly Guid SecureMatter = Guid.Parse("a1460000-0000-4000-8000-000000000004");
    public static readonly Guid SecureWorkAssignment = Guid.Parse("a1460000-0000-4000-8000-000000000005");

    private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();
    private readonly IRecordOwnershipResolver _resolver = Directory.Standard()
        .WithSecureRoot("sprk_project", SecureProject)
        .WithOrdinaryRoot("sprk_project", OrdinaryProject)
        .WithRecord("sprk_project", FlaggedProject, Directory.ChildBu, isSecure: true, owningTeam: Directory.ChildTeam)
        .WithSecureRoot("sprk_matter", SecureMatter)
        .WithSecureRoot("sprk_workassignment", SecureWorkAssignment)
        .Resolver();

    public RecordingExternalDataService Data { get; } = new();

    public int Uploads { get; private set; }

    public CallerPrincipal? Principal
    {
        get => _principals.Principal;
        set => _principals.Principal = value;
    }

    public static Guid SecureRootFor(string segment) => segment switch
    {
        "projects" => SecureProject,
        "matters" => SecureMatter,
        _ => SecureWorkAssignment,
    };

    /// <summary>A contact holding Full Access on <paramref name="rootId"/> whatever its type.</summary>
    public static CallerPrincipal FullAccessTo(Guid rootId)
    {
        var rights = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.FullAccess);
        return new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.Parse("99999999-9999-9999-9999-999999999146"),
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(rootId, ExternalAccessLevel.FullAccess) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [rootId] = rights },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [rootId] = rights },
        };
    }

    public void Reset()
    {
        Data.Reset();
        Uploads = 0;
        Principal = null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton(_resolver);
            services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
            services.AddScoped(_ => (ExternalDataService)Data);

            // The upload's container: every project here is securable and carries its own container.
            var registry = new Mock<ISecurableEntityRegistry>();
            registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(EntitySecurability.Securable);
            var records = new Mock<IGenericEntityService>();
            records.Setup(r => r.RetrieveAsync("sprk_project", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => new Entity(entity, id)
                {
                    ["sprk_issecure"] = true,
                    ["sprk_containerid"] = "b!secure-project-container",
                });
            services.RemoveAll<RecordContainerResolver>();
            services.AddSingleton(new RecordContainerResolver(
                registry.Object, records.Object, NullLogger<RecordContainerResolver>.Instance));

            var files = new Mock<ISpeFileOperations>();
            files.Setup(f => f.UploadSmallAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                .Callback(() => Uploads++)
                .ReturnsAsync(new FileHandleDto("item-1", "brief.pdf", null, 3, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    null, false, "https://contoso.example/brief.pdf", "b!secure-project-container"));
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(files.Object);
        });
    }

    /// <summary>Records the owning team every external create was handed, per child type.</summary>
    public sealed class RecordingExternalDataService : ExternalDataService
    {
        public RecordingExternalDataService()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), new StubCredential(),
                   NullLogger<ExternalDataService>.Instance)
        {
        }

        public List<Guid> TodoOwners { get; } = new();
        public List<Guid> EventOwners { get; } = new();
        public List<Guid> DocumentOwners { get; } = new();

        public void Reset()
        {
            TodoOwners.Clear();
            EventOwners.Clear();
            DocumentOwners.Clear();
        }

        // Task 146 r2 (verifier item 3): each override records the owner the REAL payload builder bound — the very
        // `ownerid@odata.bind` the real create serializes and POSTs — not the argument it was handed. A builder that
        // drops the bind records Guid.Empty, so the owner assertions above fail instead of passing on the argument.

        public override Task<ExternalTodoDto> CreateTodoAsync(
            TodoRootKind rootKind, Guid rootId, CreateExternalTodoRequest request, Guid owningTeamId,
            Guid? callerContactId, CancellationToken ct = default)
        {
            TodoOwners.Add(BoundOwner(
                BuildTodoCreatePayload(
                    request, TryGetRootBinding(rootKind)!, rootId, "root", null, owningTeamId, callerContactId)));
            return Task.FromResult(new ExternalTodoDto { SprkTodoid = Guid.NewGuid().ToString(), SprkName = request.SprkName });
        }

        public override Task<ExternalEventDto> CreateEventAsync(
            Guid projectId, CreateExternalEventRequest request, Guid owningTeamId, CancellationToken ct = default)
        {
            EventOwners.Add(BoundOwner(BuildEventCreatePayload(projectId, request, owningTeamId)));
            return Task.FromResult(new ExternalEventDto { SprkEventid = Guid.NewGuid().ToString(), SprkName = request.SprkName });
        }

        public override Task<ExternalDocumentDto> CreateDocumentAsync(
            Guid projectId, ExternalUploadedFilePointers pointers, Guid owningTeamId, CancellationToken ct = default)
        {
            DocumentOwners.Add(BoundOwner(BuildDocumentCreatePayload(projectId, pointers, owningTeamId)));
            return Task.FromResult(new ExternalDocumentDto { SprkDocumentid = Guid.NewGuid().ToString(), SprkName = pointers.FileName });
        }

        /// <summary>The team a create payload binds as its owner (<c>ownerid@odata.bind = /teams(id)</c>), or
        /// <see cref="Guid.Empty"/> when it binds none.</summary>
        internal static Guid BoundOwner(IReadOnlyDictionary<string, object?> payload) =>
            payload.TryGetValue(OwnerBindKey, out var bind)
            && bind is string path
            && path.StartsWith("/teams(", StringComparison.Ordinal)
            && path.EndsWith(')')
            && Guid.TryParse(path["/teams(".Length..^1], out var team)
                ? team
                : Guid.Empty;

        private sealed class StubCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct) =>
                new("stub-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct) =>
                new(new AccessToken("stub-token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
