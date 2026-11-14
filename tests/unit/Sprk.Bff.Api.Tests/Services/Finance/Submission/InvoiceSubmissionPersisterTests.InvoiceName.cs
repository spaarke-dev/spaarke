using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095e (EB-D-226): the invoice's <c>sprk_name</c> is "{firm record name} - {invoice number}" from the firm on the invoice. The AI
/// issuer name read off the PDF is used only when the submission has no firm (an invoice held for a firm, kept by EB-D-223).
/// </summary>
public partial class InvoiceSubmissionPersisterTests
{
    [Fact(DisplayName = "095e (EB-D-226, seed-proof): with a firm, the invoice, its stored invoiceName and the Signals' subject all use the firm RECORD's name, though the PDF's issuer differs")]
    public async Task InvoiceName_WithAFirm_IsTheFirmRecordsName_NotTheIssuer()
    {
        // A held outcome with one rule finding, moved to the invoice (the plan then has an invoice-level Signal whose subject is the invoice name).
        var harness = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()], Channel = InvoiceSubmissionChannel.Internal };
        harness.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var read = await harness.RunAsync();
        var outcome = read with
        {
            Evaluation = read.Evaluation! with { Findings = read.Evaluation.Findings.Select(f => f with { Subject = BillingSubject.ForInvoice() }).ToList() },
            Decision = read.Decision! with
            {
                Findings = read.Decision.Findings.Select(d => d with { Finding = d.Finding with { Subject = BillingSubject.ForInvoice() } }).ToList(),
            },
        };
        var issuer = outcome.Read!.Header.FirmName.Value;
        issuer.Should().NotBeNullOrWhiteSpace("the setup: the PDF names an issuer");
        issuer.Should().NotBe("Hartwell and Co LLP", "the setup: the issuer differs from the firm record");
        var store = new Store { FirmRecordName = "Hartwell and Co LLP" };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var requests = store.Transactions.Should().ContainSingle().Subject;
        var invoice = Target(requests[0]);
        invoice.GetAttributeValue<string>("sprk_name").Should().Be("Hartwell and Co LLP - INV-100", "the firm record's name and the firm invoice number");
        using var findings = JsonDocument.Parse(Target(requests[^1]).GetAttributeValue<string>("sprk_findingsjson"));
        findings.RootElement.GetProperty("invoiceName").GetString().Should().Be("Hartwell and Co LLP - INV-100", "the stored name agrees with the row");
        var invoiceEntries = findings.RootElement.GetProperty("signalPlan").EnumerateArray()
            .Where(e => e.GetProperty("subjectEntity").GetString() == "sprk_invoice").ToList();
        invoiceEntries.Should().NotBeEmpty("the setup: the plan has an invoice-level Signal, or this check proves nothing");
        invoiceEntries.Should().OnlyContain(e => e.GetProperty("subjectName").GetString() == "Hartwell and Co LLP - INV-100", "the Signal's subject is the firm record's name");
    }

    [Fact(DisplayName = "095e (EB-D-226): with no firm (an invoice held for a firm), the invoice is named from the issuer the PDF names, as before")]
    public async Task InvoiceName_WithNoFirm_IsTheIssuer()
    {
        var outcome = await NoFirmTwoLines().RunAsync();
        var header = outcome.Read!.Header;
        header.FirmName.Value.Should().NotBeNullOrWhiteSpace("the setup: the PDF names an issuer");
        var store = NoFirmStore();

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Committed);
        var invoice = Target(store.Transactions.Should().ContainSingle().Subject[0]);
        invoice.GetAttributeValue<string>("sprk_name").Should().Be(
            InvoiceReviewService.BuildInvoiceName(header.FirmName.Value, "INV-100", header.InvoiceDate.Value?.ToDateTime(TimeOnly.MinValue)));
        invoice.GetAttributeValue<string>("sprk_name").Should().StartWith(header.FirmName.Value!);
    }

    [Theory(DisplayName = "095e (EB-D-226, seed-proof, fail closed): a firm-name read fault, or a firm record with no name, is a Retry before any write; the issuer name is never used")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvoiceName_TheFirmNameCannotBeRead_IsARetry_AndNothingIsWritten(bool fault)
    {
        var outcome = await new ProcessorHarness().RunAsync();
        var store = fault ? new Store { FirmNameReadFault = new TimeoutException() } : new Store { FirmRecordName = null };

        var result = await store.Build().PersistAsync(outcome, null);

        result.Status.Should().Be(SubmissionPersistStatus.Retry);
        result.ReasonCode.Should().Be(InvoiceSubmissionPersister.FirmNameUnreadableCode);
        store.Transactions.Should().BeEmpty("nothing is written");
    }
}
