using System.Globalization;
using System.Text;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;

/// <summary>
/// Live check of the Content Understanding read path against a real resource (ebilling-r1 task 043). Runs only when
/// <c>CONTENT_UNDERSTANDING_ENDPOINT</c> is set (for example
/// <c>https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com/</c>); otherwise every test reports Skipped,
/// never a vacuous Passed. Authenticates with <see cref="DefaultAzureCredential"/> (no key); the test assembly narrows that
/// chain to EnvironmentCredential (<c>TestOutboundNetworkGuard</c>), so run locally with
/// <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> after <c>az login</c>. <c>CONTENT_UNDERSTANDING_API_VERSION</c> and <c>CONTENT_UNDERSTANDING_ANALYZER_ID</c> override the D-81 preview
/// default pair. Uses a generated blank one-page PDF, so no document content leaves the machine.
/// </summary>
[Trait("Category", "LiveIntegration")]
public sealed class ContentUnderstandingLiveTests
{
    private static readonly string? EndpointValue = Environment.GetEnvironmentVariable("CONTENT_UNDERSTANDING_ENDPOINT");

    private static ContentUnderstandingClient LiveClient()
    {
        var options = new ContentUnderstandingOptions
        {
            Endpoint = EndpointValue,
            ApiVersion = Environment.GetEnvironmentVariable("CONTENT_UNDERSTANDING_API_VERSION") ?? ContentUnderstandingOptions.PreviewApiVersion,
            AnalyzerId = Environment.GetEnvironmentVariable("CONTENT_UNDERSTANDING_ANALYZER_ID") ?? ContentUnderstandingOptions.PreviewAnalyzerId,
            PollIntervalMilliseconds = 1000,
        };
        new ContentUnderstandingOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
        return new ContentUnderstandingClient(
            new LiveHttpClientFactory(), new DefaultAzureCredential(), Options.Create(options), TimeProvider.System,
            NullLogger<ContentUnderstandingClient>.Instance);
    }

    [SkippableFact]
    public async Task HealthCheck_ConfiguredAnalyzer_ExistsOnItsApiVersion()
    {
        Skip.If(string.IsNullOrWhiteSpace(EndpointValue), "CONTENT_UNDERSTANDING_ENDPOINT is not set.");

        var result = await new ContentUnderstandingHealthCheck(LiveClient()).CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("content-understanding", _ => null!, HealthStatus.Unhealthy, null),
        });

        result.Status.Should().Be(HealthStatus.Healthy, result.Description);
    }

    [SkippableFact]
    public async Task ReadInvoiceAsync_BlankPage_IsNoInvoice()
    {
        Skip.If(string.IsNullOrWhiteSpace(EndpointValue), "CONTENT_UNDERSTANDING_ENDPOINT is not set.");

        var sut = new InvoiceAi(
            new Mock<IPlaybookService>(MockBehavior.Strict).Object,
            new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
            new NullActionResolver(NullLogger<NullActionResolver>.Instance),
            new NullActionRunner(NullLogger<NullActionRunner>.Instance),
            LiveClient(),
            NullLogger<InvoiceAi>.Instance);

        var ex = await FluentActions.Awaiting(() => sut.ReadInvoiceAsync(new InvoiceReadRequest
        {
            Content = BlankPdf(),
            DocumentId = Guid.NewGuid(),
        }))
            .Should().ThrowAsync<InvoiceAiException>();

        ex.Which.ReasonCode.Should().Be(
            InvoiceAiException.ReadNoInvoice,
            ex.Which.InnerException?.Message + " / " + ex.Which.InnerException?.InnerException?.Message);
    }

    /// <summary>
    /// Optional: reads the invoice PDF named by <c>CONTENT_UNDERSTANDING_SAMPLE_PDF</c> (for example a synthetic invoice
    /// from the evaluation set) end to end and prints counts only — never values.
    /// </summary>
    [SkippableFact]
    public async Task ReadInvoiceAsync_SampleInvoice_ReturnsHeaderAndLines()
    {
        var path = Environment.GetEnvironmentVariable("CONTENT_UNDERSTANDING_SAMPLE_PDF");
        Skip.If(string.IsNullOrWhiteSpace(EndpointValue) || string.IsNullOrWhiteSpace(path) || !File.Exists(path),
            "CONTENT_UNDERSTANDING_ENDPOINT and CONTENT_UNDERSTANDING_SAMPLE_PDF are not both set.");

        var sut = new InvoiceAi(
            new Mock<IPlaybookService>(MockBehavior.Strict).Object,
            new Mock<IOpenAiClient>(MockBehavior.Strict).Object,
            new NullActionResolver(NullLogger<NullActionResolver>.Instance),
            new NullActionRunner(NullLogger<NullActionRunner>.Instance),
            LiveClient(),
            NullLogger<InvoiceAi>.Instance);

        var invoice = await sut.ReadInvoiceAsync(new InvoiceReadRequest
        {
            Content = await File.ReadAllBytesAsync(path!),
            DocumentId = Guid.NewGuid(),
        });

        invoice.Header.FirmInvoiceNumber.Value.Should().NotBeNullOrWhiteSpace();
        invoice.Header.TotalDue.Value.Should().NotBeNull();
        invoice.Lines.Should().NotBeEmpty();
        invoice.Lines.Should().OnlyContain(l => l.RowBoundingBox == null || (l.RowBoundingBox.X >= 0 && l.RowBoundingBox.X + l.RowBoundingBox.W <= 1.0001m));
        _output.WriteLine(
            $"{invoice.AnalyzerId}@{invoice.ApiVersion}: pages={invoice.PageCount} lines={invoice.Lines.Count} " +
            $"fees={invoice.Lines.Count(l => l.LineType == InvoiceLineType.Fee)} summaryRows={invoice.TimekeeperSummary.Count} " +
            $"clockHours={invoice.Lines.Count(l => l.Hours.Repair == ExtractedValueRepair.ClockHoursConverted)} " +
            $"summaryRates={invoice.Lines.Count(l => l.Rate.Repair == ExtractedValueRepair.RateFromTimekeeperSummary)} " +
            $"computed={invoice.Lines.Count(l => l.Rate.Repair == ExtractedValueRepair.ComputedFromRow || l.Hours.Repair == ExtractedValueRepair.ComputedFromRow || l.Amount.Repair == ExtractedValueRepair.ComputedFromRow)} " +
            $"dateSwaps={invoice.Lines.Count(l => l.Date.Repair == ExtractedValueRepair.DayMonthSwapped)} " +
            $"feeRowsBalanced={invoice.Lines.Count(l => l.LineType == InvoiceLineType.Fee && l.Hours.Value * l.Rate.Value is decimal p && l.Amount.Value is decimal a && Math.Abs(p - a) <= 0.02m)}");
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ContentUnderstandingLiveTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>A valid one-page, empty US Letter PDF with a correct cross-reference table.</summary>
    internal static byte[] BlankPdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromMinutes(5) };
    }
}
