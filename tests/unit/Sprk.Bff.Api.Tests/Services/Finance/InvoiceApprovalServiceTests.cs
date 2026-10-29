using FluentAssertions;
using Moq;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 103 (FR-35, I-2, FR-30, D-40, D-72, D-78; 074 K-1 to K-5): <see cref="InvoiceApprovalService"/> over the in-memory Dataverse
/// (<see cref="ApprovalWorld"/>) with the REAL resolver, line-decision service, totals service and recorder, so each test proves the stored state
/// (invoice status, portion, lines, correction rows, N:N) and the ledger (Decision Records), not the calls made.
/// </summary>
/// <remarks>
/// <para><b>Seeding proof (task 103 acceptance).</b> <c>NotEligible_Is403_AndWritesNothing</c> fails when the eligibility check in
/// <see cref="InvoiceApprovalService"/> (the <c>set.ForContact(...) is not { } approver</c> refusal) is removed: Carol, who has Write but is not
/// eligible, then approves. <c>NoEligibleApprover_NobodyMayApprove</c> and <c>EligibilityReadFault_FailsClosed</c> fail when the refusal falls
/// back to "anyone". EB-D-171: <c>ContactSpoof_SystemUserLinkNeverGrants</c> fails when eligibility is matched on the contact's
/// <c>sprk_systemuser</c> again. EB-D-172: <c>NoCloseRights_IsRefused_AndWritesNothing</c> fails when the close authorization is removed.
/// Recorded in <c>projects/ebilling-r1/notes/103-approval-worklist.md</c>.</para>
/// </remarks>
public sealed class InvoiceApprovalServiceTests
{
    /// <summary>An extraction JSON as 049 stores it (date, hours, rate, amount, narrative extracted; task and activity coded).</summary>
    private const string Extraction =
        "{\"fields\":{\"Date\":{\"confidence\":0.95,\"combined\":0.9,\"page\":1},\"Hours\":{\"confidence\":0.9,\"page\":1},\"Rate\":{\"confidence\":0.92,\"page\":1},"
        + "\"Quantity\":{\"confidence\":null,\"page\":null},\"Amount\":{\"confidence\":0.99,\"page\":1},\"Narrative\":{\"confidence\":0.8,\"page\":1}},"
        + "\"coding\":{\"status\":\"Coded\",\"confidence\":0.82,\"task\":{\"code\":\"L110\",\"mismatch\":false},\"activity\":{\"code\":\"A101\",\"mismatch\":false},\"expense\":{\"code\":null}}}";

