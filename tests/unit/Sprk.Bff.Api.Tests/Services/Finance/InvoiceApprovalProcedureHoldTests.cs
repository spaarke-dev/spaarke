using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 082c-2 (brief 082c C11, C14): approve refuses an invoice held for its Procedures (<see cref="InvoiceProcedureHold"/>) with
/// <c>ebilling.approve.procedure_held</c>, from the stored submission state alone (never <c>sprk_reviewdetail</c>), and the approval state shows
/// it as held. Over the in-memory Dataverse (<see cref="ApprovalWorld"/>) with the real services.
/// </summary>
public sealed class InvoiceApprovalProcedureHoldTests
{
    private const string GapPipeline = """{"pipelineFindings":[{"reasonCode":"ebilling.submission.procedure_gap","values":{"procedureName":"P","workDate":"2026-06-01","gapKind":"AfterLastVersion"}}],"lineFindings":[]}""";

    private static Guid HoldForProcedures(ApprovalWorld w, string? reason, string? findingsJson)
    {
        var id = w.HoldForFirm(reasonOnly: findingsJson is null, findingsJson: findingsJson ?? "{}");
        w.Rows[("sprk_invoicesubmission", id)]["sprk_outcomereasoncode"] = reason;
        return id;
    }

    [Theory(DisplayName = "082c-2 C11/C14: each stored marker holds alone (the SPA reason, the pipeline finding, the line cause); unreadable findings are held by the firm check first (095c-4)")]
    [InlineData("reason-gap", InvoiceApprovalCodes.ProcedureGapDetail)]
    [InlineData("reason-unresolved", InvoiceApprovalCodes.ProceduresUnresolvedDetail)]
    [InlineData("pipeline", InvoiceApprovalCodes.ProcedureGapDetail)]
    [InlineData("line", InvoiceApprovalCodes.ProcedureGapDetail)]
    public async Task EachStoredMarker_RefusesApprove(string marker, string detail)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        _ = marker switch
        {
            "reason-gap" => HoldForProcedures(w, SubmissionReasonCodes.ProcedureGap, null),
            "reason-unresolved" => HoldForProcedures(w, SubmissionReasonCodes.ProceduresUnresolved, null),
            "pipeline" => HoldForProcedures(w, SubmissionReasonCodes.Blocked, GapPipeline),
            "line" => HoldForProcedures(w, null, """{"lineFindings":[{"line":0,"code":"rules.not_checked","message":"x","cause":"ebilling.submission.procedure_gap"}]}"""),
            _ => HoldForProcedures(w, null, "{not json"),
        };
        if (marker == "pipeline")
        {
            // An Internal gap after 114's Override released its Blocked submission: the FR-20 hold is gone; the Procedure hold still refuses.
            w.Rows.Values.Single(r => r.LogicalName == "sprk_invoicesubmission")["sprk_outcomereasoncode"] = null;
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held, marker);
        result.Code.Should().Be(InvoiceApprovalCodes.ProcedureHeld, marker);
        result.Detail.Should().Be(detail);
        w.Writes.Should().Be(0, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        var state = (await w.GetAs(AliceUser))!;
        state.CanApprove.Should().BeFalse();
        state.Held.Should().BeTrue();
    }

    [Fact(DisplayName = "082c-2 must not fire: a submission with no Procedure marker (a refused rule's line, no cause) approves")]
    public async Task NoProcedureMarker_Approves()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        HoldForProcedures(w, null, """{"pipelineFindings":[],"lineFindings":[{"line":0,"code":"rules.not_checked","message":"The Rate rule could not be checked on this line."}]}""");

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact(DisplayName = "082c-2: the stored Procedure reasons of one submission row (pure): reason, pipeline and line markers; other codes are not Procedure holds")]
    public void ReasonsOf_ReadsEachMarker()
    {
        var row = new Entity("sprk_invoicesubmission", Guid.NewGuid())
        {
            ["sprk_outcomereasoncode"] = SubmissionReasonCodes.FirmMissing,
            ["sprk_findingsjson"] = GapPipeline,
        };

        InvoiceProcedureHold.ReasonsOf(row).Should().BeEquivalentTo([SubmissionReasonCodes.ProcedureGap]);
        InvoiceProcedureHold.ReasonsOf(new Entity("sprk_invoicesubmission", Guid.NewGuid())).Should().BeEmpty();
        InvoiceProcedureHold.ReasonsOf(new Entity("sprk_invoicesubmission", Guid.NewGuid()) { ["sprk_findingsjson"] = "{not json" })
            .Should().BeEquivalentTo([SubmissionReasonCodes.ProceduresUnresolved], "unreadable stored findings hold (fail closed)");
    }
}
