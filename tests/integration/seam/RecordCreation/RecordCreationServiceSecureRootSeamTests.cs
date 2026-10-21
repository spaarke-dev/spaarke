using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Xunit;
using static Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureRootInheritanceTests;
using ScriptedUserClient =
    Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.SecureChildOwnershipAiToolTests.ScriptedUserClient;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 013 fix round 1 — the unified create pipeline's port of the secure-root create
/// (unified-access-control-r2 task 158 r1, UAC-r2 condition (b)) over the provisioning fixture's Dataverse: the REAL
/// <see cref="SecureRootFilingGate"/> and its <see cref="SecureRootInheritance"/> (the plan, the No Access walls, the named
/// team, and the completion that shares the new row to its creator, gives it its container, or removes it again), the REAL
/// <see cref="CallerWriteAuthorization"/> checks over the caller's scripted Dataverse, and the app-only create applying the
/// row to the fixture as the application would.
/// </summary>
/// <remarks>
/// What <c>RecordCreationServicePipelineSeamTests</c> cannot reach: its gate reads a Dataverse with no rows, so nothing is
/// ever filed under a secure work assignment or project parent there. The owner resolver is a strict double with no
/// answers: every path here is decided by the plan, so a call to it would be a defect (and would fail the test as an
/// <c>owner_unavailable</c> refusal).
/// </remarks>
[Trait("status", "new")]
public sealed class RecordCreationServiceSecureRootSeamTests : IClassFixture<ProvisionProjectTestFixture>
{
    private const string FixedCorrelation = "seam-013-secure-root";

    private readonly ProvisionProjectTestFixture _fixture;
    private readonly ScriptedUserClient _caller;
    private readonly List<string> _journal = [];
    private readonly List<(string Table, Guid Id, IReadOnlyDictionary<string, object?> Fields)> _writes = [];

    /// <summary>The creator's share on the new row when the app-only create ran, and when the "Assigned *" step ran.</summary>
    private int? _creatorShareAtCreate;
    private int? _creatorShareAtAssignedAccess;

    public RecordCreationServiceSecureRootSeamTests(ProvisionProjectTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.UseChildWorldForRoots();
        _fixture.SystemUsers[AppUser] = (false, true);
        _caller = new ScriptedUserClient(Creator)
        {
            RecordTypeOf = id => World.ValueOf<string>("sprk_recordtype_ref", id, "sprk_recordlogicalname"),
        };
    }

    private SecureChildShareWorld World => _fixture.ChildWorld;

