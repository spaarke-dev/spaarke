using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 082c-2 fix round 3 (ontology F1; access-control review): <see cref="InvoiceOpenHolds"/>, the one derivation of an invoice's open
/// hold reasons for the review detail. Pure cases over <see cref="InvoiceOpenHolds.ReasonsOf"/>, then the AGREEMENT PIN over the in-memory
/// Dataverse with the real approval service (<see cref="ApprovalWorld"/>): for each stored state, the helper's reasons (the detail they give
/// through <see cref="InvoiceReviewDetail.ReviewDetailOf"/>, and whether any is open) equal what approval decides for that same state, on both
/// the approve and the state view. Approval itself is not routed through the helper (it answers three holds with three different codes and two
/// DTO members, which one set cannot reproduce without changing what clients see), so this pin is what keeps the two from drifting.
/// </summary>
/// <remarks>
/// <para><b>Seed proofs</b> (2026-11-05, each restored byte-identical): <c>InvoiceAssignFirmService</c> put back to the Procedure-only derivation
/// fails the Set firm must-fire cases (<c>InvoiceAssignFirmServiceTests</c>); <see cref="InvoiceOpenHolds.ReasonsAsync"/> that skips the firm
/// hold fails the agreement pin; the unreadable path made to return blank fails <see cref="UnreadableFindings_AreNeverBlank"/> and the undo's
/// <c>SupersedeUndo_UnreadableFindings_ReopensWithBillingTermsNotChecked</c>.</para>
/// </remarks>
public sealed class InvoiceOpenHoldsTests
{
    private const string GapFinding = "{\"pipelineFindings\":[{\"reasonCode\":\"" + SubmissionReasonCodes.ProcedureGap + "\",\"values\":{}}],\"lineFindings\":[]}";
    private const string FirmAndGapFindings = "{\"pipelineFindings\":[{\"reasonCode\":\"" + SubmissionReasonCodes.FirmMissing + "\",\"values\":{}},"
        + "{\"reasonCode\":\"" + SubmissionReasonCodes.ProcedureGap + "\",\"values\":{}}],\"lineFindings\":[]}";

    private static InvoiceOpenHolds.StoredSubmission Stored(string? reason, string? findings = null) => new(reason, findings);

    // ── The derivation (pure) ──

