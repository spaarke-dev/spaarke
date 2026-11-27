using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology task 233 (D-232, #3275, DI-177): the ConditionCleared Decision Record the nightly pass writes derives its privilege flag with the
/// one shared rule (<see cref="WorkItemPrivilege"/>, D-212) over the closed Signal's subject and stored evidence communications, read with
/// the job's application identity and failing closed. It is never copied from <c>sprk_signal.sprk_privilegeflagged</c>, which nothing writes
/// (DI-64). The REAL job and Decision Record writer over the in-memory Dataverse seams of <see cref="SignalReevaluationJobTests"/>.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private const int ClassificationNone = 100000000;
    private const int ClassificationPotentiallyPrivileged = 100000001;
    private const int ClassificationPrivileged = 100000002;

    /// <summary>Evidence lines as the evaluator writes a communication witness (kind, ref = "{table}:{id}", tier).</summary>
    private static string CommunicationEvidence(params Guid[] communications) =>
        JsonSerializer.Serialize(communications.Select(c => new { kind = "communication", @ref = $"sprk_communication:{Lower(c)}", tier = "Fact" }));

    /// <summary>The colleague-completed To Do closure (<see cref="ToDoCompletedByAColleague"/>), its Signal carrying one evidence
    /// communication classified <paramref name="classification"/> (null: the column is empty).</summary>
    private static (Harness H, Guid SignalId, Guid CommunicationId) ClosureWithEvidence(int? classification)
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        var communicationId = Guid.NewGuid();
        h.Org.Rows[signalId]["sprk_evidencerefs"] = CommunicationEvidence(communicationId);
        h.Sysadmin.Stub(new Entity("sprk_communication", communicationId)
        {
            ["sprk_privilegeclassification"] = classification is { } value ? new OptionSetValue(value) : null,
        });
        return (h, signalId, communicationId);
    }

    private static bool RecordFlag(Harness h) =>
        h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject.GetAttributeValue<bool>("sprk_privilegeflagged");

    [Theory]
    [InlineData(ClassificationPrivileged)]
    [InlineData(ClassificationPotentiallyPrivileged)]
    public async Task Run_ClosureWhoseEvidenceCommunicationIsPrivileged_WritesARecordThatIsPrivilegeFlagged(int classification)
    {
        var (h, signalId, communicationId) = ClosureWithEvidence(classification);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        RecordFlag(h).Should().BeTrue("D-232: the flag is derived from the Signal's evidence communications (D-212's rule), app-only");
        h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(h.Org.DecisionRecordRows.Keys.Single());
        var read = h.Sysadmin.PrivilegeReads.Should().ContainSingle().Subject;
        read.ColumnSet.Columns.Should().BeEquivalentTo("sprk_communicationid", "sprk_privilegeclassification");
        read.Criteria.Conditions.Single().Values.Should().BeEquivalentTo(new object[] { communicationId });
    }

    [Theory]
    [InlineData(ClassificationNone)]
    [InlineData(null)]
    public async Task Run_ClosureWhoseEvidenceCommunicationIsNoneOrUnclassified_WritesARecordThatIsNotFlagged(int? classification)
    {
        var (h, _, _) = ClosureWithEvidence(classification);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        RecordFlag(h).Should().BeFalse("None and null are not privileged (FR-04)");
        h.Sysadmin.PrivilegeReads.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_TheSignalsOwnPrivilegeColumn_IsNeitherReadNorCopied()
    {
        // DI-64: nothing writes sprk_signal.sprk_privilegeflagged. Seeded true here with no communication evidence: the record must not copy
        // it, and no read of the Signal selects it.
        var (h, signalId) = ToDoCompletedByAColleague();
        h.Org.Rows[signalId]["sprk_privilegeflagged"] = true;
        var signalSelects = new List<string>();
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_signal") signalSelects.AddRange(q.ColumnSet.Columns);
            return null;
        };

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        RecordFlag(h).Should().BeFalse("the record's flag is derived, never copied from the unwritten Signal column");
        signalSelects.Should().NotBeEmpty("precondition: the job read the Signals").And.NotContain("sprk_privilegeflagged");
        h.Sysadmin.PrivilegeReads.Should().BeEmpty("a closure that names no communication is never read (no round trip)");
    }

    [Fact]
    public async Task Run_WhenThePrivilegeReadFaults_TheRecordIsFlagged_AndTheClosureStillCompletes()
    {
        var (h, signalId, _) = ClosureWithEvidence(ClassificationNone);
        h.Sysadmin.FaultOn = q => q.EntityName == "sprk_communication" && q.ColumnSet.Columns.Contains("sprk_privilegeclassification")
            ? new TimeoutException("Simulated read timeout.")
            : null;

        var result = await h.RunAsync();

        result.Success.Should().BeTrue("the flag is record accuracy only (ADR-015): a fault never fails the closure");
        RecordFlag(h).Should().BeTrue("fail closed (D-212): a read fault flags the record");
        var signal = h.Org.Rows[signalId];
        signal.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionConditionCleared);
        signal.GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id.Should().Be(h.Org.DecisionRecordRows.Keys.Single());
        result.ResultJson.Should().Contain("\"closureRecorded\":1").And.Contain("\"closureRecordsLinked\":1");
    }

    [Fact]
    public async Task Run_AnEvidenceCommunicationThatNoLongerExists_FlagsTheRecord()
    {
        var (h, signalId) = ToDoCompletedByAColleague();
        h.Org.Rows[signalId]["sprk_evidencerefs"] = CommunicationEvidence(Guid.NewGuid()); // never stubbed: the read does not return it

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        RecordFlag(h).Should().BeTrue("a communication the read does not return cannot be judged: flagged (fail closed, as WorkItemPrivilege)");
    }

    [Fact]
    public async Task Run_TwoClosuresInOnePass_AreJudgedWithOneBatchedRead_EachOnItsOwnEvidence()
    {
        var (h, firstSignal, _) = ClosureWithEvidence(ClassificationPrivileged);
        var secondTodo = Guid.NewGuid();
        h.Sysadmin.Stub(new Entity("sprk_todo", secondTodo)
        {
            ["sprk_todoid"] = secondTodo,
            ["sprk_name"] = "Send the draft",
            ["sprk_duedate"] = new DateTime(2026, 10, 1),
            ["ownerid"] = new EntityReference("systemuser", UserId),
        });
        h.Sysadmin.LastChange[secondTodo] = (OtherUserId, RunAt.UtcDateTime.AddDays(-1));
        var secondSignal = h.Org.SeedSignal(Key(TodoPolicyCode, "sprk_todo", secondTodo, 1), SignalWriter.SignalStatusOpen,
            policyId: h.Sysadmin.PolicyIdOf(TodoPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(TodoPolicyCode));
        h.Org.Rows[secondSignal]["createdon"] = RunAt.UtcDateTime.AddDays(-3);
        var plain = Guid.NewGuid();
        h.Org.Rows[secondSignal]["sprk_evidencerefs"] = CommunicationEvidence(plain);
        h.Sysadmin.Stub(new Entity("sprk_communication", plain) { ["sprk_privilegeclassification"] = new OptionSetValue(ClassificationNone) });

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.DecisionRecordRows.Should().HaveCount(2);
        h.Sysadmin.PrivilegeReads.Should().ContainSingle("one batched read per policy pass, not one per closure");
        FlagOfRecordLinkedTo(h, firstSignal).Should().BeTrue();
        FlagOfRecordLinkedTo(h, secondSignal).Should().BeFalse();
    }

    [Fact]
    public async Task Run_AgainAfterAFlaggedClosureIsRecorded_WritesNothingNew_AndReadsNoClassification()
    {
        var (h, _, _) = ClosureWithEvidence(ClassificationPrivileged);
        await h.RunAsync();
        h.Org.ResetCounts();
        h.Sysadmin.PrivilegeReads.Clear();
        h.Clock.Advance(TimeSpan.FromDays(1));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.WriteCalls.Should().Be(0, "D-145b / D-61: a linked closure is never touched again");
        RecordFlag(h).Should().BeTrue();
        h.Sysadmin.PrivilegeReads.Should().BeEmpty("a linked closure is not a candidate: nothing is read for it");
    }

    private static bool FlagOfRecordLinkedTo(Harness h, Guid signalId) =>
        h.Org.DecisionRecordRows[h.Org.Rows[signalId].GetAttributeValue<EntityReference>("sprk_decisionrecord")!.Id]
            .GetAttributeValue<bool>("sprk_privilegeflagged");
}
