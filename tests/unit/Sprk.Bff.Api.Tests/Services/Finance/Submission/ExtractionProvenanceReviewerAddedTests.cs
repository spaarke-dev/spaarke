using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 164a-2 (EB-D-193): the reviewer-added marker, <c>provenance.source = "reviewer"</c> inside the line's existing
/// <c>sprk_extractionconfidence</c>. Written by Add line (a later task), read by the totals and the review. Numbers, flags and ids only; no new column.
/// </summary>
public sealed class ExtractionProvenanceReviewerAddedTests
{
    private static readonly Guid DecisionId = Guid.Parse("a164a200-0000-4000-8000-0000000000d1");

    [Fact(DisplayName = "164a-2: the writer's JSON carries the reviewer source and the decision id, and the reader says reviewer-added")]
    public void TheWrittenMarker_IsReadBack()
    {
        var json = ExtractionProvenance.ForReviewerAddedLine(DecisionId);

        ExtractionProvenance.IsReviewerAdded(json).Should().BeTrue();
        using var doc = JsonDocument.Parse(json);
        var provenance = doc.RootElement.GetProperty(ExtractionProvenance.Member);
        provenance.GetProperty(ExtractionProvenance.SourceMember).GetString().Should().Be("reviewer");
        provenance.GetProperty(ExtractionProvenance.DecisionIdMember).GetString().Should().Be(DecisionId.ToString("D"));
    }

    [Fact(DisplayName = "164a-2: the marker holds the source and an id only (no text, no name, no value) and the line is not an extracted one")]
    public void TheWrittenMarker_IsFlagsAndIdsOnly_AndHasNoExtractionPair()
    {
        using var doc = JsonDocument.Parse(ExtractionProvenance.ForReviewerAddedLine(DecisionId));

        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(ExtractionProvenance.Member);
        doc.RootElement.GetProperty(ExtractionProvenance.Member).EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(ExtractionProvenance.SourceMember, ExtractionProvenance.DecisionIdMember);
        ExtractionProvenance.ParseLine(ExtractionProvenance.ForReviewerAddedLine(DecisionId))!.HasExtraction.Should().BeFalse(
            "no analyzer pair: an extracted-field correction of an added line is refused (brief K1)");
    }

    [Theory(DisplayName = "164a-2: a column with no reviewer source is not reviewer-added (absent, another source, wrong type or case, damaged, empty)")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"fields":{"Hours":{"confidence":0.9}}}""")]
    [InlineData("""{"provenance":{"analyzerId":"sprk_invoice_v1","apiVersion":"2026-06-01-preview"}}""")]
    [InlineData("""{"provenance":{"source":"extraction"}}""")]
    [InlineData("""{"provenance":{"source":"Reviewer"}}""")]
    [InlineData("""{"provenance":{"source":" reviewer"}}""")]
    [InlineData("""{"provenance":{"source":1}}""")]
    [InlineData("""{"provenance":{"source":null}}""")]
    [InlineData("""{"provenance":"reviewer"}""")]
    [InlineData("""{"source":"reviewer"}""")]
    [InlineData("""{"provenance":{"decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""")]
    public void AnythingElse_IsNotReviewerAdded(string? json)
    {
        ExtractionProvenance.IsReviewerAdded(json).Should().BeFalse();
    }

    [Fact(DisplayName = "164a-2: the marker is found beside the intake members (the source is the only thing that decides)")]
    public void TheSource_IsEnoughBesideOtherMembers()
    {
        ExtractionProvenance.IsReviewerAdded("""{"fields":{},"provenance":{"source":"reviewer","analyzerId":"x"}}""").Should().BeTrue();
    }

    [Fact(DisplayName = "164a-2: the parsed-root overload agrees with the text overload and refuses a non-object root")]
    public void TheParsedRootOverload_Agrees()
    {
        using var yes = JsonDocument.Parse(ExtractionProvenance.ForReviewerAddedLine(DecisionId));
        using var no = JsonDocument.Parse("""{"provenance":{"source":"extraction"}}""");
        using var array = JsonDocument.Parse("[1]");

        ExtractionProvenance.IsReviewerAdded(yes.RootElement).Should().BeTrue();
        ExtractionProvenance.IsReviewerAdded(no.RootElement).Should().BeFalse();
        ExtractionProvenance.IsReviewerAdded(array.RootElement).Should().BeFalse();
    }
}
