using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Xunit;
using static Sprk.Bff.Api.Tests.Services.Finance.ApprovalWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 165b-2 T1 (EB-D-200 item 3): approval decides only from server-written sources, so an invoice whose stored totals differ from what
/// <see cref="InvoiceTotalsCalculator"/> derives from its lines is refused: 409 <c>ebilling.approve.totals_mismatch</c> before any write, one log line
/// with the reason code, the table and the row id. Completion runs the same check. Over the in-memory Dataverse (<see cref="ApprovalWorld"/>), whose
/// invoices carry the totals the calculator derives from their lines (<see cref="ApprovalWorld.StoreTotalsFromLines"/>), with the real services.
/// </summary>
/// <remarks>Seed (notes/165b-integrity-checks.md section 7): with the check removed from <c>CheckApprovableAsync</c>, every must-fire case fails and the
/// controls pass.</remarks>
public sealed class InvoiceApprovalTotalsTests
{
    private const string Refusal =
        "This invoice's stored totals do not match its lines, so it cannot be approved. Nothing was changed. An administrator must review the invoice.";

    /// <summary>An invoice with a decided line, a Reduced line, a Clean line (approve accepts it in bulk, a write) and a 10% adjustment line, its stored
    /// totals derived from those lines.</summary>
    private static ApprovalWorld World()
    {
        var w = new ApprovalWorld();
        w.AddLine(InvoiceLineStatus.Accepted, 400m, approved: 400m);
        w.AddLine(InvoiceLineStatus.Reduced, 300m, approved: 250m);
        w.AddLine(InvoiceLineStatus.Clean, 200m, InvoiceLineCostType.Expense);
        var adjustment = w.AddLine(InvoiceLineStatus.Accepted, 0m, InvoiceLineCostType.Adjustment); // as the C2 control builds it
        adjustment["sprk_adjustmentmethod"] = new OptionSetValue((int)InvoiceAdjustmentMethod.Percent);
        adjustment["sprk_adjustmentvalue"] = 10m;
        adjustment["statecode"] = new OptionSetValue(0);
        w.StoreTotalsFromLines();

        // The setup: billed 1,000; fees 700, expenses 200; the Reduced line takes 50; 10% of the 650 fee base is 65; approved 885 (D-42).
        w.Invoice.GetAttributeValue<Money>("sprk_approvedtotal").Value.Should().Be(885m);
        return w;
    }

    private static void AssertRefusedNothingWritten(ApprovalWorld w, ApprovalResult result, string code, string detail, string logCode)
    {
        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(code);
        result.Detail.Should().Be(detail);
        w.Writes.Should().Be(0, "the refusal comes before any write, the Clean bulk accept included");
        w.Transitions.Should().Be(0);
        w.ApproveRecords.Should().BeEmpty();
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
        w.Portion.GetAttributeValue<OptionSetValue>("sprk_approvalstatus").Value.Should().Be(InvoiceApprovalService.PortionPending);
        w.Log.Entries.Should().ContainSingle(e => e.Message.StartsWith("Approval refusal", StringComparison.Ordinal))
            .Which.Should().Be((LogLevel.Warning, $"Approval refusal {logCode} on sprk_invoice {w.InvoiceId}"));
    }

    // ── Must fire ──

    [Theory(DisplayName = "165b-2 T1 must fire: a stored total that differs from the line-derived total by one cent is refused (409 totals_mismatch), nothing written")]
    [InlineData("sprk_feestotal")]
    [InlineData("sprk_expensestotal")]
    [InlineData("sprk_lineadjustmentstotal")]
    [InlineData("sprk_invoiceadjustmentstotal")]
    [InlineData("sprk_approvedtotal")]
    public async Task T1_AStoredTotalThatDiffers_IsRefused(string column)
    {
        var w = World();
        w.Invoice[column] = new Money(w.Invoice.GetAttributeValue<Money>(column).Value + 0.01m);

        AssertRefusedNothingWritten(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.TotalsMismatch, Refusal, InvoiceApprovalCodes.TotalsMismatch);
    }

    [Fact(DisplayName = "165b-2 T1 must fire: a line changed after the totals were stored (its amount edited, no recalculation) is refused, nothing written")]
    public async Task T1_ALineChangedWithoutRecalculation_IsRefused()
    {
        var w = World();
        var line = w.AddLine(InvoiceLineStatus.Accepted, 100m, approved: 100m);
        line["sprk_amount"] = new Money(150m);

        AssertRefusedNothingWritten(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.TotalsMismatch, Refusal, InvoiceApprovalCodes.TotalsMismatch);
    }

