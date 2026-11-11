using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 095d-2b-2 (ontology D-201 095d-2 rulings Q1 and Q2, T20): the writer's Decide-lane uses, over the real writer and the fake
/// organisation service. Q1: <see cref="SignalWriter.LinkConditionClearedRecordAsync"/> links the assign-firm record to the shared Signal the
/// re-run closed ConditionCleared (resolvedBy = the decider, no expected prior record), and leaves a foreign link or a Signal no longer
/// condition-cleared as it is. T20: the lifecycle's Superseded close writes no Decision Record. Q2: a stored human decision's set that names a
/// Signal the re-run superseded is closed without a failure, so its retry does not loop.
/// </summary>
public partial class SignalWriterTests
{
    private const string SystemPolicy = "BILL-SYSTEM-SUBMISSION";

    [Fact(DisplayName = "095d-2b-2 (Q1): a Decide-lane Signal closed ConditionCleared is linked to the assign-firm record with sprk_resolvedby = the decider, in one conditional write")]
    public async Task DecideLaneLink_ConditionCleared_IsLinked_WithTheDecider()
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", "Condition Cleared", SetReadAt);

        var outcome = await writer.LinkConditionClearedRecordAsync(ClosedSignalId, AutoRecordId, expectedPriorRecordId: null, DeciderUserId, SystemPolicy);

        outcome.Should().Be(SignalRecordLinkOutcome.Linked);
        var (id, fields) = org.ConditionalUpdates.Should().ContainSingle().Subject;
        id.Should().Be(ClosedSignalId);
        fields.Keys.Should().BeEquivalentTo("sprk_decisionrecord", "sprk_resolvedby");
        fields["sprk_decisionrecord"].Should().Be(new EntityReference("sprk_decisionrecord", AutoRecordId));
        fields["sprk_resolvedby"].Should().Be(new EntityReference("systemuser", DeciderUserId));
    }

    [Fact(DisplayName = "095d-2b-2 (Q1): a Decide-lane Signal that names another record is a ForeignLink - nothing is written")]
    public async Task DecideLaneLink_AnotherRecord_IsAForeignLink()
    {
        var (writer, org, _) = AutomatedCloseRig("Resolved", "Condition Cleared", SetReadAt, OtherRecordId);

        var outcome = await writer.LinkConditionClearedRecordAsync(ClosedSignalId, AutoRecordId, expectedPriorRecordId: null, DeciderUserId, SystemPolicy);

        outcome.Should().Be(SignalRecordLinkOutcome.ForeignLink);
        org.ConditionalUpdates.Should().BeEmpty();
        org.UpdateCalls.Should().BeEmpty();
    }

    [Theory(DisplayName = "095d-2b-2 (Q1 must-not-fire): a Decide-lane Signal no longer condition-cleared (a person's Acted, Superseded, open again) is NotApplicable - nothing is written")]
    [InlineData("Resolved", "Acted")]
    [InlineData("Resolved", "Superseded")]
    [InlineData("Open", null)]
    public async Task DecideLaneLink_NotConditionCleared_IsNotApplicable(string status, string? resolution)
    {
        var (writer, org, _) = AutomatedCloseRig(status, resolution, resolution is null ? null : SetReadAt);

        var outcome = await writer.LinkConditionClearedRecordAsync(ClosedSignalId, AutoRecordId, expectedPriorRecordId: null, DeciderUserId, SystemPolicy);

        outcome.Should().Be(SignalRecordLinkOutcome.NotApplicable);
        org.ConditionalUpdates.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-2b-2 (T20): the lifecycle's Superseded close writes no sprk_decisionrecord and no sprk_resolvedby")]
    public async Task SupersededClose_WritesNoRecord()
    {
        var (writer, org, _) = AutomatedCloseRig("Open");

        var outcome = await InvoiceSubmissionAfterSteps.SupersedeClose(writer)(ClosedSignalId, "1", SystemPolicy, CancellationToken.None);

        outcome.Should().Be(SignalCloseOutcome.Closed);
        var (_, fields) = org.ConditionalUpdates.Should().ContainSingle().Subject;
        fields["sprk_resolutiontype"].Should().Be(new OptionSetValue(Resolution("Superseded")));
        fields.Should().NotContainKey("sprk_decisionrecord");
        fields.Should().NotContainKey("sprk_resolvedby");
    }

    [Theory(DisplayName = "095d-2b-2 (Q2): a stored Override or Return set naming a Signal the re-run superseded closes with no failure (no 503 loop): before the set was read it is left as it is, after it is re-closed Acted")]
    [InlineData(-1, SignalAutomatedCloseOutcome.LeftAsIs)]
    [InlineData(1, SignalAutomatedCloseOutcome.Closed)]
    public async Task ASupersededSignalInAStoredSet_ClosesWithNoFailure(int minutesAfterRead, SignalAutomatedCloseOutcome expected)
    {
        var (writer, _, _) = AutomatedCloseRig("Resolved", "Superseded", SetReadAt.AddMinutes(minutesAfterRead));
        var outcomes = new List<SignalAutomatedCloseOutcome>();
        HumanDecisionSignalClose.CloseActed close = async (id, record, readAt, code, by, ct) =>
        {
            var outcome = await writer.CloseActedByDecisionAsync(id, record, readAt, code, by, ct);
            outcomes.Add(outcome);
            return outcome;
        };
        var set = new InvoiceSubmissionAfterSteps.ResolvedSignalSet(SetReadAt, [new InvoiceSubmissionAfterSteps.ResolvedSignal(ClosedSignalId, SystemPolicy)]);

        var failed = await HumanDecisionSignalClose.CloseSetAsync(close, set, AutoRecordId, DeciderUserId, NullLogger.Instance, CancellationToken.None);

        failed.Should().Be(0, "a superseded Signal in the set is never a failure, so the decision's retry does not answer 503 again");
        outcomes.Should().Equal(expected);
    }
}
