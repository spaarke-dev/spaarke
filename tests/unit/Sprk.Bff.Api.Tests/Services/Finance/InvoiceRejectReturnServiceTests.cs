using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 104 (FR-36, FR-30, FR-35; D-43; 074 K-1 to K-5; EB-D-171): <see cref="InvoiceRejectReturnService"/> over the in-memory
/// Dataverse (<see cref="ApprovalWorld"/>) with the REAL resolver, recorder and writer harness, so each test proves the stored state (invoice
/// status, portion) and the ledger (Decision Records), not the calls made.
/// </summary>
/// <remarks>
/// <para><b>Seeding proofs (task 104).</b> Each guard below was disabled in <see cref="InvoiceRejectReturnService"/> and the named tests went
/// red; recorded in <c>projects/ebilling-r1/notes/104-reject-return.md</c>. Eligibility: <c>NotEligible_Is403_AndWritesNothing</c>,
/// <c>ContactSpoof_SystemUserLinkNeverGrants</c>. Reason: <c>NoReason_Is400_AndWritesNothing</c>. Held: <c>Held_Is409_AndWritesNothing</c>.
/// Close authorization: <c>NoCloseRights_IsRefused_AndWritesNothing</c>. Failed-close count: <c>FailedClose_IsIncomplete_AndTheRepeatFinishesIt</c>.
/// Single core: <c>TwoCoreLookups_AreRefused_BeforeAnyWrite</c>. Claim: <c>ConcurrentDecisions_LockDefeated_ExactlyOneRecord</c>.</para>
/// </remarks>
public sealed class InvoiceRejectReturnServiceTests
{
    public static TheoryData<InvoiceDecisionKind> Kinds => new() { InvoiceDecisionKind.Reject, InvoiceDecisionKind.Return };

    private static Task<InvoiceDecisionResult> Decide(ApprovalWorld w, InvoiceDecisionKind kind, Guid user, string? reason = "A reason") =>
        kind == InvoiceDecisionKind.Reject ? w.RejectAs(user, reason) : w.ReturnAs(user, reason);

    private static string ActionOf(InvoiceDecisionKind kind) => kind == InvoiceDecisionKind.Reject ? "reject-invoice" : "return-to-firm";

    private static int InvoiceStatusOf(InvoiceDecisionKind kind) =>
        kind == InvoiceDecisionKind.Reject ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm;

    private static int PortionStatusOf(InvoiceDecisionKind kind) =>
        kind == InvoiceDecisionKind.Reject ? InvoiceApprovalService.PortionRejected : InvoiceApprovalService.PortionReturned;

    private static InvoiceLifecycleStatus TargetOf(InvoiceDecisionKind kind) =>
        kind == InvoiceDecisionKind.Reject ? InvoiceLifecycleStatus.Rejected : InvoiceLifecycleStatus.ReturnedToFirm;

    private static List<Entity> RecordsOfEither(ApprovalWorld w) =>
        [.. w.RecordsOf("reject-invoice"), .. w.RecordsOf("return-to-firm"), .. w.RecordsOf("approve-invoice")];

    private static void AssertNothingWritten(ApprovalWorld w)
    {
        w.Writes.Should().Be(0, "a refused call writes nothing");
        w.Transitions.Should().Be(0);
        RecordsOfEither(w).Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse();
        w.Portion.Contains("sprk_decidedon").Should().BeFalse();
    }

    // ── The happy paths ──

    [Theory(DisplayName = "104 FR-36: an eligible approver rejects / returns - 014 moves the status, the portion stores decider and time, and ONE Decision Record names the approver and the reason")]
    [MemberData(nameof(Kinds))]
    public async Task Decide_MovesRecordsOnce(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Pending, 400m);

        var result = await Decide(w, kind, AliceUser, "  Not what the engagement letter says  ");

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        result.Code.Should().Be("ok");
        w.InvoiceStatus.Should().Be(InvoiceStatusOf(kind));
        w.Lifecycle.Verify(l => l.TransitionAsync(w.InvoiceId, TargetOf(kind), It.IsAny<CancellationToken>()), Times.Once);
        w.Transitions.Should().Be(1);

