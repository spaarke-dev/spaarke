using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 164a-2 (EB-D-193; fix round F1): the reviewer-added marker, <c>provenance.source = "reviewer"</c> inside the line's existing
/// <c>sprk_extractionconfidence</c>, AND the row's id derived from the marker's decision id. The column is not field-secured, so the marker alone
/// never decides: a copy on an intake line reads false. Written by Add line (a later task), read by the totals, the AP export and the review.
/// </summary>
public sealed class ExtractionProvenanceReviewerAddedTests
{
    private static readonly Guid DecisionId = Guid.Parse("a164a200-0000-4000-8000-0000000000d1");

    private static Entity Row(Guid id, string? extraction)
    {
        var row = new Entity("sprk_billingevent", id);
        if (extraction is not null) row["sprk_extractionconfidence"] = extraction;
        return row;
    }

    [Fact(DisplayName = "164a-2: the writer's JSON carries the reviewer source and the decision id")]
    public void TheWrittenMarker_CarriesTheSourceAndTheDecisionId()
    {
        using var doc = JsonDocument.Parse(ExtractionProvenance.ForReviewerAddedLine(DecisionId));
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

    [Fact(DisplayName = "164a-2 F1 must-not-fire: a row whose id is AddedLineIdFor(decisionId) and whose marker names that decision reads reviewer-added")]
    public void AnAddedRow_ReadsTrue()
    {
        var row = Row(ExtractionProvenance.AddedLineIdFor(DecisionId), ExtractionProvenance.ForReviewerAddedLine(DecisionId));

        ExtractionProvenance.IsReviewerAdded(row).Should().BeTrue();
    }

    [Fact(DisplayName = "164a-2 F1: the added line id is derived from the decision id alone: stable, distinct per decision, and apart from the intake line ids")]
    public void TheAddedLineId_IsDeterministic_AndApartFromIntakeIds()
    {
        ExtractionProvenance.AddedLineIdFor(DecisionId).Should().Be(ExtractionProvenance.AddedLineIdFor(DecisionId));
        ExtractionProvenance.AddedLineIdFor(DecisionId).Should().NotBe(ExtractionProvenance.AddedLineIdFor(Guid.Parse("a164a200-0000-4000-8000-0000000000d2")));
        ExtractionProvenance.AddedLineIdFor(DecisionId).Should().NotBe(InvoiceSubmissionPersister.LineIdFor(DecisionId, 0));
    }

    [Fact(DisplayName = "164a-2 F1 must-fire: an INTAKE line (any other id) carrying a valid-looking reviewer marker reads false, whatever decision id it names")]
    public void AnIntakeRow_WithACopiedMarker_ReadsFalse()
    {
        var intakeId = InvoiceSubmissionPersister.LineIdFor(Guid.NewGuid(), 0);
        ExtractionProvenance.IsReviewerAdded(Row(intakeId, ExtractionProvenance.ForReviewerAddedLine(DecisionId))).Should().BeFalse();
        ExtractionProvenance.IsReviewerAdded(Row(intakeId, ExtractionProvenance.ForReviewerAddedLine(intakeId))).Should().BeFalse("its own id as the decision id is still not the derived id");
        ExtractionProvenance.IsReviewerAdded(Row(Guid.NewGuid(), ExtractionProvenance.ForReviewerAddedLine(DecisionId))).Should().BeFalse();
    }

    [Fact(DisplayName = "164a-2 F1 must-fire: a genuine added row whose marker names another decision reads false (the decision id does not match the id)")]
    public void AnAddedRow_WhoseMarkerNamesAnotherDecision_ReadsFalse()
    {
        var row = Row(ExtractionProvenance.AddedLineIdFor(DecisionId), ExtractionProvenance.ForReviewerAddedLine(Guid.Parse("a164a200-0000-4000-8000-0000000000d2")));

        ExtractionProvenance.IsReviewerAdded(row).Should().BeFalse();
    }

    [Theory(DisplayName = "164a-2: a column that does not prove an added row reads false (absent, another source, wrong type or case, no or bad decision id, damaged, empty)")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"fields":{"Hours":{"confidence":0.9}}}""")]
    [InlineData("""{"provenance":{"analyzerId":"sprk_invoice_v1","apiVersion":"2026-06-01-preview"}}""")]
    [InlineData("""{"provenance":{"source":"extraction","decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""")]
    [InlineData("""{"provenance":{"source":"Reviewer","decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""")]
    [InlineData("""{"provenance":{"source":" reviewer","decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""")]
    [InlineData("""{"provenance":{"source":1,"decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""")]
    [InlineData("""{"provenance":{"source":null}}""")]
    [InlineData("""{"provenance":"reviewer"}""")]
    [InlineData("""{"source":"reviewer","decisionId":"a164a200-0000-4000-8000-0000000000d1"}""")]
    [InlineData("""{"provenance":{"source":"reviewer"}}""")]
    [InlineData("""{"provenance":{"source":"reviewer","decisionId":"not-a-guid"}}""")]
    [InlineData("""{"provenance":{"source":"reviewer","decisionId":"00000000-0000-0000-0000-000000000000"}}""")]
    [InlineData("""{"provenance":{"source":"reviewer","decisionId":5}}""")]
    [InlineData("""{"provenance":{"decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""")]
    public void AnythingElse_IsNotReviewerAdded(string? json)
    {
        // Even on the id that WOULD match the decision in the column, the rest of the marker must hold.
        ExtractionProvenance.IsReviewerAdded(Row(ExtractionProvenance.AddedLineIdFor(DecisionId), json)).Should().BeFalse();
    }

    [Fact(DisplayName = "164a-2: the marker is found beside the intake members (source, decision id and id are what decide)")]
    public void TheMarker_IsEnoughBesideOtherMembers()
    {
        var json = $$$"""{"fields":{},"provenance":{"source":"reviewer","analyzerId":"x","decisionId":"{{{DecisionId:D}}}"}}""";

        ExtractionProvenance.IsReviewerAdded(Row(ExtractionProvenance.AddedLineIdFor(DecisionId), json)).Should().BeTrue();
        ExtractionProvenance.IsReviewerAdded(Row(Guid.NewGuid(), json)).Should().BeFalse();
    }
}
