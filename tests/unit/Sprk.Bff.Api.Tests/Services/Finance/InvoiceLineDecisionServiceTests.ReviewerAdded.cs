using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

// ebilling-r1 task 164a-2 (EB-D-192, EB-D-199, add-line T8): a line decision on a reviewer-added line follows the Add line approved-total rule, through BOTH
// the service's dry run and the stored recalculation, because each builds its InvoiceLineInput from the marker. An intake line is unchanged.
public sealed partial class InvoiceLineDecisionServiceTests
{
    private const string ReviewerMarker = """{"provenance":{"source":"reviewer","decisionId":"a164a200-0000-4000-8000-0000000000d1"}}""";

    [Theory(DisplayName = "164a-2 T8: Accept, Reduce and Disallow on a reviewer-added line give billed + the added contribution; billed, fees and expenses are unchanged")]
    [InlineData(LineDecisionAction.Accept, null, 1200)]
    [InlineData(LineDecisionAction.Reduce, 150, 1150)]
    [InlineData(LineDecisionAction.Disallow, null, 1000)]
    public async Task ADecisionOnAReviewerAddedLine_FollowsTheAddedLineRule(LineDecisionAction action, int? approved, int expectedApproved)
    {
        var w = new World(billedTotal: 1000m);
        w.AddLine(1000m, InvoiceLineStatus.Accepted, approved: 1000m);
        var added = w.AddLine(200m, InvoiceLineStatus.Flagged, extraction: ReviewerMarker);

        var result = await w.Service().DecideAsync(
            Command(w, action, [new(added.Id, approved is null ? null : (decimal)approved)], action == LineDecisionAction.Accept ? null : Reason),
            CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        result.Totals!.Approved.Should().Be(expectedApproved, "the dry run's totals");
        w.ApprovedTotal.Should().Be(expectedApproved, "the stored recalculation reads the same marker");
        var stored = w.TotalsWrites.Should().ContainSingle().Which;
        stored["sprk_feestotal"].Should().Be(1000m, "the added line is not in the fees total");
        stored["sprk_expensestotal"].Should().Be(0m);
        stored["sprk_lineadjustmentstotal"].Should().Be(0m, "an added line never counts as a firm-line adjustment");
    }

    [Fact(DisplayName = "164a-2 must-not-fire: the same Disallow on an intake line (no marker) still subtracts it from the billed total")]
    public async Task TheSameDisallowOnAnIntakeLine_StillSubtractsIt()
    {
        var w = new World(billedTotal: 1200m);
        w.AddLine(1000m, InvoiceLineStatus.Accepted, approved: 1000m);
        var intake = w.AddLine(200m, InvoiceLineStatus.Flagged, extraction: """{"provenance":{"analyzerId":"sprk_invoice_v1","apiVersion":"2026-06-01-preview"}}""");

        var result = await w.Service().DecideAsync(Command(w, LineDecisionAction.Disallow, [new(intake.Id)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        result.Totals!.Approved.Should().Be(1000m);
        w.ApprovedTotal.Should().Be(1000m);
        w.TotalsWrites.Should().ContainSingle().Which["sprk_feestotal"].Should().Be(1200m);
    }

    [Fact(DisplayName = "164a-2: InvoiceAdjustmentService.ToInput (used by adjustments and the correction dry run) carries the marker; a row without it is an intake line")]
    public void TheAdjustmentServicesInputBuilder_CarriesTheMarker()
    {
        var added = new Entity("sprk_billingevent", Guid.NewGuid())
        {
            ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
            ["sprk_amount"] = new Money(200m),
            ["sprk_extractionconfidence"] = ReviewerMarker,
        };
        var intake = new Entity("sprk_billingevent", Guid.NewGuid())
        {
            ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
            ["sprk_amount"] = new Money(200m),
        };

        InvoiceAdjustmentService.ToInput(added).ReviewerAdded.Should().BeTrue();
        InvoiceAdjustmentService.ToInput(intake).ReviewerAdded.Should().BeFalse();
    }
}
