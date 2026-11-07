using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095d-2b-1: Set firm queues the rules re-run after its Decision Record (apply, record, enqueue), answers <c>rulesRerun</c>
/// from the stored decision, and turns a failed enqueue into 503 incomplete at <c>failedStep</c> <c>rerun</c> that a repeat finishes.
/// </summary>
public sealed partial class InvoiceAssignFirmServiceTests
{
    [Fact(DisplayName = "095d-2b-1: after the apply and the record, Set firm queues ONE re-run for its submission and decision and answers rulesRerun pending")]
    public async Task Assigned_QueuesTheRerun_AfterTheRecord()
    {
        var w = new World();
        var recordedAtEnqueue = -1;
        w.OnEnqueue = () => { recordedAtEnqueue = w.Recorded.Count; return null; };

        var result = await w.Assign();

        (result.Outcome, result.Status, result.RulesRerun).Should().Be((AssignFirmOutcome.Assigned, 200, InvoiceAssignFirmService.RulesRerunPending));
        w.Enqueued.Should().Equal([(SubmissionId, InvoiceAssignFirmService.DecisionIdFor(SubmissionId))]);
        recordedAtEnqueue.Should().Be(1, "apply, then record, then enqueue");
    }

    [Theory(DisplayName = "095d-2b-1 (T14): a failed enqueue is 503 incomplete at failedStep rerun - the firm is set and recorded, no exception text")]
    [InlineData("fault")]
    [InlineData("timeout")]
    [InlineData("timeout-cancel")]
    public async Task EnqueueFails_IsIncomplete_AtStepRerun(string kind)
    {
        var w = new World
        {
            OnEnqueue = () => kind switch
            {
                "timeout" => new TimeoutException("secret queue text"),
                "timeout-cancel" => new TaskCanceledException("secret queue text"),
                _ => new InvalidOperationException("secret queue text"),
            },
        };

        var result = await w.Assign();

        (result.Outcome, result.Status, result.Code, result.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, 503, AssignFirmCodes.Incomplete, "rerun"));
        result.Detail.Should().Be(InvoiceAssignFirmService.IncompleteRerunDetail).And.NotContain("secret");
        w.Recorded.Should().ContainSingle("the decision was recorded before the enqueue");
        w.Applied.Should().NotBeNull();
        w.Enqueued.Should().BeEmpty();
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.State.ContainsKey("Step") && Equals(e.State["Step"], "rerun"));
        w.Locks.Released.Should().BeEquivalentTo(w.Locks.Held);
    }

    [Fact(DisplayName = "095d-2b-1 (T14, T17): with the REAL allocator, a 503 at the enqueue, then the repeat: the repeat queues the re-run - exactly one queued, one apply, the same record")]
    public async Task EnqueueFails_ThenTheRepeat_QueuesExactlyOneRerun()
    {
        var w = new World();
        w.OnEnqueue = () => new TimeoutException("queue unavailable");

        var first = await w.Assign(allocator: RealAllocator(w));

        (first.Outcome, first.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, "rerun"));
        CommitInto(w, w.Transactions.Single());
        w.OnEnqueue = null;

        var again = await w.Assign(allocator: RealAllocator(w));

        (again.Outcome, again.Status, again.RulesRerun).Should().Be((AssignFirmOutcome.AlreadyAssigned, 200, InvoiceAssignFirmService.RulesRerunPending));
        w.Enqueued.Should().ContainSingle("the repeat queued it; the 503 queued nothing").Which
            .Should().Be((SubmissionId, InvoiceAssignFirmService.DecisionIdFor(SubmissionId)));
        w.EnqueueAttempts.Should().HaveCount(2);
        w.Transactions.Should().HaveCount(1, "a repeat never applies twice");
        w.Recorded[1].Should().BeEquivalentTo(w.Recorded[0], "the repeat records the same decision (the writer answers already recorded)");
    }

    [Fact(DisplayName = "095d-2b-1 fix round F2 (must fire): the request token cancelled after the commit (apply and record done) still queues the re-run, on a token the caller cannot cancel")]
    public async Task RequestCancelledAfterTheCommit_StillQueuesTheRerun()
    {
        using var cts = new CancellationTokenSource();
        var w = new World
        {
            // The caller goes away once the Decision Record is written: the commit has happened, the enqueue is next.
            OnRecord = d => { cts.Cancel(); return new BillingDecisionRecordResult(World.RecordId, AlreadyRecorded: false, Sprk.Bff.Api.Services.Signals.RecordedDecisionClass.Judgement); },
        };

        var result = await w.Assign(ct: cts.Token);

        cts.IsCancellationRequested.Should().BeTrue("the control is real: the request token was cancelled before the enqueue");
        (result.Outcome, result.Status, result.RulesRerun).Should().Be((AssignFirmOutcome.Assigned, 200, InvoiceAssignFirmService.RulesRerunPending));
        w.Applied.Should().NotBeNull();
        w.Recorded.Should().ContainSingle();
        w.Enqueued.Should().Equal([(SubmissionId, InvoiceAssignFirmService.DecisionIdFor(SubmissionId))]);
        w.EnqueueTokens.Should().ContainSingle().Which.CanBeCanceled.Should().BeFalse("the enqueue does not run on the request token");
    }

    [Fact(DisplayName = "095d-2b-1 fix round F2 (must not fire): a cancellation before the commit writes nothing, records nothing and queues nothing")]
    public async Task RequestCancelledBeforeTheCommit_WritesNothing_AndQueuesNothing()
    {
        using var cts = new CancellationTokenSource();
        var w = new World
        {
            OnRenumber = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); },
        };

        var act = () => w.Assign(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        w.Applied.Should().BeNull("the apply never committed");
        w.Transactions.Should().BeEmpty();
        w.Recorded.Should().BeEmpty();
        w.EnqueueAttempts.Should().BeEmpty();
        w.Enqueued.Should().BeEmpty();
    }

    /// <summary>Stores the committed re-run on the world's submission: <c>rulesRerun</c> completed, and (when given) <c>assignFirmRerun.state</c>.</summary>
    private static void StoreCompletedRerun(World w, string? rerunState)
    {
        var root = JsonNode.Parse(w.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!.AsObject();
        root[InvoiceAssignFirmService.StoredDecisionMember]!["rulesRerun"] = InvoiceAssignFirmService.RulesRerunCompleted;
        if (rerunState is not null)
        {
            root[InvoiceSubmissionRerun.StateMember] = new JsonObject { ["state"] = rerunState };
        }

        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);
    }

    [Fact(DisplayName = "095d-2b-1 / EB-D-204 (must not fire): a repeat after the re-run is done (rulesRerun completed, assignFirmRerun.state done) queues nothing and answers rulesRerun completed")]
    public async Task Repeat_AfterTheRerunIsDone_QueuesNothing()
    {
        var w = Applied(Firm);
        StoreCompletedRerun(w, InvoiceSubmissionRerun.StateDone);

        var result = await w.Assign();

        (result.Outcome, result.Status, result.RulesRerun).Should().Be((AssignFirmOutcome.AlreadyAssigned, 200, InvoiceAssignFirmService.RulesRerunCompleted));
        w.EnqueueAttempts.Should().BeEmpty("the re-run is done: the gate is open and there is nothing to finish");
    }

    [Fact(DisplayName = "095d-2b-2 re-check F4 / EB-D-204 (must fire): a repeat on a committed row (rulesRerun completed, state committed) queues ONE re-run job - the job runs only the after-commit steps")]
    public async Task Repeat_OnACommittedRow_QueuesOneJob()
    {
        var w = Applied(Firm);
        StoreCompletedRerun(w, InvoiceSubmissionRerun.StateCommitted);

        var result = await w.Assign();

        (result.Outcome, result.Status, result.RulesRerun).Should().Be((AssignFirmOutcome.AlreadyAssigned, 200, InvoiceAssignFirmService.RulesRerunCompleted));
        w.Enqueued.Should().Equal([(SubmissionId, InvoiceAssignFirmService.DecisionIdFor(SubmissionId))],
            "a dead-lettered after-commit step is finished by a repeat Set firm; the job is idempotent");
    }

    [Fact(DisplayName = "095d-2b-2 re-check F4 / EB-D-204 (must fire, fail closed): a repeat on a row with rulesRerun completed and NO re-run state queues one job, as the gate holds it pending")]
    public async Task Repeat_OnACompletedRowWithNoState_QueuesOneJob()
    {
        var w = Applied(Firm);
        StoreCompletedRerun(w, rerunState: null);

        var result = await w.Assign();

        result.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
        w.Enqueued.Should().ContainSingle();
    }

    [Fact(DisplayName = "095d-2b-2 re-check F4: a failed enqueue on a committed row is 503 incomplete at failedStep rerun, as for a pending row")]
    public async Task Repeat_OnACommittedRow_EnqueueFails_IsIncompleteAtRerun()
    {
        var w = Applied(Firm);
        StoreCompletedRerun(w, InvoiceSubmissionRerun.StateCommitted);
        w.OnEnqueue = () => new TimeoutException("queue unavailable");

        var result = await w.Assign();

        (result.Outcome, result.Status, result.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, 503, "rerun"));
    }

    [Fact(DisplayName = "095d-2b-1: a repeat while the re-run is still pending queues it again (safe: the re-run commits once)")]
    public async Task Repeat_WhileThePendingRerunRuns_QueuesItAgain()
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);

        var result = await w.Assign();

        (result.Outcome, result.RulesRerun).Should().Be((AssignFirmOutcome.AlreadyAssigned, InvoiceAssignFirmService.RulesRerunPending));
        w.Enqueued.Should().ContainSingle();
    }

    [Fact(DisplayName = "095d-2b-1 (must not fire): a record that fails queues no re-run - the record comes first")]
    public async Task RecordFails_QueuesNoRerun()
    {
        var w = new World { OnRecord = _ => throw new TimeoutException("no answer") };

        var result = await w.Assign();

        result.FailedStep.Should().Be("record");
        w.EnqueueAttempts.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-2b-1: the production enqueue submits the re-run job type through the queue, with the submission and decision ids")]
    public async Task TheProductionEnqueue_SubmitsTheRerunJob()
    {
        var options = new Mock<Microsoft.Extensions.Options.IOptions<Sprk.Bff.Api.Configuration.ServiceBusOptions>>();
        options.Setup(o => o.Value).Returns(new Sprk.Bff.Api.Configuration.ServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
        });
        var jobs = new Mock<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>(
            MockBehavior.Strict, options.Object, Mock.Of<ILogger<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        Sprk.Bff.Api.Services.Jobs.JobContract? sent = null;
        jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((Sprk.Bff.Api.Services.Jobs.JobContract j, CancellationToken _) => sent = j)
            .Returns(Task.CompletedTask);
        var decision = InvoiceAssignFirmService.DecisionIdFor(SubmissionId);

        await InvoiceAssignFirmService.EnqueueRerun(jobs.Object)(SubmissionId, decision, CancellationToken.None);

        sent!.JobType.Should().Be(InvoiceSubmissionRerunJobHandler.JobTypeName);
        sent.IdempotencyKey.Should().Be(InvoiceSubmissionRerunJobHandler.BuildJob(SubmissionId, decision, null).IdempotencyKey);
        sent.Payload!.RootElement.GetProperty("submissionId").GetGuid().Should().Be(SubmissionId);
        sent.Payload.RootElement.GetProperty("decisionId").GetGuid().Should().Be(decision);
    }
}
