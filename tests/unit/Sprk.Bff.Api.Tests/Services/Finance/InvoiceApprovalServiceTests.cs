using FluentAssertions;
using Moq;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
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
/// <see cref="InvoiceApprovalService"/> (the <c>set.For(caller) is not { } approver</c> refusal) is removed: Carol, who has Write but is not
/// eligible, then approves. <c>NoEligibleApprover_NobodyMayApprove</c> and <c>EligibilityReadFault_FailsClosed</c> fail when the refusal falls
/// back to "anyone". Recorded in <c>projects/ebilling-r1/notes/103-approval-worklist.md</c>.</para>
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

    [Fact(DisplayName = "103: a refused 014 transition is 409 and records nothing")]
    public async Task TransitionRefused_RecordsNothing()
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

        var state = (await w.Service().GetAsync(w.InvoiceId, AliceUser, CancellationToken.None))!;

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
        var after = (await w.Service().GetAsync(w.InvoiceId, AliceUser, CancellationToken.None))!;
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

        var state = (await w.Service().GetAsync(w.InvoiceId, AliceUser, CancellationToken.None))!;

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

        var state = (await w.Service().GetAsync(w.InvoiceId, BobUser, CancellationToken.None))!;
        state.Status.Should().Be("Approved");
        state.ApprovedAmount.Should().Be(1000m, "the billed total (1,000) less no adjustments, frozen at approval");
        state.DecidedOn.Should().Be(Now.UtcDateTime);
        state.DecisionRecordId.Should().NotBeNull();
        state.CanApprove.Should().BeFalse();

        w.FaultsOnce["read:contact"] = new InvalidOperationException("dataverse down");
        var faulted = (await w.Service().GetAsync(w.InvoiceId, BobUser, CancellationToken.None))!;
        faulted.EligibilityUnavailable.Should().BeTrue();
        faulted.CallerEligible.Should().BeFalse();

        (await w.Service().GetAsync(Guid.NewGuid(), BobUser, CancellationToken.None)).Should().BeNull("an unknown invoice is the uniform 404");
    }
}
