using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
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

    /// <summary>Task 049 fix round 1: the "Assigned *" pass's host — its queue, the maker's linked contact, and the
    /// materializer an inline pass resolves (counted).</summary>
    private readonly List<JobContract> _queued = [];
    private readonly AssignedAccessTestDoubles.Harness _h = new();
    private Guid? _makersContact;
    private int _inlinePasses;

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

        _fixture.Grants.Should().Contain(g => g.RecordId == created && g.Principal == DataversePrincipalRef.User(Creator),
            "the completion shares the new row to its creator through the recorded share service");
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

    /// <summary>
    /// UAC-r2 F-2: a row assigned to a CONTACT whose id equals a secure matter's, and filed under that matter by a lookup the
    /// SERVER set (so step 5 did not ask its AppendTo), must still have the matter's own AppendTo asked: the contact's
    /// AppendTo, which step 5 did ask, does not stand in for it. Ported from
    /// <c>SecureRootInheritanceWriterTests.ChatCreate_AssignedToAContactWithASecureMattersId_...</c>. Since task 014 a
    /// server-set work assignment filing lookup is asked AppendTo BEFORE the plan (step 6's pre-plan check), so this no
    /// longer reaches the plan callback; the callback's (table, id) key is pinned by
    /// <see cref="CreateAsync_AssignedToAContactWithASecureMattersId_AndFiledUnderAnInvoiceOfThatMatter_IsTheUniformNotFound_AndCreatesNothing"/>.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AssignedToAContactWithASecureMattersId_AndFiledUnderThatMatterByTheServer_IsTheUniformNotFound_AndCreatesNothing()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        _caller.NoAppendToIn.Add(("sprk_matters", secure)); // the caller may append to the CONTACT with that id, not the matter

        var toSecure = await Service().CreateAsync(await AssignedToAContactWithTheMattersId(secure), AsCaller(), CancellationToken.None);

        toSecure.Succeeded.Should().BeFalse("a row is never filed under a secure matter the caller may not append to");
        toSecure.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        toSecure.Refusal.OwnerDecision!.RefusalCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "refused by step 6's pre-plan AppendTo on the server-set matter (in the plan's own shape), not by step 5 (which " +
            "asked only about the contact)");
        var problem = Problem(toSecure);
        problem.Status.Should().Be(StatusCodes.Status404NotFound, "the uniform not-found: never a 403 that says the matter exists and is secure");
        JsonSerializer.Serialize(problem).Should().NotContain(secure.ToString("D"));
        _writes.Should().BeEmpty("nothing is created");
    }

    /// <summary>
    /// Task 014 fix round 3 (UAC-r2 review, F-A): the plan callback's "already checked" set is keyed by (table, id), never by
    /// id alone. The row is filed under an INVOICE of a secure matter (the plan reaches the matter through it, so no lookup
    /// names the matter and nothing is server-set) and assigned to a CONTACT whose id equals that matter's. Step 5 asks
    /// AppendTo on the contact and the invoice only; the matter's own AppendTo is the callback's to ask — an id-only key would
    /// read the contact's as the matter's and file the row under a secure matter nobody asked about.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AssignedToAContactWithASecureMattersId_AndFiledUnderAnInvoiceOfThatMatter_IsTheUniformNotFound_AndCreatesNothing()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        var invoice = InvoiceOf(_fixture, secure);
        _caller.NoAppendToIn.Add(("sprk_matters", secure)); // the caller may append to the CONTACT with that id, not the matter

        var mapped = await DataverseWriteItemMapper.MapAsync(_caller, "sprk_workassignment", JsonSerializer.SerializeToElement(new
        {
            sprk_name = "Review the lease",
            sprk_assignedto = new { relatedTable = "contact", recordId = secure },
            sprk_regardinginvoice = new { relatedTable = "sprk_invoice", recordId = invoice },
        }), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        var request = new RecordCreationRequest("sprk_workassignment", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull("a row is never filed under a secure matter the caller may not append to");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.OwnerDecision!.RefusalCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "refused by the plan callback's own AppendTo on the matter: the contact with the same id does not stand in for it");
        Problem(outcome).Status.Should().Be(StatusCodes.Status404NotFound);
        _writes.Should().BeEmpty("nothing is created");
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
        _fixture.Grants.Should().BeEmpty("no share is made to anyone");
        _fixture.Modifies.Should().BeEmpty("no share is made to anyone");
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_ForNobody_AWorkAssignmentUnderASecureMatter_IsRefusedBeforeThePlan_NotByIt()
    {
        // Second-pass finding 2: the refusal comes BEFORE the plan reads the parent. Asked, the plan would refuse a create
        // made for nobody under a secure parent itself (creator_unresolved, 409); this answer is the pre-plan 403.
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);

        var outcome = await Service().CreateAsync(
            await WorkAssignmentUnder(secure), RecordCreationPrincipal.ForSystem(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Forbidden);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.SystemPrincipalUnsupportedCode,
            "refused before the plan, not by the plan's own creator_unresolved");
        outcome.Refusal.OwnerDecision.Should().BeNull("the plan was not asked");
        Problem(outcome).Status.Should().Be(StatusCodes.Status403Forbidden);
        _writes.Should().BeEmpty();
        _fixture.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AProjectUnderASecureMatter_IsCreatedIntoIsolation_WithNoSearchIndex()
    {
        // Task 015 review round 1 (F6), FR-10 read the OWNING unit's index here. Task 041 (FR-40): the index follows the
        // content type, so step 8 reads no unit, even though the Secure BU holds an index, and writes neither column.
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        var secureIndex = Guid.NewGuid();
        var unitReads = new List<Guid>();
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveAsync("team", SecureTeam, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("team", SecureTeam)
            {
                ["businessunitid"] = new EntityReference("businessunit", ProvisionProjectTestFixture.SecureBuId),
            });
        entities
            .Setup(e => e.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, id, _, _) => unitReads.Add(id))
            .ReturnsAsync(new Entity("businessunit", ProvisionProjectTestFixture.SecureBuId)
            {
                ["sprk_searchindexname"] = "spaarke-secure-index",
                ["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", secureIndex),
            });

        var outcome = await Service(entities.Object).CreateAsync(await ProjectUnder(secure), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Isolated.Should().BeTrue();
        outcome.OwningTeamId.Should().Be(SecureTeam);
        unitReads.Should().BeEmpty("no business unit is read for an index");
        var (table, _, fields) = _writes.Should().ContainSingle().Subject;
        table.Should().Be("sprk_project");
        fields.Should().NotContainKey("sprk_searchindexname").And.NotContainKey("sprk_AI_Search_Index@odata.bind");
    }

    // ── Task 022b (UAC-r2 Q2): the Access Permission floor of a create planned INTO isolation ─────────────────────────────

    /// <summary>
    /// Task 022b (must fire): a project filed under a secure matter that is Restricted is created into isolation, and the plan
    /// that decided so read the matter's Access Permission in the same read — its floor is Restricted, so the values mapped
    /// from that matter are written. The ordinary plan's floor is pinned in the mapping seam; this is the isolated plan's.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AProjectUnderASecureRestrictedMatter_IsCreatedIntoIsolation_AndItsMappedValuesMap()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        World.Set("sprk_matter", secure, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
        var entities = MappingSourceReads(secure);

        var outcome = await Service(entities.Object, MatterProfile("sprk_project")).CreateAsync(
            await ProjectUnder(secure) with { Mapping = MappedFrom(secure) }, AsMappingCaller(entities), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Isolated.Should().BeTrue();
        outcome.OwningTeamId.Should().Be(SecureTeam);
        var (table, _, fields) = _writes.Should().ContainSingle().Subject;
        table.Should().Be("sprk_project");
        TextOf(fields, "sprk_description").Should().Be(FromTheSecureMatter, "the isolated plan's floor is Restricted");
        fields.Should().NotContainKey(InheritedAccessPermission.Column, "the project's stored value is the cascade's, after the create");
        outcome.MappingWarnings.Should().NotContain(w => w.Code == RecordCreationWarning.MappingSourceRestrictedCode);
    }

    /// <summary>
    /// Task 022b (must not fire), at the plan: a work assignment filed under the same secure Restricted matter by its typed
    /// lookup, whose pair also names a matter no typed lookup repeats. The real plan still creates it into isolation (the only
    /// gap is that unrepeated pair, beside a readably secure parent), but its floor is undecided, and the service's answer for
    /// that floor removes the mapped values. Asked of the plan directly: the unified create refuses such a pair at its caller
    /// check (step 5) before the plan is asked, so the plan's branch is pinned here and the strip on the service's answer.
    /// </summary>
    [Fact]
    public async Task PlanCreateAsync_AnIsolatedPlanWhoseOnlyGapIsAnUnrepeatedPair_HasAnUndecidedFloor_AndItsMappedValuesAreRemoved()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        World.Set("sprk_matter", secure, InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted));
        var request = await WorkAssignmentUnderWithAnUnrepeatedPair(secure, Guid.NewGuid());
        var item = ((RecordCreationPayload.Mapped)request.Payload).Item;

        var plan = await _fixture.Services.GetRequiredService<SecureRootFilingGate>().PlanCreateAsync(
            "sprk_workassignment", OwnedChildWrite.WritesOf(item), Creator, CancellationToken.None);

        plan.Refusal.Should().BeNull(plan.Refusal?.Reason);
        plan.Isolated.Should().BeTrue("the precondition: the plan proceeds into isolation with only the unrepeated pair unread");
        plan.SecureOwnerTeamId.Should().Be(SecureTeam);
        plan.AccessPermissionFloor.Should().Be(AccessPermissionFloor.Undecided);
        var access = RecordCreationService.RootAccessPermission(ownReadable: true, ownRestricted: false, plan.AccessPermissionFloor);
        access.EndsRestricted.Should().BeFalse("an undecided floor does not make the row Restricted");
        access.Undetermined.Should().BeTrue("so the values mapped from a Restricted source are removed, as undetermined");
    }

    // ── Task 049 fix round 1 (UAC-r2 ruling, item 4): the create-time pass waits for secure provisioning, so eligibility is
    // decided on the final record ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AnIsolatedCreate_WhoseSecuringDidNotComplete_QueuesNoPass_AndGrantsNothing()
    {
        var secure = Guid.NewGuid();
        var sharee = Guid.NewGuid();
        _fixture.SystemUsers[sharee] = (false, false);
        SecureMatter(_fixture, secure, null, sharee);
        _fixture.FailShareForPrincipal = sharee; // the creator is shared; the parent's sharee is left to the securing job

        var outcome = await Service().CreateAsync(
            await WorkAssignmentUnder(secure, assignedTo: Guid.NewGuid()), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Isolated.Should().BeTrue();
        outcome.IsolatedCompletion!.IsComplete.Should().BeFalse("the precondition: securing is not finished");
        outcome.IsolatedCompletion.RowStranded.Should().BeFalse();
        _queued.Should().BeEmpty("the create-time pass waits for secure provisioning");
        _inlinePasses.Should().Be(0);
        _journal.Should().NotContain("assigned-access", "nothing is granted at create; the reconciliation job grants later");
    }

    [Fact]
    public async Task CreateAsync_AnIsolatedCreate_ForAnotherAssignee_QueuesItsPass_OnceSecuringCompleted()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        _makersContact = Guid.NewGuid();

        var outcome = await Service().CreateAsync(
            await WorkAssignmentUnder(secure, assignedTo: Guid.NewGuid()), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.IsolatedCompletion!.IsComplete.Should().BeTrue();
        _creatorShareAtAssignedAccess.Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights),
            "the pass is decided after the securing completed");
        var job = _queued.Should().ContainSingle().Subject;
        job.JobType.Should().Be(AssignedAccessCreateGrantJobHandler.JobTypeName);
        job.IdempotencyKey.Should().Be($"assigned-access-create:{outcome.CreatedId:N}");
        _inlinePasses.Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_AnIsolatedCreate_ForItsMaker_RunsItsPassInline_OnceSecuringCompleted()
    {
        var secure = Guid.NewGuid();
        SecureMatter(_fixture, secure);
        _makersContact = Guid.NewGuid();

        var outcome = await Service().CreateAsync(
            await WorkAssignmentUnder(secure, assignedTo: _makersContact), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.IsolatedCompletion!.IsComplete.Should().BeTrue();
        _creatorShareAtAssignedAccess.Should().Be(RecordShareLevels.MaskForRightsCsv(ProvisionProjectEndpoint.CreatorAccessRights),
            "the pass is decided after the securing completed");
        _inlinePasses.Should().Be(1, "the maker's own subject is granted in the request");
        _queued.Should().ContainSingle("task 049b: the full pass for the other subjects is queued after it")
            .Which.IdempotencyKey.Should().Be($"assigned-access-create:{outcome.CreatedId:N}");
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private RecordCreationService Service(IGenericEntityService? entities = null, FieldMappingProfileEntity? profile = null)
    {
        var stamps = new StampWorld();
        var restamper = new CoreAncestorRestamper(stamps.Service, stamps.Resolver, NullLogger<CoreAncestorRestamper>.Instance);
        return new RecordCreationService(
            new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object,
            AppCreatesIntoTheWorld(profile),
            entities ?? new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            _fixture.Services.GetRequiredService<SecureRootFilingGate>(),
            restamper,
            SecureChildShareWorld.SynchronizerOver(() => World, new FakeRecordShareTable()),
            new SnapshotScopes(this),
            new Mock<IMembershipEventPublisher>().Object,
            NullLogger<RecordCreationService>.Instance);
    }

    private RecordCreationPrincipal AsCaller() => RecordCreationPrincipal.ForCaller(_caller, Guid.NewGuid());

    private async Task<RecordCreationRequest> WorkAssignmentUnder(Guid matter, Guid? assignedTo = null)
    {
        var row = new Dictionary<string, object>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_regardingmatter"] = new { relatedTable = "sprk_matter", recordId = matter },
        };
        if (assignedTo is { } assignee)
            row["sprk_assignedtointernal"] = new { relatedTable = "contact", recordId = assignee }; // the "for" person
        var mapped = await DataverseWriteItemMapper.MapAsync(
            _caller, "sprk_workassignment", JsonSerializer.SerializeToElement(row), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest("sprk_workassignment", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };
    }

    /// <summary>A project filed under <paramref name="matter"/> by its ADR-024 pair (its only filing).</summary>
    private async Task<RecordCreationRequest> ProjectUnder(Guid matter)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(_caller, "sprk_project", JsonSerializer.SerializeToElement(new
        {
            sprk_projectname = "Phase 2",
            sprk_regardingrecordid = matter.ToString("D"),
            sprk_regardingrecordtype = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        }), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest("sprk_project", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };
    }

    /// <summary>A work assignment assigned to a contact whose id equals <paramref name="matter"/>'s, filed under it by a server-set lookup.</summary>
    private async Task<RecordCreationRequest> AssignedToAContactWithTheMattersId(Guid matter)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(_caller, "sprk_workassignment", JsonSerializer.SerializeToElement(new
        {
            sprk_name = "Review the lease",
            sprk_assignedto = new { relatedTable = "contact", recordId = matter },
            sprk_regardingmatter = new { relatedTable = "sprk_matter", recordId = matter },
        }), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest("sprk_workassignment", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
            ServerSetLookupColumns = new HashSet<string> { "sprk_regardingmatter" },
        };
    }

    // ── Task 022b: field mapping from the secure matter ─────────────────────────────────────────────────────────────────

    private const string FromTheSecureMatter = "From the secure matter";

    /// <summary>The matter → <paramref name="target"/> profile: one Copy rule into the description.</summary>
    private static FieldMappingProfileEntity MatterProfile(string target) => new()
    {
        Name = $"sprk_matter to {target}",
        SourceEntity = "sprk_matter",
        TargetEntity = target,
        IsActive = true,
        Rules =
        [
            new FieldMappingRuleEntity
            {
                Name = "description",
                SourceField = "sprk_matterdescription",
                TargetField = "sprk_description",
                TargetFieldType = CreateTimeFieldMapping.FieldTypeText,
                MappingType = CreateTimeFieldMapping.MappingTypeCopy,
                IsActive = true,
                ExecutionOrder = 0,
            },
        ],
    };

    /// <summary>
    /// The service's app-only reads: the mapping source (the secure matter as the application reads it: owned by the named
    /// team, flagged, Restricted) and step 8's owning-team unit.
    /// </summary>
    private static Mock<IGenericEntityService> MappingSourceReads(Guid matter)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveAsync("sprk_matter", matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matter)
            {
                ["sprk_matterdescription"] = FromTheSecureMatter,
                ["ownerid"] = new EntityReference("team", SecureTeam),
                [AccessFollowsParent.SecureFlagColumn] = true,
                [InheritedAccessPermission.Column] = new OptionSetValue(InheritedAccessPermission.Restricted),
            });
        entities
            .Setup(e => e.RetrieveAsync("team", SecureTeam, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("team", SecureTeam)
            {
                ["businessunitid"] = new EntityReference("businessunit", ProvisionProjectTestFixture.SecureBuId),
            });
        entities
            .Setup(e => e.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", ProvisionProjectTestFixture.SecureBuId));
        return entities;
    }

    /// <summary>Field mapping read as the caller, from <paramref name="matter"/> named as the source.</summary>
    private static RecordCreationMapping MappedFrom(Guid matter) =>
        RecordCreationMapping.ForCaller(new ReadsAndWritesEverything(), "caller-bearer-token", "sprk_matter", matter);

    /// <summary>The caller, who reads the mapping source as the application read it.</summary>
    private RecordCreationPrincipal AsMappingCaller(Mock<IGenericEntityService> entities) =>
        RecordCreationPrincipal.ForCaller(MappingSourceCallerReadDouble.MirroringAppReads(entities).RoutedOver(_caller), Guid.NewGuid());

    /// <summary>A work assignment filed under <paramref name="matter"/> by its typed lookup, whose pair names
    /// <paramref name="pairMatter"/>, which no typed lookup repeats.</summary>
    private async Task<RecordCreationRequest> WorkAssignmentUnderWithAnUnrepeatedPair(Guid matter, Guid pairMatter)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(_caller, "sprk_workassignment", JsonSerializer.SerializeToElement(new
        {
            sprk_name = "Review the lease",
            sprk_regardingmatter = new { relatedTable = "sprk_matter", recordId = matter },
            sprk_regardingrecordid = pairMatter.ToString("D"),
            sprk_regardingrecordtype = new { relatedTable = "sprk_recordtype_ref", recordId = RecordTypeRef(_fixture, "sprk_matter") },
        }), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest("sprk_workassignment", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            CorrelationId = FixedCorrelation,
        };
    }

    private static string? TextOf(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value)
            ? value is JsonElement { ValueKind: JsonValueKind.String } text ? text.GetString() : value as string
            : null;

    /// <summary>The caller-rights probe with only its answer substituted: Read and Write on every record.</summary>
    private sealed class ReadsAndWritesEverything()
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default) =>
            Task.FromResult(AccessRights.Read | AccessRights.Write);
    }

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Problem(RecordCreationOutcome outcome) =>
        RecordCreationProblemMap.ToProblemDetails(outcome.Refusal!, "sprk_workassignment", outcome.CorrelationId);

    /// <summary>The app-only create: the row lands in the fixture as the application created it (createdby = app). Task 022b:
    /// <paramref name="profile"/>, when set, is the one field-mapping profile the app-only reader holds.</summary>
    private IFieldMappingDataverseService AppCreatesIntoTheWorld(FieldMappingProfileEntity? profile = null)
    {
        var service = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        if (profile is not null)
        {
            service
                .Setup(s => s.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, string target, bool _, CancellationToken _) =>
                    source == profile.SourceEntity && target == profile.TargetEntity ? profile : null);
        }

        service
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                _journal.Add("create");
                table.Should().BeOneOf("sprk_workassignment", "sprk_project");
                _writes.Add((table, id, fields));
                var isSecure = fields.TryGetValue("sprk_issecure", out var flag) && flag is true;
                if (table == "sprk_project")
                {
                    _fixture.SeedProject(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"), isSecure: isSecure,
                        createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
                }
                else
                {
                    _fixture.SeedWorkAssignment(id, owningTeamId: BoundId(fields, "ownerid@odata.bind"), isSecure: isSecure,
                        createdBy: AppUser, createdByPerson: BoundId(fields, "sprk_CreatedByPerson@odata.bind"));
                }
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

    /// <summary>
    /// The "Assigned *" step's scope: journaled, with the creator's share on the new row at that moment. Since task 049 fix
    /// round 1 it holds the step's services: a queue that records, the maker's linked contact, and the materializer.
    /// </summary>
    private sealed class SnapshotScopes(RecordCreationServiceSecureRootSeamTests owner) : IServiceScopeFactory
    {
        private IServiceScopeFactory? _inner;

        public IServiceScope CreateScope()
        {
            owner._journal.Add("assigned-access");
            if (owner._writes.LastOrDefault() is { Id: var id } && id != Guid.Empty)
                owner._creatorShareAtAssignedAccess ??= owner._fixture.ShareMaskOf(id, Creator); // the FIRST such step
            _inner ??= Build();
            return _inner.CreateScope();
        }

        private IServiceScopeFactory Build()
        {
            var jobs = new Mock<JobSubmissionService>(
                MockBehavior.Strict,
                Microsoft.Extensions.Options.Options.Create(new Sprk.Bff.Api.Configuration.ServiceBusOptions { QueueName = "sdap-jobs" }),
                Mock.Of<Microsoft.Extensions.Logging.ILogger<JobSubmissionService>>(),
                new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
            jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Callback<JobContract, CancellationToken>((job, _) => owner._queued.Add(job))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection();
            services.AddSingleton(jobs.Object);
            services.AddSingleton(
                Sprk.Bff.Api.Tests.TestInfrastructure.IdentityNormalizationFixtures.WithContact(owner._makersContact).Object);
            services.AddScoped(_ =>
            {
                Interlocked.Increment(ref owner._inlinePasses);
                return owner._h.Materializer;
            });
            return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }
    }
}
