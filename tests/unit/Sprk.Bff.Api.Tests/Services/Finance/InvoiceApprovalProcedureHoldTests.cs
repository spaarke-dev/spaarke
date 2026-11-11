using System.Text.Json;
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
    [InlineData("line", InvoiceApprovalCodes.ProcedureGapDetail)]
    public async Task EachStoredMarker_RefusesApprove(string marker, string detail)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        _ = marker switch
        {
            "reason-gap" => HoldForProcedures(w, SubmissionReasonCodes.ProcedureGap, null),
            "reason-unresolved" => HoldForProcedures(w, SubmissionReasonCodes.ProceduresUnresolved, null),
            "line" => HoldForProcedures(w, null, """{"lineFindings":[{"line":0,"code":"rules.not_checked","message":"x","cause":"ebilling.submission.procedure_gap"}]}"""),
            _ => HoldForProcedures(w, null, "{not json"),
        };
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
        state.BlockedSubmissionHeld.Should().BeFalse("a Procedure-only hold refuses Approve, not Reject or Return");
    }

    [Fact(DisplayName = "PR #2689 F1 (contract): the approval state names the blocked-submission hold apart from a Procedure hold on the wire (blockedSubmissionHeld)")]
    public async Task State_NamesTheBlockedSubmissionHold_OnTheWire()
    {
        var procedure = new ApprovalWorld();
        procedure.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        HoldForProcedures(procedure, SubmissionReasonCodes.ProcedureGap, null);
        var procedureJson = JsonSerializer.Serialize((await procedure.GetAs(AliceUser))!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        procedureJson.Should().Contain("\"held\":true").And.Contain("\"blockedSubmissionHeld\":false");

        var blocked = new ApprovalWorld();
        blocked.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        blocked.Hold();
        var blockedJson = JsonSerializer.Serialize((await blocked.GetAs(AliceUser))!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        blockedJson.Should().Contain("\"held\":true").And.Contain("\"blockedSubmissionHeld\":true");
    }

    [Fact(DisplayName = "082c-2 / 165a: an Internal gap (the pipeline marker, kind Blocked) is held by FR-20 first; the genuine Override release (Accepted, Overridden, the stamp) lifts both holds")]
    public async Task InternalPipelineGap_HeldUntilTheOverrideRelease()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        // The persister's findings of an Internal gap: kind Blocked, the blocked reason, the gap as a pipeline finding.
        var findings = System.Text.Json.Nodes.JsonNode.Parse(GapPipeline)!.AsObject();
        findings["schema"] = 1;
        findings["kind"] = nameof(SubmissionEvaluationKind.Blocked);
        findings["reasonCode"] = SubmissionReasonCodes.Blocked;
        var id = HoldForProcedures(w, SubmissionReasonCodes.Blocked, findings.ToJsonString());
        var row = w.Rows[("sprk_invoicesubmission", id)];
        row["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Rejected);
        findings["invoiceId"] = InvoiceSubmissionPersister.InvoiceIdFor(id).ToString("D");
        row["sprk_findingsjson"] = findings.ToJsonString();

        var held = await w.ApproveAs(AliceUser);

        held.Code.Should().Be(InvoiceApprovalCodes.Held, "an Internal gap is Blocked, so the FR-20 hold answers first");
        InvoiceProcedureHold.ReasonsOf(row).Should().BeEquivalentTo([SubmissionReasonCodes.ProcedureGap], "the pipeline marker holds too");
        (await w.GetAs(AliceUser))!.Should().Match<InvoiceApprovalStateDto>(s => s.Held && s.BlockedSubmissionHeld,
            "both holds: the blocked submission is named, so the page keeps Reject and Return off (the blocked banner owns it)");

        // The Override release as SubmissionOverrideService writes it: status Accepted, code Overridden and the stamp, in one write.
        findings[SubmissionOverrideService.OverrideMember] = new System.Text.Json.Nodes.JsonObject
        {
            ["recordId"] = Guid.NewGuid().ToString("D"),
            ["bySystemUserId"] = Guid.NewGuid().ToString("D"),
            ["on"] = "2026-11-06T10:00:00.0000000Z",
            ["reason"] = "negotiated",
        };
        row["sprk_findingsjson"] = findings.ToJsonString();
        row["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);
        row["sprk_outcomereasoncode"] = SubmissionReasonCodes.Overridden;
        InvoiceProcedureHold.ReasonsOf(row).Should().BeEmpty("task 114: the release lifts the Procedure hold");

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
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