    [Fact(DisplayName = "165b-2 T1 must fire: a stored total that is missing does not match, and is refused")]
    public async Task T1_AMissingStoredTotal_IsRefused()
    {
        var w = World();
        w.Invoice.Attributes.Remove("sprk_feestotal");

        AssertRefusedNothingWritten(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.TotalsMismatch, Refusal, InvoiceApprovalCodes.TotalsMismatch);
    }

    [Fact(DisplayName = "165b-2 T1 must fire: lines the calculator refuses (a line with no cost type) are refused before any write, with the answer approve gave for them")]
    public async Task T1_LinesTheCalculatorRefuses_AreRefusedBeforeAnyWrite()
    {
        var w = World();
        var line = w.AddLine(InvoiceLineStatus.Clean, 100m);
        line.Attributes.Remove("sprk_costtype");

        AssertRefusedNothingWritten(w, await w.ApproveAs(AliceUser), InvoiceApprovalCodes.TotalsRefused,
            InvoiceTotalsRefusalCodes.LineCostTypeUnknown, InvoiceTotalsRefusalCodes.LineCostTypeUnknown);
    }

    [Fact(DisplayName = "165b-2 T1 must fire at completion: an interrupted approval whose stored totals no longer match is refused (409 completion_refused, reason totals_mismatch), no approve record")]
    public async Task T1_Completion_AStoredTotalThatDiffers_IsRefused()
    {
        var w = World();
        w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);
        w.Portion["sprk_decidedby"] = new EntityReference("contact", Alice);
        w.Portion["sprk_decidedon"] = Now.UtcDateTime;
        w.Portion["sprk_approvedamount"] = new Money(885m);
        w.Invoice["sprk_expensestotal"] = new Money(199.99m);

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Conflict, result.Detail);
        result.Code.Should().Be(InvoiceApprovalCodes.CompletionRefused);
        w.ApproveRecords.Should().BeEmpty();
        w.Writes.Should().Be(0);
        w.Log.Entries.Should().ContainSingle(e => e.Message.StartsWith("Approval completion refusal", StringComparison.Ordinal))
            .Which.Should().Be((LogLevel.Warning, $"Approval completion refusal {InvoiceApprovalCodes.TotalsMismatch} on sprk_invoice {w.InvoiceId}"));
    }

    // ── Must not fire ──

    [Fact(DisplayName = "165b-2 T1 control: stored totals equal to the line-derived totals approve, and the frozen amount is the calculator's")]
    public async Task T1_Control_MatchingTotals_Approve()
    {
        var w = World();

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, result.Detail);
        w.InvoiceStatus.Should().Be(InvoiceStatusCodes.Approved);
        w.ApproveRecords.Should().ContainSingle();
        w.Portion.GetAttributeValue<Money>("sprk_approvedamount").Value.Should().Be(885m);
        w.Log.Entries.Should().NotContain(e => e.Message.Contains(InvoiceApprovalCodes.TotalsMismatch, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "165b-2 T1 control: a stored value with more decimal places but the same money amount (885.0000) matches")]
    public async Task T1_Control_TheSameAmountAtAnotherScale_Matches()
    {
        var w = World();
        w.Invoice["sprk_approvedtotal"] = new Money(885.0000m);

        (await w.ApproveAs(AliceUser)).Outcome.Should().Be(ApprovalOutcome.Approved);
    }

    [Fact(DisplayName = "165b-2 T1 control at completion: an interrupted approval with matching totals completes with one record")]
    public async Task T1_Control_Completion_MatchingTotals_Complete()
    {
        var w = World();
        w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);
        w.Portion["sprk_decidedby"] = new EntityReference("contact", Alice);
        w.Portion["sprk_decidedon"] = Now.UtcDateTime;
        w.Portion["sprk_approvedamount"] = new Money(885m);

        // The Clean line is accepted in bulk before the claim, so a completion never sees one.
        foreach (var line in w.Rows.Values.Where(r => r.LogicalName == "sprk_billingevent"
                     && r.GetAttributeValue<OptionSetValue>("sprk_linestatus")?.Value == (int)InvoiceLineStatus.Clean))
        {
            line["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        }

        var result = await w.ApproveAs(AliceUser);

        result.Outcome.Should().Be(ApprovalOutcome.Approved, $"{result.Code} {result.Detail}");
        w.ApproveRecords.Should().ContainSingle();
    }
}
