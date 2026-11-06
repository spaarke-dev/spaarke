using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095c-4 (095c-3 review K1; uac-r2 2026-11-03): approve, the approval state's <c>canApprove</c> and Return to Firm refuse an
/// invoice that needs a firm (<see cref="InvoiceFirmHold"/>), at the INVOICE level: no firm, or a stored <c>firm_missing</c> hold, also with no
/// lines, and also after the firm was set by a direct write. Over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the real services.
/// </summary>
/// <remarks>
/// <para><b>Seed proofs (notes 095c section 14).</b> <c>NoFirm_ZeroLines_ApproveIs409_AndTheFirmNeededSignalStaysOpen</c> fails when the approve
/// check is removed (the zero-line invoice then approves and its Signal is closed). <c>FirmSetNatively_WhileHeld_ApproveIsStill409</c> fails
/// when the check reads only the firm column.</para>
/// </remarks>
public sealed class InvoiceApprovalFirmHoldTests
{
    private static void AssertNothingWritten(ApprovalWorld w)
    {
        w.Writes.Should().Be(0, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
    }

    [Fact(DisplayName = "095c-4 K1: an invoice with no firm and NO lines is 409 firm_missing (I-2 alone would allow it) and its Firm needed Signal stays open")]
    public async Task NoFirm_ZeroLines_ApproveIs409_AndTheFirmNeededSignalStaysOpen()
    {
        var w = new ApprovalWorld();
        w.WithoutFirm();
        w.HoldForFirm();
        var signal = w.AddInvoiceSignal(BillingRuleCatalogue.SystemPolicyCode);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held);
        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing);
        result.Detail.Should().Be(InvoiceApprovalCodes.FirmMissingDetail);
        AssertNothingWritten(w);
        w.Rows[("sprk_signal", signal)].GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen,
            "EB-D-172: approval would have closed the Firm needed Signal");

        var state = (await w.GetAs(AliceUser))!;
        state.CanApprove.Should().BeFalse();
        state.FirmNeeded.Should().BeTrue();
    }

    [Fact(DisplayName = "095c-4 uac-r2: the firm set by a direct write while the hold is on: approve is still 409 firm_missing and canApprove stays false")]
    public async Task FirmSetNatively_WhileHeld_ApproveIsStill409()
    {
        var w = new ApprovalWorld();
        w.WithoutFirm();
        w.HoldForFirm();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.SetFirmNatively(Guid.NewGuid());

        var result = await w.ApproveAs(AliceUser);

        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing, "the stored hold, not the firm column, decides");
        AssertNothingWritten(w);
        (await w.GetAs(AliceUser))!.CanApprove.Should().BeFalse();
    }

    [Theory(DisplayName = "095c-4: each stored marker holds alone (the reason, the pipeline finding, the line marker); unreadable findings hold (fail closed)")]
    [InlineData("reason")]
    [InlineData("pipeline")]
    [InlineData("line")]
    [InlineData("unreadable")]
    public async Task EachStoredMarker_HoldsAlone(string marker)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var id = w.HoldForFirm(reasonOnly: marker == "reason", findingsJson: marker switch
        {
            "pipeline" => """{"pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing","values":{}}]}""",
            "line" => """{"lineFindings":[{"line":0,"code":"rules.not_checked","message":"x","cause":"ebilling.submission.firm_missing"}]}""",
            "unreadable" => "{not json",
            _ => null,
        });
        if (marker != "reason")
        {
            w.Rows[("sprk_invoicesubmission", id)]["sprk_outcomereasoncode"] = null; // the reason rewritten: only the stored findings hold
        }

        var result = await w.ApproveAs(AliceUser);

        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing, marker);
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "095c-4 must not fire: a firm invoice with a rules.not_checked line of a refused rule (F2, no firm cause) and no lines open approves")]
    public async Task FirmInvoice_WithARefusedRuleLine_Approves()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var id = w.HoldForFirm(findingsJson: """{"lineFindings":[{"line":0,"code":"rules.not_checked","message":"The Rate rule could not be checked on this line."}]}""");
        w.Rows[("sprk_invoicesubmission", id)]["sprk_outcomereasoncode"] = null;

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, "EB-D-175: a refused rule's line is decided by the reviewer, not held for a firm");
    }

    [Theory(DisplayName = "095d-0 must-fire: approve of an invoice whose Set-firm re-run is pending ('pending', any other word, or a stored decision that cannot be read) is 409 firm_missing with the re-run sentence; nothing written, its Signal open")]
    [InlineData("pending")]
    [InlineData("running")]
    [InlineData("unreadable")]
    public async Task RerunPending_ApproveIs409_AndNothingIsWritten(string state)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.LinkSubmission(state == "unreadable" ? InvoiceFirmGateTests.FindingsWithUnreadableAssignFirm() : InvoiceFirmGateTests.FindingsWithAssignFirm(state));
        var signal = w.AddInvoiceSignal(BillingRuleCatalogue.SystemPolicyCode);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held);
        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing);
        result.Code.Should().Be("ebilling.approve.firm_missing", "approve keeps its code");
        result.Detail.Should().Be(InvoiceApprovalCodes.FirmRerunPendingDetail);
        AssertNothingWritten(w);
        w.Rows[("sprk_signal", signal)].GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Fact(DisplayName = "095d-0 must-not-fire: approve of an invoice whose Set-firm re-run is 'completed' proceeds")]
    public async Task RerunCompleted_Approves()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.LinkSubmission(InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunCompleted));

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact(DisplayName = "095d-0: the approve sentences are one per gate state; the existing two are unchanged")]
    public void TheSentences()
    {
        InvoiceApprovalCodes.FirmDetailFor(InvoiceFirmGateResult.NoFirm).Should().Be(InvoiceApprovalCodes.FirmMissingDetail);
        InvoiceApprovalCodes.FirmDetailFor(InvoiceFirmGateResult.HeldForFirm).Should().Be(InvoiceApprovalCodes.FirmHeldDetail);
        InvoiceApprovalCodes.FirmDetailFor(InvoiceFirmGateResult.RerunPending).Should().Be(InvoiceApprovalCodes.FirmRerunPendingDetail);
    }

    [Fact(DisplayName = "095c-4 must not fire: a firm invoice with no lines and no hold approves; canApprove is true")]
    public async Task FirmInvoice_ZeroLines_NoHold_Approves()
    {
        var w = new ApprovalWorld();

        (await w.GetAs(AliceUser))!.Should().Match<InvoiceApprovalStateDto>(s => s.CanApprove && !s.FirmNeeded);
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact(DisplayName = "095c-4: Return to Firm of an invoice with no firm (or held for one) is 409 firm_missing; 095d-0 (EB-D-190): so is Reject")]
    public async Task ReturnToFirm_NeedsAFirm_AndSoDoesReject()
    {
        var returned = new ApprovalWorld();
        returned.WithoutFirm();
        returned.HoldForFirm();
        var refused = await returned.ReturnAs(AliceUser);
        refused.Outcome.Should().Be(InvoiceDecisionOutcome.Held);
        refused.Code.Should().Be(InvoiceRejectReturnCodes.FirmMissing);
        returned.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        returned.Writes.Should().Be(0);

        var held = new ApprovalWorld();
        held.HoldForFirm();
        (await held.ReturnAs(AliceUser)).Code.Should().Be(InvoiceRejectReturnCodes.FirmMissing, "a firm set natively while held does not count");

        var rejected = new ApprovalWorld();
        rejected.WithoutFirm();
        rejected.HoldForFirm();
        // Task 095d-0 (EB-D-190): was "Reject is final and needs no firm" (Decided / Rejected). The owner now gates Reject like approve.
        var reject = await rejected.RejectAs(AliceUser);
        reject.Outcome.Should().Be(InvoiceDecisionOutcome.Held);
        reject.Code.Should().Be(InvoiceRejectReturnCodes.FirmMissing);
        rejected.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        rejected.Writes.Should().Be(0);
    }

    private static void AssertRejectNothingWritten(ApprovalWorld w, Guid signal)
    {
        w.Writes.Should().Be(0, "a refused reject writes nothing");
        w.Transitions.Should().Be(0);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Portion.GetAttributeValue<EntityReference>(InvoiceApprovalService.PortionDecidedBy).Should().BeNull("no claim written");
        w.H.Created.Should().BeEmpty("no Decision Record");
        w.Rows[("sprk_signal", signal)].GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value.Should().Be(SignalWriter.SignalStatusOpen, "no Signal closed");
    }

    [Theory(DisplayName = "095d-0 must-fire (EB-D-190): Reject of an invoice with no firm, held for one (its own column or a submission's stored hold), or with the Set-firm re-run pending (or unreadable) is 409 ebilling.invoice.firm_missing; nothing written, recorded or closed")]
    [InlineData("no-firm")]
    [InlineData("held-own-column")]
    [InlineData("held-submission")]
    [InlineData("rerun-pending")]
    [InlineData("rerun-unreadable")]
    public async Task Reject_WithoutAnOpenFirmGate_Is409_AndNothingIsWritten(string state)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var signal = w.AddInvoiceSignal(BillingRuleCatalogue.SystemPolicyCode);
        InvoiceFirmGateResult gate;
        switch (state)
        {
            case "no-firm":
                w.WithoutFirm();
                gate = InvoiceFirmGateResult.NoFirm;
                break;
            case "held-own-column":
                w.HoldInvoiceForFirm();
                gate = InvoiceFirmGateResult.HeldForFirm;
                break;
            case "held-submission":
                w.HoldForFirm();
                gate = InvoiceFirmGateResult.HeldForFirm;
                break;
            case "rerun-pending":
                w.LinkSubmission(InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunPending));
                gate = InvoiceFirmGateResult.RerunPending;
                break;
            default:
                w.LinkSubmission(InvoiceFirmGateTests.FindingsWithUnreadableAssignFirm());
                gate = InvoiceFirmGateResult.RerunPending;
                break;
        }

        var result = await w.RejectAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Held);
        result.Code.Should().Be("ebilling.invoice.firm_missing");
        result.Detail.Should().Be(InvoiceRejectReturnCodes.RejectFirmDetailFor(gate));
        AssertRejectNothingWritten(w, signal);
    }

    [Fact(DisplayName = "095d-0 must-not-fire: Reject of an invoice with a firm, no hold and a Set-firm re-run 'completed' proceeds; so does one with a firm and no assignFirm member")]
    public async Task Reject_WithAnOpenFirmGate_Proceeds()
    {
        var completed = new ApprovalWorld();
        completed.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        completed.LinkSubmission(InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunCompleted));
        (await completed.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        completed.InvoiceStatus.Should().Be(InvoiceStatusCodes.Rejected);

        var plain = new ApprovalWorld();
        (await plain.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
    }

    [Fact(DisplayName = "095d-0 must-not-fire: Return to Firm proceeds while the Set-firm re-run is pending (the firm is known); Set firm is not gated by this service")]
    public async Task Return_WhileTheRerunIsPending_Proceeds()
    {
        var w = new ApprovalWorld();
        w.LinkSubmission(InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunPending));

        (await w.ReturnAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
    }

    [Fact(DisplayName = "095d-0 order: a caller who is not an eligible approver, on a firmless invoice, gets NotEligible (403), not the firm 409")]
    public async Task Reject_ByANonApprover_OnAFirmlessInvoice_IsForbidden_NotConflict()
    {
        var w = new ApprovalWorld();
        w.WithoutFirm();
        w.HoldForFirm();
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol); // Alice is no longer an eligible approver (live core record)

        var result = await w.RejectAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        result.Code.Should().Be(InvoiceRejectReturnCodes.NotEligible);
        w.Writes.Should().Be(0);
    }

    [Fact(DisplayName = "095d-0: the approval state's firmNeeded (and so canApprove) follows the gate - it is true, and canApprove false, while the Set-firm re-run is pending")]
    public async Task State_FirmNeeded_FollowsTheGate()
    {
        var pending = new ApprovalWorld();
        pending.LinkSubmission(InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunPending));
        (await pending.GetAs(AliceUser))!.Should().Match<InvoiceApprovalStateDto>(s => s.FirmNeeded && !s.CanApprove);

        var completed = new ApprovalWorld();
        completed.LinkSubmission(InvoiceFirmGateTests.FindingsWithAssignFirm(InvoiceAssignFirmService.RulesRerunCompleted));
        (await completed.GetAs(AliceUser))!.Should().Match<InvoiceApprovalStateDto>(s => !s.FirmNeeded && s.CanApprove);
    }

    [Fact(DisplayName = "095c-4: the pure hold reader - the reason, the pipeline finding and the caused line hold; a firm invoice's findings do not")]
    public void IsHeld_ReadsTheStoredMarkers()
    {
        Entity Row(string? reason, string? json) => new("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_outcomereasoncode"] = reason,
            ["sprk_findingsjson"] = json,
        };

        InvoiceFirmHold.IsHeld(Row(SubmissionReasonCodes.FirmMissing, null)).Should().BeTrue();
        InvoiceFirmHold.IsHeld(Row(null, """{"pipelineFindings":[{"reasonCode":"ebilling.submission.firm_missing"}]}""")).Should().BeTrue();
        InvoiceFirmHold.IsHeld(Row(null, """{"lineFindings":[{"code":"rules.not_checked","cause":"ebilling.submission.firm_missing"}]}""")).Should().BeTrue();
        InvoiceFirmHold.IsHeld(Row(null, "[1]")).Should().BeTrue("not an object: fail closed");

        InvoiceFirmHold.IsHeld(Row(null, null)).Should().BeFalse();
        InvoiceFirmHold.IsHeld(Row(SubmissionReasonCodes.Blocked, null)).Should().BeFalse("the FR-20 hold is read elsewhere");
        InvoiceFirmHold.IsHeld(Row(null, """{"pipelineFindings":[{"reasonCode":"ebilling.submission.invoice_number_mismatch"}],"lineFindings":[{"code":"rules.not_checked"}]}"""))
            .Should().BeFalse("a firm invoice's refused-rule line has no firm cause");
    }
}