    [Fact(DisplayName = "082c-2 fix round 3 (F1): the firm hold united with the Procedure holds - each source holds alone, and the detail is the D-205 priority of them")]
    public void ReasonsOf_UnitesTheFirmHoldWithTheProcedureHolds()
    {
        InvoiceOpenHolds.ReasonsOf(false, false, []).Should().BeEmpty();
        InvoiceOpenHolds.ReasonsOf(true, false, []).Should().BeEquivalentTo([SubmissionReasonCodes.FirmMissing], "no firm");
        InvoiceOpenHolds.ReasonsOf(false, true, []).Should().BeEquivalentTo([SubmissionReasonCodes.FirmMissing], "the invoice's own hold");
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(SubmissionReasonCodes.FirmMissing)]).Should().BeEquivalentTo([SubmissionReasonCodes.FirmMissing], "the stored reason");
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, FirmAndGapFindings)])
            .Should().BeEquivalentTo([SubmissionReasonCodes.FirmMissing, SubmissionReasonCodes.ProcedureGap]);
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, GapFinding)]).Should().BeEquivalentTo([SubmissionReasonCodes.ProcedureGap]);
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, "{\"pipelineFindings\":[],\"lineFindings\":[]}"), Stored(SubmissionReasonCodes.ProceduresUnresolved)])
            .Should().BeEquivalentTo([SubmissionReasonCodes.ProceduresUnresolved], "every linked submission counts");

        InvoiceReviewDetail.ReviewDetailOf(InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, FirmAndGapFindings)])).Should().Be(InvoiceReviewDetail.WaitingForFirm);
        InvoiceReviewDetail.ReviewDetailOf(InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, GapFinding)])).Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
        InvoiceReviewDetail.ReviewDetailOf(InvoiceOpenHolds.ReasonsOf(false, false, [])).Should().BeNull();
    }

    [Fact(DisplayName = "114 (must fire / must not fire): a submission released by an override holds no Procedure reason, even with the gap kept in its findings; the firm marker beside it still holds")]
    public void ReasonsOf_AReleasedSubmission_HoldsNoProcedureReason_ButTheFirmStillHolds()
    {
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(SubmissionReasonCodes.Overridden, GapFinding)]).Should().BeEmpty("released: not blank only because it is kept as evidence");
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(SubmissionReasonCodes.Overridden, FirmAndGapFindings)])
            .Should().BeEquivalentTo([SubmissionReasonCodes.FirmMissing], "the firm hold is independent of the release");
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(SubmissionReasonCodes.Overridden, GapFinding), Stored(null, GapFinding)])
            .Should().BeEquivalentTo([SubmissionReasonCodes.ProcedureGap], "another, unreleased submission still holds (control)");
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, GapFinding)]).Should().BeEquivalentTo([SubmissionReasonCodes.ProcedureGap], "control: unreleased");
    }

    [Theory(DisplayName = "082c-2 fix round 3 (F1, fail closed, must fire): findings that cannot be read are never blank - 'Billing terms could not be checked', and the firm is not asserted from them")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public void UnreadableFindings_AreNeverBlank(string findings)
    {
        var reasons = InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, findings)]);

        reasons.Should().BeEquivalentTo([SubmissionReasonCodes.ProceduresUnresolved]);
        InvoiceReviewDetail.ReviewDetailOf(reasons).Should().Be(InvoiceReviewDetail.BillingTermsNotChecked).And.NotBeNull();
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, fail closed): unreadable findings beside a readable firm marker are still the firm (the firm wins), and a document that parses but names nothing is not a hold")]
    public void UnreadableFindings_BesideAReadableFirmMarker_AreTheFirm_AndAnEmptyDocumentIsNoHold()
    {
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(SubmissionReasonCodes.FirmMissing, "{not json")]).Should().Contain(SubmissionReasonCodes.FirmMissing);
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, "{}")]).Should().BeEmpty("control: readable findings with no marker are no hold");
        InvoiceOpenHolds.ReasonsOf(false, false, [Stored(null, "   ")]).Should().BeEmpty("control: no findings stored");
    }

    [Fact(DisplayName = "082c-2 fix round 3: InvoiceFirmHold.IsHeld(reason, findings) answers exactly as IsHeld(row), and says when the answer rests on unreadable findings")]
    public void FirmHold_StringOverload_AgreesWithTheRowOverload()
    {
        foreach (var (reason, json) in new (string?, string?)[]
        {
            (null, null), (SubmissionReasonCodes.FirmMissing, null), (null, "{not json"), (null, "{}"), (null, FirmAndGapFindings), (null, GapFinding), (null, "[1]"),
        })
        {
            var row = new Entity("sprk_invoicesubmission", Guid.NewGuid());
            if (reason is not null)
            {
                row["sprk_outcomereasoncode"] = reason;
            }

            if (json is not null)
            {
                row["sprk_findingsjson"] = json;
            }

            InvoiceFirmHold.IsHeld(reason, json, out _).Should().Be(InvoiceFirmHold.IsHeld(row), $"{reason} / {json}");
        }

        InvoiceFirmHold.IsHeld(null, "{not json", out var unreadable).Should().BeTrue();
        unreadable.Should().BeTrue();
        InvoiceFirmHold.IsHeld(null, FirmAndGapFindings, out unreadable).Should().BeTrue();
        unreadable.Should().BeFalse("a readable marker is not the fail-closed answer");
        InvoiceFirmHold.IsHeld(SubmissionReasonCodes.FirmMissing, "{not json", out unreadable).Should().BeTrue();
        unreadable.Should().BeFalse("the stored reason names the firm before the findings are read");
    }

    // ── The agreement pin: the stored state, the helper over it, and approval over it ──

    /// <summary>One stored state the invoice can be in, planted into a fresh approval world, with what the helper must answer for it.</summary>
    public static TheoryData<string, int?, bool> States => new()
    {
        { "firm only", InvoiceReviewDetail.WaitingForFirm, true },
        { "firm + gap", InvoiceReviewDetail.WaitingForFirm, true },
        { "gap only", InvoiceReviewDetail.WaitingForBillingTerms, false },
        { "unresolved only", InvoiceReviewDetail.BillingTermsNotChecked, false },
        { "own hold only", InvoiceReviewDetail.WaitingForFirm, true },
        { "no firm only", InvoiceReviewDetail.WaitingForFirm, true },
        { "unreadable findings", InvoiceReviewDetail.BillingTermsNotChecked, true },
        { "none", null, false },

        // Task 114: a released (Overridden) submission holds no Procedure reason, whatever its findings keep as evidence; the firm hold is its own.
        { "gap released by an override", null, false },
        { "firm + gap, gap released by an override", InvoiceReviewDetail.WaitingForFirm, true },
    };

    private static void Plant(ApprovalWorld w, string state)
    {
        switch (state)
        {
            case "firm only":
                w.HoldForFirm();
                break;
            case "firm + gap":
                w.HoldForFirm(findingsJson: FirmAndGapFindings);
                break;
            case "gap only":
                PlantSubmission(w, null, GapFinding);
                break;
            case "unresolved only":
                PlantSubmission(w, SubmissionReasonCodes.ProceduresUnresolved, "{}");
                break;
            case "own hold only":
                w.HoldInvoiceForFirm();
                PlantSubmission(w, null, "{}");
                break;
            case "no firm only":
                w.WithoutFirm();
                PlantSubmission(w, null, "{}");
                break;
            case "unreadable findings":
                PlantSubmission(w, null, "{not json");
                break;
            case "gap released by an override":
                PlantSubmission(w, SubmissionReasonCodes.Overridden, GapFinding);
                break;
            case "firm + gap, gap released by an override":
                PlantSubmission(w, SubmissionReasonCodes.Overridden, FirmAndGapFindings);
                break;
            case "none":
                PlantSubmission(w, null, "{\"pipelineFindings\":[],\"lineFindings\":[{\"line\":0,\"code\":\"rules.not_checked\",\"message\":\"The Rate rule could not be checked on this line.\"}]}");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private static void PlantSubmission(ApprovalWorld w, string? reason, string findings)
    {
        var id = w.HoldForFirm(findingsJson: findings);
        w.Rows[("sprk_invoicesubmission", id)]["sprk_outcomereasoncode"] = reason;
    }

    [Theory(DisplayName = "082c-2 fix round 3 (F1, agreement pin): for each stored state the review detail is ReviewDetailOf(helper(state)), and the helper's 'held' equals approval's held decision on the approve and the state view")]
    [MemberData(nameof(States))]
    public async Task TheHelperAgreesWithApproval_ForTheSameStoredState(string state, int? expectedDetail, bool firmCode)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        Plant(w, state);

        var reasons = await InvoiceOpenHolds.ReasonsAsync(w.Entities.Object, w.Invoice, CancellationToken.None);
        var detail = InvoiceReviewDetail.ReviewDetailOf(reasons);
        var held = reasons.Count > 0;

        detail.Should().Be(expectedDetail, state);
        held.Should().Be(expectedDetail is not null, state);

        // The approve: refused as held exactly when the helper holds, and the code names the same kind of hold (a firm-needing state by the firm
        // code, a Procedure-only state by the Procedure code; unreadable findings are held by the firm check first, 095c-4). The state view is read
        // AFTER a refused approve (a GET stamps the resolved approver set) and BEFORE an approve that goes through (it moves the portion).
        ApprovalResult result;
        InvoiceApprovalStateDto view;
        if (held)
        {
            result = await w.ApproveAs(AliceUser);
            w.Writes.Should().Be(0, $"a refused approve writes nothing ({state})");
            view = (await w.GetAs(AliceUser))!;
            result.Outcome.Should().Be(ApprovalOutcome.Held, state);
            result.Code.Should().Be(firmCode ? InvoiceApprovalCodes.FirmMissing : InvoiceApprovalCodes.ProcedureHeld, state);
            view.CanApprove.Should().BeFalse(state);
        }
        else
        {
            view = (await w.GetAs(AliceUser))!;
            result = await w.ApproveAs(AliceUser);
            result.Outcome.Should().Be(ApprovalOutcome.Approved, state);
        }

        // The state view: held for a Procedure, or needing a firm, is "cannot approve".
        (view.Held || view.FirmNeeded).Should().Be(held, $"the approval state view for: {state}");
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, fail closed): a read fault in the helper propagates - the detail is never derived (blank) from a read that did not happen")]
    public async Task AReadFault_Propagates()
    {
        var w = new ApprovalWorld();
        w.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("read fault"));

        var act = () => InvoiceOpenHolds.ReasonsAsync(w.Entities.Object, w.Invoice, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
