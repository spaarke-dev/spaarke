using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 153, adversarial pass 2, end to end at unit level (the REAL job, writer, closure service and Decision Record writer over the in-memory
/// seams of <see cref="SignalReevaluationJobTests"/>). F1: a user-created row carrying a closure's key is never its record. K (foreign link vs
/// cache eviction): a prior link is replaced on a retry only when it is a writer-created D-128 reminder or inquiry. K1: a closure's record left
/// unlinked by a person's re-close is a logged, metered skip. K (silent abandon): an unlinked closure whose subject is a hit again is logged.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private static readonly Guid ConsoleUserId = Guid.Parse("153f0123-0000-4000-8000-0000000000aa");

    /// <summary>A D-128 reminder on Alice's To Do, created two days before the pass by <paramref name="createdBy"/>; with
    /// <paramref name="resolving"/>, a review that resolved the item instead (mark-complete).</summary>
    private static Entity ForeignReminderRecord(Guid id, Guid createdBy, bool resolving = false) => new("sprk_decisionrecord", id)
    {
        ["sprk_regardingtodo"] = new EntityReference("sprk_todo", TodoId),
        ["sprk_recordclass"] = new OptionSetValue((int)RecordedDecisionClass.Routine),
        ["sprk_decidedon"] = RunAt.UtcDateTime.AddDays(-2),
        ["createdon"] = RunAt.UtcDateTime.AddDays(-2),
        ["createdby"] = new EntityReference("systemuser", createdBy),
        ["sprk_steps"] = "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"steps\":[{\"code\":\""
                         + (resolving ? "mark-complete" : "send-reminder") + "\",\"taken\":true}]}",
    };

    [Fact]
    public async Task Run_AUserCreatedRowWithTheClosuresKeyAndCode_IsIgnored_TheGenuineRecordIsWritten_AndTheLinkAndResolvedByNameIt()
    {
        // F1 (probe E): a Console user (Deep create on sprk_decisionrecord) creates a row with the computable key, the step code and a
        // confirmer of their choosing.
        var (h, signalId) = ToDoCompletedByAColleague();
        var forged = Guid.NewGuid();
        var framed = Guid.NewGuid();
        h.Org.DecisionRecordRows[forged] = new Entity("sprk_decisionrecord", forged)
        {
            ["sprk_regardingtodo"] = new EntityReference("sprk_todo", TodoId),
            ["createdon"] = RunAt.UtcDateTime.AddDays(-2),
            ["createdby"] = new EntityReference("systemuser", ConsoleUserId),
            ["sprk_confirmedby"] = new EntityReference("systemuser", framed),
            ["sprk_steps"] = "{\"reviewId\":\"" + ConditionClearedRecordKey.IdOf(signalId, 1).ToString("D") + "\",\"steps\":[{\"code\":\"condition-cleared\"}]}",
        };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"closureRecorded\":1").And.Contain("\"closureRecordsLinked\":1");
        var genuine = h.Org.DecisionRecordRows.Keys.Should().ContainSingle(id => id != forged, "the genuine record is written").Subject;
        h.Org.DecisionRecordRows[genuine].GetAttributeValue<EntityReference>("sprk_confirmedby")!.Id.Should().Be(OtherUserId);
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(genuine, "the link names the genuine record");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_resolvedby")!.Id.Should().Be(OtherUserId, "and the person who did the work");
    }

    [Theory]
    [InlineData("userCreatedReminder")]
    [InlineData("writerCreatedResolvingRecord")]
    public async Task Run_WhenTheSkipMarkerIsEvicted_AForeignLinkToAnOlderRecord_IsStillNeverOverwritten(string foreignKind)
    {
        // K: the job's link met a foreign link (a record created before the close) and marked the closure skipped. The marker is then evicted.
        // The retry must not take that link for a D-128 reminder: it is either a person's row or a record that resolved the item.
        var (h, signalId) = ToDoCompletedByAColleague();
        var foreign = Guid.NewGuid();
        h.Sysadmin.DecisionRecords[foreign] = foreignKind == "userCreatedReminder"
            ? ForeignReminderRecord(foreign, createdBy: ConsoleUserId)
            : ForeignReminderRecord(foreign, createdBy: WriterOrg.WriterUserId, resolving: true);
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_decisionrecord") h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", foreign);
            return null;
        };

        var first = await h.RunAsync();
        first.ResultJson.Should().Contain("\"closureRecordSkips\":1", "precondition: the foreign link was a skip");

        h.Org.CreateFaultFor = null;
        await h.Cache.RemoveAsync(SignalReevaluationJob.ClosureRecordSkippedCacheKey(signalId, 1));
        h.Org.ResetCounts();
        h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "a prior link that is not a writer-created D-128 reminder or inquiry is foreign, whatever the marker says");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(foreign);
        h.Org.Rows[signalId].Contains("sprk_resolvedby").Should().BeFalse();
    }

    [Fact]
    public async Task Run_WhenAPersonReClosesTheSignalBeforeTheLink_TheRecordStaysUnlinked_AndASkipIsLoggedAndMetered()
    {
        // K1: the job closed the Signal and wrote its record; before the link, a person's decision re-closed it (Acted, with its own record).
        var (h, signalId) = ToDoCompletedByAColleague();
        var human = Guid.NewGuid();
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_decisionrecord")
            {
                h.Org.Rows[signalId]["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionActed);
                h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", human);
            }

            return null;
        };

        using var metrics = new MetricCapture();
        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Contain("\"closureRecorded\":1").And.Contain("\"closureRecordSkips\":1").And.Contain("\"closureRecordsLinked\":0");
        var record = h.Org.DecisionRecordRows.Keys.Should().ContainSingle().Subject;
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(human, "the human decision wins");
        h.Log.Should().ContainSingle(e => e.Message.Contains(OntologyWriterFailureReason.ClosureRecordSkippedSignalReclosed)
                                          && e.Message.Contains(record.ToString()))
            .Which.Level.Should().Be(LogLevel.Warning);
        metrics.Counter("ontology.writer.skips", "reason", OntologyWriterFailureReason.ClosureRecordSkippedSignalReclosed).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Run_AnUnlinkedClosureWhoseSubjectIsAHitAgain_LogsThatItsRecordIsAbandoned_OnceOnThatPass()
    {
        // K (silent abandon): the record write failed on the closing pass; before any retry succeeds the To Do is overdue again, so the
        // closure leaves the retry set (a new episode opens). That is logged at Warning on the pass it happens, not again after.
        var (h, signalId) = ToDoCompletedByAColleague();
        h.Org.CreateFaultFor = entity => entity.LogicalName == "sprk_decisionrecord" ? new InvalidOperationException("Simulated create failure.") : null;
        (await h.RunAsync()).ResultJson.Should().Contain("\"closureRecordFailures\":1", "precondition: the closure is unrecorded");

        h.Org.CreateFaultFor = null;
        h.WithTodo(TodoId, due: new DateTime(2026, 10, 1), owner: new EntityReference("systemuser", UserId)); // a hit again
        h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await h.RunAsync();
        h.Clock.Advance(TimeSpan.FromDays(1));
        var third = await h.RunAsync();

        second.Success.Should().BeTrue();
        third.Success.Should().BeTrue();
        h.Org.DecisionRecordRows.Should().BeEmpty("the closure is no longer retried");
        h.Log.Should().ContainSingle(e => e.EventId == DecisionRecordEvents.ConditionClearedSkipped && e.Message.Contains(signalId.ToString())
                                          && e.Message.Contains("hit again"))
            .Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task Run_ANextStepsOnlyReviewRecordLinked_IsReplacedByTheClosuresRecord_LikeAReminder()
    {
        // uac-r2 re-check K2: a review that took no action but created a Next step is InquirySent in the commit (the Signal stays Acknowledged
        // and linked to it). The retry must recognise that record by the same rule, or the closure's record is dropped on every pass.
        var (h, signalId, nextStepsId) = ToDoWithAReminderCompletedByAColleague();
        h.Sysadmin.DecisionRecords[nextStepsId]["sprk_steps"] = "{\"reviewId\":\"" + Guid.NewGuid().ToString("D") + "\",\"steps\":[{\"code\":\"mark-complete\",\"taken\":false}]}";
        h.Sysadmin.DecisionRecords[nextStepsId]["sprk_followons"] = "[{\"actionCode\":\"add-todo\",\"entity\":\"sprk_todo\",\"id\":\"" + Guid.NewGuid().ToString("D") + "\"}]";

        // The closing pass writes the record but its link is refused (D-61), so the Signal stays linked to the Next-steps-only record; only
        // the next pass's RETRY can replace that link.
        h.Org.TouchBeforeConditionalUpdateFor = target => target.Contains("sprk_decisionrecord");
        (await h.RunAsync()).ResultJson.Should().Contain("\"closureRecordsLinked\":0");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(nextStepsId, "precondition: still the old link");

        h.Org.TouchBeforeConditionalUpdateFor = null;
        h.Clock.Advance(TimeSpan.FromDays(1));
        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(record.Id, "the retry replaces the Next-steps-only link");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_resolvedby")!.Id.Should().Be(OtherUserId);
        result.ResultJson.Should().Contain("\"closureRecordsLinked\":1");
    }

    [Theory]
    [InlineData("userCreatedReminder")]
    [InlineData("writerCreatedResolvingRecord")]
    public async Task Run_AClosuresRetryWhosePriorLinkIsNotAWriterD128Link_IsLoggedAndMetered_NeverSilent(string priorKind)
    {
        // uac-r2 re-check K2: the retry is dropped (the link is foreign), and the drop says so: a Warning naming the Signal and the record,
        // and a skip on ontology.writer.skips.
        var (h, signalId) = ToDoCompletedByAColleague();
        var prior = Guid.NewGuid();
        h.Sysadmin.DecisionRecords[prior] = priorKind == "userCreatedReminder"
            ? ForeignReminderRecord(prior, createdBy: ConsoleUserId)
            : ForeignReminderRecord(prior, createdBy: WriterOrg.WriterUserId, resolving: true);
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_decisionrecord") h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", prior);
            return null;
        };
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Org.CreateFaultFor = null;
        await h.Cache.RemoveAsync(SignalReevaluationJob.ClosureRecordSkippedCacheKey(signalId, 1));
        h.Org.ResetCounts();
        h.Clock.Advance(TimeSpan.FromDays(1));

        using var metrics = new MetricCapture();
        var retry = await h.RunAsync();

        retry.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "the foreign link is not replaced");
        h.Log.Should().ContainSingle(e => e.Message.Contains(OntologyWriterFailureReason.ClosureRecordSkippedUnrecognisedPriorLink)
                                          && e.Message.Contains(signalId.ToString()) && e.Message.Contains(prior.ToString()))
            .Which.Level.Should().Be(LogLevel.Warning);
        metrics.Counter("ontology.writer.skips", "reason", OntologyWriterFailureReason.ClosureRecordSkippedUnrecognisedPriorLink).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Run_AForeignPriorLinkOnAClosuresRetry_WarnsAndMetersOnce_NotOnEveryNightlyPass()
    {
        // uac-r2 re-check 2 (K): the foreign-link skip sets the closure-skip marker (like the other permanent skips) and counts in the policy
        // summary, so the nights that follow stay quiet while the marker holds.
        var (h, signalId) = ToDoCompletedByAColleague();
        var prior = Guid.NewGuid();
        h.Sysadmin.DecisionRecords[prior] = ForeignReminderRecord(prior, createdBy: ConsoleUserId);
        h.Org.CreateFaultFor = entity =>
        {
            if (entity.LogicalName == "sprk_decisionrecord") h.Org.Rows[signalId]["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", prior);
            return null;
        };
        (await h.RunAsync()).Success.Should().BeTrue();
        h.Org.CreateFaultFor = null;
        await h.Cache.RemoveAsync(SignalReevaluationJob.ClosureRecordSkippedCacheKey(signalId, 1)); // no marker held for this closure yet
        h.Org.ResetCounts();
        h.Log.Clear();

        using var metrics = new MetricCapture();
        h.Clock.Advance(TimeSpan.FromDays(1));
        var night1 = await h.RunAsync();
        h.Clock.Advance(TimeSpan.FromDays(1));
        var night2 = await h.RunAsync();

        night1.ResultJson.Should().Contain("\"closureRecordSkips\":1", "the skip is in the policy summary");
        night2.ResultJson.Should().Contain("\"closureRecordSkips\":0", "and is not counted again");
        h.Org.WriteCalls.Should().Be(0);
        h.Log.Where(e => e.Message.Contains(OntologyWriterFailureReason.ClosureRecordSkippedUnrecognisedPriorLink)).Should().ContainSingle(
            "one warning across both nights").Which.Level.Should().Be(LogLevel.Warning);
        metrics.Counter("ontology.writer.skips", "reason", OntologyWriterFailureReason.ClosureRecordSkippedUnrecognisedPriorLink).Should().Be(1);
    }
}
