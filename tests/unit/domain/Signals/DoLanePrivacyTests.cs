using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology platform R1 task 150 (owner D-141, answering uac-r2's DI-86 #1883): Do items are private to their assignee on every
/// per-Signal route. <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> is the one gate of the decision-plan, suppression and
/// commit routes; after its existing reads allow a Do-lane Signal, the worklist's own "mine" read (as the caller) must return the
/// Signal's subject. The caller-identity client is a small in-memory Dataverse that evaluates the "mine" clause the gate sends, so
/// who is admitted comes from the shared rule, not from the test. Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).
/// The commit route's colleague case is in <c>DecisionCommitServiceTests</c> (its rig).
/// </summary>
public sealed class DoLanePrivacyTests
{
    private static readonly Guid SignalId = Guid.Parse("15000000-0000-4000-8000-000000000001");
    private static readonly Guid VersionId = Guid.Parse("15000000-0000-4000-8000-000000000002");
    private static readonly Guid MatterType = Guid.Parse("15000000-0000-4000-8000-000000000003");
    private static readonly Guid MatterId = Guid.Parse("15000000-0000-4000-8000-000000000004");
    private static readonly Guid SubjectId = Guid.Parse("15000000-0000-4000-8000-000000000005");
    private static readonly Guid Alice = Guid.Parse("15000000-0000-4000-8000-0000000000a1");
    private static readonly Guid AliceContact = Guid.Parse("15000000-0000-4000-8000-0000000000c1");
    private static readonly Guid Bob = Guid.Parse("15000000-0000-4000-8000-0000000000a2");
    private static readonly Guid BobContact = Guid.Parse("15000000-0000-4000-8000-0000000000c2");
    private static readonly Guid Team = Guid.Parse("15000000-0000-4000-8000-0000000000e1");

    private static string PersonColumn(string table) => $"_{SignalReadService.DoLaneMineRules[table].PersonColumn}_value";

    private static readonly string CreatorColumn = $"_{RecordCreatorPersonColumn.LogicalName}_value";

    public static IEnumerable<object[]> EveryMineTableWithAndWithoutACore() =>
        SignalReadService.DoLaneMineRules.Keys.SelectMany(t => new[] { new object[] { t, true }, new object[] { t, false } });

    // ---- Allowed: the people the worklist's "mine" rule admits ----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EveryMineTableWithAndWithoutACore))]
    public async Task ADoItemAssignedToTheCallersContact_IsAllowed_AndASameBuColleagueWhoReadsEverything_IsNot(string table, bool hasCore)
    {
        // The team-owned item (D-129): its person column names Alice's contact. Bob reads the Signal, the matter and the subject.
        var world = new World(table, hasCore) { Subject = { [PersonColumn(table)] = AliceContact, ["_ownerid_value"] = Team } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Allowed);
        (await world.As(Bob).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData("sprk_todo", true)]
    [InlineData("sprk_event", true)]
    [InlineData("sprk_workassignment", false)]  // D-44: a work assignment is the assigner's (creator) and the assignee's, never its owner's
    public async Task ADoItemOwnedByTheCaller_IsAllowedOnlyWhereTheTablesRuleCountsTheOwner(string table, bool allowed)
    {
        var world = new World(table, hasCore: true) { Subject = { ["_ownerid_value"] = Alice } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome
            .Should().Be(allowed ? SignalAccessOutcome.Allowed : SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData("sprk_event", true)]
    [InlineData("sprk_workassignment", true)]
    [InlineData("sprk_todo", false)]
    public async Task ADoItemCreatedByTheCaller_IsAllowedOnlyWhereTheTablesRuleCountsTheCreator(string table, bool allowed)
    {
        var world = new World(table, hasCore: false) { Subject = { [CreatorColumn] = Alice, ["_ownerid_value"] = Team } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome
            .Should().Be(allowed ? SignalAccessOutcome.Allowed : SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task AnAllowedDoItem_CarriesTheCallersContact_SoTheSuppressionCountIsTheirOwn_152()
    {
        // Task 152 (D-145a): GET /suppression passes this contact to the count, which reports a Do item's count only to its own person.
        var world = new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact, ["_ownerid_value"] = Team } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).CallerContactId.Should().Be(AliceContact);
    }

    // ---- The colleague: every per-Signal route answers exactly as for an unreadable Signal ------------------------------------

    [Fact]
    public async Task TheDecisionPlanRoute_AnswersTheColleague_ExactlyAsAnUnreadableSignal()
    {
        var notMine = await PlanRoute(new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } }.As(Bob));
        var unreadable = await PlanRoute(new World("sprk_todo", hasCore: true) { SignalUnreadable = true }.As(Bob));

        Problem(notMine).Should().BeEquivalentTo(Problem(unreadable));
        Problem(notMine).Status.Should().Be(404);
    }

    [Fact]
    public async Task TheSuppressionRoute_AnswersTheColleague_ExactlyAsAnUnreadableSignal_AndCountsNothing()
    {
        var notMine = await SuppressionRoute(new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } }, Bob);
        var unreadable = await SuppressionRoute(new World("sprk_todo", hasCore: true) { SignalUnreadable = true }, Bob);

        Problem(notMine).Should().BeEquivalentTo(Problem(unreadable));
        Problem(notMine).Status.Should().Be(404, "a strict count service would have thrown (503) had the route gone past the gate");
    }

    // ---- Fail closed ----------------------------------------------------------------------------------------------------------

    public enum Fault { ContactUnreadable, IdentityThrows, WhoAmIFaults, EntitySetThrows, MineReadFaults, MineReadThrows, MineReadUnparseable }

    [Theory]
    [InlineData(Fault.ContactUnreadable)]
    [InlineData(Fault.IdentityThrows)]
    [InlineData(Fault.WhoAmIFaults)]
    [InlineData(Fault.EntitySetThrows)]
    [InlineData(Fault.MineReadFaults)]
    [InlineData(Fault.MineReadThrows)]
    [InlineData(Fault.MineReadUnparseable)]
    public async Task AFaultInTheMineRead_IsUnavailable_NeverAllowed(Fault fault)
    {
        var world = new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } };
        world.Inject(fault);

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Unavailable);
    }

    [Fact]
    public async Task AFaultInTheMineRead_Is503OnTheRoute_AsTheWorklistAnswers()
    {
        var world = new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } };
        world.Inject(Fault.MineReadFaults);

        Problem(await PlanRoute(world.As(Alice))).Status.Should().Be(503);
    }

    public static IEnumerable<object[]> MineReadAnswers() =>
    [
        [DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "no read on the table"), SignalAccessOutcome.NotFound],
        [DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.OboExchangeFailed, "obo"), SignalAccessOutcome.CallerUnresolved],
        // A row that is not the subject (never the case in Dataverse, but the gate matches the id, not "any row").
        [DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = new[] { new Dictionary<string, object> { ["sprk_todoid"] = Guid.NewGuid().ToString("D") } } })),
            SignalAccessOutcome.NotFound],
    ];

    [Theory]
    [MemberData(nameof(MineReadAnswers))]
    public async Task TheMineReadsOtherAnswers_DenyOrAreTheSingle403(DataverseUserResponse answer, SignalAccessOutcome expected)
    {
        var world = new World("sprk_todo", hasCore: true) { MineAnswer = answer, Subject = { [PersonColumn("sprk_todo")] = AliceContact } };

        (await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task TheCommitRoutesTwoStepForm_DefersMine_AndConfirmingItDeniesTheColleague()
    {
        var world = new World("sprk_todo", hasCore: true) { Subject = { [PersonColumn("sprk_todo")] = AliceContact } };
        var access = world.As(Bob);

        var readable = await access.AuthorizeAsync(SignalId, CancellationToken.None, enforceDoLaneMine: false);

        readable.Outcome.Should().Be(SignalAccessOutcome.Allowed, "Bob can read the Signal, the matter and the To Do");
        (readable.SubjectTable, readable.SubjectId).Should().Be(("sprk_todo", SubjectId));
        world.Requests.Should().NotContain(p => p.Contains("&$filter=", StringComparison.Ordinal));
        (await access.ConfirmDoLaneMineAsync(readable, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    // ---- Decide lane unchanged ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADecideSignal_IsDecidedAsBefore_WithNoMineRead()
    {
        var world = new World("sprk_todo", hasCore: true) { Lane = DecisionLane.Decide, Subject = { [PersonColumn("sprk_todo")] = AliceContact } };

        (await world.As(Bob).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Allowed);
        world.Requests.Should().NotContain(p => p == "WhoAmI()" || p.Contains("&$filter=", StringComparison.Ordinal));
    }

    // ---- One rule: the worklist and the gate send the same request ------------------------------------------------------------

    [Fact]
    public async Task TheGate_SendsExactlyTheWorklistsMineRequest()
    {
        var world = new World("sprk_event", hasCore: true) { Subject = { [PersonColumn("sprk_event")] = AliceContact } };

        await world.As(Alice).AuthorizeAsync(SignalId, CancellationToken.None);

        world.Requests.Where(p => p.Contains("&$filter=", StringComparison.Ordinal)).Should().Equal(
            SignalReadService.MineSubjectQuery(SignalReadService.DoLaneMineRules["sprk_event"], "sprk_events", [SubjectId], Alice, AliceContact));
    }

    [Fact]
    public void TheMinePredicate_IsBuiltInOnePlace_AndBothTheWorklistAndTheGateUseIt()
    {
        var signals = Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals");
        var read = File.ReadAllText(Path.Combine(signals, "SignalReadService.cs"));
        var gate = File.ReadAllText(Path.Combine(signals, "SignalCoreRecordAccess.cs"));

        // MineFilter is called only by MineSubjectQuery (its definition is the other match).
        Regex.Matches(read, @"\bMineFilter\(").Count.Should().Be(2);
        Regex.Matches(read, @"\bMineSubjectQuery\(").Count.Should().Be(2, "its definition and the worklist's Do-lane read");
        Regex.Matches(read, @"\bResolveMineCallerAsync\(").Count.Should().Be(2, "its definition and the worklist's caller resolution");
        gate.Should().Contain("SignalReadService.MineSubjectQuery(").And.Contain("SignalReadService.ResolveMineCallerAsync(");
        gate.Should().NotContain("MineFilter(").And.NotContain("_ownerid_value").And.NotContain("sprk_createdbyperson",
            "the gate never builds the predicate itself");
    }

    // ---- Task 154 (D-111): an inquiry's "Record the outcome" To Do is never offered Mark complete --------------------------------

    private static readonly Guid Inquiry = Guid.Parse("15000000-0000-4000-8000-0000000000f1");
    private static readonly Guid OtherInquiry = Guid.Parse("15000000-0000-4000-8000-0000000000f2");
    private const string TodoPlan = "{\"actions\":[\"mark-complete\",\"reschedule\",\"reassign\"],\"nextSteps\":[\"add-todo\"]}";

    private static World AlicesTodo(Guid rowId, string? notes, int? inquiryState = 0, bool inquiryReadFaults = false) =>
        new("sprk_todo", hasCore: true)
        {
            SubjectRowId = rowId, Notes = notes, Subject = { [PersonColumn("sprk_todo")] = AliceContact },
            InquiryState = inquiryState, InquiryReadFaults = inquiryReadFaults,
        };

    private static string Hint(Guid inquiry) => $"{InquiryReplyTodoCreator.NotesKey}{inquiry:D}";

    private static string InquiryStateRead(Guid inquiry) => $"sprk_servicerequests({inquiry:D})?$select=statecode,sprk_direction";

    [Fact]
    public async Task AnOpenInquirysOutcomeTodo_ItsPlanOffersNoMarkComplete_RescheduleAndReassignRemain_WithOneGetOfTheInquiry()
    {
        var inquiryTodo = AlicesTodo(InquiryDispositionService.TodoIdFor(Inquiry), Hint(Inquiry));
        var ordinary = AlicesTodo(SubjectId, null);

        var plan = Plan(await PlanRoute(inquiryTodo.As(Alice), TodoPlan));
        Plan(await PlanRoute(ordinary.As(Alice), TodoPlan));

        plan.Actions.Select(a => a.Code).Should().Equal("reschedule", "reassign");
        plan.Actions[0].IsRecommended.Should().BeTrue("the plan's next action becomes the recommendation");
        plan.NextSteps.Select(a => a.Code).Should().Equal("add-todo");
        inquiryTodo.Requests.Should().HaveCount(ordinary.Requests.Count + 1, "the gate's own mine read carries the notes; only the inquiry's state is read");
        inquiryTodo.Requests.Where(r => r.StartsWith("sprk_servicerequests", StringComparison.Ordinal)).Should().Equal(InquiryStateRead(Inquiry));
        inquiryTodo.Requests.Should().Contain(r => r.StartsWith("sprk_todos?$select=", StringComparison.Ordinal) && r.Contains("sprk_notes", StringComparison.Ordinal));
        ordinary.Requests.Should().NotContain(r => r.StartsWith("sprk_servicerequests", StringComparison.Ordinal), "an ordinary To Do costs nothing more");
    }

    [Theory]
    [InlineData(1)]     // the disposition was recorded (or the inquiry closed otherwise), but the To Do stayed open
    [InlineData(null)]  // the caller cannot read the inquiry (403/404)
    public async Task ATodoWhoseInquiryIsClosedOrUnreadable_IsOrdinary_ItsPlanOffersMarkComplete(int? inquiryState)
    {
        // Adversarial pass 1 F1: a closed inquiry's To Do that went overdue must not be stuck (409 already_resolved on the
        // disposition, 409 disposition_required on Mark complete).
        var world = AlicesTodo(InquiryDispositionService.TodoIdFor(Inquiry), Hint(Inquiry), inquiryState);

        var plan = Plan(await PlanRoute(world.As(Alice), TodoPlan));

        plan.Actions.Select(a => a.Code).Should().Equal("mark-complete", "reschedule", "reassign");
        world.Requests.Should().Contain(InquiryStateRead(Inquiry));
    }

    [Fact]
    public async Task WhenTheInquirysStateCannotBeRead_ThePlanIs503_NeverAGuess()
    {
        var world = AlicesTodo(InquiryDispositionService.TodoIdFor(Inquiry), Hint(Inquiry), inquiryReadFaults: true);

        var problem = Problem(await PlanRoute(world.As(Alice), TodoPlan));

        problem.Status.Should().Be(503);
        problem.Extensions["reasonCode"]!.ToString().Should().Be(DecisionPlanEndpoints.AccessUnavailableReasonCode);
    }

    [Fact]
    public async Task AHintBelowANoteTheAssigneeTyped_IsStillTheHint_ThePlanOffersNoMarkComplete()
    {
        // Adversarial pass 1 F2: the v5 id match is the proof, not the line the hint sits on.
        var world = AlicesTodo(InquiryDispositionService.TodoIdFor(Inquiry), $"Chased by phone on Monday\r\n{Hint(Inquiry)}\nwaiting");

        var plan = Plan(await PlanRoute(world.As(Alice), TodoPlan));

        plan.Actions.Select(a => a.Code).Should().Equal("reschedule", "reassign");
    }

    [Fact]
    public void TheWorklistsMineRead_SelectsTheColumnsItDidBefore_OnlyTheGatesSingleIdReadAddsTheNotes()
    {
        // Adversarial pass 1 K3: sprk_notes is ntext; the worklist's chunked read (up to 100 To Dos) never uses it.
        var rule = SignalReadService.DoLaneMineRules["sprk_todo"];
        static string SelectOf(string query) => query[(query.IndexOf("$select=", StringComparison.Ordinal) + 8)..query.IndexOf("&$filter=", StringComparison.Ordinal)];

        var worklist = SignalReadService.MineSubjectQuery(rule, "sprk_todos", [SubjectId, Guid.NewGuid()], Alice, AliceContact);
        var gate = SignalReadService.MineSubjectQuery(rule, "sprk_todos", [SubjectId], Alice, AliceContact, includeTodoNotes: true);

        SelectOf(worklist).Should().Be($"sprk_todoid,{PersonColumn("sprk_todo")},sprk_highpriority", "the base (pre-154) select, unchanged");
        SelectOf(gate).Should().Be($"sprk_todoid,{PersonColumn("sprk_todo")},sprk_highpriority,sprk_notes,_sprk_regardingmatter_value",
            "pass 2 F1: the To Do's matter too, for the commit route's matter fallback");
        gate[gate.IndexOf("&$filter=", StringComparison.Ordinal)..].Should().Be(
            SignalReadService.MineSubjectQuery(rule, "sprk_todos", [SubjectId], Alice, AliceContact)[worklist.IndexOf("&$filter=", StringComparison.Ordinal)..],
            "the same predicate: a column, never a filter");
        SelectOf(SignalReadService.MineSubjectQuery(SignalReadService.DoLaneMineRules["sprk_event"], "sprk_events", [SubjectId], Alice, AliceContact, includeTodoNotes: true))
            .Should().NotContain("sprk_notes", "only a To Do carries the hint").And.NotContain("_sprk_regardingmatter_value");
    }

    [Fact]
    public async Task AnOrdinaryTodo_StillOffersMarkComplete()
    {
        var plan = Plan(await PlanRoute(AlicesTodo(SubjectId, "Call the client back").As(Alice), TodoPlan));

        plan.Actions.Select(a => a.Code).Should().Equal("mark-complete", "reschedule", "reassign");
    }

    [Theory]
    [InlineData(false)]  // the hint names another inquiry: the derived id is not this To Do's
    [InlineData(true)]   // the hint is on the second line and its id matches: guarded (adversarial pass 1 F2)
    public async Task ATodoIsGuarded_OnlyWhenAHintDerivesItsOwnId_OnWhicheverLine(bool hintOnLineTwoDerivesTheId)
    {
        var notes = hintOnLineTwoDerivesTheId
            ? $"Chase this\n{Hint(Inquiry)}"
            : Hint(OtherInquiry);

        var world = AlicesTodo(InquiryDispositionService.TodoIdFor(Inquiry), notes);
        var plan = Plan(await PlanRoute(world.As(Alice), TodoPlan));

        if (hintOnLineTwoDerivesTheId)
        {
            plan.Actions.Select(a => a.Code).Should().NotContain("mark-complete");
        }
        else
        {
            plan.Actions.Select(a => a.Code).Should().Contain("mark-complete");
            world.Requests.Should().NotContain(r => r.StartsWith("sprk_servicerequests", StringComparison.Ordinal), "a hint that fails costs no request");
        }
    }

    // ---- Task 154 pass 2 F1: the plan identifies the To Do exactly as the commit does (the matter fallback included) -----------

    public enum InquiryListFault { Fails, MorePages, Throws }

    private static string MatterListOf(Guid matter) => $"_sprk_regardingmatter_value eq {matter:D}";

    [Theory]
    [InlineData(null)]                    // the notes were cleared
    [InlineData("Waiting on the firm")]   // the serviceRequestId= line was deleted in TodoDetail
    public async Task AHintlessTodo_OnAMatterWithItsOpenInquiry_GetsAPlanWithoutMarkComplete_LikeTheCommit(string? notes)
    {
        // Pass 2 F1: before, the plan read only the hint, so it offered (and recommended) Mark complete, and the commit refused it
        // with 409 inquiry.disposition_required.
        var world = new World("sprk_todo", hasCore: true)
        {
            SubjectRowId = InquiryDispositionService.TodoIdFor(Inquiry), Notes = notes, Subject = { [PersonColumn("sprk_todo")] = AliceContact },
            TodoMatter = MatterId, MatterInquiries = [OtherInquiry, Inquiry],
        };

        var plan = Plan(await PlanRoute(world.As(Alice), TodoPlan));

        plan.Actions.Select(a => a.Code).Should().Equal("reschedule", "reassign");
        plan.Actions[0].IsRecommended.Should().BeTrue();
        world.Requests.Where(r => r.StartsWith("sprk_servicerequests", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Contain(MatterListOf(MatterId), "one list of the matter's open inquiries, as the caller");
        world.Requests.Should().NotContain(r => r.StartsWith("sprk_todos(", StringComparison.Ordinal),
            "the gate's own mine read carried the To Do's matter: no To Do request of its own");
    }

    [Fact]
    public async Task AnOrdinaryTodoOnAMatter_StillOffersMarkComplete_AfterOneList()
    {
        // The matter has an open inquiry, but its derived To Do id is not this To Do's. The cost: one list per plan read.
        var world = new World("sprk_todo", hasCore: true)
        {
            SubjectRowId = SubjectId, Notes = "Call the client back", Subject = { [PersonColumn("sprk_todo")] = AliceContact },
            TodoMatter = MatterId, MatterInquiries = [Inquiry],
        };

        var plan = Plan(await PlanRoute(world.As(Alice), TodoPlan));

        plan.Actions.Select(a => a.Code).Should().Equal("mark-complete", "reschedule", "reassign");
        world.Requests.Where(r => r.StartsWith("sprk_servicerequests", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Contain(MatterListOf(MatterId));
    }

    [Theory]
    [InlineData(InquiryListFault.Fails)]
    [InlineData(InquiryListFault.MorePages)]
    [InlineData(InquiryListFault.Throws)]
    public async Task WhenTheMattersInquiriesCannotBeListed_ThePlanIs503_NeverMarkComplete(InquiryListFault fault)
    {
        var world = new World("sprk_todo", hasCore: true)
        {
            SubjectRowId = InquiryDispositionService.TodoIdFor(Inquiry), Notes = null, Subject = { [PersonColumn("sprk_todo")] = AliceContact },
            TodoMatter = MatterId, MatterInquiries = [Inquiry], InquiryListFails = fault,
        };

        var problem = Problem(await PlanRoute(world.As(Alice), TodoPlan));

        problem.Status.Should().Be(503);
        problem.Extensions["reasonCode"]!.ToString().Should().Be(DecisionPlanEndpoints.AccessUnavailableReasonCode);
    }

    // =================================================================================================
    // Fixtures
    // =================================================================================================

    private static Task<IResult> PlanRoute(SignalCoreRecordAccess access) =>
        DecisionPlanEndpoints.GetDecisionPlanAsync(
            SignalId, Http(), access,
            new DecisionPlanService(new Mock<IGenericEntityService>(MockBehavior.Strict).Object, NullLogger<DecisionPlanService>.Instance),
            new RuleBodyDescriber(
                new PolicyVersionValidator(new RuleBodySchemaValidator(), new PredicateCompiler(new RuleBodySchemaValidator(), TimeProvider.System),
                    NullLogger<PolicyVersionValidator>.Instance),
                new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance),
            CancellationToken.None);

    private static Task<IResult> SuppressionRoute(World world, Guid actingAs)
    {
        var access = world.As(actingAs);
        var strict = new SignalSuppressionService(
            new Mock<IGenericEntityService>(MockBehavior.Strict).Object,
            new OntologyWriterDataverseClient(() => new Mock<IOrganizationServiceAsync2>(MockBehavior.Strict).Object, NullLogger<OntologyWriterDataverseClient>.Instance),
            TimeProvider.System, new ConfigurationBuilder().Build(),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SignalSuppressionService>.Instance);
        return DecisionPlanEndpoints.GetSuppressionAsync(
            SignalId, Http(), access, strict, world.Client, NullLogger<SignalSuppressionService>.Instance, CancellationToken.None);
    }

    /// <summary>The plan route with the version's plan readable (configuration, the BFF's own read).</summary>
    private static Task<IResult> PlanRoute(SignalCoreRecordAccess access, string planJson)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("sprk_policyversion", VersionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_policyversion", VersionId) { ["sprk_decisionplan"] = planJson });
        return DecisionPlanEndpoints.GetDecisionPlanAsync(
            SignalId, Http(), access,
            new DecisionPlanService(entities.Object, NullLogger<DecisionPlanService>.Instance),
            new RuleBodyDescriber(
                new PolicyVersionValidator(new RuleBodySchemaValidator(), new PredicateCompiler(new RuleBodySchemaValidator(), TimeProvider.System),
                    NullLogger<PolicyVersionValidator>.Instance),
                new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance),
            CancellationToken.None);
    }

    private static DecisionPlanResponse Plan(IResult result) =>
        result.Should().BeOfType<Ok<DecisionPlanResponse>>().Subject.Value!;

    private static HttpContext Http() => new DefaultHttpContext { TraceIdentifier = "trace-150" };

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Problem(IResult result) =>
        result.Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    /// <summary>
    /// One Signal on a matter (or with no core), whose subject is one row of <see cref="Table"/>. Alice and Bob are in the same BU:
    /// both read the Signal, the matter and the subject. The "mine" read is evaluated like Dataverse would: the id list, then any
    /// <c>_column_value eq id</c> part of the OR-ed clause the gate sent.
    /// </summary>
    private sealed class World(string table, bool hasCore)
    {
        public string Table { get; } = table;
        public DecisionLane Lane { get; init; } = DecisionLane.Do;
        public bool SignalUnreadable { get; init; }
        public DataverseUserResponse? MineAnswer { get; init; }
        public Dictionary<string, Guid> Subject { get; } = new(StringComparer.Ordinal);

        /// <summary>The subject row's id (task 154: an inquiry-outcome To Do's id is derived from its inquiry).</summary>
        public Guid SubjectRowId { get; init; } = SubjectId;

        /// <summary>The subject row's <c>sprk_notes</c>, returned only when the request selects it (task 154).</summary>
        public string? Notes { get; init; }

        /// <summary>The hinted inquiry's <c>statecode</c> as the caller reads it (0 open, 1 closed); null when the caller cannot read it
        /// (404). Outbound. Task 154, adversarial pass 1.</summary>
        public int? InquiryState { get; init; } = 0;

        /// <summary>The caller's read of the inquiry faults (503).</summary>
        public bool InquiryReadFaults { get; init; }

        /// <summary>The subject To Do's matter (<c>_sprk_regardingmatter_value</c>), returned only when the request selects it (task 154
        /// pass 2 F1); null: the To Do has none.</summary>
        public Guid? TodoMatter { get; init; }

        /// <summary>The open outbound inquiries on <see cref="TodoMatter"/> as the caller lists them (the matter fallback).</summary>
        public Guid[] MatterInquiries { get; init; } = [];

        /// <summary>How the caller's list of the matter's open inquiries fails, when it does.</summary>
        public InquiryListFault? InquiryListFails { get; init; }
        public List<string> Requests { get; } = new();
        public IDataverseUserClient Client => _client.Object;

        private readonly Mock<IDataverseUserClient> _client = new();
        private Guid _actingAs;
        private Fault? _fault;

        public void Inject(Fault fault) => _fault = fault;

        public SignalCoreRecordAccess As(Guid user)
        {
            _actingAs = user;
            _client.Setup(u => u.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken _) =>
                {
                    Requests.Add(path);
                    if (_fault == Fault.MineReadThrows && path.Contains("&$filter=", StringComparison.Ordinal)
                        && !path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal))
                    {
                        return Task.FromException<DataverseUserResponse>(new TimeoutException("no answer"));
                    }

                    if (InquiryListFails == InquiryListFault.Throws && path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal))
                    {
                        return Task.FromException<DataverseUserResponse>(new TaskCanceledException("Dataverse timed out"));
                    }

                    return Task.FromResult(Answer(path));
                });

            var entities = new Mock<IGenericEntityService>();
            entities.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", MatterType, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_recordtype_ref", MatterType) { ["sprk_recordlogicalname"] = "sprk_matter" });
            entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string logical, CancellationToken _) => _fault == Fault.EntitySetThrows && logical == Table
                    ? Task.FromException<string>(new TimeoutException("no metadata"))
                    : Task.FromResult(logical + "s"));

            var identity = new Mock<IIdentityNormalizationService>();
            identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid id, CancellationToken _) => _fault switch
                {
                    Fault.IdentityThrows => Task.FromException<PersonIdentity>(new TimeoutException("no answer")),
                    Fault.ContactUnreadable => Task.FromResult(new PersonIdentity(id) { Faults = IdentityReadFaults.SystemUser }),
                    _ => Task.FromResult(new PersonIdentity(id, id == Alice ? AliceContact : id == Bob ? BobContact : null)),
                });

            return new SignalCoreRecordAccess(_client.Object, entities.Object, identity.Object, NullLogger<SignalCoreRecordAccess>.Instance);
        }

        private DataverseUserResponse Answer(string path)
        {
            if (path is "WhoAmI" or "WhoAmI()")
            {
                return _fault == Fault.WhoAmIFaults && path == "WhoAmI()"
                    ? DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom")
                    : Ok(new { UserId = _actingAs.ToString("D") });
            }

            if (path.StartsWith("sprk_signals(", StringComparison.Ordinal))
            {
                if (SignalUnreadable)
                {
                    return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
                }

                var row = new Dictionary<string, object?>
                {
                    ["sprk_lane"] = (int)Lane,
                    ["_sprk_policyversion_value"] = VersionId.ToString("D"),
                    [$"_{SignalWriter.RegardingLookupByEntity[Table]}_value"] = SubjectRowId.ToString("D"),
                    ["sprk_regardingrecordid"] = SubjectRowId.ToString("D"),
                };
                if (hasCore)
                {
                    row["_sprk_corerecordtype_value"] = MatterType.ToString("D");
                    row["sprk_corerecordid"] = MatterId.ToString("D");
                }

                return Ok(row);
            }

            if (path.StartsWith("sprk_servicerequests?", StringComparison.Ordinal))
            {
                // The matter's open outbound inquiries (the fallback), as the caller.
                var matter = Guid.Parse(Regex.Match(path, "_sprk_regardingmatter_value eq ([0-9a-f-]{36})").Groups[1].Value);
                var rows = matter == TodoMatter ? MatterInquiries.Select(i => new { sprk_servicerequestid = i.ToString("D") }).ToArray() : [];
                return InquiryListFails switch
                {
                    InquiryListFault.Fails => DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom"),
                    InquiryListFault.MorePages => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new Dictionary<string, object>
                    {
                        ["value"] = rows,
                        ["@odata.nextLink"] = "https://org.example/api/data/v9.2/sprk_servicerequests?$skiptoken=2",
                    })),
                    _ => Ok(new { value = rows }),
                };
            }

            if (path.Contains("&$filter=", StringComparison.Ordinal))
            {
                if (_fault == Fault.MineReadFaults)
                {
                    return DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");
                }

                if (_fault == Fault.MineReadUnparseable)
                {
                    return Ok(new { unexpected = true });
                }

                if (MineAnswer is { } answer)
                {
                    return answer;
                }

                var filter = path[(path.IndexOf("$filter=", StringComparison.Ordinal) + 8)..];
                var ids = Regex.Matches(filter[..(filter.IndexOf("])", StringComparison.Ordinal) + 2)], "'([0-9a-f-]{36})'")
                    .Select(m => Guid.Parse(m.Groups[1].Value));
                var mine = Regex.Matches(filter, @"(_\w+_value) eq ([0-9a-f-]{36})")
                    .Any(m => Subject.TryGetValue(m.Groups[1].Value, out var v) && v == Guid.Parse(m.Groups[2].Value));
                var pk = $"{Table}id";
                var subjectRow = new Dictionary<string, object> { [pk] = SubjectRowId.ToString("D") };
                if (Notes is not null && Regex.IsMatch(path, @"\$select=[^&]*sprk_notes"))
                {
                    subjectRow["sprk_notes"] = Notes;
                }

                if (TodoMatter is { } todoMatter && Regex.IsMatch(path, @"\$select=[^&]*_sprk_regardingmatter_value"))
                {
                    subjectRow["_sprk_regardingmatter_value"] = todoMatter.ToString("D");
                }

                return Ok(new { value = ids.Contains(SubjectRowId) && mine ? new[] { subjectRow } : [] });
            }

            if (path.StartsWith("sprk_servicerequests(", StringComparison.Ordinal))
            {
                if (InquiryReadFaults)
                {
                    return DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");
                }

                return InquiryState is { } state
                    ? Ok(new { statecode = state, sprk_direction = BudgetInquiryExecutor.DirectionOutbound })
                    : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "no");
            }

            // The matter and the subject row (createdon): both readable to Alice and Bob.
            return path.StartsWith("sprk_matters(", StringComparison.Ordinal) || path.StartsWith($"{Table}s(", StringComparison.Ordinal)
                ? Ok(new { createdon = "2026-10-01T00:00:00Z" })
                : DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "unscripted");
        }

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));
    }
}
