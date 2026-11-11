using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// ebilling-r1 task 166 fix round (EB-D-200, adversarial review F1): the invoice's billed total and five derived totals are mapping-protected
/// columns (<see cref="RecordTableRule.MappingProtectedColumns"/>). They are field-secured, but the BFF application user is a Writers-profile
/// member, so field security does not stop a field-mapping rule; only typed finance writers write them. Same two seams as the previous-sequence
/// column: a post-mapping default is checked against the protected set alone (must fire if the entries are removed), and the set is per
/// table (must not fire).
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    public static TheoryData<string> InvoiceAmountColumns() => new()
    {
        InvoiceReviewService.InvTotalAmount,
        InvoiceTotalsService.InvoiceFeesTotal,
        InvoiceTotalsService.InvoiceExpensesTotal,
        InvoiceTotalsService.InvoiceLineAdjustmentsTotal,
        InvoiceTotalsService.InvoiceInvoiceAdjustmentsTotal,
        InvoiceTotalsService.InvoiceApprovedTotal,
    };

    [Theory(DisplayName = "166: a post-mapping default aimed at an invoice amount is an argument error (mapping never writes it)")]
    [MemberData(nameof(InvoiceAmountColumns))]
    public async Task PostMappingDefault_AnInvoiceAmount_IsAnArgumentError(string column)
    {
        var act = () => Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            },
                mapping: true) with
            { PostMappingDefaults = new Dictionary<string, object?> { [column] = 1 } },
            AsCaller(), CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain(column).And.Contain("field mapping never writes it");
        _creates.Should().BeEmpty();
    }

    [Theory(DisplayName = "166: only the invoice protects its amount columns from mapping, and its other columns stay mappable")]
    [MemberData(nameof(InvoiceAmountColumns))]
    public void ProtectedFromMapping_TheInvoiceAmounts_AreOnTheInvoiceOnly(string column)
    {
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().Contain(column);
        RecordCreationService.ProtectedFromMapping("sprk_invoice", RecordTableRules.Get("sprk_invoice"), null)
            .Should().NotContain("sprk_description", "an ordinary column stays mappable");
        RecordCreationService.ProtectedFromMapping("sprk_billingevent", RecordTableRules.Get("sprk_billingevent"), null)
            .Should().NotContain(column, "only the invoice carries the amount columns");
    }
}
