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

    [Fact(DisplayName = "082c-2 fix round 1 (D-205 writer 1, the no-firm case): an Internal no-firm held create writes 'Waiting for the firm to be confirmed' on the invoice create")]
    public async Task ReviewDetail_NoFirmInternalHold_IsWaitingForTheFirm()
    {
        var outcome = await NoFirmTwoLines().RunAsync();
        outcome.Submission!.FirmId.Should().BeNull("the setup: a submission with no firm");
        var store = NoFirmStore();

        (await store.Build().PersistAsync(outcome, null)).Status.Should().Be(SubmissionPersistStatus.Committed);

        var invoice = CreatedInvoice(store);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview);
        invoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(InvoiceReviewDetail.WaitingForFirm).And.Be(659490002);
        invoice.Attributes.Should().NotContainKey("sprk_vendororg", "the same create holds the invoice for the firm");
    }

    private static void TamperTheStagedBlob(Store s) =>
        s.Files.Gateway.Overwrite(s.StagedBlob!.Container, s.StagedBlob.BlobPath, "%PDF-tampered"u8.ToArray(), s.Files.Gateway.Now.AddMinutes(5));

    [Fact(DisplayName = "082c-2 fix round 1 (D-205 writer 7, the undo; F2): the undo of a correction reopens a held-for-gap invoice In Review WITH its gap detail, in the status request")]
    public async Task SupersedeUndo_ReopensAHeldInvoice_WithTheDetailItsRemainingHoldsDerive()
    {
        object? detailAfterTheClose = "unset";
        var store = CorrectionStore(afterCommit: s =>
        {
            detailAfterTheClose = s.OldInvoice!.Contains(InvoiceReviewDetail.Column) ? s.OldInvoice[InvoiceReviewDetail.Column] : null;
            TamperTheStagedBlob(s);
        });
        store.OldInvoice![InvoiceReviewDetail.Column] = new OptionSetValue(InvoiceReviewDetail.WaitingForBillingTerms);
        store.OldInvoiceSubmissions.Add(new Entity("sprk_invoicesubmission", EarlierSubmission)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
            ["sprk_outcomereasoncode"] = SubmissionReasonCodes.ProcedureGap,
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeFalse();
        detailAfterTheClose.Should().BeNull("the Superseded close blanked the detail in the same request (writer 7)");
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        store.OldInvoiceWrites.Should().ContainSingle("the reopen is one request");
        store.OldInvoiceWrites[0].Target.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value
            .Should().Be(InvoiceReviewDetail.WaitingForBillingTerms).And.Be(659490000, "the detail travels with the status, not in a later write");
        store.OldInvoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(659490000);
    }

    [Fact(DisplayName = "082c-2 fix round 2 (F2): where the column does not exist yet, the undo reopens a held invoice without naming it and is not held - the gate probes before it builds the request")]
    public async Task SupersedeUndo_ColumnMissing_ReopensWithoutNamingTheColumn()
    {
        var store = CorrectionStore(afterCommit: TamperTheStagedBlob);
        store.ReviewDetailColumnAbsentInDataverse = true;
        store.ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(present: false); // the commit's own writers already know
        store.GateReviewDetailProbe = new ReviewDetailColumnProbe(TimeProvider.System); // the undo's probe has not been asked: another delivery or process
        store.OldInvoiceSubmissions.Add(new Entity("sprk_invoicesubmission", EarlierSubmission)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
            ["sprk_outcomereasoncode"] = SubmissionReasonCodes.ProcedureGap,
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        result.UndoHeld.Should().BeFalse("the reopen did not name a column that does not exist");
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        store.OldInvoiceWrites.Should().ContainSingle().Which.Target.Attributes.Keys.Should().BeEquivalentTo("statecode", "statuscode");
    }

    [Fact(DisplayName = "082c-2 fix round 2 (F2): where the column exists, the same undo still carries the derived detail (the probe changes nothing)")]
    public async Task SupersedeUndo_ColumnPresent_StillCarriesTheDetail_AfterTheGateProbed()
    {
        var store = CorrectionStore(afterCommit: TamperTheStagedBlob);
        store.GateReviewDetailProbe = new ReviewDetailColumnProbe(TimeProvider.System);
        store.OldInvoiceSubmissions.Add(new Entity("sprk_invoicesubmission", EarlierSubmission)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
            ["sprk_outcomereasoncode"] = SubmissionReasonCodes.ProcedureGap,
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        store.OldInvoiceWrites.Should().ContainSingle().Which.Target.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value
            .Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
    }

    [Fact(DisplayName = "082c-2 fix round 1 (F2): the undo derives the detail from the firm hold too, and leaves it blank when no hold is open")]
    public async Task SupersedeUndo_FirmHoldGivesTheFirmDetail_NoHoldGivesNone()
    {
        // A no-firm invoice held for its firm (and for a gap) is corrected by a no-firm correction (095c-3/5: a firm on one side only is refused).
        var firmHeld = NoFirmStore(CorrectionStore(afterCommit: TamperTheStagedBlob));
        firmHeld.OldInvoice!.Attributes.Remove("sprk_vendororg");
        firmHeld.OldInvoice[InvoiceFirmHold.InvoiceHoldColumn] = SubmissionReasonCodes.FirmMissing;
        firmHeld.OldInvoiceSubmissions.Add(new Entity("sprk_invoicesubmission", EarlierSubmission)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
            ["sprk_outcomereasoncode"] = SubmissionReasonCodes.ProcedureGap,
        });
        (await firmHeld.Build().PersistAsync(await CorrectionOutcomeAsync(InvoiceSubmissionProcessorTests.NoFirm()), null))
            .Status.Should().Be(SubmissionPersistStatus.Undone);
        firmHeld.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        firmHeld.OldInvoice!.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value
            .Should().Be(InvoiceReviewDetail.WaitingForFirm, "the firm comes before the gap (D-205 priority)");

        var notHeld = CorrectionStore(afterCommit: TamperTheStagedBlob);
        (await notHeld.Build().PersistAsync(await CorrectionOutcomeAsync(), null)).Status.Should().Be(SubmissionPersistStatus.Undone);
        notHeld.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        notHeld.OldInvoiceWrites.Should().ContainSingle().Which.Target.Attributes.Keys.Should().NotContain(InvoiceReviewDetail.Column, "no hold is open: blank is a normal review");
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, fail closed, must fire): the undo of an invoice whose stored findings cannot be read reopens it with 'Billing terms could not be checked', never blank")]
    public async Task SupersedeUndo_UnreadableFindings_ReopensWithBillingTermsNotChecked()
    {
        // The findings turn unreadable AFTER the commit (the commit itself refuses an unreadable one: it reads as a firm hold, fail closed), so the
        // undo's read is the first to meet them.
        var store = CorrectionStore(afterCommit: s =>
        {
            TamperTheStagedBlob(s);
            s.OldInvoiceSubmissions.Add(new Entity("sprk_invoicesubmission", Guid.NewGuid())
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", OldInvoiceId),
                ["sprk_findingsjson"] = "{not json",
            });
        });

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        store.OldInvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        store.OldInvoiceWrites.Should().ContainSingle().Which.Target.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value
            .Should().Be(InvoiceReviewDetail.BillingTermsNotChecked).And.Be(659490001);
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, must fire): the undo of an invoice with NO firm and no stored firm hold reopens it 'Waiting for the firm', as approval refuses it (needs a firm)")]
    public async Task SupersedeUndo_NoFirmOnTheInvoice_ReopensWaitingForTheFirm()
    {
        var store = NoFirmStore(CorrectionStore(afterCommit: TamperTheStagedBlob));
        store.OldInvoice!.Attributes.Remove("sprk_vendororg"); // no own hold, no stored firm marker: only the missing firm

        var result = await store.Build().PersistAsync(await CorrectionOutcomeAsync(InvoiceSubmissionProcessorTests.NoFirm()), null);

        result.Status.Should().Be(SubmissionPersistStatus.Undone);
        store.OldInvoiceWrites.Should().ContainSingle().Which.Target.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value
            .Should().Be(InvoiceReviewDetail.WaitingForFirm);
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
