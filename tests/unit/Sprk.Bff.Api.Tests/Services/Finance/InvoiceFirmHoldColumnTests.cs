using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095c-5 (uac-r2 2026-11-04) and the 095c-4 review fixes: the invoice's own field-secured hold, <c>sprk_invoice.sprk_firmhold</c>.
/// The hold must survive each direct write uac-r2 names: (1) the submission's <c>sprk_invoice</c> cleared or repointed, (2) the submission deleted,
/// (3) the reason code rewritten and the invoice firm set while the findings JSON is unchanged. It is read app-only (never from the row a caller
/// hands in), a read fault or a missing column is held, and a firm invoice with no hold still approves. Over the in-memory Dataverse
/// (<see cref="ApprovalWorld"/>, the app-only store) with the real services. AP export: <c>ApExportFirmHoldColumnTests</c>.
/// </summary>
/// <remarks>
/// <b>Seed proof (notes 095c section 15).</b> With the invoice-side read removed from <see cref="InvoiceFirmHold.HeldInvoicesAsync"/>,
/// <c>SubmissionUnlinkedRepointedOrDeleted_TheInvoiceHoldStill409</c> (all three cases) and <c>TheInvoiceHoldAlone_IsHeld</c> fail.
/// </remarks>
public sealed class InvoiceFirmHoldColumnTests
{
    private static void AssertNothingWritten(ApprovalWorld w)
    {
        w.Writes.Should().Be(0, "a refused approve writes nothing");
        w.Transitions.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    /// <summary>A held no-firm invoice as 095c-5's persister leaves it (the submission's markers AND the invoice's own hold), whose firm was then set natively.</summary>
    private static (ApprovalWorld World, Guid Submission) HeldThenFirmSetNatively()
    {
        var w = new ApprovalWorld();
        w.WithoutFirm();
        var submission = w.HoldForFirm();
        w.HoldInvoiceForFirm();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.SetFirmNatively(Guid.NewGuid());
        return (w, submission);
    }

    [Theory(DisplayName = "095c-5 uac-r2 (1)(2): the submission unlinked, repointed or deleted - the invoice's own hold keeps approve at 409 and canApprove false")]
    [InlineData("unlinked")]
    [InlineData("repointed")]
    [InlineData("deleted")]
    public async Task SubmissionUnlinkedRepointedOrDeleted_TheInvoiceHoldStill409(string write)
    {
        var (w, submission) = HeldThenFirmSetNatively();
        switch (write)
        {
            case "unlinked":
                w.Rows[("sprk_invoicesubmission", submission)]["sprk_invoice"] = null;
                break;
            case "repointed":
                w.Rows[("sprk_invoicesubmission", submission)]["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
                break;
            default:
                w.Rows.Remove(("sprk_invoicesubmission", submission));
                break;
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Held, write);
        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing);
        result.Detail.Should().Be(InvoiceApprovalCodes.FirmHeldDetail, "095c-4 review K1: the form shows a firm, so the sentence says it is waiting for one");
        AssertNothingWritten(w);
        (await w.GetAs(AliceUser))!.Should().Match<InvoiceApprovalStateDto>(s => !s.CanApprove && s.FirmNeeded);
        (await w.ReturnAs(AliceUser)).Code.Should().Be(InvoiceRejectReturnCodes.FirmMissing, "Return to Firm reads the same answer");
    }

    [Theory(DisplayName = "095c-5 uac-r2 (3) / 095c-4 review pin: the reason rewritten and the invoice firm set, the findings JSON unchanged - approve stays 409")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReasonRewritten_FirmSet_FindingsUnchanged_Still409(bool invoiceHold)
    {
        var w = new ApprovalWorld();
        w.WithoutFirm();
        var submission = w.HoldForFirm(); // the findings carry the firm_missing pipeline finding, as the persister writes them
        if (invoiceHold)
        {
            w.HoldInvoiceForFirm();
        }

        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var findings = w.Rows[("sprk_invoicesubmission", submission)].GetAttributeValue<string>("sprk_findingsjson");
        w.Rows[("sprk_invoicesubmission", submission)]["sprk_outcomereasoncode"] = "ebilling.submission.accepted"; // the reason rewritten
        w.SetFirmNatively(Guid.NewGuid());

        var result = await w.ApproveAs(AliceUser);

        w.Rows[("sprk_invoicesubmission", submission)].GetAttributeValue<string>("sprk_findingsjson").Should().Be(findings, "the findings JSON is unchanged");
        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing, invoiceHold ? "the invoice hold and the findings hold" : "the stored findings alone hold");
        result.Detail.Should().Be(InvoiceApprovalCodes.FirmHeldDetail);
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "095c-5: the invoice's own hold alone (no submission at all) holds approve and Return to Firm (correction: InvoiceCorrectionServiceTests, K5)")]
    public async Task TheInvoiceHoldAlone_IsHeld()
    {
        var w = new ApprovalWorld();
        w.HoldInvoiceForFirm();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);