    private static void AssertNothingWritten(ApprovalWorld w)
    {
        w.Writes.Should().Be(0, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse();
    }

    // ── The happy path ──

    [Fact(DisplayName = "103 FR-35: an eligible approver approves - status Approved through 014, the portion stores the frozen amount, decider and time, and ONE Decision Record names the approver")]
    public async Task Approve_ApprovesFreezesAndRecordsOnce()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        w.AddLine(InvoiceLineStatus.Reduced, 600m, approved: 450m);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
        result.DecisionId.Should().Be(InvoiceApprovalService.DecisionIdFor(w.ApprovalId));
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
        w.Transitions.Should().Be(1);

        var portion = w.Portion;
        portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionApproved);
        portion.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(850m, "billed 1,000 less the 150 reduction, frozen at approval");
        portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice, "the decider's contact, from the live eligible set");
        portion.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(Now.UtcDateTime);
        portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(result.DecisionRecordId!.Value);

        var record = w.ApproveRecords.Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(AliceUser, "the approver is the authenticated caller");
        w.Invoice.GetAttributeValue<Money>("sprk_approvedtotal").Value.Should().Be(850m);
    }

    [Fact(DisplayName = "103 D-40: any one eligible approver approves (Assigned To Internal as well as Assigned Attorney 1)")]
    public async Task AnyOneEligibleApprover_Approves()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);

        (await w.ApproveAs(BobUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(BobUser);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Bob);
    }

    // ── Who may approve (FR-35: live, never the stored set) ──

    [Fact(DisplayName = "103 FR-35 SEEDED: a caller who is not an eligible approver gets NotEligible (403) and NOTHING is written")]
    public async Task NotEligible_Is403_AndWritesNothing()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);

        var result = await w.ApproveAs(CarolUser);

        result.Outcome.Should().Be(ApprovalOutcome.NotEligible);
        result.Code.Should().Be("ebilling.approver.not_eligible");
        AssertNothingWritten(w);
        w.Corrections.Should().BeEmpty("not even the Clean lines are accepted");
    }

    [Fact(DisplayName = "103 FR-35: the stored eligible set never grants - a caller in the stored N:N but no longer assigned is refused")]
    public async Task StoredSetNeverGrants_TheLiveValueDecides()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        await w.Resolver().RefreshInvoiceAsync(w.InvoiceId, CancellationToken.None);
        w.StoredApprovers.Should().Contain(Alice);

        // Re-assigned before any refresh: the stored set still names Alice, the live core record does not.
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol);
        w.WriteLog.Clear();

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.NotEligible);
        AssertNothingWritten(w);

        (await w.ApproveAs(CarolUser)).Outcome.Should().Be(ApprovalOutcome.Approved, "the newly assigned person is eligible immediately");
    }

    [Fact(DisplayName = "103 FR-14 SEEDED: when nobody is eligible (the only assigned contact has no user), nobody may approve - never 'anyone'")]
    public async Task NoEligibleApprover_NobodyMayApprove()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Core["sprk_billingapproverrole"] = new OptionSetValueCollection([new OptionSetValue((int)BillingApproverRole.AssignedParalegal1)]);

        foreach (var user in new[] { AliceUser, BobUser, CarolUser })
        {
            (await w.ApproveAs(user)).Outcome.Should().Be(ApprovalOutcome.NotEligible);
        }

        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "103 SEEDED fail-closed: a fault resolving eligibility is Unavailable (503) and writes nothing")]
    public async Task EligibilityReadFault_FailsClosed()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.FaultsOnce["read:contact"] = new InvalidOperationException("dataverse down");

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Unavailable);
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "103 fail-closed: an invoice with no core record is refused before anything is written")]
    public async Task NoCoreRecord_IsRefused()
    {
        var w = new ApprovalWorld();
        w.Invoice.Attributes.Remove("sprk_project");

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Failed);
        result.Code.Should().Be("ebilling.approval.core_unresolved");
        AssertNothingWritten(w);
    }

    // ── I-2 and the other refusals ──

    [Theory(DisplayName = "103 I-2: approve with a Flagged or Pending line is 409 lines_open WITH the counts, and writes nothing")]
    [InlineData(InvoiceLineStatus.Flagged)]
    [InlineData(InvoiceLineStatus.Pending)]
    public async Task OpenLines_Are409WithCounts(InvoiceLineStatus open)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        w.AddLine(open);
        w.AddLine(InvoiceLineStatus.Flagged);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.LinesOpen);
        result.Code.Should().Be("ebilling.approve.lines_open");
        result.OpenLines.Should().Be(open == InvoiceLineStatus.Flagged ? new ApprovalOpenLinesDto(0, 2) : new ApprovalOpenLinesDto(1, 1));
        AssertNothingWritten(w);
        w.Corrections.Should().BeEmpty();
    }

    [Fact(DisplayName = "103 I-2 fail-closed: a line with no recognised status counts as Pending")]
    public async Task UnknownLineStatus_CountsAsPending()
    {
        var w = new ApprovalWorld();
        w.AddLine(status: null);

        (await w.ApproveAs(AliceUser)).OpenLines.Should().Be(new ApprovalOpenLinesDto(1, 0));
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "103 FR-20: a held invoice (blocked submission) is 409 held and writes nothing")]
    public async Task Held_Is409()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Hold();

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held);
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "103: an invoice not In Review (and not Approved) is 409 not_in_review")]
    public async Task NotInReview_Is409()
    {
        var w = new ApprovalWorld(invoiceStatus: InvoiceStatusCodes.Rejected);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.NotInReview);
        w.Writes.Should().Be(0);
        w.Transitions.Should().Be(0);
    }

    [Fact(DisplayName = "103 K2: a refused 014 transition is 409, records nothing, and clears the claim it wrote (so nobody is blocked by it)")]
    public async Task TransitionRefused_RecordsNothing_AndClearsTheClaim()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Lifecycle.Setup(l => l.TransitionAsync(w.InvoiceId, InvoiceLifecycleStatus.Approved, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceTransitionResult(InvoiceTransitionOutcome.Refused, InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.InReview, "invoice-lifecycle.transition-not-allowed"));

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict);
        result.Code.Should().Be("ebilling.approve.refused");
        w.ApproveRecords.Should().BeEmpty();
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Portion.Contains("sprk_decidedby").Should().BeFalse("K2: the claim is cleared");
        w.Portion.Contains("sprk_decidedon").Should().BeFalse();
        w.Portion.Contains("sprk_approvedamount").Should().BeFalse();
        w.WriteLog.Count(e => e == "claim:sprk_invoiceapproval").Should().Be(2, "the claim, then its conditional clear");

        // Another approver is not blocked by the refused attempt (K1 would refuse a live claim).
        w.Lifecycle.Setup(l => l.TransitionAsync(w.InvoiceId, InvoiceLifecycleStatus.Approved, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceTransitionResult(InvoiceTransitionOutcome.Transitioned, InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.InReview, null));
        (await w.ApproveAs(BobUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact(DisplayName = "103 K2: the clear is conditional on the version just read - a portion changed since is left alone")]
    public async Task TransitionRefused_ClaimChangedSince_IsNotCleared()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Lifecycle.Setup(l => l.TransitionAsync(w.InvoiceId, InvoiceLifecycleStatus.Approved, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                // Someone else's write lands on the portion between the claim and the refusal: it is no longer our claim.
                w.Portion["sprk_decidedby"] = new EntityReference("contact", Bob);
                return Task.FromResult(new InvoiceTransitionResult(InvoiceTransitionOutcome.Refused, InvoiceLifecycleStatus.Approved, InvoiceLifecycleStatus.InReview, "invoice-lifecycle.transition-not-allowed"));
            });

        (await w.ApproveAs(AliceUser)).Code.Should().Be("ebilling.approve.refused");

        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Bob, "a claim that is not ours is never cleared");
    }

    // ── EB-D-171: eligible only by the caller's OWN contact ──

    [Fact(DisplayName = "103 EB-D-171 SEEDED: a contact whose sprk_systemuser points at the caller does NOT make the caller eligible; nothing is written")]
    public async Task ContactSpoof_SystemUserLinkNeverGrants()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        // Eve is the assigned attorney; someone set her contact's sprk_systemuser to Carol's user. Carol's own contact is Carol (not assigned).
        var eve = Guid.NewGuid();
        w.Put(Contact(eve, "Eve Evans", CarolUser));
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", eve);

        var result = await w.ApproveAs(CarolUser);

        result.Outcome.Should().Be(ApprovalOutcome.NotEligible);
        result.Code.Should().Be("ebilling.approver.not_eligible");
        AssertNothingWritten(w);
        w.Corrections.Should().BeEmpty();

        var state = (await w.GetAs(CarolUser))!;
        state.CallerEligible.Should().BeFalse("the stored or listed contact never grants");
        state.CanApprove.Should().BeFalse();
        state.EligibleApprovers.Should().Contain("Eve Evans", "the set may list the contact for display");
    }

    [Fact(DisplayName = "103 EB-D-171: the real assignee's OWN contact makes them eligible, whatever that contact's user link says")]
    public async Task OwnContact_Grants()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        // Alice's contact's user link was edited to name Bob's user: the identity service still resolves Alice's user to Alice's contact.
        w.Put(Contact(Alice, "Alice Adams", BobUser));
        w.Core["sprk_assignedtointernal"] = new EntityReference("contact", Dan);

        (await w.ApproveAs(BobUser)).Outcome.Should().Be(ApprovalOutcome.NotEligible, "Bob's own contact is not assigned");
        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice);
    }

    [Fact(DisplayName = "103 EB-D-171 SEEDED fail-closed: a caller with no resolvable contact is 403 not_eligible; an identity fault is 503; neither writes")]
    public async Task UnresolvableIdentity_FailsClosed()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        w.ContactOfUser.Remove(AliceUser);

        var none = await w.ApproveAs(AliceUser);

        none.Outcome.Should().Be(ApprovalOutcome.NotEligible);
        none.Code.Should().Be("ebilling.approver.not_eligible");
        AssertNothingWritten(w);

        var faulted = new ApprovalWorld();
        faulted.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        faulted.FaultsOnce["caller-identity"] = new InvalidOperationException("dataverse down");

        var unknown = await faulted.ApproveAs(AliceUser);

        unknown.Outcome.Should().Be(ApprovalOutcome.Unavailable);
        unknown.FailedStep.Should().Be("identity");
        AssertNothingWritten(faulted);
        faulted.Corrections.Should().BeEmpty();
    }

    // ── EB-D-172: approve closes the invoice-level Signals ──

    [Fact(DisplayName = "103 EB-D-172: approve stores the invoice's open Signals (created by the decision time) with its record and closes them as Acted by it")]
    public async Task Approve_ClosesTheInvoiceSignals()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var open = w.AddInvoiceSignal("EB-DUP");
        var acknowledged = w.AddInvoiceSignal("EB-TOTAL", SignalWriter.SignalStatusAcknowledged);
        var resolved = w.AddInvoiceSignal("EB-OLD", SignalWriter.SignalStatusResolved);
        var later = w.AddInvoiceSignal("EB-LATE", createdOn: Now.UtcDateTime.AddMinutes(5));

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
        var stored = (await w.Recorder().FindStoredResolvedSetAsync("approve-invoice", w.InvoiceId, result.DecisionId!.Value))!;
        stored.RecordId.Should().Be(result.DecisionRecordId!.Value);
        stored.Signals.Select(s => s.SignalId).Should().BeEquivalentTo([open, acknowledged]);
        stored.Signals.Single(s => s.SignalId == acknowledged).PolicyCode.Should().Be("EB-TOTAL");
        w.ClosedBy.Keys.Should().BeEquivalentTo([open, acknowledged]);
        w.ClosedBy.Values.Should().OnlyContain(id => id == result.DecisionRecordId);
        w.SignalStatus(open).Should().Be(SignalWriter.SignalStatusResolved);
        w.SignalStatus(resolved).Should().Be(SignalWriter.SignalStatusResolved, "untouched");
        w.SignalStatus(later).Should().Be(SignalWriter.SignalStatusOpen, "raised after the decision time: not this decision's");
    }

    [Fact(DisplayName = "103 EB-D-172 SEEDED: a caller without the rights to close the invoice's Signals is refused (403) and NOTHING is written")]
    public async Task NoCloseRights_IsRefused_AndWritesNothing()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        var signal = w.AddInvoiceSignal();
        // Write on the invoice but no AppendTo on the core record.
        w.Rights = (set, _) => set == "sprk_projects" ? AccessRights.Read | AccessRights.Write : AccessRights.Read | AccessRights.Write | AccessRights.AppendTo;

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Forbidden);
        result.Code.Should().Be("ebilling.decision.not_authorized");
        AssertNothingWritten(w);
        w.Corrections.Should().BeEmpty("not even the Clean lines are accepted");
        w.Closes.Should().BeEmpty();
        w.SignalStatus(signal).Should().Be(SignalWriter.SignalStatusOpen);
    }

    [Theory(DisplayName = "103 EB-D-172 (#1911 F2): a close that fails (a timeout or a change since the read) is Incomplete at 'signals', never Approved; the repeat finishes the closes with NO second record")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedClose_IsIncomplete_AndTheRepeatFinishesIt(bool timeout)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var closes = w.AddInvoiceSignal("EB-DUP");
        var fails = w.AddInvoiceSignal("EB-TOTAL");
        // LeftAsIs marks "throw a timeout" in the world's fake; ChangedSinceRead is answered as is.
        w.CloseFailsOnce[fails] = timeout ? SignalAutomatedCloseOutcome.LeftAsIs : SignalAutomatedCloseOutcome.ChangedSinceRead;

        var first = await w.ApproveAs(AliceUser);

        first.Outcome.Should().Be(ApprovalOutcome.Incomplete);
        first.Code.Should().Be("ebilling.approval.incomplete");
        first.FailedStep.Should().Be("signals");
        w.ApproveRecords.Should().ContainSingle();
        w.SignalStatus(closes).Should().Be(SignalWriter.SignalStatusResolved, "the other close still ran");
        w.SignalStatus(fails).Should().Be(SignalWriter.SignalStatusOpen);
        w.Portion.Contains("sprk_decisionrecord").Should().BeFalse("not linked until the closes are done");

        var repeat = await w.ApproveAs(AliceUser);

        repeat.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle("the repeat finds the record; it never writes a second");
        repeat.DecisionRecordId.Should().Be(w.ClosedBy[closes]);
        w.SignalStatus(fails).Should().Be(SignalWriter.SignalStatusResolved, "the remaining close is finished");
        w.ClosedBy[fails].Should().Be(w.ClosedBy[closes], "closed by the same record");
        w.Closes.Where(c => c.SignalId == closes).Select(c => c.Outcome)
            .Should().Equal(SignalAutomatedCloseOutcome.Closed, SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(repeat.DecisionRecordId!.Value);
        w.Transitions.Should().Be(1);
    }

    [Fact(DisplayName = "103 EB-D-172 (#1911 F2): when every close succeeds the approve is Approved and linked")]
    public async Task CleanClose_IsApproved()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var signal = w.AddInvoiceSignal();

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
        result.Code.Should().Be("ok");
        w.SignalStatus(signal).Should().Be(SignalWriter.SignalStatusResolved);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Id.Should().Be(result.DecisionRecordId!.Value);
    }

    [Fact(DisplayName = "103 EB-D-172: with no invoice Signal, no rights are asked")]
    public async Task NoSignals_NoRightsAsked()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Rights = (_, _) => AccessRights.None;

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.Probe.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "103 EB-D-172: the claimant's repeat after the record closes the STORED set - nothing twice, and a Signal raised since stays open")]
    public async Task Replay_AfterTheRecord_UsesTheStoredSet()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var first = w.AddInvoiceSignal();
        w.FaultsOnce["link"] = new InvalidOperationException("dataverse down");

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);
        w.ClosedBy.Keys.Should().Equal(first);

        // Raised before the decision time but only now visible (a late writer): the stored set never widens to it.
        var since = w.AddInvoiceSignal("EB-LATE", createdOn: Now.UtcDateTime.AddMinutes(-30));
        var retried = await w.ApproveAs(AliceUser);

        retried.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle();
        w.Closes.Where(c => c.SignalId == first).Select(c => c.Outcome)
            .Should().Equal(SignalAutomatedCloseOutcome.Closed, SignalAutomatedCloseOutcome.AlreadyClosedByThisRecord);
        w.SignalStatus(since).Should().Be(SignalWriter.SignalStatusOpen, "not in the stored set");
        w.Closes.Should().NotContain(c => c.SignalId == since);
    }

    [Fact(DisplayName = "103 EB-D-172: the claimant's repeat BEFORE the record re-derives the set with the frozen decision time - a Signal raised after it is never added")]
    public async Task Replay_BeforeTheRecord_IsBoundedByTheFrozenTime()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var first = w.AddInvoiceSignal();
        w.FaultsOnce["identity"] = new InvalidOperationException("dataverse down"); // the recorder's contact read: no record written

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);
        w.ApproveRecords.Should().BeEmpty();
        w.Closes.Should().BeEmpty();

        w.Clock.Advance(TimeSpan.FromMinutes(10));
        var after = w.AddInvoiceSignal("EB-LATE", createdOn: Now.UtcDateTime.AddMinutes(5));
        var retried = await w.ApproveAs(AliceUser);

        retried.Outcome.Should().Be(ApprovalOutcome.Approved);
        var stored = (await w.Recorder().FindStoredResolvedSetAsync("approve-invoice", w.InvoiceId, retried.DecisionId!.Value))!;
        stored.Signals.Select(s => s.SignalId).Should().Equal(first);
        w.SignalStatus(after).Should().Be(SignalWriter.SignalStatusOpen);
        w.Portion.GetAttributeValue<DateTime>("sprk_decidedon").Should().Be(Now.UtcDateTime, "the decision time frozen by the claim");
    }

    // ── F2: a failed bulk accept is completed, never skipped ──

    [Theory(DisplayName = "103 F2 SEEDED: after the bulk accept failed past its line write, approving again completes it - the line records exist and the corrections are 'Accepted in bulk'")]
    [InlineData("identity")]
    [InlineData("totals")]
    [InlineData("tx:corrections")]
    public async Task RetryAfterAFailedBulkAccept_CompletesIt(string step)
    {
        var w = new ApprovalWorld();
        var clean = w.AddLine(InvoiceLineStatus.Clean, 300m, extraction: Extraction);
        w.AddLine(InvoiceLineStatus.Accepted, 700m, approved: 700m);
        w.FaultsOnce[step] = new InvalidOperationException("dataverse down");

        var failed = await w.ApproveAs(AliceUser);

        failed.Outcome.Should().Be(ApprovalOutcome.Incomplete);
        failed.FailedStep.Should().Be("accept-clean");
        failed.CommittedLineIds.Should().Equal(clean.Id);
        w.Row("sprk_billingevent", clean.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Accepted);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);

        var retried = await w.ApproveAs(AliceUser);

        retried.Outcome.Should().Be(ApprovalOutcome.Approved);
        var lineRecord = w.H.Created.Where(r => r.GetAttributeValue<string>("sprk_actioncode") == "accept-line").Should().ContainSingle().Subject;
        lineRecord.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id.Should().Be(clean.Id);
        (await w.Recorder().FindStoredResolvedSetAsync("accept-line", clean.Id, InvoiceApprovalService.AcceptCleanDecisionIdFor(w.ApprovalId)))
            .Should().NotBeNull("recorded under the approval's bulk decision id");
        var bulk = w.Corrections.Where(r => r.GetAttributeValue<EntityReference>("sprk_invoiceline").Id == clean.Id).ToList();
        bulk.Should().HaveCount(7).And.OnlyContain(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value == 100000002, "Accepted in bulk");
        w.ApproveRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "103 F2: an Accepted line decided by ANOTHER person and not recorded is 409 until it is recorded; nothing is written")]
    public async Task AnotherPersonsUnrecordedLine_Is409()
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        line["sprk_reviewedby"] = new EntityReference("systemuser", BobUser);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict);
        result.Code.Should().Be("ebilling.approve.decision_incomplete");
        result.CommittedLineIds.Should().Equal(line.Id);
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "103 F2: lines decided individually (recorded) are not re-decided by approve")]
    public async Task RecordedLines_AreLeftAlone()
    {
        var w = new ApprovalWorld();
        var line = w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        var decided = await w.LineService().DecideAsync(
            new LineDecisionCommand(w.InvoiceId, Guid.NewGuid(), LineDecisionAction.Accept, null, [new LineDecisionLine(line.Id)], BobUser, Bulk: false, CallerToken),
            CancellationToken.None);
        decided.IsApplied.Should().BeTrue();

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.H.Created.Where(r => r.GetAttributeValue<string>("sprk_actioncode") == "accept-line").Should().ContainSingle("Bob's record only");
    }

    // ── F3: exactly one core record ──

    [Fact(DisplayName = "103 F3 SEEDED: an invoice with two core lookups is refused before anything is written (approve, and GET says not eligible)")]
    public async Task TwoCoreLookups_AreRefused()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        w.Invoice["sprk_matter"] = new EntityReference("sprk_matter", Guid.NewGuid());

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Failed);
        result.Code.Should().Be("ebilling.approval.core_unresolved");
        AssertNothingWritten(w);
        w.Corrections.Should().BeEmpty();

        var state = (await w.GetAs(AliceUser))!;
        state.EligibilityUnavailable.Should().BeTrue();
        state.CanApprove.Should().BeFalse();
    }

    [Fact(DisplayName = "103 F3: the recorder's filing read runs before the claim - a fault is 503 and nothing is written")]
    public async Task FilingReadFault_IsUnavailable()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Clean, extraction: Extraction);
        w.FaultsOnce["stamps"] = new InvalidOperationException("dataverse down");

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Unavailable);
        result.FailedStep.Should().Be("filing");
        AssertNothingWritten(w);
        w.Corrections.Should().BeEmpty();
    }

    // ── F4, K1, K3, K4 ──

    [Fact(DisplayName = "103 F4: GET canApprove is false without the decide right (Write on the invoice), even for an eligible approver")]
    public async Task Get_CanApproveNeedsTheDecideRight()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.CanDecide = false;

        var state = (await w.GetAs(AliceUser))!;

        state.CallerEligible.Should().BeTrue();
        state.CanApprove.Should().BeFalse();

        var noCaller = (await w.Service().GetAsync(w.InvoiceId, AliceUser, null, CancellationToken.None))!;
        noCaller.CanApprove.Should().BeFalse("no caller to ask is no");
    }

    [Fact(DisplayName = "103 K1: another approver's unfinished claim is 409 claimed and nothing is written over it; the claimant's own repeat proceeds")]
    public async Task ClaimedByAnother_Is409()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Portion["sprk_decidedby"] = new EntityReference("contact", Bob);
        w.Portion["sprk_decidedon"] = Now.UtcDateTime.AddMinutes(-1);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict);
        result.Code.Should().Be("ebilling.approval.claimed");
        w.Writes.Should().Be(0);
        w.Transitions.Should().Be(0);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Bob);

        (await w.ApproveAs(BobUser)).Outcome.Should().Be(ApprovalOutcome.Approved, "the claimant completes it");
    }

    [Fact(DisplayName = "103 K3 SEEDED: the approved amount is in the record's Values, so a repeat with a different frozen amount is refused, never answered as recorded")]
    public async Task ApprovedAmount_IsCoveredByTheHash()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.FaultsOnce["link"] = new InvalidOperationException("dataverse down");
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Incomplete);
        w.ApproveRecords.Should().ContainSingle();

        w.Portion["sprk_approvedamount"] = new Money(999m);
        var retried = await w.ApproveAs(AliceUser);

        retried.Outcome.Should().Be(ApprovalOutcome.Incomplete);
        retried.FailedStep.Should().Be("record");
        w.ApproveRecords.Should().ContainSingle("the writer refuses other content under the same id");
    }

    [Theory(DisplayName = "103 K4: a portion with no or an unknown status is refused (409 status_unknown), never treated as Pending; GET shows Unknown")]
    [InlineData(null)]
    [InlineData(999)]
    public async Task UnknownPortionStatus_IsRefused(int? status)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        if (status is { } s)
        {
            w.Portion["sprk_approvalstatus"] = new OptionSetValue(s);
        }
        else
        {
            w.Portion.Attributes.Remove("sprk_approvalstatus");
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict);
        result.Code.Should().Be("ebilling.approval.status_unknown");
        w.Writes.Should().Be(0);
        w.Transitions.Should().Be(0);

        var state = (await w.GetAs(AliceUser))!;
        state.Status.Should().Be("Unknown");
        state.CanApprove.Should().BeFalse();
    }

    [Fact(DisplayName = "103 K-4: another decision holding the invoice's lock is Busy; a lock fault is Unavailable; neither writes")]
    public async Task Locks_BusyAndFault()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Locks.Hold(SubmissionOverrideService.LockKey(w.InvoiceId));
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Busy);

        var line = new ApprovalWorld();
        line.Locks.Hold(InvoiceLineDecisionService.LockKeyFor(line.InvoiceId));
        (await line.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Busy);
        line.Locks.IsHeld(SubmissionOverrideService.LockKey(line.InvoiceId)).Should().BeFalse("the first lock is released when the second is refused");

        var faulted = new ApprovalWorld();
        faulted.Locks.Fault = new InvalidOperationException("redis down");
        (await faulted.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Unavailable);

        AssertNothingWritten(w);
        AssertNothingWritten(faulted);
    }

    // ── D-72 (F-2): undecided Clean lines and Unreviewed fields ──

    [Fact(DisplayName = "103 D-72: undecided Clean lines are accepted in bulk ('Accepted in bulk' rows, not labels); AI fields of individually decided lines with no row get 'Unreviewed' (not labels)")]
    public async Task CleanLinesAcceptedInBulk_DecidedFieldsUnreviewed()
    {
        var w = new ApprovalWorld();
        var clean = w.AddLine(InvoiceLineStatus.Clean, 300m, extraction: Extraction);
        var reduced = w.AddLine(InvoiceLineStatus.Reduced, 500m, extraction: Extraction, approved: 400m);
        var corrected = w.AddLine(InvoiceLineStatus.Accepted, 200m, extraction: Extraction, approved: 200m);
        // A reviewer already corrected the Rate of the third line (101): that field keeps its row and gets no Unreviewed one.
        w.Put(new Entity("sprk_invoicecorrection", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", w.InvoiceId),
            ["sprk_invoiceline"] = new EntityReference("sprk_billingevent", corrected.Id),
            ["sprk_fieldname"] = "Rate",
            ["sprk_outcometype"] = new OptionSetValue(100000000),
        });

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved);
        w.Row("sprk_billingevent", clean.Id).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Accepted);
        w.Portion.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(900m, "billed 1,000 less the 100 reduction");

        var rows = w.Corrections;
        var bulk = rows.Where(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value == 100000002).ToList();
        bulk.Select(r => r.GetAttributeValue<EntityReference>("sprk_invoiceline").Id).Distinct().Should().Equal(clean.Id);
        bulk.Should().HaveCount(7, "date, hours, rate, amount, narrative, task code, activity code");

        var unreviewed = rows.Where(r => r.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value == 100000003).ToList();
        unreviewed.Where(r => r.GetAttributeValue<EntityReference>("sprk_invoiceline").Id == reduced.Id).Should().HaveCount(7);
        unreviewed.Where(r => r.GetAttributeValue<EntityReference>("sprk_invoiceline").Id == corrected.Id)
            .Select(r => r.GetAttributeValue<string>("sprk_fieldname")).Should().HaveCount(6).And.NotContain("Rate");
        unreviewed.Should().NotContain(r => r.GetAttributeValue<EntityReference>("sprk_invoiceline").Id == clean.Id, "accepted-in-bulk fields have their row");
        bulk.Concat(unreviewed).Should().OnlyContain(r => r.GetAttributeValue<bool>("sprk_islabel") == false);
        unreviewed.Should().OnlyContain(r => r.GetAttributeValue<string>("sprk_reviewerrole") == InvoiceApprovalService.ApproverRole
            && r.GetAttributeValue<EntityReference>("sprk_reviewedby").Id == AliceUser
            && r.GetAttributeValue<EntityReference>("sprk_regardingproject").Id == w.CoreId);

        // Two records: the bulk accept's (on the line) and the approval's (on the invoice).
        w.ApproveRecords.Should().ContainSingle();
        w.H.Created.Where(r => r.GetAttributeValue<string>("sprk_actioncode") == "accept-line").Should().ContainSingle();
    }

    // ── Exactly once (FR-35, FR-30; 074 K-1 / K-3 / K-4) ──

    [Fact(DisplayName = "103 FR-35: a second approve - by the same or another eligible approver - is a no-op with no new Decision Record")]
    public async Task SecondApprove_IsANoOp()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var first = await w.ApproveAs(AliceUser);
        var writes = w.Writes;

        var again = await w.ApproveAs(AliceUser);
        var other = await w.ApproveAs(BobUser);

        first.Outcome.Should().Be(ApprovalOutcome.Approved);
        again.Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        again.DecisionRecordId.Should().Be(first.DecisionRecordId);
        other.Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        w.ApproveRecords.Should().ContainSingle();
        w.Writes.Should().Be(writes, "a no-op writes nothing");
        w.Transitions.Should().Be(1);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice);
    }

    [Theory(DisplayName = "103 FR-30 / K-3: a failure between the transition and the record is recovered by the same approver's retry, with exactly ONE record")]
    [InlineData("portion")]
    [InlineData("identity")]
    [InlineData("link")]
    [InlineData("transition-after-commit")]
    public async Task FailureAfterTheTransition_RetryBySameApprover_RecordsOnce(string step)
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        if (step == "transition-after-commit")
        {
            w.TransitionThrowsAfterCommit = true;
        }
        else
        {
            // "identity" makes the recorder's contact read fault: a transient refusal (BillingRecordLookupFailed), nothing written.
            w.FaultsOnce[step] = new InvalidOperationException("dataverse down");
        }

        var failed = await w.ApproveAs(AliceUser);

        failed.Outcome.Should().Be(ApprovalOutcome.Incomplete);
        failed.Code.Should().Be("ebilling.approval.incomplete");
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved, "the transition happened");

        // Another approver in between: a no-op that records nothing (the claimant's record is the claimant's).
        (await w.ApproveAs(BobUser)).Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        w.ApproveRecords.Count.Should().BeLessThanOrEqualTo(1);

        var retried = await w.ApproveAs(AliceUser);

        retried.Outcome.Should().Be(ApprovalOutcome.Approved);
        var record = w.ApproveRecords.Should().ContainSingle().Subject;
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(AliceUser, "attributed to the approver whose transition succeeded");
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionApproved);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(Alice);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decisionrecord").Should().NotBeNull();
        w.Transitions.Should().Be(1, "the retry does not transition again");

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.AlreadyApproved);
        w.ApproveRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "103 K-1 SEEDED: two concurrent approves with the lock defeated (same derived decision id) - the conditional claim lets one through; exactly ONE record, attributed to it")]
    public async Task ConcurrentApproves_LockDefeated_ExactlyOneRecord()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.Locks.AlwaysGrant = true;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        // Both requests have read the portion at the same version before either claims it.
        w.BeforeClaim = () =>
        {
            if (Interlocked.Increment(ref arrived) == 2)
            {
                both.SetResult();
            }

            return both.Task;
        };

        var results = await Task.WhenAll(Task.Run(() => w.ApproveAs(AliceUser)), Task.Run(() => w.ApproveAs(BobUser)));

        results.Select(r => r.Outcome).Should().BeEquivalentTo([ApprovalOutcome.Approved, ApprovalOutcome.Conflict]);
        results.Single(r => r.Outcome == ApprovalOutcome.Conflict).Code.Should().Be("ebilling.approval.changed");
        var winner = results[0].Outcome == ApprovalOutcome.Approved ? (AliceUser, Alice) : (BobUser, Bob);
        w.ApproveRecords.Should().ContainSingle().Which.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(winner.Item1);
        w.Portion.GetAttributeValue<EntityReference>("sprk_decidedby").Id.Should().Be(winner.Item2);
        w.Transitions.Should().Be(1);
    }

    [Fact(DisplayName = "103 K-1: the same approver twice at once, lock defeated: one Approved, one refused, ONE record")]
    public async Task ConcurrentSameApprover_ExactlyOneRecord()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
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

        var results = await Task.WhenAll(Task.Run(() => w.ApproveAs(AliceUser)), Task.Run(() => w.ApproveAs(AliceUser)));

        results.Count(r => r.Outcome == ApprovalOutcome.Approved).Should().Be(1);
        w.ApproveRecords.Should().ContainSingle();
    }

    [Fact(DisplayName = "103 K-4: with the lock held, a second approve while the first runs is Busy (the locks serialise per invoice)")]
    public async Task LockedApprove_SecondIsBusy()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.BeforeClaim = () => release.Task;

        var first = Task.Run(() => w.ApproveAs(AliceUser));
        while (!w.Locks.IsHeld(InvoiceLineDecisionService.LockKeyFor(w.InvoiceId)))
        {
            await Task.Delay(5);
        }

        var second = await w.ApproveAs(BobUser);
        w.BeforeClaim = null;
        release.SetResult();

        second.Outcome.Should().Be(ApprovalOutcome.Busy);
        (await first).Outcome.Should().Be(ApprovalOutcome.Approved);
        w.ApproveRecords.Should().ContainSingle();
    }

    // ── GET (D-78 recheck) ──

    [Fact(DisplayName = "103 D-78: GET reports the live eligible approvers and canApprove, and mirrors them onto the stored set while Pending")]
    public async Task Get_ReportsLiveState_AndStoresTheSet()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.AddLine(InvoiceLineStatus.Clean);

        var state = (await w.GetAs(AliceUser))!;

        state.Status.Should().Be("Pending");
        state.EligibleApprovers.Should().Equal("Alice Adams", "Bob Brown");
        state.CallerEligible.Should().BeTrue();
        state.CanApprove.Should().BeTrue();
        state.UndecidedCleanLines.Should().Be(1);
        state.OpenLines.Should().Be(new ApprovalOpenLinesDto(0, 0));
        state.EligibleResolvedOn.Should().Be(Now.UtcDateTime);
        w.StoredApprovers.Should().BeEquivalentTo([Alice, Bob]);

        // An assignment change shows on the next GET, both live and in the stored set.
        w.Core["sprk_assignedattorney1"] = new EntityReference("contact", Carol);
        var after = (await w.GetAs(AliceUser))!;
        after.CallerEligible.Should().BeFalse();
        after.CanApprove.Should().BeFalse();
        w.StoredApprovers.Should().BeEquivalentTo([Bob, Carol]);
    }

    [Fact(DisplayName = "103: GET with open lines, a hold, or nobody eligible says so and canApprove is false")]
    public async Task Get_ExplainsWhyNot()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Flagged);
        w.Hold();
        w.Core["sprk_billingapproverrole"] = new OptionSetValueCollection([new OptionSetValue((int)BillingApproverRole.AssignedParalegal1)]);

        var state = (await w.GetAs(AliceUser))!;

        state.OpenLines.Should().Be(new ApprovalOpenLinesDto(0, 1));
        state.Held.Should().BeTrue();
        state.NoEligibleApprover.Should().BeTrue();
        state.CanApprove.Should().BeFalse();
    }

    [Fact(DisplayName = "103: GET after approval shows who decided, when, and the frozen amount; a failed eligibility read is reported, not hidden")]
    public async Task Get_AfterApproval_AndOnEligibilityFault()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        await w.ApproveAs(AliceUser);

        var state = (await w.GetAs(BobUser))!;
        state.Status.Should().Be("Approved");
        state.ApprovedAmount.Should().Be(1000m, "the billed total (1,000) less no adjustments, frozen at approval");
        state.DecidedOn.Should().Be(Now.UtcDateTime);
        state.DecisionRecordId.Should().NotBeNull();
        state.CanApprove.Should().BeFalse();

        w.FaultsOnce["read:contact"] = new InvalidOperationException("dataverse down");
        var faulted = (await w.GetAs(BobUser))!;
        faulted.EligibilityUnavailable.Should().BeTrue();
        faulted.CallerEligible.Should().BeFalse();

        (await w.Service().GetAsync(Guid.NewGuid(), BobUser, null, CancellationToken.None)).Should().BeNull("an unknown invoice is the uniform 404");
    }
}
