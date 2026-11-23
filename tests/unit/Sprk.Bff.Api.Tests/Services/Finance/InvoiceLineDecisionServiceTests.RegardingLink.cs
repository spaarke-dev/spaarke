using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

// ebilling-r1 task 164a fix round (uac-r2 condition): every reduce and disallow Decision Record carries its regarding invoice-line link (the
// external visibility filter reads it). The add-line record is pinned in InvoiceLineAddServiceTests (TheAddLineRecord_AlwaysCarriesTheRegardingLineLink).
public sealed partial class InvoiceLineDecisionServiceTests
{
    [Theory(DisplayName = "uac-r2: every reduce and disallow Decision Record carries the regarding invoice-line link (the decided line)")]
    [InlineData(LineDecisionAction.Reduce, 450)]
    [InlineData(LineDecisionAction.Disallow, null)]
    public async Task AReduceOrDisallowRecord_AlwaysCarriesTheRegardingLineLink(LineDecisionAction action, int? approved)
    {
        var w = new World(billedTotal: 1000m);
        var line = w.AddLine(600m, InvoiceLineStatus.Flagged);
        w.AddLine(400m, InvoiceLineStatus.Clean);

        var result = await w.Service().DecideAsync(
            Command(w, action, [new(line.Id, approved is { } a ? a : null)], Reason), CancellationToken.None);

        result.Status.Should().Be(LineDecisionStatus.Applied);
        var record = w.H.Created.Should().ContainSingle().Subject;
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(action == LineDecisionAction.Reduce ? "reduce-line" : "disallow-line");
        var regarding = record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline");
        regarding.Should().NotBeNull("the link the external visibility filter reads");
        regarding.LogicalName.Should().Be("sprk_billingevent");
        regarding.Id.Should().Be(line.Id);
    }
}
