using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 158 r1 (AC 1, the verifier's LOW: "drive each CREATE route end to end") — the two BFF
/// routes that CREATE a work assignment or project filed under a secure record, driven over the REAL host (TestServer, the
/// real routing, auth, endpoint filters and handlers) with the REAL secure-root inheritance and provisioning:
/// <list type="bullet">
/// <item><c>POST /api/office/quickcreate/project</c> — the Office quick-create (its mapping files the project under the matter
/// it is created from).</item>
/// <item><c>POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve</c> — the chat <c>dataverse.create_record</c> tool
/// runs ONLY through the confirmation gate (a side-effect tool, FR-P2-02): confirming it executes the typed handler.</item>
/// </list>
/// Only the module boundaries are substituted (ADR-038): the app-only Dataverse seams (writing into the provisioning
/// fixture's Dataverse), the caller's own Dataverse (scripted OBO client), the chat session and gate store, the tool catalog.
/// Each create comes out created INTO isolation and SECURE (owner round 31 item 2): flag, named team, own container, its
/// creator shared — with no owner move after the create (no business-unit-visible window).
/// </summary>
[Trait("status", "task-158-uac-r2")]
public class SecureRootCreateRouteTests : IClassFixture<SecureRootCreateRouteFixture>
{
    private const string OfficeRoute = "/api/office/quickcreate/project";

    private readonly SecureRootCreateRouteFixture _fixture;

    public SecureRootCreateRouteTests(SecureRootCreateRouteFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SystemUsers[AppUser] = (false, true);
        _fixture.ResetRoutes();
    }

    private void ShouldBeSecure(Guid id, string because)
    {
        _fixture.IsSecureOf(id).Should().BeTrue($"{because}: sprk_issecure");
        _fixture.OwningTeamOf(id).Should().Be(SecureTeam, $"{because}: the named owner team");
        _fixture.ContainerIdOf(id).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId, $"{because}: its own container");
        _fixture.ShareMaskOf(id, Creator).Should().Be(
            RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights), $"{because}: its creator is shared");
        _fixture.Updates.Should().NotContain(u => u.RecordId == id && u.Payload.ContainsKey("ownerid@odata.bind"),
            $"{because}: created owned by the named team — never moved there from the caller's business unit");
    }

    // ── Office quick-create ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Office route end to end: a project quick-created from a SECURE matter (the mapping files it under the matter by
    /// the polymorphic pair) answers 201 and is created into isolation and secure; the caller's own rights were asked first
    /// (G5: Create on the table, AppendTo on the matter).
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_OfAProjectFromASecureMatter_IsCreatedSecure_EndToEnd()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.CallerHoldsAppendTo = true;
        _fixture.MapProjectsUnder(matter, RecordTypeRef(_fixture, "sprk_matter"));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, new
        {
            name = "Lease review",
            sourceEntityType = "sprk_matter",
            sourceRecordId = matter,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var project = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);
        _fixture.OfficeCreates.Should().ContainSingle()
            .Which.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam, "the create itself names the named team");
        ShouldBeSecure(project, "quick-created from a secure matter");
        _fixture.DelegationProbes.Should().Contain(("sprk_matters", matter), "AppendTo on the secure matter, asked as the caller");
    }

    /// <summary>The Office route end to end, negative: the caller cannot file under the secure matter — 403, nothing created.</summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheCallerCannotFileUnderTheSecureMatter_Is403_AndNothingIsCreated()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.MapProjectsUnder(matter, RecordTypeRef(_fixture, "sprk_matter"));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, new
        {
            name = "Lease review",
            sourceEntityType = "sprk_matter",
            sourceRecordId = matter,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.OfficeCreates.Should().BeEmpty();
    }

    // ── Chat: dataverse.create_record through the confirmation gate ─────────────────────────────────────────────────

    /// <summary>
    /// The chat route end to end: confirming a suspended <c>dataverse.create_record</c> of a work assignment regarding a
    /// SECURE matter answers 200 <c>confirmed</c>, and the work assignment is created into isolation and secure.
    /// </summary>
    [Fact]
    public async Task ChatCreate_ConfirmedThroughTheGate_OfAWorkAssignmentUnderASecureMatter_IsCreatedSecure_EndToEnd()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var gate = _fixture.SuspendCreate(JsonSerializer.Serialize(new
        {
            tablename = "sprk_workassignment",
            item = new Dictionary<string, object>
            {
                ["sprk_name"] = "Review the lease",
                ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter },
            },
        }));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(
            $"/api/ai/chat/sessions/{SecureRootCreateRouteFixture.SessionId}/gates/{gate}/resolve", new { approved = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("confirmed");
        var created = Guid.Parse(doc.RootElement.GetProperty("recordId").GetString()!);
        _fixture.ChatCreates.Should().ContainSingle().Which.Id.Should().Be(created);
        ShouldBeSecure(created, "created from chat under a secure matter");
    }

    /// <summary>
    /// The chat route end to end, negative (owner round 31 item 1): the caller is on the secure matter's No Access list —
    /// the confirmed create is refused (502 dispatch failure naming the No Access code), nothing created.
    /// </summary>
    [Fact]
    public async Task ChatCreate_ConfirmedThroughTheGate_ByACallerWalledOffTheSecureMatter_IsRefused_AndNothingIsCreated()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.NoAccessList.DenySystemUserOnRecord(Creator, matter);
        var gate = _fixture.SuspendCreate(JsonSerializer.Serialize(new
        {
            tablename = "sprk_workassignment",
            item = new Dictionary<string, object>
            {
                ["sprk_name"] = "Review the lease",
                ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter },
            },
        }));

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(
            $"/api/ai/chat/sessions/{SecureRootCreateRouteFixture.SessionId}/gates/{gate}/resolve", new { approved = true });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Contain(ProvisionProjectEndpoint.ReasonCreatorNoAccess);
        _fixture.ChatCreates.Should().BeEmpty();
    }
}

