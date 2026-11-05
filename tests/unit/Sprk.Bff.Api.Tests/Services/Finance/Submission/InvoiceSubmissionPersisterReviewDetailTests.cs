using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 082c-2 (ontology D-205 writer 1): the persister's held create carries the derived review detail; a normal or rejected create
/// carries none; where the column does not exist yet the hold still persists and the column is left out.
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    private static ProcedureResolution EndedProcedure()
    {
        var kit = new ProcedureKit().Procedure("Ended", from: new DateOnly(2025, 1, 1), to: new DateOnly(2026, 1, 10));
        kit.Rule("Ended", "ENDED-INCREMENT", RuleType.TimeIncrement);
        return kit.Build();
    }

    private static Entity CreatedInvoice(Store store) =>
        store.Transactions.Single().OfType<CreateRequest>().Single(c => c.Target.LogicalName == "sprk_invoice").Target;

    [Fact(DisplayName = "082c-2 (D-205 writer 1): an SPA gap hold's invoice is created In Review with 'Waiting for billing terms'")]
    public async Task ReviewDetail_GapHold_IsWaitingForBillingTerms()
    {
        var outcome = await new ProcessorHarness { Procedures = EndedProcedure() }.RunAsync();
        var store = new Store();

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        var invoice = CreatedInvoice(store);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        invoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
    }

    [Fact(DisplayName = "082c-2 (D-205 writer 1): a final-delivery unresolved hold's invoice is created with 'Billing terms could not be checked'")]
    public async Task ReviewDetail_UnresolvedHold_IsBillingTermsNotChecked()
    {
        var outcome = await new ProcessorHarness { EvaluationFault = new ProcedureResolutionFaultException("rows"), FinalDelivery = true }.RunAsync();
        var store = new Store();

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        CreatedInvoice(store).GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(InvoiceReviewDetail.BillingTermsNotChecked);
    }

    [Fact(DisplayName = "082c-2: a normal accepted invoice is created with no review detail (blank)")]
    public async Task ReviewDetail_NoHold_IsBlank()
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = new Store();

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        CreatedInvoice(store).Attributes.Should().NotContainKey(InvoiceReviewDetail.Column);
    }

    [Fact(DisplayName = "082c-2 (uac-r2 (b)): an automatic rejection is created Rejected with no review detail")]
    public async Task ReviewDetail_AutoRejected_IsBlank()
    {
        var h = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        h.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))]; // as 049 B's RejectingFinding_OnSpa_IsAutoRejected
        var outcome = await h.RunAsync();
        outcome.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected, "control: the rejection path");
        var store = new Store();

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        var invoice = CreatedInvoice(store);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.Rejected);
        invoice.Attributes.Should().NotContainKey(InvoiceReviewDetail.Column);
    }

    [Fact(DisplayName = "082c-2 (mismatch): where the column does not exist yet, the gap hold still persists and the column is left out")]
    public async Task ReviewDetail_ColumnMissing_TheHoldPersistsWithoutIt()
    {
        var outcome = await new ProcessorHarness { Procedures = EndedProcedure() }.RunAsync();
        var store = new Store { ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(present: false) };

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        CreatedInvoice(store).Attributes.Should().NotContainKey(InvoiceReviewDetail.Column);
        var submission = store.Transactions.Single().OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoicesubmission").Target;
        InvoiceProcedureHold.ReasonsOf(submission).Should().Contain(SubmissionReasonCodes.ProcedureGap, "the hold is the stored state, not the column");
    }

    [Theory(DisplayName = "082c-2: HoldReasonsOf names the same Procedure holds the stored-state reader finds in what the outcome persists")]
    [InlineData("gap")]
    [InlineData("unresolved")]
    [InlineData("none")]
    public async Task HoldReasonsOf_AgreesWithTheStoredStateReader(string kind)
    {
        var h = kind switch
        {
            "gap" => new ProcessorHarness { Procedures = EndedProcedure() },
            "unresolved" => new ProcessorHarness { EvaluationFault = new ProcedureResolutionFaultException("rows"), FinalDelivery = true },
            _ => new ProcessorHarness(),
        };
        var outcome = await h.RunAsync();

        var json = InvoiceSubmissionPersister.BuildFindingsJson(outcome, Guid.NewGuid());
        var stored = InvoiceProcedureHold.ReasonsOf(InvoiceSubmissionPersister.TerminalFor(outcome).ReasonCode, json);

        InvoiceSubmissionPersister.HoldReasonsOf(outcome).Intersect(InvoiceProcedureHold.HoldCodes).Should().BeEquivalentTo(stored);
    }
}
