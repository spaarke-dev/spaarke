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
    private static readonly Guid InvoiceId = Guid.Parse("a164a200-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherInvoiceId = Guid.Parse("a164a200-0000-4000-8000-0000000000a2");

    /// <summary>A line row on <paramref name="invoice"/> (default <see cref="InvoiceId"/>; <see cref="Guid.Empty"/>: no invoice lookup).</summary>
    private static Entity Row(Guid id, string? extraction, Guid? invoice = null)
    {
        var row = new Entity("sprk_billingevent", id);
        if (extraction is not null) row["sprk_extractionconfidence"] = extraction;
        if ((invoice ?? InvoiceId) is var on && on != Guid.Empty) row["sprk_invoice"] = new EntityReference("sprk_invoice", on);
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

    [Fact(DisplayName = "164a-2 F1 must-not-fire: a row on its invoice whose id is AddedLineIdFor(invoice, decisionId) and whose marker names that decision reads reviewer-added")]
    public void AnAddedRow_ReadsTrue()
    {
        var row = Row(ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId), ExtractionProvenance.ForReviewerAddedLine(DecisionId));

        ExtractionProvenance.IsReviewerAdded(row).Should().BeTrue();
    }

    [Fact(DisplayName = "164a-2 F1: the added line id is derived from the invoice and the decision id: stable, distinct per decision and per invoice, and apart from the intake line ids")]
    public void TheAddedLineId_IsDeterministic_AndApartFromIntakeIds()
    {
        ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId).Should().Be(ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId));
        ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId).Should().NotBe(ExtractionProvenance.AddedLineIdFor(InvoiceId, Guid.Parse("a164a200-0000-4000-8000-0000000000d2")));
        ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId).Should().NotBe(ExtractionProvenance.AddedLineIdFor(OtherInvoiceId, DecisionId));
        ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId).Should().NotBe(InvoiceSubmissionPersister.LineIdFor(DecisionId, 0));
    }

    [Fact(DisplayName = "164a fix round K2 must-fire: an added row moved to another invoice reads false there (an ordinary line); with no invoice it reads false")]
    public void AnAddedRow_MovedToAnotherInvoice_ReadsFalse()
    {
        var id = ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId);
        var marker = ExtractionProvenance.ForReviewerAddedLine(DecisionId);

        ExtractionProvenance.IsReviewerAdded(Row(id, marker, OtherInvoiceId)).Should().BeFalse("the id derives from the invoice it was added on, not the one it is on");
        ExtractionProvenance.IsReviewerAdded(Row(id, marker, Guid.Empty)).Should().BeFalse("no invoice on the row proves nothing");
        ExtractionProvenance.IsReviewerAdded(Row(id, marker, InvoiceId)).Should().BeTrue("must-not-fire: back on its own invoice");
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
        var row = Row(ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId), ExtractionProvenance.ForReviewerAddedLine(Guid.Parse("a164a200-0000-4000-8000-0000000000d2")));

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
        ExtractionProvenance.IsReviewerAdded(Row(ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId), json)).Should().BeFalse();
    }

    [Fact(DisplayName = "164a-2: the marker is found beside the intake members (source, decision id and id are what decide)")]
    public void TheMarker_IsEnoughBesideOtherMembers()
    {
        var json = $$$"""{"fields":{},"provenance":{"source":"reviewer","analyzerId":"x","decisionId":"{{{DecisionId:D}}}"}}""";

        ExtractionProvenance.IsReviewerAdded(Row(ExtractionProvenance.AddedLineIdFor(InvoiceId, DecisionId), json)).Should().BeTrue();
        ExtractionProvenance.IsReviewerAdded(Row(Guid.NewGuid(), json)).Should().BeFalse();
    }
}
