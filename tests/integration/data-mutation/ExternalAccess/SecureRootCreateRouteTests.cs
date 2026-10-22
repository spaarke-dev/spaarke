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
public partial class SecureRootCreateRouteTests : IClassFixture<SecureRootCreateRouteFixture>
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
    /// The Office route end to end: a project quick-created from a SECURE matter (the adapter files it under the matter by
    /// the derived polymorphic pair) answers 201 and is created into isolation and secure; the caller's own rights were asked
    /// first (G5: Create on the table, AppendTo on the matter).
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
        _fixture.CallerDataverse.Requests.Should().Contain(
            r => r.StartsWith("GET systemusers(", StringComparison.Ordinal)
                 && r.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                 && r.Contains($"sprk_matters({matter:D})", StringComparison.Ordinal),
            "AppendTo on the secure matter is asked through the caller's own Dataverse (the unified create's step 5)");
    }

    // ── Office quick-create: G4 (UAC-r2 2026-10-22), only the derived pair is ever written ──────────────────────────

    /// <summary>
    /// G4 condition 1, pinned by the contract's shape: a body carrying extra regarding-style members (naming ANOTHER record)
    /// under a SECURE source is still 201, filed under the DERIVED pair — the source matter, its id in the canonical lower-case
    /// "D" spelling, its catalog type — and secure: <c>sprk_issecure</c> true, owned by the Secure Record Owners team.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderASecureMatter_ABodyWithRegardingMembers_IsFiledUnderTheDerivedPair_AndSecure()
    {
        var matter = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.CatalogMattersAs(matterType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, BodyWithRegardingMembers(matter, elsewhere));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeTrue("the create itself is secure");
        _fixture.OfficeCreates.Single().GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SecureTeam);
        ShouldBeSecure(project, "a body's own regarding members never move a secure-sourced project");
    }

    /// <summary>G4 condition 1, the non-secure control: the same body under an ordinary matter is 201 under the derived pair.</summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAnOrdinaryMatter_ABodyWithRegardingMembers_IsFiledUnderTheDerivedPair()
    {
        var matter = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.CatalogMattersAs(matterType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, BodyWithRegardingMembers(matter, elsewhere));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeFalse("an ordinary source confers nothing");
    }

    /// <summary>
    /// G4 condition 2 (the base test): a SECURE source whose mapping profile has rules aimed at the pair is 201, filed under
    /// the derived pair, secure — and each skipped pair rule's <c>mapping_rule_skipped</c> text reaches the RESPONSE warnings.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderASecureMatter_WithAPairRule_IsFiledUnderTheDerivedPair_Secure_AndWarnsTheRuleWasSkipped()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.MapProjectsUnder(matter, matterType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        ShouldBeSecure(project, "a pair rule never decides isolation");
        var warnings = await WarningsOf(response);
        warnings.Should().Contain(PairRuleSkipped("regarding id", "sprk_regardingrecordid"))
            .And.Contain(PairRuleSkipped("regarding type", "sprk_regardingrecordtype"));
    }

    /// <summary>G4 condition 3, must not fire: a SECURE source with no pair rule is 201, secure, and warns nothing.</summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderASecureMatter_WithNoPairRule_IsSecure_AndHasNoWarning()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.CatalogMattersAs(matterType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var project = ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        ShouldBeSecure(project, "quick-created from a secure matter");
        (await WarningsOf(response)).Should().BeEmpty("no rule was skipped and nothing else is wrong");
    }

    /// <summary>
    /// G4 condition 3, the non-secure control: an ORDINARY source with a pair rule keeps the non-secure behaviour of fill-only
    /// mapping — the pair rule is skipped with the same <c>mapping_rule_skipped</c> warning (fill-only never writes a pair, for
    /// any source), and the project is 201 under the derived pair, not secure.
    /// </summary>
    [Fact]
    public async Task OfficeQuickCreate_UnderAnOrdinaryMatter_WithAPairRule_SkipsItWithTheSameWarning_UnderFillOnly()
    {
        var matter = Guid.NewGuid();
        _fixture.SeedMatter(matter, isSecure: false);
        var matterType = RecordTypeRef(_fixture, "sprk_matter");
        _fixture.MapProjectsUnder(matter, matterType);

        var response = await _fixture.CreateAuthenticatedClient().PostAsJsonAsync(OfficeRoute, Body(matter));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        ShouldBeFiledUnderTheDerivedPair(matter, matterType);
        _fixture.OfficeCreates.Single().GetAttributeValue<bool>("sprk_issecure").Should().BeFalse();
        (await WarningsOf(response)).Should().Contain(PairRuleSkipped("regarding id", "sprk_regardingrecordid"))
            .And.Contain(PairRuleSkipped("regarding type", "sprk_regardingrecordtype"));
    }

    private static object Body(Guid matter) => new { name = "Lease review", sourceEntityType = "sprk_matter", sourceRecordId = matter };

    /// <summary>A body that also carries regarding-style members naming <paramref name="elsewhere"/> (unknown to the contract).</summary>
    private static object BodyWithRegardingMembers(Guid matter, Guid elsewhere) => new Dictionary<string, object>
    {
        ["name"] = "Lease review",
        ["sourceEntityType"] = "sprk_matter",
        ["sourceRecordId"] = matter,
        ["regardingRecordId"] = elsewhere.ToString("D"),
        ["regardingRecordType"] = "sprk_project",
        ["sprk_regardingrecordid"] = elsewhere.ToString("D"),
        ["regardingRecordTypeId"] = Guid.NewGuid(),
    };

    /// <summary>The one Office create is filed under (<paramref name="matter"/>, <paramref name="matterType"/>); returns its id.</summary>
    private Guid ShouldBeFiledUnderTheDerivedPair(Guid matter, Guid matterType)
    {
        var created = _fixture.OfficeCreates.Should().ContainSingle().Subject;
        created.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matter.ToString("D").ToLowerInvariant(),
            "the derived pair's id, in the canonical lower-case \"D\" spelling a text-equality reader matches");
        created.GetAttributeValue<EntityReference>("sprk_regardingrecordtype").Id.Should().Be(matterType, "the derived pair's type");
        return created.Id;
    }

    /// <summary>The fill-only engine's skip text for a rule aimed at the regarding pair (CreateTimeFieldMapping).</summary>
    private static string PairRuleSkipped(string rule, string target) =>
        $"Field-mapping rule \"{rule}\" targeting \"{target}\" was skipped: that field is part of the regarding association and "
        + "is set by the caller, never by a field-mapping rule.";

    private static async Task<IReadOnlyList<string>> WarningsOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array
            ? warnings.EnumerateArray().Select(w => w.GetString()!).ToList()
            : Array.Empty<string>();
    }

    /// <summary>The Office route end to end, negative: the caller cannot file under the secure matter — 403, nothing created.</summary>
    [Fact]
    public async Task OfficeQuickCreate_WhenTheCallerCannotFileUnderTheSecureMatter_Is403_AndNothingIsCreated()
    {
        var matter = Guid.NewGuid();
        SecureMatter(_fixture, matter);
        _fixture.MapProjectsUnder(matter, RecordTypeRef(_fixture, "sprk_matter"));
        _fixture.CallerNoAppendTo.Add(matter); // the caller's own Dataverse withholds AppendTo on the secure matter

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

    /// <summary>The business unit every owning team reads back as, for the Office create's search-index defaults (step 8).</summary>
    private static readonly Guid OfficeBusinessUnit = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a990158");

    /// <summary>The Office app-only creates (the entity as the service sent it).</summary>
    public List<Entity> OfficeCreates { get; } = new();

    /// <summary>Runs right after the Office project's create lands (a test's "walled off after the plan" moment); cleared by <see cref="ResetRoutes"/>.</summary>
    public Action<Guid>? AfterOfficeCreate { get; set; }

    /// <summary>The chat tool's app-only creates (table, id).</summary>
    public List<(string Table, Guid Id)> ChatCreates { get; } = new();

    /// <summary>Records on which the caller's own Dataverse answers WITHOUT AppendTo (RetrievePrincipalAccess as the caller).</summary>
    public HashSet<Guid> CallerNoAppendTo { get; } = new();

    /// <summary>Every question asked of the caller's own Dataverse (OBO), unescaped, in order (field mapping task 018b).</summary>
    public RecordedCallerDataverse CallerDataverse { get; } = new();

    /// <summary>
    /// The owner resolver the Office create's step 7 asks (record-first): every question gets <see cref="RecordOwnershipResolverDouble.TeamId"/>,
    /// and is recorded. Field mapping task 018b-2: a root created INTO isolation never asks it.
    /// </summary>
    internal RecordOwnershipResolverDouble OwnerResolver { get; } = new();

    /// <summary>The Office create's Web API bodies, exactly as the service sent them (field mapping task 018b-2).</summary>
    public List<Dictionary<string, object?>> OfficeCreateBodies { get; } = new();

    /// <summary>Each team's business unit for step 8's read (a team not named here is in the default Office unit).</summary>
    public Dictionary<Guid, Guid> TeamBusinessUnits { get; } = new();

    /// <summary>A business unit's search-index defaults (name, catalog row); a unit not named here has none.</summary>
    public Dictionary<Guid, (string Name, Guid Index)> BusinessUnitSearchDefaults { get; } = new();

    /// <summary>Every team whose business unit step 8 read, in order.</summary>
    public List<Guid> TeamReads { get; } = new();

    /// <summary>
    /// When true, the app-only read of a source matter (the mapping's source read) throws (field mapping task 018c: the deleted
    /// Office core's "app read of the source fails" tests, re-pointed); cleared by <see cref="ResetRoutes"/>.
    /// </summary>
    public bool SourceAppReadFails { get; set; }

    /// <summary>
    /// When set, the caller's read of the mapping source answers this failure status (field mapping task 018c: the deleted
    /// Office core's "caller read of the source fails" tests, re-pointed); cleared by <see cref="ResetRoutes"/>.
    /// </summary>
    public int? SourceCallerReadFailStatus { get; set; }

    private PendingInvocation? _pending;
    private Guid? _mappedMatter;
    private Guid? _mappedType;

    private readonly TestableChatSessionManager _sessions = new();

    /// <summary>Clears the route-side state (the provisioning state is <see cref="ProvisionProjectTestFixture.Reset"/>'s).</summary>
    public void ResetRoutes()
    {
        AfterOfficeCreate = null;
        OfficeCreates.Clear();
        ChatCreates.Clear();
        CallerNoAppendTo.Clear();
        CallerDataverse.Clear();
        OwnerResolver.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
        OwnerResolver.RefusalCode = Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.SecureParentNotIsolated;
        OwnerResolver.Requests.Clear();
        OfficeCreateBodies.Clear();
        TeamBusinessUnits.Clear();
        BusinessUnitSearchDefaults.Clear();
        TeamReads.Clear();
        SourceAppReadFails = false;
        SourceCallerReadFailStatus = null;
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

    /// <summary>
    /// The record-type catalog names <paramref name="typeRef"/> for a matter, with NO mapping profile — so a project created
    /// from a matter is filed under it by the adapter's derived pair alone (field mapping task 018b, G4: no pair rule).
    /// </summary>
    public void CatalogMattersAs(Guid typeRef) => _mappedType = typeRef;

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
            // One class fixture serves every test here: the Office route's per-user rate limit (5 a minute) would refuse the
            // sixth Office create of a run (field mapping task 018b adds the G4 tests), so it is off, as the Office hosts have it.
            services.Configure<Sprk.Bff.Api.Configuration.OfficeRateLimitOptions>(options => options.Enabled = false);

            // ── Office quick-create: the app-only entity seam, the mapping profile, the caller's systemuser ──
            services.RemoveAll<IGenericEntityService>();
            var officeEntities = OfficeEntities();
            services.AddSingleton(officeEntities.Object);
            services.RemoveAll<IFieldMappingDataverseService>();
            services.AddSingleton(FieldMappings().Object);
            services.RemoveAll<ICallerSystemUserResolver>();
            var resolver = new Mock<ICallerSystemUserResolver>();
            resolver.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
            services.AddScoped(_ => resolver.Object);
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(OwnerResolver);

            // ── Chat: the caller's own Dataverse (scripted OBO), the session, the gate store, the tool catalog ──
            services.RemoveAll<IDataverseUserClient>();
            // #2188: the Office mapping-source caller read is answered as the app read was (a caller who reads every column).
            // Field mapping task 018b: the Office create now asks as the caller too — the pair's record-type row from this
            // fixture's catalog, and AppendTo on the record it is filed under (CallerNoAppendTo withholds it).
            services.AddScoped<IDataverseUserClient>(_ =>
            {
                var caller = new SecureChildOwnershipAiToolTests.ScriptedUserClient(CallerSystemUserId)
                {
                    RecordTypeOf = id => ChildWorld.ValueOf<string>("sprk_recordtype_ref", id, "sprk_recordlogicalname"),
                };
                caller.NoAppendTo.UnionWith(CallerNoAppendTo);
                var sourceRead = MappingSourceCallerReadDouble.MirroringAppReads(officeEntities);
                sourceRead.FailWithStatus = SourceCallerReadFailStatus;
                return CallerDataverse.Over(sourceRead.RoutedOver(caller));
            });
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
            .Returns((string _, Guid id, string[] _, CancellationToken _) => SourceAppReadFails
                ? Task.FromException<Entity>(new TimeoutException("app-only read timed out"))
                : Task.FromResult(new Entity("sprk_matter", id)
                {
                    ["sprk_matterid"] = id.ToString("D"),
                    ["sprk_recordtype"] = new EntityReference("sprk_recordtype_ref", _mappedType ?? Guid.Empty),
                }));

        // The owning team's business unit and its search defaults (step 8 reads team -> business unit app-only), so a create
        // that has nothing else to warn about warns nothing (field mapping task 018b, G4 condition 3). Task 018b-2: per team
        // and per unit (TeamBusinessUnits, BusinessUnitSearchDefaults), so a test can tell whose unit the defaults came from.
        entities
            .Setup(e => e.RetrieveAsync("team", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid team, string[] _, CancellationToken _) =>
            {
                TeamReads.Add(team);
                return Task.FromResult(new Entity("team", team)
                {
                    ["businessunitid"] = new EntityReference("businessunit",
                        TeamBusinessUnits.TryGetValue(team, out var unit) ? unit : OfficeBusinessUnit),
                });
            });
        entities
            .Setup(e => e.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid unit, string[] _, CancellationToken _) =>
            {
                var row = new Entity("businessunit", unit);
                if (BusinessUnitSearchDefaults.TryGetValue(unit, out var defaults))
                {
                    row["sprk_searchindexname"] = defaults.Name;
                    row["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", defaults.Index);
                }

                return Task.FromResult(row);
            });

        // #2191 K2: the record-type catalog row the service derives a project's filed-under pair from (the matter's type).
        // It carries its logical name, as the live row does: the owner step reads it back by id for an ordinary parent
        // (RecordCreationService.PairParentAsync), where a secure parent's owner is the plan's.
        entities
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(_mappedType is { } type
                ? new EntityCollection(new List<Entity>
                {
                    new("sprk_recordtype_ref", type)
                    {
                        ["sprk_recordlogicalname"] = ChildWorld.ValueOf<string>("sprk_recordtype_ref", type, "sprk_recordlogicalname"),
                    },
                })
                : new EntityCollection()));
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

        // The app-only create (create-by-upsert) of both routes lands in the provisioning fixture's Dataverse: the Office
        // quick-create's project (RecordCreationService step 11, field mapping task 018b) and the chat tool's create.
        mappings
            .Setup(m => m.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                if (table == "sprk_project")
                {
                    RecordOfficeProjectCreate(id, fields);
                    return;
                }

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

    /// <summary>
    /// The Office quick-create's project, as the service's Web API body sent it: recorded in <see cref="OfficeCreates"/> as the
    /// SDK entity the old app-only create passed (owner, flag, pair, creator person) and seeded like the old create seam did.
    /// </summary>
    private void RecordOfficeProjectCreate(Guid id, IReadOnlyDictionary<string, object?> fields)
    {
        OfficeCreateBodies.Add(new Dictionary<string, object?>(fields));
        var owner = Bound(fields, "ownerid@odata.bind");
        var isSecure = fields.TryGetValue("sprk_issecure", out var flag)
                       && (flag is true || flag is JsonElement { ValueKind: JsonValueKind.True });
        var creatorPerson = Bound(fields, "sprk_CreatedByPerson@odata.bind");
        var pairId = fields.TryGetValue("sprk_regardingrecordid", out var rawPair)
            ? rawPair is JsonElement { ValueKind: JsonValueKind.String } pairJson ? pairJson.GetString() : rawPair as string
            : null;
        var pairType = Bound(fields, "sprk_RegardingRecordType@odata.bind");

        var row = new Entity("sprk_project", id) { ["sprk_issecure"] = isSecure };
        if (owner is { } team)
            row["ownerid"] = new EntityReference("team", team);
        if (creatorPerson is { } person)
            row[RecordCreatorPerson.Column] = new EntityReference("systemuser", person);
        if (pairId is not null)
            row["sprk_regardingrecordid"] = pairId;
        if (pairType is { } type)
            row["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", type);
        OfficeCreates.Add(row);

        SeedProject(id, owningTeamId: owner, isSecure: isSecure, createdBy: AppUser, createdByPerson: creatorPerson);
        if (pairId is not null)
            ChildWorld.Set("sprk_project", id, "sprk_regardingrecordid", pairId);
        if (pairType is { } pairTypeId)
            ChildWorld.Set("sprk_project", id, "sprk_regardingrecordtype", new EntityReference("sprk_recordtype_ref", pairTypeId));
        AfterOfficeCreate?.Invoke(id);
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

/// <summary>
/// Field mapping task 018b: records every request made of the caller's own Dataverse (the per-request OBO client), so a test
/// can show a check was asked AS THE CALLER — not inferred from the route filter's own probe.
/// </summary>
public sealed class RecordedCallerDataverse
{
    private readonly List<string> _requests = new();

    /// <summary>The requests, unescaped, in order.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public void Clear()
    {
        lock (_requests)
        {
            _requests.Clear();
        }
    }

    /// <summary><paramref name="inner"/>, recording each request before passing it on unchanged.</summary>
    internal IDataverseUserClient Over(IDataverseUserClient inner) => new Recording(this, inner);

    private void Record(string method, string path)
    {
        lock (_requests)
        {
            _requests.Add($"{method} {Uri.UnescapeDataString(path)}");
        }
    }

    private sealed class Recording(RecordedCallerDataverse log, IDataverseUserClient inner) : IDataverseUserClient
    {
        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            log.Record("GET", relativePath);
            return inner.GetAsync(relativePath, cancellationToken);
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
        {
            log.Record("POST", absoluteApiPath);
            return inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);
        }

        public Task<DataverseUserResponse> PostAsync(
            string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        {
            log.Record("POST", absoluteApiPath);
            return inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            log.Record("PATCH", relativePath);
            return inner.PatchAsync(relativePath, jsonBody, cancellationToken);
        }

        public Task<DataverseUserResponse> PatchAsync(
            string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken)
        {
            log.Record("PATCH", relativePath);
            return inner.PatchAsync(relativePath, jsonBody, expectedVersion, cancellationToken);
        }

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
        {
            log.Record("DELETE", relativePath);
            return inner.DeleteAsync(relativePath, cancellationToken);
        }
    }
}
