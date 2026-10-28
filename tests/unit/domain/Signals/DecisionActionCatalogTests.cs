using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T> (internal, same assembly)
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Behaviour tests for the closed decision-action catalog, the plan resolver and the Signal access decision
/// (ontology platform R1 task 036; spec FR-49/FR-50; D-17..D-20, D-45, D-51). Maintain-class (ADR-038,
/// <c>tests/unit/domain/**</c>): pure catalog logic plus the module-boundary fakes <see cref="IDataverseUserClient"/> and
/// <see cref="IGenericEntityService"/>. No DI-registration or constructor-null tests.
/// </summary>
[Trait("status", "new")]
public class DecisionActionCatalogTests
{
    // POL-COMMIT-BUDGET v2's plan as task 009 wrote it (notes/004-seed-policy-rows.md, "v2 re-seed").
    private const string SeededPlan = """{"actions":["send-budget-inquiry","revise-budget","approve-variance"],"nextSteps":[]}""";

    private static readonly Guid SignalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VersionId = Guid.Parse("e51906ea-62c2-f111-a05a-7c1e520a989f");
    private static readonly Guid CoreId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CoreTypeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OwnerId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // =================================================================================================
    // The catalog's contents
    // =================================================================================================

    [Fact]
    public void SeededPlan_ResolvesInPlanOrder_WithJudgementClass_AndApproveVarianceExcludingReviseBudget()
    {
        var resolution = DecisionPlanService.Resolve(SeededPlan, DecisionLane.Decide);

        resolution.IsRefused.Should().BeFalse();
        var actions = resolution.Plan!.Actions;
        actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
        actions.Select(a => a.Label).Should().Equal("Send budget inquiry", "Revise budget", "Approve variance");
        actions.Should().OnlyContain(a => a.RecordClass == DecisionRecordClass.Judgement);
        actions.Should().OnlyContain(a => a.Parameters.Count > 0 && a.EffectLines.Count > 0);
        actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget", "create-budget");
        resolution.Plan.NextSteps.Should().BeEmpty();
    }

    [Fact]
    public void SendBudgetInquiry_HasNoReplyDueParameter_D56_D20()
    {
        DecisionActionCatalog.TryGet("send-budget-inquiry", out var inquiry).Should().BeTrue();

        // Task 043 added the optional outside firm (sprk_regardingorganization); still no reply-due parameter (D-56/D-20).
        inquiry.Parameters.Select(p => p.Code).Should().Equal("to", "subject", "body", "outsideFirm");
        inquiry.Parameters.Single(p => p.Code == "outsideFirm").Required.Should().BeFalse();
    }

