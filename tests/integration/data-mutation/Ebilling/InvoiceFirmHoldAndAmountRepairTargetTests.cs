using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// Task 067 (owner 2026-11-27): Repair runs app-only and copies the filing parent's mapped values, but it never WRITES an
/// invoice's firm (<c>sprk_vendororg</c>: which firm's contacts see the invoice), its firm hold (<c>sprk_firmhold</c>: hides it
/// from that firm while set) or an amount (<c>sprk_totalamount</c>). Repair's per-rule ask
/// (<see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/>) reports each with the access-column code, and nothing it writes
/// names it. Must fire: each column. Must not fire: an ordinary invoice rule beside it is not refused.
/// </summary>
public class InvoiceFirmHoldAndAmountRepairTargetTests
{
    private const string Invoice = "sprk_invoice";

    [Theory]
    [InlineData("sprk_vendororg")]
    [InlineData("sprk_firmhold")]
    [InlineData("sprk_totalamount")]
    public async Task Repair_ARuleTargetingTheInvoicesFirmHoldOrAmount_IsRefusedWithItsCode_AndWritesNothingOfIt(string column)
    {
        var world = new RepairWorld();
        world.MatterLinksTo();
        world.Map("sprk_matter", Invoice,
            RepairWorld.DefaultText(column, "set-by-a-mapping", 1),
            RepairWorld.DefaultText("sprk_description", "from the matter", 2));
        var id = world.InvoiceUnderTheMatter();

        var outcome = await world.RepairAsync(Invoice, id);

        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Mapping && f.Column == column)
            .Which.ReasonCode.Should().Be(InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode, $"{column} is never a mapping's to write (must fire)");
        outcome.Fields.Where(f => f.Column == "sprk_description")
            .Should().NotContain(f => f.ReasonCode == InheritedAccessPermission.AppOnlyAccessColumnWriteReasonCode, "an ordinary rule is not refused (must not fire)");
        world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain(k => k.Contains(column, StringComparison.OrdinalIgnoreCase));
        world.Writes.SelectMany(w => w.Fields.Values).Select(v => v?.ToString()).Should().NotContain("set-by-a-mapping");
    }
}
