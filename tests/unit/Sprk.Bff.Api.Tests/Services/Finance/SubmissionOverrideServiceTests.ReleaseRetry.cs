using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

// ebilling-r1 task 095f-2c, review round 1 (F1): every Override's release is conditioned on the invoice's row version (it always writes the internal decision
// note), and the invoice has writers that do not take the decision lock (the totals recalculation, a line decision, a form edit). A fault on the INVOICE's
// request of the release transaction is retried under the lock after a re-read, as InvoiceLifecycleService.TransitionAsync retries its conditional write
// (InvoiceReviewService.MaxConditionalWriteAttempts in all); a fault on the SUBMISSION's request is not.
public partial class SubmissionOverrideServiceTests
{
    private static int ReleaseWrites(World w) => w.Order.Count(o => o == "update");

    /// <summary>An unrelated writer touches the invoice: its row version moves (what the totals recalculation or a form edit does).</summary>
    private static void TouchInvoice(World w) => w.Invoice!["versionnumber"] = w.Invoice.GetAttributeValue<long>("versionnumber") + 1;

    [Fact(DisplayName = "095f-2c round 1 F1: an invoice write between the read and the release is retried: the override completes, the note and detail are written once, one Decision Record")]
    public async Task ReleaseRetry_AnInvoiceWriteInTheWindow_IsRetried_AndTheOverrideCompletesOnce()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            w.OnUpdate = null; // one unrelated write, in the window of the first release only
            TouchInvoice(w);
        };

        var result = await w.Service.OverrideAsync(World.SubmissionId, Reason, "Spoke to the partner.", World.Token, CancellationToken.None);

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        ReleaseWrites(w).Should().Be(2, "the first release faulted on the invoice's version; the second carries the re-read version");
        w.Transactions.Should().ContainSingle("only the second release applied");
        var invoiceUpdate = w.Transactions.Single().Cast<Microsoft.Xrm.Sdk.Messages.UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoice").Target;
        invoiceUpdate.RowVersion.Should().Be("501", "the release is conditioned on the version of the re-read, not the stale one");
        invoiceUpdate.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn).Should().Be("Spoke to the partner.");
        w.Invoice!.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn).Should().Be("Spoke to the partner.", "the note landed once");
        w.Updates.Should().ContainSingle("the submission's state was applied once");
        w.RecordCreates.Should().Be(1, "one Decision Record");
        w.Recorded.Should().ContainSingle("the record was not asked for again by the retry");
        w.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview, "an override writes no status");
        w.Notified.Should().ContainSingle();
    }

    [Fact(DisplayName = "095f-2c round 1 F1: the review detail is re-derived against the re-read invoice when it is written (another hold appeared in the window), and written once")]
    public async Task ReleaseRetry_ReDerivesTheReviewDetail_WhenItIsWritten()
    {
        var w = await World.CreateHeldAsync(InvoiceSubmissionChannel.Internal);
        w.OnUpdate = () =>
        {
            w.OnUpdate = null;
            TouchInvoice(w);
            // In the same window another submission of the invoice is held by a Procedure gap: the first derivation (no other hold) is stale.
            w.OtherSubmissions.Add(OtherSubmission(SubmissionReasonCodes.ProcedureGap, OtherGapFindings));
        };

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        ReleaseWrites(w).Should().Be(2);
        w.Transactions.Should().ContainSingle();
        w.InvoiceDetail.Should().Be(InvoiceReviewDetail.WaitingForBillingTerms, "re-derived against the invoice as the retry found it, not the stale first derivation (blank)");
        w.RecordCreates.Should().Be(1);
    }

    [Fact(DisplayName = "095f-2c round 1 F1: repeated invoice faults up to the bound end in the existing 409 concurrent_change (the invoice's wording), nothing applied; the repeat then finishes with the one record")]
    public async Task ReleaseRetry_RepeatedInvoiceFaults_EndInThe409_AtTheBound_WithNothingApplied()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () => TouchInvoice(w); // the invoice keeps changing

        var result = await w.Service.OverrideAsync(World.SubmissionId, Reason, "First note", World.Token, CancellationToken.None);

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.ConcurrentChange);
        result.Detail.Should().Contain("invoice").And.NotContain("submission changed");
        ReleaseWrites(w).Should().Be(InvoiceReviewService.MaxConditionalWriteAttempts, "the bound is the lifecycle's");
        w.Transactions.Should().BeEmpty("all or nothing");
        w.Updates.Should().BeEmpty();
        w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn).Should().BeFalse("no note without its release");
        w.Code.Should().Be(SubmissionReasonCodes.Blocked, "still held");
        w.Notified.Should().BeEmpty();

        w.OnUpdate = null;
        var repeat = await w.Service.OverrideAsync(World.SubmissionId, Reason, "First note", World.Token, CancellationToken.None);

        repeat.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1, "no second record");
        w.Invoice.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn).Should().Be("First note");
    }

    [Fact(DisplayName = "095f-2c round 1 F1: a re-read that finds the invoice no longer In Review stops without writing (409 invoice_not_held); a repeat is refused the same way")]
    public async Task ReleaseRetry_AReReadThatFindsTheInvoiceNoLongerInReview_StopsWithoutWriting()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            w.OnUpdate = null;
            TouchInvoice(w);
            w.Invoice!["statuscode"] = new OptionSetValue(InvoiceStatusCodes.StatusCodeOf(InvoiceLifecycleStatus.Superseded));
        };

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.InvoiceNotHeld);
        ReleaseWrites(w).Should().Be(1, "no second release is sent");
        w.Transactions.Should().BeEmpty();
        w.Updates.Should().BeEmpty();
        w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn).Should().BeFalse();
        w.Code.Should().Be(SubmissionReasonCodes.Blocked);
        w.Notified.Should().BeEmpty();
    }

    [Fact(DisplayName = "095f-2c round 1 F1: a re-read that finds the firm gate closed stops without writing (409 firm_missing)")]
    public async Task ReleaseRetry_AReReadThatFindsTheFirmGateClosed_StopsWithoutWriting()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            w.OnUpdate = null;
            TouchInvoice(w);
            w.InvoiceOwnHold = FirmMissing; // the firm was removed in the window
        };

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.FirmMissing);
        ReleaseWrites(w).Should().Be(1);
        w.Transactions.Should().BeEmpty();
        w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn).Should().BeFalse();
    }

    [Fact(DisplayName = "095f-2c round 1 F1: a re-read that cannot read the invoice answers 503 and writes nothing more")]
    public async Task ReleaseRetry_AReReadThatFaults_Is503_AndWritesNothing()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            w.OnUpdate = null;
            TouchInvoice(w);
            w.InvoiceReadFault = new TimeoutException("dataverse");
        };

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.Unavailable);
        ReleaseWrites(w).Should().Be(1);
        w.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "095f-2c round 1 F1: a fault on the SUBMISSION's request is not retried (409 concurrent_change in the submission's wording, one release attempt, nothing applied); the repeat finishes")]
    public async Task ReleaseRetry_ASubmissionSideFault_BehavesAsBefore()
    {
        var w = await World.CreateAsync();
        w.OnUpdate = () =>
        {
            w.OnUpdate = null;
            w.Submission["versionnumber"] = w.Submission.GetAttributeValue<long>("versionnumber") + 1;
        };

        var result = await w.OverrideAsync();

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.ConcurrentChange);
        result.Detail.Should().Contain("The submission changed");
        ReleaseWrites(w).Should().Be(1, "no retry");
        w.Transactions.Should().BeEmpty();
        w.Updates.Should().BeEmpty();

        var repeat = await w.OverrideAsync();

        repeat.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
    }

    [Fact(DisplayName = "095f-2c round 1 F1: a version fault Dataverse does not attribute to a request is not retried (as before), and its 409 names neither row")]
    public async Task ReleaseRetry_AFaultWithNoFailedIndex_IsNotRetried()
    {
        var w = await World.CreateAsync();
        w.UpdateFault = new DataverseTransactionException(
            "Transaction rolled back: a row version no longer matches (0x80060882, ConcurrencyVersionMismatch).", failedRequestIndex: null, outcomeUnknown: false, inner: null);

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.ConcurrentChange);
        result.Detail.Should().Contain("submission or its invoice");
        ReleaseWrites(w).Should().Be(1, "no retry without the failed request's index");
        w.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "095f-2c round 1 K3: an ordinary override whose invoice is read without a row version is 503 before any write, for every override (not only a hold-lifting one)")]
    public async Task Override_AnInvoiceReadWithoutARowVersion_Is503_BeforeAnyWrite()
    {
        var w = await World.CreateAsync();
        w.Invoice!.Attributes.Remove("versionnumber");

        var result = await w.OverrideAsync();

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.Unavailable);
        w.AssertNothingWritten();
    }
}
