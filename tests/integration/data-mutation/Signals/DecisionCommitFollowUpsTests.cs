using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 043: what the commit route does after its writes (032 and 034 hand-offs): the budget-revision trigger for a revision a
/// decision wrote, and suppression for the grains it dismissed. Neither may fail the commit; each is reported as a warning.
/// The trigger is scripted at its virtual seam; suppression is 034's REAL service over a scripted entity service.
/// </summary>
public sealed class DecisionCommitFollowUpsTests
{
    private static readonly Guid Matter = Guid.Parse("c0430000-0000-4000-8000-000000000111");
    private static readonly Guid TypeMatter = Guid.Parse("c0430000-0000-4000-8000-000000000110");
    private static readonly Guid Policy = Guid.Parse("c0430000-0000-4000-8000-000000000120");
    private static readonly Guid Revision = Guid.Parse("c0430000-0000-4000-8000-000000000152");
    private static readonly Guid Budget = Guid.Parse("c0430000-0000-4000-8000-000000000140");
    private static readonly Guid Item = Guid.Parse("c0430000-0000-4000-8000-000000000170");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<SignalEvaluationTrigger> _trigger;

    public DecisionCommitFollowUpsTests()
    {
        var jobs = new Mock<JobSubmissionService>(MockBehavior.Loose, Options.Create(new ServiceBusOptions { QueueName = "test" }),
            NullLogger<JobSubmissionService>.Instance, new Mock<ServiceBusClient>().Object);
        _trigger = new Mock<SignalEvaluationTrigger>(jobs.Object, NullLogger<SignalEvaluationTrigger>.Instance) { CallBase = false };
        _trigger.Setup(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _entities.Setup(e => e.RetrieveAsync("sprk_policy", Policy, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_policy", Policy) { ["sprk_policycode"] = "POL-COMMIT-BUDGET", ["sprk_countstowardsuppression"] = true });
        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
    }

    private DecisionCommitFollowUps FollowUps()
    {
        var writer = new OntologyWriterDataverseClient(() => new Mock<IOrganizationServiceAsync2>().Object,
            NullLogger<OntologyWriterDataverseClient>.Instance);
        // #1661: uac-r2's real synchronizer, scoped as in ExternalAccessModule; its walk reads the same mocked Dataverse.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddScoped(_ => new SecureChildShareSynchronizer(_entities.Object, null!, null!, configuration, NullLogger<SecureChildShareSynchronizer>.Instance));
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var suppression = new SignalSuppressionService(_entities.Object, writer, new FakeTimeProvider(DateTimeOffset.UtcNow),
            configuration, scopes,
            NullLogger<SignalSuppressionService>.Instance);
        return new DecisionCommitFollowUps(_trigger.Object, suppression, _entities.Object, NullLogger<DecisionCommitFollowUps>.Instance);
    }

    private static DecisionCommitStep Step(string code, string status, params DecisionCommitRef[] written) =>
        new($"action:{code}", code, 2, status, written, [], null, null);

    private static readonly DecisionCommitStep Revised =
        Step("revise-budget", "Done", new DecisionCommitRef("sprk_budgetrevision", Revision), new DecisionCommitRef("sprk_budget", Budget));

    private static DecisionCommitService.SignalRow Row(Guid? coreType, Guid? core, string? dedupe, Guid? policy) =>
        new(Guid.NewGuid(), DecisionLane.Decide, policy, Guid.NewGuid(), coreType, core, false, SignalWriter.SignalStatusOpen, null, null,
            null, null, core, null, false, dedupe, null, "1");

    [Fact]
    public async Task ARevisionTheDecisionWrote_QueuesTheBudgetRevisionTrigger_ForTheMatter_WithNoDelay()
    {
        var warnings = await FollowUps().AfterRecordedAsync(Matter, [Revised], [], CancellationToken.None);

        warnings.Should().BeEmpty();
        _trigger.Verify(t => t.EnqueueAsync(SignalTriggerKind.BudgetRevision, "sprk_matter", Matter, Revision, null, It.IsAny<CancellationToken>()), Times.Once);
        _trigger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ARevisionThatLandedBeforeAFailure_IsQueuedToo()
    {
        var failedAfter = Step("revise-budget", "Failed", new DecisionCommitRef("sprk_budgetrevision", Revision));

        await FollowUps().AfterFailedCommitAsync(Matter, [failedAfter, Step("add-todo", "Failed")], CancellationToken.None);

        _trigger.Verify(t => t.EnqueueAsync(SignalTriggerKind.BudgetRevision, "sprk_matter", Matter, Revision, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoRevision_QueuesNothing()
    {
        await FollowUps().AfterFailedCommitAsync(Matter, [Step("add-todo", "Done", new DecisionCommitRef("sprk_todo", Guid.NewGuid()))], CancellationToken.None);
        (await FollowUps().AfterRecordedAsync(Matter, [Step("approve-variance", "Done")], [], CancellationToken.None)).Should().BeEmpty();

        _trigger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ATriggerThatCannotBeQueued_IsAWarning_NotAFailure()
    {
        _trigger.Setup(t => t.EnqueueAsync(It.IsAny<SignalTriggerKind>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var warnings = await FollowUps().AfterRecordedAsync(Matter, [Revised], [], CancellationToken.None);

        warnings.Should().Equal("budget_revision_trigger_not_queued");
    }

    [Fact]
    public async Task ADismissal_RunsSuppressionForItsGrain()
    {
        var warnings = await FollowUps().AfterRecordedAsync(Matter, [], [Row(TypeMatter, Matter, null, Policy)], CancellationToken.None);

        warnings.Should().BeEmpty();
        _entities.Verify(e => e.RetrieveAsync("sprk_policy", Policy, It.Is<string[]>(c => c.Contains("sprk_policycode")), It.IsAny<CancellationToken>()), Times.Once);
        _entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce(),
            "034's ApplyAsync counted the grain's dismissals");
    }

    [Fact]
    public async Task SuppressionOverSignalsTheCallerCannotRead_ReachesTheCallerAsNothing_NoStatusNoCount()
    {
        // uac-r2 on 034: ApplyAsync counts every Signal of the (policy, core) grain app-only, Secure-team rows included. Here the
        // grain holds three dismissed team-owned Signals (enough to suppress) that this caller cannot read. This test is about
        // non-disclosure to the caller, not the wall: it deliberately runs with no Secure team (the wall and the undetermined 503
        // are covered in SignalSuppressionTests).
        var now = DateTime.UtcNow;
        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            // #1661: the Secure-team lookup (businessunit / team) is not a Signal read - it answers none, so this environment has no
            // Secure Record owner team and the partition (SignalSuppressionService.PartitionsAsync) is ordinary; only Signal reads return the grain.
            .ReturnsAsync((QueryExpression q, CancellationToken _) => q.EntityName != "sprk_signal" ? new EntityCollection()
                : new EntityCollection(Enumerable.Range(0, 3).Select(_ => new Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved),
                ["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionDismissed),
                ["sprk_resolvedon"] = now.AddDays(-1),
                ["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", Guid.NewGuid()),
                ["owningteam"] = new EntityReference("team", Guid.NewGuid()),
            }).ToList()));
        var walled = await FollowUps().AfterRecordedAsync(Matter, [], [Row(TypeMatter, Matter, null, Policy)], CancellationToken.None);

        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        var none = await FollowUps().AfterRecordedAsync(Matter, [], [Row(TypeMatter, Matter, null, Policy)], CancellationToken.None);

        walled.Should().Equal(none, "the caller's answer is the same whether or not the grain holds rows they cannot read");
        walled.Should().BeEmpty();
        typeof(DecisionCommitResult).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Suppress", StringComparison.OrdinalIgnoreCase) || n.Contains("Count", StringComparison.OrdinalIgnoreCase),
                "the commit response (Results.Ok of this type) carries no suppression status or count");
    }

    [Fact]
    public async Task SuppressionThatFails_IsAWarning_NotAFailure()
    {
        _entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("no answer"));

        var warnings = await FollowUps().AfterRecordedAsync(Matter, [], [Row(TypeMatter, Matter, null, Policy)], CancellationToken.None);

        warnings.Should().Equal("suppression_not_applied");
    }

    [Fact]
    public async Task APolicyWithoutACode_IsNotSuppressed_AndSaysSo()
    {
        _entities.Setup(e => e.RetrieveAsync("sprk_policy", Policy, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_policy", Policy));

        var warnings = await FollowUps().AfterRecordedAsync(Matter, [], [Row(TypeMatter, Matter, null, Policy)], CancellationToken.None);

        warnings.Should().Equal("suppression_not_applied");
        _entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Grains_AreKeyedAs034KeysThem_CoreRecordOrItem_AndOncePerGrain()
    {
        var key = SignalWriter.BuildDedupeKey("POL-DO", "sprk_todo", Item.ToString("D"), 2);

        var grains = DecisionCommitFollowUps.Grains(
        [
            Row(TypeMatter, Matter, null, Policy),
            Row(TypeMatter, Matter, null, Policy),
            Row(null, null, key, Policy),
            Row(null, null, "not-a-key", Policy),
            Row(TypeMatter, Matter, null, null),
        ]);

        grains.Should().Equal(
            (Policy, SuppressionGrain.ForCore(TypeMatter, Matter)),
            (Policy, SuppressionGrain.ForItem("sprk_todo", Item)));
    }

    [Fact]
    public void LandedRevisions_AreOnlyTheReviseBudgetStepsRevisionRows()
    {
        DecisionCommitFollowUps.LandedRevisions(
        [
            Revised,
            Step("add-todo", "Done", new DecisionCommitRef("sprk_budgetrevision", Guid.NewGuid())),
        ]).Should().Equal(Revision);
    }
}
