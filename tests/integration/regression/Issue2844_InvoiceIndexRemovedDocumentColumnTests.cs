using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// Issue #2844: the invoice indexing job asked Dataverse for <c>sprk_invoice.sprk_document</c>, a column task 094 removed from the invoice
/// (its files are Stored Files now). Dataverse refuses a query that names a column the table does not have, so every invoice change failed its
/// re-index and invoice search went stale (seen on dev 2026-11-15 18:51Z). The column set no longer names it, and the index's
/// <c>documentId</c> is written as null so a Merge clears a value an older run stored. The general rule (every column the index reads exists
/// in the solution source) is held by <c>Spaarke.ArchTests.InvoiceIndexColumnsGuardTests</c>.
/// </summary>
public class Issue2844_InvoiceIndexRemovedDocumentColumnTests
{
    private static readonly Guid InvoiceId = Guid.Parse("28440000-0000-4000-8000-000000000001");

    [Fact]
    public void InvoiceQuery_AfterTask094_DoesNotAskForTheRemovedDocumentColumn()
    {
        var columns = InvoiceIndexDocument.InvoiceQuery(InvoiceId).ColumnSet.Columns;

        columns.Should().NotContain("sprk_document");
        columns.Should().NotBeEmpty();
    }

    [Fact]
    public void ToSearchDocument_WhenTheRowStillCarriesAStaleDocument_WritesDocumentIdAsNull()
    {
        var invoice = new Entity("sprk_invoice", InvoiceId)
        {
            ["sprk_invoicenumber"] = "INV-2844",
            ["sprk_document"] = new EntityReference("sprk_document", Guid.Parse("28440000-0000-4000-8000-0000000000d1")),
        };

        var model = InvoiceIndexDocument.Read(invoice, []);
        var doc = InvoiceIndexDocument.ToSearchDocument(model, "tenant-2844", ReadOnlyMemory<float>.Empty, DateTimeOffset.UnixEpoch);

        doc.ContainsKey("documentId").Should().BeTrue("a Merge must clear a documentId an older run stored");
        doc["documentId"].Should().BeNull();
        doc["invoiceId"].Should().Be(InvoiceId.ToString("D"));
    }
}
