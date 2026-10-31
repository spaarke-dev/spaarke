using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 015 (D-41): the finance summary's recent invoices read the invoice status from <c>statuscode</c>, the column
/// that holds it now; the retired status choice column is deleted, so a read of it fails in Dataverse.
/// </summary>
public class FinanceSummaryRecentInvoicesTests
{
    [Fact]
    public void TheRecentInvoicesQuery_ReadsStatuscode()
    {
        var query = FinanceSummaryService.BuildRecentInvoicesQuery(Guid.NewGuid());

        query.EntityName.Should().Be("sprk_invoice");
        query.ColumnSet.Columns.Should().BeEquivalentTo(
            "sprk_invoicenumber", "sprk_invoicedate", "sprk_totalamount", "sprk_vendororg", "statuscode");
        query.TopCount.Should().Be(5);
    }

    [Fact]
    public void ARecentInvoice_CarriesItsStatuscodeValue()
    {
        var row = new Entity("sprk_invoice", Guid.NewGuid())
        {
            ["sprk_invoicenumber"] = "INV-9",
            ["sprk_totalamount"] = new Money(42m),
            ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Paid),
        };

        var dto = FinanceSummaryService.ToRecentInvoice(row);

        dto.Status.Should().Be(InvoiceStatusCodes.Paid);
        dto.InvoiceNumber.Should().Be("INV-9");
        dto.TotalAmount.Should().Be(42m);
    }
}