/// <summary>
/// The provisioning host (<see cref="ProvisionProjectTestFixture"/>) with the CREATE routes' module boundaries substituted —
/// so the Office quick-create and the chat confirmation gate run over the same real secure-root inheritance and provisioning
/// the transition tests use.
/// </summary>
public sealed class SecureRootCreateRouteFixture : ProvisionProjectTestFixture
{
    /// <summary>The chat session the gate belongs to (owned by the fake principal).</summary>
    public const string SessionId = "15815800-0000-0000-0000-000000000158";

    private const string ToolName = "SYS-Dataverse Create Record";

    /// <summary>The Office app-only creates (the entity as the service sent it).</summary>
    public List<Entity> OfficeCreates { get; } = new();

    /// <summary>The chat tool's app-only creates (table, id).</summary>
    public List<(string Table, Guid Id)> ChatCreates { get; } = new();

    private PendingInvocation? _pending;
    private Guid? _mappedMatter;
    private Guid? _mappedType;

    private readonly TestableChatSessionManager _sessions = new();

    /// <summary>Clears the route-side state (the provisioning state is <see cref="ProvisionProjectTestFixture.Reset"/>'s).</summary>
    public void ResetRoutes()
    {
        OfficeCreates.Clear();
        ChatCreates.Clear();
        _pending = null;
        _mappedMatter = null;
        _mappedType = null;
        _sessions.Session = new ChatSession(
            SessionId: SessionId,
            TenantId: WorkspaceTestConstants.TestTenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: null) { OwnerOid = WorkspaceTestConstants.TestUserId };
    }

    /// <summary>The maker's mapping profile files a new project under <paramref name="matter"/> (the polymorphic pair).</summary>
    public void MapProjectsUnder(Guid matter, Guid typeRef)
    {
        _mappedMatter = matter;
        _mappedType = typeRef;
    }

