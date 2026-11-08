using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
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

    [Fact(DisplayName = "165b-3 C2 control (fix round F3): a real interruption with a Reduced line and a Percent adjustment line - the repeat completes and records the calculator's total")]
    public async Task C2_Control_WithAdjustments_RepeatCompletesAndRecordsTheCalculatorTotal()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Reduced, 400m, approved: 300m);
        w.AddLine(InvoiceLineStatus.Accepted, 600m, approved: 600m);
        var adjustment = w.AddLine(InvoiceLineStatus.Accepted, 0m, InvoiceLineCostType.Adjustment);
        adjustment["sprk_adjustmentmethod"] = new OptionSetValue((int)InvoiceAdjustmentMethod.Percent);
        adjustment["sprk_adjustmentvalue"] = 10m;
        adjustment["statecode"] = new OptionSetValue(0); // Active: the line read keeps an Adjustment line only when its statecode is Active
        w.StoreTotalsFromLines(); // Task 165b-2: the adjustment service recalculates after it writes the line

        w.FaultsOnce["identity"] = new InvalidOperationException("dataverse down"); // the recorder's contact read: no record written
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);
        w.ApproveRecords.Should().BeEmpty();

        var repeat = await w.ApproveAs(AliceUser);

        // Billed 1,000; the Reduced line takes 100 off, so the fee base is 900 and 10% of it (90) is taken: 810 (D-42).
        repeat.Outcome.Should().Be(ApprovalOutcome.Approved, repeat.Detail);
        w.ApproveRecords.Should().ContainSingle();
        w.Portion.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(810m);
        w.ApproveRecords.Single().GetAttributeValue<string>("sprk_factsnapshot").Should().Contain("810");
        w.Log.Entries.Should().NotContain(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal));
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

    [Theory(DisplayName = "165b-3 C3 must fire (fix round F1): a portion that says Approved on an invoice that is not Approved, with no writer-created approve record, is refused (409 completion_refused), never answered as already approved; nothing is written")]
    [InlineData(InvoiceStatusCodes.InReview)]
    [InlineData(InvoiceStatusCodes.SentToAp)]
    public async Task C3_PortionApproved_InvoiceNotApproved_NoRecord_IsRefused(int invoiceStatus)
    {
        var w = World();
        w.Invoice["statuscode"] = new OptionSetValue(invoiceStatus);
        w.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionApproved);

        var result = await w.ApproveAs(AliceUser);

        AssertRefused(w, result, InvoiceApprovalCodes.CompletionStatusWithoutRecord, writesBefore: 0);
        w.Invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(invoiceStatus);
        w.Transitions.Should().Be(0);
    }

    [Fact(DisplayName = "165b-3 C3 (fix round F1): the approval state does not report Approved from the portion's status alone; a real approval does")]
    public async Task C3_TheState_DoesNotReportApprovedFromThePortionAlone()
    {
        var w = World();
        w.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionApproved);
        w.Portion["sprk_approvedamount"] = new Money(1000m);

        var forged = await w.GetAs(AliceUser);

        forged!.Status.Should().NotBe("Approved");
        forged.ApprovedAmount.Should().BeNull();
        forged.CanApprove.Should().BeFalse();

        var real = World();
        (await real.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        var state = await real.GetAs(AliceUser);
        state!.Status.Should().Be("Approved");
        state.ApprovedAmount.Should().Be(1000m);
    }

    [Fact(DisplayName = "165b-3 C3 must fire (fix round K4): a portion that links a different writer-created record (another invoice's) is not complete - the completion checks run (and refuse here)")]
    public async Task C3_ALinkToAnotherInvoicesWriterRecord_IsNotComplete()
    {
        var other = World();
        (await other.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        var othersRecord = other.ApproveRecords.Single();

        var w = World();
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        var own = w.ApproveRecords.Single();
        own.Id.Should().NotBe(othersRecord.Id, "the setup: each created row has its own id");
        w.H.Created.Remove(own);
        w.H.Created.Add(othersRecord);
        w.Portion["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", othersRecord.Id);
        w.AddLine(InvoiceLineStatus.Flagged, 50m);
        var writes = w.Writes;

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.CompletionRefused);
        w.Writes.Should().Be(writes);
        w.Log.Entries.Should().ContainSingle(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal))
            .Which.Should().Be((LogLevel.Warning, $"Approval completion refusal ebilling.approve.lines_open on sprk_invoice {w.InvoiceId}"));
    }

    // ── F2 (fix round): the decision time is the portion's user-writable column ──

    [Theory(DisplayName = "165b-3 F2 must fire: completion is refused (409 completion_refused, decided_on_invalid) when the portion's decision time is missing or later than now plus the lock duration; nothing is written")]
    [InlineData("missing")]
    [InlineData("future")]
    public async Task F2_ADecidedOnThatIsMissingOrInTheFuture_IsRefused(string kind)
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        if (kind == "missing")
        {
            w.Portion.Attributes.Remove("sprk_decidedon");
        }
        else
        {
            w.Portion["sprk_decidedon"] = w.Clock.GetUtcNow().UtcDateTime + InvoiceApprovalService.LockDuration + TimeSpan.FromMinutes(1);
        }

        AssertRefused(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.CompletionDecidedOnInvalid, writesBefore: 0);
    }

    [Fact(DisplayName = "165b-3 F2 control: a decision time in the past completes and writes one record")]
    public async Task F2_Control_ADecidedOnInThePast_Completes()
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        w.Portion["sprk_decidedon"] = w.Clock.GetUtcNow().UtcDateTime.AddHours(-2);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, result.Detail);
        w.ApproveRecords.Should().ContainSingle();
        w.Log.Entries.Should().NotContain(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal));
    }

    // ── K3 (fix round): an "already recorded" answer for a decision with no writer-created record ──

    [Fact(DisplayName = "165b-3 K3: the writer answering 'already recorded' for a decision with no writer-created record found is a refusal (record_not_writer_created), not a completion")]
    public async Task K3_AlreadyRecordedWithNoWriterRecordFound_IsRefused()
    {
        var w = World();
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);

        // The record the writer will find is the writer's by createdby but created on its behalf by another identity: the completion's lookup
        // (createdby and createdonbehalfby) does not count it, the writer's own "already recorded" lookup (createdby alone) does.
        var record = w.ApproveRecords.Single();
        w.H.Created.Remove(record);
        record["createdby"] = new EntityReference("systemuser", DecisionRecordWriterTests.WriterUser);
        record["createdonbehalfby"] = new EntityReference("systemuser", BobUser);
        w.H.Seeded.Add(record);
        w.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionPending);
        w.Portion.Attributes.Remove("sprk_decisionrecord");

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.CompletionRefused);
        w.H.Created.Should().NotContain(r => r.GetAttributeValue<string>("sprk_actioncode") == "approve-invoice", "no second record is written");
        w.Portion.Contains("sprk_decisionrecord").Should().BeFalse("the record the writer named is not linked");
        w.Log.Entries.Should().ContainSingle(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal))
            .Which.Should().Be((LogLevel.Warning, $"Approval completion refusal {InvoiceApprovalCodes.CompletionRecordNotWriterCreated} on sprk_invoice {w.InvoiceId}"));
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
    // ── Part B (EB-D-208): an invoice counts as Approved, Sent to AP or Paid only with the writer-created approve record ──

    /// <summary>A real approval whose approve record is then replaced by the same row created by another identity, or on the writer's behalf by
    /// one (every other column equal). The invoice is Approved and the portion says Approved and links that row.</summary>
    private static async Task<ApprovalWorld> ApprovedWithForeignRecord(bool onBehalfOfTheWriter)
    {
        var w = World();
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        var record = w.ApproveRecords.Single();
        w.H.Created.Remove(record);
        record["createdby"] = new EntityReference("systemuser", onBehalfOfTheWriter ? DecisionRecordWriterTests.WriterUser : BobUser);
        if (onBehalfOfTheWriter)
        {
            record["createdonbehalfby"] = new EntityReference("systemuser", BobUser);
        }

        w.H.Seeded.Add(record);
        return w;
    }

    [Theory(DisplayName = "165b-3 B1 must fire: the approval state does not report Approved for an Approved or Sent to AP invoice with no writer-created approve record")]
    [InlineData(InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceStatusCodes.SentToAp)]
    public async Task B1_TheState_NeedsTheWritersRecord(int invoiceStatus)
    {
        var w = World();
        w.Invoice["statuscode"] = new OptionSetValue(invoiceStatus);
        w.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionApproved);
        w.Portion["sprk_approvedamount"] = new Money(1000m);
        w.Portion["sprk_decidedby"] = new EntityReference("contact", Alice);

        var state = await w.GetAs(AliceUser);

        state!.Status.Should().NotBe("Approved");
        state.ApprovedAmount.Should().BeNull();
        state.DecidedBy.Should().BeNull();
    }

    [Fact(DisplayName = "165b-3 B1 control: after a real approval the state reports Approved, and still does once the invoice is Sent to AP")]
    public async Task B1_Control_TheState_WithTheWritersRecord_IsApproved()
    {
        var w = World();
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        (await w.GetAs(AliceUser))!.Status.Should().Be("Approved");

        w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.SentToAp);
        var state = await w.GetAs(AliceUser);
        state!.Status.Should().Be("Approved");
        state.ApprovedAmount.Should().Be(1000m);
    }

    [Fact(DisplayName = "165b-3 B1 must fire: another approver's approve on an Approved invoice with no writer-created approve record is refused (409 claimed), never answered as already approved; nothing is written")]
    public async Task B1_AnotherApprover_NoRecord_IsRefused()
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);

        var result = await w.ApproveAs(BobUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.Claimed);
        w.Writes.Should().Be(0, "nothing is written, not even the portion's status");
        w.ApproveRecords.Should().BeEmpty();
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
    }

    [Fact(DisplayName = "165b-3 B1 must fire: an Approved invoice with no claim and no writer-created approve record is refused (completion_refused, status_without_record); nothing is written")]
    public async Task B1_NoClaim_NoRecord_IsRefused()
    {
        var w = World();
        w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);

        AssertRefused(w, await w.ApproveAs(BobUser), InvoiceApprovalCodes.CompletionStatusWithoutRecord, writesBefore: 0);
    }

    [Fact(DisplayName = "165b-3 B1 control: another approver's approve after a real approval is a no-op (already approved)")]
    public async Task B1_Control_AnotherApprover_WithTheWritersRecord_IsANoOp()
    {
        var w = World();
        var approved = await w.ApproveAs(AliceUser);
        var writes = w.Writes;

        var result = await w.ApproveAs(BobUser);

        result.Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        result.DecisionRecordId.Should().Be(approved.DecisionRecordId);
        w.Writes.Should().Be(writes);
    }

    [Theory(DisplayName = "165b-3 R3 (approval) must fire: an approve record created by another identity, or on the writer's behalf by one, does not make the state Approved")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R3_TheState_ARecordNotCreatedByTheWriter_DoesNotCount(bool onBehalfOfTheWriter)
    {
        var w = await ApprovedWithForeignRecord(onBehalfOfTheWriter);

        var state = await w.GetAs(AliceUser);

        state!.Status.Should().NotBe("Approved");
        state.ApprovedAmount.Should().BeNull();
    }

    [Fact(DisplayName = "165b-3 R3 (approval) control: the writer's own approve record makes the state Approved")]
    public async Task R3_Control_TheState_TheWritersRecord_Counts()
    {
        var w = World();
        // The harness stores the writer's rows as Dataverse does: createdby the writer identity, createdonbehalfby empty.
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);

        (await w.GetAs(AliceUser))!.Status.Should().Be("Approved");
    }

    [Fact(DisplayName = "165b-3 part B F2 lower bound must fire: a decision time earlier than the invoice's createdon is refused (decided_on_invalid); nothing is written")]
    public async Task F2_ADecidedOnBeforeTheInvoiceWasCreated_IsRefused()
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        w.Portion["sprk_decidedon"] = w.Invoice.GetAttributeValue<DateTime>("createdon").AddMinutes(-1);

        AssertRefused(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.CompletionDecidedOnInvalid, writesBefore: 0);
    }

    [Fact(DisplayName = "165b-3 part B F2 lower bound control: a decision time after the invoice's createdon completes")]
    public async Task F2_Control_ADecidedOnAfterTheInvoiceWasCreated_Completes()
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        w.Portion["sprk_decidedon"] = w.Invoice.GetAttributeValue<DateTime>("createdon").AddMinutes(1);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, result.Detail);
        w.ApproveRecords.Should().ContainSingle();
    }

    // ── Fix round (uac-r2 note a): the currency, one check at approve (before the claim) and at completion ──

    [Theory(DisplayName = "165b-3 fix round currency control: a currency with 2 decimals or fewer, a bare $ (D-119) and a missing currency (USD, D-115) approve")]
    [InlineData("$")]
    [InlineData("AUD")]
    [InlineData("JPY")]
    [InlineData("usd")]
    [InlineData(null)]
    public async Task Currency_Control_Approves(string? currency)
    {
        var w = World();
        if (currency is not null)
        {
            w.Invoice["sprk_currency"] = currency;
        }

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle();
    }

    [Theory(DisplayName = "165b-3 fix round currency control: completion of an interrupted approve completes for $, AUD, JPY and a missing currency")]
    [InlineData("$")]
    [InlineData("AUD")]
    [InlineData("JPY")]
    [InlineData(null)]
    public async Task Currency_Control_Completes(string? currency)
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        if (currency is not null)
        {
            w.Invoice["sprk_currency"] = currency;
        }

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle();
    }

    [Theory(DisplayName = "165b-3 fix round currency must fire: a currency with more than 2 decimals (KWD, BHD) or text that is not a currency code is refused at approve (409 currency_unsupported) before the claim; nothing is written")]
    [InlineData("KWD")]
    [InlineData("bhd")]
    [InlineData("CA$")]
    [InlineData("EURO")]
    public async Task Currency_MoreThanTwoDecimals_IsRefusedAtApprove_NothingClaimed(string currency)
    {
        var w = World();
        w.Invoice["sprk_currency"] = currency;

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.CurrencyUnsupported);
        w.Writes.Should().Be(0, "nothing is claimed or written");
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Should().BeNull("the claim is never written");
        w.Transitions.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();
    }

    [Fact(DisplayName = "165b-3 fix round currency must fire: completion runs the same check - KWD is refused (completion_refused, currency_unsupported); nothing is written")]
    public async Task Currency_MoreThanTwoDecimals_IsRefusedAtCompletion()
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        w.Invoice["sprk_currency"] = "KWD";

        AssertRefused(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.CurrencyUnsupported, writesBefore: 0);
    }

    // ── Fix round (uac-r2 F1): the state counts the record only under the approval's own key ──

    [Theory(DisplayName = "165b-3 fix round (uac-r2 F1): the approval state counts a writer-created approve record only under the approval's own key - another key shows Unknown (must fire), the own key Approved (control)")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeyMismatch_TheState(bool anotherKey)
    {
        var w = World();
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        var record = w.ApproveRecords.Single();
        var key = BillingDecisionKey.IdOf("sprk_invoice", w.InvoiceId, InvoiceApprovalService.DecisionIdFor(w.ApprovalId)).ToString("D");
        var steps = record.GetAttributeValue<string>("sprk_steps");
        steps.Should().Contain(key, "the writer stores the approval's key");
        if (anotherKey)
        {
            record["sprk_steps"] = steps.Replace(key, Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        }

        var state = await w.GetAs(AliceUser);

        if (anotherKey)
        {
            state!.Status.Should().Be("Unknown");
            state.ApprovedAmount.Should().BeNull();
        }
        else
        {
            state!.Status.Should().Be("Approved");
        }
    }

    // ── Fix round (review F1, low): the transition answers "already in target" ──

    [Fact(DisplayName = "165b-3 fix round must fire: the status moved to Approved outside this call and no approve record exists - the transition's 'already in target' is Incomplete at transition, and the claimant's repeat completes with one record")]
    public async Task AlreadyInTarget_NoRecord_IsIncomplete()
    {
        var w = World();
        w.BeforeClaim = () =>
        {
            w.BeforeClaim = null;
            w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);
            return Task.CompletedTask;
        };

        var first = await w.ApproveAs(AliceUser);

        first.Outcome.Should().Be(ApprovalOutcome.Incomplete, first.Detail);
        first.FailedStep.Should().Be("transition");
        w.ApproveRecords.Should().BeEmpty();

        var repeat = await w.ApproveAs(AliceUser);

        repeat.Outcome.Should().Be(ApprovalOutcome.Approved, repeat.Detail);
        w.ApproveRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "165b-3 fix round control: the transition's 'already in target' with the writer-created approve record under the approval's key is already approved, naming that record")]
    public async Task AlreadyInTarget_WithTheRecord_IsAlreadyApproved()
    {
        var w = World();
        var record = new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", w.InvoiceId),
            ["sprk_actioncode"] = "approve-invoice",
            ["createdby"] = new EntityReference("systemuser", DecisionRecordWriterTests.WriterUser),
            ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_invoice", w.InvoiceId, InvoiceApprovalService.DecisionIdFor(w.ApprovalId)):D}\"}}",
        };
        w.BeforeClaim = () =>
        {
            w.BeforeClaim = null;
            w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);
            w.H.Seeded.Add(record);
            return Task.CompletedTask;
        };

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.AlreadyApproved, result.Detail);
        result.DecisionRecordId.Should().Be(record.Id);
    }

    [Fact(DisplayName = "165b-3 part B writer-id fault: the writer identity cannot be read - completion answers Unavailable with nothing written, and the state read fails (never 'not approved')")]
    public async Task WriterIdFault_FailsClosed()
    {
        var w = World();
        AsIfTransitioned(w, Alice, 1000m);
        w.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionApproved);
        w.H.WhoAmIFault = new TimeoutException("whoami");

        var repeat = await w.ApproveAs(AliceUser);
        repeat.Outcome.Should().Be(ApprovalOutcome.Unavailable, repeat.Detail);
        w.Writes.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();

        var state = () => w.GetAs(AliceUser);
        await state.Should().ThrowAsync<TimeoutException>();
    }
}
