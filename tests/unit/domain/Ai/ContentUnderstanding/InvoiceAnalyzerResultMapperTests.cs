using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.ContentUnderstanding;

/// <summary>
/// ebilling-r1 task 043: source parsing (<c>D(page,…)</c> → page and a box normalised 0-1), tolerance of missing and
/// mistyped values, and the merge of several page ranges. The recorded-result mapping is in the seam tests
/// (<c>tests/integration/seam/ContentUnderstanding/InvoiceReadSeamTests.cs</c>).
/// </summary>
public sealed class InvoiceAnalyzerResultMapperTests
{
    private static readonly InvoiceAnalyzerResultMapper.RangeContent Letter = Range(
        """{"pages":[{"pageNumber":1,"width":8.5,"height":11},{"pageNumber":2,"width":8.5,"height":11}],"fields":{}}""");

    [Fact]
    public void ParseSource_Polygon_IsItsBoundingRectangleOverThePageSize()
    {
        var (page, box) = InvoiceAnalyzerResultMapper.ParseSource("D(2,0.85,1.1,1.7,1.1,1.7,2.2,0.85,2.2)", Letter);

        page.Should().Be(2);
        box.Should().Be(new ExtractedBoundingBox(2, 0.1m, 0.1m, 0.1m, 0.1m));
    }

    [Fact]
    public void ParseSource_Rectangle_IsXYWidthHeight()
    {
        InvoiceAnalyzerResultMapper.ParseSource("D(1,0.85,1.1,0.85,1.1)", Letter).Box
            .Should().Be(new ExtractedBoundingBox(1, 0.1m, 0.1m, 0.1m, 0.1m));
    }

    [Fact]
    public void ParseSource_RegionsOnTwoPages_KeepsTheFirstPageOnly()
    {
        var (page, box) = InvoiceAnalyzerResultMapper.ParseSource(
            "D(1,0.85,1.1,1.7,1.1,1.7,2.2,0.85,2.2);D(2,0,0,8.5,0,8.5,11,0,11)", Letter);

        page.Should().Be(1);
        box.Should().Be(new ExtractedBoundingBox(1, 0.1m, 0.1m, 0.1m, 0.1m));
    }

    [Fact]
    public void ParseSource_CoordinatesOffThePage_AreClamped()
    {
        InvoiceAnalyzerResultMapper.ParseSource("D(1,-1,-1,9,-1,9,12,-1,12)", Letter).Box
            .Should().Be(new ExtractedBoundingBox(1, 0m, 0m, 1m, 1m));
    }

    [Fact]
    public void ParseSource_PageWithoutASize_KeepsThePageAndHasNoBox()
    {
        var (page, box) = InvoiceAnalyzerResultMapper.ParseSource("D(7,1,1,2,1,2,2,1,2)", Letter);

        page.Should().Be(7);
        box.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("D(1,a,b,c,d)")]
    [InlineData("D(0,1,1,2,1,2,2,1,2)")]
    [InlineData("D(1,1,1,2)")]
    public void ParseSource_Unusable_IsNoPageAndNoBox(string? source)
    {
        InvoiceAnalyzerResultMapper.ParseSource(source, Letter).Should().Be(((int?)null, (ExtractedBoundingBox?)null));
    }

    [Fact]
    public void Map_MistypedAndMissingValues_AreNullNotErrors()
    {
        var invoice = Map(
            """
            {"fields":{
              "FirmInvoiceNumber":{"type":"string","valueNumber":5,"confidence":0.4},
              "InvoiceDate":{"type":"date","valueDate":"not a date","confidence":0.5},
              "TotalDue":{"type":"number","valueString":"12.00","confidence":7},
              "TimeEntries":{"type":"array","valueArray":[{"type":"string"},{"type":"object","valueObject":{}}]}
            }}
            """);

        invoice.Header.FirmInvoiceNumber.Value.Should().BeNull();
        invoice.Header.FirmInvoiceNumber.Confidence.Should().Be(0.4m);
        invoice.Header.InvoiceDate.Value.Should().BeNull();
        invoice.Header.TotalDue.Value.Should().BeNull();
        invoice.Header.TotalDue.Confidence.Should().BeNull("a confidence outside 0-1 is not a confidence");
        invoice.Header.FirmName.Value.Should().BeNull("an absent field is an empty field");
        invoice.Lines.Should().ContainSingle("a non-object array item is skipped; an empty row is kept")
            .Which.RowBoundingBox.Should().BeNull();
    }

    [Fact]
    public void Map_IntegerAndDateTimeValues_AreRead()
    {
        var invoice = Map(
            """
            {"fields":{
              "FeesTotal":{"type":"integer","valueInteger":1200,"confidence":0.9},
              "InvoiceDate":{"type":"date","valueDate":"2025-08-01T00:00:00Z","confidence":0.9}
            }}
            """);

        invoice.Header.FeesTotal.Value.Should().Be(1200m);
        invoice.Header.InvoiceDate.Value.Should().Be(new DateOnly(2025, 8, 1));
    }

    [Fact]
    public void Map_SameConfidenceInTwoRanges_KeepsTheEarlierRangesValue()
    {
        var invoice = Map(
            """{"fields":{"Currency":{"type":"string","valueString":"USD","confidence":0.9}}}""",
            """{"fields":{"Currency":{"type":"string","valueString":"EUR","confidence":0.9}}}""");

        invoice.Header.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Map_ValueInALaterRangeOnly_IsFound()
    {
        var invoice = Map(
            """{"fields":{"TotalDue":{"type":"number","confidence":0.99}}}""",
            """{"fields":{"TotalDue":{"type":"number","valueNumber":10.5,"confidence":0.6}}}""");

        invoice.Header.TotalDue.Value.Should().Be(10.5m, "an empty value never beats a found one, whatever its confidence");
    }

    [Fact]
    public void Map_RowBox_IsTheUnionOfItsValuesOnTheFirstLocatedPage()
    {
        var invoice = Map(
            """
            {"pages":[{"pageNumber":1,"width":10,"height":10},{"pageNumber":2,"width":10,"height":10}],"fields":{
              "TimeEntries":{"type":"array","valueArray":[{"type":"object","valueObject":{
                "Date":{"type":"date","valueDate":"2025-07-01","confidence":0.9,"source":"D(1,1,5,2,5,2,6,1,6)"},
                "Amount":{"type":"number","valueNumber":10,"confidence":0.9,"source":"D(1,8,5,9,5,9,5.5,8,5.5)"},
                "Narrative":{"type":"string","valueString":"x","confidence":0.9,"source":"D(2,1,1,2,1,2,2,1,2)"}
              }}]}
            }}
            """);

        invoice.Lines[0].RowBoundingBox.Should().Be(new ExtractedBoundingBox(1, 0.1m, 0.5m, 0.8m, 0.1m));
    }

    private static InvoiceAnalyzerResultMapper.RangeContent Range(string json) =>
        new(JsonDocument.Parse(json).RootElement.Clone());

    private static ExtractedInvoice Map(params string[] contents) =>
        InvoiceAnalyzerResultMapper.ToInvoice(new ContentUnderstandingAnalysis(
            contents.Select(c => JsonDocument.Parse(c).RootElement.Clone()).ToList(), 1, "a", "v"));
}