    /// <summary>A suspended, confirmable <c>dataverse.create_record</c> invocation; returns its gate id.</summary>
    public string SuspendCreate(string argsJson)
    {
        var gateId = Guid.NewGuid().ToString("N");
        _pending = new PendingInvocation
        {
            GateId = gateId,
            SessionId = SessionId,
            TenantId = WorkspaceTestConstants.TestTenantId,
            ToolId = ToolHandlerToAIFunctionAdapter.SanitiseToolName(ToolName),
            SideEffectClass = "write",
            ArgsJson = argsJson,
            Turn = 1,
        };
        return gateId;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // ── Office quick-create: the app-only entity seam, the mapping profile, the caller's systemuser ──
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(OfficeEntities().Object);
            services.RemoveAll<IFieldMappingDataverseService>();
            services.AddSingleton(FieldMappings().Object);
            services.RemoveAll<ICallerSystemUserResolver>();
            var resolver = new Mock<ICallerSystemUserResolver>();
            resolver.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
            services.AddScoped(_ => resolver.Object);
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(new RecordOwnershipResolverDouble());

            // ── Chat: the caller's own Dataverse (scripted OBO), the session, the gate store, the tool catalog ──
            services.RemoveAll<IDataverseUserClient>();
            services.AddScoped<IDataverseUserClient>(_ => new SecureChildOwnershipAiToolTests.ScriptedUserClient(CallerSystemUserId));
            services.RemoveAll<ChatSessionManager>();
            services.AddSingleton<ChatSessionManager>(_sessions);
            services.RemoveAll<PendingPlanManager>();
            services.AddScoped<PendingPlanManager>(sp => new GatePlanManager(this, _sessions,
                sp.GetRequiredService<ILogger<PendingPlanManager>>()));
            services.RemoveAll<AnalysisToolService>();
            services.AddSingleton<AnalysisToolService>(sp => new CatalogWithTheCreateTool(
                sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<Azure.Core.TokenCredential>()));
        });
    }

    private Mock<IGenericEntityService> OfficeEntities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Returns((Entity row, CancellationToken _) =>
            {
                var id = Guid.NewGuid();
                OfficeCreates.Add(row);
                SeedProject(id, owningTeamId: row.GetAttributeValue<EntityReference>("ownerid")?.Id,
                    isSecure: row.GetAttributeValue<bool?>("sprk_issecure") == true,
                    createdBy: AppUser, createdByPerson: row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id);
                if (row.GetAttributeValue<string>("sprk_regardingrecordid") is { } pairId)
                    ChildWorld.Set("sprk_project", id, "sprk_regardingrecordid", pairId);
                if (row.GetAttributeValue<EntityReference>("sprk_regardingrecordtype") is { } pairType)
                    ChildWorld.Set("sprk_project", id, "sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", pairType.Id));
                return Task.FromResult(id);
            });
        entities
            .Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) => Task.FromResult(new Entity("sprk_matter", id)
            {
                ["sprk_matterid"] = id.ToString("D"),
                ["sprk_recordtype"] = new EntityReference("sprk_recordtype_ref", _mappedType ?? Guid.Empty),
            }));
        return entities;
    }

    private Mock<IFieldMappingDataverseService> FieldMappings()
    {
        var mappings = new Mock<IFieldMappingDataverseService>(MockBehavior.Loose);
        mappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_matter", "sprk_project", true, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<FieldMappingProfileEntity?>(_mappedMatter is null ? null : new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "matter to project",
                SourceEntity = "sprk_matter",
                TargetEntity = "sprk_project",
                IsActive = true,
                Rules =
                [
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(), Name = "regarding id", SourceField = "sprk_matterid", SourceFieldType = 0,
                        TargetField = "sprk_regardingrecordid", TargetFieldType = 0, MappingType = 0, ExecutionOrder = 1, IsActive = true,
                    },
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(), Name = "regarding type", SourceField = "sprk_recordtype", SourceFieldType = 1,
                        TargetField = "sprk_regardingrecordtype", TargetFieldType = 1, MappingType = 0, ExecutionOrder = 2, IsActive = true,
                    },
                ],
            }));

        // The chat tool's app-only create (create-by-upsert) lands in the provisioning fixture's Dataverse.
        mappings
            .Setup(m => m.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                ChatCreates.Add((table, id));
                SeedWorkAssignment(id, owningTeamId: Bound(fields, "ownerid@odata.bind"),
                    isSecure: fields.TryGetValue("sprk_issecure", out var flag) && flag is true,
                    createdBy: AppUser, createdByPerson: Bound(fields, "sprk_CreatedByPerson@odata.bind"));
                if (Bound(fields, "sprk_RegardingMatter@odata.bind") is { } matter)
                    ChildWorld.Set(table, id, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
            })
            .Returns(Task.CompletedTask);
        return mappings;
    }

    private static Guid? Bound(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var raw)
        && (raw is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : raw as string) is { } bind
        && Guid.TryParse(bind.TrimEnd(')').Split('(').Last(), out var id)
            ? id
            : null;

    /// <summary>The gate store holding the one suspended invocation the test made (confirmable once).</summary>
    private sealed class GatePlanManager(SecureRootCreateRouteFixture fixture, ChatSessionManager sessions, ILogger<PendingPlanManager> logger)
        : PendingPlanManager(new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache(), sessions, logger)
    {
        public override Task<PendingInvocation?> GetInvocationAsync(
            string tenantId, string sessionId, string gateId, CancellationToken ct = default)
            => Task.FromResult(fixture._pending is { } p && p.GateId == gateId ? p : null);

        public override Task<PendingInvocation?> ResumeInvocationAsync(
            string tenantId, string sessionId, string gateId, CancellationToken ct = default)
        {
            var pending = fixture._pending is { } p && p.GateId == gateId ? p : null;
            fixture._pending = null;
            return Task.FromResult(pending);
        }
    }

    /// <summary>The tool catalog: the chat-available <c>dataverse.create_record</c> row (task 009's seeded row).</summary>
    private sealed class CatalogWithTheCreateTool(IConfiguration configuration, Azure.Core.TokenCredential credential)
        : AnalysisToolService(new HttpClient(), configuration, credential, NullLogger<AnalysisToolService>.Instance)
    {
        public override Task<ScopeListResult<AnalysisTool>> ListToolsAsync(ScopeListOptions options, CancellationToken cancellationToken)
            => Task.FromResult(new ScopeListResult<AnalysisTool>
            {
                Items = new[]
                {
                    new AnalysisTool
                    {
                        Id = Guid.NewGuid(),
                        Name = ToolName,
                        HandlerClass = nameof(DataverseCreateRecordHandler),
                        AvailableInContexts = ToolAvailabilityContext.Chat,
                    },
                },
                TotalCount = 1,
                Page = 1,
                PageSize = 200,
            });
    }
}
