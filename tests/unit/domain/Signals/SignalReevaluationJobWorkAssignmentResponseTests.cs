using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 061 (D-54) end to end at unit level: the REAL job, evaluator, compiler, writer and closure service over the in-memory
/// Dataverse seams of <see cref="SignalReevaluationJobTests"/>. An active work assignment past <c>sprk_responseduedate</c> with
/// <c>sprk_respondedon</c> null raises a Signal; once a response is recorded the next pass closes it ConditionCleared (and, task 153,
/// records the closure in one Routine Decision Record), and a response recorded BEFORE the first pass raises nothing.
/// </summary>
/// <remarks>
/// The seam models the null test the way Dataverse answers it (<c>HoldsNullTests</c>: a root-filter <c>null</c> / <c>not-null</c> with
/// no <c>entityname</c>). The compiled operator itself is pinned in <see cref="NullTestGrammarTests"/>; the live behaviour of the
/// FetchXML is pinned by <c>DoLanePolicySeamTests</c> against spaarkedev1.
/// </remarks>
public partial class SignalReevaluationJobTests
{
    private const string WorkAssignmentPolicyCode = "POL-WA-RESPONSE";
    private static readonly Guid WorkAssignmentId = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000000e1");

    private static Harness WithWorkAssignmentResponsePolicy()
    {
        var h = new Harness();
        h.Sysadmin.SubjectRows["sprk_workassignment"] = new List<Entity>();
        h.Sysadmin.AddPolicy(
            WorkAssignmentPolicyCode, SignalWriter.LaneDo, true, NullTestGrammarTests.WorkAssignmentNoResponseBody,
            "This work assignment is past its response date and no response is recorded.");
        return h;
    }

    private static Entity AddWorkAssignment(Harness h, DateTime responseDue, DateTime? respondedOn = null, int statecode = 0)
    {
        var row = new Entity("sprk_workassignment", WorkAssignmentId)
        {
            ["sprk_workassignmentid"] = WorkAssignmentId,
            ["sprk_name"] = "Send the engagement letter",
            ["sprk_responseduedate"] = responseDue,
            ["statecode"] = new OptionSetValue(statecode),
            ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId),
        };
        if (respondedOn is { } on) row["sprk_respondedon"] = on;
        h.Sysadmin.Stub(row);
        h.Sysadmin.SubjectRows["sprk_workassignment"].Add(row);
        return row;
    }

    [Fact]
    public async Task Run_ActiveWorkAssignmentPastItsResponseDateWithNoResponse_RaisesASignalGroupedUnderTheAssignmentItself()
    {
        var h = WithWorkAssignmentResponsePolicy();
        AddWorkAssignment(h, responseDue: new DateTime(2026, 10, 1));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        var created = h.Org.CreatedPayloads.Should().ContainSingle().Subject;
        created.GetAttributeValue<DateTime?>("sprk_duedate").Should().Be(new DateTime(2026, 10, 1), "D-27/D-33: the response due date");
        created.GetAttributeValue<string>("sprk_dedupekey").Should().StartWith($"{WorkAssignmentPolicyCode}|sprk_workassignment|{Lower(WorkAssignmentId)}|1");
        created.GetAttributeValue<string>("sprk_corerecordid").Should().Be(Lower(WorkAssignmentId), "D-36: a work assignment is its own core record");
    }

    [Fact]
    public async Task Run_WorkAssignmentWhoseResponseIsRecordedAfterTheSignal_ClosesItConditionCleared_AndRecordsTheClosure()
    {
        var h = WithWorkAssignmentResponsePolicy();
        var assignment = AddWorkAssignment(h, responseDue: new DateTime(2026, 10, 1));
        await h.RunAsync();
        var signalId = h.Org.Rows.Keys.Single();
        h.Org.ResetCounts();

        assignment["sprk_respondedon"] = new DateTime(2026, 10, 9); // record-the-response writes this column (task 044)
        var second = await h.RunAsync();

        second.Success.Should().BeTrue();
        h.Org.ConditionalUpdates.Should().HaveCount(2, "the close, then (task 153) the link to its Decision Record");
        var update = h.Org.ConditionalUpdates[0];
        update.Id.Should().Be(signalId);
        update.Fields["sprk_signalstatus"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.SignalStatusResolved));
        update.Fields["sprk_resolutiontype"].Should().BeEquivalentTo(new OptionSetValue(SignalWriter.ResolutionConditionCleared));
        update.Fields.Should().NotContainKey("sprk_decisionrecord", "the close itself writes no Decision Record (FR-16)");
        // Task 153 (D-145b): a Do-lane ConditionCleared closure gets ONE Routine record, filed under the assignment, linked to the Signal.
        var record = h.Org.DecisionRecordRows.Values.Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("sprk_regardingworkassignment")!.Id.Should().Be(WorkAssignmentId);
        h.Org.ConditionalUpdates[1].Fields["sprk_decisionrecord"].Should().BeEquivalentTo(new EntityReference("sprk_decisionrecord", record.Id));
        h.Org.CreateCalls.Should().Be(0, "no Signal is raised");
    }

    [Fact]
    public async Task Run_WorkAssignmentThatAlreadyHasAResponse_RaisesNothing()
    {
        var h = WithWorkAssignmentResponsePolicy();
        AddWorkAssignment(h, responseDue: new DateTime(2026, 10, 1), respondedOn: new DateTime(2026, 10, 9));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue();
        h.Org.CreateCalls.Should().Be(0, "D-54: a recorded response is not a missing one");
    }
}