        (await w.ApproveAs(AliceUser)).Code.Should().Be(InvoiceApprovalCodes.FirmMissing);
        var returned = await w.ReturnAs(AliceUser);
        returned.Code.Should().Be(InvoiceRejectReturnCodes.FirmMissing);
        returned.Detail.Should().Be(InvoiceRejectReturnCodes.FirmHeldDetail);
        AssertNothingWritten(w);
    }

    [Fact(DisplayName = "095c-4 review K1: no firm - the sentence says there is no firm; held with a firm shown - it says the invoice is waiting for one")]
    public async Task TheSentence_FollowsTheNeed()
    {
        var noFirm = new ApprovalWorld();
        noFirm.WithoutFirm();
        noFirm.HoldInvoiceForFirm();
        (await noFirm.ApproveAs(AliceUser)).Detail.Should().Be(InvoiceApprovalCodes.FirmMissingDetail);
        (await noFirm.ReturnAs(AliceUser)).Detail.Should().Be(InvoiceRejectReturnCodes.FirmMissingDetail);

        InvoiceApprovalCodes.FirmDetailFor(InvoiceFirmNeed.Held).Should().Be(InvoiceApprovalCodes.FirmHeldDetail).And.NotContain("has no firm");
        InvoiceRejectReturnCodes.FirmDetailFor(InvoiceFirmNeed.Held).Should().NotContain("has no firm");
        CorrectionCodes.FirmDetailFor(InvoiceFirmNeed.Held).Should().Be(CorrectionCodes.FirmHeldDetail).And.NotContain("has no firm");
        CorrectionCodes.FirmDetailFor(InvoiceFirmNeed.NoFirm).Should().Be(CorrectionCodes.FirmMissingDetail);
    }

    [Fact(DisplayName = "095c-5 fail closed: the hold column does not exist in this environment (script not run) - approve is unavailable, never approved")]
    public async Task TheColumnIsMissing_ApproveIsUnavailable_NothingWritten()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        w.MissingColumns.Add(InvoiceFirmHold.InvoiceHoldColumn);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().NotBe(ApprovalOutcome.Approved, "an unreadable hold is held, not 'not held'");
        result.Outcome.Should().Be(ApprovalOutcome.Unavailable);
        AssertNothingWritten(w);
        (await w.ReturnAs(AliceUser)).Outcome.Should().NotBe(InvoiceDecisionOutcome.Decided);
    }

    [Fact(DisplayName = "095c-5 must not fire: a firm invoice whose hold column is present and empty, with no submission hold, approves")]
    public async Task FirmInvoice_HoldColumnEmpty_Approves()
    {
        var w = new ApprovalWorld();
        w.Rows[("sprk_invoice", w.InvoiceId)][InvoiceFirmHold.InvoiceHoldColumn] = null;
        w.AddLine(InvoiceLineStatus.Accepted, approved: 100m);

        (await w.GetAs(AliceUser))!.Should().Match<InvoiceApprovalStateDto>(s => s.CanApprove && !s.FirmNeeded);
        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact(DisplayName = "095c-5 uac-r2 (binding): a read on the caller's token returns the hold as null, the app-only read returns it set - InvoiceFirmHold.CheckAsync answers Held (the route half is TheInvoiceHoldAlone_IsHeld)")]
    public async Task TheCallersRowSaysNoHold_TheAppOnlyReadSaysHeld_IsHeld()
    {
        var invoiceId = Guid.NewGuid();
        // What an OBO read returns for a user outside the Readers profile: the firm, and the field-secured hold as null.
        var callersRow = new Entity("sprk_invoice", invoiceId)
        {
            ["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.NewGuid()),
            [InvoiceFirmHold.InvoiceHoldColumn] = null,
        };
        var appOnlyRow = new Entity("sprk_invoice", invoiceId) { [InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceHoldValue };
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(
                q.EntityName == "sprk_invoice" ? [appOnlyRow] : []));

        InvoiceFirmHold.IsInvoiceHeld(callersRow).Should().BeFalse("the caller's row, read on the user's token, shows no hold");
        (await InvoiceFirmHold.CheckAsync(entities.Object, callersRow, CancellationToken.None)).Should().Be(InvoiceFirmNeed.Held,
            "the hold is re-read app-only, never taken from the row a caller hands in");
    }

    [Fact(DisplayName = "095c-5: the pure invoice reader - a reason code or any other non-blank value holds; null, empty and absent do not")]
    public void IsInvoiceHeld_ReadsTheColumn()
    {
        Entity Row(object? value) => new("sprk_invoice", Guid.NewGuid()) { [InvoiceFirmHold.InvoiceHoldColumn] = value };

        InvoiceFirmHold.IsInvoiceHeld(Row(InvoiceFirmHold.InvoiceHoldValue)).Should().BeTrue();
        InvoiceFirmHold.IsInvoiceHeld(Row("anything else")).Should().BeTrue("an unrecognised value still holds (fail closed)");
        InvoiceFirmHold.IsInvoiceHeld(Row(true)).Should().BeTrue("a value of another shape holds (fail closed)");
        InvoiceFirmHold.IsInvoiceHeld(Row(null)).Should().BeFalse();
        InvoiceFirmHold.IsInvoiceHeld(Row("  ")).Should().BeFalse();
        InvoiceFirmHold.IsInvoiceHeld(new Entity("sprk_invoice", Guid.NewGuid())).Should().BeFalse();
    }

    [Theory(DisplayName = "095c-4 review K3: findings with a duplicated member hold (fail closed), whatever the parser throws when the member is read")]
    [InlineData("""{"pipelineFindings":[],"pipelineFindings":[]}""")]
    [InlineData("""{"lineFindings":[{"code":"rules.not_checked","code":"rules.not_checked"}]}""")]
    [InlineData("""{"pipelineFindings":[{"reasonCode":"a","reasonCode":"b"}]}""")]
    public void DuplicateKeys_Hold(string json)
    {
        var row = new Entity("sprk_invoicesubmission", Guid.NewGuid()) { ["sprk_findingsjson"] = json };

        InvoiceFirmHold.IsHeld(row).Should().BeTrue("what the stored findings hold cannot be known");
    }
}
