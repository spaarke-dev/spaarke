using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using ScriptedUserClient =
    Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.SecureChildOwnershipAiToolTests.ScriptedUserClient;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 013 — the unified create pipeline (<see cref="RecordCreationService"/>, spec
/// FR-06 / FR-07 / FR-12) driven end to end: the REAL service, the REAL <see cref="RecordOwnershipResolver"/> over
/// <see cref="Directory"/>, the REAL <see cref="CallerWriteAuthorization"/> checks and <see cref="DataverseWriteItemMapper"/>
/// over the caller's scripted Dataverse, the REAL <see cref="SecureRootFilingGate"/>, <see cref="CoreAncestorRestamper"/>
/// (over <see cref="StampWorld"/>) and <see cref="SecureChildShareSynchronizer"/> (over <see cref="SecureChildShareWorld"/>).
/// Only the Dataverse module boundaries are doubled: the caller's client, the app-only create, the app-only read-back
/// and the membership publisher.
/// </summary>
/// <remarks>
/// <para><b>What is observed.</b> Every dependency call is written to ONE ordered journal as it happens (the caller's
/// questions by kind, the owner decision, the pre-write rule, the create, the read-back, the "Assigned *" scope, the
/// re-stamp's reads, the sharee mirror's queries, the membership event), so the step ORDER is asserted on what the
/// pipeline did, not only on the step list it reports. The create is asserted on what the app-only seam was asked to
/// write. Refusals are rendered through <see cref="RecordCreationProblemMap"/> — the HTTP body a route will return.</para>
/// </remarks>
[Trait("status", "new")]
public sealed partial class RecordCreationServicePipelineSeamTests
{
    private static readonly Guid Caller = Guid.Parse("a0130000-0000-4000-8000-0000000000aa");
    private static readonly Guid CallerOid = Guid.Parse("a0130000-0000-4000-8000-0000000000ab");
    private static readonly Guid OrdinaryMatter = Guid.Parse("a0130000-0000-4000-8000-000000000001");
    private static readonly Guid MissingMatter = Guid.Parse("a0130000-0000-4000-8000-000000000002");
    private static readonly Guid SecureMatter = Guid.Parse("a0130000-0000-4000-8000-000000000003");
    private const string FixedCorrelation = "seam-013-correlation";

    private readonly Directory _world = Directory.Standard()
        .WithUser(Caller, CallerOid, Directory.ChildBu)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithSecureRoot("sprk_matter", SecureMatter);

    /// <summary>Every app-only create ATTEMPT (a retried create is one row, several attempts).</summary>
    private readonly List<(string Table, Guid Id)> _createAttempts = [];

    /// <summary>Faults the app-only create answers, one per attempt, in order; an empty queue lets the create land.</summary>
    private readonly Queue<Exception> _createFaults = new();

    private readonly List<string> _journal = [];
    private readonly List<(string Table, Guid Id)> _restampReads = [];
    private readonly ScriptedUserClient _scripted = new(Caller);
    private readonly JournalingUserClient _user;
    private readonly List<(string Table, Guid Id, Dictionary<string, object?> Fields)> _creates = [];
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _readBack = new(MockBehavior.Strict);
    private readonly StampWorld _stamps = new StampWorld().Row("sprk_matter", OrdinaryMatter).Row("sprk_matter", SecureMatter);
    private readonly SecureChildShareWorld _shareWorld = SecureChildShareWorld.Standard()
        .OrdinaryRoot("sprk_matter", OrdinaryMatter)
        .SecureRoot("sprk_matter", SecureMatter);
    private readonly RecordingPublisher _publisher;
    private readonly CapturingLogger _logger = new();
    private Exception? _resolverFault;
    private Func<string, RecordTableRule?> _tableRules = RecordTableRules.Find;

    public RecordCreationServicePipelineSeamTests()
    {
        _user = new JournalingUserClient(_scripted, _journal);
        _publisher = new RecordingPublisher(_journal);

        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                _journal.Add("create");
                _createAttempts.Add((table, id));
                if (_createFaults.TryDequeue(out var fault))
                    throw fault;

                // The app-only create is a Web API PATCH: a body naming a lookup by its logical name is refused, as live.
                if (ScriptedUserClient.UndeclaredPropertyIn(table, JsonSerializer.Serialize(fields), "ownerid", "sprk_CreatedByPerson")
                    is { } undeclared)
                {
                    throw new HttpRequestException($"0x80060888 Could not find a property named '{undeclared}'.", null,
                        System.Net.HttpStatusCode.BadRequest);
                }

                _creates.Add((table, id, fields));
                Materialize(table, id, fields);
            })
            .Returns(Task.CompletedTask);

        _readBack
            .Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => _journal.Add("readback"))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
                new Entity(table, id) { ["sprk_matternumber"] = "MAT-000013" });

        // Task 015 (step 8, FR-10): a search-indexed table's owner team is read for its business unit, and that unit for its
        // search-index defaults — none set in this world, so nothing is filled (SearchIndexDefaultTests covers the fill).
        _readBack
            .Setup(e => e.RetrieveAsync("team", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) =>
                new Entity(table, id) { ["businessunitid"] = new EntityReference("businessunit", Directory.ChildBu) });
        _readBack
            .Setup(e => e.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Entity(table, id));
    }

    // =====================================================================================
    // The twelve steps, in order, and exactly one create
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnEventFiledUnderAnOrdinaryMatter_RunsTheStepsInOrder_AndCreatesExactlyOnce()
    {
        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        Steps(outcome).Should().Equal(
            (RecordCreationStep.Principal, RecordCreationStepState.Ran),
            (RecordCreationStep.TableRules, RecordCreationStepState.Ran),
            (RecordCreationStep.FieldMapping, RecordCreationStepState.NoOp),
            (RecordCreationStep.MapPayload, RecordCreationStepState.Ran),
            (RecordCreationStep.CallerChecks, RecordCreationStepState.Ran),
            (RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran),
            (RecordCreationStep.Owner, RecordCreationStepState.Ran),
            (RecordCreationStep.SearchIndexDefault, RecordCreationStepState.NoOp),
            (RecordCreationStep.CreatorAndDefaults, RecordCreationStepState.Ran),
            (RecordCreationStep.StampAndInheritance, RecordCreationStepState.Ran),
            (RecordCreationStep.Create, RecordCreationStepState.Ran),
            (RecordCreationStep.PostCreate, RecordCreationStepState.Ran));
        Journal().Should().Equal(
            "user:whoami", "user:map", "user:check", "owner", "create", "restamp", "mirror", "membership");

        var (table, id, fields) = _creates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_event");
        outcome.CreatedId.Should().Be(id);
        Owner(fields).Should().Be(Directory.ChildTeam, "the matter's business-unit team (record-first)");
        fields["sprk_CreatedByPerson@odata.bind"].Should().Be($"/systemusers({Caller:D})");
        _scripted.Posts.Should().BeEmpty("the row is never created as the caller");
        _scripted.Patches.Should().BeEmpty();
        _publisher.Published.Should().ContainSingle()
            .Which.Should().Match<MembershipChangedEvent>(e => e.EntityRecordId == id && e.PersonId == Directory.ChildTeam);
    }

    [Fact]
    public async Task CreateAsync_AMatter_RunsTheStepsInOrder_WithTheNumberReadBackAndTheAssignedAccessScope_AndCreatesExactlyOnce()
    {
        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_matter", new { sprk_mattername = "Acme v. Widget" }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        Steps(outcome).Select(s => s.Step).Should().Equal(
            RecordCreationStep.Principal, RecordCreationStep.TableRules, RecordCreationStep.FieldMapping,
            RecordCreationStep.MapPayload, RecordCreationStep.CallerChecks, RecordCreationStep.SecureRootPlan,
            RecordCreationStep.Owner, RecordCreationStep.SearchIndexDefault, RecordCreationStep.CreatorAndDefaults,
            RecordCreationStep.StampAndInheritance, RecordCreationStep.Create, RecordCreationStep.PostCreate);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.FieldMapping).State
            .Should().Be(RecordCreationStepState.NoOp, "step 3 is a recorded no-op seam until task 014");
        Journal().Should().Equal(
            "user:whoami", "user:check", "owner", "create", "readback", "assigned-access", "membership");

        var (table, id, fields) = _creates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_matter");
        Owner(fields).Should().Be(Directory.ChildTeam, "an unfiled root is owned by the caller's business-unit team");
        fields["sprk_CreatedByPerson@odata.bind"].Should().Be($"/systemusers({Caller:D})");
        fields.Should().NotContainKey("sprk_matternumber", "the platform autonumber fills it");
        outcome.Warnings.Should().BeEmpty();
        _publisher.Published.Should().ContainSingle().Which.EntityRecordId.Should().Be(id);
    }

    /// <summary>
    /// Task 076 (word-add-in-r1 code review W3), re-pointed from the deleted Office core by field mapping task 018c: the row
    /// exists once the create commits, so a number read-back cut short by a cancelled request must not turn the committed
    /// create into an error — and, having read nothing, it says nothing about the number.
    /// </summary>
    [Fact]
    public async Task CreateAsync_AMatter_WhenTheRequestIsCancelledAfterTheCreateCommits_StillSucceeds_WithoutANumberWarning()
    {
        using var cts = new CancellationTokenSource();
        _readBack
            .Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _journal.Add("readback");
                cts.Cancel(); // the client goes away just after the row is written
            })
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_matter", new { sprk_mattername = "Cancelled late" }), AsCaller(), cts.Token);

        outcome.Succeeded.Should().BeTrue("the row exists; a cancelled confirmation read must not hide it behind an error");
        outcome.CreatedId.Should().Be(_creates.Should().ContainSingle().Subject.Id);
        outcome.Warnings.Should().NotContain(w => w.Contains("without a number"),
            "a read that did not complete says nothing about the number");
    }

    // =====================================================================================
    // Principals (FR-07)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_WhenTheCallerCannotBeNamedInDataverse_Is403_BeforeThePayloadIsRead_AndCreatesNothing()
    {
        _user.WhoAmIFailure = DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "0x80040220 The user is disabled.");

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.PrincipalUnresolved);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Extensions["errorCode"].Should().Be(Sprk.Bff.Api.Api.ChildRecordEndpoints.CallerUnresolvedReasonCode);
        problem.Detail.Should().NotContain("0x80040220");
        Journal().Should().Equal("user:whoami");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_SkipsTheCallerSteps_RecordsTheRequester_AndRunsEveryOtherStep()
    {
        var request = await MappedRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_regardingmatter = Lookup("sprk_matter", OrdinaryMatter) });
        _journal.Clear();

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        Steps(outcome).Should().Equal(
            (RecordCreationStep.Principal, RecordCreationStepState.Ran),
            (RecordCreationStep.TableRules, RecordCreationStepState.Skipped),
            (RecordCreationStep.FieldMapping, RecordCreationStepState.NoOp),
            (RecordCreationStep.MapPayload, RecordCreationStepState.Ran),
            (RecordCreationStep.CallerChecks, RecordCreationStepState.Skipped),
            (RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran),
            (RecordCreationStep.Owner, RecordCreationStepState.Ran),
            (RecordCreationStep.SearchIndexDefault, RecordCreationStepState.NoOp),
            (RecordCreationStep.CreatorAndDefaults, RecordCreationStepState.Ran),
            (RecordCreationStep.StampAndInheritance, RecordCreationStepState.Ran),
            (RecordCreationStep.Create, RecordCreationStepState.Ran),
            (RecordCreationStep.PostCreate, RecordCreationStepState.Ran));
        Journal().Should().Equal("owner", "create", "restamp", "mirror", "membership");
        _creates.Should().ContainSingle().Which.Fields["sprk_CreatedByPerson@odata.bind"]
            .Should().Be($"/systemusers({Caller:D})", "the system principal's requester is recorded");
    }

    [Fact]
    public async Task CreateAsync_ASystemCreateAndACallerCreateUnderTheSameParent_GetTheSameOwnerCreatorAndStamp()
    {
        var service = Service();
        var asCaller = await service.CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "A", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);
        var asSystem = await service.CreateAsync(
            await MappedRequest("sprk_event", new { sprk_eventname = "B", sprk_regardingmatter = Lookup("sprk_matter", OrdinaryMatter) }),
            RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        asCaller.Succeeded.Should().BeTrue(asCaller.Refusal?.Detail);
        asSystem.Succeeded.Should().BeTrue(asSystem.Refusal?.Detail);
        _creates.Should().HaveCount(2);
        var (callerFields, systemFields) = (_creates[0].Fields, _creates[1].Fields);
        Owner(systemFields).Should().Be(Owner(callerFields)).And.Be(Directory.ChildTeam);
        systemFields["sprk_CreatedByPerson@odata.bind"].Should().Be(callerFields["sprk_CreatedByPerson@odata.bind"]);
        asSystem.OwningTeamId.Should().Be(asCaller.OwningTeamId);

        // The stamp (step 10, derived after the create): the same re-stamp, the same derived columns, on each row.
        _restampReads.Should().Contain([("sprk_event", asCaller.CreatedId!.Value), ("sprk_event", asSystem.CreatedId!.Value)],
            "the re-stamp derived each row's stamp from what it is filed under");
        _stamps.RowOf("sprk_event", asSystem.CreatedId!.Value).Attributes.Select(a => (a.Key, Value: Describe(a.Value)))
            .Where(a => a.Key != "sprk_eventname")
            .Should().BeEquivalentTo(
                _stamps.RowOf("sprk_event", asCaller.CreatedId!.Value).Attributes.Select(a => (a.Key, Value: Describe(a.Value)))
                    .Where(a => a.Key != "sprk_eventname"));
        _stamps.PatchesTo("sprk_event", asSystem.CreatedId!.Value).Select(p => p.Fields.Keys.Order().ToArray())
            .Should().BeEquivalentTo(_stamps.PatchesTo("sprk_event", asCaller.CreatedId!.Value).Select(p => p.Fields.Keys.Order().ToArray()));
    }

    // =====================================================================================
    // Negative cases: nothing is created
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_APayloadNamingAColumnTheServerOwns_IsRefusedBeforeAnyCheckOrWrite()
    {
        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", createdon = "2020-01-01T00:00:00Z" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Detail.Should().Contain("'createdon'");
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status403Forbidden);
        Journal().Should().NotContain(["user:check", "owner", "create"]);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentPayloadNamingItsServerOwnedColumn_IsRefusedForEveryCallerPath()
    {
        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_workassignment", new { sprk_name = "Review", sprk_accesspermission = 1 }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Detail.Should().Be("Column 'sprk_accesspermission' of a work assignment is set by Spaarke — omit it.");
        Journal().Should().NotContain(["user:check", "owner", "create"]);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenACallerCheckThrows_Is503_AndCreatesNothing()
    {
        _user.ThrowOn = path => path.Contains("RetrieveUserSetOfPrivilegesByNames", StringComparison.Ordinal)
            ? new TimeoutException("privilege read timed out at 10.0.0.7")
            : null;

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.CheckFailedCode);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Detail.Should().NotContain("10.0.0.7").And.NotContain("timed out");
        Journal().Should().NotContain(["owner", "create"]);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AMissingParentAndAParentTheCallerCannotUse_AnswerTheSameStatusAndBody()
    {
        _scripted.Missing.Add(MissingMatter);
        var missing = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({MissingMatter:D})" }),
            AsCaller(), CancellationToken.None);

        _scripted.NoAppendTo.Add(OrdinaryMatter);
        var denied = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        missing.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        denied.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        var missingBody = JsonSerializer.Serialize(Problem(missing, "sprk_event"));
        var deniedBody = JsonSerializer.Serialize(Problem(denied, "sprk_event"));
        deniedBody.Should().Be(missingBody, "a record that does not exist and one the caller cannot use are indistinguishable");
        Problem(denied, "sprk_event").Status.Should().Be(StatusCodes.Status404NotFound);
        missingBody.Should().NotContain(MissingMatter.ToString("D")).And.NotContain(OrdinaryMatter.ToString("D"));
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenTheOwnerDecisionFaults_Is503OwnerUnavailable_AndCreatesNothing()
    {
        _resolverFault = new TimeoutException("Dataverse timed out reading teams");

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.OwnerUnavailableCode);
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.Extensions["errorCode"].Should().Be(RecordCreationRefusal.OwnerUnavailableCode);
        problem.Detail.Should().NotContain("teams");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AnUnfiledCommunication_IsRefusedByItsOwnershipException_AndCreatesNothing()
    {
        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_communication", new { sprk_subject = "Hello" }), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.OwnershipExceptionCode);
        Problem(outcome, "sprk_communication").Status.Should().Be(StatusCodes.Status409Conflict);
        Journal().Should().NotContain(["owner", "create"]);
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // Step 12 never throws
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_WhenTheNumberReadBackFaults_LogsIt_AndReturnsTheCreatedId()
    {
        _readBack
            .Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("read-back timed out"));

        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_matter", new { sprk_mattername = "Acme v. Widget" }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.CreatedId.Should().Be(_creates.Should().ContainSingle().Subject.Id);
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Exception is TimeoutException
            && e.Message.Contains("could not be read back", StringComparison.Ordinal));
        _publisher.Published.Should().ContainSingle("the steps after a failed one still run");
    }

    [Fact]
    public async Task CreateAsync_WhenTheShareeMirrorFaults_LogsIt_AndReturnsTheCreatedId()
    {
        _shareWorld.FailingQueriesOf("sprk_event");

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.CreatedId.Should().Be(_creates.Should().ContainSingle().Subject.Id);
        _logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("mirror", StringComparison.Ordinal));
        _publisher.Published.Should().ContainSingle("the steps after a failed one still run");
    }

    // =====================================================================================
    // Step 5½ — the pre-write rules (ebilling-r1 recipe; a TEST rule: the invoice-family rule is not on this branch)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_APreWriteRuleRuns_AfterTheCallerChecks_AndBeforeTheOwner()
    {
        var asked = WithTestRule(_ => null);

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        asked.Should().ContainSingle().Which.Table.Should().Be("sprk_event");
        Journal().Should().Equal(
            "user:whoami", "user:map", "user:check", "prewrite", "owner", "create", "restamp", "mirror", "membership");
        outcome.Steps.Select(s => s.Step).Should().ContainInOrder(
            RecordCreationStep.CallerChecks, RecordCreationStep.PreWriteRules, RecordCreationStep.SecureRootPlan);
    }

    [Fact]
    public async Task CreateAsync_APreWriteRuleRefusal_IsCarriedByReference_AndCreatesNothing()
    {
        var refused = PreWriteRefusal.Refused("test.prewrite_refused", "The test rule refuses this row.");
        WithTestRule(_ => refused);

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.PreWrite.Should().BeSameAs(refused, "ebilling-r1 correction (c): the rule's own object");
        outcome.Refusal.Code.Should().Be("test.prewrite_refused");
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status400BadRequest);
        Journal().Should().NotContain(["owner", "create"]);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_APreWriteRuleNotFound_IsTheUniformNotFound_WithTheChatCreatesWords()
    {
        var notFound = PreWriteRefusal.NotFound();
        WithTestRule(_ => notFound);

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        outcome.Refusal.Detail.Should().Be(RecordCreationService.NotAvailableToFileUnder, "ebilling-r1 correction (b)");
        outcome.Refusal.PreWrite.Should().BeSameAs(notFound);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status404NotFound);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_APreWriteRuleClientFailure_IsTheCallersOwnFailure_WithoutItsText()
    {
        WithTestRule(_ => PreWriteRefusal.Failed(
            DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.RateLimited, "Rate limit exceeded for tenant 1234")));

        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status429TooManyRequests);
        problem.Detail.Should().NotContain("tenant 1234");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_OnATableWithAPreWriteRule_IsRefused_WithoutAskingTheRule()
    {
        var asked = WithTestRule(_ => null);
        var request = await MappedRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_regardingmatter = Lookup("sprk_matter", OrdinaryMatter) });

        var outcome = await Service().CreateAsync(request, RecordCreationPrincipal.ForSystem(), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.SystemPrincipalUnsupportedCode);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status403Forbidden);
        asked.Should().BeEmpty();
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // Step 11 — the autonumber retry (NFR-03): the platform's own number only, on the SAME id
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AMatterWhoseNumberIsTakenTwice_IsRetriedOnTheSameId_AndCreatesOneRow()
    {
        _createFaults.Enqueue(NumberTaken());
        _createFaults.Enqueue(NumberTaken());

        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_matter", new { sprk_mattername = "Acme v. Widget" }), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _createAttempts.Should().HaveCount(RecordCreationService.MaxNumberAttempts);
        _createAttempts.Select(a => a.Id).Distinct().Should().ContainSingle(
            "a retry re-sends the SAME id, so an upsert that did land cannot be created twice");
        _creates.Should().ContainSingle().Which.Id.Should().Be(outcome.CreatedId!.Value);
    }

    [Fact]
    public async Task CreateAsync_AMatterWhoseNumberIsTakenOnEveryAttempt_Is409NumberUnavailable_AndRunsNoPostCreateStep()
    {
        for (var attempt = 0; attempt < RecordCreationService.MaxNumberAttempts; attempt++)
            _createFaults.Enqueue(NumberTaken());

        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_matter", new { sprk_mattername = "Acme v. Widget" }), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Conflict);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.NumberUnavailableCode);
        Problem(outcome, "sprk_matter").Status.Should().Be(StatusCodes.Status409Conflict);
        _createAttempts.Should().HaveCount(RecordCreationService.MaxNumberAttempts);
        _creates.Should().BeEmpty();
        _journal.Should().NotContain(["readback", "assigned-access", "membership"]);
        Steps(outcome).Should().NotContain(s => s.Step == RecordCreationStep.PostCreate);
    }

    [Fact]
    public async Task CreateAsync_WhenTheCreateFailsForAnyOtherReason_ItPropagatesAfterOneAttempt()
    {
        _createFaults.Enqueue(new HttpRequestException("Dataverse answered 500", null, System.Net.HttpStatusCode.InternalServerError));
        var request = await MappedRequest("sprk_matter", new { sprk_mattername = "Acme v. Widget" });

        var create = () => Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        await create.Should().ThrowAsync<HttpRequestException>();
        _createAttempts.Should().ContainSingle("a create that faulted may have landed, so it is never retried (NFR-03)");
    }

    [Fact]
    public async Task CreateAsync_ADuplicateOnATableWithNoAutonumber_PropagatesAfterOneAttempt()
    {
        _createFaults.Enqueue(NumberTaken());

        var create = () => Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({OrdinaryMatter:D})" }),
            AsCaller(), CancellationToken.None);

        await create.Should().ThrowAsync<HttpRequestException>();
        _createAttempts.Should().ContainSingle("only an autonumber table's number is retried");
    }

    [Fact]
    public async Task CreateAsync_ACallerPayloadNamingTheAutonumber_IsRefusedAsABlockedColumn_BeforeAnyCheckOrWrite()
    {
        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_matter", new { sprk_mattername = "Acme v. Widget", sprk_matternumber = "MAT-000001" }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.BlockedColumn);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.BlockedColumnCode);
        outcome.Refusal.Detail.Should().Be("Column 'sprk_matternumber' of a matter is set by Spaarke — omit it.");
        Problem(outcome, "sprk_matter").Status.Should().Be(StatusCodes.Status403Forbidden);
        Journal().Should().NotContain(["user:check", "owner", "create"]);
        _createAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ASystemItemNamingItsOwnNumber_IsNotRetried_WhenThatNumberIsTaken()
    {
        _createFaults.Enqueue(NumberTaken());
        var request = await MappedRequest("sprk_matter", new { sprk_mattername = "Imported", sprk_matternumber = "MAT-000001" });

        var create = () => Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        await create.Should().ThrowAsync<HttpRequestException>("the row collides on the number it names, which no retry changes");
        _createAttempts.Should().ContainSingle();
    }

    // =====================================================================================
    // The owner over a SECURE parent (UAC-r2 condition (b)): secure-if-any, and the G5 core's two secure-filing refusals
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AnEventFiledUnderASecureMatter_IsOwnedByTheSecureTeam()
    {
        var outcome = await Service().CreateAsync(
            WebApiRequest("sprk_event", new { sprk_eventname = "Hearing", sprk_RegardingMatter_bind = $"/sprk_matters({SecureMatter:D})" }),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.SecureNamedTeam, "secure-if-any: a child of a secure matter is owned by the named Secure team");
        outcome.OwningTeamId.Should().Be(Directory.SecureNamedTeam);
        outcome.Isolated.Should().BeFalse("a child follows its parent's owner; only a work assignment or project is created into isolation");
        fields["sprk_CreatedByPerson@odata.bind"].Should().Be($"/systemusers({Caller:D})");
        Journal().Should().ContainInOrder("owner", "create", "mirror");
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentTheOwnerFindsSecure_ButThePlanDidNot_Is409NoOwnerSource_AndCreatesNothing()
    {
        // The plan reads a Dataverse with no rows (no secure parent); the resolver's world says the matter IS secure. The
        // two cannot be reconciled at create time, so the G5 core's refusal applies: never a root owned by the Secure team
        // outside isolation.
        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_workassignment", new { sprk_name = "Review", sprk_regardingmatter = Lookup("sprk_matter", SecureMatter) }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        outcome.Refusal.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        outcome.Refusal.Detail.Should().Contain("could not be decided consistently").And.Contain("NOT created");
        Problem(outcome, "sprk_workassignment").Status.Should().Be(StatusCodes.Status409Conflict);
        _createAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ANonReparentableRecordUnderASecureMatter_Is409NoOwnerSource_AndCreatesNothing()
    {
        // sprk_signal is a registry table that is no ownership parent, so it may not take the Secure owner (the G5 core's
        // second secure-filing refusal). The arm only — the Signal writer itself does not route through this service.
        _scripted.Held.UnionWith(["prvCreatesprk_signal", "prvAppendsprk_signal"]);
        var outcome = await Service().CreateAsync(
            await MappedRequest("sprk_signal", new { sprk_name = "Deadline risk", sprk_matter = Lookup("sprk_matter", SecureMatter) }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved, outcome.Refusal.Detail);
        outcome.Refusal.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        outcome.Refusal.Detail.Should().Contain("cannot be filed under a secure record").And.Contain("NOT created");
        Problem(outcome, "sprk_signal").Status.Should().Be(StatusCodes.Status409Conflict);
        _createAttempts.Should().BeEmpty();
    }

    // =====================================================================================
    // A system principal and a work assignment or project filed under a record (fix round 1, fail closed)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AsTheSystem_AWorkAssignmentFiledUnderARecord_Is403_BeforeThePlan_AndCreatesNothing()
    {
        var request = await MappedRequest("sprk_workassignment",
            new { sprk_name = "Review", sprk_regardingmatter = Lookup("sprk_matter", OrdinaryMatter) });
        _journal.Clear();

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.SystemPrincipalUnsupportedCode,
            "whether the matter is secure is known only once the plan reads it, and its checks are the caller's");
        Problem(outcome, "sprk_workassignment").Status.Should().Be(StatusCodes.Status403Forbidden);
        outcome.Steps.Should().ContainSingle(s => s.Step == RecordCreationStep.SecureRootPlan)
            .Which.State.Should().Be(RecordCreationStepState.Refused);
        _journal.Should().BeEmpty("refused before the plan, the owner and the create");
        _createAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_AWorkAssignmentFiledUnderNothing_IsCreated()
    {
        var request = await MappedRequest("sprk_workassignment", new { sprk_name = "Review" });
        _journal.Clear();

        var outcome = await Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Table.Should().Be("sprk_workassignment");
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static HttpRequestException NumberTaken() =>
        new("A record with matching key values already exists (0x80060892).", null, System.Net.HttpStatusCode.PreconditionFailed);

    private RecordCreationService Service()
    {
        var resolver = new JournalingResolver(_world.Resolver(_resolverFault), _journal);

        var restampEntities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        restampEntities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, string[] c, CancellationToken ct) =>
            {
                _restampReads.Add((t, id));
                return Journaled("restamp", () => _stamps.Service.RetrieveAsync(t, id, c, ct));
            });
        restampEntities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, Dictionary<string, object> f, CancellationToken ct) => Journaled("restamp", () => _stamps.Service.UpdateAsync(t, id, f, ct)));
        restampEntities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken ct) => Journaled("restamp", () => _stamps.Service.RetrieveMultipleAsync(q, ct)));
        restampEntities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression q, CancellationToken ct) => Journaled("restamp", () => _stamps.Service.RetrieveMultipleAsync(q, ct)));
        var restamper = new CoreAncestorRestamper(restampEntities.Object, InheritResolver(), NullLogger<CoreAncestorRestamper>.Instance);

        var shares = SecureChildShareWorld.SynchronizerOver(() =>
        {
            _journal.Add("mirror");
            return _shareWorld;
        }, new FakeRecordShareTable());

        return new RecordCreationService(
            resolver, _appOnly.Object, _readBack.Object, SecureRootFilingGateFixtures.NothingSecure(), restamper, shares,
            new JournalingScopes(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _journal),
            _publisher, _logger)
        {
            TableRules = t => _tableRules(t),
        };
    }

    private async Task<T> Journaled<T>(string entry, Func<Task<T>> run)
    {
        _journal.Add(entry);
        return await run();
    }

    private async Task Journaled(string entry, Func<Task> run)
    {
        _journal.Add(entry);
        await run();
    }

    /// <summary>Registers a test pre-write rule on the event table; returns the rows it was asked about.</summary>
    private List<PreWriteRow> WithTestRule(Func<PreWriteRow, PreWriteRefusal?> decide)
    {
        var asked = new List<PreWriteRow>();
        var rule = RecordTableRules.Get("sprk_event") with
        {
            PreWriteRules =
            [
                new PreWriteRule(new NamedRule("SeamTestRule", "A fixture rule standing in for the invoice-family guard."),
                    (row, _) =>
                    {
                        _journal.Add("prewrite");
                        asked.Add(row);
                        return Task.FromResult(decide(row));
                    }),
            ],
        };
        _tableRules = t => t == "sprk_event" ? rule : RecordTableRules.Find(t);
        return asked;
    }

    private RecordCreationPrincipal AsCaller() => RecordCreationPrincipal.ForCaller(_user, CallerOid);

    /// <summary>A browser writer's Web API body (<c>_bind</c> in a property name stands for <c>@odata.bind</c>).</summary>
    private static RecordCreationRequest WebApiRequest(string table, object body) =>
        new(table, new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(body))!
                .ToDictionary(kv => kv.Key.EndsWith("_bind", StringComparison.Ordinal) ? kv.Key[..^"_bind".Length] + "@odata.bind" : kv.Key,
                    kv => kv.Value))))
        {
            CorrelationId = FixedCorrelation,
        };

    /// <summary>A chat-shaped item, mapped as a server writer would hand it over.</summary>
    private async Task<RecordCreationRequest> MappedRequest(string table, object item)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(_scripted, table, JsonSerializer.SerializeToElement(item), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new RecordCreationRequest(table, new RecordCreationPayload.Mapped(mapped.Item!)) { CorrelationId = FixedCorrelation };
    }

    private static object Lookup(string table, Guid id) => new { relatedTable = table, recordId = id };

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Problem(RecordCreationOutcome outcome, string table) =>
        RecordCreationProblemMap.ToProblemDetails(outcome.Refusal!, table, outcome.CorrelationId);

    private static IEnumerable<(RecordCreationStep Step, RecordCreationStepState State)> Steps(RecordCreationOutcome outcome) =>
        outcome.Steps.Select(s => (s.Step, s.State));

    /// <summary>The journal with consecutive repeats collapsed (a step's several reads are one step).</summary>
    private List<string> Journal()
    {
        var collapsed = new List<string>();
        foreach (var entry in _journal)
        {
            if (collapsed.Count == 0 || collapsed[^1] != entry)
                collapsed.Add(entry);
        }

        return collapsed;
    }

    private static Guid Owner(IReadOnlyDictionary<string, object?> fields) =>
        fields.TryGetValue("ownerid@odata.bind", out var bind) && bind is string path
        && Guid.TryParse(path["/teams(".Length..^1], out var team)
            ? team
            : Guid.Empty;

    private static string Describe(object? value) => value switch
    {
        EntityReference reference => $"{reference.LogicalName}:{reference.Id:D}",
        null => "null",
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>The app-only create as Dataverse would store it: every lookup bind as a column, in both worlds.</summary>
    private void Materialize(string table, Guid id, Dictionary<string, object?> fields)
    {
        var lookups = new List<(string Column, string Target, Guid Id)>();
        var shareColumns = new List<(string Column, object Value)>();
        foreach (var (key, value) in fields)
        {
            if (!key.EndsWith("@odata.bind", StringComparison.Ordinal))
                continue;
            var path = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;
            if (path is null)
                continue;
            var trimmed = path.TrimStart('/');
            var set = trimmed[..trimmed.IndexOf('(')];
            var target = Guid.Parse(trimmed[(trimmed.IndexOf('(') + 1)..trimmed.IndexOf(')')]);
            var navigation = key[..^"@odata.bind".Length];
            if (navigation == "ownerid")
            {
                shareColumns.Add(("owningteam", new EntityReference("team", target)));
            }
            else if (set != "systemusers")
            {
                lookups.Add((navigation.ToLowerInvariant(), set.TrimEnd('s'), target));
                shareColumns.Add((navigation.ToLowerInvariant(), new EntityReference(set.TrimEnd('s'), target)));
            }
        }

        _stamps.Row(table, id, [.. lookups]);
        if (SecureChildLineage.IsChild(table))
            _shareWorld.Add(table, id, [.. shareColumns]);
    }

    /// <summary>The caller's Dataverse, journaled by kind, with a WhoAmI failure and a thrown-fault switch.</summary>
    private sealed class JournalingUserClient(IDataverseUserClient inner, List<string> journal) : IDataverseUserClient
    {
        public DataverseUserResponse? WhoAmIFailure { get; set; }

        public Func<string, Exception?>? ThrowOn { get; set; }

        /// <summary>Answers a read itself (instead of the scripted Dataverse) when it returns a response; null lets it through.</summary>
        public Func<string, DataverseUserResponse?>? RespondTo { get; set; }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(relativePath);
            journal.Add(KindOf(path));
            if (ThrowOn?.Invoke(path) is { } fault)
                throw fault;
            if (RespondTo?.Invoke(path) is { } answer)
                return Task.FromResult(answer);
            if (path == "WhoAmI()" && WhoAmIFailure is { } failure)
                return Task.FromResult(failure);
            return inner.GetAsync(relativePath, cancellationToken);
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
        {
            journal.Add("user:post");
            return inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);
        }

        public Task<DataverseUserResponse> PostAsync(
            string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        {
            journal.Add("user:post");
            return inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);
        }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            journal.Add("user:patch");
            return inner.PatchAsync(relativePath, jsonBody, cancellationToken);
        }

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
        {
            journal.Add("user:delete");
            return inner.DeleteAsync(relativePath, cancellationToken);
        }

        private static string KindOf(string path) =>
            path == "WhoAmI()" ? "user:whoami"
            : path.Contains("ManyToOneRelationships", StringComparison.Ordinal) || path.Contains("EntitySetName", StringComparison.Ordinal)
                ? "user:map"
                : "user:check";
    }

    /// <summary>The REAL resolver, journaled.</summary>
    private sealed class JournalingResolver(IRecordOwnershipResolver inner, List<string> journal) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            journal.Add("owner");
            return inner.ResolveOwningTeamAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            journal.Add("owner");
            return inner.ResolveOwnerAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct)
        {
            journal.Add("reparent");
            return inner.ReparentAsync(request, applyChange, ct);
        }
    }

    /// <summary>The "Assigned *" step's scope, journaled (no materializer is registered: the job's, as in a host without one).</summary>
    private sealed class JournalingScopes(IServiceScopeFactory inner, List<string> journal) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            journal.Add("assigned-access");
            return inner.CreateScope();
        }
    }

    private sealed class RecordingPublisher(List<string> journal) : IMembershipEventPublisher
    {
        public List<MembershipChangedEvent> Published { get; } = [];

        public Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct)
        {
            journal.Add("membership");
            Published.Add(evt);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<RecordCreationService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
