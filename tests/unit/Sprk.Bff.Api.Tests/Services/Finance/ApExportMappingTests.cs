using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>The AP export mapping and its two file writers (ebilling-r1 task 150; FR-41, D-56): the mapping is data, the files are deterministic.</summary>
public sealed class ApExportMappingTests
{
    private static readonly Guid InvoiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ApExportInvoiceValues Invoice(string number, Action<Dictionary<string, object?>>? tweak = null, params Dictionary<string, object?>[] lines)
    {
        var values = new Dictionary<string, object?>
        {
            ["batch.id"] = "EXP-1",
            ["invoice.id"] = InvoiceId,
            ["firm.name"] = "Acme Law LLP",
            ["invoice.firmInvoiceNumber"] = number,
            ["invoice.invoiceDate"] = new DateOnly(2026, 9, 30),
            ["invoice.approvedTotal"] = 1234.5m,
            ["invoice.currency"] = "USD",
            ["record.name"] = "Smith v. Jones, \"et al.\"",
        };
        tweak?.Invoke(values);
        return new ApExportInvoiceValues(number, InvoiceId, values, lines.Cast<IReadOnlyDictionary<string, object?>>().ToList());
    }

    [Fact(DisplayName = "150 D-56: a blank mapping is the default - the 25 columns, in the D-56 order, with the five R1-unsourced columns present")]
    public void BlankMapping_IsTheD56Default()
    {
        var parsed = ApExportMappingParser.Parse("  ");

        parsed.IsValid.Should().BeTrue();
        parsed.Model!.Columns.Select(c => c.Name).Should().Equal(
            "BatchId", "SpaarkeInvoiceId", "VendorName", "VendorApId", "VendorTaxId", "InvoiceNumber", "TrackingNumber", "InvoiceDate", "DueDate",
            "BillingPeriodStart", "BillingPeriodEnd", "Currency", "BilledTotal", "LineAdjustmentsTotal", "InvoiceAdjustmentsTotal", "ApprovedTotal",
            "CoreRecordType", "CoreRecordNumber", "CoreRecordName", "LawDepartmentMatterNumber", "FirmClientMatterNumber", "PONumber", "ApprovedBy",
            "ApprovedOn", "DocumentUrl");
        ApExportSources.AlwaysEmptyInvoiceSources.Should().BeEquivalentTo(
            ["firm.apVendorId", "firm.taxId", "record.firmClientMatterNumber", "invoice.poNumber", "invoice.documentUrl"]);
    }

    [Fact(DisplayName = "150 FR-41: the canonical default JSON parses back to the default model (the snapshot of a blank mapping is a real mapping)")]
    public void DefaultJson_RoundTrips()
    {
        var parsed = ApExportMappingParser.Parse(ApExportDefaultMapping.Json);

        parsed.IsValid.Should().BeTrue(parsed.Error);
        parsed.Model!.Columns.Should().Equal(ApExportDefaultMapping.Model.Columns);
        parsed.Model.LineColumns.Should().Equal(ApExportDefaultMapping.Model.LineColumns);
    }

    [Fact(DisplayName = "150 FR-41: a custom mapping changes the CSV header and values with no code change")]
    public void CustomMapping_ChangesTheOutput()
    {
        var mapping = ApExportMappingParser.Parse("""
            {"version":1,"columns":[{"name":"Inv","source":"invoice.firmInvoiceNumber"},{"name":"Amt","source":"invoice.approvedTotal","format":"decimal"},
            {"name":"Day","source":"invoice.invoiceDate","format":"date"}]}
            """).Model!;

        var csv = Encoding.UTF8.GetString(ApExportWriter.WriteCsv(mapping, [Invoice("INV-1")]));

        csv.Should().Be("Inv,Amt,Day\r\nINV-1,1234.50,2026-09-30\r\n");
    }

    [Fact(DisplayName = "150 D-56: a known source with no value (and the five unsourced columns) is written empty, never as the word null")]
    public void MissingValues_AreEmpty()
    {
        var csv = Encoding.UTF8.GetString(ApExportWriter.WriteCsv(ApExportDefaultMapping.Model, [Invoice("INV-1")]));
        var row = csv.Split("\r\n")[1];

        row.Should().StartWith($"EXP-1,{InvoiceId:D},Acme Law LLP,,,INV-1,,2026-09-30,,,,USD,,,,1234.50,,,\"Smith v. Jones, \"\"et al.\"\"\"");
        row.Should().NotContain("null");
    }

