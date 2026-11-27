using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarke-ontology-platform-r1 task 228 (#1991, DI-101): recording an inquiry's disposition completes its "Record the outcome" To Do and
/// queues <see cref="SignalTriggerKind.InquiryDisposition"/> for that To Do. The trigger runs the nightly job's own scoped path for the
/// Do lane as well, so the To Do's open Do-lane Signal closes as ConditionCleared within the job's latency instead of at the next nightly
/// run. Every other trigger still evaluates the Decide lane only (D-12).
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). A partial of <see cref="SignalReevaluationJobTests"/>: the REAL job,
/// evaluator, writer and Decision Record writer over that class's in-memory Dataverse seams (task 153's completed-To-Do fixture).</remarks>
public partial class SignalReevaluationJobTests
{
    private static readonly Guid DispositionInquiryId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000002a8");

    private static Task<JobOutcome> RunTodoTriggerAsync(Harness h, SignalTriggerKind kind) =>
        TriggerHandler(h).ProcessAsync(TriggerJob(kind, "sprk_todo", TodoId, DispositionInquiryId), default);

    [Fact]
    public async Task InquiryDispositionTrigger_OnTheCompletedToDo_ClosesItsOpenDoLaneSignal_AsConditionCleared()
    {
        var (h, signalId) = ToDoCompletedByAColleague(); // the To Do is no longer a hit: completed

        var outcome = await RunTodoTriggerAsync(h, SignalTriggerKind.InquiryDisposition);

        outcome.Status.Should().Be(JobStatus.Completed);
        var signal = h.Org.Rows[signalId];
        signal.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusResolved);
        signal.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        h.Org.ConditionalUpdates[0].Id.Should().Be(signalId);
        h.Org.ConditionalUpdates[0].Fields.Should().NotContainKey("sprk_decisionrecord", "the close is the system close, not a decision");
        h.Org.CreateCalls.Should().Be(0, "no Signal is raised");
        // The same path as the nightly run (D-145b): one Routine closure record naming who changed the To Do, and no other record.
        h.Org.DecisionRecordRows.Values.Should().ContainSingle()
            .Which.GetAttributeValue<OptionSetValue>("sprk_recordclass")!.Value.Should().Be(100000001, "Routine, as on the nightly run");
    }

    [Fact]
    public async Task InquiryDispositionTrigger_Again_WritesNothing()
    {
        var (h, _) = ToDoCompletedByAColleague();
        await RunTodoTriggerAsync(h, SignalTriggerKind.InquiryDisposition);
        h.Org.ResetCounts();

        var outcome = await RunTodoTriggerAsync(h, SignalTriggerKind.InquiryDisposition);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.WriteCalls.Should().Be(0, "D-61: a closed, recorded Signal is never stamped again");
    }

    [Fact]
    public async Task InquiryDispositionTrigger_OnAToDoThatStillHolds_ClosesNothing()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        h.WithTodo(TodoId, new DateTime(2026, 10, 1)); // still open and past due: still a hit

        var outcome = await RunTodoTriggerAsync(h, SignalTriggerKind.InquiryDisposition);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.Rows[signalId].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        h.Org.DecisionRecordRows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SignalTriggerKind.Classification)]
    [InlineData(SignalTriggerKind.BudgetRevision)]
    [InlineData(SignalTriggerKind.AssociationConfirmed)]
    public async Task EveryOtherTrigger_StillEvaluatesTheDecideLaneOnly_SoTheDoLaneSignalIsLeftToTheNightlyRun(SignalTriggerKind kind)
    {
        var (h, signalId) = ToDoCompletedByAColleague();

        var outcome = await RunTodoTriggerAsync(h, kind);

        outcome.Status.Should().Be(JobStatus.Completed);
        h.Org.Rows[signalId].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        h.Org.WriteCalls.Should().Be(0);
    }

    [Fact]
    public void OnlyTheInquiryDispositionKind_IncludesTheDoLane()
    {
        Enum.GetValues<SignalTriggerKind>()
            .Where(k => SignalEvaluationTrigger.IncludesDoLane(SignalEvaluationTrigger.KindName(k)))
            .Should().Equal(SignalTriggerKind.InquiryDisposition);
    }
}
