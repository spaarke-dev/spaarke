using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 ISS-031 review round 1 (found in passing): a Procedure-held submission (an SPA one is Accepted, so the FR-20 hold does not hold its
/// invoice) can be Overridden. An Override that recorded its Decision Record but whose release failed leaves the submission held and the invoice
/// In Review, and the invoice's own reject and return used to go ahead on top of it, so the Override could never release. Over the in-memory
/// Dataverse of <see cref="ApprovalWorld"/> with the REAL resolver, recorder and writer harness, each test proves the stored state: the invoice's
/// status, the approval portion, the Decision Records.
/// </summary>
public sealed class InvoiceRejectReturnOverridePendingTests
{
    public static TheoryData<InvoiceDecisionKind> Kinds => new() { InvoiceDecisionKind.Reject, InvoiceDecisionKind.Return };

    private const string OverrideReason = "Procedure gap accepted for this matter.";

    private static Task<InvoiceDecisionResult> Decide(ApprovalWorld w, InvoiceDecisionKind kind, Guid user) =>
        kind == InvoiceDecisionKind.Reject ? w.RejectAs(user) : w.ReturnAs(user);

    /// <summary>The invoice's own submission as the processor leaves an SPA Procedure gap: Accepted, reason <c>procedure_gap</c>, the recorded outcome
    /// Accepted with that reason (task 114's ProcedureHeld).</summary>
    private static void HoldOwnSubmissionForProcedures(ApprovalWorld w)
    {
        var own = w.Rows[("sprk_invoicesubmission", w.OwnSubmissionId)];
        own["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Accepted);
        own["sprk_outcomereasoncode"] = SubmissionReasonCodes.ProcedureGap;
        own["sprk_findingsjson"] = $$"""{"schema":1,"kind":"Accepted","invoiceId":"{{w.InvoiceId:D}}","reasonCode":"{{SubmissionReasonCodes.ProcedureGap}}"}""";
    }

    /// <summary>The Override's Decision Record, written by the writer as the Override writes it before its release (the stranded state when the
    /// release then fails), under the invoice with the Override's derived decision id. Returns its id.</summary>
    private static async Task<Guid> RecordOverrideAsync(ApprovalWorld w, Guid decider)
    {
        var recorded = await w.Recorder().RecordAsync(new BillingDecision(
            SubmissionOverrideService.OverrideDecisionIdFor(w.OwnSubmissionId),
            SubmissionOverrideService.OverrideActionCode,
            w.InvoiceId,
            BillingDecider.Human(decider, "internal user with the eBilling Override privilege"),
            new Dictionary<string, object?>
            {
                ["decision"] = SubmissionOverrideService.OverrideActionCode,
                ["outcome"] = "Overridden",
                ["submissionId"] = w.OwnSubmissionId.ToString("D"),
            },
            "Overridden",
            Reason: OverrideReason), CancellationToken.None);
        recorded.AlreadyRecorded.Should().BeFalse();
        return recorded.RecordId;
    }

    /// <summary>The release the Override's own transaction writes: reason Overridden and the field-secured stamp (InvoiceProcedureHold.IsReleased).</summary>
    private static void Release(ApprovalWorld w, Guid recordId, Guid decider)
    {
        var own = w.Rows[("sprk_invoicesubmission", w.OwnSubmissionId)];
        own["sprk_outcomereasoncode"] = SubmissionReasonCodes.Overridden;
        own["sprk_findingsjson"] = SubmissionOverrideService.WithStamp(
            own.GetAttributeValue<string>("sprk_findingsjson"), SubmissionOverrideService.OverrideMember,
            new BlockedDecisionStamp(recordId, decider, Now, OverrideReason));
    }

    private static int OverrideRecords(ApprovalWorld w) => w.RecordsOf(SubmissionOverrideService.OverrideActionCode).Count;

    private static void AssertNothingWritten(ApprovalWorld w)
    {
        w.Writes.Should().Be(0, "a refused call writes nothing");
        w.Transitions.Should().Be(0);
        w.RecordsOf("reject-invoice").Should().BeEmpty();
        w.RecordsOf("return-to-firm").Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse("no claim");
        OverrideRecords(w).Should().Be(1, "the stranded Override's record is untouched");
    }

    // ── Must fire ──

    [Theory(DisplayName = "ISS-031 round 1: a stranded Procedure-hold Override (record written, not released) refuses the invoice's reject and return 409 other_decision_pending; nothing is written")]
    [MemberData(nameof(Kinds))]
    public async Task StrandedOverride_RefusesRejectAndReturn_AndWritesNothing(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        await RecordOverrideAsync(w, CarolUser);

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Conflict, "the endpoint answers 409");
        result.Code.Should().Be(InvoiceRejectReturnCodes.OverridePending);
        result.Code.Should().Be("ebilling.blocked.other_decision_pending", "the wire code is a contract");
        result.Detail.Should().Be(InvoiceRejectReturnCodes.OverridePendingDetail(kind, callerIsDecider: false));
        result.Detail.Should().Contain("They complete it by repeating it.");
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "ISS-031 round 1: the Override's own decider, also an eligible approver, is told to repeat the override")]
    public async Task StrandedOverride_TheDeciderIsToldToRepeatIt()
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        await RecordOverrideAsync(w, AliceUser);

        var result = await w.ReturnAs(AliceUser);

        result.Code.Should().Be(InvoiceRejectReturnCodes.OverridePending);
        result.Detail.Should().Be(InvoiceRejectReturnCodes.OverridePendingDetail(InvoiceDecisionKind.Return, callerIsDecider: true));
        result.Detail.Should().Contain("Repeat the override to finish it.");
        AssertNothingWritten(w);
    }

    // ── The 403s come first (round 2) ──

    [Theory(DisplayName = "ISS-031 round 2: on a stranded Override, an eligible approver without the rights to close the invoice's Signals gets 403 not_authorized, not the 409 that says a decision is pending; nothing written")]
    [MemberData(nameof(Kinds))]
    public async Task StrandedOverride_NoCloseRights_Is403BeforeThe409(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        await RecordOverrideAsync(w, CarolUser);
        w.AddInvoiceSignal();
        // Write on the invoice but no AppendTo on the core record (the close's rights, FR-51).
        w.Rights = (set, _) => set == "sprk_projects" ? AccessRights.Read | AccessRights.Write : AccessRights.Read | AccessRights.Write | AccessRights.AppendTo;

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Forbidden, "the rights are asked before a pending decision is named");
        result.Code.Should().Be(InvoiceRejectReturnCodes.NotAuthorized);
        AssertNothingWritten(w);
    }

    [Theory(DisplayName = "ISS-031 round 2 control: on a stranded Override, a caller who is not an eligible approver gets 403 not_eligible and no 409; nothing written")]
    [MemberData(nameof(Kinds))]
    public async Task StrandedOverride_IneligibleCaller_Is403NotEligible(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        await RecordOverrideAsync(w, CarolUser);

        var result = await Decide(w, kind, CarolUser); // Carol recorded the Override but is assigned nowhere on the core record

        result.Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        result.Code.Should().Be(InvoiceRejectReturnCodes.NotEligible);
        AssertNothingWritten(w);
    }

    [Theory(DisplayName = "ISS-031 round 2 control: an invoice already at the verb's target (the claimant's decision stranded before its record) with an Override record goes through the repeat path, which the guard does not block; the claimant's repeat finishes with one record")]
    [MemberData(nameof(Kinds))]
    public async Task InvoiceAtTarget_WithAnOverrideRecord_TheRepeatFinishes(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        w.FaultsOnce["identity"] = new InvalidOperationException("dataverse down"); // the recorder's contact read: the status moved, no record
        (await Decide(w, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        w.InvoiceStatus.Should().Be(kind == InvoiceDecisionKind.Reject ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm);
        await RecordOverrideAsync(w, CarolUser);

        var repeat = await Decide(w, kind, AliceUser);

        repeat.Outcome.Should().Be(InvoiceDecisionOutcome.Decided, "CompleteOrNoOpAsync finishes the claimant's decision; the guard is not on that path");
        repeat.Code.Should().NotBe(InvoiceRejectReturnCodes.OverridePending);
        w.RecordsOf(kind == InvoiceDecisionKind.Reject ? "reject-invoice" : "return-to-firm").Should().ContainSingle();

        var other = await Decide(w, kind, BobUser);
        other.Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided, "anyone else's repeat is a no-op, not a 409");
    }

    // ── Must not fire ──

    [Theory(DisplayName = "ISS-031 round 1 must-not-fire: a Procedure-held invoice with NO Override record is rejected or returned as before")]
    [MemberData(nameof(Kinds))]
    public async Task NoOverrideRecord_DecidesAsBefore(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        w.InvoiceStatus.Should().Be(kind == InvoiceDecisionKind.Reject ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm);
    }

    [Theory(DisplayName = "ISS-031 round 1 must-not-fire: after the Override FINISHED (released: reason Overridden and its stamp) the invoice is rejected or returned as before")]
    [MemberData(nameof(Kinds))]
    public async Task FinishedOverride_DecidesAsBefore(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        var recordId = await RecordOverrideAsync(w, CarolUser);
        Release(w, recordId, CarolUser);

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        w.InvoiceStatus.Should().Be(kind == InvoiceDecisionKind.Reject ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm);
        OverrideRecords(w).Should().Be(1);
    }

    // ── Approve cannot reach it ──

    [Fact(DisplayName = "ISS-031 round 1: approve is not reachable on a stranded Procedure-hold Override: the Procedure hold refuses it until the release lands (control)")]
    public async Task StrandedOverride_ApproveIsRefusedByTheProcedureHold()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Pending, 400m);
        HoldOwnSubmissionForProcedures(w);
        await RecordOverrideAsync(w, CarolUser);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held);
        result.Code.Should().Be(InvoiceApprovalCodes.ProcedureHeld);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    // ── A lookup fault ──

    [Theory(DisplayName = "ISS-031 round 1: a fault on the Override record lookup is 503 decision_unavailable (failed step override-record) and writes nothing")]
    [MemberData(nameof(Kinds))]
    public async Task AFaultOnTheOverrideLookup_Is503_AndWritesNothing(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        HoldOwnSubmissionForProcedures(w);
        await RecordOverrideAsync(w, CarolUser);
        w.H.LookupFault = new TimeoutException("dataverse");

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Unavailable, "the endpoint answers 503");
        result.Code.Should().Be(InvoiceRejectReturnCodes.Unavailable);
        result.FailedStep.Should().Be("override-record");
        AssertNothingWritten(w);
    }
}