    [Fact(DisplayName = "150 RFC 4180: CRLF line ends, no BOM, a comma / quote / line break forces quoting with doubled quotes")]
    public void Csv_FollowsRfc4180()
    {
        var bytes = ApExportWriter.WriteCsv(ApExportDefaultMapping.Model, [Invoice("INV-1", v => v["firm.name"] = "Line\nBreak, Inc.")]);

        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "no byte-order mark");
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().EndWith("\r\n").And.Contain("\"Line\nBreak, Inc.\"");
        text.Split("\r\n", StringSplitOptions.None).Should().HaveCount(3, "header, one row, and the trailing empty after the last CRLF");
    }

    [Theory(DisplayName = "150 security: a text value that starts with a formula character is prefixed so a spreadsheet never runs it")]
    [InlineData("=SUM(A1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@cmd")]
    public void Csv_PrefixesFormulaText(string value)
    {
        var csv = Encoding.UTF8.GetString(ApExportWriter.WriteCsv(ApExportDefaultMapping.Model, [Invoice("INV-1", v => v["firm.name"] = value)]));

        csv.Should().Contain(",'" + value + ",");
    }

    [Fact(DisplayName = "150: a negative AMOUNT is a number, not text - it is not prefixed")]
    public void Csv_DoesNotPrefixNumbers()
    {
        var csv = Encoding.UTF8.GetString(ApExportWriter.WriteCsv(ApExportDefaultMapping.Model, [Invoice("INV-1", v => v["invoice.approvedTotal"] = -25m)]));

        csv.Should().Contain(",-25.00,").And.NotContain("'-25");
    }

    [Fact(DisplayName = "150: rows are in invoice-number order whatever order they were read in, so the same batch writes the same bytes")]
    public void Output_IsDeterministic()
    {
        var a = Invoice("INV-A");
        var b = Invoice("INV-B");

        var one = ApExportWriter.WriteCsv(ApExportDefaultMapping.Model, [b, a]);
        var two = ApExportWriter.WriteCsv(ApExportDefaultMapping.Model, [a, b]);

        one.Should().Equal(two);
        Encoding.UTF8.GetString(one).Split("\r\n")[1].Should().Contain("INV-A");
    }

    [Fact(DisplayName = "150 FR-41: the JSON has the mapped columns plus lines[] per invoice; dates are ISO strings, amounts numbers, absent values null")]
    public void Json_AddsLines()
    {
        var line = new Dictionary<string, object?> { ["line.date"] = new DateOnly(2026, 9, 12), ["line.type"] = "Fee", ["line.hours"] = 3.0m, ["line.billed"] = 900m, ["line.taskCode"] = "L110" };

        using var doc = JsonDocument.Parse(ApExportWriter.WriteJson("EXP-1", ApExportDefaultMapping.Model, [Invoice("INV-1", null, line)]));

        doc.RootElement.GetProperty("batchId").GetString().Should().Be("EXP-1");
        doc.RootElement.GetProperty("invoiceCount").GetInt32().Should().Be(1);
        var invoice = doc.RootElement.GetProperty("invoices")[0];
        invoice.GetProperty("ApprovedTotal").GetDecimal().Should().Be(1234.5m);
        invoice.GetProperty("InvoiceDate").GetString().Should().Be("2026-09-30");
        invoice.GetProperty("VendorApId").ValueKind.Should().Be(JsonValueKind.Null);
        var written = invoice.GetProperty("lines")[0];
        written.GetProperty("date").GetString().Should().Be("2026-09-12");
        written.GetProperty("hours").GetDecimal().Should().Be(3.0m);
        written.GetProperty("rate").ValueKind.Should().Be(JsonValueKind.Null);
        written.GetProperty("taskCode").GetString().Should().Be("L110");
    }

    [Theory(DisplayName = "150: a mapping that cannot be applied is invalid - unknown source, unknown format, duplicate name, wrong version, no columns, not JSON")]
    [InlineData("""{"version":1,"columns":[{"name":"X","source":"invoice.nope"}]}""")]
    [InlineData("""{"version":1,"columns":[{"name":"X","source":"invoice.approvedTotal","format":"roman"}]}""")]
    [InlineData("""{"version":1,"columns":[{"name":"X","source":"invoice.id"},{"name":"X","source":"batch.id"}]}""")]
    [InlineData("""{"version":2,"columns":[{"name":"X","source":"invoice.id"}]}""")]
    [InlineData("""{"version":1,"columns":[]}""")]
    [InlineData("""{"version":1,"columns":[{"name":"X","source":"line.hours"}]}""")]
    [InlineData("not json")]
    public void InvalidMappings_AreRefused(string json)
    {
        var parsed = ApExportMappingParser.Parse(json);

        parsed.IsValid.Should().BeFalse();
        parsed.Error.Should().NotBeNullOrWhiteSpace();
    }
}
