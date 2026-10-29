using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Scheduling;
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

        first.Success.Should().BeTrue("adversarial pass 1 F1b: a transient record failure is metered and retried, never a failed run");
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

        first.Success.Should().BeTrue("adversarial pass 1 F1b: the failure pages on ontology.writer.failures; it does not fail the run");
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

    // ── Adversarial pass 1 ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_ADeletedToDosClosure_AcrossNineNightlyPasses_EveryRunSucceeds_NoRecordIsWritten_OneSkipIsMetered_AndItIsNeverRetried()
    {
        // F1: the To Do was deleted, so it is no longer a hit and its Signal closes ConditionCleared. A by-id read of it faults
        // ObjectDoesNotExist, as the ServiceClient does. Its closure can never be recorded: a counted skip, not a failure, never retried.
        var h = new Harness().WithTodoPolicy();
        var signalId = h.Org.SeedSignal(Key(TodoPolicyCode, "sprk_todo", TodoId, 1), SignalWriter.SignalStatusOpen,
            policyId: h.Sysadmin.PolicyIdOf(TodoPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(TodoPolicyCode));
        h.Org.Rows[signalId]["createdon"] = RunAt.UtcDateTime.AddDays(-3);

        using var metrics = new MetricCapture();
        var results = new List<JobRunResult>();
        for (var night = 1; night <= 9; night++)
        {
            results.Add(await h.RunAsync());
            h.Clock.Advance(TimeSpan.FromDays(1));
        }

        results.Should().OnlyContain(r => r.Success, "a closure that cannot be recorded never fails the run (nor trips evaluator-stalled)");
        h.Org.Rows[signalId].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared,
            "precondition: the Signal closed");
        h.Org.DecisionRecordRows.Should().BeEmpty("a deleted subject gets no record");
        h.Org.Rows[signalId].Contains("sprk_decisionrecord").Should().BeFalse();
        results[0].ResultJson.Should().Contain("\"closureRecordSkips\":1").And.Contain("\"closureRecordFailures\":0");
        results.Skip(1).Should().OnlyContain(r => r.ResultJson!.Contains("\"closureRecordSkips\":0"), "the skip is recorded, so it is not re-attempted");
        h.Sysadmin.ChangedByReads.Should().Be(1, "only the first pass attempted the record");
        h.Log.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.ConditionClearedSkipped, "one skip, once")
            .Which.Level.Should().Be(LogLevel.Warning);
        h.Log.Should().NotContain(e => e.Level >= LogLevel.Error);
        metrics.Counter("ontology.writer.skips", "reason", OntologyWriterFailureReason.ClosureRecordSkippedSubjectMissing).Should().BeGreaterThanOrEqualTo(1);
        h.Cache.GetString(SignalReevaluationJob.ClosureRecordSkippedCacheKey(signalId, 1)).Should().NotBeNull("the skip is recorded");
    }

    /// <summary>F2: Alice's To Do carried a D-128 reminder while it was open: the Signal is Acknowledged and links the reminder's record.</summary>
    private static (Harness H, Guid SignalId, Guid ReminderId) ToDoWithAReminderCompletedByAColleague()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        var reminderId = Guid.NewGuid();
        h.Sysadmin.DecisionRecords[reminderId] = new Entity("sprk_decisionrecord", reminderId)
        {
            ["sprk_regardingtodo"] = new EntityReference("sprk_todo", TodoId),
            ["sprk_recordclass"] = new OptionSetValue((int)RecordedDecisionClass.Routine),
            ["sprk_decidedon"] = RunAt.UtcDateTime.AddDays(-2),
            ["createdon"] = RunAt.UtcDateTime.AddDays(-2),
            ["createdby"] = new EntityReference("systemuser", WriterOrg.WriterUserId), // the commit route writes through the writer
            ["sprk_steps"] = "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"steps\":[{\"code\":\"send-reminder\",\"taken\":true}]}",
        };
        h.Org.Rows[signalId]["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusAcknowledged);
        h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", reminderId);
        return (h, signalId, reminderId);
    }

    [Fact]
    public async Task Run_ReminderThenCompleted_TheClosingRecordReplacesTheRemindersLink_AndSetsResolvedBy_TheReminderUntouched()
    {
        var (h, signalId, reminderId) = ToDoWithAReminderCompletedByAColleague();
        var reminderBefore = h.Sysadmin.DecisionRecords[reminderId].Attributes.ToDictionary(a => a.Key, a => a.Value);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        var signal = h.Org.Rows[signalId];
        signal.GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id, "D-128 Q18: the closing record replaces the link");
        signal.GetAttributeValue<EntityReference>("sprk_resolvedby")!.Id.Should().Be(OtherUserId);
        h.Sysadmin.DecisionRecords[reminderId].Attributes.Should().BeEquivalentTo(reminderBefore, "records are append-only");
        h.Org.ConditionalUpdates.Concat(h.Org.Updates).Should().OnlyContain(u => u.Id == signalId, "nothing but the Signal is written");
        result.ResultJson.Should().Contain("\"closureRecordsLinked\":1").And.Contain("\"closureRecordSkips\":0");
    }

    [Fact]
    public async Task Run_WhenTheSignalNamesAnUnexpectedRecordAtTheLink_ItIsSkippedAndMetered_NothingIsOverwritten_AndItIsNotRetried()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        var foreign = Guid.NewGuid();
        // Adversarial pass 2 F3: the foreign record EXISTS, was created by the writer before the close and is a D-128 reminder, so every
        // other retry test passes on night 2 and only the skip marker keeps it from being replaced.
        h.Sysadmin.DecisionRecords[foreign] = ForeignReminderRecord(foreign, createdBy: WriterOrg.WriterUserId);
        // Between the job's read and its link, another writer links the Signal to some other record.
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_decisionrecord") h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", foreign);
            return null;
        };

        using var metrics = new MetricCapture();
        var first = await h.RunAsync();

        first.Success.Should().BeTrue("a foreign link is a skip, not a failure");
        first.ResultJson.Should().Contain("\"closureRecordSkips\":1").And.Contain("\"closureRecordsLinked\":0");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(foreign, "never overwritten");
        h.Org.Rows[signalId].Contains("sprk_resolvedby").Should().BeFalse();
        h.Org.ConditionalUpdates.Should().ContainSingle("the close only: no link was written");
        h.Log.Should().ContainSingle(e => e.Message.Contains(OntologyWriterFailureReason.ClosureRecordSkippedForeignLink))
            .Which.Level.Should().Be(LogLevel.Warning);
        metrics.Counter("ontology.writer.skips", "reason", OntologyWriterFailureReason.ClosureRecordSkippedForeignLink).Should().BeGreaterThanOrEqualTo(1);

        h.Org.CreateFaultFor = null;
        h.Org.ResetCounts();
        h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "a skipped closure is not retried");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(foreign);
    }

    [Fact]
    public async Task Run_WhenTheSignalChangesBetweenTheLinksReadAndItsWrite_TheLinkIsRefused_TheReminderLinkStays_AndTheNextPassLinks()
    {
        var (h, signalId, reminderId) = ToDoWithAReminderCompletedByAColleague();
        h.Org.TouchBeforeConditionalUpdateFor = target => target.Contains("sprk_decisionrecord"); // D-61: the row version moves

        var first = await h.RunAsync();

        first.Success.Should().BeTrue();
        first.ResultJson.Should().Contain("\"closureRecordsLinked\":0").And.Contain("\"changedSinceRead\":1");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(reminderId, "the refused link wrote nothing");
        h.Org.Rows[signalId].Contains("sprk_resolvedby").Should().BeFalse();

        h.Org.TouchBeforeConditionalUpdateFor = null;
        h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle("the retry finds the record by its key").Subject;
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id,
            "the retry still replaces the reminder, which existed before the close");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_resolvedby")!.Id.Should().Be(OtherUserId);
    }

    [Fact]
    public async Task Run_WhenTheJobsOwnCoreTypeReadIsDenied_TheFailureIsLoggedAndMetered_TheSignalStaysClosed()
    {
        // K4: an access denial from the job's own read is not already metered by any writer, so the job logs and meters it itself.
        var h = new Harness().WithTodoPolicy();
        h.Sysadmin.Stub(new Entity("sprk_todo", TodoId)
        {
            ["sprk_todoid"] = TodoId,
            ["ownerid"] = new EntityReference("systemuser", UserId),
            ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId),
        });
        var signalId = h.Org.SeedSignal(Key(TodoPolicyCode, "sprk_todo", TodoId, 1), SignalWriter.SignalStatusOpen, coreId: ProjectId,
            policyId: h.Sysadmin.PolicyIdOf(TodoPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(TodoPolicyCode));
        h.Org.Rows[signalId]["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", ProjectType);
        h.Org.Rows[signalId]["createdon"] = RunAt.UtcDateTime.AddDays(-3);
        h.Sysadmin.RetrieveFaultFor = (table, _) => table == "sprk_recordtype_ref"
            ? new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "Principal user is missing prvReadsprk_recordtype_ref privilege." },
                new FaultReason("Principal user is missing prvReadsprk_recordtype_ref privilege."))
            : null;

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"closureRecordFailures\":1");
        h.Org.Rows[signalId].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        h.Org.DecisionRecordRows.Should().BeEmpty();
        h.Log.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId == OntologyWriterEvents.EvaluatorPolicyFailed
                                          && e.Message.Contains("could not be recorded") && e.Message.Contains(OntologyWriterFailureReason.DataverseAccessDenied),
            "logged once, by the job");
        metrics.Counter("ontology.writer.failures", "reason", OntologyWriterFailureReason.DataverseAccessDenied).Should().BeGreaterThanOrEqualTo(1);
    }
}
