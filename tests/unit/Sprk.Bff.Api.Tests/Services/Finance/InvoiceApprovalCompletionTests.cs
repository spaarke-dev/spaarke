using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 165b-3 part A: approval completion decides only from server-written sources. Before completion writes the approve Decision
/// Record it re-runs the approval checks and requires the approved amount to equal the line-derived total (409
/// <c>ebilling.approve.completion_refused</c>, nothing written, one log line with the reason code, table and row id); "complete" is decided from
/// the writer-created approve record. Over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the real services.
/// </summary>
/// <remarks>Seed (recorded in <c>notes/165b-integrity-checks.md</c>): with the completion re-check removed from the claimant branch, every
/// C1 must-fire case approves and writes a record, so C1 fails.</remarks>
public sealed class InvoiceApprovalCompletionTests
{
    /// <summary>Two intake lines, Accepted: billed 1,000, approved total 1,000.</summary>
    private static ApprovalWorld World()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        w.AddLine(InvoiceLineStatus.Accepted, 600m, approved: 600m);
        return w;
    }

    /// <summary>The state completion sees after a transition: the invoice Approved, the portion Pending and naming <paramref name="contact"/>
    /// as decider with <paramref name="amount"/>, and no approve record. Set directly on the rows (no write is logged).</summary>
    private static void AsIfTransitioned(ApprovalWorld w, Guid contact, decimal amount)
    {
        w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);
        w.Portion["sprk_decidedby"] = new EntityReference("contact", contact);
        w.Portion["sprk_decidedon"] = Now.UtcDateTime;
        w.Portion["sprk_approvedamount"] = new Money(amount);
    }

    private static void AssertRefused(ApprovalWorld w, ApprovalResult result, string reasonCode, int writesBefore)
    {
        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.CompletionRefused);
        w.ApproveRecords.Should().BeEmpty("a refused completion writes no approve record");
        w.Writes.Should().Be(writesBefore, "a refused completion writes nothing");
        w.Log.Entries.Should().ContainSingle(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal))
            .Which.Should().Be((LogLevel.Warning, $"Approval completion refusal {reasonCode} on sprk_invoice {w.InvoiceId}"));
    }

    // ── C1: the approval checks run again at completion ──

    [Theory(DisplayName = "165b-3 C1 must fire: completion re-runs the approval checks; a failing check is refused (409 completion_refused) and no approve record is written")]
    [InlineData("eligibility", "ebilling.approver.not_eligible")]
    [InlineData("hold", "ebilling.approve.held")]
    [InlineData("firm", "ebilling.approve.firm_missing")]
    [InlineData("lines-open", "ebilling.approve.lines_open")]
    [InlineData("line-origin", "ebilling.approve.line_origin_unknown")]
    [InlineData("clean-undecided", InvoiceApprovalCodes.CompletionCleanUndecided)]
    public async Task C1_AFailingCheckAtCompletion_IsRefused(string check, string reasonCode)
    {
        var w = World();
        var caller = AliceUser;
        var contact = Alice;
        switch (check)
        {
            case "eligibility":
                (caller, contact) = (CarolUser, Carol); // Carol has a contact but is assigned nowhere
                break;
            case "hold":
                w.Hold();
                break;
            case "firm":
                w.WithoutFirm();
                break;
            case "lines-open":
                w.AddLine(InvoiceLineStatus.Flagged, 50m);
                break;
            case "line-origin":
                w.AddLine(InvoiceLineStatus.Accepted, 50m, approved: 50m, id: Guid.NewGuid());
                break;
            case "clean-undecided":
                w.AddLine(InvoiceLineStatus.Clean, 50m);
                break;
        }

        AsIfTransitioned(w, contact, 1000m);

        var result = await w.ApproveAs(caller);

        AssertRefused(w, result, reasonCode, writesBefore: 0);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Transitions.Should().Be(0);
    }

    [Fact(DisplayName = "165b-3 C1 control: an approve interrupted after the transition is completed by its approver's repeat and writes one record")]
    public async Task C1_Control_AnInterruptedApproval_Completes()
    {
        var w = World();
        w.TransitionThrowsAfterCommit = true;
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);
        w.ApproveRecords.Should().BeEmpty();

        var repeat = await w.ApproveAs(AliceUser);

        repeat.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle();
        w.Portion.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(1000m);
        w.Log.Entries.Should().NotContain(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal));
    }

    // ── C2: the approved amount equals the line-derived total ──

    [Fact(DisplayName = "165b-3 C2 must fire: completion is refused when the portion's approved amount differs from the line-derived total")]
    public async Task C2_AnAmountThatDiffersFromTheLines_IsRefused()
    {
        var forged = World();
        AsIfTransitioned(forged, Alice, 999m);

        AssertRefused(forged, await forged.ApproveAs(AliceUser), InvoiceApprovalCodes.CompletionAmountMismatch, writesBefore: 0);

        // The same after a real interruption before the record: the frozen amount changed since the claim.
        var interrupted = World();
        interrupted.FaultsOnce["identity"] = new InvalidOperationException("dataverse down"); // the recorder's contact read: no record written
        (await interrupted.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);
        interrupted.Portion["sprk_approvedamount"] = new Money(999m);
        var writes = interrupted.Writes;

        AssertRefused(interrupted, await interrupted.ApproveAs(AliceUser), InvoiceApprovalCodes.CompletionAmountMismatch, writes);
    }

    [Fact(DisplayName = "165b-3 C2 control: an approved amount equal to the line-derived total completes")]
    public async Task C2_Control_EqualAmounts_Complete()
    {
        var w = World();
        w.FaultsOnce["identity"] = new InvalidOperationException("dataverse down");
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);

        var repeat = await w.ApproveAs(AliceUser);

        repeat.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle();
    }

    // ── C3: "complete" is decided from the writer-created approve record ──

    [Theory(DisplayName = "165b-3 C3 must fire: a portion that says Approved and links a record the writer identity did not create is not complete - the completion checks run (and refuse here)")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task C3_ALinkToARecordNotCreatedByTheWriter_IsNotComplete(bool onBehalfOfTheWriter)
    {
        var w = World();
        var approved = await w.ApproveAs(AliceUser);
        approved.Outcome.Should().Be(ApprovalOutcome.Approved);

        // The same record, every column equal, but not the writer's own: created by another identity, or on the writer's behalf by one.
        var record = w.ApproveRecords.Single();
        w.H.Created.Remove(record);
        record.Id = w.H.NewId;
        record["createdby"] = new EntityReference("systemuser", onBehalfOfTheWriter ? DecisionRecordWriterTests.WriterUser : BobUser);
        if (onBehalfOfTheWriter)
        {
            record["createdonbehalfby"] = new EntityReference("systemuser", BobUser);
        }

        w.H.Seeded.Add(record);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(record.Id, "the setup: the portion links that row");
        w.AddLine(InvoiceLineStatus.Flagged, 50m);
        var writes = w.Writes;

        AssertRefused(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.LinesOpen, writes);
    }

    [Fact(DisplayName = "165b-3 C3 control: the writer's own approve record makes the approval complete - a repeat is a no-op")]
    public async Task C3_Control_TheWritersRecord_IsComplete()
    {
        var w = World();
        var approved = await w.ApproveAs(AliceUser);
        w.AddLine(InvoiceLineStatus.Flagged, 50m);
        var writes = w.Writes;

        var repeat = await w.ApproveAs(AliceUser);

        repeat.Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        repeat.DecisionRecordId.Should().Be(approved.DecisionRecordId);
        w.Writes.Should().Be(writes);
        w.ApproveRecords.Should().ContainSingle();
    }

    // ── C4: a completion replayed after it finished writes no second record ──

    [Fact(DisplayName = "165b-3 C4: completion replayed after it finished writes no second record, even when the portion's link was lost")]
    public async Task C4_ReplayAfterFinish_WritesNoSecondRecord()
    {
        var w = World();
        var first = await w.ApproveAs(AliceUser);
        first.Outcome.Should().Be(ApprovalOutcome.Approved);

        var writes = w.Writes;
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        w.Writes.Should().Be(writes, "a replay of a finished approval writes nothing");

        w.Portion.Attributes.Remove("sprk_decisionrecord");
        var relinked = await w.ApproveAs(AliceUser);

        relinked.Outcome.Should().Be(ApprovalOutcome.Approved);
        relinked.DecisionRecordId.Should().Be(first.DecisionRecordId, "the writer answers the recorded decision");
        w.ApproveRecords.Should().ContainSingle("no second record");
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(first.DecisionRecordId!.Value);
        w.Transitions.Should().Be(1);
    }
}
