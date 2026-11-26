using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology task 232 (D-230, #1767, #1663): when the job raises a spend, budget or utilization Threshold Signal on a matter or project, it
/// recalculates that one record's stored roll-up through ebilling-r1's <see cref="FinanceRollupService"/>, so the form shows the figure the
/// Signal states. Nothing else recalculates: an unchanged open Signal, a closure, another record and every other rule (D-61).
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the real job, evaluator and writer over the <see cref="Harness"/> seams;
/// the roll-up service at its virtual seam (<see cref="RollupRecorder"/>), whose own unchanged-skip is pinned by
/// <c>FinanceRollupUnchangedWriteTests</c>.</remarks>
public partial class SignalReevaluationJobTests
{
    [Fact]
    public async Task Run_UtilizationThreshold_RaisedOnAMatter_RecalculatesThatMattersRollup_AndNoOtherRecord_232()
    {
        var h = new Harness().WithThresholdPolicy()
            .WithMatterUtilization(MatterId, 95m, "MAT-OVER")
            .WithMatterUtilization(OtherMatterId, 62m, "MAT-UNDER");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreatedPayloads.Should().ContainSingle();
        h.Rollup.Calls.Should().Equal(new[] { ("sprk_matter", MatterId) },
            "only the matter whose Signal was raised is recalculated, once; the matter under the threshold is not touched");
    }

    [Fact]
    public async Task Run_SpendThreshold_RaisedOnAMatter_RecalculatesThatMattersRollup_232()
    {
        const string body = """{"type":"Threshold","subject":"sprk_matter","threshold":{"field":"sprk_totalspendtodate","operator":">=","value":1000}}""";
        var h = new Harness().WithThresholdPolicy(body, "Spend to date is {{sprk_totalspendtodate}}.");
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", MatterId) { ["sprk_matterid"] = MatterId });
        h.WithInvoice("sprk_matter", MatterId, 1200m, InvoiceStatusCodes.Approved);

        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_sentence").Should().Be("Spend to date is 1200.");
        h.Rollup.Calls.Should().Equal(("sprk_matter", MatterId));
    }

    [Fact]
    public async Task Run_UtilizationThreshold_RaisedOnAProject_RecalculatesThatProjectsRollup_232()
    {
        var body = UtilizationBody.Replace("sprk_matter", "sprk_project", StringComparison.Ordinal);
        var h = new Harness().WithThresholdPolicy(body).WithProject(ProjectId, "PRJ-1")
            .WithBudget("sprk_project", ProjectId, 200000m).WithInvoice("sprk_project", ProjectId, 190000m);

        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.CreatedPayloads.Should().ContainSingle();
        h.Rollup.Calls.Should().Equal(new[] { ("sprk_project", ProjectId) }, "the project's own recalculation, never the matter's");
    }

    [Fact]
    public async Task Run_UtilizationThreshold_TheSignalAlreadyOpenAndUnchanged_RecalculatesNothing_232()
    {
        // D-61: the second pass finds the open Signal unchanged and writes nothing, so nothing is recalculated either.
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, 95m, "MAT-OVER");
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Rollup.Calls.Should().ContainSingle();

        h.Rollup.Calls.Clear();
        h.Org.ResetCounts();
        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.CreateCalls.Should().Be(0);
        h.Rollup.Calls.Should().BeEmpty("an unchanged Signal triggers no recalculation");
    }

    [Fact]
    public async Task Run_UtilizationThreshold_AClosedSignal_RecalculatesNothing_232()
    {
        // The matter is under the threshold now: its open Signal closes ConditionCleared. That is not a raise.
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, 62m, "MAT-UNDER");
        var id = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);

        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusResolved);
        h.Rollup.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_UtilizationThreshold_ReRaisedAfterARecrossing_RecalculatesAgain_232()
    {
        // D-120: Acted, then observed under the threshold (stamped), then over it again: episode 2 is a raise and recalculates.
        var h = ThresholdActedHarness(utilization: 62m);
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Rollup.Calls.Should().BeEmpty("the cleared-observed stamp is not a raise");

        h.Sysadmin.InvoiceRows.Single()["sprk_totalamount"] = new Money(95000m);
        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.CreatedPayloads.Should().ContainSingle().Which.GetAttributeValue<string>("sprk_dedupekey")
            .Should().Be($"{PolicyCode}|sprk_matter|{Lower(MatterId)}|2");
        h.Rollup.Calls.Should().Equal(("sprk_matter", MatterId));
    }

    [Fact]
    public async Task Run_UtilizationThreshold_ACreateThatMeetsAnExistingRow_CreatedFalse_RecalculatesNothing_232()
    {
        // The dedupe race: another pass created this episode between this pass's read and its create, so the writer reconciles to the
        // existing row (Created false). That pass raised the Signal and recalculated; this one must not.
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, 95m, "MAT-OVER");
        var seeded = false;
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_signal" && !seeded)
            {
                seeded = true;
                h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);
            }

            return null;
        };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        seeded.Should().BeTrue("the planner asked for a create");
        h.Org.CreatedPayloads.Should().BeEmpty("the create met the existing row");
        result.ResultJson.Should().Contain("\"updated\":1");
        h.Rollup.Calls.Should().BeEmpty("a create that returned Created=false raised nothing");
    }

    [Fact]
    public async Task Run_ANonFinancePolicy_RaisedOnAMatter_RecalculatesNothing_232()
    {
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);

        (await h.RunAsync()).Success.Should().BeTrue();

        h.Org.CreatedPayloads.Should().ContainSingle();
        h.Rollup.Calls.Should().BeEmpty("only a base-derived spend, budget or utilization Threshold recalculates the roll-up");
    }

    [Fact]
    public async Task Run_UtilizationThreshold_ARecalculationFault_LeavesTheSignalRaised_AndIsLogged_232()
    {
        var h = new Harness().WithThresholdPolicy().WithMatterUtilization(MatterId, 95m, "MAT-OVER");
        h.Rollup.Failure = new InvalidOperationException("the roll-up write failed");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue("a recalculation fault never fails the Signal pass");
        h.Org.CreatedPayloads.Should().ContainSingle("the Signal stays raised");
        h.Rollup.Calls.Should().Equal(("sprk_matter", MatterId));
        h.Log.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("could not be recalculated", StringComparison.Ordinal))
            .Which.Exception.Should().BeSameAs(h.Rollup.Failure);
    }

    [Fact]
    public void RollupSubjectOf_IsTheMatterOrProject_OfADerivedThresholdOnly_232()
    {
        var hits = Array.Empty<SignalHit>();
        SignalEvaluation Of(string? subject, string? field) =>
            new(true, subject, RunAt, 14, hits) { DerivedThresholdField = field };

        SignalReevaluationJob.RollupSubjectOf(Of("sprk_matter", "sprk_totalspendtodate")).Should().Be("sprk_matter");
        SignalReevaluationJob.RollupSubjectOf(Of("sprk_project", "sprk_totalbudget")).Should().Be("sprk_project");
        SignalReevaluationJob.RollupSubjectOf(Of("sprk_matter", null)).Should().BeNull("an Existence rule on a matter");
        SignalReevaluationJob.RollupSubjectOf(Of("sprk_todo", "sprk_totalspendtodate")).Should().BeNull("no other table has a finance roll-up");
        SignalReevaluationJob.RollupSubjectOf(SignalEvaluation.Invalid).Should().BeNull();
    }

    /// <summary>Task 232: ebilling-r1's roll-up service at its virtual seam, recording what it is asked to recalculate.</summary>
    internal sealed class RollupRecorder : FinanceRollupService
    {
        public RollupRecorder()
            : base(Moq.Mock.Of<IDataverseService>(), Moq.Mock.Of<IFieldMappingDataverseService>(),
                new BillingDecisionRecorder(new Sprk.Bff.Api.Tests.Services.Signals.DecisionRecordWriterTests.Harness().Writer,
                    Moq.Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>(), NullLogger<BillingDecisionRecorder>.Instance),
                NullLogger<FinanceRollupService>.Instance)
        {
        }

        public List<(string Entity, Guid Id)> Calls { get; } = new();

        public Exception? Failure { get; set; }

        public override Task<RecalculateFinanceResponse> RecalculateMatterAsync(Guid matterId, CancellationToken ct = default) =>
            Record("sprk_matter", matterId);

        public override Task<RecalculateFinanceResponse> RecalculateProjectAsync(Guid projectId, CancellationToken ct = default) =>
            Record("sprk_project", projectId);

        private Task<RecalculateFinanceResponse> Record(string entity, Guid id)
        {
            Calls.Add((entity, id));
            return Failure is null ? Task.FromResult(new RecalculateFinanceResponse()) : Task.FromException<RecalculateFinanceResponse>(Failure);
        }
    }
}