    [Fact]
    public async Task CreateAsync_AWorkAssignmentUnderASecureMatter_IsCreatedIntoIsolation_ThenCompleted_BeforeItsAssignedAccess()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);

        var outcome = await Service().CreateAsync(await WorkAssignmentUnder(secure), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Isolated.Should().BeTrue();
        outcome.OwningTeamId.Should().Be(SecureTeam);
        var (table, created, fields) = _writes.Should().ContainSingle().Subject;
        table.Should().Be("sprk_workassignment");
        outcome.CreatedId.Should().Be(created);
        fields["ownerid@odata.bind"].Should().Be($"/teams({SecureTeam})", "created owned by the named Secure Record Owners team");
        fields["sprk_issecure"].Should().Be(true, "flagged in the create itself — never a business-unit-visible row");
        fields["sprk_CreatedByPerson@odata.bind"].Should().Be($"/systemusers({Creator:D})", "made for the caller");

        // The route's order: the create, then the isolated completion (the creator's share, read back), then "Assigned *".
        _journal.Should().ContainInOrder("create", "assigned-access");
        _creatorShareAtCreate.Should().Be(0, "nothing is shared before the create");
        _creatorShareAtAssignedAccess.Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights),
            "the isolated completion runs before the \"Assigned *\" step");

        _fixture.IsSecureOf(created).Should().BeTrue();
        _fixture.OwningTeamOf(created).Should().Be(SecureTeam);
        _fixture.ContainerIdOf(created).Should().Be(ProvisionProjectTestFixture.ProvisionedContainerId, "its own container");
    }

    [Fact]
    public async Task CreateAsync_WhenTheIsolatedRowsCreatorCannotBeShared_TheRowIsRemoved_AndTheRefusalIsUndecided500()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        _fixture.FailShareForPrincipal = Creator;

        var outcome = await Service().CreateAsync(await WorkAssignmentUnder(secure), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.CreatedId.Should().BeNull();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Undecided);
        outcome.Refusal.Code.Should().StartWith("sdap.", "the secured result's own reason code")
            .And.NotBe(RecordCreationRefusal.NoDecisionCode);
        outcome.Refusal.Detail.Should().Contain("removed again").And.Contain("Nothing was created");
        var problem = Problem(outcome);
        problem.Status.Should().Be(StatusCodes.Status500InternalServerError);
        problem.Extensions["errorCode"].Should().Be(outcome.Refusal.Code);

        var created = _writes.Should().ContainSingle().Subject.Id;
        World.Has("sprk_workassignment", created).Should().BeFalse("the row was deleted again");
        _journal.Should().NotContain("assigned-access", "a removed row gets no \"Assigned *\" grants");
    }

    [Fact]
    public async Task CreateAsync_WhenTheCallerMayNotFileUnderTheSecureMatter_TheBodyIsTheMissingParents_404()
    {
        // The plan's own AppendTo (G5) on the secure parent: the matter is a lookup the SERVER set, so step 5 did not ask
        // about it and the plan's callback must. Refused there, it is the uniform not-found — never a 403 that would say
        // the record exists and is secure.
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        _caller.NoAppendTo.Add(secure);
        var denied = await Service().CreateAsync(
            (await WorkAssignmentUnder(secure)) with { ServerSetLookupColumns = new HashSet<string> { "sprk_regardingmatter" } },
            AsCaller(), CancellationToken.None);

        var missingMatter = Guid.NewGuid();
        _caller.Missing.Add(missingMatter);
        var missing = await Service().CreateAsync(await WorkAssignmentUnder(missingMatter), AsCaller(), CancellationToken.None);

        denied.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        denied.Refusal.OwnerDecision!.RefusalCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied, "the plan's refusal");
        missing.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        Problem(denied).Status.Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(Problem(denied)).Should().Be(JsonSerializer.Serialize(Problem(missing)),
            "a secure record the caller may not file under and a record that does not exist are indistinguishable");
        _writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_ForAPersonNobodyChecked_AWorkAssignmentUnderASecureMatter_Is403_AndNothingIsShared()
    {
        // Review finding 4: a system create "for" X would be shared to X with no check that X may file under the matter.
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        var person = Guid.NewGuid();

        var outcome = await Service().CreateAsync(
            await WorkAssignmentUnder(secure), RecordCreationPrincipal.ForSystem(RecordRequester.Of(person, Guid.NewGuid())),
            CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.SystemPrincipalUnsupportedCode);
        Problem(outcome).Status.Should().Be(StatusCodes.Status403Forbidden);
        _writes.Should().BeEmpty();
        _fixture.SharesOn(secure).Should().NotContainKey(DataversePrincipalRef.User(person));
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private RecordCreationService Service()
    {
        var stamps = new StampWorld();
        var restamper = new CoreAncestorRestamper(stamps.Service, stamps.Resolver, NullLogger<CoreAncestorRestamper>.Instance);
        return new RecordCreationService(
            new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object,
            AppCreatesIntoTheWorld(),
            new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            _fixture.Services.GetRequiredService<SecureRootFilingGate>(),
            restamper,
            SecureChildShareWorld.SynchronizerOver(() => World, new FakeRecordShareTable()),
            new SnapshotScopes(this),
            new Mock<IMembershipEventPublisher>().Object,
            NullLogger<RecordCreationService>.Instance);
    }

    private RecordCreationPrincipal AsCaller() => RecordCreationPrincipal.ForCaller(_caller, Guid.NewGuid());

    private async Task<RecordCreationRequest> WorkAssignmentUnder(Guid matter)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(_caller, "sprk_workassignment", JsonSerializer.SerializeToElement(new
        {
            sprk_name = "Review the lease",
            sprk_regardingmatter = new { relatedTable = "sprk_matter", recordId = matter },
        }), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest("sprk_workassignment", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };
    }

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Problem(RecordCreationOutcome outcome) =>
        RecordCreationProblemMap.ToProblemDetails(outcome.Refusal!, "sprk_workassignment", outcome.CorrelationId);

    /// <summary>The app-only create: the row lands in the fixture as the application created it (createdby = app).</summary>
    private IFieldMappingDataverseService AppCreatesIntoTheWorld()
    {
        var service = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        service
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                _journal.Add("create");
                table.Should().Be("sprk_workassignment");
                _writes.Add((table, id, fields));
                _fixture.SeedWorkAssignment(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"),
                    isSecure: fields.TryGetValue("sprk_issecure", out var flag) && flag is true,
                    createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
                if (BoundId(fields, "sprk_RegardingMatter@odata.bind") is { } matter)
                    World.Set(table, id, "sprk_regardingmatter", new EntityReference("sprk_matter", matter));
                _creatorShareAtCreate = _fixture.ShareMaskOf(id, Creator);
            })
            .Returns(Task.CompletedTask);
        return service.Object;
    }

    private static Guid? BoundId(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var raw)
        && (raw is JsonElement { ValueKind: JsonValueKind.String } json ? json.GetString() : raw as string) is { } bind
        && Guid.TryParse(bind.TrimEnd(')').Split('(').Last(), out var id)
            ? id
            : null;

    /// <summary>The "Assigned *" step's scope: journaled, with the creator's share on the new row at that moment.</summary>
    private sealed class SnapshotScopes(RecordCreationServiceSecureRootSeamTests owner) : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner =
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        public IServiceScope CreateScope()
        {
            owner._journal.Add("assigned-access");
            if (owner._writes.LastOrDefault() is { Id: var id } && id != Guid.Empty)
                owner._creatorShareAtAssignedAccess ??= owner._fixture.ShareMaskOf(id, Creator); // the FIRST such step
            return _inner.CreateScope();
        }
    }
}
