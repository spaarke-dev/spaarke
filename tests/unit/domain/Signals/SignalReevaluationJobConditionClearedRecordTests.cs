using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using Ownership = Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 153 (D-145b) end to end at unit level: the REAL job, writer, closure service and Decision Record writer over the in-memory
/// Dataverse seams of <see cref="SignalReevaluationJobTests"/>. When the nightly pass closes a Do-lane Signal as ConditionCleared it writes
/// ONE Routine Decision Record naming, best effort, the person who changed the item, and links it to the Signal. Nothing else about system
/// closures changes: Superseded, PolicyRetired, a Decide-lane ConditionCleared and a re-file stay record-free.
/// </summary>
public partial class SignalReevaluationJobTests
{
    /// <summary>Alice's To Do (owner <see cref="UserId"/>), its Signal raised three days before the pass; a colleague
    /// (<see cref="OtherUserId"/>, a person) completed it the day before, so it is no longer a hit.</summary>
    private static (Harness H, Guid SignalId) ToDoCompletedByAColleague()
    {
        var h = new Harness().WithTodoPolicy();
        h.Sysadmin.Stub(new Entity("sprk_todo", TodoId)
        {
            ["sprk_todoid"] = TodoId,
            ["sprk_name"] = "File the reply",
            ["sprk_duedate"] = new DateTime(2026, 10, 1),
            ["ownerid"] = new EntityReference("systemuser", UserId),
        });
        var signalId = h.Org.SeedSignal(Key(TodoPolicyCode, "sprk_todo", TodoId, 1), SignalWriter.SignalStatusOpen,
            policyId: h.Sysadmin.PolicyIdOf(TodoPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(TodoPolicyCode));
        h.Org.Rows[signalId]["createdon"] = RunAt.UtcDateTime.AddDays(-3);
        h.Sysadmin.Person(OtherUserId);
        h.Sysadmin.LastChange[TodoId] = (OtherUserId, RunAt.UtcDateTime.AddDays(-1));
        return (h, signalId);
    }

    private static bool IsDecisionRecordLink(Entity update) => update.Contains("sprk_decisionrecord");

    [Fact]
    public async Task Run_ColleagueCompletesAlicesToDo_ClosesTheSignal_AndWritesOneRoutineRecordConfirmedByTheColleague_LinkedToTheSignal()
    {
        var (h, signalId) = ToDoCompletedByAColleague();

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        record.GetAttributeValue<OptionSetValue>("sprk_recordclass")!.Value.Should().Be(100000001, "Routine");
        record.GetAttributeValue<EntityReference>("sprk_confirmedby")!.Id.Should().Be(OtherUserId, "the colleague who changed the To Do");
        record.GetAttributeValue<EntityReference>("sprk_regardingtodo")!.Id.Should().Be(TodoId);
        record.GetAttributeValue<string>("sprk_steps").Should().Contain(signalId.ToString("D"), "the record names the Signal it resolved");

        var signal = h.Org.Rows[signalId];
        signal.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusResolved);
        signal.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        signal.GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id);
        signal.GetAttributeValue<EntityReference>("sprk_resolvedby")!.Id.Should().Be(OtherUserId);

