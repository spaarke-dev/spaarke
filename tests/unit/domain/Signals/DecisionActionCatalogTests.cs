using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
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
        actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget");
        resolution.Plan.NextSteps.Should().BeEmpty();
    }

    [Fact]
    public void SendBudgetInquiry_HasNoReplyDueParameter_D56_D20()
    {
        DecisionActionCatalog.TryGet("send-budget-inquiry", out var inquiry).Should().BeTrue();

        inquiry.Parameters.Select(p => p.Code).Should().Equal("to", "subject", "body");
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
        plan.Actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget");
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
            "send-budget-inquiry", "revise-budget", "approve-variance",
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
    [InlineData("""{"actions":["add-todo"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["send-budget-inquiry"],"nextSteps":["revise-budget"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.ActionNotAllowed)]
    [InlineData("""{"actions":["revise-budget","revise-budget"]}""", DecisionLane.Decide, DecisionPlanRefusalReason.DuplicateAction)]
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
        await users.Received(1).GetAsync($"{set}({CoreId})?$select=createdon", Arg.Any<CancellationToken>());
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
        await users.DidNotReceive().GetAsync(Arg.Is<string>(p => p.Contains($"({CoreId})", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
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
        await users.DidNotReceive().GetAsync(Arg.Is<string>(p => p.StartsWith("sprk_matters(", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task ANoCoreDoItem_IsAllowedToItsOwnerOnly_D35()
    {
        var row = SignalRow(withType: false, withId: false, lane: DecisionLane.Do);
        var owner = Access(Users(signal: row, who: Ok(new { UserId = OwnerId })));
        var other = Access(Users(signal: row, who: Ok(new { UserId = Guid.NewGuid() })));

        (await owner.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.Allowed);
        (await other.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task ANoCoreDecideItem_IsDeniedEvenToItsOwner_D35()
    {
        var row = SignalRow(withType: false, withId: false, lane: DecisionLane.Decide);
        var access = Access(Users(signal: row, who: Ok(new { UserId = OwnerId })));

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.NotFound);
    }

    [Fact]
    public async Task ANoCoreDoItem_WhoseCallerCannotBeResolved_IsCallerUnresolved()
    {
        var row = SignalRow(withType: false, withId: false, lane: DecisionLane.Do);
        var access = Access(Users(signal: row, who: Fail(0, DataverseUserClientErrorCodes.UserContextRequired)));

        (await access.AuthorizeAsync(SignalId, CancellationToken.None)).Outcome.Should().Be(SignalAccessOutcome.CallerUnresolved);
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
        body.Actions.Single(a => a.Code == "approve-variance").Excludes.Should().Equal("revise-budget");
        body.DismissalReasons.Should().HaveCount(6);
        body.RuleDescription.Should().BeNull("a version with no describable rule body is served without a description, never a guess");
    }

    [Fact]
    public async Task TheRoute_StillReturns200_WithANullRuleDescription_WhenTheNameReadFaults()
    {
        var access = Access(Users(signal: SignalRow(), core: Ok(new { createdon = "x" })));
        var entities = EntitiesReturningPlan(SeededPlan, ruleBody: PathBBody());
        entities.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
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
        await entities.DidNotReceive().RetrieveAsync("sprk_policyversion", Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
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
    // Fixtures
    // =================================================================================================

    private static SignalCoreRecordAccess Access(
        IDataverseUserClient users, string? coreTable = "sprk_matter", string coreSet = "sprk_matters")
    {
        var entities = Substitute.For<IGenericEntityService>();
        var typeRow = new Entity("sprk_recordtype_ref", CoreTypeId);
        if (coreTable is not null)
        {
            typeRow["sprk_recordlogicalname"] = coreTable;
        }

        entities.RetrieveAsync("sprk_recordtype_ref", CoreTypeId, Arg.Any<string[]>(), Arg.Any<CancellationToken>()).Returns(typeRow);
        entities.GetEntitySetNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(coreSet);
        return new SignalCoreRecordAccess(users, entities, NullLogger<SignalCoreRecordAccess>.Instance);
    }

    private static IDataverseUserClient Users(
        DataverseUserResponse signal, DataverseUserResponse? core = null, DataverseUserResponse? who = null)
    {
        var users = Substitute.For<IDataverseUserClient>();
        users.GetAsync(Arg.Is<string>(p => p.StartsWith("sprk_signals(", StringComparison.Ordinal)), Arg.Any<CancellationToken>()).Returns(signal);
        users.GetAsync(Arg.Is<string>(p => !p.StartsWith("sprk_signals(", StringComparison.Ordinal) && p != "WhoAmI"), Arg.Any<CancellationToken>())
            .Returns(core ?? Fail(404, DataverseUserClientErrorCodes.NotFound));
        users.GetAsync("WhoAmI", Arg.Any<CancellationToken>()).Returns(who ?? Fail(0, DataverseUserClientErrorCodes.UserContextRequired));
        return users;
    }

    private static DataverseUserResponse Ok(object body) =>
        DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

    private static DataverseUserResponse Fail(int status, string code) =>
        DataverseUserResponse.Fail(status, code, "test");

    private static DataverseUserResponse SignalRow(
        bool withType = true, bool withId = true, DecisionLane lane = DecisionLane.Decide)
    {
        var row = new Dictionary<string, object?>
        {
            ["sprk_lane"] = (int)lane,
            ["_sprk_policyversion_value"] = VersionId,
            ["_ownerid_value"] = OwnerId,
        };
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
        new(Validator(), Substitute.For<IGenericEntityService>(), NullLogger<RuleBodyDescriber>.Instance);

    private static PolicyVersionValidator Validator()
    {
        var schema = new RuleBodySchemaValidator();
        return new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
    }

    private static IGenericEntityService EntitiesReturningPlan(string plan, string? ruleBody = null)
    {
        var entities = Substitute.For<IGenericEntityService>();
        var version = new Entity("sprk_policyversion", VersionId) { ["sprk_decisionplan"] = plan };
        if (ruleBody is not null)
        {
            version["sprk_rulebody"] = ruleBody;
        }

        entities.RetrieveAsync("sprk_policyversion", VersionId, Arg.Any<string[]>(), Arg.Any<CancellationToken>()).Returns(version);
        return entities;
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