    [Fact]
    public void EveryChoiceParameter_CarriesOptionsOrANamedSource_AndNeverBoth()
    {
        var choices = DecisionActionCatalog.All.SelectMany(a => a.Parameters.Select(p => (a.Code, Param: p)))
            .Where(x => x.Param.Kind == DecisionParameterKind.Choice).ToList();

        choices.Should().NotBeEmpty();
        foreach (var (code, param) in choices)
        {
            var hasOptions = param.Options is { Count: > 0 };
            var hasSource = !string.IsNullOrWhiteSpace(param.OptionsSource);
            (hasOptions ^ hasSource).Should().BeTrue(because: $"{code}.{param.Code} must carry options or a source, not neither or both");
            if (hasOptions)
            {
                param.Options!.Select(o => o.Value).Should().OnlyHaveUniqueItems();
                param.Options!.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Value) && !string.IsNullOrWhiteSpace(o.Label));
            }
        }
    }

    [Fact]
    public void AnExcludeNamingAnUnknownCode_FailsCatalogConstruction_NotSilently()
    {
        DecisionActionDefinition Action(string code, params string[] excludes) =>
            new(code, code, DecisionLane.Decide, false, null, [], [], excludes, DecisionRecordClass.Judgement);

        var typo = () => DecisionActionCatalog.Mutualize([Action("a", "b-typo"), Action("b")]);
        var fine = DecisionActionCatalog.Mutualize([Action("a", "b"), Action("b")]);

        typo.Should().Throw<InvalidOperationException>().WithMessage("*'a' excludes 'b-typo'*not a declared action*");
        fine.Single(x => x.Code == "b").Excludes.Should().Equal("a");
    }

    [Fact]
    public void TheResponsePairsAreMutual_RecordTheResponseWithReminderAndExtend()
    {
        DecisionActionCatalog.TryGet("record-the-response", out var record).Should().BeTrue();
        DecisionActionCatalog.TryGet("extend-response-date", out var extend).Should().BeTrue();
        DecisionActionCatalog.TryGet("send-reminder", out var reminder).Should().BeTrue();

        record.Excludes.Should().BeEquivalentTo("send-reminder", "extend-response-date");
        extend.Excludes.Should().Equal("record-the-response");
        reminder.Excludes.Should().Equal("record-the-response");
        DecisionActionCatalog.FirstConflict(["extend-response-date", "send-reminder"]).Should().BeNull("those two are compatible");
    }

    [Fact]
    public void RecordTheResponse_OffersTheD58ResponseValues()
    {
        DecisionActionCatalog.TryGet("record-the-response", out var record).Should().BeTrue();

        record.Parameters.Single(p => p.Code == "response").Options!.Select(o => o.Label)
            .Should().Equal("Received outside Spaarke", "Delivered on the matter", "No longer needed");
    }

    [Fact]
    public void Excludes_AreMutual_SoNeitherOrderOfAPairCanBothBeTaken()
    {
        foreach (var action in DecisionActionCatalog.All)
        {
            foreach (var excluded in action.Excludes)
            {
                DecisionActionCatalog.TryGet(excluded, out var other).Should().BeTrue();
                other.Excludes.Should().Contain(action.Code, because: $"{action.Code} excludes {excluded}, so {excluded} must exclude {action.Code}");
            }
        }

        DecisionActionCatalog.FirstConflict(["revise-budget", "approve-variance"]).Should().NotBeNull();
        DecisionActionCatalog.FirstConflict(["approve-variance", "revise-budget"]).Should().NotBeNull();
        DecisionActionCatalog.FirstConflict(["mark-complete", "reassign"]).Should().NotBeNull();
        DecisionActionCatalog.FirstConflict(["reassign", "mark-complete"]).Should().NotBeNull();
        DecisionActionCatalog.FirstConflict(["send-reminder", "record-the-response"]).Should().NotBeNull();
        DecisionActionCatalog.FirstConflict(["reschedule", "reassign"]).Should().BeNull("reschedule and reassign are compatible");
    }

    [Fact]
    public void TheSeededPlansOrder_CannotTakeReviseBudgetAndApproveVarianceTogether()
    {
        // POL-COMMIT-BUDGET v2 lists revise-budget BEFORE approve-variance, the order a one-way exclusion would miss.
        var plan = DecisionPlanService.Resolve(SeededPlan, DecisionLane.Decide).Plan!;
        plan.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");

        DecisionActionCatalog.FirstConflict(plan.Actions.Select(a => a.Code)).Should().Be(("revise-budget", "approve-variance"));
        DecisionActionCatalog.FirstConflict(["send-budget-inquiry", "revise-budget"]).Should().BeNull();
        DecisionActionCatalog.FirstConflict(["send-budget-inquiry", "approve-variance"]).Should().BeNull();
        plan.Actions.Single(a => a.Code == "revise-budget").Excludes.Should().Equal("approve-variance");
        plan.Actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget", "create-budget");
    }

    [Fact]
    public void ReviseBudget_CarriesTheBudgetPickerParameter_D18()
    {
        DecisionActionCatalog.TryGet("revise-budget", out var revise).Should().BeTrue();

        revise.Parameters.Should().Contain(p =>
            p.Code == "budget" && p.Kind == DecisionParameterKind.Lookup && p.LookupEntity == "sprk_budget" && p.Required);
    }

    [Fact]
    public void NoEffectLine_ShowsAColumnOrTableName()
    {
        DecisionActionCatalog.All.SelectMany(a => a.EffectLines).Should().OnlyContain(l => !l.Contains("sprk_"));
    }

    [Fact]
    public void ApproveVariance_IsRecordOnly_ItsOneEffectLineSaysTheDecisionRecordIsTheApproval_D19()
    {
        DecisionActionCatalog.TryGet("approve-variance", out var approve).Should().BeTrue();

        approve.EffectLines.Should().Equal("The Decision Record is the approval; nothing else is written");
    }

    [Fact]
    public void DoLaneActions_HaveTheStatedExcludes_AndRecordClasses_D45()
    {
        DecisionActionCatalog.TryGet("mark-complete", out var complete).Should().BeTrue();
        complete.Excludes.Should().BeEquivalentTo("reschedule", "reassign");
        complete.RecordClass.Should().Be(DecisionRecordClass.Routine);

        foreach (var routine in new[] { "reschedule", "reassign", "extend-response-date" })
        {
            DecisionActionCatalog.TryGet(routine, out var action).Should().BeTrue();
            action.RecordClass.Should().Be(DecisionRecordClass.Routine, because: routine);
        }

        foreach (var judgement in new[] { "send-reminder", "record-the-response" })
        {
            DecisionActionCatalog.TryGet(judgement, out var action).Should().BeTrue();
            action.RecordClass.Should().Be(DecisionRecordClass.Judgement, because: judgement);
        }
    }

    [Fact]
    public void EveryExcludeNamesACatalogAction_InTheSameLane_AndNoActionExcludesItself()
    {
        foreach (var action in DecisionActionCatalog.All)
        {
            action.Excludes.Should().NotContain(action.Code);
            foreach (var excluded in action.Excludes)
            {
                DecisionActionCatalog.TryGet(excluded, out var target).Should().BeTrue(because: $"{action.Code} excludes {excluded}");
                target.PlanLane.Should().Be(action.PlanLane, because: $"{action.Code} and {excluded} share a lane");
            }
        }
    }

    [Fact]
    public void EveryNextStepCreatorIsJudgement_BecauseANextStepMakesTheRecordJudgement_R2()
    {
        var nextSteps = DecisionActionCatalog.All.Where(a => a.IsNextStep).ToList();

        nextSteps.Select(a => a.Code).Should().BeEquivalentTo("add-todo", "create-event", "send-email", "assign-work");
        nextSteps.Should().OnlyContain(a => a.RecordClass == DecisionRecordClass.Judgement);
    }

    [Theory]
    [InlineData("escalate")]
    [InlineData("extend-sla")]
    [InlineData("close-inquiry")]
    public void TheCatalogHasNoInquiryActions_D20(string code)
    {
        DecisionActionCatalog.TryGet(code, out _).Should().BeFalse();
        DecisionActionCatalog.All.Select(a => a.Code).Should().NotContain(code);
    }

    [Fact]
    public void CatalogCodesAreUnique_AndExactlyTheStatedSet()
    {
        DecisionActionCatalog.All.Select(a => a.Code).Should().BeEquivalentTo(
            "send-budget-inquiry", "revise-budget", "create-budget", "approve-variance", "assign-task",
            "mark-complete", "reschedule", "reassign", "send-reminder", "extend-response-date", "record-the-response",
            "add-todo", "create-event", "send-email", "assign-work");
    }

    [Fact]
    public void AssignWork_ResolvesAsADecidePlanAction_AndAsANextStep_D51()
    {
        var asAction = DecisionPlanService.Resolve("""{"actions":["assign-work"]}""", DecisionLane.Decide);
        var asNextStep = DecisionPlanService.Resolve("""{"actions":["send-budget-inquiry"],"nextSteps":["assign-work"]}""", DecisionLane.Decide);

        asAction.IsRefused.Should().BeFalse();
        asAction.Plan!.Actions.Single().RecordClass.Should().Be(DecisionRecordClass.Judgement);
        asNextStep.Plan!.NextSteps.Single().Code.Should().Be("assign-work");
    }

    [Fact]
    public void AssignTask_IsADecideLaneActionNotANextStep_WithTheD178Shape()
    {
        DecisionActionCatalog.TryGet("assign-task", out var action).Should().BeTrue();

        action.Label.Should().Be("Assign Task");
        action.PlanLane.Should().Be(DecisionLane.Decide);
        action.IsNextStep.Should().BeFalse("a Task is the decision itself here; Add To Do and Assign Work are its Next steps (D-178)");
        action.RecordClass.Should().Be(DecisionRecordClass.Judgement);
        action.ResolvesSignal.Should().BeTrue();
        action.SendsMessage.Should().BeFalse();
        action.Excludes.Should().BeEmpty();
        DecisionGateTier.Resolve(action.Gate!).Should().Be(GateRiskTier.Tier3);
        action.Parameters.Select(p => (p.Code, p.Kind, p.Required)).Should().Equal(
            ("assignee", DecisionParameterKind.Person, true),
            ("description", DecisionParameterKind.TextArea, true),
            ("dueDate", DecisionParameterKind.Date, false));
        action.EffectLines.Should().Equal("Creates a task (an event of type Task) linked to the record, assigned to the person above, when the decision is recorded");
    }

    [Fact]
    public void TheNoBudgetV2Plan_ResolvesForDecide_AssignTaskAsTheAction_AndAddTodoAndAssignWorkAsNextSteps()
    {
        var resolution = DecisionPlanService.Resolve("""{"actions":["assign-task"],"nextSteps":["add-todo","assign-work"]}""", DecisionLane.Decide);

        resolution.IsRefused.Should().BeFalse(resolution.RefusalReason);
        resolution.Plan!.Actions.Select(a => a.Code).Should().Equal("assign-task");
        resolution.Plan.NextSteps.Select(a => a.Code).Should().Equal("add-todo", "assign-work");
    }

    [Fact]
    public void DismissalReasons_AreExactlyTheStatedClosedLists_AndOnlyWrongMatterDoesNotCountTowardSuppression()
    {
        var decide = DecisionActionCatalog.DismissalReasons(DecisionLane.Decide);
        var doLane = DecisionActionCatalog.DismissalReasons(DecisionLane.Do);

        decide.Select(r => r.Label).Should().Equal(
            "Already approved offline", "Not material", "Wrong matter - misresolved", "Duplicate", "Handled outside Spaarke", "Other");
        doLane.Select(r => r.Label).Should().Equal(
            "Already done outside Spaarke", "Not mine", "No longer needed", "Duplicate", "Other");

        decide.Concat(doLane).Where(r => !r.CountsTowardSuppression).Select(r => r.Label)
            .Should().Equal("Wrong matter - misresolved");
        decide.Concat(doLane).Where(r => r.RequiresDetail).Select(r => r.Label).Should().Equal("Other", "Other");
    }

    // =================================================================================================
    // Plan resolution: fail closed
    // =================================================================================================

    [Theory]
    [InlineData("""{"actions":["send-budget-inquiry","not-a-real-action"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.UnknownAction)]
    [InlineData("""{"actions":["send-budget-inquiry"],"nextSteps":["escalate"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.UnknownAction)]
    [InlineData("""{"actions":["Send-Budget-Inquiry"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.UnknownAction)]
    [InlineData("""{"actions":["mark-complete"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["assign-work"]}""", DecisionLane.Do, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["assign-task"]}""", DecisionLane.Do, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["send-budget-inquiry"],"nextSteps":["assign-task"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["add-todo"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["send-budget-inquiry"],"nextSteps":["revise-budget"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["revise-budget","revise-budget"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.DuplicateAction)]
    // Task 161 (D-180): create-budget is never authored; a stored plan naming it, as an action or a Next step, is refused whole.
    [InlineData("""{"actions":["create-budget"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["send-budget-inquiry","create-budget","approve-variance"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["send-budget-inquiry"],"nextSteps":["create-budget"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["assign-work"],"nextSteps":["assign-work"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.DuplicateAction)]
    [InlineData("""{"actions":[]}""", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMissing)]
    [InlineData("", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMissing)]
    [InlineData(null, DecisionLane.Decide, DecisionPlanRefusalReason.PlanMissing)]
    [InlineData("""{"nextSteps":[]}""", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMalformed)]
    [InlineData("""{"actions":"send-budget-inquiry"}""", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMalformed)]
    [InlineData("""{"actions":[1]}""", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMalformed)]
    [InlineData("[]", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMalformed)]
    [InlineData("{not json", DecisionLane.Decide, DecisionPlanRefusalReason.PlanMalformed)]
    public void APlanThatDoesNotResolve_IsRefusedWholeWithABoundedReason(string? plan, DecisionLane lane, string expectedReason)
    {
        var resolution = DecisionPlanService.Resolve(plan, lane);

        resolution.IsRefused.Should().BeTrue();
        resolution.Plan.Should().BeNull();
        resolution.RefusalReason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task AnUnknownCodeInAStoredPlan_IsLoggedWithAStableEventId_AndMetered_AndTheLogNamesNeitherPlanNorCode()
    {
        var logger = new CapturingLogger<DecisionPlanService>();
        var service = new DecisionPlanService(EntitiesReturningPlan("""{"actions":["secret-unknown-code"]}"""), logger);
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenRefusals(scope);
        using var _ = listener;

        var resolution = await service.GetAsync(VersionId, DecisionLane.Decide, CancellationToken.None);

        resolution.IsRefused.Should().BeTrue();
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(OntologyWriterEvents.DecisionPlanRefused.Id);
        entry.Field("Reason").Should().Be(DecisionPlanRefusalReason.UnknownAction);
        entry.Message.Should().NotContain("secret-unknown-code");
        reasons().Should().Equal(DecisionPlanRefusalReason.UnknownAction);
    }

    [Fact]
    public async Task AResolvedPlan_IsNeitherLoggedNorMetered()
    {
        var logger = new CapturingLogger<DecisionPlanService>();
        var service = new DecisionPlanService(EntitiesReturningPlan(SeededPlan), logger);
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenRefusals(scope);
        using var _ = listener;

        var resolution = await service.GetAsync(VersionId, DecisionLane.Decide, CancellationToken.None);

        resolution.IsRefused.Should().BeFalse();
        logger.Entries.Should().BeEmpty();
        reasons().Should().BeEmpty();
    }

    // =================================================================================================
    // The Signal access decision (the one place; task 038 reuses it)
    // =================================================================================================

    [Fact]
    public async Task AReaderOfTheSignalAndItsCoreRecord_IsAllowed_AndLearnsLaneAndVersion()
    {
        var access = Access(Users(signal: SignalRow(), core: Ok(new { createdon = "2026-10-01T00:00:00Z" })));

        var decision = await access.AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Outcome.Should().Be(SignalAccessOutcome.Allowed);
        decision.Lane.Should().Be(DecisionLane.Decide);
        decision.PolicyVersionId.Should().Be(VersionId);
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_matters")]
    [InlineData("sprk_project", "sprk_projects")]
    [InlineData("sprk_workassignment", "sprk_workassignments")]
    [InlineData("sprk_servicerequest", "sprk_servicerequests")]
    public async Task EveryCoreType_IsHandledByTheSameCode_BecauseTheTypeComesFromTheCatalogRow_D36(string table, string set)
    {
        var users = Users(signal: SignalRow(), core: Ok(new { createdon = "x" }));
        var access = Access(users, coreTable: table, coreSet: set);

        var decision = await access.AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Outcome.Should().Be(SignalAccessOutcome.Allowed);
        Mock.Get(users).Verify(u => u.GetAsync($"{set}({CoreId})?$select=createdon", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TheAcceptedCoreTypes_AreExactlyUacR2sCoreSet_NotACopy()
    {
        // The route accepts what CoreAncestorResolver.CoreRecordEntities lists; this pins that the test above covers all of it.
        CoreAncestorResolver.CoreRecordEntities.Should().BeEquivalentTo(
            "sprk_matter", "sprk_project", "sprk_workassignment", "sprk_servicerequest");
    }

    [Theory]
    [InlineData("account")]
    [InlineData("sprk_document")]
    [InlineData("sprk_signal")]
    public async Task ASignalWhoseCoreRecordPointsAtANonCoreTable_IsDenied_AndThatTableIsNeverRead_K2(string table)
    {
        var users = Users(signal: SignalRow(), core: Ok(new { createdon = "x" }));
        var access = Access(users, coreTable: table, coreSet: table + "s");

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
        Mock.Get(users).Verify(u => u.GetAsync(It.Is<string>(p => p.Contains($"({CoreId})", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ACallerWhoCannotReadTheCoreRecord_IsTheUniformNotFound()
    {
        var access = Access(Users(signal: SignalRow(), core: Fail(404, DataverseUserClientErrorCodes.NotFound)));

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData(404, DataverseUserClientErrorCodes.NotFound)]
    [InlineData(429, DataverseUserClientErrorCodes.RateLimited)]
    [InlineData(500, "DATAVERSE_UNAVAILABLE")]
    public async Task ASignalTheCallerCannotReadOrCouldNotBeRead_IsTheUniformNotFound_AndTheCoreRecordIsNeverAsked(int status, string code)
    {
        var users = Users(signal: Fail(status, code), core: Ok(new { createdon = "x" }));
        var access = Access(users);

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
        Mock.Get(users).Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_matters(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADeniedRead_ForACallerDataverseKnows_IsTheUniformNotFound()
    {
        var users = Users(signal: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Ok(new { UserId = OwnerId }));

        (await Access(users).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task ADeniedRead_ForAValidTokenWithNoDataverseUser_IsTheSingleCallerUnresolved_D29()
    {
        var users = Users(signal: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Fail(403, DataverseUserClientErrorCodes.AccessDenied));

        (await Access(users).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
    }

    [Theory]
    [InlineData(429, DataverseUserClientErrorCodes.RateLimited)]
    [InlineData(500, DataverseUserClientErrorCodes.ServiceError)]
    public async Task ADeniedRead_WhenTheIdentityCheckIsThrottledOrFaults_IsTheUniformNotFound_NotACallerUnresolved(int status, string code)
    {
        var users = Users(signal: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Fail(status, code));

        (await Access(users).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task ADeniedRead_WhenTheIdentityCheckHasATransientOboFailure_IsStillCallerUnresolved()
    {
        // The existing user-context / OBO codes keep their meaning: the caller cannot be identified.
        var users = Users(signal: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Fail(0, DataverseUserClientErrorCodes.OboExchangeFailed));

        (await Access(users).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
    }

    [Fact]
    public async Task ADeniedCoreRecordRead_ForAValidTokenWithNoDataverseUser_IsCallerUnresolved_AndANormalDenialIsNot()
    {
        var unknown = Users(signal: SignalRow(), core: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Fail(403, DataverseUserClientErrorCodes.AccessDenied));
        var known = Users(signal: SignalRow(), core: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Ok(new { UserId = OwnerId }));

        (await Access(unknown).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
        (await Access(known).AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData(DataverseUserClientErrorCodes.UserContextRequired)]
    [InlineData(DataverseUserClientErrorCodes.OboExchangeFailed)]
    public async Task AnUnresolvedCaller_IsTheSingleCallerUnresolved(string code)
    {
        var access = Access(Users(signal: Fail(0, code), core: Ok(new { createdon = "x" })));

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
    }

    [Theory]
    [InlineData(true, false)]   // type but no id
    [InlineData(false, true)]   // id but no type
    public async Task AHalfCoreRecordPair_IsDenied_NotTreatedAsNoCoreRecord(bool hasType, bool hasId)
    {
        var access = Access(Users(signal: SignalRow(withType: hasType, withId: hasId), core: Ok(new { createdon = "x" })));

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task ACoreRecordTypeTheCatalogCannotResolve_IsDenied()
    {
        var access = Access(Users(signal: SignalRow(), core: Ok(new { createdon = "x" })), coreTable: null);

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    // ---- No-core Do item (D-35 as amended by D-129): visible to whoever can read the SUBJECT row as themselves ------------------

    private static readonly Guid SubjectId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static DataverseUserResponse NoCoreDoRow(string? subjectType = "sprk_todo", Guid? subjectId = null) =>
        SignalRow(withType: false, withId: false, lane: DecisionLane.Do, subjectType: subjectType, subjectId: subjectId ?? SubjectId);

    [Fact]
    public async Task ANoCoreDoItem_IsAllowed_WhenTheCallerCanReadItsSubject_EvenWhenNobodyIsItsOwner_D129()
    {
        // The team-owned item: the caller is a team member, not the Signal's "owner" (the old equality test matched nobody).
        var users = Users(signal: NoCoreDoRow(), core: Ok(new { createdon = "x" }), who: Ok(new { UserId = Guid.NewGuid() }));

        var decision = await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None);

        decision.Outcome.Should().Be(SignalAccessOutcome.Allowed);
        decision.Lane.Should().Be(DecisionLane.Do);
        decision.CoreId.Should().BeNull("a no-core Signal has no core pair");
        Mock.Get(users).Verify(u => u.GetAsync($"sprk_todos({SubjectId})?$select=createdon", It.IsAny<CancellationToken>()), Times.Once,
            "the subject row is read AS THE CALLER");
        Mock.Get(users).Verify(u => u.GetAsync("WhoAmI", It.IsAny<CancellationToken>()), Times.Never, "owner equality is gone");
    }

    [Theory]
    [InlineData(404, DataverseUserClientErrorCodes.NotFound)]
    [InlineData(403, DataverseUserClientErrorCodes.AccessDenied)]
    public async Task ANoCoreDoItem_IsTheUniformNotFound_WhenTheCallerCannotReadItsSubject_EvenForTheSignalsOwner_D123(int status, string code)
    {
        // Includes a subject in a secure record family: a non-member's read of it is denied, so the Signal and its Decision Record
        // are part of that family's access and invisible to everyone else.
        var users = Users(signal: NoCoreDoRow(), core: Fail(status, code), who: Ok(new { UserId = OwnerId }));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome
            .Should().Be(SignalAccessOutcome.NotFound, "the caller IS the Signal's ownerid, which no longer decides anything");
    }

    [Fact]
    public async Task ANoCoreDoItem_WhoseSignalTheCallerCannotRead_IsTheUniformNotFound_AndTheSubjectIsNeverAsked()
    {
        var users = Users(signal: Fail(404, DataverseUserClientErrorCodes.NotFound), core: Ok(new { createdon = "x" }));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
        Mock.Get(users).Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Never,
            "BOTH reads must succeed: the Signal is read first");
    }

    [Fact]
    public async Task ANoCoreDecideItem_IsDenied_EvenWhenTheCallerCanReadItsSubject_D35()
    {
        var row = SignalRow(withType: false, withId: false, lane: DecisionLane.Decide, subjectType: "sprk_todo", subjectId: SubjectId);
        var users = Users(signal: row, core: Ok(new { createdon = "x" }), who: Ok(new { UserId = OwnerId }));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
        Mock.Get(users).Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Task 141 (review F2): since the subject comes from the lookup that carries the pair id, all three cases take the SAME path:
    // a pair id with no subject lookup carrying it. sprk_signal and systemuser have no sprk_signal subject lookup, so the row
    // cannot express them; they no longer reach the ownership-parent (K2) check.
    [Theory]
    [InlineData(null)]               // no subject type written
    [InlineData("sprk_signal")]      // a table with no sprk_signal subject lookup: not expressible on the row
    [InlineData("systemuser")]       // likewise
    public async Task ANoCoreDoItem_WhosePairIdNoSubjectLookupCarries_IsTheUniformNotFound_AndNothingIsRead(string? subjectType)
    {
        var users = Users(signal: NoCoreDoRow(subjectType), core: Ok(new { createdon = "x" }), who: Ok(new { UserId = OwnerId }));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
        Mock.Get(users).Verify(u => u.GetAsync(It.Is<string>(p => !p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static IEnumerable<object[]> EverySignalSubjectTable() =>
        SignalWriter.RegardingLookupByEntity.Keys.Select(k => new object[] { k });

    // Task 141 fix round 2 (K4b-a). The three theory cases above converged on "no subject lookup carries the pair id", so none of
    // them reaches the ownership-parent test in AuthorizeNoCoreRecordAsync with a REAL, derivable subject any more. This one does:
    // every table a Signal can name as its subject is derivable, passes that test, and goes on to the caller's own read.
    [Theory]
    [MemberData(nameof(EverySignalSubjectTable))]
    public async Task ANoCoreDoItem_WithAnyDerivableSubject_ReachesTheCallersOwnRead(string subjectTable)
    {
        var users = Users(signal: NoCoreDoRow(subjectTable), core: Ok(new { createdon = "x" }), who: Ok(new { UserId = OwnerId }));

        var decision = await Access(users, coreSet: "subject_set").AuthorizeAsync(SignalId, CancellationToken.None);

        // Task 150 (D-141): a Do item is then visible only when its subject table has a worklist "mine" rule (the worklist shows
        // a Do row on any other table to nobody), and the caller's "mine" read returns it.
        decision.Outcome.Should().Be(SignalReadService.DoLaneMineRules.ContainsKey(subjectTable) ? SignalAccessOutcome.Allowed : SignalAccessOutcome.NotFound);
        Mock.Get(users).Verify(u => u.GetAsync($"subject_set({SubjectId})?$select=createdon", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void EveryTableASignalCanNameAsItsSubject_IsAnOwnershipParent_SoTheK2SubjectTestHasNoLiveFalseBranchYet()
    {
        // SignalCoreRecordAccess' `!IsOwnershipParent(subjectType)` refusal is defence in depth: the 9 subject lookups on
        // sprk_signal are all ownership parents today, so no derivable subject can reach it. If a lookup is added for a table that
        // is not a parent, this fails: add the case that reaches that refusal (a derivable non-parent subject is the uniform
        // NotFound and nothing is read) and keep this test honest.
        SignalWriter.RegardingLookupByEntity.Keys
            .Where(k => !RecordOwnershipResolver.IsOwnershipParent(k))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ANoCoreDoItem_WithNoSubjectId_IsTheUniformNotFound()
    {
        var row = SignalRow(withType: false, withId: false, lane: DecisionLane.Do, subjectType: "sprk_todo", subjectId: null);

        (await Access(Users(signal: row, core: Ok(new { createdon = "x" })), coreSet: "sprk_todos")
            .AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Theory]
    [InlineData(429, DataverseUserClientErrorCodes.RateLimited)]
    [InlineData(500, DataverseUserClientErrorCodes.ServiceError)]
    public async Task ANoCoreDoItem_WhoseSubjectReadFaults_IsUnavailable_NotTheUniformNotFound_F4b(int status, string code)
    {
        var users = Users(signal: NoCoreDoRow(), core: Fail(status, code));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Unavailable);
    }

    [Fact]
    public async Task ANoCoreDoItem_WhoseSubjectEntitySetLookupFaults_IsUnavailable_F4b()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("no answer"));
        var access = new SignalCoreRecordAccess(Users(signal: NoCoreDoRow(), core: Ok(new { createdon = "x" })), entities.Object, CallerWithAContact(), NullLogger<SignalCoreRecordAccess>.Instance);

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Unavailable);
    }

    [Fact]
    public async Task ANoCoreDoItem_WhoseSubjectReadThrows_IsUnavailable_NotTheUniformNotFound_F4b()
    {
        var users = Users(signal: NoCoreDoRow(), core: Ok(new { createdon = "x" }));
        Mock.Get(users).Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_todos(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Unavailable);
    }

    [Fact]
    public async Task ANoCoreDoItem_WhoseCallerCannotBeResolved_IsCallerUnresolved()
    {
        var users = Users(signal: NoCoreDoRow(), core: Fail(0, DataverseUserClientErrorCodes.UserContextRequired));

        (await Access(users, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
    }

    [Fact]
    public async Task ANoCoreDoItem_WhoseSubjectIsDeniedToAValidTokenWithNoDataverseUser_IsCallerUnresolved_AndAKnownCallerIsNot()
    {
        var unknown = Users(signal: NoCoreDoRow(), core: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Fail(403, DataverseUserClientErrorCodes.AccessDenied));
        var known = Users(signal: NoCoreDoRow(), core: Fail(403, DataverseUserClientErrorCodes.AccessDenied), who: Ok(new { UserId = OwnerId }));

        (await Access(unknown, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
        (await Access(known, coreSet: "sprk_todos").AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task TheRoute_Returns503_WhenANoCoreSubjectReadFaults_Not404()
    {
        var access = Access(Users(signal: NoCoreDoRow(), core: Fail(500, DataverseUserClientErrorCodes.ServiceError)), coreSet: "sprk_todos");
        var plans = new DecisionPlanService(EntitiesReturningPlan(SeededPlan), NullLogger<DecisionPlanService>.Instance);

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, new DefaultHttpContext(), access, plans, NoRuleDescriber(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(503);
    }

    // =================================================================================================
    // The route's result mapping: 200 / 404 / 403 / 422
    // =================================================================================================

    [Fact]
    public async Task TheRoute_Returns200WithThePlan_OnlyForAnAllowedCaller()
    {
        var access = Access(Users(signal: SignalRow(), core: Ok(new { createdon = "x" })));
        var plans = new DecisionPlanService(EntitiesReturningPlan(SeededPlan), new CapturingLogger<DecisionPlanService>());

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, new DefaultHttpContext(), access, plans, NoRuleDescriber(), CancellationToken.None);

        var body = result.Should().BeOfType<Ok<DecisionPlanResponse>>().Subject.Value!;
        body.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
        body.Actions.Select(a => a.IsRecommended).Should().Equal(true, false, false);
        body.Actions.Should().OnlyContain(a => a.RecordClass == "Judgement");
        body.Actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget", "create-budget");
        body.DismissalReasons.Should().HaveCount(6);
        body.RuleDescription.Should().BeNull("a version with no describable rule body is served without a description, never a guess");
    }

    [Fact]
    public async Task TheRoute_StillReturns200_WithANullRuleDescription_WhenTheNameReadFaults()
    {
        var access = Access(Users(signal: SignalRow(), core: Ok(new { createdon = "x" })));
        var entities = EntitiesReturningPlan(SeededPlan, ruleBody: PathBBody());
        Mock.Get(entities).Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse is down"));
        var plans = new DecisionPlanService(entities, NullLogger<DecisionPlanService>.Instance);
        var describer = new RuleBodyDescriber(Validator(), entities, NullLogger<RuleBodyDescriber>.Instance);

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, new DefaultHttpContext(), access, plans, describer, CancellationToken.None);

        var body = result.Should().BeOfType<Ok<DecisionPlanResponse>>().Subject.Value!;
        body.RuleDescription.Should().BeNull();
        body.Actions.Should().HaveCount(3);
    }

    private static string PathBBody()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "signals", "pathb-existence.rulebody.json"));
    }

    [Fact]
    public async Task TheRoute_Returns404_WhenTheCallerCannotReadTheMatter_AndNeverReadsThePlan()
    {
        var access = Access(Users(signal: SignalRow(), core: Fail(404, DataverseUserClientErrorCodes.NotFound)));
        var entities = EntitiesReturningPlan(SeededPlan);
        var plans = new DecisionPlanService(entities, NullLogger<DecisionPlanService>.Instance);

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, new DefaultHttpContext(), access, plans, NoRuleDescriber(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(404);
        Mock.Get(entities).Verify(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheRoute_Returns403_ForAnUnresolvedCaller()
    {
        var access = Access(Users(signal: Fail(0, DataverseUserClientErrorCodes.UserContextRequired), core: Ok(new { createdon = "x" })));
        var plans = new DecisionPlanService(EntitiesReturningPlan(SeededPlan), NullLogger<DecisionPlanService>.Instance);

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, new DefaultHttpContext(), access, plans, NoRuleDescriber(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task TheRoute_Returns422_ForAPlanThatDoesNotResolve_NeverAPartialPlan()
    {
        var access = Access(Users(signal: SignalRow(), core: Ok(new { createdon = "x" })));
        var plans = new DecisionPlanService(EntitiesReturningPlan("""{"actions":["send-budget-inquiry","bogus"]}"""), NullLogger<DecisionPlanService>.Instance);

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(SignalId, new DefaultHttpContext(), access, plans, NoRuleDescriber(), CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(422);
    }

    // =================================================================================================
    // Create budget (task 161, D-180): revise-budget on a matter with no active budget
    // =================================================================================================

    [Fact]
    public void CreateBudget_IsReviseBudgetsTwin_WithTheBudgetFieldsAddedByD193_AndExcludesApproveVariance_D180()
    {
        DecisionActionCatalog.TryGet("create-budget", out var create).Should().BeTrue();
        DecisionActionCatalog.TryGet("revise-budget", out var revise).Should().BeTrue();

        create.Label.Should().Be("Create budget");
        create.PlanLane.Should().Be(revise.PlanLane);
        create.WorkType.Should().Be(revise.WorkType);
        create.RecordClass.Should().Be(revise.RecordClass);
        create.ResolvesSignal.Should().Be(revise.ResolvesSignal);
        create.Gate.Should().Be(revise.Gate);
        create.IsNextStep.Should().BeFalse();
        create.SubstitutionOnly.Should().BeTrue();
        revise.SubstitutionOnly.Should().BeFalse("must-not-fire: only create-budget is substitution-only");
        // Task 167 (D-193): the budget step collects Category, Year, Start date, End date, Period and Currency; only the amount and the
        // reason are required (the reason still goes to the Decision Record only).
        create.Parameters.Select(p => (p.Code, p.Kind, p.Required)).Should().Equal(
            ("amount", DecisionParameterKind.Money, true),
            ("category", DecisionParameterKind.Choice, false),
            ("year", DecisionParameterKind.Text, false),
            ("startDate", DecisionParameterKind.Date, false),
            ("endDate", DecisionParameterKind.Date, false),
            ("period", DecisionParameterKind.Choice, false),
            ("currency", DecisionParameterKind.Choice, false),
            ("reason", DecisionParameterKind.TextArea, true));
        create.Excludes.Should().Equal("approve-variance");
        DecisionActionCatalog.FirstConflict(["create-budget", "approve-variance"]).Should().NotBeNull("the exclusion is mutual");
        DecisionActionCatalog.FirstConflict(["approve-variance", "create-budget"]).Should().NotBeNull();
        DecisionActionCatalog.All.Where(a => a.SubstitutionOnly).Select(a => a.Code).Should().Equal("create-budget");
    }

    [Fact]
    public void CreateBudget_TheChoiceFieldsReadLiveOptions_NeverAFixedList_AndTheCurrencyDefaultsFromTheBusinessUnit_D193()
    {
        DecisionActionCatalog.TryGet("create-budget", out var create).Should().BeTrue();
        Param(create, "category").OptionsSource.Should().Be("metadata:sprk_budget.sprk_budgetcategory");
        Param(create, "period").OptionsSource.Should().Be("metadata:sprk_budget.sprk_budgetperiod");
        Param(create, "currency").OptionsSource.Should().Be(DecisionParameterSources.ActiveCurrencies);
        Param(create, "currency").DefaultSource.Should().Be(DecisionParameterSources.MatterCurrencyDefault);
        Param(create, "endDate").NotBefore.Should().Be("startDate");
        create.Parameters.Where(p => p.Kind == DecisionParameterKind.Choice).Should().OnlyContain(p => p.Options == null && p.OptionsSource != null,
            "a relabel by the owner must show up: nothing is hard-coded (D-193)");
        create.Parameters.Where(p => p.Code is not ("currency" or "endDate")).Should().OnlyContain(p => p.DefaultSource == null && p.NotBefore == null);
    }

    private static DecisionActionParameter Param(DecisionActionDefinition action, string code) => action.Parameters.Single(p => p.Code == code);

    [Fact]
    public void NoOtherActionDeclaresADefaultSourceOrANotBefore_MustNotFire_D193()
    {
        DecisionActionCatalog.All.Where(a => a.Code != "create-budget")
            .SelectMany(a => a.Parameters)
            .Should().OnlyContain(p => p.DefaultSource == null && p.NotBefore == null);
    }

    [Fact]
    public void TheAuthoringCatalog_NeverListsCreateBudget_NorNamesItAsAnExclusion_D180()
    {
        var decide = OntologyAdminAuthoringService.LaneCatalog(DecisionLane.Decide);

        decide.Actions.Select(a => a.Code).Should().NotContain("create-budget");
        decide.NextSteps.Select(a => a.Code).Should().NotContain("create-budget");
        decide.Actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget");
        decide.Actions.Single(a => a.Code == "revise-budget").Excludes.Should().Equal("approve-variance");
    }

    [Fact]
    public void WithCreateBudgetInsteadOfRevise_ReplacesInPlace_KeepingThePositionAndEveryOtherAction_D180()
    {
        var plan = DecisionPlanService.Resolve(SeededPlan, DecisionLane.Decide).Plan!;

        var substituted = plan.WithCreateBudgetInsteadOfRevise();

        substituted.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "create-budget", "approve-variance");
        substituted.NextSteps.Should().BeSameAs(plan.NextSteps);
        substituted.DismissalReasons.Should().BeSameAs(plan.DismissalReasons);
        plan.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance"); // the original is not changed

        var first = DecisionPlanService.Resolve("""{"actions":["revise-budget","approve-variance"]}""", DecisionLane.Decide).Plan!;
        // The recommended action is the first one, so a substituted first action stays the recommendation.
        first.WithCreateBudgetInsteadOfRevise().Actions.Select(a => a.Code).Should().Equal("create-budget", "approve-variance");

        var without = DecisionPlanService.Resolve("""{"actions":["send-budget-inquiry"]}""", DecisionLane.Decide).Plan!;
        without.WithCreateBudgetInsteadOfRevise().Should().BeSameAs(without, "a plan with no Revise budget is returned as it is");
    }

    [Fact]
    public void WithCreateBudgetInsteadOfRevise_LeavesThePolMatterNobudgetV2Plan_AssignTaskPlusNextSteps_Untouched_D178_D180()
    {
        // 159 x 161: the v2 plan of POL-MATTER-NOBUDGET offers assign-task, not revise-budget, so the no-budget substitution never applies.
        var v2 = DecisionPlanService.Resolve("""{"actions":["assign-task"],"nextSteps":["add-todo","assign-work"]}""", DecisionLane.Decide).Plan!;

        v2.WithCreateBudgetInsteadOfRevise().Should().BeSameAs(v2);
        v2.Actions.Select(a => a.Code).Should().Equal("assign-task");
        v2.NextSteps.Select(a => a.Code).Should().Equal("add-todo", "assign-work");
    }

    private const string PolMatterNobudgetV3Plan = """{"actions":["revise-budget","assign-task"],"nextSteps":["add-todo","assign-work"]}""";

    [Fact]
    public void WithCreateBudgetInsteadOfRevise_TurnsThePolMatterNobudgetV3Plan_IntoCreateBudgetThenAssignTask_KeepingTheNextSteps_D192()
    {
        // 166 (D-192) x 161 (D-180): the v3 plan names revise-budget first, so a matter with no active budget is offered Create budget first.
        var v3 = DecisionPlanService.Resolve(PolMatterNobudgetV3Plan, DecisionLane.Decide).Plan!;
        v3.Actions.Select(a => a.Code).Should().Equal("revise-budget", "assign-task");

        var substituted = v3.WithCreateBudgetInsteadOfRevise();

        substituted.Actions.Select(a => a.Code).Should().Equal("create-budget", "assign-task");
        substituted.NextSteps.Select(a => a.Code).Should().Equal("add-todo", "assign-work");
        v3.Actions.Select(a => a.Code).Should().Equal("revise-budget", "assign-task"); // the original is not changed
    }

    private const string ReviseThenVariance = """{"actions":["send-budget-inquiry","revise-budget","approve-variance"],"nextSteps":[]}""";

    /// <summary>The caller's reads: the Signal, the matter (readable or not), and the caller's OWN read of <c>sprk_budgets</c>
    /// (<paramref name="callerBudgets"/>, which a Deep Read can leave blind to a parent-BU budget; the routes must not decide on it).</summary>
    private static (IDataverseUserClient Users, Mock<IDataverseUserClient> Mock) UsersWithBudgets(
        DataverseUserResponse? callerBudgets = null, bool matterReadable = true)
    {
        var users = new Mock<IDataverseUserClient>();
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>())).ReturnsAsync(SignalRow());
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_matters(", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matterReadable ? Ok(new { createdon = "x" }) : Fail(404, DataverseUserClientErrorCodes.NotFound));
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_projects(", StringComparison.Ordinal)), It.IsAny<CancellationToken>())).ReturnsAsync(Ok(new { createdon = "x" }));
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_budgets?", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerBudgets ?? Budgets(0));
        return (users.Object, users);
    }

    private static DataverseUserResponse Budgets(int count) =>
        Ok(new { value = Enumerable.Range(0, count).Select(_ => new { sprk_budgetid = Guid.NewGuid() }).ToArray() });

    /// <summary>Task 161 fix round 1 (F1-a): the BFF's OWN (app-only) read of the matter's active budgets, which sees every budget.
    /// <paramref name="count"/> rows, or a throw.</summary>
    private sealed class AppBudgets
    {
        public AppBudgets(int count = 0, Exception? throws = null)
        {
            Mock.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) =>
                {
                    Queries.Add(q);
                    if (throws is not null) throw throws;
                    return Task.FromResult(new EntityCollection(Enumerable.Range(0, count).Select(_ => new Entity("sprk_budget", Guid.NewGuid())).ToList()));
                });
        }

        public Mock<IGenericEntityService> Mock { get; } = new();

        public List<QueryExpression> Queries { get; } = new();
    }

    private static async Task<DecisionPlanResponse> PlanRouteAsync(
        SignalCoreRecordAccess access, string plan = ReviseThenVariance, DecisionParameterSources? sources = null)
    {
        var plans = new DecisionPlanService(EntitiesReturningPlan(plan), NullLogger<DecisionPlanService>.Instance);
        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(
            SignalId, new DefaultHttpContext(), access, plans, NoRuleDescriber(), CancellationToken.None, parameterSources: sources);
        return result.Should().BeOfType<Ok<DecisionPlanResponse>>().Subject.Value!;
    }

    private static Mock<DecisionParameterSources> PlanSources(DecisionParameterFacts facts)
    {
        var sources = new Mock<DecisionParameterSources>(
            new Mock<IDataverseUserClient>().Object, new Mock<IGenericEntityService>().Object, new Mock<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>().Object,
            NullLogger<DecisionParameterSources>.Instance)
        { CallBase = false };
        sources.Setup(s => s.ForPlanAsync(It.IsAny<HttpContext>(), It.IsAny<ResolvedDecisionPlan>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(facts);
        return sources;
    }

    private static void CallerBudgetsReadTimes(Mock<IDataverseUserClient> mock, Times times) =>
        mock.Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_budgets?", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task ThePlanRoute_OffersCreateBudget_InPlaceOfReviseBudget_WhenTheMatterHasNoActiveBudget_D180()
    {
        var (users, mock) = UsersWithBudgets();
        var app = new AppBudgets(0);

        var body = await PlanRouteAsync(Access(users, budgets: app));

        body.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "create-budget", "approve-variance");
        body.Actions.Select(a => a.IsRecommended).Should().Equal(true, false, false);
        var create = body.Actions.Single(a => a.Code == "create-budget");
        create.Label.Should().Be("Create budget");
        create.Parameters.Select(p => p.Code).Should().Equal("amount", "category", "year", "startDate", "endDate", "period", "currency", "reason");
        create.Excludes.Should().Equal("approve-variance");
        var query = app.Queries.Should().ContainSingle("one read, the BFF's own (F1-a), of the ACTIVE budgets of the core matter").Subject;
        query.EntityName.Should().Be("sprk_budget");
        query.TopCount.Should().Be(1);
        query.Criteria.Conditions.Select(c => (c.AttributeName, c.Values.Single())).Should()
            .BeEquivalentTo(new[] { ("sprk_matter", (object)CoreId), ("statecode", (object)0) });
        CallerBudgetsReadTimes(mock, Times.Never());
    }

    [Fact]
    public async Task ThePlanRoute_CreateBudget_CarriesTheLiveOptions_TheCurrencyDefault_AndTheDateOrder_D193()
    {
        var (users, _) = UsersWithBudgets();
        var euro = Guid.Parse("cccccccc-0000-0000-0000-0000000000e1");
        var facts = new DecisionParameterFacts(
            new Dictionary<string, IReadOnlyList<DecisionParameterOption>>
            {
                ["metadata:sprk_budget.sprk_budgetcategory"] = [new("100000000", "Fees")],
                ["metadata:sprk_budget.sprk_budgetperiod"] = [new("0", "Annual"), new("1", "Quarter 1")],
                [DecisionParameterSources.ActiveCurrencies] = [new(euro.ToString("D"), "Euro (EUR)")],
            },
            new Dictionary<string, string> { [DecisionParameterSources.MatterCurrencyDefault] = euro.ToString("D") });
        var sources = PlanSources(facts);

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(0)), sources: sources.Object);

        var create = body.Actions.Single(a => a.Code == "create-budget");
        create.Parameters.Single(p => p.Code == "category").Options!.Select(o => (o.Value, o.Label)).Should().Equal(("100000000", "Fees"));
        create.Parameters.Single(p => p.Code == "period").Options!.Select(o => o.Label).Should().Equal("Annual", "Quarter 1");
        var currency = create.Parameters.Single(p => p.Code == "currency");
        currency.Options!.Select(o => o.Label).Should().Equal("Euro (EUR)");
        currency.DefaultValue.Should().Be(euro.ToString("D"));
        create.Parameters.Single(p => p.Code == "endDate").NotBefore.Should().Be("startDate");
        sources.Verify(s => s.ForPlanAsync(It.IsAny<HttpContext>(),
            It.Is<ResolvedDecisionPlan>(p => p.Actions.Any(a => a.Code == "create-budget")), CoreId, It.IsAny<CancellationToken>()), Times.Once,
            "the options and the default are read for the plan the caller will see, for the core matter");
        body.Actions.Where(a => a.Code != "create-budget").SelectMany(a => a.Parameters)
            .Should().OnlyContain(p => p.DefaultValue == null && p.NotBefore == null, "must-not-fire: every other action's parameters are unchanged");
    }

    [Fact]
    public async Task ThePlanRoute_CreateBudget_WithoutTheResolver_OffersEmptyChoicesAndNoDefault_AndDoesNotFail_D193()
    {
        var (users, _) = UsersWithBudgets();

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(0)));

        var create = body.Actions.Single(a => a.Code == "create-budget");
        create.Parameters.Single(p => p.Code == "category").Options.Should().BeNull();
        create.Parameters.Single(p => p.Code == "currency").DefaultValue.Should().BeNull();
    }

    [Fact]
    public async Task ThePlanRoute_ReviseBudget_DoesNotAskTheResolverForAnythingItCarries_MustNotFire_D193()
    {
        var (users, _) = UsersWithBudgets();
        var sources = PlanSources(DecisionParameterFacts.None);

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(1)), sources: sources.Object);

        body.Actions.Single(a => a.Code == "revise-budget").Parameters.Select(p => p.Code).Should().Equal("budget", "amount", "reason");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ThePlanRoute_StillOffersReviseBudget_WhenTheMatterHasAnActiveBudget_MustNotFire_D180(int budgets)
    {
        var (users, _) = UsersWithBudgets();

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(budgets)));

        body.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
    }

    [Fact]
    public async Task ThePlanRoute_PolMatterNobudgetV3_OffersCreateBudgetThenAssignTask_WhenTheMatterHasNoActiveBudget_D192()
    {
        var (users, _) = UsersWithBudgets();

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(0)), PolMatterNobudgetV3Plan);

        body.Actions.Select(a => a.Code).Should().Equal("create-budget", "assign-task");
        body.Actions.Single(a => a.Code == "create-budget").Label.Should().Be("Create budget");
        body.NextSteps.Select(a => a.Code).Should().Equal("add-todo", "assign-work");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ThePlanRoute_PolMatterNobudgetV3_StillOffersReviseBudgetThenAssignTask_WhenTheMatterHasAnActiveBudget_MustNotFire_D192(int budgets)
    {
        var (users, _) = UsersWithBudgets();

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(budgets)), PolMatterNobudgetV3Plan);

        body.Actions.Select(a => a.Code).Should().Equal("revise-budget", "assign-task");
        body.Actions.Should().NotContain(a => a.Code == "create-budget");
    }

    [Fact]
    public async Task ThePlanRoute_StillOffersReviseBudget_WhenOnlyTheBffCanSeeTheBudget_TheCallersOwnReadSeesNone_F1a()
    {
        // A Core User whose Read on sprk_budget is Deep cannot see a budget owned in a parent business unit: their own read says 0.
        var (users, mock) = UsersWithBudgets(callerBudgets: Budgets(0));

        var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(1)));

        body.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
        CallerBudgetsReadTimes(mock, Times.Never());
    }

    [Fact]
    public async Task ThePlanRoute_OffersReviseBudget_AndDoesNotFail_WhenTheBudgetsCannotBeCounted_D180()
    {
        foreach (var thrown in new Exception[] { new TimeoutException("no answer"), new InvalidOperationException("Dataverse 503") })
        {
            var (users, _) = UsersWithBudgets();

            var body = await PlanRouteAsync(Access(users, budgets: new AppBudgets(throws: thrown)));

            body.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
        }
    }

    [Fact]
    public async Task ThePlanRoute_ReadsNoBudgets_ForAPlanWithoutReviseBudget_OrACoreThatIsNotAMatter_D180()
    {
        var (withoutRevise, mockA) = UsersWithBudgets();
        var appA = new AppBudgets(0);
        var body = await PlanRouteAsync(Access(withoutRevise, budgets: appA), """{"actions":["send-budget-inquiry","approve-variance"]}""");
        body.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "approve-variance");
        appA.Queries.Should().BeEmpty();
        CallerBudgetsReadTimes(mockA, Times.Never());

        var (project, mockB) = UsersWithBudgets();
        var appB = new AppBudgets(0);
        var projectBody = await PlanRouteAsync(Access(project, coreTable: "sprk_project", coreSet: "sprk_projects", budgets: appB));
        projectBody.Actions.Select(a => a.Code).Should().Equal("send-budget-inquiry", "revise-budget", "approve-variance");
        appB.Queries.Should().BeEmpty();
        CallerBudgetsReadTimes(mockB, Times.Never());
    }

    [Fact]
    public async Task ThePlanRoute_NeverReadsBudgetsApp_Only_ForACallerWhoCannotReadTheMatter_F1a()
    {
        var (users, _) = UsersWithBudgets(matterReadable: false);
        var app = new AppBudgets(0);
        var plans = new DecisionPlanService(EntitiesReturningPlan(ReviseThenVariance), NullLogger<DecisionPlanService>.Instance);

        var result = await DecisionPlanEndpoints.GetDecisionPlanAsync(
            SignalId, new DefaultHttpContext(), Access(users, budgets: app), plans, NoRuleDescriber(), CancellationToken.None);

        result.Should().NotBeOfType<Ok<DecisionPlanResponse>>("the matter is not readable: the uniform 404");
        app.Queries.Should().BeEmpty("never an app-only read for a matter the caller could not read");
    }

    [Fact]
    public async Task TheBudgetCount_IsNone_Some_OrUnknown_AndIsNotCheckedWithoutAnAllowedMatterDecision_F1b()
    {
        var (users, _) = UsersWithBudgets();
        var allowed = await Access(users).AuthorizeAsync(SignalId, CancellationToken.None);
        allowed.Outcome.Should().Be(SignalAccessOutcome.Allowed);

        (await Access(users, budgets: new AppBudgets(0)).CountMatterBudgetsAsync(allowed, CancellationToken.None)).Should().Be(MatterBudgetCount.None);
        (await Access(users, budgets: new AppBudgets(1)).CountMatterBudgetsAsync(allowed, CancellationToken.None)).Should().Be(MatterBudgetCount.Some);
        (await Access(users, budgets: new AppBudgets(throws: new TimeoutException())).CountMatterBudgetsAsync(allowed, CancellationToken.None)).Should().Be(MatterBudgetCount.Unknown);

        var app = new AppBudgets(0);
        (await Access(users, budgets: app).CountMatterBudgetsAsync(SignalAccessDecision.NotFound, CancellationToken.None)).Should().Be(MatterBudgetCount.Unknown);
        app.Queries.Should().BeEmpty();
    }

    // =================================================================================================
    // Fixtures
    // =================================================================================================

    private static SignalCoreRecordAccess Access(
        IDataverseUserClient users, string? coreTable = "sprk_matter", string coreSet = "sprk_matters", AppBudgets? budgets = null)
    {
        var entitiesMock = budgets?.Mock ?? new Mock<IGenericEntityService>();
        var entities = entitiesMock.Object;
        var typeRow = new Entity("sprk_recordtype_ref", CoreTypeId);
        if (coreTable is not null)
        {
            typeRow["sprk_recordlogicalname"] = coreTable;
        }

        entitiesMock.Setup(e => e.RetrieveAsync("sprk_recordtype_ref", CoreTypeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(typeRow);
        entitiesMock.Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(coreSet);
        return new SignalCoreRecordAccess(users, entities, CallerWithAContact(), NullLogger<SignalCoreRecordAccess>.Instance);
    }

    /// <summary>Task 150: the caller's linked contact, which the Do-lane "mine" rule compares (D-141).</summary>
    private static IIdentityNormalizationService CallerWithAContact()
    {
        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PersonIdentity(id, Guid.Parse("66666666-6666-6666-6666-666666666666")));
        return identity.Object;
    }

    private static IDataverseUserClient Users(
        DataverseUserResponse signal, DataverseUserResponse? core = null, DataverseUserResponse? who = null)
    {
        var users = new Mock<IDataverseUserClient>();
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), It.IsAny<CancellationToken>())).ReturnsAsync(signal);
        users.Setup(u => u.GetAsync(It.Is<string>(p => !p.StartsWith("sprk_signals(", StringComparison.Ordinal) && p != "WhoAmI"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(core ?? Fail(404, DataverseUserClientErrorCodes.NotFound));
        users.Setup(u => u.GetAsync("WhoAmI", It.IsAny<CancellationToken>())).ReturnsAsync(who ?? Fail(0, DataverseUserClientErrorCodes.UserContextRequired));

        // Task 150 (D-141): a Do-lane Signal also needs the worklist's "mine" read. Here every subject is the caller's own work
        // (the read returns it); DoLanePrivacyTests covers who is and is not admitted.
        users.Setup(u => u.GetAsync("WhoAmI()", It.IsAny<CancellationToken>())).ReturnsAsync(Ok(new { UserId = OwnerId }));
        users.Setup(u => u.GetAsync(It.Is<string>(p => p.Contains("&$filter=", StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
            {
                var pk = Regex.Match(path, "PropertyName='(\\w+)'").Groups[1].Value;
                var id = Regex.Match(path, "PropertyValues=\\['([0-9a-f-]{36})'").Groups[1].Value;
                return Ok(new { value = new[] { new Dictionary<string, object> { [pk] = id } } });
            });
        return users.Object;
    }

    private static DataverseUserResponse Ok(object body) =>
        DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

    private static DataverseUserResponse Fail(int status, string code) =>
        DataverseUserResponse.Fail(status, code, "test");

    private static DataverseUserResponse SignalRow(
        bool withType = true, bool withId = true, DecisionLane lane = DecisionLane.Decide,
        string? subjectType = null, Guid? subjectId = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["sprk_lane"] = (int)lane,
            ["_sprk_policyversion_value"] = VersionId,
        };
        // Task 141: the subject is the typed subject lookup that carries the pair id. A table a Signal has no lookup for (a forged
        // or unknown pointer) cannot be expressed on the row at all, so it is simply absent.
        if (subjectType is not null && subjectId is { } subjectRow
            && SignalWriter.RegardingLookupByEntity.TryGetValue(subjectType, out var subjectLookup))
        {
            row[$"_{subjectLookup}_value"] = subjectRow.ToString();
        }

        if (subjectId is { } subject)
        {
            row["sprk_regardingrecordid"] = subject.ToString();
        }

        if (withType)
        {
            row["_sprk_corerecordtype_value"] = CoreTypeId;
        }

        if (withId)
        {
            row["sprk_corerecordid"] = CoreId.ToString();
        }

        return Ok(row);
    }

    // The rule body is absent from these fixtures, so the describer refuses; the plan must still be served (task 026 wiring).
    private static RuleBodyDescriber NoRuleDescriber() =>
        new(Validator(), new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance);

    private static PolicyVersionValidator Validator()
    {
        var schema = new RuleBodySchemaValidator();
        return new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
    }

    private static IGenericEntityService EntitiesReturningPlan(string plan, string? ruleBody = null)
    {
        var entitiesMock = new Mock<IGenericEntityService>();
        var version = new Entity("sprk_policyversion", VersionId) { ["sprk_decisionplan"] = plan };
        if (ruleBody is not null)
        {
            version["sprk_rulebody"] = ruleBody;
        }

        entitiesMock.Setup(e => e.RetrieveAsync("sprk_policyversion", VersionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(version);
        return entitiesMock.Object;
    }

    // OntologyWriterTelemetry's Meter is process-global; scope the listener per test like SignalWriterTests does.
    private static readonly AsyncLocal<Guid> MetricScope = new();

    private static (MeterListener Listener, Func<IReadOnlyList<string>> Reasons) ListenRefusals(Guid scope)
    {
        var reasons = new List<string>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.decisionplan.refused")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason" && tag.Value is string reason)
                {
                    lock (reasons)
                    {
                        reasons.Add(reason);
                    }
                }
            }
        });
        listener.Start();
        return (listener, () =>
        {
            lock (reasons)
            {
                return reasons.ToArray();
            }
        });
    }
}