        h.Org.ConditionalUpdates.Should().HaveCount(2, "the close, then the link");
        h.Org.ConditionalUpdates[0].Fields.Should().NotContainKey("sprk_decisionrecord", "the close itself is unchanged");
        h.Org.ConditionalUpdates[1].Fields.Keys.Should().BeEquivalentTo("sprk_decisionrecord", "sprk_resolvedby");
        h.Org.CreateCalls.Should().Be(0, "no Signal is raised");
        result.ResultJson.Should().Contain("\"closureRecorded\":1").And.Contain("\"closureRecordsLinked\":1");
    }

    [Fact]
    public async Task Run_AgainAfterTheClosureIsRecorded_WritesNothingNew()
    {
        var (h, _) = ToDoCompletedByAColleague();
        await h.RunAsync();
        h.Org.ResetCounts();
        var reads = h.Sysadmin.ChangedByReads;
        h.Clock.Advance(TimeSpan.FromDays(1));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "D-61: a linked closure is never touched again");
        h.Org.DecisionRecordRows.Should().ContainSingle();
        h.Sysadmin.ChangedByReads.Should().Be(reads, "a linked closure is not a candidate: nothing is read for it");
    }

    [Fact]
    public async Task Run_WhoseLinkFailed_IsRetriedNextPass_FindsItsRecordByTheKey_AndLinksIt_WithoutASecondRecord()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        h.Org.ConditionalUpdateFaultFor = target => IsDecisionRecordLink(target) ? new InvalidOperationException("Simulated link failure.") : null;

        using var metrics = new MetricCapture();
        var first = await h.RunAsync();

        first.Success.Should().BeFalse("an unrecorded closure is a failed run");
        first.ResultJson.Should().Contain("\"closureRecordFailures\":1");
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.SignalRecordLinkFailed).Should().BeGreaterThanOrEqualTo(1);
        h.Org.DecisionRecordRows.Should().ContainSingle("the record was written; only its link failed");
        h.Org.Rows[signalId].Contains("sprk_decisionrecord").Should().BeFalse();

        h.Org.ConditionalUpdateFaultFor = null;
        h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        h.Org.DecisionRecordRows.Should().ContainSingle("D-139: the retry finds the record by its key and writes no second one");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(h.Org.DecisionRecordRows.Keys.Single());
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_resolvedby")!.Id.Should().Be(OtherUserId, "the stored confirmer");
    }

    [Fact]
    public async Task Run_WhenTheRecordWriteFails_TheSignalStaysClosed_TheFailureIsMetered_AndTheNextPassRecordsIt()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        h.Org.CreateFaultFor = entity => entity.LogicalName == "sprk_decisionrecord" ? new InvalidOperationException("Simulated create failure.") : null;

        using var metrics = new MetricCapture();
        var first = await h.RunAsync();

        first.Success.Should().BeFalse();
        first.ResultJson.Should().Contain("\"closed\":1").And.Contain("\"closureRecordFailures\":1");
        metrics.Counter("ontology.writer.failures", "reason", DecisionRecordRefusalReason.CreateFailed).Should().BeGreaterThanOrEqualTo(1);
        var signal = h.Org.Rows[signalId];
        signal.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusResolved, "the close stands");
        signal.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        signal.Contains("sprk_decisionrecord").Should().BeFalse();
        h.Org.DecisionRecordRows.Should().BeEmpty();

        h.Org.CreateFaultFor = null;
        h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id);
        record.GetAttributeValue<EntityReference>("sprk_confirmedby")!.Id.Should().Be(OtherUserId, "the change still falls before the close");
    }

    [Theory]
    [InlineData("applicationUser")]
    [InlineData("readFailed")]
    public async Task Run_WhenTheChangerIsNotAPersonOrCannotBeRead_WritesOneRecordWithNoConfirmer_AndTheUnknownPersonReason(string why)
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        if (why == "applicationUser") h.Sysadmin.ApplicationUser(OtherUserId);
        else h.Sysadmin.FaultOn = q => q.LinkEntities.Any(l => l.EntityAlias == "changedby") ? new TimeoutException("Simulated read timeout.") : null;

        var result = await h.RunAsync();

        result.Success.Should().BeTrue("an unknown person is not a failure");
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        record.Contains("sprk_confirmedby").Should().BeFalse("nobody is guessed");
        record.GetAttributeValue<string>("sprk_reason").Should().StartWith("condition_cleared_person_unknown - ");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id);
        h.Org.Rows[signalId].Contains("sprk_resolvedby").Should().BeFalse();
    }

    [Theory]
    [InlineData("superseded")]
    [InlineData("policyRetired")]
    [InlineData("decideLaneConditionCleared")]
    [InlineData("refiled")]
    public async Task Run_AnyOtherSystemClosure_WritesNoDecisionRecord(string closure)
    {
        Harness h;
        Guid? closedId = null;
        int expected;
        switch (closure)
        {
            case "superseded":
                (h, var supersededId) = ToDoCompletedByAColleague();
                h.Sysadmin.PublishNewVersion(TodoPolicyCode, TodoOverdueBody, "This To Do is past its due date.");
                (closedId, expected) = (supersededId, SignalWriter.ResolutionSuperseded);
                break;
            case "policyRetired":
                (h, var retiredId) = ToDoCompletedByAColleague();
                h.Sysadmin.SetPolicyState(TodoPolicyCode, enabled: false);
                (closedId, expected) = (retiredId, SignalWriter.ResolutionPolicyRetired);
                break;
            case "decideLaneConditionCleared":
                h = new Harness().WithPathBPolicy(); // no hit
                closedId = h.Org.SeedSignal(Key(PolicyCode, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);
                h.Org.Rows[closedId.Value]["createdon"] = RunAt.UtcDateTime.AddDays(-3);
                h.Sysadmin.Person(OtherUserId);
                h.Sysadmin.LastChange[MatterId] = (OtherUserId, RunAt.UtcDateTime.AddDays(-1));
                expected = SignalWriter.ResolutionConditionCleared;
                break;
            default:
                h = RefiledToDoHarness(); // still a hit, now under another core: the old episode closes, a new one opens
                closedId = h.Org.Rows.Keys.Single();
                expected = SignalWriter.ResolutionConditionCleared;
                break;
        }

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.Rows[closedId.Value].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(expected, "precondition: it closed");
        h.Org.DecisionRecordRows.Should().BeEmpty();
        h.Org.Rows.Values.Should().NotContain(r => r.Contains("sprk_decisionrecord"));
        h.Sysadmin.ChangedByReads.Should().Be(0, "no closure was a candidate");
    }

    [Fact]
    public async Task Run_ToDoUnderASecureProject_ItsClosureRecordIsASecureChild_OwnedByTheSecureRecordOwnersTeam()
    {
        var h = new Harness().WithTodoPolicy();
        h.Sysadmin.Stub(new Entity("sprk_todo", TodoId)
        {
            ["sprk_todoid"] = TodoId,
            ["sprk_name"] = "File the reply",
            ["ownerid"] = new EntityReference("systemuser", UserId),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId),
        });
        var signalId = h.Org.SeedSignal(Key(TodoPolicyCode, "sprk_todo", TodoId, 1), SignalWriter.SignalStatusOpen, coreId: ProjectId,
            policyId: h.Sysadmin.PolicyIdOf(TodoPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(TodoPolicyCode));
        h.Org.Rows[signalId]["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectType);
        h.Org.Rows[signalId]["createdon"] = RunAt.UtcDateTime.AddDays(-3);
        h.Sysadmin.Person(OtherUserId);
        h.Sysadmin.LastChange[TodoId] = (OtherUserId, RunAt.UtcDateTime.AddDays(-1));
        h.Ownership.AnswerFor[ProjectId] = Ownership.RecordOwnerResolution.Owned(SecureTeamId) with { IsSecureOwner = true };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeamId),
            "D-123: secure-if-any over every parent the record sits under");
        record.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(ProjectId), "D-34: the Signal's core, copied");
        h.Ownership.Contexts.Should().Contain(c => c.TargetRecordId == ProjectId && c.Parents.Any(p => p.EntityLogicalName == "sprk_todo"),
            "the record's owner is asked over the project and the To Do it is filed under");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id);
    }
}