        var portion = w.Portion;
        portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(PortionStatusOf(kind));
        portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice, "the decider's contact");
        portion.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(Now.UtcDateTime);
        portion.Contains("sprk_approvedamount").Should().BeFalse("no approved amount is frozen by a reject or a return");
        portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(result.DecisionRecordId!.Value);

        var record = w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(AliceUser, "the decider is the authenticated caller");
        record.GetAttributeValue<string>("sprk_reason").Should().Be("Not what the engagement letter says", "stored trimmed, the one place the firm notice reads it from");
        RecordsOfEither(w).Should().ContainSingle("exactly one Decision Record across all invoice decisions");
        w.Corrections.Should().BeEmpty("the lines are not decided by a reject or a return");
    }

    [Fact(DisplayName = "104: the decision id is derived from the portion and the verb - never from the request, and different for reject, return and approve")]
    public void DecisionIds_AreDerivedFromThePortion()
    {
        var portion = Guid.NewGuid();

        InvoiceRejectReturnService.RejectDecisionIdFor(portion).Should().Be(InvoiceRejectReturnService.RejectDecisionIdFor(portion));
        InvoiceRejectReturnService.RejectDecisionIdFor(portion).Should().NotBe(InvoiceRejectReturnService.ReturnDecisionIdFor(portion));
        InvoiceRejectReturnService.RejectDecisionIdFor(portion).Should().NotBe(InvoiceApprovalService.DecisionIdFor(portion));
        InvoiceRejectReturnService.ReturnDecisionIdFor(portion).Should().NotBe(InvoiceRejectReturnService.ReturnDecisionIdFor(Guid.NewGuid()));
    }

    [Theory(DisplayName = "104 D-40: any one eligible approver (Assigned To Internal as well as Assigned Attorney 1) may reject or return")]
    [MemberData(nameof(Kinds))]
    public async Task AnyOneEligibleApprover_Decides(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();

        (await Decide(w, kind, BobUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(BobUser);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Bob);
    }

    [Theory(DisplayName = "104 FR-36: a Rejected or Returned invoice is outside the rollup set (CountedInvoiceStatuses) and the pending set")]
    [MemberData(nameof(Kinds))]
    public async Task DecidedInvoice_IsExcludedFromTheRollupSets(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        (await Decide(w, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);

        FinanceBaseValues.CountedInvoiceStatuses.Should().NotContain(w.InvoiceStatus, "actual spend counts Approved, Sent to AP and Paid only (D-103)");
        FinanceBaseValues.PendingInvoiceStatuses.Should().NotContain(w.InvoiceStatus, "pending spend counts In Review only (D-104)");
        FinanceBaseValues.CountedInvoiceStatuses.Should().NotContain([InvoiceStatusCodes.Rejected, InvoiceStatusCodes.ReturnedToFirm]);
    }

    // ── FR-36: the reason ──

    [Theory(DisplayName = "104 FR-36 SEEDED: a missing, empty or blank reason is 400 ebilling.reason_required and WRITES NOTHING (no lock, no read)")]
    [InlineData(InvoiceDecisionKind.Reject, null)]
    [InlineData(InvoiceDecisionKind.Reject, "")]
    [InlineData(InvoiceDecisionKind.Reject, "   ")]
    [InlineData(InvoiceDecisionKind.Return, null)]
    [InlineData(InvoiceDecisionKind.Return, "")]
    [InlineData(InvoiceDecisionKind.Return, "\t \n")]
    public async Task NoReason_Is400_AndWritesNothing(InvoiceDecisionKind kind, string? reason)
    {
        var w = new ApprovalWorld();

        var result = await Decide(w, kind, AliceUser, reason);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.InvalidRequest);
        result.Code.Should().Be("ebilling.reason_required");
        AssertNothingWritten(w);
        w.Locks.Acquired.Should().BeEmpty();
    }

    [Theory(DisplayName = "104 FR-36: a reason over the column's length is 400 ebilling.reason_too_long and writes nothing")]
    [MemberData(nameof(Kinds))]
    public async Task ReasonTooLong_Is400(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();

        var result = await Decide(w, kind, AliceUser, new string('x', InvoiceRejectReturnService.ReasonMaxLength + 1));

        result.Code.Should().Be("ebilling.reason_too_long");
        AssertNothingWritten(w);
    }

    // ── Who may decide (FR-35 / F-7: live, by the caller's OWN contact) ──

    [Theory(DisplayName = "104 FR-35 SEEDED: a caller who is not an eligible approver gets NotEligible (403) and NOTHING is written")]
    [MemberData(nameof(Kinds))]
    public async Task NotEligible_Is403_AndWritesNothing(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean);

        var result = await Decide(w, kind, CarolUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        result.Code.Should().Be("ebilling.approver.not_eligible");
        AssertNothingWritten(w);
    }

    [Theory(DisplayName = "104 EB-D-171 SEEDED: a contact whose sprk_systemuser points at the caller does NOT make the caller eligible; nothing is written")]
    [MemberData(nameof(Kinds))]
    public async Task ContactSpoof_SystemUserLinkNeverGrants(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        var eve = Guid.NewGuid();
        w.Put(Contact(eve, "Eve Evans", CarolUser));
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", eve);

        var result = await Decide(w, kind, CarolUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        AssertNothingWritten(w);
    }

    [Theory(DisplayName = "104 FR-35: the stored eligible set never grants - a caller in the stored N:N but no longer assigned is refused; the newly assigned person is eligible at once")]
    [MemberData(nameof(Kinds))]
    public async Task StoredSetNeverGrants_TheLiveValueDecides(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        await w.Resolver().RefreshInvoiceAsync(w.InvoiceId, CancellationToken.None);
        w.StoredApprovers.Should().Contain(Alice);
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol);
        w.WriteLog.Clear();

        (await Decide(w, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        AssertNothingWritten(w);
        (await Decide(w, kind, CarolUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
    }

    [Theory(DisplayName = "104 FR-14: when nobody is eligible (the only assigned contact has no user), nobody may decide - never 'anyone'")]
    [MemberData(nameof(Kinds))]
    public async Task NoEligibleApprover_NobodyMayDecide(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.Core["sprk_billingapproverrole"] = new OptionSetValueCollection([new OptionSetValue((int)BillingApproverRole.AssignedParalegal1)]);

        foreach (var user in new[] { AliceUser, BobUser, CarolUser })
        {
            (await Decide(w, kind, user)).Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        }

        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "104 EB-D-171 fail-closed: a caller with no resolvable contact is 403; an identity fault is 503; a core-record read fault is 503; none writes")]
    public async Task UnresolvableIdentity_AndReadFaults_FailClosed()
    {
        var none = new ApprovalWorld();
        none.ContactOfUser.Remove(AliceUser);
        (await none.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.NotEligible);
        AssertNothingWritten(none);

        var identity = new ApprovalWorld();
        identity.FaultsOnce["caller-identity"] = new InvalidOperationException("dataverse down");
        var unknown = await identity.ReturnAs(AliceUser);
        unknown.Outcome.Should().Be(InvoiceDecisionOutcome.Unavailable);
        unknown.FailedStep.Should().Be("identity");
        AssertNothingWritten(identity);

        var eligibility = new ApprovalWorld();
        eligibility.FaultsOnce["read:contact"] = new InvalidOperationException("dataverse down");
        (await eligibility.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Unavailable);
        AssertNothingWritten(eligibility);
    }

    // ── 194b: exactly one core lookup, checked before any claim or write ──

    [Theory(DisplayName = "104 194b SEEDED: an invoice with two core lookups is refused before anything is written; one with none likewise")]
    [MemberData(nameof(Kinds))]
    public async Task TwoCoreLookups_AreRefused_BeforeAnyWrite(InvoiceDecisionKind kind)
    {
        var two = new ApprovalWorld();
        two.Invoice["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());
        var refused = await Decide(two, kind, AliceUser);
        refused.Outcome.Should().Be(InvoiceDecisionOutcome.Failed);
        refused.Code.Should().Be("ebilling.approval.core_unresolved");
        AssertNothingWritten(two);

        var none = new ApprovalWorld();
        none.Invoice.Attributes.Remove("sprk_project");
        (await Decide(none, kind, AliceUser)).Code.Should().Be("ebilling.approval.core_unresolved");
        AssertNothingWritten(none);
    }

    [Fact(DisplayName = "104 074 K-3: the recorder's filing read runs before the claim - a fault is 503 and nothing is written")]
    public async Task FilingReadFault_IsUnavailable_AndWritesNothing()
    {
        var w = new ApprovalWorld();
        w.FaultsOnce["stamps"] = new InvalidOperationException("dataverse down");

        var result = await w.ReturnAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Unavailable);
        result.FailedStep.Should().Be("filing");
        AssertNothingWritten(w);
    }

    // ── FR-36 / FR-06: only from In Review ──

    [Theory(DisplayName = "104 FR-36: an invoice that is not In Review (Approved, Sent to AP, Paid, Cancelled) is 409 not_in_review and writes nothing")]
    [InlineData(InvoiceDecisionKind.Reject, InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceDecisionKind.Return, InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceDecisionKind.Reject, InvoiceStatusCodes.SentToAp)]
    [InlineData(InvoiceDecisionKind.Return, InvoiceStatusCodes.Paid)]
    [InlineData(InvoiceDecisionKind.Reject, InvoiceStatusCodes.Cancelled)]
    public async Task NotInReview_Is409(InvoiceDecisionKind kind, int status)
    {
        var w = new ApprovalWorld(invoiceStatus: status);

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.NotInReview);
        result.Code.Should().Be("ebilling.invoice.not_in_review");
        w.Writes.Should().Be(0);
        w.Transitions.Should().Be(0);
        RecordsOfEither(w).Should().BeEmpty();
    }

    [Fact(DisplayName = "104 FR-36: the OTHER final decision is not a repeat - return after a reject, and reject after a return, are 409 and record nothing more")]
    public async Task TheOtherFinalDecision_Is409()
    {
        var rejected = new ApprovalWorld();
        (await rejected.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        var writes = rejected.Writes;
        (await rejected.ReturnAs(BobUser)).Outcome.Should().Be(InvoiceDecisionOutcome.NotInReview);
        rejected.Writes.Should().Be(writes);
        rejected.RecordsOf("return-to-firm").Should().BeEmpty();

        var returned = new ApprovalWorld();
        (await returned.ReturnAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        (await returned.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.NotInReview);
        returned.RecordsOf("reject-invoice").Should().BeEmpty();
        returned.InvoiceStatus.Should().Be(InvoiceStatusCodes.ReturnedToFirm);
    }

    [Theory(DisplayName = "104: an invoice approved first cannot then be rejected or returned (and the reverse): one decision per invoice")]
    [MemberData(nameof(Kinds))]
    public async Task AfterApprove_NoRejectOrReturn(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);

        (await Decide(w, kind, BobUser)).Outcome.Should().Be(InvoiceDecisionOutcome.NotInReview);
        w.ApproveRecords.Should().ContainSingle();
        w.RecordsOf(ActionOf(kind)).Should().BeEmpty();

        var later = new ApprovalWorld();
        (await Decide(later, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        var approve = await later.ApproveAs(BobUser);
        approve.Outcome.Should().Be(ApprovalOutcome.Conflict, "103: the portion already records another decision");
        approve.Code.Should().Be("ebilling.approval.already_decided");
        later.ApproveRecords.Should().BeEmpty();
    }

    [Theory(DisplayName = "104 SEEDED FR-20/D-101: a held invoice (blocked submission) is 409 held, writes nothing; the way out is 105's override or return of the submission")]
    [MemberData(nameof(Kinds))]
    public async Task Held_Is409_AndWritesNothing(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.Hold();

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Held);
        result.Code.Should().Be("ebilling.invoice.held");
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "104 D-101: 049's automatic rejection (Rejected, no approval portion) - reject is a no-op with no record, return is 409; nothing is written")]
    public async Task AutoRejectedInvoice_RejectIsANoOp_ReturnIs409()
    {
        var w = new ApprovalWorld(invoiceStatus: InvoiceStatusCodes.Rejected);
        w.Rows.Remove(("sprk_invoiceapproval", w.ApprovalId));

        var reject = await w.RejectAs(AliceUser);
        var @return = await w.ReturnAs(AliceUser);

        reject.Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided);
        reject.DecisionRecordId.Should().BeNull();
        @return.Outcome.Should().Be(InvoiceDecisionOutcome.NotInReview);
        w.Writes.Should().Be(0);
        RecordsOfEither(w).Should().BeEmpty();
    }

    [Fact(DisplayName = "104 105: an invoice 105 already returned (blocked submission returned, portion still Pending) - return is a no-op with NO second return record")]
    public async Task InvoiceReturnedBy105_ReturnIsANoOp()
    {
        var w = new ApprovalWorld(invoiceStatus: InvoiceStatusCodes.ReturnedToFirm);

        var result = await w.ReturnAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided);
        w.Writes.Should().Be(0);
        RecordsOfEither(w).Should().BeEmpty();
        (await w.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.NotInReview);
    }

    [Fact(DisplayName = "104: no approval portion, two portions, a decided portion on an In Review invoice and an unknown status are 409 and write nothing")]
    public async Task PortionProblems_AreRefused()
    {
        var none = new ApprovalWorld();
        none.Rows.Remove(("sprk_invoiceapproval", none.ApprovalId));
        (await none.RejectAs(AliceUser)).Code.Should().Be("ebilling.approval.missing");
        none.Writes.Should().Be(0);

        var two = new ApprovalWorld();
        two.Put(new Entity("sprk_invoiceapproval", Guid.NewGuid())
        {
            ["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionPending),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", two.InvoiceId),
            ["versionnumber"] = 1L,
        });
        (await two.ReturnAs(AliceUser)).Code.Should().Be("ebilling.approval.ambiguous");
        two.Writes.Should().Be(0);

        var decided = new ApprovalWorld();
        decided.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionApproved);
        (await decided.RejectAs(AliceUser)).Code.Should().Be("ebilling.approval.already_decided");
        decided.Writes.Should().Be(0);

        var unknown = new ApprovalWorld();
        unknown.Portion.Attributes.Remove("sprk_approvalstatus");
        (await unknown.ReturnAs(AliceUser)).Code.Should().Be("ebilling.approval.status_unknown");
        unknown.Writes.Should().Be(0);
        unknown.RecordsOf("return-to-firm").Should().BeEmpty();
    }

    // ── Repeats ──

    [Theory(DisplayName = "104 FR-36: a repeat - by the same or another eligible approver, even with another reason - is a no-op with no new Decision Record and no write")]
    [MemberData(nameof(Kinds))]
    public async Task Repeat_IsANoOp_WithOneRecord(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        var first = await Decide(w, kind, AliceUser, "First reason");
        var writes = w.Writes;

        var again = await Decide(w, kind, AliceUser, "First reason");
        var other = await Decide(w, kind, BobUser, "A different reason");

        first.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        again.Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided);
        again.DecisionRecordId.Should().Be(first.DecisionRecordId);
        other.Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided);
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle();
        w.Writes.Should().Be(writes, "a no-op writes nothing");
        w.Transitions.Should().Be(1);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice);
    }

    [Theory(DisplayName = "104 FR-30 / K-3: a failure between the transition and the record is recovered by the same approver's repeat, with exactly ONE record")]
    [InlineData(InvoiceDecisionKind.Reject, "portion")]
    [InlineData(InvoiceDecisionKind.Reject, "identity")]
    [InlineData(InvoiceDecisionKind.Reject, "link")]
    [InlineData(InvoiceDecisionKind.Reject, "transition-after-commit")]
    [InlineData(InvoiceDecisionKind.Return, "portion")]
    [InlineData(InvoiceDecisionKind.Return, "identity")]
    [InlineData(InvoiceDecisionKind.Return, "link")]
    [InlineData(InvoiceDecisionKind.Return, "transition-after-commit")]
    public async Task FailureAfterTheTransition_RepeatBySameApprover_RecordsOnce(InvoiceDecisionKind kind, string step)
    {
        var w = new ApprovalWorld();
        if (step == "transition-after-commit")
        {
            w.TransitionThrowsAfterCommit = true;
        }
        else
        {
            w.FaultsOnce[step] = new InvalidOperationException("dataverse down");
        }

        var failed = await Decide(w, kind, AliceUser);

        failed.Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        failed.Code.Should().Be("ebilling.invoice.decision_incomplete");
        w.InvoiceStatus.Should().Be(InvoiceStatusOf(kind), "the transition happened");

        // Another approver in between: a no-op that records nothing (the claimant's record is the claimant's).
        (await Decide(w, kind, BobUser)).Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided);
        w.RecordsOf(ActionOf(kind)).Count.Should().BeLessThanOrEqualTo(1);

        var retried = await Decide(w, kind, AliceUser);

        retried.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        var record = w.RecordsOf(ActionOf(kind)).Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(AliceUser, "attributed to the approver whose transition succeeded");
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(PortionStatusOf(kind));
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(retried.DecisionRecordId!.Value);
        w.Transitions.Should().Be(1, "the repeat does not transition again");

        (await Decide(w, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.AlreadyDecided);
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle();
    }

    [Theory(DisplayName = "104 K2: a refused 014 transition is 409, records nothing, and clears the claim it wrote (nobody is blocked by it)")]
    [MemberData(nameof(Kinds))]
    public async Task TransitionRefused_RecordsNothing_AndClearsTheClaim(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.Lifecycle.Setup(l => l.TransitionAsync(w.InvoiceId, TargetOf(kind), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceTransitionResult(InvoiceTransitionOutcome.Refused, TargetOf(kind), InvoiceLifecycleStatus.InReview, "invoice-lifecycle.transition-not-allowed"));

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Conflict);
        result.Code.Should().Be("ebilling.invoice.decision_refused");
        RecordsOfEither(w).Should().BeEmpty();
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse("the claim is cleared");
        w.Portion.Contains("sprk_decidedon").Should().BeFalse();
        w.WriteLog.Count(e => e == "claim:sprk_invoiceapproval").Should().Be(2, "the claim, then its conditional clear");
    }

    [Fact(DisplayName = "104 K1: another approver's unfinished claim is 409 claimed and nothing is written over it; the claimant's own repeat proceeds")]
    public async Task ClaimedByAnother_Is409()
    {
        var w = new ApprovalWorld();
        w.Portion["sprk_decidedby"] = new EntityReference("contact", Bob);
        w.Portion["sprk_decidedon"] = Now.UtcDateTime.AddMinutes(-1);

        var result = await w.RejectAs(AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Conflict);
        result.Code.Should().Be("ebilling.approval.claimed");
        w.Writes.Should().Be(0);
        w.Transitions.Should().Be(0);

        (await w.RejectAs(BobUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided, "the claimant completes it");
    }

    // ── 074 K-1 / K-4: exactly once, concurrently ──

    [Fact(DisplayName = "104 K-1 SEEDED: a reject and a return at once with the lock defeated (two decision ids) - the conditional claim lets ONE through; exactly ONE record across both verbs")]
    public async Task ConcurrentDecisions_LockDefeated_ExactlyOneRecord()
    {
        var w = new ApprovalWorld();
        w.Locks.AlwaysGrant = true;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        w.BeforeClaim = () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.SetResult();
            }

            return both.Task;
        };

        var results = await Task.WhenAll(Task.Run(() => w.RejectAs(AliceUser)), Task.Run(() => w.ReturnAs(BobUser)));

        results.Select(r => r.Outcome).Should().BeEquivalentTo([InvoiceDecisionOutcome.Decided, InvoiceDecisionOutcome.Conflict]);
        results.Single(r => r.Outcome == InvoiceDecisionOutcome.Conflict).Code.Should().Be("ebilling.approval.changed");
        RecordsOfEither(w).Should().ContainSingle("one Decision Record, whichever verb won");
        w.Transitions.Should().Be(1);
        w.InvoiceStatus.Should().BeOneOf(InvoiceStatusCodes.Rejected, InvoiceStatusCodes.ReturnedToFirm);
    }

    [Theory(DisplayName = "104 K-1: the same decision twice at once, lock defeated: one Decided, one refused, ONE record")]
    [MemberData(nameof(Kinds))]
    public async Task ConcurrentSameDecision_ExactlyOneRecord(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.Locks.AlwaysGrant = true;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        w.BeforeClaim = () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.SetResult();
            }

            return both.Task;
        };

        var results = await Task.WhenAll(Task.Run(() => Decide(w, kind, AliceUser)), Task.Run(() => Decide(w, kind, AliceUser)));

        results.Count(r => r.Outcome == InvoiceDecisionOutcome.Decided).Should().Be(1);
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle();
    }

    [Fact(DisplayName = "104 K-4: another decision holding the invoice's lock (an approve, an override) is Busy; a lock fault is Unavailable; neither writes")]
    public async Task Locks_BusyAndFault()
    {
        var w = new ApprovalWorld();
        w.Locks.Hold(SubmissionOverrideService.LockKey(w.InvoiceId));
        (await w.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Busy);

        var line = new ApprovalWorld();
        line.Locks.Hold(InvoiceLineDecisionService.LockKeyFor(line.InvoiceId));
        (await line.ReturnAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Busy);
        line.Locks.IsHeld(SubmissionOverrideService.LockKey(line.InvoiceId)).Should().BeFalse("the first lock is released when the second is refused");

        var faulted = new ApprovalWorld();
        faulted.Locks.Fault = new InvalidOperationException("redis down");
        (await faulted.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Unavailable);

        AssertNothingWritten(w);
        AssertNothingWritten(line);
        AssertNothingWritten(faulted);
    }

    // ── D-122: the Signals ──

    [Theory(DisplayName = "104 D-122: a decision stores the open Signals of the invoice AND its lines (created by the decision time) with its record and closes them as Acted by it")]
    [MemberData(nameof(Kinds))]
    public async Task Decide_ClosesTheInvoiceAndLineSignals(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Flagged);
        var open = w.AddInvoiceSignal("EB-DUP");
        var acknowledged = w.AddInvoiceSignal("EB-TOTAL", SignalWriter.SignalStatusAcknowledged);
        var onLine = w.AddLineSignal(line.Id, "EB-RATE");
        var resolved = w.AddInvoiceSignal("EB-OLD", SignalWriter.SignalStatusResolved);
        var later = w.AddInvoiceSignal("EB-LATE", createdOn: Now.UtcDateTime.AddMinutes(5));

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        var stored = (await w.Recorder().FindStoredResolvedSetAsync(ActionOf(kind), w.InvoiceId, result.DecisionId!.Value))!;
        stored.RecordId.Should().Be(result.DecisionRecordId!.Value);
        stored.Signals.Select(s => s.SignalId).Should().BeEquivalentTo([open, acknowledged, onLine]);
        w.ClosedBy.Keys.Should().BeEquivalentTo([open, acknowledged, onLine]);
        w.ClosedBy.Values.Should().OnlyContain(id => id == result.DecisionRecordId);
        w.SignalStatus(onLine).Should().Be(SignalWriter.SignalStatusResolved);
        w.SignalStatus(resolved).Should().Be(SignalWriter.SignalStatusResolved, "untouched");
        w.SignalStatus(later).Should().Be(SignalWriter.SignalStatusOpen, "raised after the decision time: not this decision's");
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(result.DecisionRecordId!.Value);
    }

    [Theory(DisplayName = "104 D-122 SEEDED: a caller without the rights to close the Signals is refused (403) and NOTHING is written")]
    [MemberData(nameof(Kinds))]
    public async Task NoCloseRights_IsRefused_AndWritesNothing(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        var signal = w.AddInvoiceSignal();
        // Write on the invoice but no AppendTo on the core record.
        w.Rights = (set, _) => set == "sprk_projects" ? AccessRights.Read | AccessRights.Write : AccessRights.Read | AccessRights.Write | AccessRights.AppendTo;

        var result = await Decide(w, kind, AliceUser);

        result.Outcome.Should().Be(InvoiceDecisionOutcome.Forbidden);
        result.Code.Should().Be("ebilling.decision.not_authorized");
        AssertNothingWritten(w);
        w.Closes.Should().BeEmpty();
        w.SignalStatus(signal).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Theory(DisplayName = "104 D-122 SEEDED (#1911 F2): a close that fails (a timeout or a change since the read) is Incomplete at 'signals', never Decided; the repeat finishes the closes with NO second record")]
    [InlineData(InvoiceDecisionKind.Reject, true)]
    [InlineData(InvoiceDecisionKind.Reject, false)]
    [InlineData(InvoiceDecisionKind.Return, true)]
    [InlineData(InvoiceDecisionKind.Return, false)]
    public async Task FailedClose_IsIncomplete_AndTheRepeatFinishesIt(InvoiceDecisionKind kind, bool timeout)
    {
        var w = new ApprovalWorld();
        var closes = w.AddInvoiceSignal("EB-DUP");
        var fails = w.AddInvoiceSignal("EB-TOTAL");
        // LeftAsIs marks "throw a timeout" in the world's fake; ChangedSinceRead is answered as is.
        w.CloseFailsOnce[fails] = timeout ? SignalAutomatedCloseOutcome.LeftAsIs : SignalAutomatedCloseOutcome.ChangedSinceRead;

        var first = await Decide(w, kind, AliceUser);

        first.Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        first.Code.Should().Be("ebilling.invoice.decision_incomplete");
        first.FailedStep.Should().Be("signals");
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle();
        w.SignalStatus(closes).Should().Be(SignalWriter.SignalStatusResolved, "the other close still ran");
        w.SignalStatus(fails).Should().Be(SignalWriter.SignalStatusOpen);
        w.Portion.Contains("sprk_decisionrecord").Should().BeFalse("not linked until the closes are done");

        var repeat = await Decide(w, kind, AliceUser);

        repeat.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle("the repeat finds the record; it never writes a second");
        repeat.DecisionRecordId.Should().Be(w.ClosedBy[closes]);
        w.SignalStatus(fails).Should().Be(SignalWriter.SignalStatusResolved, "the remaining close is finished");
        w.ClosedBy[fails].Should().Be(w.ClosedBy[closes], "closed by the same record");
        w.Closes.Where(c => c.SignalId == closes).Select(c => c.Outcome)
            .Should().Equal(SignalAutomatedCloseOutcome.Closed, SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(repeat.DecisionRecordId!.Value);
        w.Transitions.Should().Be(1);
    }

    [Theory(DisplayName = "104 D-122 SEEDED (#1911 F2): a finish over an already-written record whose close fails is Incomplete at 'signals', never Decided")]
    [MemberData(nameof(Kinds))]
    public async Task FailedClose_OverAnExistingRecord_IsIncomplete(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        var closes = w.AddInvoiceSignal("EB-DUP");
        var fails = w.AddInvoiceSignal("EB-TOTAL");
        w.CloseFailsOnce[fails] = SignalAutomatedCloseOutcome.ChangedSinceRead;
        (await Decide(w, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);

        // The portion names the record but is not yet marked decided (the defensive branch: a record the finish finds already linked).
        w.Portion["sprk_decisionrecord"] = new EntityReference("sprk_decisionrecord", w.ClosedBy[closes]);
        w.Portion["sprk_approvalstatus"] = new OptionSetValue(InvoiceApprovalService.PortionPending);
        w.CloseFailsOnce[fails] = SignalAutomatedCloseOutcome.ChangedSinceRead;

        var repeat = await Decide(w, kind, AliceUser);

        repeat.Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        repeat.FailedStep.Should().Be("signals");
        w.RecordsOf(ActionOf(kind)).Should().ContainSingle();
        w.SignalStatus(fails).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Theory(DisplayName = "104 D-122: with no Signal on the invoice or its lines, no rights are asked")]
    [MemberData(nameof(Kinds))]
    public async Task NoSignals_NoRightsAsked(InvoiceDecisionKind kind)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Flagged);
        w.Rights = (_, _) => AccessRights.None;

        (await Decide(w, kind, AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        w.Probe.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "104 D-122: the claimant's repeat after the record closes the STORED set - nothing twice, and a Signal raised since stays open")]
    public async Task Replay_AfterTheRecord_UsesTheStoredSet()
    {
        var w = new ApprovalWorld();
        var first = w.AddInvoiceSignal();
        w.FaultsOnce["link"] = new InvalidOperationException("dataverse down");

        (await w.RejectAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        w.ClosedBy.Keys.Should().Equal(first);

        var since = w.AddInvoiceSignal("EB-LATE", createdOn: Now.UtcDateTime.AddMinutes(-30));
        var retried = await w.RejectAs(AliceUser);

        retried.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        w.RecordsOf("reject-invoice").Should().ContainSingle();
        w.Closes.Where(c => c.SignalId == first).Select(c => c.Outcome)
            .Should().Equal(SignalAutomatedCloseOutcome.Closed, SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);
        w.SignalStatus(since).Should().Be(SignalWriter.SignalStatusOpen, "not in the stored set");
    }

    [Fact(DisplayName = "104 D-122: the claimant's repeat BEFORE the record re-derives the set with the frozen decision time - a Signal raised after it is never added")]
    public async Task Replay_BeforeTheRecord_IsBoundedByTheFrozenTime()
    {
        var w = new ApprovalWorld();
        var first = w.AddInvoiceSignal();
        w.FaultsOnce["identity"] = new InvalidOperationException("dataverse down"); // the recorder's contact read: no record written

        (await w.ReturnAs(AliceUser)).Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);
        w.RecordsOf("return-to-firm").Should().BeEmpty();
        w.Closes.Should().BeEmpty();

        w.Clock.Advance(TimeSpan.FromMinutes(10));
        var after = w.AddInvoiceSignal("EB-LATE", createdOn: Now.UtcDateTime.AddMinutes(5));
        var retried = await w.ReturnAs(AliceUser);

        retried.Outcome.Should().Be(InvoiceDecisionOutcome.Decided);
        var stored = (await w.Recorder().FindStoredResolvedSetAsync("return-to-firm", w.InvoiceId, retried.DecisionId!.Value))!;
        stored.Signals.Select(s => s.SignalId).Should().Equal(first);
        w.SignalStatus(after).Should().Be(SignalWriter.SignalStatusOpen);
        w.Portion.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(Now.UtcDateTime, "the decision time frozen by the claim");
    }

    [Fact(DisplayName = "104 K-5: the reason is part of the record's content - a repeat with a different reason after the record exists never writes a second record")]
    public async Task RepeatWithAnotherReason_NeverWritesASecondRecord()
    {
        var w = new ApprovalWorld();
        w.FaultsOnce["link"] = new InvalidOperationException("dataverse down");
        (await w.ReturnAs(AliceUser, "First reason")).Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete);

        var repeat = await w.ReturnAs(AliceUser, "A different reason");

        w.RecordsOf("return-to-firm").Should().ContainSingle();
        w.RecordsOf("return-to-firm").Single().GetAttributeValue<string>("sprk_reason").Should().Be("First reason");
        repeat.Outcome.Should().Be(InvoiceDecisionOutcome.Incomplete, "the writer refuses the same decision id with different content; the approver repeats with the original reason");
        repeat.FailedStep.Should().Be("record");
    }
}
