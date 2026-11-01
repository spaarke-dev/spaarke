using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 168 (D-194): the commit route hands each Assign Task with Send email notification on to the follow-ups AFTER the Decision Record
/// (D-17), and nothing the notification does can fail or change the decision. What the notice then sends is pinned in
/// <c>AssignTaskNotifierTests</c>.
/// </summary>
public sealed partial class DecisionCommitServiceTests
{
    private const string AssignTaskPlan = "{\"actions\":[\"assign-task\"]}";
    private static readonly Guid TaskEventId = Guid.Parse("c0430000-0000-4000-8000-000000000168");

    /// <summary>A rig whose plan is the no-budget decision's Assign Task, whose executor writes <see cref="TaskEventId"/>, and whose
    /// follow-ups log "notify" (with the notices) when they are handed any.</summary>
    private static (Rig Rig, List<IReadOnlyList<AssignTaskNotice>> Handed) AssignTaskRig()
    {
        var rig = new Rig();
        rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity("sprk_policyversion", id) { ["sprk_decisionplan"] = AssignTaskPlan });
        rig.Executors["assign-task"].Result = DecisionActionOutcome.Done("assign-task", new DecisionRecordRef("sprk_event", TaskEventId));
        var handed = new List<IReadOnlyList<AssignTaskNotice>>();
        rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
            .Callback((HttpContext _, IReadOnlyList<AssignTaskNotice> notices, CancellationToken _) =>
            {
                rig.Log.Add("notify");
                handed.Add(notices);
            })
            .ReturnsAsync(Array.Empty<string>());
        return (rig, handed);
    }

    private static DecisionReview AssignTaskReview(object? notify, bool omitNotify = false)
    {
        var values = new List<(string, object?)>
        {
            ("assignee", Assignee.ToString("D")),
            ("description", "Call the client about the budget"),
            ("dueDate", "2026-11-15"),
        };
        if (!omitNotify)
            values.Add(("notify", notify));
        return new DecisionReview
        {
            ReviewId = Guid.NewGuid(),
            Confirmed = true,
            Actions = [Take("assign-task", Values([.. values]))],
            Because = "The matter has no budget",
        };
    }

    [Fact]
    public async Task AssignTask_NotifyOn_HandsOneNoticeAfterTheRecordAndTheClose_D17_D194()
    {
        var (rig, handed) = AssignTaskRig();

        var result = await rig.CommitAsync(AssignTaskReview(true));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        rig.Writes.Should().Equal("execute:assign-task", "record:sprk_decisionrecord", $"close:{Primary}");
        rig.Log.IndexOf("notify").Should().BeGreaterThan(rig.Log.IndexOf("record:sprk_decisionrecord"), "D-17: the record is written first");
        rig.Log.IndexOf("notify").Should().BeGreaterThan(rig.Log.IndexOf($"close:{Primary}"));
        rig.Records.Should().ContainSingle("the notification writes no second record");

        var notice = handed.Should().ContainSingle().Which.Should().ContainSingle().Which;
        notice.AssigneeContactId.Should().Be(Assignee, "only the contact the step bound");
        notice.TaskId.Should().Be(TaskEventId);
        notice.Description.Should().Be("Call the client about the budget");
        notice.Due.Should().Be(new DateOnly(2026, 11, 15));
        notice.Core.Should().NotBeNull();
        notice.Core!.Entity.Should().Be("sprk_matter");
        notice.Core.Id.Should().Be(Matter);
    }

    [Fact]
    public async Task AssignTask_NotifyOmitted_IsOn_D194()
    {
        var (rig, handed) = AssignTaskRig();

        var result = await rig.CommitAsync(AssignTaskReview(null, omitNotify: true));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        handed.Should().ContainSingle().Which.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData("false")]
    [InlineData("FALSE")]
    public async Task AssignTask_NotifyOff_HandsNothing_MustNotFire(object notify)
    {
        var (rig, handed) = AssignTaskRig();

        var result = await rig.CommitAsync(AssignTaskReview(notify));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        handed.Should().BeEmpty("Send email notification is off");
        rig.Log.Should().NotContain("notify");
    }

    [Fact]
    public async Task AssignTask_NotifyNotAYesNo_IsRefusedBeforeAnyWrite()
    {
        var (rig, handed) = AssignTaskRig();

        var result = await rig.CommitAsync(AssignTaskReview("maybe"));

        result.ReasonCode.Should().Be(DecisionCommitReasons.InvalidParameter);
        rig.Writes.Should().BeEmpty();
        handed.Should().BeEmpty();
    }

    [Fact]
    public async Task AssignTask_ANotificationFault_LeavesTheDecisionRecorded_TheRecordAndTheSignalUnchanged_MustNotFire()
    {
        var (rig, _) = AssignTaskRig();
        rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("graph did not answer"));

        var result = await rig.CommitAsync(AssignTaskReview(true));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.DecisionRecordId.Should().Be(RecordId);
        rig.Writes.Should().Equal("execute:assign-task", "record:sprk_decisionrecord", $"close:{Primary}");
        rig.Records.Should().ContainSingle();
        rig.Status(Primary).Should().Be(Resolved);
        rig.Resolution(Primary).Should().Be(SignalWriter.ResolutionActed);
        result.Steps.Should().ContainSingle().Which.Written.Should().Equal(new DecisionCommitRef("sprk_event", TaskEventId));
        result.Warnings.Should().Contain("assignee_email_not_sent");
    }

    [Fact]
    public async Task AssignTask_TheNotifiersWarnings_ReachTheResult()
    {
        var (rig, _) = AssignTaskRig();
        rig.FollowUps.Setup(f => f.NotifyAssigneesAsync(It.IsAny<HttpContext>(), It.IsAny<IReadOnlyList<AssignTaskNotice>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "assignee_email_missing" });

        var result = await rig.CommitAsync(AssignTaskReview(true));

        result.Status.Should().Be(DecisionCommitStatus.Recorded, result.Detail);
        result.Warnings.Should().Contain("assignee_email_missing");
    }

    [Fact]
    public async Task AssignTask_AStepThatDidNotFinish_IsNotNotified_MustNotFire()
    {
        var (rig, handed) = AssignTaskRig();
        rig.Executors["assign-task"].Result = DecisionActionOutcome.Refused("assign-task", DecisionActionReasons.WriteFailed, "no");

        var result = await rig.CommitAsync(AssignTaskReview(true));

        result.Status.Should().NotBe(DecisionCommitStatus.Recorded);
        handed.Should().BeEmpty();
    }
}
